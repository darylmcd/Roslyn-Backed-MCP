using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

public sealed class UnusedCodeAnalyzer : IUnusedCodeAnalyzer
{
    private static readonly HashSet<string> FrameworkInvokedAttributeNames =
    [
        "Fact",
        "Theory",
        "Test",
        "TestMethod",
        "TestCase",
        "DataMember",
        "JsonProperty",
        "JsonRequired",
        "JsonInclude",
        "Required",
        "Key",
        "Column",
        "Table",
        "HttpGet",
        "HttpPost",
        "HttpPut",
        "HttpDelete",
        "HttpPatch",
        "Route",
        "ApiController",
        "Controller",
        "EventHandler",
        "MessageHandler",
        "Subscribe"
    ];

    private readonly IWorkspaceManager _workspace;
    private readonly ICompilationCache _compilationCache;
    private readonly ILogger<UnusedCodeAnalyzer> _logger;

    public UnusedCodeAnalyzer(IWorkspaceManager workspace, ICompilationCache compilationCache, ILogger<UnusedCodeAnalyzer> logger)
    {
        _workspace = workspace;
        _compilationCache = compilationCache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<UnusedSymbolDto>> FindUnusedSymbolsAsync(
        string workspaceId,
        string? projectFilter,
        bool includePublic,
        int limit,
        bool excludeEnums,
        bool excludeRecordProperties,
        CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var results = new List<UnusedSymbolDto>();
        var processedSymbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

        var projects = ProjectFilterHelper.FilterProjects(solution, projectFilter);

        foreach (var project in projects)
        {
            if (ct.IsCancellationRequested || results.Count >= limit) break;

            var compilation = await _compilationCache.GetCompilationAsync(workspaceId, project, ct).ConfigureAwait(false);
            if (compilation is null) continue;

            // Phase 1 (sequential, cheap): walk syntax and collect candidates that survive
            // the pre-filter. The HashSet de-dup must run on a single thread.
            var candidates = new List<ISymbol>();
            foreach (var tree in compilation.SyntaxTrees)
            {
                if (ct.IsCancellationRequested) break;
                if (PathFilter.IsGeneratedOrContentFile(tree.FilePath)) continue;

                var semanticModel = compilation.GetSemanticModel(tree);
                var root = await tree.GetRootAsync(ct).ConfigureAwait(false);

                foreach (var decl in root.DescendantNodes().OfType<MemberDeclarationSyntax>())
                {
                    if (ct.IsCancellationRequested) break;

                    var symbol = semanticModel.GetDeclaredSymbol(decl, ct);
                    if (symbol is null) continue;
                    if (symbol is INamespaceSymbol) continue;
                    if (symbol.IsImplicitlyDeclared) continue;
                    if (!processedSymbols.Add(symbol)) continue;

                    if (ShouldSkipSymbolForUnusedAnalysis(symbol, includePublic, excludeEnums, excludeRecordProperties))
                        continue;

                    candidates.Add(symbol);
                }
            }

            if (candidates.Count == 0) continue;

            // Phase 2 (parallel, expensive): each candidate fans out to a SymbolFinder lookup
            // (and possibly a cross-compilation fallback). Roslyn's Solution is immutable, so
            // SymbolFinder is safe under concurrency. Bound parallelism by CPU count.
            var parallelism = Math.Clamp(Environment.ProcessorCount, 4, 16);
            using var semaphore = new SemaphoreSlim(parallelism, parallelism);
            var capturedProject = project;
            var capturedSolution = solution;

            var tasks = candidates.Select(async symbol =>
            {
                await semaphore.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    var refs = await SymbolFinder.FindReferencesAsync(symbol, capturedSolution, ct).ConfigureAwait(false);
                    var refCount = refs.Sum(r => r.Locations.Count());

                    // Fallback: cross-compilation re-resolution for symbols prone to
                    // identity mismatches (extension methods, overloaded methods resolved
                    // via implicit conversion). The project-local symbol from
                    // GetDeclaredSymbol may not match the reduced/converted form that
                    // callers bind to in other compilations.
                    if (refCount == 0 && NeedsCrossCompilationCheck(symbol))
                    {
                        refCount = await CountCrossCompilationReferencesAsync(
                            workspaceId, symbol, capturedProject, capturedSolution, ct).ConfigureAwait(false);
                    }

                    return refCount == 0 ? BuildUnusedDto(symbol) : null;
                }
                finally
                {
                    semaphore.Release();
                }
            });

            // Preserve original ordering (project → tree → declaration order) by awaiting
            // in input order, not completion order.
            var unusedDtos = await Task.WhenAll(tasks).ConfigureAwait(false);
            foreach (var dto in unusedDtos)
            {
                if (dto is null) continue;
                results.Add(dto);
                if (results.Count >= limit) break;
            }
        }

        return results;
    }

