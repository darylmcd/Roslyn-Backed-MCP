using System.Text.Json;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class WorkspaceToolsIntegrationTests : SharedWorkspaceTestBase
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
    public async Task WorkspaceTools_ListWorkspaces_Returns_Count_And_Id()
    {
        var json = await WorkspaceTools.ListWorkspaces(WorkspaceManager, verbose: false);
        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.GetProperty("count").GetInt32() >= 1);
        var workspaces = doc.RootElement.GetProperty("workspaces").EnumerateArray().ToList();
        Assert.IsTrue(workspaces.Any(w => w.GetProperty("workspaceId").GetString() == WorkspaceId));
    }

    [TestMethod]
    public async Task WorkspaceTools_GetWorkspaceStatus_Returns_Projects()
    {
        var json = await WorkspaceTools.GetWorkspaceStatus(
            WorkspaceExecutionGate,
            WorkspaceManager,
            WorkspaceId,
            verbose: false,
            CancellationToken.None);
        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.GetProperty("projectCount").GetInt32() >= 1);
    }

    [TestMethod]
    public async Task WorkspaceTools_GetWorkspaceStatus_Default_Is_Summary()
    {
        var json = await WorkspaceTools.GetWorkspaceStatus(
            WorkspaceExecutionGate,
            WorkspaceManager,
            WorkspaceId,
            verbose: false,
            CancellationToken.None);
        using var doc = JsonDocument.Parse(json);
        Assert.IsFalse(doc.RootElement.TryGetProperty("projects", out _),
            "Summary mode must NOT include the per-project tree.");
        Assert.IsTrue(doc.RootElement.TryGetProperty("workspaceDiagnosticCount", out _),
            "Summary mode must include the diagnostic count field.");
    }

    [TestMethod]
    public async Task WorkspaceTools_GetWorkspaceStatus_Verbose_Includes_Projects()
    {
        var json = await WorkspaceTools.GetWorkspaceStatus(
            WorkspaceExecutionGate,
            WorkspaceManager,
            WorkspaceId,
            verbose: true,
            CancellationToken.None);
        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.TryGetProperty("projects", out var projects),
            "Verbose mode must include the per-project tree.");
        Assert.IsTrue(projects.GetArrayLength() >= 1);
    }

    [TestMethod]
    public async Task WorkspaceTools_ListWorkspaces_Default_Is_Summary()
    {
        var json = await WorkspaceTools.ListWorkspaces(WorkspaceManager, verbose: false);
        using var doc = JsonDocument.Parse(json);
        var workspaces = doc.RootElement.GetProperty("workspaces").EnumerateArray().ToList();
        Assert.IsTrue(workspaces.Count >= 1);
        Assert.IsFalse(workspaces[0].TryGetProperty("projects", out _),
            "Summary mode must NOT include the per-project tree on each workspace.");
    }

    [TestMethod]
    public async Task WorkspaceTools_GetProjectGraph_Returns_Nodes()
    {
        var json = await WorkspaceTools.GetProjectGraph(
            WorkspaceExecutionGate,
            WorkspaceManager,
            WorkspaceId,
            CancellationToken.None);
        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.GetProperty("projects").GetArrayLength() >= 1);
    }

    // project-graph-missing-metadata-fields: every project node MUST advertise a non-empty
    // `name` and `filePath`. Pre-fix, projects with an empty-string Name (unusual but
    // observed in MSBuild evaluation races) emitted `name: ""` which broke downstream
    // project-lookup flows (scaffold_test_preview, DI resolution).
    [TestMethod]
    public async Task WorkspaceTools_GetProjectGraph_AllNodesHaveNonEmptyNameAndFilePath()
    {
        var json = await WorkspaceTools.GetProjectGraph(
            WorkspaceExecutionGate,
            WorkspaceManager,
            WorkspaceId,
            CancellationToken.None);
        using var doc = JsonDocument.Parse(json);
        var projects = doc.RootElement.GetProperty("projects").EnumerateArray().ToList();
        Assert.IsTrue(projects.Count >= 1);

        foreach (var project in projects)
        {
            var name = project.GetProperty("name").GetString();
            var filePath = project.GetProperty("filePath").GetString();
            Assert.IsFalse(string.IsNullOrWhiteSpace(name),
                $"ProjectGraphNodeDto.name must be non-empty; got '{name ?? "<null>"}'. Full node: {project}");
            Assert.IsFalse(string.IsNullOrWhiteSpace(filePath),
                $"ProjectGraphNodeDto.filePath must be non-empty; got '{filePath ?? "<null>"}'. Full node: {project}");
        }
    }

    [TestMethod]
    public async Task WorkspaceTools_GetSourceGeneratedDocuments_Returns_Array()
    {
        var json = await WorkspaceTools.GetSourceGeneratedDocuments(
            WorkspaceExecutionGate,
            WorkspaceManager,
            WorkspaceId,
            projectName: null,
            CancellationToken.None);
        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.ValueKind == JsonValueKind.Array);
    }

    [TestMethod]
    public async Task WorkspaceTools_GetSourceText_Returns_LineCount()
    {
        var programPath = FindProgramPath();
        var json = await WorkspaceTools.GetSourceText(
            WorkspaceExecutionGate,
            WorkspaceManager,
            WorkspaceId,
            programPath,
            startLine: null,
            endLine: null,
            ct: CancellationToken.None);
        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.GetProperty("totalLineCount").GetInt32() >= 1);
        Assert.IsTrue(doc.RootElement.TryGetProperty("text", out _));
    }

    [TestMethod]
    public async Task WorkspaceTools_GetSourceText_LineRange_ReturnsOnlyRequestedLines()
    {
        var programPath = FindProgramPath();
        // Slice the first 2 lines
        var json = await WorkspaceTools.GetSourceText(
            WorkspaceExecutionGate,
            WorkspaceManager,
            WorkspaceId,
            programPath,
            startLine: 1,
            endLine: 2,
            ct: CancellationToken.None);
        using var doc = JsonDocument.Parse(json);
        var text = doc.RootElement.GetProperty("text").GetString() ?? "";
        Assert.AreEqual(1, doc.RootElement.GetProperty("returnedStartLine").GetInt32());
        Assert.AreEqual(2, doc.RootElement.GetProperty("returnedEndLine").GetInt32());
        var lineCount = text.Split('\n').Length - (text.EndsWith('\n') ? 1 : 0);
        Assert.IsTrue(lineCount <= 2, $"Expected <=2 lines, got {lineCount}: {text}");
    }

    [TestMethod]
    public async Task WorkspaceTools_GetSourceText_EndPastEof_ClampsToFileEnd()
    {
        var programPath = FindProgramPath();
        var json = await WorkspaceTools.GetSourceText(
            WorkspaceExecutionGate,
            WorkspaceManager,
            WorkspaceId,
            programPath,
            startLine: 1,
            endLine: 99999,
            ct: CancellationToken.None);
        using var doc = JsonDocument.Parse(json);
        var total = doc.RootElement.GetProperty("totalLineCount").GetInt32();
        var returnedEnd = doc.RootElement.GetProperty("returnedEndLine").GetInt32();
        Assert.AreEqual(total, returnedEnd, "endLine should clamp to total line count, not error.");
        Assert.AreEqual(99999, doc.RootElement.GetProperty("requestedEndLine").GetInt32());
    }

    [TestMethod]
    public async Task WorkspaceTools_GetSourceText_InvertedRange_ReturnsInvalidArgument()
    {
        var programPath = FindProgramPath();
        var json = await WorkspaceTools.GetSourceText(
            WorkspaceExecutionGate,
            WorkspaceManager,
            WorkspaceId,
            programPath,
            startLine: 5,
            endLine: 2,
            ct: CancellationToken.None);
        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual("InvalidArgument", doc.RootElement.GetProperty("category").GetString());
        StringAssert.Contains(doc.RootElement.GetProperty("message").GetString()!, "<=");
    }

    [TestMethod]
    public async Task WorkspaceTools_GetSourceText_StartPastEof_ReturnsInvalidArgument()
    {
        var programPath = FindProgramPath();
        var json = await WorkspaceTools.GetSourceText(
            WorkspaceExecutionGate,
            WorkspaceManager,
            WorkspaceId,
            programPath,
            startLine: 99999,
            endLine: 99999,
            ct: CancellationToken.None);
        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual("InvalidArgument", doc.RootElement.GetProperty("category").GetString());
    }

    [TestMethod]
    public async Task WorkspaceTools_GetSourceText_MaxCharsCap_TruncatesWithMarker()
    {
        var programPath = FindProgramPath();
        var json = await WorkspaceTools.GetSourceText(
            WorkspaceExecutionGate,
            WorkspaceManager,
            WorkspaceId,
            programPath,
            startLine: null,
            endLine: null,
            maxChars: 16,
            ct: CancellationToken.None);
        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.GetProperty("truncated").GetBoolean(),
            "Should be truncated since maxChars=16 is well below file size.");
        var text = doc.RootElement.GetProperty("text").GetString() ?? "";
        StringAssert.Contains(text, "[TRUNCATED");
    }

    [TestMethod]
    public async Task WorkspaceExecutionGate_Rejects_Unknown_Workspace_Id()
    {
        await Assert.ThrowsExceptionAsync<KeyNotFoundException>(() =>
            WorkspaceExecutionGate.RunReadAsync(
                "ffffffffffffffffffffffffffffffff",
                _ => Task.FromResult("x"),
                CancellationToken.None));
    }

    private static string FindProgramPath()
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var path = solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => string.Equals(d.Name, "Program.cs", StringComparison.Ordinal))?.FilePath;
        return path ?? throw new AssertFailedException("Program.cs not found.");
    }
}
