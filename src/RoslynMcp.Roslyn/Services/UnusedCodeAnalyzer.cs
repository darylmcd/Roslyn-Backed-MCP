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

        var projects = ProjectFilterHelper.FilterProjects(solution, projectFilter);

        foreach (var project in projects)
        {
            if (ct.IsCancellationRequested || results.Count >= limit) break;

            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null) continue;

            foreach (var tree in compilation.SyntaxTrees)
            {
                if (ct.IsCancellationRequested || results.Count >= limit) break;

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

                    var refs = await SymbolFinder.FindReferencesAsync(symbol, solution, ct).ConfigureAwait(false);
                    var refCount = refs.Sum(r => r.Locations.Count());

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
}
