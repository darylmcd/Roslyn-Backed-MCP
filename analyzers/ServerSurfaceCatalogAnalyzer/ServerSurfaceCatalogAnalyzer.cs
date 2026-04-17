// Copyright (c) darylmcd. Licensed under the MIT License.
//
// ServerSurfaceCatalogAnalyzer — build-time parity check for the hand-maintained
// Host.Stdio surface catalog (`ServerSurfaceCatalog.Tools` / `.Resources` / `.Prompts`)
// against the [McpServerTool] / [McpServerResource] / [McpServerPrompt] attribute
// decorations on methods in the same compilation.
//
// See ai_docs/plans/20260417T120000Z_backlog-sweep/plan.md initiative #2
// (mcp-server-surface-catalog-parity-generator) for the rationale: drift was
// only caught at test-run by SurfaceCatalogTests, which produced a slow
// build-green→test-red signal for a problem the compiler could flag directly.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RoslynMcp.Analyzers.ServerSurfaceCatalog;

/// <summary>
/// Enforces name-parity between the hand-maintained <c>ServerSurfaceCatalog</c>
/// initializer lists (<c>Tools</c>, <c>Resources</c>, <c>Prompts</c>) and methods
/// decorated with the corresponding ModelContextProtocol server attributes.
/// </summary>
/// <remarks>
/// Kinds are intentionally paired: <c>[McpServerTool]</c> pairs with
/// <c>Tools</c>, <c>[McpServerResource]</c> with <c>Resources</c>, and
/// <c>[McpServerPrompt]</c> with <c>Prompts</c>. An attributed method with
/// name <c>foo</c> that appears in the <em>wrong</em> list (e.g. Tools when it
/// should be Prompts) is still treated as "missing from the correct list" —
/// that produces both an <c>RMCP001</c> on the attribute site and an
/// <c>RMCP002</c> on the catalog string — which is the correct behavior.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ServerSurfaceCatalogAnalyzer : DiagnosticAnalyzer
{
    // ModelContextProtocol.Server attribute metadata names — the analyzer matches on
    // these fully qualified names so a typo in the attribute namespace does NOT
    // silently stop the parity check.
    private const string McpServerToolAttribute = "ModelContextProtocol.Server.McpServerToolAttribute";
    private const string McpServerResourceAttribute = "ModelContextProtocol.Server.McpServerResourceAttribute";
    private const string McpServerPromptAttribute = "ModelContextProtocol.Server.McpServerPromptAttribute";

    // Catalog helper method names — the catalog initializer shape is
    //   public static IReadOnlyList<SurfaceEntry> Tools { get; } =
    //   [
    //       Tool("server_info", ...),
    //       Tool("server_heartbeat", ...),
    //       ...
    //   ];
    // We match on the method name and surrounding containing property to infer kind.
    private const string CatalogToolMethod = "Tool";
    private const string CatalogResourceMethod = "Resource";
    private const string CatalogPromptMethod = "Prompt";
    private const string CatalogToolsProperty = "Tools";
    private const string CatalogResourcesProperty = "Resources";
    private const string CatalogPromptsProperty = "Prompts";

    // Containing type name for the catalog class — we ignore helper-named invocations
    // defined elsewhere (consumer code may have unrelated `Tool`/`Resource` methods).
    private const string CatalogTypeName = "ServerSurfaceCatalog";

    private static readonly DiagnosticDescriptor s_missingCatalogEntry = new(
        id: "RMCP001",
        title: "SurfaceCatalog missing entry for attributed MCP method",
        messageFormat: "SurfaceCatalog missing entry for '{0}' — method decorated with [{1}] has no matching entry in ServerSurfaceCatalog.{2}",
        category: "McpCatalog",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "Every method decorated with [McpServerTool], [McpServerResource], or " +
            "[McpServerPrompt] must have a matching entry (by Name) in the corresponding " +
            "ServerSurfaceCatalog list. The runtime catalog drives server_info and the " +
            "server_catalog resource — drift produces missing-from-catalog server surface " +
            "that clients cannot discover.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    private static readonly DiagnosticDescriptor s_orphanCatalogEntry = new(
        id: "RMCP002",
        title: "SurfaceCatalog entry not backed by any MCP attribute",
        messageFormat: "SurfaceCatalog entry '{0}' in ServerSurfaceCatalog.{1} is not backed by any [McpServer{2}] attribute",
        category: "McpCatalog",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "Every entry in ServerSurfaceCatalog.Tools / Resources / Prompts must be " +
            "backed by a method decorated with the matching [McpServerTool] / " +
            "[McpServerResource] / [McpServerPrompt] attribute. An orphan entry signals " +
            "either a removed handler whose catalog row was forgotten or a typo in the " +
            "catalog name.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(s_missingCatalogEntry, s_orphanCatalogEntry);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationStart =>
        {
            var toolAttr = compilationStart.Compilation.GetTypeByMetadataName(McpServerToolAttribute);
            var resourceAttr = compilationStart.Compilation.GetTypeByMetadataName(McpServerResourceAttribute);
            var promptAttr = compilationStart.Compilation.GetTypeByMetadataName(McpServerPromptAttribute);

            // No MCP server types in scope → nothing to enforce. An analyzer loaded into
            // a consumer project that doesn't reference ModelContextProtocol should be
            // a no-op, not a crash.
            if (toolAttr is null && resourceAttr is null && promptAttr is null)
            {
                return;
            }

            var state = new CatalogState();

            compilationStart.RegisterSymbolAction(
                symbolContext => AnalyzeMethodAttributes(symbolContext, state, toolAttr, resourceAttr, promptAttr),
                SymbolKind.Method);

            compilationStart.RegisterSyntaxNodeAction(
                syntaxContext => AnalyzeCatalogInvocation(syntaxContext, state),
                SyntaxKind.InvocationExpression);

            compilationStart.RegisterCompilationEndAction(endContext => ReportDrift(endContext, state));
        });
    }

    private static void AnalyzeMethodAttributes(
        SymbolAnalysisContext context,
        CatalogState state,
        INamedTypeSymbol? toolAttr,
        INamedTypeSymbol? resourceAttr,
        INamedTypeSymbol? promptAttr)
    {
        var method = (IMethodSymbol)context.Symbol;
        foreach (var attribute in method.GetAttributes())
        {
            var attrClass = attribute.AttributeClass;
            if (attrClass is null) continue;

            SurfaceKind? kind = null;
            string? attributeName = null;
            if (toolAttr is not null && SymbolEqualityComparer.Default.Equals(attrClass, toolAttr))
            {
                kind = SurfaceKind.Tool;
                attributeName = "McpServerTool";
            }
            else if (resourceAttr is not null && SymbolEqualityComparer.Default.Equals(attrClass, resourceAttr))
            {
                kind = SurfaceKind.Resource;
                attributeName = "McpServerResource";
            }
            else if (promptAttr is not null && SymbolEqualityComparer.Default.Equals(attrClass, promptAttr))
            {
                kind = SurfaceKind.Prompt;
                attributeName = "McpServerPrompt";
            }

            if (kind is null || attributeName is null) continue;

            var toolName = ExtractNameArgument(attribute);
            if (string.IsNullOrEmpty(toolName)) continue;

            var location = attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
                ?? method.Locations.FirstOrDefault()
                ?? Location.None;

            state.AddAttributed(kind.Value, toolName!, location, attributeName);
        }
    }

    private static string? ExtractNameArgument(AttributeData attribute)
    {
        // Attribute usage we care about:
        //   [McpServerTool(Name = "server_info", ReadOnly = true, ...)]
        //   [McpServerResource(Name = "server_catalog", ...)]
        //   [McpServerPrompt(Name = "explain_error", ...)]
        // The Name is always a named argument in this codebase; positional support
        // is added defensively in case a future refactor changes the shape.
        foreach (var named in attribute.NamedArguments)
        {
            if (string.Equals(named.Key, "Name", StringComparison.Ordinal)
                && named.Value.Value is string named_s)
            {
                return named_s;
            }
        }

        foreach (var ctor in attribute.ConstructorArguments)
        {
            if (ctor.Value is string s) return s;
        }

        return null;
    }

    private static void AnalyzeCatalogInvocation(SyntaxNodeAnalysisContext context, CatalogState state)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Only handle simple-name invocations: `Tool(...)`, `Resource(...)`, `Prompt(...)`.
        // Qualified names (e.g. `self.Tool(...)`) are not the pattern used in the catalog
        // and would require extra semantic resolution — skip them here rather than mis-
        // classify.
        if (invocation.Expression is not IdentifierNameSyntax identifier)
        {
            return;
        }

        var methodName = identifier.Identifier.ValueText;
        switch (methodName)
        {
            case CatalogToolMethod:
            case CatalogResourceMethod:
            case CatalogPromptMethod:
                break;
            default:
                return;
        }

        // The invocation must be inside one of the three catalog initializer properties.
        // We walk up the syntax until we hit a PropertyDeclarationSyntax and compare the
        // identifier. This is also our primary scoping check: the walk is bounded and
        // rejects any `Tool(...)` call outside a catalog-shaped initializer.
        var containingProperty = invocation.FirstAncestorOrSelf<PropertyDeclarationSyntax>();
        if (containingProperty is null)
        {
            return;
        }

        var propertyName = containingProperty.Identifier.ValueText;
        SurfaceKind kind;
        switch (propertyName)
        {
            case CatalogToolsProperty: kind = SurfaceKind.Tool; break;
            case CatalogResourcesProperty: kind = SurfaceKind.Resource; break;
            case CatalogPromptsProperty: kind = SurfaceKind.Prompt; break;
            default: return;
        }

        // The property must be on a class named ServerSurfaceCatalog — this scopes
        // the analyzer to one specific class and prevents false-positives when a
        // consumer happens to have a `Tools` property with `Tool(...)` factory calls.
        var containingClass = containingProperty.FirstAncestorOrSelf<ClassDeclarationSyntax>();
        if (containingClass is null
            || !string.Equals(containingClass.Identifier.ValueText, CatalogTypeName, StringComparison.Ordinal))
        {
            return;
        }

        // Semantic check: require the invocation to bind to a method on ServerSurfaceCatalog
        // (not some helper of the same name imported from elsewhere). This is the final
        // disambiguation that makes the analyzer safe against identifier shadowing.
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        var targetMethod = symbolInfo.Symbol as IMethodSymbol
            ?? symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
        if (targetMethod is not null
            && !string.Equals(targetMethod.ContainingType?.Name, CatalogTypeName, StringComparison.Ordinal))
        {
            return;
        }

        var firstArg = invocation.ArgumentList.Arguments.FirstOrDefault();
        if (firstArg is null)
        {
            return;
        }

        if (firstArg.Expression is not LiteralExpressionSyntax literal
            || !literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            // Dynamic / concatenated names can't be checked statically; record them
            // as "catalog entry present but unknown" so we don't false-positive a
            // RMCP001 against an attributed method whose row genuinely exists but is
            // not literal-only.
            state.AddUnresolvedCatalogEntry(kind);
            return;
        }

        var name = literal.Token.ValueText;
        state.AddCatalogEntry(kind, name, literal.GetLocation());
    }

    private static void ReportDrift(CompilationAnalysisContext context, CatalogState state)
    {
        // RMCP001: attributed methods whose name is not present in the catalog.
        foreach (var kind in AllKinds)
        {
            if (state.HasUnresolvedCatalogEntries(kind))
            {
                // If any catalog entry was non-literal, we cannot be sure whether a given
                // attribute is "missing". Skip RMCP001 for this kind to avoid false-positives.
                // RMCP002 still runs (orphan detection does not depend on attribute presence
                // being provable in isolation).
            }
            else
            {
                foreach (var (name, occurrence) in state.GetAttributed(kind))
                {
                    if (!state.HasCatalogEntry(kind, name))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            s_missingCatalogEntry,
                            occurrence.Location,
                            name,
                            occurrence.AttributeName,
                            PropertyNameFor(kind)));
                    }
                }
            }
        }

        // RMCP002: catalog entries whose name is not claimed by any attributed method.
        foreach (var kind in AllKinds)
        {
            foreach (var (name, location) in state.GetCatalogEntries(kind))
            {
                if (!state.HasAttributed(kind, name))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        s_orphanCatalogEntry,
                        location,
                        name,
                        PropertyNameFor(kind),
                        KindSuffixFor(kind)));
                }
            }
        }
    }

    private enum SurfaceKind { Tool, Resource, Prompt }

    private static readonly SurfaceKind[] AllKinds =
        [SurfaceKind.Tool, SurfaceKind.Resource, SurfaceKind.Prompt];

    private static string PropertyNameFor(SurfaceKind kind) => kind switch
    {
        SurfaceKind.Tool => CatalogToolsProperty,
        SurfaceKind.Resource => CatalogResourcesProperty,
        SurfaceKind.Prompt => CatalogPromptsProperty,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static string KindSuffixFor(SurfaceKind kind) => kind switch
    {
        SurfaceKind.Tool => "Tool",
        SurfaceKind.Resource => "Resource",
        SurfaceKind.Prompt => "Prompt",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>
    /// Concurrent accumulator for the parity check. Symbol and syntax-node callbacks
    /// fire in parallel under <see cref="AnalysisContext.EnableConcurrentExecution"/>,
    /// so every mutable collection here is thread-safe.
    /// </summary>
    private sealed class CatalogState
    {
        private readonly ConcurrentDictionary<(SurfaceKind Kind, string Name), AttributedMethodOccurrence> _attributed = new();
        private readonly ConcurrentDictionary<(SurfaceKind Kind, string Name), Location> _catalog = new();
        private readonly int[] _unresolvedCatalogCountByKind = new int[3];

        public void AddAttributed(SurfaceKind kind, string name, Location location, string attributeName)
        {
            // Many methods can share a catalog name when a future refactor splits a
            // single tool into partials — but that is not a pattern in this codebase
            // today, and we intentionally keep the first occurrence so the diagnostic
            // points at a stable source location.
            _attributed.TryAdd((kind, name), new AttributedMethodOccurrence(location, attributeName));
        }

        public void AddCatalogEntry(SurfaceKind kind, string name, Location location)
        {
            _catalog.TryAdd((kind, name), location);
        }

        public void AddUnresolvedCatalogEntry(SurfaceKind kind)
        {
            Interlocked.Increment(ref _unresolvedCatalogCountByKind[(int)kind]);
        }

        public bool HasUnresolvedCatalogEntries(SurfaceKind kind) =>
            Volatile.Read(ref _unresolvedCatalogCountByKind[(int)kind]) > 0;

        public bool HasCatalogEntry(SurfaceKind kind, string name) =>
            _catalog.ContainsKey((kind, name));

        public bool HasAttributed(SurfaceKind kind, string name) =>
            _attributed.ContainsKey((kind, name));

        public IEnumerable<(string Name, AttributedMethodOccurrence Occurrence)> GetAttributed(SurfaceKind kind) =>
            _attributed
                .Where(entry => entry.Key.Kind == kind)
                .Select(entry => (entry.Key.Name, entry.Value));

        public IEnumerable<(string Name, Location Location)> GetCatalogEntries(SurfaceKind kind) =>
            _catalog
                .Where(entry => entry.Key.Kind == kind)
                .Select(entry => (entry.Key.Name, entry.Value));
    }

    private sealed class AttributedMethodOccurrence
    {
        public AttributedMethodOccurrence(Location location, string attributeName)
        {
            Location = location;
            AttributeName = attributeName;
        }

        public Location Location { get; }
        public string AttributeName { get; }
    }
}

