using ModelContextProtocol;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests.Progress;

/// <summary>
/// Locks in the shipped stage-fine progress emission contract. The four long-running tools
/// (<c>workspace_load</c>, <c>workspace_warm</c>, <c>build_workspace</c>, <c>test_run</c>)
/// MUST emit a documented sequence of stage labels (kebab-case, stable across releases) so
/// MCP clients can render intermediate progress instead of waiting silently. Per
/// <see cref="ProgressHelper.ReportStage(System.IProgress{ModelContextProtocol.ProgressNotificationValue}?, float, float, string)"/>'s
/// remarks, labels are part of the public surface — these tests are the regression guard.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a recorder, not a mock workspace.</b> The progress contract is independent of
/// workspace state — emissions happen at tool boundaries before/after the underlying service
/// call. We record emissions via a lightweight <see cref="ProgressEmissionRecorder"/> and
/// drive the SUT against the SampleSolution fixture (already loaded by
/// <see cref="SharedWorkspaceTestBase"/>). Mocking the workspace would defeat the
/// integration-level coverage; mocking the progress sink keeps the assertions deterministic.
/// </para>
/// <para>
/// <b>Workspace load testing.</b> <c>workspace_load</c>'s tool surface takes an
/// <see cref="McpServer"/> for client-roots validation. The validator handles
/// <see langword="null"/> servers by skipping the roots check (path is allowed
/// unconditionally), and resource-list notification failures are intentionally non-fatal
/// and logged at Debug when a logger is supplied. So the test passes <see langword="null!"/>
/// for the server parameter and drives the existing dedup path: re-loading an already-loaded
/// sample returns the cached session without re-running MSBuild. The full stage sequence is
/// still emitted.
/// </para>
/// </remarks>
[DoNotParallelize]
[TestClass]
public sealed class ProgressEmissionTests : SharedWorkspaceTestBase
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
    public async Task WorkspaceLoad_EmitsStageSequence_ValidatingPath_To_Done()
    {
        var recorder = new ProgressEmissionRecorder();

        // Re-loading the cached sample workspace exercises the dedup path; the tool still
        // emits every stage label because the emissions sit outside `workspace.LoadAsync`.
        // server: null! is safe — ClientRootPathValidator handles null servers explicitly
        // and the fire-and-forget resource-list notification remains non-fatal.
        await WorkspaceTools.LoadWorkspace(
            server: null!,
            gate: WorkspaceExecutionGate,
            workspace: WorkspaceManager,
            warmService: WorkspaceWarmService,
            commandRunner: DotnetCommandRunner,
            path: SampleSolutionPath,
            verbose: false,
            autoRestore: false,
            progress: recorder,
            ct: CancellationToken.None);

        AssertStageSequence(
            recorder,
            new[] { "validating-path", "opening-workspace", "checking-restore", "done" },
            expectedTotal: 4,
            toolName: "workspace_load");
    }

    [TestMethod]
    public async Task WorkspaceWarm_EmitsStageSequence_SchedulingWarm_To_Done()
    {
        var recorder = new ProgressEmissionRecorder();

        await WorkspaceWarmTools.WorkspaceWarm(
            gate: WorkspaceExecutionGate,
            warmService: WorkspaceWarmService,
            workspaceId: WorkspaceId,
            projects: null,
            progress: recorder,
            ct: CancellationToken.None);

        AssertStageSequence(
            recorder,
            new[] { "scheduling-warm", "warming-projects", "done" },
            expectedTotal: 3,
            toolName: "workspace_warm");
    }

    [TestMethod]
    public async Task BuildWorkspace_EmitsStageSequence_PreparingBuild_To_Done()
    {
        var recorder = new ProgressEmissionRecorder();

        await ValidationTools.BuildWorkspace(
            gate: WorkspaceExecutionGate,
            buildService: BuildService,
            workspaceId: WorkspaceId,
            progress: recorder,
            ct: CancellationToken.None);

        AssertStageSequence(
            recorder,
            new[] { "preparing-build", "msbuild-running", "done" },
            expectedTotal: 3,
            toolName: "build_workspace");
    }

    [TestMethod]
    public async Task TestRun_EmitsStageSequence_DiscoveringTests_To_Done()
    {
        var recorder = new ProgressEmissionRecorder();

        // Use a name filter that matches no tests so dotnet-test returns immediately
        // without spending full execution time. The progress emissions still fire because
        // they bracket the entire RunTestsAsync call.
        await ValidationTools.RunTests(
            gate: WorkspaceExecutionGate,
            testRunnerService: TestRunnerService,
            workspaceId: WorkspaceId,
            projectName: null,
            filter: "FullyQualifiedName~__progress_emit_audit_no_match__",
            progress: recorder,
            ct: CancellationToken.None);

        AssertStageSequence(
            recorder,
            new[] { "discovering-tests", "running-tests", "done" },
            expectedTotal: 3,
            toolName: "test_run");
    }

    [TestMethod]
    public void ProgressHelper_ReportStage_SetsMessageAndProgressFields()
    {
        var recorder = new ProgressEmissionRecorder();
        ProgressHelper.ReportStage(recorder, current: 2, total: 5, stageLabel: "midway");

        Assert.AreEqual(1, recorder.Notifications.Count);
        var notification = recorder.Notifications[0];
        Assert.AreEqual(2f, notification.Progress, "Progress field must round-trip.");
        Assert.AreEqual(5f, notification.Total, "Total field must round-trip.");
        Assert.AreEqual("midway", notification.Message, "Message field must carry the stage label.");
    }

    [TestMethod]
    public void ProgressHelper_ReportStage_NullProgress_DoesNotThrow()
    {
        // Tools that don't have a subscribed client receive a null progress sink. The helper
        // must handle this gracefully — no exception, no observable side effects.
        ProgressHelper.ReportStage(progress: null, current: 1, total: 4, stageLabel: "any");
    }

    private static void AssertStageSequence(
        ProgressEmissionRecorder recorder,
        string[] expectedLabels,
        float expectedTotal,
        string toolName)
    {
        var actualLabels = recorder.Notifications.Select(n => n.Message).ToList();

        Assert.AreEqual(
            expectedLabels.Length,
            actualLabels.Count,
            $"{toolName}: expected exactly {expectedLabels.Length} stage emissions; got " +
            $"{actualLabels.Count}: [{string.Join(", ", actualLabels)}].");

        for (int i = 0; i < expectedLabels.Length; i++)
        {
            Assert.AreEqual(
                expectedLabels[i],
                actualLabels[i],
                $"{toolName}: stage {i} label mismatch. " +
                $"Expected sequence: [{string.Join(", ", expectedLabels)}]; " +
                $"got: [{string.Join(", ", actualLabels)}].");
        }

        // Total must be consistent across every emission so client progress bars track the
        // same denominator across stages. A varying Total would make the bar jump.
        foreach (var notification in recorder.Notifications)
        {
            Assert.AreEqual(
                expectedTotal,
                notification.Total,
                $"{toolName}: every stage emission must use Total={expectedTotal}; " +
                $"got Total={notification.Total} on label '{notification.Message}'.");
        }
    }
}

/// <summary>
/// Captures every <see cref="ProgressNotificationValue"/> reported through the
/// <see cref="IProgress{T}"/> sink. Tests inspect the captured sequence to assert the
/// stage-emission contract.
/// </summary>
internal sealed class ProgressEmissionRecorder : IProgress<ProgressNotificationValue>
{
    private readonly List<ProgressNotificationValue> _notifications = new();

    public IReadOnlyList<ProgressNotificationValue> Notifications => _notifications;

    public void Report(ProgressNotificationValue value)
    {
        _notifications.Add(value);
    }
}
