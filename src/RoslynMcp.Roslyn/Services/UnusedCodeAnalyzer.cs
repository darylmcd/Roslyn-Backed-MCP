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
    private readonly ILogger<UnusedCodeAnalyzer> _logger;

    public UnusedCodeAnalyzer(IWorkspaceManager workspace, ILogger<UnusedCodeAnalyzer> logger)
    {
        _workspace = workspace;
        _logger = logger;
    }

    public async Task<IReadOnlyList<UnusedSymbolDto>> FindUnusedSymbolsAsync(
        string workspaceId, string? projectFilter, bool includePublic, int limit, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var results = new List<UnusedSymbolDto>();
        var processedSymbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

        var projects = ProjectFilterHelper.FilterProjects(solution, projectFilter);

        foreach (var project in projects)
        {
            if (ct.IsCancellationRequested || results.Count >= limit) break;

            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null) continue;

            foreach (var tree in compilation.SyntaxTrees)
            {
                if (ct.IsCancellationRequested || results.Count >= limit) break;
                if (PathFilter.IsGeneratedOrContentFile(tree.FilePath)) continue;

                var semanticModel = compilation.GetSemanticModel(tree);
                var root = await tree.GetRootAsync(ct).ConfigureAwait(false);

                var declarations = root.DescendantNodes().OfType<MemberDeclarationSyntax>();
                foreach (var decl in declarations)
                {
                    if (ct.IsCancellationRequested || results.Count >= limit) break;

                    var symbol = semanticModel.GetDeclaredSymbol(decl, ct);
                    if (symbol is null) continue;
                    if (symbol is INamespaceSymbol) continue;
                    if (symbol.IsImplicitlyDeclared) continue;
                    if (!processedSymbols.Add(symbol)) continue;

                    // Skip public symbols unless explicitly requested
                    if (!includePublic && symbol.DeclaredAccessibility == Accessibility.Public) continue;

                    // Skip constructors, operators, finalizers
                    if (symbol is IMethodSymbol method &&
                        (method.MethodKind is MethodKind.Constructor or MethodKind.StaticConstructor
                            or MethodKind.Destructor or MethodKind.UserDefinedOperator
                            or MethodKind.Conversion))
                        continue;

                    // Skip interface implementations
                    if (symbol.ContainingType is not null &&
                        symbol.ContainingType.AllInterfaces.Any(i =>
                            i.GetMembers().Any(m => SymbolEqualityComparer.Default.Equals(
                                symbol.ContainingType.FindImplementationForInterfaceMember(m), symbol))))
                        continue;

                    // Skip overrides
                    if (symbol is IMethodSymbol { IsOverride: true } or IPropertySymbol { IsOverride: true })
                        continue;

                    // Skip entry points
                    if (symbol is IMethodSymbol { Name: "Main" } && symbol.IsStatic) continue;

                    // Skip symbols with framework-invoked attributes (test methods, serialized members, etc.)
                    if (HasFrameworkInvokedAttribute(symbol)) continue;

                    // Skip static classes that contain extension methods — they are
                    // accessed implicitly through their members, not by direct reference.
                    if (symbol is INamedTypeSymbol { IsStatic: true } staticType &&
                        staticType.GetMembers().OfType<IMethodSymbol>().Any(m => m.IsExtensionMethod))
                        continue;

                    var refs = await SymbolFinder.FindReferencesAsync(symbol, solution, ct).ConfigureAwait(false);
                    var refCount = refs.Sum(r => r.Locations.Count());

                    // Fallback: cross-compilation re-resolution for symbols prone to
                    // identity mismatches (extension methods, overloaded methods resolved
                    // via implicit conversion). The project-local symbol from
                    // GetDeclaredSymbol may not match the reduced/converted form that
                    // callers bind to in other compilations.
                    if (refCount == 0 && NeedsCrossCompilationCheck(symbol))
                    {
                        refCount = await CountCrossCompilationReferencesAsync(
                            symbol, project, solution, ct).ConfigureAwait(false);
                    }

                    if (refCount == 0)
                    {
                        var location = symbol.Locations.FirstOrDefault(l => l.IsInSource);
                        if (location is null) continue;
                        var lineSpan = location.GetLineSpan();

                        results.Add(new UnusedSymbolDto(
                            symbol.Name,
                            symbol.Kind.ToString(),
                            lineSpan.Path,
                            lineSpan.StartLinePosition.Line + 1,
                            lineSpan.StartLinePosition.Character + 1,
                            symbol.ContainingType?.Name,
                            SymbolHandleSerializer.CreateHandle(symbol)));
                    }
                }
            }
        }

        return results;
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
    private static async Task<int> CountCrossCompilationReferencesAsync(
        ISymbol symbol, Project declaringProject, Solution solution, CancellationToken ct)
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

            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
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
