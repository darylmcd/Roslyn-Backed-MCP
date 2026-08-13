using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Contracts;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression coverage for <c>workspace-close-with-drain-processes-for-teardown</c>:
/// <see cref="WorkspaceTools.CloseWorkspace"/> with <c>drainProcesses=true</c> invokes
/// <see cref="IDotnetCommandRunner.RunAsync"/> with <c>build-server shutdown</c> after
/// the session is removed, and with <c>drainProcesses=false</c> (the default) it does not.
/// </summary>
[DoNotParallelize]
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
            server: null!,
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

            var json = await WorkspaceTools.CloseWorkspace(
                server: null!,
                gate: gate,
                workspace: fakeWorkspace,
                commandRunner: commandRunner,
                workspaceId: expectedWorkspaceId,
                drainProcesses: true,
                ct: CancellationToken.None,
                getProcessesByName: getProcessesByName);

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

            var json = await WorkspaceTools.CloseWorkspace(
                server: null!,
                gate: gate,
                workspace: fakeWorkspace,
                commandRunner: commandRunner,
                workspaceId: expectedWorkspaceId,
                drainProcesses: true,
                ct: CancellationToken.None,
                getProcessesByName: getProcessesByName);

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
            server: null!,
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
        var logger = new RecordingLogger();
        var loggerFactory = new RecordingLoggerFactory(logger);

        var json = await WorkspaceTools.CloseWorkspace(
            server: null!,
            gate: gate,
            workspace: fakeWorkspace,
            commandRunner: commandRunner,
            workspaceId: unknownId,
            drainProcesses: true,
            loggerFactory: loggerFactory,
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        // The close returns success=false because the workspace was not found.
        Assert.IsFalse(doc.RootElement.GetProperty("success").GetBoolean(),
            "Closing an unknown workspaceId must return success=false.");

        // The drain must have been skipped — no call to commandRunner.
        Assert.AreEqual(0, commandRunner.CallCount,
            "The drain must be skipped when GetStatus throws (unknown workspace).");

        Assert.IsTrue(
            logger.Entries.Any(e =>
                e.Level == LogLevel.Debug &&
                e.Exception is KeyNotFoundException &&
                e.Message.Contains("skipped process drain because loaded path lookup failed", StringComparison.Ordinal)),
            "Loaded-path lookup failures must produce a Debug log signal while preserving close success semantics.");
    }

    [TestMethod]
    public async Task CloseWorkspace_DrainFailure_ReturnsSuccessAndLogsDebug()
    {
        const string expectedWorkspaceId = "test-ws-drain-fail";
        var loadedPath = Path.Combine(Path.GetTempPath(), "repo", "FailingDrain.slnx");

        var status = CreateStatus(expectedWorkspaceId, loadedPath);
        var fakeWorkspace = new FakeWorkspaceManagerForDrain(status);
        var gate = new PassthroughGate();
        var commandRunner = new RecordingDotnetCommandRunner
        {
            ExceptionToThrow = new InvalidOperationException("simulated drain failure")
        };
        var logger = new RecordingLogger();
        var loggerFactory = new RecordingLoggerFactory(logger);

        var json = await WorkspaceTools.CloseWorkspace(
            server: null!,
            gate: gate,
            workspace: fakeWorkspace,
            commandRunner: commandRunner,
            workspaceId: expectedWorkspaceId,
            drainProcesses: true,
            loggerFactory: loggerFactory,
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.GetProperty("success").GetBoolean(),
            "Drain failures are non-fatal; workspace_close must still return the close result.");
        Assert.AreEqual(1, commandRunner.CallCount,
            "drainProcesses=true must still attempt the drain before logging the failure.");

        Assert.IsTrue(
            logger.Entries.Any(e =>
                e.Level == LogLevel.Debug &&
                e.Exception is InvalidOperationException &&
                e.Message.Contains("process drain failed", StringComparison.Ordinal)),
            "Drain exceptions must produce a Debug log signal while preserving close success semantics.");
    }

    [TestMethod]
    public async Task CloseWorkspace_DrainCancellationAfterClose_ReturnsSuccessAndLogsDebug()
    {
        const string expectedWorkspaceId = "test-ws-drain-canceled";
        var loadedPath = Path.Combine(Path.GetTempPath(), "repo", "CanceledDrain.slnx");

        var status = CreateStatus(expectedWorkspaceId, loadedPath);
        var fakeWorkspace = new FakeWorkspaceManagerForDrain(status);
        var gate = new PassthroughGate();
        var commandRunner = new RecordingDotnetCommandRunner
        {
            ExceptionToThrow = new OperationCanceledException("simulated drain cancellation")
        };
        var logger = new RecordingLogger();
        var loggerFactory = new RecordingLoggerFactory(logger);

        var json = await WorkspaceTools.CloseWorkspace(
            server: null!,
            gate: gate,
            workspace: fakeWorkspace,
            commandRunner: commandRunner,
            workspaceId: expectedWorkspaceId,
            drainProcesses: true,
            loggerFactory: loggerFactory,
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.GetProperty("success").GetBoolean(),
            "Post-close drain cancellation is non-fatal; workspace_close must still return the close result.");

        Assert.IsTrue(
            logger.Entries.Any(e =>
                e.Level == LogLevel.Debug &&
                e.Exception is OperationCanceledException &&
                e.Message.Contains("process drain failed", StringComparison.Ordinal)),
            "Post-close drain cancellation must produce a Debug log signal while preserving close success semantics.");
    }

    [TestMethod]
    public async Task CloseWorkspace_DrainNonZeroExit_ReturnsSuccessAndLogsDebug()
    {
        const string expectedWorkspaceId = "test-ws-drain-nonzero";
        var loadedPath = Path.Combine(Path.GetTempPath(), "repo", "NonZeroDrain.slnx");

        var status = CreateStatus(expectedWorkspaceId, loadedPath);
        var fakeWorkspace = new FakeWorkspaceManagerForDrain(status);
        var gate = new PassthroughGate();
        var commandRunner = new RecordingDotnetCommandRunner { ExitCode = 1 };
        var logger = new RecordingLogger();
        var loggerFactory = new RecordingLoggerFactory(logger);

        var json = await WorkspaceTools.CloseWorkspace(
            server: null!,
            gate: gate,
            workspace: fakeWorkspace,
            commandRunner: commandRunner,
            workspaceId: expectedWorkspaceId,
            drainProcesses: true,
            loggerFactory: loggerFactory,
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.GetProperty("success").GetBoolean(),
            "Non-zero drain exits are non-fatal; workspace_close must still return the close result.");

        Assert.IsTrue(
            logger.Entries.Any(e =>
                e.Level == LogLevel.Debug &&
                e.Message.Contains("process drain exited with code 1", StringComparison.Ordinal)),
            "Non-zero drain exits must produce a Debug log signal while preserving close success semantics.");
    }

    [TestMethod]
    public async Task LoadWorkspace_ReturnsStatus_AndNotificationHelperLogsFailures()
    {
        const string expectedWorkspaceId = "test-ws-load-notify";
        var loadedPath = Path.Combine(Path.GetTempPath(), "repo", "NotifyFailure.slnx");

        var status = CreateStatus(expectedWorkspaceId, loadedPath);
        var fakeWorkspace = new FakeWorkspaceManagerForDrain(status);
        var gate = new PassthroughGate();
        var commandRunner = new RecordingDotnetCommandRunner();
        var logger = new RecordingLogger();
        var loggerFactory = new RecordingLoggerFactory(logger);
        await using var session = await McpRootsTestServerFactory.CreateWithSanctionedRootAsync(
            Path.GetTempPath(),
            CancellationToken.None);

        var json = await WorkspaceTools.LoadWorkspace(
            server: session.Server,
            gate: gate,
            workspace: fakeWorkspace,
            warmService: new ThrowingWorkspaceWarmService(),
            commandRunner: commandRunner,
            path: loadedPath,
            verbose: false,
            autoRestore: false,
            prewarm: false,
            loggerFactory: loggerFactory,
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual(expectedWorkspaceId, doc.RootElement.GetProperty("workspaceId").GetString(),
            "Resource-list notification failures are non-fatal; workspace_load must still return its status payload.");

        // The connected test server accepts the notification. Exercise the same internal helper
        // with a missing transport context to pin its failure logging independently of path
        // validation, which now correctly rejects null/no-options workspace-load calls.
        await WorkspaceTools.NotifyResourcesChangedAsync(null!, "workspace_load", logger);

        Assert.IsTrue(
            SpinWait.SpinUntil(
                () => logger.Entries.Any(e =>
                    e.Level == LogLevel.Debug &&
                    e.Message.Contains("workspace_load could not notify clients", StringComparison.Ordinal)),
                TimeSpan.FromSeconds(2)),
            "Resource-list notification failures must produce a Debug log signal while preserving load success semantics.");
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

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
    /// MainModule.FileName prefix-matches the working directory) and starts it long-lived.
    /// </summary>
    private static Process StartLongLivedProcessUnder(string targetDirectory)
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

        return StartLongLived(copiedPath);
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
        public Exception? ExceptionToThrow { get; init; }
        public int ExitCode { get; init; }

        public Task<CommandExecutionDto> RunAsync(
            string workingDirectory,
            string targetPath,
            IReadOnlyList<string> arguments,
            CancellationToken ct)
        {
            CallCount++;
            LastWorkingDirectory = workingDirectory;
            LastArguments = arguments;
            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return Task.FromResult(new CommandExecutionDto(
                Command: "dotnet",
                Arguments: arguments,
                WorkingDirectory: workingDirectory,
                TargetPath: targetPath,
                ExitCode: ExitCode,
                Succeeded: ExitCode == 0,
                DurationMs: 0,
                StdOut: string.Empty,
                StdErr: string.Empty));
        }
    }

    private sealed class ThrowingWorkspaceWarmService : IWorkspaceWarmService
    {
        public Task<WorkspaceWarmResult> WarmAsync(string workspaceId, string[]? projects, CancellationToken ct) =>
            throw new NotSupportedException("This test disables prewarm.");
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
