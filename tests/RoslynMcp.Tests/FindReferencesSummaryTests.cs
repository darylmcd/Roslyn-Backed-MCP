using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression for `find-references-preview-text-inflates-response` (P3): Jellyfin stress
/// test 2026-04-15 §7 reproduced `find_references(IUserManager, limit=500)` returning
/// ~154 KB on 233 refs because per-ref preview text dominated the payload — `limit`
/// capped the ref count but not the response size. The new `summary: bool = false`
/// parameter on <see cref="IReferenceService.FindReferencesAsync"/> drops PreviewText
/// from each LocationDto when set; structural fields (filePath, line, column,
/// classification) stay populated.
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class FindReferencesSummaryTests : SharedWorkspaceTestBase
{
    private static string WorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        WorkspaceId = await GetOrLoadWorkspaceIdAsync(SampleSolutionPath, CancellationToken.None);
    }

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task FindReferences_DefaultMode_PopulatesPreviewText()
    {
        // Anchor on a known-referenced symbol in the SampleSolution. `IAnimal.Speak()`
        // is used by every animal class — multiple references guaranteed.
        var animalDoc = WorkspaceManager.GetCurrentSolution(WorkspaceId)
            .Projects.SelectMany(p => p.Documents)
            .FirstOrDefault(d => d.Name == "IAnimal.cs");
        Assert.IsNotNull(animalDoc, "IAnimal.cs not found in SampleSolution");

        var locator = SymbolLocator.BySource(animalDoc.FilePath!, line: 5, column: 10); // `IAnimal` interface name
        var refs = await ReferenceService.FindReferencesAsync(WorkspaceId, locator, CancellationToken.None, summary: false);

        Assert.IsTrue(refs.Count >= 1, $"Expected at least 1 reference, got {refs.Count}");
        Assert.IsTrue(refs.Any(r => !string.IsNullOrEmpty(r.PreviewText)),
            "Default summary=false mode must populate PreviewText on at least one ref");
    }

    [TestMethod]
    public async Task FindReferences_SummaryMode_DropsPreviewTextButKeepsStructuralFields()
    {
        var animalDoc = WorkspaceManager.GetCurrentSolution(WorkspaceId)
            .Projects.SelectMany(p => p.Documents)
            .FirstOrDefault(d => d.Name == "IAnimal.cs");
        Assert.IsNotNull(animalDoc, "IAnimal.cs not found in SampleSolution");

        var locator = SymbolLocator.BySource(animalDoc.FilePath!, line: 5, column: 10);
        var summaryRefs = await ReferenceService.FindReferencesAsync(WorkspaceId, locator, CancellationToken.None, summary: true);

        Assert.IsTrue(summaryRefs.Count >= 1, $"Expected at least 1 reference in summary mode, got {summaryRefs.Count}");
        foreach (var loc in summaryRefs)
        {
            Assert.IsNull(loc.PreviewText,
                $"summary=true must produce null PreviewText (got '{loc.PreviewText}' for {loc.FilePath}:{loc.StartLine})");
            Assert.IsFalse(string.IsNullOrWhiteSpace(loc.FilePath), "FilePath must remain populated in summary mode");
            Assert.IsTrue(loc.StartLine > 0, "StartLine must remain populated in summary mode");
            Assert.IsTrue(loc.StartColumn > 0, "StartColumn must remain populated in summary mode");
        }
    }

    [TestMethod]
    public async Task FindReferences_SummaryMode_ReducesResponseSize()
    {
        var animalDoc = WorkspaceManager.GetCurrentSolution(WorkspaceId)
            .Projects.SelectMany(p => p.Documents)
            .FirstOrDefault(d => d.Name == "IAnimal.cs");
        Assert.IsNotNull(animalDoc, "IAnimal.cs not found in SampleSolution");

        var locator = SymbolLocator.BySource(animalDoc.FilePath!, line: 5, column: 10);
        var fullRefs = await ReferenceService.FindReferencesAsync(WorkspaceId, locator, CancellationToken.None, summary: false);
        var summaryRefs = await ReferenceService.FindReferencesAsync(WorkspaceId, locator, CancellationToken.None, summary: true);

        Assert.AreEqual(fullRefs.Count, summaryRefs.Count, "summary mode must return the same ref count as full mode");

        // Crude size comparison via System.Text.Json.JsonSerializer — summary serialization
        // must be smaller than full serialization. The actual byte ratio depends on whether
        // refs in SampleSolution have non-empty preview text; just assert summary <= full.
        var fullJson = System.Text.Json.JsonSerializer.Serialize(fullRefs);
        var summaryJson = System.Text.Json.JsonSerializer.Serialize(summaryRefs);
        Assert.IsTrue(summaryJson.Length <= fullJson.Length,
            $"summary JSON must not exceed full JSON; summary={summaryJson.Length}, full={fullJson.Length}");
    }
}
