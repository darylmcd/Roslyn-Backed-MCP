using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Runtime;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Contracts;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression coverage for <c>workspace-close-with-drain-processes-for-teardown</c>:
/// <see cref="WorkspaceTools.CloseWorkspace"/> with <c>drainProcesses=true</c> invokes
/// <see cref="IDotnetCommandRunner.RunAsync"/> with <c>build-server shutdown</c> after
/// the session is removed, and with <c>drainProcesses=false</c> (the default) it does not.
/// </summary>
// donotparallelize-audit-wave-36: [DoNotParallelize] removed. The workspace manager, execution
// gate, command runner and exception reporter are all per-test fakes, so no shared session or
// server state is touched. The helper processes are copies of PING.EXE / sleep under a per-test
// GUID directory, and the getProcessesByName seam hands the drain only those pids, so a drain
// can never reach another class's process. The single seam-less test drains a fake workspace
// rooted at %TEMP%\repo, whose kill filter only matches testhost/vstest.console executables under
// that directory, which no test in this assembly launches. Validated by a bounded repeated (3x)
// concurrent run alongside its wave-36 siblings and parallel-enabled workspace-loading classes,
// green every time.
[TestClass]
public sealed class WorkspaceCloseDrainTests
{
    // ---------------------------------------------------------------------------
    // Positive: drainProcesses=true calls build-server shutdown
    // ---------------------------------------------------------------------------

    [TestMethod]
    public async Task CloseWorkspace_DrainProcessesTrue_InvokesBuildServerShutdown()
    {
        const string expectedWorkspaceId = "test-ws-drain-1";
        var loadedPath = Path.Combine(Path.GetTempPath(), "repo", "Sample.slnx");

        var status = CreateStatus(expectedWorkspaceId, loadedPath);
        var fakeWorkspace = new FakeWorkspaceManagerForDrain(status);
        var gate = new PassthroughGate();
        var commandRunner = new RecordingDotnetCommandRunner();

        var json = await WorkspaceTools.CloseWorkspace(
            gate: gate,
            workspace: fakeWorkspace,
            commandRunner: commandRunner,
            workspaceId: expectedWorkspaceId,
            drainProcesses: true,
            ct: CancellationToken.None);

        // The close payload must report success.
        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.GetProperty("success").GetBoolean(),
            "CloseWorkspace must return success=true when the session existed.");
        Assert.AreEqual(expectedWorkspaceId, doc.RootElement.GetProperty("workspaceId").GetString());

        // The drain command must have been called exactly once.
        Assert.AreEqual(1, commandRunner.CallCount,
            "drainProcesses=true must invoke commandRunner exactly once.");

        // Verify the arguments passed to the runner.
        Assert.IsNotNull(commandRunner.LastArguments, "commandRunner must have captured arguments.");
        CollectionAssert.AreEqual(
            new[] { "build-server", "shutdown" },
            commandRunner.LastArguments!.ToArray(),
            "The drain command must pass [\"build-server\", \"shutdown\"] to the runner.");

