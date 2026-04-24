using System.Text.Json;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

/// <summary>
/// Covers the <c>AnalysisTools</c> MCP tool surface. Focus is tool-wrapper behavior
/// (parameter shape, aliasing, error envelopes) — service-level semantics are
/// exercised by <see cref="DiagnosticFixIntegrationTests"/>.
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class AnalysisToolsTests : SharedWorkspaceTestBase
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

    private static string FindDocumentPath(string name)
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        return solution.Projects
            .SelectMany(project => project.Documents)
            .First(document => document.Name == name).FilePath!;
    }

    [TestMethod]
    public async Task DiagnosticDetails_Accepts_LineColumn_And_StartLineStartColumn_Identically()
    {
        // diagnostic-details-param-naming-drift: `diagnostic_details` historically required
        // `line`/`column` while every other positional tool uses `startLine`/`startColumn`.
        // The tool should now accept either naming convention with identical results.
        var animalServicePath = FindDocumentPath("AnimalService.cs");

        var legacyJson = await AnalysisTools.GetDiagnosticDetails(
            server: null!,
            gate: WorkspaceExecutionGate,
            diagnosticService: DiagnosticService,
            workspaceId: WorkspaceId,
            diagnosticId: "CS8019",
            filePath: animalServicePath,
            line: 1,
            column: 1,
            startLine: null,
            startColumn: null,
            ct: CancellationToken.None);

        var aliasJson = await AnalysisTools.GetDiagnosticDetails(
            server: null!,
            gate: WorkspaceExecutionGate,
            diagnosticService: DiagnosticService,
            workspaceId: WorkspaceId,
            diagnosticId: "CS8019",
            filePath: animalServicePath,
            line: null,
            column: null,
            startLine: 1,
            startColumn: 1,
            ct: CancellationToken.None);

        Assert.AreEqual(legacyJson, aliasJson,
            "diagnostic_details must produce identical output for line/column vs startLine/startColumn.");

        using var doc = JsonDocument.Parse(legacyJson);
        // Confirms the call actually resolved a diagnostic (not a not-found envelope).
        Assert.IsTrue(doc.RootElement.TryGetProperty("diagnostic", out _),
            $"Expected a resolved diagnostic, got: {legacyJson}");
    }

    [TestMethod]
    public async Task DiagnosticDetails_Accepts_Matching_Values_From_Both_Alias_Names()
    {
        // When both alias forms are supplied with matching values, treat as a single position
        // rather than a conflict — callers that are unsure which name the tool accepts may
        // redundantly send both.
        var animalServicePath = FindDocumentPath("AnimalService.cs");

        var bothJson = await AnalysisTools.GetDiagnosticDetails(
            server: null!,
            gate: WorkspaceExecutionGate,
            diagnosticService: DiagnosticService,
            workspaceId: WorkspaceId,
            diagnosticId: "CS8019",
            filePath: animalServicePath,
            line: 1,
            column: 1,
            startLine: 1,
            startColumn: 1,
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(bothJson);
        Assert.IsTrue(doc.RootElement.TryGetProperty("diagnostic", out _),
            $"Expected a resolved diagnostic, got: {bothJson}");
    }

    [TestMethod]
    public async Task DiagnosticDetails_Rejects_Missing_Position()
    {
        var animalServicePath = FindDocumentPath("AnimalService.cs");

        await Assert.ThrowsExceptionAsync<ArgumentException>(async () =>
        {
            await AnalysisTools.GetDiagnosticDetails(
                server: null!,
                gate: WorkspaceExecutionGate,
                diagnosticService: DiagnosticService,
                workspaceId: WorkspaceId,
                diagnosticId: "CS8019",
                filePath: animalServicePath,
                line: null,
                column: null,
                startLine: null,
                startColumn: null,
                ct: CancellationToken.None);
        });
    }

    [TestMethod]
    public async Task DiagnosticDetails_Rejects_Conflicting_Alias_Values()
    {
        var animalServicePath = FindDocumentPath("AnimalService.cs");

        await Assert.ThrowsExceptionAsync<ArgumentException>(async () =>
        {
            await AnalysisTools.GetDiagnosticDetails(
                server: null!,
                gate: WorkspaceExecutionGate,
                diagnosticService: DiagnosticService,
                workspaceId: WorkspaceId,
                diagnosticId: "CS8019",
                filePath: animalServicePath,
                line: 1,
                column: 1,
                startLine: 5,
                startColumn: 1,
                ct: CancellationToken.None);
        });
    }

    // impact-analysis-summary-mode: parity with find_references(summary=true). The wrapper
    // gains `summary: bool` and we already had `declarationsLimit: int` since FLAG-3D —
    // these tests pin the contract: summary=true drops the per-ref / per-decl arrays and
    // returns only counts; non-summary continues to honor declarationsLimit with hasMore.
    [TestMethod]
    public async Task ImpactAnalysis_DefaultMode_ReturnsReferencesAndDeclarationsArrays()
    {
        var animalPath = FindDocumentPath("IAnimal.cs");

        var json = await AnalysisTools.AnalyzeImpact(
            gate: WorkspaceExecutionGate,
            mutationAnalysisService: MutationAnalysisService,
            workspaceId: WorkspaceId,
            filePath: animalPath,
            line: 6,
            column: 12,
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.IsTrue(root.TryGetProperty("directReferences", out var refs),
            $"Default mode must include directReferences. Got: {json}");
        Assert.AreEqual(JsonValueKind.Array, refs.ValueKind, "directReferences must be an array");
        Assert.IsTrue(root.TryGetProperty("affectedDeclarations", out var decls),
            "Default mode must include affectedDeclarations.");
        Assert.AreEqual(JsonValueKind.Array, decls.ValueKind, "affectedDeclarations must be an array");
        Assert.IsTrue(root.TryGetProperty("totalDirectReferences", out _),
            "Default mode must include totalDirectReferences.");
        Assert.IsTrue(root.TryGetProperty("totalAffectedDeclarations", out _),
            "Default mode must include totalAffectedDeclarations.");
    }

    [TestMethod]
    public async Task ImpactAnalysis_SummaryMode_OmitsArraysAndKeepsCounts()
    {
        var animalPath = FindDocumentPath("IAnimal.cs");

        var json = await AnalysisTools.AnalyzeImpact(
            gate: WorkspaceExecutionGate,
            mutationAnalysisService: MutationAnalysisService,
            workspaceId: WorkspaceId,
            filePath: animalPath,
            line: 6,
            column: 12,
            summary: true,
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Summary mode drops the heavy arrays — counts and the cheap project list stay.
        Assert.IsFalse(root.TryGetProperty("directReferences", out _),
            $"summary=true must omit directReferences. Got: {json}");
        Assert.IsFalse(root.TryGetProperty("affectedDeclarations", out _),
            "summary=true must omit affectedDeclarations.");

        Assert.IsTrue(root.TryGetProperty("targetSymbol", out _),
            "summary=true must keep targetSymbol.");
        Assert.IsTrue(root.TryGetProperty("affectedProjects", out var projects)
                       && projects.ValueKind == JsonValueKind.Array,
            "summary=true must keep affectedProjects array.");
        Assert.IsTrue(root.TryGetProperty("totalDirectReferences", out var totalRefs)
                       && totalRefs.GetInt32() >= 0,
            "summary=true must keep totalDirectReferences.");
        Assert.IsTrue(root.TryGetProperty("totalAffectedDeclarations", out var totalDecls)
                       && totalDecls.GetInt32() >= 0,
            "summary=true must keep totalAffectedDeclarations.");
        Assert.IsTrue(root.TryGetProperty("hasMoreReferences", out _),
            "summary=true must keep hasMoreReferences flag.");
        Assert.IsTrue(root.TryGetProperty("hasMoreDeclarations", out _),
            "summary=true must keep hasMoreDeclarations flag.");
        Assert.IsTrue(root.TryGetProperty("summaryMode", out var mode)
                       && mode.GetBoolean(),
            "summary=true must set summaryMode:true so callers can detect the shape.");
    }

    [TestMethod]
    public async Task ImpactAnalysis_SummaryMode_ProducesSmallerPayloadThanDefault()
    {
        var animalPath = FindDocumentPath("IAnimal.cs");

        var fullJson = await AnalysisTools.AnalyzeImpact(
            gate: WorkspaceExecutionGate,
            mutationAnalysisService: MutationAnalysisService,
            workspaceId: WorkspaceId,
            filePath: animalPath,
            line: 6,
            column: 12,
            summary: false,
            ct: CancellationToken.None);

        var summaryJson = await AnalysisTools.AnalyzeImpact(
            gate: WorkspaceExecutionGate,
            mutationAnalysisService: MutationAnalysisService,
            workspaceId: WorkspaceId,
            filePath: animalPath,
            line: 6,
            column: 12,
            summary: true,
            ct: CancellationToken.None);

        // Summary mode strips the per-ref / per-decl arrays, so it must be no larger than
        // the full envelope. On non-trivial symbols it's strictly smaller; on a symbol
        // with zero references the two can converge — assert <= rather than <.
        Assert.IsTrue(summaryJson.Length <= fullJson.Length,
            $"summary JSON must not exceed full JSON; summary={summaryJson.Length}, full={fullJson.Length}");
    }

    [TestMethod]
    public async Task ImpactAnalysis_NonSummary_HonorsDeclarationsLimit_WithHasMore()
    {
        var animalPath = FindDocumentPath("IAnimal.cs");

        // First, learn the unbounded declaration count using a generous limit. We pick a
        // symbol with broad reach (the IAnimal interface — every animal class implements
        // it) so a limit of 1 is overwhelmingly likely to trip hasMoreDeclarations.
        var generousJson = await AnalysisTools.AnalyzeImpact(
            gate: WorkspaceExecutionGate,
            mutationAnalysisService: MutationAnalysisService,
            workspaceId: WorkspaceId,
            filePath: animalPath,
            line: 6,
            column: 12,
            declarationsLimit: 1000,
            ct: CancellationToken.None);

        using var generousDoc = JsonDocument.Parse(generousJson);
        var totalDecls = generousDoc.RootElement.GetProperty("totalAffectedDeclarations").GetInt32();
        if (totalDecls < 2)
        {
            // Fixture moved on us — without >= 2 declarations the hasMore assertion is
            // meaningless. Fail loudly so the test gets re-anchored rather than silently
            // becoming a no-op.
            Assert.Inconclusive($"Test fixture changed: IAnimal now has {totalDecls} affected declarations; need >= 2 to exercise declarationsLimit paging.");
        }

        var clampedJson = await AnalysisTools.AnalyzeImpact(
            gate: WorkspaceExecutionGate,
            mutationAnalysisService: MutationAnalysisService,
            workspaceId: WorkspaceId,
            filePath: animalPath,
            line: 6,
            column: 12,
            declarationsLimit: 1,
            ct: CancellationToken.None);

        using var clampedDoc = JsonDocument.Parse(clampedJson);
        var clampedRoot = clampedDoc.RootElement;
        Assert.AreEqual(1, clampedRoot.GetProperty("affectedDeclarations").GetArrayLength(),
            "declarationsLimit=1 must clamp the returned array to 1.");
        Assert.IsTrue(clampedRoot.GetProperty("hasMoreDeclarations").GetBoolean(),
            "declarationsLimit=1 with totalAffectedDeclarations>=2 must set hasMoreDeclarations:true.");
        Assert.AreEqual(totalDecls, clampedRoot.GetProperty("totalAffectedDeclarations").GetInt32(),
            "totalAffectedDeclarations must reflect the unpaged total regardless of limit.");
    }

    [TestMethod]
    public async Task ImpactAnalysis_RejectsInvalidDeclarationsLimit()
    {
        var animalPath = FindDocumentPath("IAnimal.cs");

        await Assert.ThrowsExceptionAsync<ArgumentException>(async () =>
        {
            await AnalysisTools.AnalyzeImpact(
                gate: WorkspaceExecutionGate,
                mutationAnalysisService: MutationAnalysisService,
                workspaceId: WorkspaceId,
                filePath: animalPath,
                line: 6,
                column: 12,
                declarationsLimit: 0,
                ct: CancellationToken.None);
        });
    }
}
