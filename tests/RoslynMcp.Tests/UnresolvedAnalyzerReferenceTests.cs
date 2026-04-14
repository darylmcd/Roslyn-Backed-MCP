using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Verifies the unresolved-analyzer-reference-crash fix: an UnresolvedAnalyzerReference in any
/// project must be stripped at workspace_load so SymbolFinder, Compilation.GetDiagnostics, and
/// related Roslyn-internal switches over AnalyzerReference subtypes never throw
/// <c>InvalidOperationException("Unexpected value 'UnresolvedAnalyzerReference'")</c>.
///
/// The 6 tools previously blocked were find_unused_symbols, type_hierarchy, find_implementations,
/// member_hierarchy, impact_analysis, and suggest_refactorings — the test below exercises the
/// load path and verifies the WORKSPACE_UNRESOLVED_ANALYZER warning is surfaced through
/// workspace_status so callers can still see what was stripped.
/// </summary>
[TestClass]
public sealed class UnresolvedAnalyzerReferenceTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        InitializeServices();
    }

    [TestMethod]
    public async Task WorkspaceLoad_WithUnresolvedAnalyzerRef_StripsAndWarns()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        InjectUnresolvedAnalyzerReference(workspace.GetPath("SampleLib", "SampleLib.csproj"));

        var workspaceId = await workspace.LoadAsync();

        var status = await WorkspaceManager.GetStatusAsync(workspaceId, CancellationToken.None);
        var warning = status.WorkspaceDiagnostics
            .FirstOrDefault(d => d.Id == "WORKSPACE_UNRESOLVED_ANALYZER");

        Assert.IsNotNull(warning, "Expected a WORKSPACE_UNRESOLVED_ANALYZER warning to surface in workspace_status.");
        Assert.AreEqual("Warning", warning.Severity);
        StringAssert.Contains(warning.Message, "SampleLib");
        StringAssert.Contains(warning.Message, "DefinitelyMissingAnalyzer.dll");

        // Confirm the project's AnalyzerReferences no longer contains an UnresolvedAnalyzerReference
        var solution = WorkspaceManager.GetCurrentSolution(workspaceId);
        var sampleLib = solution.Projects.First(p => p.Name == "SampleLib");
        Assert.IsFalse(
            sampleLib.AnalyzerReferences.Any(r => r is Microsoft.CodeAnalysis.Diagnostics.UnresolvedAnalyzerReference),
            "UnresolvedAnalyzerReference must be stripped from project state after load.");
    }

    [TestMethod]
    public async Task FindUnusedSymbols_OnSolutionWithUnresolvedAnalyzerRef_DoesNotCrash()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        InjectUnresolvedAnalyzerReference(workspace.GetPath("SampleLib", "SampleLib.csproj"));

        var workspaceId = await workspace.LoadAsync();

        // Pre-fix: would throw InvalidOperationException("Unexpected value 'UnresolvedAnalyzerReference'").
        // Post-fix: returns a result list, even if empty.
        var result = await UnusedCodeAnalyzer.FindUnusedSymbolsAsync(
            workspaceId,
            new UnusedSymbolsAnalysisOptions
            {
                Limit = 50,
                ExcludeConventionInvoked = false,
            },
            CancellationToken.None);

        Assert.IsNotNull(result, "find_unused_symbols must return a result list, not throw.");
    }

    [TestMethod]
    public async Task TypeHierarchy_OnSolutionWithUnresolvedAnalyzerRef_DoesNotCrash()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        InjectUnresolvedAnalyzerReference(workspace.GetPath("SampleLib", "SampleLib.csproj"));

        var workspaceId = await workspace.LoadAsync();

        var animalSearch = await SymbolSearchService.SearchSymbolsAsync(
            workspaceId, "IAnimal", projectFilter: null, kindFilter: "Interface", namespaceFilter: null, limit: 5, CancellationToken.None);
        var iAnimal = animalSearch.FirstOrDefault();
        Assert.IsNotNull(iAnimal, "Sample fixture must contain IAnimal symbol.");

        var locator = SymbolLocator.ByMetadataName("SampleLib.IAnimal");
        var hierarchy = await SymbolRelationshipService.GetTypeHierarchyAsync(
            workspaceId, locator, CancellationToken.None);

        Assert.IsNotNull(hierarchy, "type_hierarchy must return a result, not throw, when an unresolved analyzer ref is present.");
    }

    private static void InjectUnresolvedAnalyzerReference(string csprojPath)
    {
        // MSBuildWorkspace turns <Analyzer Include="…"/> entries into UnresolvedAnalyzerReference
        // when the path does not resolve to an existing file. Inject one such entry into the
        // project file so the load reproduces the original crash trigger.
        var content = File.ReadAllText(csprojPath);
        var unresolvedAnalyzerSnippet = """
              <ItemGroup>
                <Analyzer Include="DefinitelyMissingAnalyzer.dll" />
              </ItemGroup>
            </Project>
            """;

        var newContent = content.Replace("</Project>", unresolvedAnalyzerSnippet);
        File.WriteAllText(csprojPath, newContent);
    }
}