        // Working directory must be the directory that contained the loaded path.
        Assert.AreEqual(
            Path.GetDirectoryName(loadedPath),
            commandRunner.LastWorkingDirectory,
            "The drain working directory must be the directory of the loaded path.");
    }

    // ---------------------------------------------------------------------------
    // Positive: drainProcesses=true terminates detached testhost processes whose
    // executable lives under the working directory, and leaves out-of-dir ones alone.
    //
    // Uses real, long-lived child processes (the only way to exercise the
    // Process.MainModule path-prefix filter + Kill end-to-end — Process has no
    // mockable surface). The getProcessesByName seam injects them into the drain.
    // ---------------------------------------------------------------------------

    [TestMethod]
    public async Task CloseWorkspace_DrainProcessesTrue_DoesNotLeakTesthostInWorkingDir()
    {
        const string expectedWorkspaceId = "test-ws-testhost-drain";

        // workingDirectory = directory of the loaded path. Place the in-dir process's
        // executable in a child folder so its MainModule.FileName prefix-matches.
        var workingDirectory = Path.Combine(Path.GetTempPath(), "rmcp-testhost-drain-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);
        var loadedPath = Path.Combine(workingDirectory, "Sample.slnx");

        Process? inDirProcess = null;
        Process? outOfDirProcess = null;
        try
        {
            // A process whose executable lives UNDER workingDirectory: must be killed.
            inDirProcess = StartLongLivedProcessUnder(Path.Combine(workingDirectory, "host"));
            // A process whose executable lives OUTSIDE workingDirectory (the original system
            // launcher path): must be left running.
            outOfDirProcess = StartLongLivedProcessFromSystemPath();

            var inDirPid = inDirProcess.Id;
            var outOfDirPid = outOfDirProcess.Id;

            var status = CreateStatus(expectedWorkspaceId, loadedPath);
            var fakeWorkspace = new FakeWorkspaceManagerForDrain(status);
            var gate = new PassthroughGate();
            var commandRunner = new RecordingDotnetCommandRunner();

            // Inject both as "testhost" candidates; vstest.console returns none.
            Func<string, Process[]> getProcessesByName = name => name == "testhost"
                ? new[] { GetByIdOrEmpty(inDirPid), GetByIdOrEmpty(outOfDirPid) }
                    .Where(p => p is not null).Select(p => p!).ToArray()
                : [];

            var json = await CloseWithProcessSeamAsync(
                gate, fakeWorkspace, commandRunner, expectedWorkspaceId, getProcessesByName);

            using var doc = JsonDocument.Parse(json);
            Assert.IsTrue(doc.RootElement.GetProperty("success").GetBoolean(),
                "CloseWorkspace must still return success=true when the testhost drain runs.");

            // The in-dir testhost must have been terminated.
            Assert.IsTrue(
                inDirProcess.WaitForExit(10_000),
                "A testhost process whose executable lives under the working directory must be killed by the drain.");

            // The out-of-dir process must NOT have been touched.
            Assert.IsFalse(
                outOfDirProcess.HasExited,
                "A testhost process whose executable lives outside the working directory must be left running.");

            // Additive contract: a fully handled drain keeps the original { success, workspaceId } shape.
            AssertNoUndrainedProcesses(doc);
        }
        finally
        {
            KillQuietly(inDirProcess);
            KillQuietly(outOfDirProcess);
            TryDeleteDirectory(workingDirectory);
        }
    }

    // ---------------------------------------------------------------------------
    // Boundary: a SIBLING worktree whose path is a string-prefix of workingDirectory
    // (e.g. workingDirectory ".../wt-foo", sibling ".../wt-foo-sibling") must NOT be
    // killed. This is the multi-drain hazard: .worktrees/ holds concurrent siblings
    // during parallel sweeps, and an un-guarded StartsWith(workingDirectory) would
    // false-positive on the sibling and kill its testhost tree mid-test-run.
    //
    // This test FAILS against the un-guarded `StartsWith(workingDirectory)` (the sibling's
    // executable path string-prefix-matches and gets killed) and PASSES once the filter
    // requires a true descendant (workingDirectory + separator).
    // ---------------------------------------------------------------------------

    [TestMethod]
    public async Task CloseWorkspace_DrainProcessesTrue_DoesNotKillSiblingPrefixWorktree()
    {
        const string expectedWorkspaceId = "test-ws-sibling-prefix-drain";

        var workingDirectory = Path.Combine(Path.GetTempPath(), "rmcp-sibling-drain-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);
        var loadedPath = Path.Combine(workingDirectory, "Sample.slnx");

        // The sibling directory shares workingDirectory's string prefix but is NOT a descendant:
        // its path is workingDirectory + "-sibling" (the char after the prefix is '-', not a
        // path separator). A correct boundary filter must leave its process running.
        var siblingDirectory = workingDirectory + "-sibling";
        Directory.CreateDirectory(siblingDirectory);

        Process? inDirProcess = null;
        Process? siblingProcess = null;
        try
        {
            // True descendant of workingDirectory: must be killed.
            inDirProcess = StartLongLivedProcessUnder(Path.Combine(workingDirectory, "host"));
            // Sibling sharing the string prefix but NOT a descendant: must be left running.
            siblingProcess = StartLongLivedProcessUnder(Path.Combine(siblingDirectory, "host"));

            var inDirPid = inDirProcess.Id;
            var siblingPid = siblingProcess.Id;

            var status = CreateStatus(expectedWorkspaceId, loadedPath);
            var fakeWorkspace = new FakeWorkspaceManagerForDrain(status);
            var gate = new PassthroughGate();
            var commandRunner = new RecordingDotnetCommandRunner();

            Func<string, Process[]> getProcessesByName = name => name == "testhost"
                ? new[] { GetByIdOrEmpty(inDirPid), GetByIdOrEmpty(siblingPid) }
                    .Where(p => p is not null).Select(p => p!).ToArray()
                : [];

            var json = await CloseWithProcessSeamAsync(
                gate, fakeWorkspace, commandRunner, expectedWorkspaceId, getProcessesByName);

            using var doc = JsonDocument.Parse(json);
            Assert.IsTrue(doc.RootElement.GetProperty("success").GetBoolean(),
                "CloseWorkspace must still return success=true when the testhost drain runs.");

            // The true-descendant testhost must have been terminated.
            Assert.IsTrue(
                inDirProcess.WaitForExit(10_000),
                "A testhost process whose executable lives under the working directory must be killed by the drain.");

            // The sibling-prefix testhost must NOT have been touched.
            Assert.IsFalse(
                siblingProcess.HasExited,
                "A testhost in a SIBLING directory sharing the working-directory string prefix " +
                "(workingDirectory + \"-sibling\") must be left running — it is not a true descendant.");
            AssertNoUndrainedProcesses(doc);
        }
        finally
        {
            KillQuietly(inDirProcess);
            KillQuietly(siblingProcess);
            TryDeleteDirectory(workingDirectory);
            TryDeleteDirectory(siblingDirectory);
        }
    }

    // ---------------------------------------------------------------------------
    // Race: a testhost started moments before the drain has not run its loader yet.
    //
    // Process.MainModule reads the target's loader module list (EnumProcessModules over the
    // PEB loader data), which a new process only builds once its primary thread has run the
    // loader. Until then MainModule returns null or throws ERROR_PARTIAL_COPY, and the drain
    // used to skip the candidate: a testhost spawned just before workspace_close survived and
    // kept its bin/ locks. Under CI load this window reached the real-process tests above.
    // CREATE_SUSPENDED holds a process in that pre-loader state, so this test reproduces the
    // race deterministically. It fails on the MainModule-based drain and passes once the drain
    // reads the image path the kernel recorded at process creation.
    // ---------------------------------------------------------------------------

    [TestMethod]
    public async Task CloseWorkspace_DrainProcessesTrue_KillsInDirTesthostWhoseLoaderHasNotRun()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The CREATE_SUSPENDED pre-loader fixture is Windows-only.");
            return;
        }

        const string expectedWorkspaceId = "test-ws-pre-loader-drain";
        var workingDirectory = Path.Combine(Path.GetTempPath(), "rmcp-preloader-drain-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);
        var loadedPath = Path.Combine(workingDirectory, "Sample.slnx");

        try
        {
            var executablePath = CopyLauncherUnder(Path.Combine(workingDirectory, "host"));
            using var suspended = WindowsSuspendedProcess.Start(executablePath, LauncherArguments);
            var suspendedPid = suspended.Process.Id;

            Func<string, Process[]> getProcessesByName = name => name == "testhost"
                ? new[] { GetByIdOrEmpty(suspendedPid) }.Where(p => p is not null).Select(p => p!).ToArray()
                : [];

            var json = await CloseWithProcessSeamAsync(
                new PassthroughGate(),
                new FakeWorkspaceManagerForDrain(CreateStatus(expectedWorkspaceId, loadedPath)),
                new RecordingDotnetCommandRunner(),
                expectedWorkspaceId,
                getProcessesByName);

            using var doc = JsonDocument.Parse(json);
            Assert.IsTrue(doc.RootElement.GetProperty("success").GetBoolean(),
                "CloseWorkspace must still return success=true when the testhost drain runs.");
            Assert.IsTrue(
                suspended.Process.WaitForExit(10_000),
                "A testhost under the working directory must be killed even when its loader has not run yet.");
            AssertNoUndrainedProcesses(doc);
        }
        finally
        {
            TryDeleteDirectory(workingDirectory);
        }
    }

    // ---------------------------------------------------------------------------
    // Paths compare in the physical namespace. workspace_load stores LoadedPath only after
    // PhysicalPathResolver has resolved every symlink and junction, and the drain resolves each
    // candidate to its post-reparse image path. A testhost launched through a junction into the
    // workspace therefore still matches. Process.MainModule reported the as-launched link path,
    // which missed it.
    // ---------------------------------------------------------------------------

    [TestMethod]
    public async Task CloseWorkspace_DrainProcessesTrue_KillsInDirTesthostLaunchedThroughAJunction()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("NTFS junctions are Windows-only.");
            return;
        }

        const string expectedWorkspaceId = "test-ws-junction-drain";
        var root = Path.Combine(Path.GetTempPath(), "rmcp-junction-drain-" + Guid.NewGuid().ToString("N"));
        var workingDirectory = Path.Combine(root, "physical");
        Directory.CreateDirectory(workingDirectory);
        var loadedPath = Path.Combine(workingDirectory, "Sample.slnx");
        var link = Path.Combine(root, "link");

        Process? launched = null;
        try
        {
            WindowsDirectoryJunction.Create(link, workingDirectory);
            launched = StartLongLived(CopyLauncherUnder(Path.Combine(link, "host")));
            var launchedPid = launched.Id;

            var json = await CloseWithProcessSeamAsync(
                new PassthroughGate(),
                new FakeWorkspaceManagerForDrain(CreateStatus(expectedWorkspaceId, loadedPath)),
                new RecordingDotnetCommandRunner(),
                expectedWorkspaceId,
                name => name == "testhost"
                    ? new[] { GetByIdOrEmpty(launchedPid) }.Where(p => p is not null).Select(p => p!).ToArray()
                    : []);

            using var doc = JsonDocument.Parse(json);
            Assert.IsTrue(doc.RootElement.GetProperty("success").GetBoolean());
            Assert.IsTrue(
                launched.WaitForExit(10_000),
                "A testhost launched through a junction into the physical workspace must be killed.");
            AssertNoUndrainedProcesses(doc);
        }
        finally
        {
            KillQuietly(launched);
            TryDeleteDirectory(root);
        }
    }

    // ---------------------------------------------------------------------------
    // A candidate the drain cannot inspect is reported, never dropped and never killed.
    // Its location is unknown, so killing it could hit an unrelated worktree's testhost, but
    // it may hold locks under this workspace. The response and a Warning log name it.
    // ---------------------------------------------------------------------------

    [TestMethod]
    [DataRow("access-denied", "access-denied", 0)]
    [DataRow("throws", "path-query-failed", 1)]
    public async Task CloseWorkspace_DrainProcessesTrue_ReportsCandidateWhosePathCannotBeRead(
        string resolverOutcome,
        string expectedReason,
        int expectedUnexpectedReports)
    {
        const string expectedWorkspaceId = "test-ws-unreadable-path-drain";
        var workingDirectory = Path.Combine(Path.GetTempPath(), "rmcp-unreadable-drain-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);
        var loadedPath = Path.Combine(workingDirectory, "Sample.slnx");

        Process? inDirProcess = null;
        try
        {
            inDirProcess = StartLongLivedProcessUnder(Path.Combine(workingDirectory, "host"));
            var inDirPid = inDirProcess.Id;
            var logger = new RecordingLogger();
            var reporter = new RecordingUnexpectedExceptionReporter();

            var json = await WorkspaceTools.CloseWorkspaceCore(
                gate: new PassthroughGate(),
                workspace: new FakeWorkspaceManagerForDrain(CreateStatus(expectedWorkspaceId, loadedPath)),
                commandRunner: new RecordingDotnetCommandRunner(),
                workspaceId: expectedWorkspaceId,
                drainProcesses: true,
                loggerFactory: new RecordingLoggerFactory(logger),
                exceptionReporter: reporter,
                getProcessesByName: name => name == "testhost"
                    ? new[] { GetByIdOrEmpty(inDirPid) }.Where(p => p is not null).Select(p => p!).ToArray()
                    : [],
                processDrainTimeout: WorkspaceTools.DefaultProcessDrainTimeout,
                ct: CancellationToken.None,
                resolveExecutablePath: _ => resolverOutcome == "throws"
                    ? throw new InvalidOperationException("simulated path query failure")
                    : ProcessExecutablePathResolution.Unavailable(ProcessExecutablePathResolver.AccessDeniedReason));

            using var doc = JsonDocument.Parse(json);
            Assert.IsTrue(doc.RootElement.GetProperty("success").GetBoolean(),
                "An uninspectable candidate cannot roll back the committed close.");
            AssertSingleUndrained(doc, "testhost", inDirPid, expectedReason);

            Assert.IsFalse(inDirProcess.HasExited,
                "A candidate whose executable path cannot be read must never be killed: its location is unknown.");

            var warning = logger.Entries.Single(e => e.Level == LogLevel.Warning);
            StringAssert.Contains(warning.Message, inDirPid.ToString(CultureInfo.InvariantCulture));
            StringAssert.Contains(warning.Message, expectedReason);
            Assert.AreEqual(expectedUnexpectedReports, reporter.Reports.Count,
                "An access refusal is expected; only an exception from the path query is an unexpected failure.");
        }
        finally
        {
            KillQuietly(inDirProcess);
            TryDeleteDirectory(workingDirectory);
        }
    }

    // Production teardown timing: the drain enumerates a live testhost, which exits before the
    // drain resolves its path. Its parent (vstest.console / dotnet test) still holds a handle, so
    // the pid still names the exited process and opening it succeeds. On Windows the path query
    // then fails, and that failure must not be mistaken for a live, uninspectable process. The
    // real resolver runs here: a stub resolver returning Exited cannot catch that misclassification.
    [TestMethod]
    public async Task CloseWorkspace_DrainProcessesTrue_CandidateThatExitedAfterEnumeration_IsNotReported()
    {
        const string expectedWorkspaceId = "test-ws-exited-candidate-drain";
        var workingDirectory = Path.Combine(Path.GetTempPath(), "rmcp-exited-drain-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);
        var loadedPath = Path.Combine(workingDirectory, "Sample.slnx");

        // `parent` plays the testhost's parent: it stays undisposed, so it holds a process handle
        // after the exit. `enumerated` is the drain's own Process for the same pid.
        Process? parent = null;
        Process? enumerated = null;
        try
        {
            parent = StartLongLivedProcessUnder(Path.Combine(workingDirectory, "host"));
            enumerated = Process.GetProcessById(parent.Id);
            parent.Kill();
            Assert.IsTrue(parent.WaitForExit(10_000), "The candidate must exit before the drain resolves it.");

            var logger = new RecordingLogger();
            var reporter = new RecordingUnexpectedExceptionReporter();
            var candidate = enumerated;

            var json = await WorkspaceTools.CloseWorkspaceCore(
                gate: new PassthroughGate(),
                workspace: new FakeWorkspaceManagerForDrain(CreateStatus(expectedWorkspaceId, loadedPath)),
                commandRunner: new RecordingDotnetCommandRunner(),
                workspaceId: expectedWorkspaceId,
                drainProcesses: true,
                loggerFactory: new RecordingLoggerFactory(logger),
                exceptionReporter: reporter,
                getProcessesByName: name => name == "testhost" ? [candidate] : [],
                processDrainTimeout: WorkspaceTools.DefaultProcessDrainTimeout,
                ct: CancellationToken.None);

            using var doc = JsonDocument.Parse(json);
            Assert.IsTrue(doc.RootElement.GetProperty("success").GetBoolean());
            AssertNoUndrainedProcesses(doc);
            Assert.IsFalse(logger.Entries.Any(e => e.Level == LogLevel.Warning),
                "A candidate that exited before inspection holds no locks and must not be reported.");
            Assert.AreEqual(0, reporter.Reports.Count, "An exited candidate is not a failure.");
        }
        finally
        {
            enumerated?.Dispose();
            KillQuietly(parent);
            TryDeleteDirectory(workingDirectory);
        }
    }

    // ---------------------------------------------------------------------------
    // Cancellation: once the cleanup budget is spent the drain terminates nothing, but it
    // still reports the in-workspace testhost it left running instead of skipping the step.
    // ---------------------------------------------------------------------------

    [TestMethod]
    public async Task CloseWorkspace_DrainBudgetExhausted_ReportsInDirTesthostItLeftRunning()
    {
        const string expectedWorkspaceId = "test-ws-cancelled-drain";
        var workingDirectory = Path.Combine(Path.GetTempPath(), "rmcp-cancelled-drain-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);
        var loadedPath = Path.Combine(workingDirectory, "Sample.slnx");

        Process? inDirProcess = null;
        try
        {
            inDirProcess = StartLongLivedProcessUnder(Path.Combine(workingDirectory, "host"));
            var inDirPid = inDirProcess.Id;
            var reporter = new RecordingUnexpectedExceptionReporter();

            var json = await WorkspaceTools.CloseWorkspaceCore(
                gate: new PassthroughGate(),
                workspace: new FakeWorkspaceManagerForDrain(CreateStatus(expectedWorkspaceId, loadedPath)),
                commandRunner: new RecordingDotnetCommandRunner { Outcome = "timeout" },
                workspaceId: expectedWorkspaceId,
                drainProcesses: true,
                loggerFactory: null,
                exceptionReporter: reporter,
                getProcessesByName: name => name == "testhost"
                    ? new[] { GetByIdOrEmpty(inDirPid) }.Where(p => p is not null).Select(p => p!).ToArray()
                    : [],
                processDrainTimeout: TimeSpan.FromMilliseconds(25),
                ct: CancellationToken.None);

            using var doc = JsonDocument.Parse(json);
            Assert.IsTrue(doc.RootElement.GetProperty("success").GetBoolean());
            AssertSingleUndrained(doc, "testhost", inDirPid, DetachedTestHostDrain.DrainCancelledReason);
            Assert.IsFalse(inDirProcess.HasExited,
                "A drain whose cleanup budget is spent must not terminate further processes.");
            Assert.AreEqual(1, reporter.Reports.Count,
                "The budget overrun is still reported exactly once.");
        }
        finally
        {
            KillQuietly(inDirProcess);
            TryDeleteDirectory(workingDirectory);
        }
    }

    // ---------------------------------------------------------------------------
    // ProcessExecutablePathResolver: resolves a process the moment it exists.
    // ---------------------------------------------------------------------------

    [TestMethod]
    public void ProcessExecutablePathResolver_JustStartedProcess_ResolvesItsExecutable()
    {
        var directory = Path.Combine(Path.GetTempPath(), "rmcp-resolver-" + Guid.NewGuid().ToString("N"));
        Process? process = null;
        try
        {
            var executablePath = CopyLauncherUnder(directory);
            process = StartLongLived(executablePath);

            // No readiness wait: resolve the same instant the drain would.
            using var candidate = Process.GetProcessById(process.Id);
            var resolution = ProcessExecutablePathResolver.Resolve(candidate);

            Assert.AreEqual(ProcessExecutablePathStatus.Resolved, resolution.Status, resolution.Reason);
            Assert.AreEqual(
                Path.GetFullPath(executablePath),
                Path.GetFullPath(resolution.Path!),
                ignoreCase: OperatingSystem.IsWindows());
        }
        finally
        {
            KillQuietly(process);
            TryDeleteDirectory(directory);
        }
    }

    [TestMethod]
    public void ProcessExecutablePathResolver_ProcessThatExitedWhileAHandleIsHeld_IsExited()
    {
        var directory = Path.Combine(Path.GetTempPath(), "rmcp-resolver-exited-" + Guid.NewGuid().ToString("N"));
        Process? process = null;
        try
        {
            process = StartLongLived(CopyLauncherUnder(directory));
            process.Kill();
            Assert.IsTrue(process.WaitForExit(10_000), "The helper must exit before it is resolved.");

            // `process` still holds its handle, so the pid still names the exited process.
            var resolution = ProcessExecutablePathResolver.Resolve(process);

            Assert.AreEqual(ProcessExecutablePathStatus.Exited, resolution.Status, resolution.Reason);
        }
        finally
        {
            KillQuietly(process);
            TryDeleteDirectory(directory);
        }
    }

    [TestMethod]
    public void ProcessExecutablePathResolver_ProcessWhoseLoaderHasNotRun_ResolvesItsExecutable()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The CREATE_SUSPENDED pre-loader fixture is Windows-only.");
            return;
        }

        var directory = Path.Combine(Path.GetTempPath(), "rmcp-resolver-preloader-" + Guid.NewGuid().ToString("N"));
        try
        {
            var executablePath = CopyLauncherUnder(directory);
            using var suspended = WindowsSuspendedProcess.Start(executablePath, LauncherArguments);

            var resolution = ProcessExecutablePathResolver.Resolve(suspended.Process);

            Assert.AreEqual(ProcessExecutablePathStatus.Resolved, resolution.Status, resolution.Reason);
            Assert.AreEqual(executablePath, resolution.Path, ignoreCase: true);
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    // ---------------------------------------------------------------------------
    // Negative: drainProcesses=false (default) must NOT invoke commandRunner
    // ---------------------------------------------------------------------------

    [TestMethod]
    public async Task CloseWorkspace_DrainProcessesFalse_DoesNotInvokeCommandRunner()
    {
        const string expectedWorkspaceId = "test-ws-no-drain";
        var loadedPath = Path.Combine(Path.GetTempPath(), "repo", "Another.slnx");

        var status = CreateStatus(expectedWorkspaceId, loadedPath);
        var fakeWorkspace = new FakeWorkspaceManagerForDrain(status);
        var gate = new PassthroughGate();
        var commandRunner = new RecordingDotnetCommandRunner();

        var json = await WorkspaceTools.CloseWorkspace(
            gate: gate,
            workspace: fakeWorkspace,
            commandRunner: commandRunner,
            workspaceId: expectedWorkspaceId,
            drainProcesses: false,
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.GetProperty("success").GetBoolean());

        Assert.AreEqual(0, commandRunner.CallCount,
            "drainProcesses=false must NOT invoke commandRunner.");
    }

    // ---------------------------------------------------------------------------
    // Defensive: unknown workspaceId + drainProcesses=true must NOT error on drain
    // ---------------------------------------------------------------------------

    [TestMethod]
    public async Task CloseWorkspace_UnknownWorkspaceId_DrainProcessesTrue_ReturnsFalseWithoutError()
    {
        const string unknownId = "unknown-ws-id";

        // FakeWorkspaceManagerForDrain returns false from Close when the id is unknown,
        // and throws from GetStatus — the drain path should skip gracefully.
        var fakeWorkspace = new FakeWorkspaceManagerForDrain(status: null);
        var gate = new PassthroughGate();
        var commandRunner = new RecordingDotnetCommandRunner();

        var json = await WorkspaceTools.CloseWorkspace(
            gate: gate,
            workspace: fakeWorkspace,
            commandRunner: commandRunner,
            workspaceId: unknownId,
            drainProcesses: true,
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        // The close returns success=false because the workspace was not found.
        Assert.IsFalse(doc.RootElement.GetProperty("success").GetBoolean(),
            "Closing an unknown workspaceId must return success=false.");

        // The drain must have been skipped — no call to commandRunner.
        Assert.AreEqual(0, commandRunner.CallCount,
            "The drain must be skipped when GetStatus throws (unknown workspace).");
    }

    [TestMethod]
    [DataRow("success")]
    [DataRow("exception")]
    [DataRow("nonzero")]
    [DataRow("caller-cancellation")]
    [DataRow("timeout")]
    public async Task CloseWorkspace_PostCommitDrainOutcomes_PreserveCloseAndReportOnce(string outcome)
    {
        const string expectedWorkspaceId = "test-ws-drain-outcome";
        var loadedPath = Path.Combine(Path.GetTempPath(), "repo", "DrainOutcome.slnx");
        using var callerCancellation = new CancellationTokenSource();
        var reporter = new RecordingUnexpectedExceptionReporter();
        var commandRunner = new RecordingDotnetCommandRunner
        {
            Outcome = outcome,
            CallerCancellation = callerCancellation
        };

        var json = await WorkspaceTools.CloseWorkspaceCore(
            gate: new PassthroughGate(),
            workspace: new FakeWorkspaceManagerForDrain(CreateStatus(expectedWorkspaceId, loadedPath)),
            commandRunner: commandRunner,
            workspaceId: expectedWorkspaceId,
            drainProcesses: true,
            loggerFactory: null,
            exceptionReporter: reporter,
            getProcessesByName: _ => [],
            processDrainTimeout: TimeSpan.FromMilliseconds(25),
            ct: callerCancellation.Token);

        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.GetProperty("success").GetBoolean(),
            "Workspace removal is the commit point; cleanup outcomes cannot roll it back.");
        AssertNoUndrainedProcesses(doc);
        Assert.AreEqual(1, commandRunner.CallCount);

        var expectedReportCount = outcome == "success" ? 0 : 1;
        Assert.AreEqual(expectedReportCount, reporter.Reports.Count,
            "Every unsuccessful cleanup outcome must emit exactly one safe diagnostic.");
        if (reporter.Reports.Count == 1)
        {
            Assert.AreEqual(UnexpectedExceptionCategory.WorkspaceCloseProcessDrain, reporter.Reports[0].Category);
        }

        Assert.AreEqual(outcome == "caller-cancellation", callerCancellation.IsCancellationRequested,
            "Only the caller-cancellation case should cancel the request token.");
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    // workspace-close-schema-leaks-test-seams: the process-enumerator seam lives on the internal
    // CloseWorkspaceCore, not on the [McpServerTool] method, so it never reaches the MCP schema.
    private static Task<string> CloseWithProcessSeamAsync(
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        IDotnetCommandRunner commandRunner,
        string workspaceId,
        Func<string, Process[]> getProcessesByName) =>
        WorkspaceTools.CloseWorkspaceCore(
            gate,
            workspace,
            commandRunner,
            workspaceId,
            drainProcesses: true,
            loggerFactory: null,
            exceptionReporter: null,
            getProcessesByName,
            WorkspaceTools.DefaultProcessDrainTimeout,
            CancellationToken.None);

    private static void AssertNoUndrainedProcesses(JsonDocument doc) =>
        Assert.IsFalse(
            doc.RootElement.TryGetProperty("undrainedProcesses", out _),
            "undrainedProcesses is additive: it must be omitted when the drain handled every candidate.");

    private static void AssertSingleUndrained(
        JsonDocument doc,
        string expectedProcessName,
        int expectedProcessId,
        string expectedReason)
    {
        Assert.IsTrue(doc.RootElement.TryGetProperty("undrainedProcesses", out var undrained),
            "A candidate the drain left running must be listed in undrainedProcesses.");
        Assert.AreEqual(1, undrained.GetArrayLength());
        var entry = undrained[0];
        Assert.AreEqual(expectedProcessName, entry.GetProperty("processName").GetString());
        Assert.AreEqual(expectedProcessId, entry.GetProperty("processId").GetInt32());
        Assert.AreEqual(expectedReason, entry.GetProperty("reason").GetString());
    }

    private static WorkspaceStatusDto CreateStatus(string workspaceId, string loadedPath) =>
        new WorkspaceStatusDto(
            WorkspaceId: workspaceId,
            LoadedPath: loadedPath,
            WorkspaceVersion: 1,
            SnapshotToken: $"{workspaceId}:1",
            LoadedAtUtc: DateTimeOffset.UtcNow,
            ProjectCount: 1,
            DocumentCount: 1,
            Projects: [new ProjectStatusDto(
                Name: "Proj",
                FilePath: @"C:\repo\Proj\Proj.csproj",
                DocumentCount: 1,
                ProjectReferences: [],
                TargetFrameworks: ["net10.0"],
                IsTestProject: false,
                AssemblyName: "Proj",
                OutputType: "Library")],
            IsLoaded: true,
            IsStale: false,
            WorkspaceDiagnostics: []);

    // ---------------------------------------------------------------------------
    // Real-process helpers for the testhost-drain test
    // ---------------------------------------------------------------------------

    // Cross-platform idle-for-a-while launcher: ping on Windows (no admin needed,
    // present on the self-hosted runner), sleep elsewhere. Both block ~60s until killed.
    private static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private static string SystemLauncherPath =>
        IsWindows
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "PING.EXE")
            : "/bin/sleep";

    private static string[] LauncherArguments =>
        IsWindows
            ? ["-n", "60", "127.0.0.1"]
            : ["60"];

    /// <summary>
    /// Copies the system launcher executable into <paramref name="targetDirectory"/> (so its
    /// executable path prefix-matches the working directory) and starts it long-lived.
    /// </summary>
    private static Process StartLongLivedProcessUnder(string targetDirectory) =>
        StartLongLived(CopyLauncherUnder(targetDirectory));

    private static string CopyLauncherUnder(string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        var copyName = Path.GetFileName(SystemLauncherPath);
        var copiedPath = Path.Combine(targetDirectory, copyName);
        File.Copy(SystemLauncherPath, copiedPath, overwrite: true);
        if (!OperatingSystem.IsWindows())
        {
            // Preserve the executable bit on the copy.
            File.SetUnixFileMode(copiedPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return copiedPath;
    }

    private static Process StartLongLivedProcessFromSystemPath() => StartLongLived(SystemLauncherPath);

    private static Process StartLongLived(string executablePath)
    {
        // No stdout/stderr redirection: the test never reads the helper's output, and an
        // undrained redirected pipe is a latent deadlock shape if the child ever fills it.
        // CreateNoWindow already suppresses the console window.
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in LauncherArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start long-lived helper process: {executablePath}.");
    }

    private static Process? GetByIdOrEmpty(int pid)
    {
        try
        {
            return Process.GetProcessById(pid);
        }
        catch (ArgumentException)
        {
            // Process already exited — no longer in the table.
            return null;
        }
    }

    private static void KillQuietly(Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5_000);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
        finally
        {
            process.Dispose();
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best effort; temp dir, OS reaps it eventually.
        }
    }

    // ---------------------------------------------------------------------------
    // Fakes
    // ---------------------------------------------------------------------------

    private sealed class RecordingDotnetCommandRunner : IDotnetCommandRunner
    {
        public int CallCount { get; private set; }
        public IReadOnlyList<string>? LastArguments { get; private set; }
        public string? LastWorkingDirectory { get; private set; }
        public string Outcome { get; init; } = "success";
        public CancellationTokenSource? CallerCancellation { get; init; }

        public async Task<CommandExecutionDto> RunAsync(
            string workingDirectory,
            string targetPath,
            IReadOnlyList<string> arguments,
            CancellationToken ct)
        {
            CallCount++;
            LastWorkingDirectory = workingDirectory;
            LastArguments = arguments;
            if (Outcome == "exception")
            {
                throw new InvalidOperationException("simulated drain failure");
            }

            if (Outcome == "caller-cancellation")
            {
                CallerCancellation!.Cancel();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }

            if (Outcome == "timeout")
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }

            var exitCode = Outcome == "nonzero" ? 1 : 0;
            return new CommandExecutionDto(
                Command: "dotnet",
                Arguments: arguments,
                WorkingDirectory: workingDirectory,
                TargetPath: targetPath,
                ExitCode: exitCode,
                Succeeded: exitCode == 0,
                DurationMs: 0,
                StdOut: string.Empty,
                StdErr: string.Empty);
        }
    }

    private sealed class RecordingUnexpectedExceptionReporter : IUnexpectedExceptionReporter
    {
        public List<(Exception Exception, UnexpectedExceptionCategory Category)> Reports { get; } = [];

        public UnexpectedExceptionDetails ReportUnexpected(
            Exception exception,
            UnexpectedExceptionCategory category)
        {
            Reports.Add((exception, category));
            return PublicExceptionDetailPolicy.ProjectUnexpected(exception, correlationId: "test-correlation");
        }
    }

    private sealed class RecordingLoggerFactory(RecordingLogger logger) : ILoggerFactory
    {
        public ILogger CreateLogger(string categoryName) => logger;

        public void AddProvider(ILoggerProvider provider) { }

        public void Dispose() { }
    }

    private sealed class RecordingLogger : ILogger
    {
        private readonly List<LogEntry> _entries = [];

        public IReadOnlyList<LogEntry> Entries => _entries;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

    private sealed class PassthroughGate : IWorkspaceExecutionGate
    {
        public Task<T> RunReadAsync<T>(string workspaceId, Func<CancellationToken, Task<T>> action, CancellationToken ct) =>
            action(ct);

        public Task<T> RunWriteAsync<T>(
            string workspaceId,
            Func<CancellationToken, Task<T>> action,
            CancellationToken ct,
            bool applyStalenessPolicy = true) =>
            action(ct);

        public Task<T> RunLoadGateAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct) =>
            action(ct);

        public void RemoveGate(string workspaceId) { }
    }

    private sealed class FakeWorkspaceManagerForDrain(WorkspaceStatusDto? status) : IWorkspaceManager
    {
        public event Action<string>? WorkspaceClosed { add { } remove { } }
        public event Action<string>? WorkspaceReloaded { add { } remove { } }

        public Task<WorkspaceStatusDto> LoadAsync(string path, EvictPolicy evictPolicy, CancellationToken ct) =>
            Task.FromResult(status ?? throw new KeyNotFoundException("No status configured."));

        public Task<WorkspaceStatusDto> ReloadAsync(string workspaceId, CancellationToken ct) =>
            throw new NotSupportedException();

        public bool ContainsWorkspace(string workspaceId) =>
            status is not null && workspaceId == status.WorkspaceId;

        public bool IsStale(string workspaceId) => false;

        public bool Close(string workspaceId) =>
            status is not null && workspaceId == status.WorkspaceId;

        public IReadOnlyList<WorkspaceStatusDto> ListWorkspaces() =>
            status is not null ? [status] : [];

        public WorkspaceStatusDto GetStatus(string workspaceId) =>
            status is not null && workspaceId == status.WorkspaceId
                ? status
                : throw new KeyNotFoundException($"Workspace not found: {workspaceId}");

        public Task<WorkspaceStatusDto> GetStatusAsync(string workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(GetStatus(workspaceId));

        public ProjectGraphDto GetProjectGraph(string workspaceId) => throw new NotSupportedException();

        public Task<IReadOnlyList<GeneratedDocumentDto>> GetSourceGeneratedDocumentsAsync(
            string workspaceId, string? projectName, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<string?> GetSourceTextAsync(string workspaceId, string filePath, CancellationToken ct) =>
            Task.FromResult<string?>(null);

        public int GetCurrentVersion(string workspaceId) =>
            status?.WorkspaceVersion ?? throw new KeyNotFoundException();

        public Solution GetCurrentSolution(string workspaceId) => throw new NotSupportedException();

        public Project? GetProject(string workspaceId, string projectNameOrPath) => null;

        public bool TryApplyChanges(string workspaceId, Solution newSolution) => throw new NotSupportedException();

        public void RestoreVersion(string workspaceId, int version) { }
    }
}