    private static UnusedSymbolDto? BuildUnusedDto(ISymbol symbol)
    {
        var location = symbol.Locations.FirstOrDefault(l => l.IsInSource);
        if (location is null) return null;
        var lineSpan = location.GetLineSpan();

        return new UnusedSymbolDto(
            symbol.Name,
            symbol.Kind.ToString(),
            lineSpan.Path,
            lineSpan.StartLinePosition.Line + 1,
            lineSpan.StartLinePosition.Character + 1,
            symbol.ContainingType?.Name,
            SymbolHandleSerializer.CreateHandle(symbol),
            ComputeUnusedConfidence(symbol));
    }

    /// <summary>
    /// Returns true when the symbol should not be analyzed for unused status (filters noise: public API, tests, etc.).
    /// </summary>
    private static bool ShouldSkipSymbolForUnusedAnalysis(
        ISymbol symbol,
        bool includePublic,
        bool excludeEnums,
        bool excludeRecordProperties)
    {
        if (!includePublic && symbol.DeclaredAccessibility == Accessibility.Public)
            return true;

        if (excludeEnums && symbol is IFieldSymbol { ContainingType.TypeKind: TypeKind.Enum })
            return true;

        if (excludeRecordProperties && symbol is IPropertySymbol prop &&
            prop.ContainingType?.IsRecord == true)
            return true;

        if (symbol is IMethodSymbol method &&
            (method.MethodKind is MethodKind.Constructor or MethodKind.StaticConstructor
                or MethodKind.Destructor or MethodKind.UserDefinedOperator
                or MethodKind.Conversion))
            return true;

        if (symbol.ContainingType is not null &&
            symbol.ContainingType.AllInterfaces.Any(i =>
                i.GetMembers().Any(m => SymbolEqualityComparer.Default.Equals(
                    symbol.ContainingType!.FindImplementationForInterfaceMember(m), symbol))))
            return true;

        if (symbol is IMethodSymbol { IsOverride: true } or IPropertySymbol { IsOverride: true })
            return true;

        if (symbol is IMethodSymbol { Name: "Main" } && symbol.IsStatic)
            return true;

        if (HasFrameworkInvokedAttribute(symbol))
            return true;

        if (symbol is INamedTypeSymbol { IsStatic: true } staticType &&
            staticType.GetMembers().OfType<IMethodSymbol>().Any(m => m.IsExtensionMethod))
            return true;

        return false;
    }

