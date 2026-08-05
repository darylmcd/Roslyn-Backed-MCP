// Copyright (c) darylmcd. Licensed under the MIT License.
//
// StdoutWriteAnalyzer — preventive build-time invariant gate that flags any direct
// stdout write inside the `RoslynMcp.Host.Stdio` assembly. Stdio MCP servers MUST
// keep stdout reserved for the protocol's NDJSON framing channel — a stray
// `Console.WriteLine`, `Console.Out.Write`, `Trace.WriteLine`, etc. corrupts the
// stream and silently breaks every downstream client.
//
// See ai_docs/plans/20260426T025255Z_backlog-sweep/plan.md initiative #1
// (stdio-host-stdout-audit) for the rationale: the audit found zero current
// violations in `Program.cs` (only `Console.Out.Flush()` calls, which are
// protocol-correct), so this analyzer is a forward-going invariant gate rather
// than a fix-now. Diagnostic id: RMCP010.
//
// ALLOW-LIST (no diagnostic emitted):
//   * `Console.Out.Flush()`        — synchronous flush of the framing channel
//   * `Console.Out.FlushAsync()`   — async flush of the framing channel
//   * Any member access on `Console.Error.*` — stderr is fine for stdio servers
//
// FLAGGED (RMCP010 emitted):
//   * `Console.Write*` / `Console.WriteLine*`
//   * `Console.Out.Write*` / `Console.Out.WriteLine*`
//   * `Trace.Write*` / `Trace.WriteLine*`
//   * Any other `*.Write*` invocation where the receiver is `System.Console.Out`
//     (covers `var stdout = Console.Out; stdout.WriteLine(...)` patterns)
//
// The analyzer is assembly-scoped: it only fires inside the
// `RoslynMcp.Host.Stdio` assembly. Other assemblies (libraries, tests) keep
// stdout for their own use.

using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RoslynMcp.Analyzers.ServerSurfaceCatalog;

