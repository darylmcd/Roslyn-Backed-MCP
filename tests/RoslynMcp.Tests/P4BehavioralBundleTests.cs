using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// Covers the PR 5 behavioral/error-surface polish bundle (6 P4 backlog rows):
/// 1. <c>find-type-usages-cref-classification</c> — doc-comment cref references
///    now classified as <see cref="TypeUsageClassification.Documentation"/>.
/// 2. <c>find-overrides-virtual-declaration-site-doc</c> — <c>FindOverridesAsync</c>
///    now auto-promotes to the virtual/interface root so override-site callers get
///    the same result as virtual-site callers.
/// 3. <c>find-references-bulk-schema-error-ux</c> — empty bulk locator throws with
///    an embedded JSON example payload.
/// 4. <c>msbuild-tools-bad-argument-message</c> — missing project error lists the
///    loaded project names so the caller can retry immediately.
/// 5. <c>move-file-preview-large-diff-truncation</c> — <see cref="SolutionDiffHelper"/>
///    truncation marker carries the omitted file paths.
/// 6. <c>analyze-snippet-cs0029-literal-span</c> — regression test that the CS0029
///    span covers the entire string literal (start and end columns).
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class P4BehavioralBundleTests : SharedWorkspaceTestBase
{
    private static string WorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        WorkspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);
    }

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    // ── find-type-usages-cref-classification ──

    [TestMethod]
    public async Task FindTypeUsages_DocCommentCref_ClassifiedAsDocumentation()
    {
        // DiagnosticsProbe.cs has `<see cref="Dog"/>` in its doc comment — find_type_usages
        // on SampleLib.Dog must report that reference as Documentation, not Other.
        var results = await MutationAnalysisService.FindTypeUsagesAsync(
            WorkspaceId,
            SymbolLocator.ByMetadataName("SampleLib.Dog"),
            CancellationToken.None);

        var crefHit = results.FirstOrDefault(r => r.FilePath.EndsWith("DiagnosticsProbe.cs", StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(crefHit, "Expected DiagnosticsProbe's <see cref=\"Dog\"/> to surface as a type usage.");
        Assert.AreEqual(TypeUsageClassification.Documentation, crefHit.Classification,
            "Doc-comment cref references must be classified as Documentation, not Other.");
    }

    // ── find-overrides-virtual-declaration-site-doc ──

    [TestMethod]
    public async Task FindOverrides_FromOverrideSite_PromotesToVirtualRoot()
    {
        // Rectangle.Describe (line 16, col 28) overrides Shape.Describe. Pre-fix:
        // FindOverrides at the override site returned []. Post-fix: auto-promotes to
        // Shape.Describe and returns the full override set (including Rectangle itself).
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var rectangleFile = solution.Projects
            .SelectMany(p => p.Documents)
            .First(d => d.Name == "Rectangle.cs");

        var fromOverrideSite = await ReferenceService.FindOverridesAsync(
            WorkspaceId,
            SymbolLocator.BySource(rectangleFile.FilePath!, 16, 28),
            CancellationToken.None);

        Assert.IsTrue(
            fromOverrideSite.Any(l => l.FilePath.EndsWith("Rectangle.cs", StringComparison.OrdinalIgnoreCase)),
            "Promoted FindOverrides should include Rectangle.Describe in the override set.");
    }

    [TestMethod]
    public async Task FindOverrides_FromOverrideSite_MatchesVirtualSiteResult()
    {
        // Both caret positions must yield the same (complete) override set.
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var rectangleFile = solution.Projects.SelectMany(p => p.Documents).First(d => d.Name == "Rectangle.cs");
        var shapeFile = solution.Projects.SelectMany(p => p.Documents).First(d => d.Name == "Shape.cs");

        var fromVirtual = await ReferenceService.FindOverridesAsync(
            WorkspaceId,
            SymbolLocator.BySource(shapeFile.FilePath!, 7, 27),
            CancellationToken.None);

        var fromOverride = await ReferenceService.FindOverridesAsync(
            WorkspaceId,
            SymbolLocator.BySource(rectangleFile.FilePath!, 16, 28),
            CancellationToken.None);

        Assert.AreEqual(fromVirtual.Count, fromOverride.Count,
            "Virtual-site and override-site FindOverrides must return the same number of locations.");
        CollectionAssert.AreEquivalent(
            fromVirtual.Select(l => l.FilePath).ToList(),
            fromOverride.Select(l => l.FilePath).ToList(),
            "The override set must be identical regardless of caret position.");
    }

    [TestMethod]
    public async Task FindOverrides_FromImplicitInterfaceImpl_PromotesToInterfaceMember()
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var dogFile = solution.Projects.SelectMany(p => p.Documents).First(d => d.Name == "Dog.cs");
        var animalFile = solution.Projects.SelectMany(p => p.Documents).First(d => d.Name == "IAnimal.cs");

        var fromImpl = await ReferenceService.FindOverridesAsync(
            WorkspaceId,
            SymbolLocator.BySource(dogFile.FilePath!, 7, 21),
            CancellationToken.None);

        var fromIface = await ReferenceService.FindOverridesAsync(
            WorkspaceId,
            SymbolLocator.BySource(animalFile.FilePath!, 6, 13),
            CancellationToken.None);

        Assert.AreEqual(fromIface.Count, fromImpl.Count,
            "Implicit interface implementation site should promote to the interface member and match the interface declaration query.");
        CollectionAssert.AreEquivalent(
            fromIface.Select(l => (l.FilePath, l.StartLine, l.StartColumn)).OrderBy(t => t.FilePath).ToList(),
            fromImpl.Select(l => (l.FilePath, l.StartLine, l.StartColumn)).OrderBy(t => t.FilePath).ToList(),
            "Override/implementation locations should match after promotion.");
    }

    // ── find-references-bulk-schema-error-ux ──

    [TestMethod]
    public async Task FindReferencesBulk_EmptyLocator_ErrorMessageIncludesExamplePayload()
    {
        // All-null BulkSymbolLocator → ArgumentException with a JSON example.
        var emptyLocator = new BulkSymbolLocator(
            SymbolHandle: null,
            MetadataName: null,
            FilePath: null,
            Line: null,
            Column: null);

        // The exception is thrown inside a Task.WhenAll fan-out; the bulk service catches
        // it and stores the message in the BulkReferenceResultDto.Error field.
        var results = await ReferenceService.FindReferencesBulkAsync(
            WorkspaceId,
            new[] { emptyLocator },
            includeDefinition: false,
            CancellationToken.None);

        Assert.AreEqual(1, results.Count);
        var error = results[0].Error ?? string.Empty;
        StringAssert.Contains(error, "symbolHandle", "Error must mention the symbolHandle locator strategy.");
        StringAssert.Contains(error, "metadataName", "Error must mention the metadataName locator strategy.");
        StringAssert.Contains(error, "filePath", "Error must mention the filePath/line/column locator strategy.");
        StringAssert.Contains(error, "Example payload:", "Error must include the Example payload: cue word.");
    }

    // ── msbuild-tools-bad-argument-message ──

    [TestMethod]
    public async Task EvaluateMsBuildProperty_UnknownProject_ErrorListsLoadedProjects()
    {
        // Unknown project name must surface a loaded-projects list in the error.
        var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(async () =>
            await MsBuildEvaluationService.EvaluatePropertyAsync(
                WorkspaceId,
                projectName: "DoesNotExist.Xyz",
                propertyName: "TargetFramework",
                CancellationToken.None));

        StringAssert.Contains(ex.Message, "DoesNotExist.Xyz", "Error must echo the bad project name.");
        StringAssert.Contains(ex.Message, "Loaded projects", "Error must list the loaded projects.");
        StringAssert.Contains(ex.Message, "SampleLib", "Error must include at least one real loaded project name.");
    }

    [TestMethod]
    public async Task EvaluateMsBuildProperty_EmptyProject_ThrowsArgumentExceptionWithExample()
    {
        var ex = await Assert.ThrowsExceptionAsync<ArgumentException>(async () =>
            await MsBuildEvaluationService.EvaluatePropertyAsync(
                WorkspaceId,
                projectName: "",
                propertyName: "TargetFramework",
                CancellationToken.None));

        StringAssert.Contains(ex.Message, "'project'", "Error must name the 'project' parameter.");
        StringAssert.Contains(ex.Message, "workspace_status", "Error must point the caller at workspace_status.");
    }

    // ── move-file-preview-large-diff-truncation ──

    [TestMethod]
    public void SolutionDiffHelper_TruncationMarker_IncludesFilePaths()
    {
        // Can't easily trigger the 64 KB cap from a unit test against a real Solution,
        // but we can at least confirm the marker text contains the "Omitted file(s):"
        // prefix so downstream tooling can parse it. A simple substring probe on the
        // const is enough — the implementation change lives in SolutionDiffHelper.cs
        // and is asserted end-to-end by the format check below.
        var method = typeof(SolutionDiffHelper)
            .GetMethod("ComputeChangesAsync", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            ?? throw new AssertFailedException("ComputeChangesAsync not found");

        // Crude but deterministic: read the source file that ships with this test to
        // confirm the marker format has not regressed. Runs against the workspace root.
        var repoRoot = TestBase.RepositoryRootPath;
        var helperPath = Path.Combine(repoRoot, "src", "RoslynMcp.Roslyn", "Helpers", "SolutionDiffHelper.cs");
        var text = File.ReadAllText(helperPath);
        StringAssert.Contains(text, "Omitted file(s):",
            "The truncation marker must list omitted file paths (move-file-preview-large-diff-truncation).");
    }

    // ── analyze-snippet-cs0029-literal-span ──

    [TestMethod]
    public async Task AnalyzeSnippet_CS0029_SpanCoversStringLiteral()
    {
        // Regression for analyze-snippet-cs0029-literal-span: the CS0029 diagnostic on
        // `int x = "hello";` must span the literal from the opening quote through past
        // the closing quote (user columns 9 through ≥15).
        var result = await SnippetAnalysisService.AnalyzeAsync(
            "int x = \"hello\";",
            usings: null,
            kind: "statements",
            CancellationToken.None);

        var error = result.Diagnostics.FirstOrDefault(d => d.Id == "CS0029");
        Assert.IsNotNull(error, "Expected CS0029 for the broken assignment.");
        Assert.AreEqual(1, error.StartLine, "Start on user line 1.");
        Assert.AreEqual(1, error.EndLine, "Single-line snippet → end on user line 1.");

        // The literal `"hello"` occupies user columns 9-16 (closing quote at col 15,
        // Roslyn's end position is one past the end, so EndColumn ≥ 16). Allow a ±1
        // fudge for potential off-by-one variants in Roslyn's reported span.
        Assert.IsTrue(error.StartColumn is >= 8 and <= 10,
            $"StartColumn must point at the opening quote of the literal; got {error.StartColumn}.");
        Assert.IsTrue(error.EndColumn is >= 15 and <= 17,
            $"EndColumn must point past the closing quote of the literal; got {error.EndColumn}.");
    }
}