    /// <summary>
    /// Determines whether a symbol is susceptible to cross-compilation identity
    /// mismatches that cause <see cref="SymbolFinder.FindReferencesAsync"/> to miss
    /// references when the symbol comes from the declaring project's compilation.
    /// </summary>
    private static bool NeedsCrossCompilationCheck(ISymbol symbol)
    {
        if (symbol is IMethodSymbol method)
        {
            // Extension methods: call sites bind to ReducedExtensionMethod which
            // may not map back to the original definition across compilations.
            if (method.IsExtensionMethod)
                return true;

            // Overloaded methods: when callers resolve via implicit conversion
            // (e.g. List<T> → IEnumerable<T>), the bound symbol in the caller's
            // compilation may have a different identity than the declaring symbol.
            if (method.ContainingType is not null &&
                method.ContainingType.GetMembers(method.Name)
                    .OfType<IMethodSymbol>()
                    .Count(m => m.MethodKind == MethodKind.Ordinary) > 1)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Re-resolves a symbol through dependent projects' compilations and checks for
    /// references from each. Returns the total reference count found, or 0 if none.
    /// </summary>
    private async Task<int> CountCrossCompilationReferencesAsync(
        string workspaceId, ISymbol symbol, Project declaringProject, Solution solution, CancellationToken ct)
    {
        var containingType = symbol.ContainingType;
        if (containingType is null) return 0;

        var metadataName = containingType.ToDisplayString();

        foreach (var project in solution.Projects)
        {
            if (ct.IsCancellationRequested) break;

            // Skip the declaring project — we already checked it.
            if (project.Id == declaringProject.Id) continue;

            // Only check projects that could reference the declaring project.
            if (!project.ProjectReferences.Any(r => r.ProjectId == declaringProject.Id))
                continue;

            var compilation = await _compilationCache.GetCompilationAsync(workspaceId, project, ct).ConfigureAwait(false);
            if (compilation is null) continue;

            var resolvedType = compilation.GetTypeByMetadataName(metadataName);
            if (resolvedType is null) continue;

            // Find the equivalent member in this compilation's type.
            var resolvedMember = FindMatchingMember(resolvedType, symbol);
            if (resolvedMember is null) continue;

            var refs = await SymbolFinder.FindReferencesAsync(resolvedMember, solution, ct).ConfigureAwait(false);
            var count = refs.Sum(r => r.Locations.Count());
            if (count > 0) return count;
        }

        return 0;
    }

    /// <summary>
    /// Finds a member in <paramref name="type"/> that matches <paramref name="original"/>
    /// by name, kind, and parameter signature.
    /// </summary>
    private static ISymbol? FindMatchingMember(INamedTypeSymbol type, ISymbol original)
    {
        var candidates = type.GetMembers(original.Name);

        if (original is not IMethodSymbol originalMethod)
            return candidates.FirstOrDefault(c => c.Kind == original.Kind);

        foreach (var candidate in candidates.OfType<IMethodSymbol>())
        {
            if (candidate.Parameters.Length != originalMethod.Parameters.Length)
                continue;

            var match = true;
            for (var i = 0; i < candidate.Parameters.Length; i++)
            {
                if (!SymbolEqualityComparer.Default.Equals(
                        candidate.Parameters[i].Type.OriginalDefinition,
                        originalMethod.Parameters[i].Type.OriginalDefinition))
                {
                    match = false;
                    break;
                }
            }

            if (match) return candidate;
        }

        return null;
    }

    private static string ComputeUnusedConfidence(ISymbol symbol)
    {
        if (symbol.ContainingType?.TypeKind == TypeKind.Interface)
            return "low";

        if (symbol is IFieldSymbol { ContainingType.TypeKind: TypeKind.Enum })
            return "low";

        if (symbol is IPropertySymbol p &&
            (p.ContainingType?.IsRecord == true || HasJsonSerializationHint(p)))
            return "low";

        if (symbol.DeclaredAccessibility == Accessibility.Public)
            return "medium";

        return "high";
    }

    private static bool HasJsonSerializationHint(IPropertySymbol p)
    {
        foreach (var attribute in p.GetAttributes())
        {
            var name = attribute.AttributeClass?.Name;
            if (name is null) continue;
            if (name is "JsonPropertyNameAttribute" or "JsonPropertyAttribute" or "JsonIncludeAttribute"
                or "DataMemberAttribute")
                return true;
        }

        return false;
    }

    private static bool HasFrameworkInvokedAttribute(ISymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            var name = attribute.AttributeClass?.Name;
            if (string.IsNullOrWhiteSpace(name)) continue;

            var normalized = name.EndsWith("Attribute", StringComparison.Ordinal)
                ? name[..^"Attribute".Length]
                : name;

            if (FrameworkInvokedAttributeNames.Contains(normalized))
                return true;
        }

        return false;
    }
}