/// <summary>
/// Flags direct stdout writes inside the <c>RoslynMcp.Host.Stdio</c> assembly.
/// </summary>
/// <remarks>
/// MCP stdio transports use stdout exclusively for protocol NDJSON framing.
/// Any stray <c>Console.WriteLine</c> / <c>Console.Out.Write</c> / <c>Trace.WriteLine</c>
/// call corrupts the framing and silently breaks every downstream client (cf.
/// IT-Chat-Bot 2026-04-13 §9.4: clients received 0 bytes after stdout pollution).
/// Allow-listed: <c>Console.Out.Flush()</c> / <c>FlushAsync()</c> (protocol-required)
/// and the entire <c>Console.Error.*</c> surface (stderr is fine for stdio servers).
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StdoutWriteAnalyzer : DiagnosticAnalyzer
{
    private const string TargetAssemblyName = "RoslynMcp.Host.Stdio";
    private const string SystemConsoleMetadataName = "System.Console";
    private const string SystemDiagnosticsTraceMetadataName = "System.Diagnostics.Trace";
    private const string SystemIOTextWriterMetadataName = "System.IO.TextWriter";

    private static readonly DiagnosticDescriptor s_stdoutWrite = new(
        id: "RMCP010",
        title: "Direct stdout write in stdio MCP host assembly",
        messageFormat: "Direct stdout write '{0}' is forbidden in {1} — stdout is reserved for MCP NDJSON framing; route diagnostic output through ILogger (which the host configures to write to stderr) or use Console.Error.Write* explicitly",
        category: "McpHostStdio",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "MCP stdio servers must keep stdout reserved for protocol NDJSON framing. " +
            "Any direct write via Console.Write*, Console.Out.Write*, Trace.Write*, or " +
            "an alias to Console.Out (e.g. `var stdout = Console.Out; stdout.WriteLine(...)`) " +
            "corrupts the framing and silently breaks downstream clients. Use ILogger (the " +
            "host wires AddConsole with LogToStandardErrorThreshold=Trace, so all log output " +
            "lands on stderr) or call Console.Error.Write* directly. Console.Out.Flush() and " +
            "FlushAsync() are allow-listed because the protocol framing requires explicit flushes.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(s_stdoutWrite);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationStart =>
        {
            // Assembly-scoped: only enforce inside RoslynMcp.Host.Stdio. Other assemblies
            // (libraries, tests, downstream consumers) keep stdout for their own use.
            // The analyzer DLL ships into Host.Stdio's analyzer set via OutputItemType="Analyzer"
            // in src/RoslynMcp.Host.Stdio/RoslynMcp.Host.Stdio.csproj, so the only consumer
            // today is Host.Stdio — but the assembly-name guard makes the analyzer safe if a
            // future refactor wires it into a sibling assembly via shared analyzer config.
            if (!string.Equals(
                compilationStart.Compilation.AssemblyName,
                TargetAssemblyName,
                StringComparison.Ordinal))
            {
                return;
            }

            var consoleType = compilationStart.Compilation.GetTypeByMetadataName(SystemConsoleMetadataName);
            var traceType = compilationStart.Compilation.GetTypeByMetadataName(SystemDiagnosticsTraceMetadataName);
            var textWriterType = compilationStart.Compilation.GetTypeByMetadataName(SystemIOTextWriterMetadataName);

            // No System.Console / System.Diagnostics.Trace / System.IO.TextWriter resolved
            // means the runtime references are absent — the host can't be writing through
            // these APIs in this compilation, so nothing to enforce.
            if (consoleType is null && traceType is null && textWriterType is null)
            {
                return;
            }

            compilationStart.RegisterSyntaxNodeAction(
                ctx => AnalyzeInvocation(ctx, consoleType, traceType, textWriterType),
                SyntaxKind.InvocationExpression);
        });
    }

    private static void AnalyzeInvocation(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol? consoleType,
        INamedTypeSymbol? traceType,
        INamedTypeSymbol? textWriterType)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var methodSymbol = ResolveInvokedMethod(context, invocation);
        if (methodSymbol?.ContainingType is not { } containingType)
        {
            return;
        }

        var methodName = methodSymbol.Name;

        var staticReceiver = GetForbiddenStaticReceiver(
            containingType,
            consoleType,
            traceType,
            methodName);
        if (staticReceiver is not null)
        {
            ReportInvocation(context, invocation, $"{staticReceiver}.{methodName}");
            return;
        }

        // Static imports have no receiver expression and were handled above. The remaining
        // TextWriter cases require member access (`Console.Out.WriteLine`, `writer.WriteLine`).
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return;
        }

        // Branch 3: TextWriter instance Write* methods. This catches three patterns:
        //   (a) Console.Out.WriteLine("...")           — receiver expression is `Console.Out`
        //   (b) var stdout = Console.Out; stdout.WriteLine("...")
        //                                              — receiver expression is `stdout`,
        //                                                 type-bound symbol is Console.Out
        //   (c) Console.Error.WriteLine("...")         — receiver expression is `Console.Error`
        //                                                 (ALLOW-LISTED — stderr is fine)
        // The receiver-resolution step uses GetSymbolInfo on the member-access expression,
        // which gives us the property symbol for `Console.Out` / `Console.Error` even when
        // the receiver is a local alias for the same property.
        if (textWriterType is null
            || !IsTypeOrDerived(containingType, textWriterType)
            || !IsWriteOrFlushMethodName(methodName))
        {
            return;
        }

        var receiverExpr = memberAccess.Expression;
        if (IsConsoleErrorReceiver(receiverExpr, context.SemanticModel, consoleType, context.CancellationToken)
            || IsFlushMethodName(methodName))
        {
            return;
        }

        ReportInvocation(context, invocation, $"{ReceiverDescription(receiverExpr)}.{methodName}");
    }

    private static IMethodSymbol? ResolveInvokedMethod(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation)
    {
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        return symbolInfo.Symbol as IMethodSymbol
            ?? (symbolInfo.CandidateSymbols.Length > 0
                ? symbolInfo.CandidateSymbols[0] as IMethodSymbol
                : null);
    }

    private static bool IsStaticWrite(
        INamedTypeSymbol containingType,
        INamedTypeSymbol? expectedType,
        string methodName) =>
        expectedType is not null
        && SymbolEqualityComparer.Default.Equals(containingType, expectedType)
        && IsWriteMethodName(methodName);

    private static string? GetForbiddenStaticReceiver(
        INamedTypeSymbol containingType,
        INamedTypeSymbol? consoleType,
        INamedTypeSymbol? traceType,
        string methodName)
    {
        if (IsStaticWrite(containingType, consoleType, methodName))
        {
            return "Console";
        }

        return IsStaticWrite(containingType, traceType, methodName)
            ? "Trace"
            : null;
    }

    private static bool IsConsoleErrorReceiver(
        ExpressionSyntax receiver,
        SemanticModel semanticModel,
        INamedTypeSymbol? consoleType,
        System.Threading.CancellationToken cancellationToken)
    {
        if (consoleType is null)
        {
            return false;
        }

        var property = ResolveReceiverProperty(receiver, semanticModel, cancellationToken);
        return property is not null
            && SymbolEqualityComparer.Default.Equals(property.ContainingType, consoleType)
            && string.Equals(property.Name, "Error", StringComparison.Ordinal);
    }

    private static IPropertySymbol? ResolveReceiverProperty(
        ExpressionSyntax receiver,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var receiverSymbol = semanticModel.GetSymbolInfo(receiver, cancellationToken).Symbol;
        if (receiverSymbol is IPropertySymbol property)
        {
            return property;
        }

        if (receiverSymbol is not ILocalSymbol local
            || local.DeclaringSyntaxReferences.Length == 0
            || local.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken)
                is not VariableDeclaratorSyntax { Initializer.Value: MemberAccessExpressionSyntax initializer })
        {
            return null;
        }

        return semanticModel.GetSymbolInfo(initializer, cancellationToken).Symbol as IPropertySymbol;
    }

    private static string ReceiverDescription(ExpressionSyntax receiver) => receiver switch
    {
        MemberAccessExpressionSyntax ma => ma.ToString(),
        IdentifierNameSyntax id => id.Identifier.ValueText,
        _ => receiver.ToString(),
    };

    private static bool IsTypeOrDerived(INamedTypeSymbol candidate, INamedTypeSymbol baseType)
    {
        // Walk the inheritance chain to handle both `TextWriter.WriteLine` (declared on base)
        // and any TextWriter subclass that overrides Write/WriteLine. We don't expect Host.Stdio
        // to subclass TextWriter, but the check is cheap and protects against future drift.
        for (var current = candidate; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsWriteMethodName(string name) =>
        string.Equals(name, "Write", StringComparison.Ordinal)
        || string.Equals(name, "WriteLine", StringComparison.Ordinal)
        || string.Equals(name, "WriteAsync", StringComparison.Ordinal)
        || string.Equals(name, "WriteLineAsync", StringComparison.Ordinal);

    private static bool IsWriteOrFlushMethodName(string name) =>
        IsWriteMethodName(name) || IsFlushMethodName(name);

    private static bool IsFlushMethodName(string name) =>
        string.Equals(name, "Flush", StringComparison.Ordinal)
        || string.Equals(name, "FlushAsync", StringComparison.Ordinal);

    private static void ReportInvocation(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        string description)
    {
        // Report on the full invocation span (including arguments) so the diagnostic
        // squiggle covers the whole call site, not just the method-name member access.
        // This matches the IDE convention for Console.WriteLine-style violations.
        context.ReportDiagnostic(Diagnostic.Create(
            s_stdoutWrite,
            invocation.GetLocation(),
            description,
            TargetAssemblyName));
    }
}
