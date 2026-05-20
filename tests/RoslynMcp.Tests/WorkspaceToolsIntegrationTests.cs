using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using RoslynMcp.Host.Stdio.Resources;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[DoNotParallelize]
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

    // source-file-lines-off-by-one-marker-count: regression for gh #769 §13.26.
    // Pre-fix, `WorkspaceTools.GetSourceText` computed `totalLineCount` inline as
    // `text.Count('\n') + 1`, which over-counted files ending with a trailing newline
    // by one line. The `source_file_lines` resource correctly used
    // `SourceTextSlicer.CountLines(text)` (which subtracts 1 when the last char is '\n').
    // The two surfaces therefore disagreed on N for newline-terminated files — e.g. on
    // Program.cs (43 lines per `wc -l`, ends with '\n'), `get_source_text.totalLineCount`
    // returned 44 while the resource marker printed `of 43`. The fix routes both through
    // `SourceTextSlicer.CountLines` so the counts agree.
    [TestMethod]
    public async Task WorkspaceTools_GetSourceText_TotalLineCount_MatchesResourceMarker()
    {
        var programPath = FindProgramPath();

        // Tool: read the JSON envelope and grab totalLineCount.
        var toolJson = await WorkspaceTools.GetSourceText(
            WorkspaceExecutionGate,
            WorkspaceManager,
            WorkspaceId,
            programPath,
            startLine: null,
            endLine: null,
            ct: CancellationToken.None);
        using var doc = JsonDocument.Parse(toolJson);
        var toolTotal = doc.RootElement.GetProperty("totalLineCount").GetInt32();

        // Resource: read the marker-prefixed body and parse the `of N` suffix on the marker line.
        // Marker shape (see WorkspaceResources.GetSourceFileLines):
        //   // roslyn://workspace/{workspaceId}/file/.../lines/{startLine}-{clampedEnd} of {totalLineCount}
        var resourceBody = await WorkspaceResources.GetSourceFileLines(
            WorkspaceExecutionGate,
            WorkspaceManager,
            WorkspaceId,
            programPath,
            "1-1",
            CancellationToken.None);
        var match = Regex.Match(resourceBody, @"of (?<n>\d+)");
        Assert.IsTrue(match.Success, $"Resource marker must include 'of N'; got: {resourceBody}");
        var resourceTotal = int.Parse(match.Groups["n"].Value);

        Assert.AreEqual(
            resourceTotal,
            toolTotal,
            $"get_source_text.totalLineCount must match the source_file_lines resource marker " +
            $"(file: {programPath}). Tool reported {toolTotal}, resource reported {resourceTotal}.");
    }

    [TestMethod]
    public async Task WorkspaceTools_GetSourceText_InvertedRange_ReturnsInvalidArgument()
    {
        var programPath = FindProgramPath();
        var json = await ToolExecutionTestHarness.RunAsync(
            "get_source_text",
            () => WorkspaceTools.GetSourceText(
                WorkspaceExecutionGate,
                WorkspaceManager,
                WorkspaceId,
                programPath,
                startLine: 5,
                endLine: 2,
                ct: CancellationToken.None));
        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual("InvalidArgument", doc.RootElement.GetProperty("category").GetString());
        StringAssert.Contains(doc.RootElement.GetProperty("message").GetString()!, "<=");
    }

    [TestMethod]
    public async Task WorkspaceTools_GetSourceText_StartPastEof_ReturnsInvalidArgument()
    {
        var programPath = FindProgramPath();
        var json = await ToolExecutionTestHarness.RunAsync(
            "get_source_text",
            () => WorkspaceTools.GetSourceText(
                WorkspaceExecutionGate,
                WorkspaceManager,
                WorkspaceId,
                programPath,
                startLine: 99999,
                endLine: 99999,
                ct: CancellationToken.None));
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

    // workspace-status-verbose-5s-timeout-race-on-ready-workspace: regression test for gh #761.
    // Pre-fix, `workspace_status(verbose=true)` acquired the per-session `LoadLock` (SemaphoreSlim)
    // with a 5-second hard timeout before calling `BuildStatus`. When a concurrent
    // `LoadIntoSessionAsync` held the lock for the duration of an MSBuild workspace load (30+ s on
    // medium solutions), the verbose-status call timed out and threw `TimeoutException` — even
    // though the workspace had reported `isReady=true` to a `verbose=false` call moments earlier.
    // The fix removes the lock acquire entirely: `BuildStatus` reads only thread-safe fields
    // (ImmutableArray, ConcurrentQueue, scalar refs) and the outer `gate.RunReadAsync` already
    // serializes against concurrent writes. This test simulates the contention by holding the
    // session's LoadLock via reflection and asserting the verbose-status call completes in well
    // under the pre-fix 5-second timeout window.
    [TestMethod]
    public async Task WorkspaceTools_GetWorkspaceStatus_Verbose_DoesNotBlockOnConcurrentLoadLock()
    {
        var session = GetWorkspaceSession(WorkspaceManager, WorkspaceId);
        var loadLock = GetLoadLock(session);

        // Hold the LoadLock to simulate an in-flight LoadIntoSessionAsync. Pre-fix, the verbose
        // status path would wait 5 s on this semaphore and then throw TimeoutException.
        await loadLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var json = await WorkspaceTools.GetWorkspaceStatus(
                WorkspaceExecutionGate,
                WorkspaceManager,
                WorkspaceId,
                verbose: true,
                CancellationToken.None);
            stopwatch.Stop();

            // Generous upper bound: BuildStatus itself is sub-millisecond, but the outer gate
            // (rate-limit, throttle, per-workspace reader lock, staleness check) adds overhead.
            // 1000 ms is two orders of magnitude below the pre-fix 5005 ms ceiling and still well
            // clear of any realistic gate overhead on a healthy workspace.
            Assert.IsTrue(
                stopwatch.ElapsedMilliseconds < 1000,
                $"workspace_status(verbose=true) should not block on a held LoadLock; took {stopwatch.ElapsedMilliseconds} ms.");

            using var doc = JsonDocument.Parse(json);
            Assert.IsTrue(doc.RootElement.TryGetProperty("projects", out var projects),
                "Verbose mode must include the per-project tree.");
            Assert.IsTrue(projects.GetArrayLength() >= 1);
        }
        finally
        {
            loadLock.Release();
        }
    }

    private static object GetWorkspaceSession(object workspaceManager, string workspaceId)
    {
        // WorkspaceSession is a private nested type; access via the `_sessions` field on
        // WorkspaceManager. We deliberately use reflection to drive the precise race condition
        // (LoadLock held while a status call runs) — production code never reaches into the
        // session directly.
        var sessionsField = workspaceManager.GetType().GetField(
            "_sessions", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(sessionsField, "WorkspaceManager._sessions field must exist.");
        var sessions = sessionsField.GetValue(workspaceManager);
        Assert.IsNotNull(sessions, "WorkspaceManager._sessions must be initialised.");
        var indexer = sessions.GetType().GetProperty("Item", new[] { typeof(string) });
        Assert.IsNotNull(indexer, "ConcurrentDictionary indexer must be discoverable via reflection.");
        var session = indexer.GetValue(sessions, new object[] { workspaceId });
        Assert.IsNotNull(session, $"Workspace session for '{workspaceId}' must be present.");
        return session;
    }

    private static SemaphoreSlim GetLoadLock(object session)
    {
        var loadLockProperty = session.GetType().GetProperty(
            "LoadLock", BindingFlags.Public | BindingFlags.Instance);
        Assert.IsNotNull(loadLockProperty, "WorkspaceSession.LoadLock property must exist.");
        var loadLock = loadLockProperty.GetValue(session) as SemaphoreSlim;
        Assert.IsNotNull(loadLock, "WorkspaceSession.LoadLock must be a SemaphoreSlim instance.");
        return loadLock;
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
