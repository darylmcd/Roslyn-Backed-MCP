using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Item 10 implementation. Walks each test project's methods and records every productive
/// symbol (method/property/constructor) they invoke via Roslyn's semantic model. Productive
/// symbols live in projects that are NOT test projects themselves.
/// </summary>
public sealed class TestReferenceMapService : ITestReferenceMapService
{
    private readonly IWorkspaceManager _workspace;

    public TestReferenceMapService(IWorkspaceManager workspace)
    {
        _workspace = workspace;
    }

    public async Task<TestReferenceMapDto> BuildAsync(string workspaceId, string? projectName, CancellationToken ct)
    {
        var status = _workspace.GetStatus(workspaceId);
        var solution = _workspace.GetCurrentSolution(workspaceId);

        var scopedProjects = string.IsNullOrWhiteSpace(projectName)
            ? solution.Projects.ToList()
            : solution.Projects.Where(p =>
                string.Equals(p.Name, projectName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.FilePath, projectName, StringComparison.OrdinalIgnoreCase)).ToList();

        if (scopedProjects.Count == 0)
        {
            throw new InvalidOperationException(
                $"test_reference_map: project '{projectName}' not found in workspace.");
        }

        // Map project ids to their test-or-not shape so we can classify quickly during the scan.
        var testProjectIds = new HashSet<ProjectId>();
        foreach (var project in solution.Projects)
        {
            var projStatus = status.Projects.FirstOrDefault(p =>
                string.Equals(p.FilePath, project.FilePath, StringComparison.OrdinalIgnoreCase));
            if (projStatus is not null && projStatus.IsTestProject)
                testProjectIds.Add(project.Id);
        }

        var productiveSymbols = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var allProductive = new HashSet<string>(StringComparer.Ordinal);
        var scannedTestProjects = new List<string>();
        var notes = new List<string>();

        // First pass: collect every public/internal productive symbol declaration across
        // non-test referenced projects so we can compute uncovered = productive - covered.
        foreach (var project in solution.Projects)
        {
            if (testProjectIds.Contains(project.Id)) continue;
            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null) continue;
            CollectProductiveSymbols(compilation.GlobalNamespace, allProductive);
        }

        // Second pass: walk each test project's test methods and record references to
        // productive symbols.
        foreach (var testProject in scopedProjects.Where(p => testProjectIds.Contains(p.Id)))
        {
            ct.ThrowIfCancellationRequested();
            scannedTestProjects.Add(testProject.Name);
            var compilation = await testProject.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null) continue;

            foreach (var document in testProject.Documents)
            {
                var semanticModel = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
                var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
                if (semanticModel is null || root is null) continue;

                foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
                {
                    var testMethodSymbol = semanticModel.GetDeclaredSymbol(method, ct) as IMethodSymbol;
                    if (testMethodSymbol is null) continue;
                    var testMethodName = testMethodSymbol.ToDisplayString();

                    foreach (var invocation in method.DescendantNodes().OfType<InvocationExpressionSyntax>())
                    {
                        var symbolInfo = semanticModel.GetSymbolInfo(invocation, ct);
                        var target = symbolInfo.Symbol as IMethodSymbol;
                        if (target is null) continue;
                        if (target.ContainingAssembly is null) continue;
                        if (target.Locations.All(l => l.Kind == LocationKind.MetadataFile)) continue;
                        if (testProjectIds.Contains(target.ContainingAssembly.GetProjectIdFromSolution(solution))) continue;
                        if (target.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or Accessibility.Protected or Accessibility.ProtectedOrInternal))
                            continue;

                        var display = target.OriginalDefinition.ToDisplayString();
                        if (!allProductive.Contains(display)) continue;

                        if (!productiveSymbols.TryGetValue(display, out var methods))
                        {
                            methods = new HashSet<string>(StringComparer.Ordinal);
                            productiveSymbols[display] = methods;
                        }
                        methods.Add(testMethodName);
                    }
                }
            }
        }

        if (scannedTestProjects.Count == 0)
        {
            notes.Add("No test projects detected (IsTestProject=true). Coverage defaults to 0%.");
        }
        notes.Add(
            "Static reference analysis misses reflection, DI-constructed calls, and mocks. " +
            "Runtime coverage from test_coverage remains the authoritative view for those.");

        var covered = productiveSymbols
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new CoveredSymbolDto(kv.Key, kv.Value.OrderBy(x => x, StringComparer.Ordinal).ToArray()))
            .ToArray();

        var uncovered = allProductive
            .Where(sym => !productiveSymbols.ContainsKey(sym))
            .OrderBy(sym => sym, StringComparer.Ordinal)
            .ToArray();

        var denominator = covered.Length + uncovered.Length;
        var percent = denominator == 0 ? 0 : Math.Round(covered.Length * 100.0 / denominator, 1);

        return new TestReferenceMapDto(covered, uncovered, percent, scannedTestProjects, notes);
    }

    /// <summary>Recurse a namespace collecting every method symbol declared in source.</summary>
    private static void CollectProductiveSymbols(INamespaceSymbol ns, HashSet<string> accumulator)
    {
        foreach (var member in ns.GetMembers())
        {
            if (member is INamespaceSymbol childNs)
            {
                CollectProductiveSymbols(childNs, accumulator);
            }
            else if (member is INamedTypeSymbol type)
            {
                CollectProductiveSymbolsFromType(type, accumulator);
            }
        }
    }

    private static void CollectProductiveSymbolsFromType(INamedTypeSymbol type, HashSet<string> accumulator)
    {
        foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
        {
            if (method.MethodKind is not (MethodKind.Ordinary or MethodKind.Constructor)) continue;
            if (method.Locations.All(l => l.Kind == LocationKind.MetadataFile)) continue;
            if (method.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or Accessibility.Protected or Accessibility.ProtectedOrInternal))
                continue;
            accumulator.Add(method.OriginalDefinition.ToDisplayString());
        }
        foreach (var nested in type.GetTypeMembers())
        {
            CollectProductiveSymbolsFromType(nested, accumulator);
        }
    }
}

internal static class AssemblyProjectIdExtensions
{
    /// <summary>
    /// Roslyn exposes symbols' <c>ContainingAssembly</c> but not the owning <c>ProjectId</c>.
    /// Map by assembly name + solution project set; returns <see cref="ProjectId"/> default when
    /// the symbol is from metadata (BCL / NuGet) so <c>.Contains(default)</c> safely returns false.
    /// </summary>
    public static ProjectId GetProjectIdFromSolution(this IAssemblySymbol assembly, Solution solution)
    {
        var name = assembly.Identity.Name;
        foreach (var p in solution.Projects)
        {
            if (string.Equals(p.AssemblyName, name, StringComparison.Ordinal))
                return p.Id;
        }
        return default!;
    }
}
