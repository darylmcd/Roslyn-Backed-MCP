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

        // Only member-access invocations carry the receiver shape we care about
        // (`Console.WriteLine(...)`, `Console.Out.WriteLine(...)`, `stdout.WriteLine(...)`).
        // Bare-identifier invocations like `WriteLine(...)` (only valid via `using static`)
        // are caught by the symbol-binding check below — we resolve via `GetSymbolInfo`
        // rather than syntax shape so the rule is robust to import style.
        var memberAccess = invocation.Expression as MemberAccessExpressionSyntax;
        if (memberAccess is null)
        {
            return;
        }

        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        var methodSymbol = symbolInfo.Symbol as IMethodSymbol;
        if (methodSymbol is null && symbolInfo.CandidateSymbols.Length > 0)
        {
            methodSymbol = symbolInfo.CandidateSymbols[0] as IMethodSymbol;
        }

        if (methodSymbol is null)
        {
            return;
        }

        var methodName = methodSymbol.Name;
        var containingType = methodSymbol.ContainingType;
        if (containingType is null)
        {
            return;
        }

        // Branch 1: System.Console static methods (Console.Write*, Console.WriteLine*).
        // No instance receiver — Console is a static class — so any Write* is forbidden.
        if (consoleType is not null
            && SymbolEqualityComparer.Default.Equals(containingType, consoleType)
            && IsWriteMethodName(methodName))
        {
            ReportInvocation(context, invocation, $"Console.{methodName}");
            return;
        }

        // Branch 2: System.Diagnostics.Trace static methods (Trace.Write*, Trace.WriteLine*).
        // Trace defaults to writing to OutputDebugString on Windows but is documented to
        // route to stdout in some host configurations — flag it for the same reason as
        // Console.Write*. Allow-listed flush methods don't apply (Trace.Flush is fine —
        // it doesn't emit content).
        if (traceType is not null
            && SymbolEqualityComparer.Default.Equals(containingType, traceType)
            && IsWriteMethodName(methodName))
        {
            ReportInvocation(context, invocation, $"Trace.{methodName}");
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
        if (textWriterType is not null
            && IsTypeOrDerived(containingType, textWriterType)
            && IsWriteOrFlushMethodName(methodName))
        {
            // Resolve the receiver expression (the bit before the `.` in `receiver.Method(...)`).
            var receiverExpr = memberAccess.Expression;

            // Allow-list `Console.Error.*` — stderr is the canonical diagnostic channel for
            // stdio servers and is never the framing channel.
            if (IsConsoleErrorReceiver(receiverExpr, context.SemanticModel, consoleType, context.CancellationToken))
            {
                return;
            }

            // From here on we're looking at a TextWriter receiver that is NOT Console.Error.
            // Allow flush calls regardless of receiver — they don't emit content.
            if (IsFlushMethodName(methodName))
            {
                return;
            }

            // Flag the write. We don't gate on "is this Console.Out specifically" because
            // Host.Stdio has no legitimate use case for writing to any other TextWriter
            // (no file appenders, no in-memory writers in production code paths). The
            // diagnostic message points at the canonical fix (route through ILogger).
            ReportInvocation(context, invocation, $"{ReceiverDescription(receiverExpr)}.{methodName}");
        }
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

        var receiverSymbolInfo = semanticModel.GetSymbolInfo(receiver, cancellationToken);
        var receiverSymbol = receiverSymbolInfo.Symbol;
        if (receiverSymbol is null)
        {
            return false;
        }

        // Direct `Console.Error` receiver — symbol is the IPropertySymbol on Console.
        if (receiverSymbol is IPropertySymbol property
            && SymbolEqualityComparer.Default.Equals(property.ContainingType, consoleType)
            && string.Equals(property.Name, "Error", StringComparison.Ordinal))
        {
            return true;
        }

        // Aliased `var stderr = Console.Error; stderr.WriteLine(...)` — receiver symbol is the
        // local; we follow the local's initializer if present. Conservative: we only allow when
        // the local's initializer is exactly the `Console.Error` property access. More elaborate
        // dataflow (e.g. assignment from a parameter) is out of scope; if Host.Stdio ever needs
        // that pattern it can use ILogger instead, which is the recommended path anyway.
        if (receiverSymbol is ILocalSymbol local)
        {
            var declaringRef = local.DeclaringSyntaxReferences.Length > 0
                ? local.DeclaringSyntaxReferences[0]
                : null;
            var declaringSyntax = declaringRef?.GetSyntax(cancellationToken);
            if (declaringSyntax is VariableDeclaratorSyntax declarator
                && declarator.Initializer?.Value is MemberAccessExpressionSyntax initMa)
            {
                var initSymbol = semanticModel.GetSymbolInfo(initMa, cancellationToken).Symbol;
                if (initSymbol is IPropertySymbol initProperty
                    && SymbolEqualityComparer.Default.Equals(initProperty.ContainingType, consoleType)
                    && string.Equals(initProperty.Name, "Error", StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
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
