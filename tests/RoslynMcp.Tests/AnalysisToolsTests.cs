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
}
