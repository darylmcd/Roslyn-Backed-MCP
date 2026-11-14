using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests.Services;

/// <summary>
/// Direct unit tests for <see cref="GatedCommandExecutor"/>'s per-workspace gate lifecycle
/// (<c>workspace-infra-resource-cleanup-hygiene</c>). The executor is an <c>AddSingleton</c>, so
/// without pruning its <c>_workspaceCommandGates</c> dictionary would accumulate one
/// <see cref="SemaphoreSlim"/> per distinct workspaceId for the process lifetime. These tests
/// prove the gate is removed when the workspace's <see cref="IWorkspaceManager.WorkspaceClosed"/>
/// event fires (explicit close AND LRU eviction both raise it), keeping the dictionary bounded.
/// </summary>
[TestClass]
public sealed class GatedCommandExecutorTests
{
    [TestMethod]
    public async Task WorkspaceClosed_PrunesGate_AcrossRepeatedLoadCloseCycles()
    {
        var ws = new FakeWorkspaceManager();
        var runner = new StubCommandRunner();
        using var executor = new GatedCommandExecutor(ws, runner, NullLogger<GatedCommandExecutor>.Instance);

        // Drive 5 distinct load→execute→close cycles. Without the WorkspaceClosed subscription
        // the dictionary would grow to 5; with it, each close prunes its entry back to 0.
        for (var i = 0; i < 5; i++)
        {
            var workspaceId = $"ws-{i}";
            await executor.ExecuteAsync(
                workspaceId, Path.Combine(Path.GetTempPath(), "Sample.sln"), new[] { "build" }, TimeSpan.FromMinutes(1), default);

            Assert.AreEqual(1, GetGateCount(executor),
                "A gate is created lazily on ExecuteAsync for the active workspace.");

            ws.RaiseWorkspaceClosed(workspaceId);

            Assert.AreEqual(0, GetGateCount(executor),
                "Closing the workspace must prune its command gate so the dictionary stays bounded.");
        }
    }

    [TestMethod]
    public async Task WorkspaceClosed_RemovesOnlyClosedWorkspaceGate()
    {
        var ws = new FakeWorkspaceManager();
        var runner = new StubCommandRunner();
        using var executor = new GatedCommandExecutor(ws, runner, NullLogger<GatedCommandExecutor>.Instance);

        await executor.ExecuteAsync("ws-a", Path.Combine(Path.GetTempPath(), "A.sln"), new[] { "build" }, TimeSpan.FromMinutes(1), default);
        await executor.ExecuteAsync("ws-b", Path.Combine(Path.GetTempPath(), "B.sln"), new[] { "build" }, TimeSpan.FromMinutes(1), default);
        Assert.AreEqual(2, GetGateCount(executor));

        ws.RaiseWorkspaceClosed("ws-a");

        Assert.AreEqual(1, GetGateCount(executor), "Only the closed workspace's gate is removed.");
        Assert.IsTrue(GateContains(executor, "ws-b"), "The still-open workspace's gate is retained.");
        Assert.IsFalse(GateContains(executor, "ws-a"), "The closed workspace's gate is gone.");
    }

    [TestMethod]
    public void WorkspaceClosed_ForUnknownWorkspace_IsNoOp()
    {
        var ws = new FakeWorkspaceManager();
        var runner = new StubCommandRunner();
        using var executor = new GatedCommandExecutor(ws, runner, NullLogger<GatedCommandExecutor>.Instance);

        // No gate was ever created; a close for an unseen id must not throw.
        ws.RaiseWorkspaceClosed("never-seen");
        Assert.AreEqual(0, GetGateCount(executor));
    }

    [TestMethod]
    public async Task Dispose_UnsubscribesFromWorkspaceClosed()
    {
        var ws = new FakeWorkspaceManager();
        var runner = new StubCommandRunner();
        var executor = new GatedCommandExecutor(ws, runner, NullLogger<GatedCommandExecutor>.Instance);

        await executor.ExecuteAsync("ws-x", Path.Combine(Path.GetTempPath(), "X.sln"), new[] { "build" }, TimeSpan.FromMinutes(1), default);
        executor.Dispose();

        Assert.AreEqual(0, ws.WorkspaceClosedSubscriberCount);
        ws.RaiseWorkspaceClosed("ws-x");
        Assert.AreEqual(0, GetGateCount(executor));
    }

    [TestMethod]
    public async Task Dispose_WithRunningCommand_PreservesItsResult()
    {
        var ws = new FakeWorkspaceManager();
        var runner = new ControlledCommandRunner();
        using var executor = new GatedCommandExecutor(ws, runner, NullLogger<GatedCommandExecutor>.Instance);
        var target = Path.Combine(TestFixtureFileSystem.FindRepositoryRoot(), "RoslynMcp.slnx");

        var running = executor.ExecuteAsync("workspace", target, ["build"], TimeSpan.FromSeconds(30), CancellationToken.None);
        try
        {
            await runner.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            executor.Dispose();
        }
        finally
        {
            runner.Release();
        }

        Assert.IsTrue((await running).Succeeded);
    }

    [TestMethod]
    public async Task DisposeAsync_WithActiveAndQueuedCommands_DrainsAdmittedCalls()
    {
        var runner = new ControlledCommandRunner();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRoslynServices();
        services.AddSingleton<IDotnetCommandRunner>(runner);
        await using var provider = services.BuildServiceProvider();
        var executor = (GatedCommandExecutor)provider.GetRequiredService<IGatedCommandExecutor>();
        var target = Path.Combine(TestFixtureFileSystem.FindRepositoryRoot(), "RoslynMcp.slnx");
        var budget = TimeSpan.FromSeconds(30);
        using var cancellation = new CancellationTokenSource();
        var running = executor.ExecuteAsync("workspace", target, ["build"], budget, CancellationToken.None);
        Task<CommandExecutionDto>? queued = null;
        Task<CommandExecutionDto>? cancelled = null;
        Task? providerShutdown = null;
        try
        {
            await runner.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            queued = executor.ExecuteAsync("workspace", target, ["build"], budget, CancellationToken.None);
            cancelled = executor.ExecuteAsync("workspace", target, ["build"], budget, cancellation.Token);

            providerShutdown = provider.DisposeAsync().AsTask();
            Assert.IsFalse(providerShutdown.IsCompleted);
            await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() =>
                executor.ExecuteAsync("other-workspace", target, ["build"], budget, CancellationToken.None));
            var executorShutdown = executor.DisposeAsync().AsTask();
            Assert.IsFalse(executorShutdown.IsCompleted);

            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await cancelled.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.IsFalse(queued.IsCompleted);
            Assert.IsFalse(executorShutdown.IsCompleted);
            runner.Release();
            await Task.WhenAll(providerShutdown, executorShutdown).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsTrue((await running).Succeeded);
            Assert.IsTrue((await queued).Succeeded);
        }
        finally
        {
            runner.Release();
            cancellation.Cancel();
            if (providerShutdown is not null)
            {
                await providerShutdown.WaitAsync(TimeSpan.FromSeconds(10));
            }

            if (cancelled is not null)
            {
                try { await cancelled; }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            }

            if (queued is not null)
            {
                await Task.WhenAll(running, queued);
            }
            else
            {
                await running;
            }
        }
    }

    [TestMethod]
    public async Task ExecuteAsync_GlobalGateSaturated_CountsQueueWaitAgainstTimeout()
    {
        var ws = new FakeWorkspaceManager();
        var runner = new StubCommandRunner();
        using var executor = new GatedCommandExecutor(ws, runner, NullLogger<GatedCommandExecutor>.Instance);
        var globalGate = GetGlobalGate(executor);
        var acquiredPermits = globalGate.CurrentCount;

        for (var i = 0; i < acquiredPermits; i++)
        {
            await globalGate.WaitAsync();
        }

        try
        {
            var exception = await Assert.ThrowsExactlyAsync<TimeoutException>(() =>
                executor.ExecuteAsync(
                    "ws-queued",
                    Path.Combine(Path.GetTempPath(), "Queued.sln"),
                    new[] { "build" },
                    TimeSpan.FromMilliseconds(100),
                    CancellationToken.None));

            StringAssert.Contains(exception.Message, "including queue wait");

            // gate-timeout-exception-drops-inner-oce: the reclassification must carry the original
            // OperationCanceledException so cancellation provenance (which token fired, original
            // stack trace) survives into logs instead of being discarded.
            Assert.IsInstanceOfType<OperationCanceledException>(exception.InnerException,
                "The reclassified TimeoutException should preserve the caught OCE as InnerException.");
        }
        finally
        {
            globalGate.Release(acquiredPermits);
        }
    }

    [TestMethod]
    public async Task ExecuteAsync_WithoutPatterns_ForwardsNull()
    {
        var runner = new StubCommandRunner();
        using IGatedCommandExecutor executor = new GatedCommandExecutor(
            new FakeWorkspaceManager(), runner, NullLogger<GatedCommandExecutor>.Instance);
        var target = Path.Combine(TestFixtureFileSystem.FindRepositoryRoot(), "RoslynMcp.slnx");

        await executor.ExecuteAsync("workspace", target, ["build"], TimeSpan.FromSeconds(10), CancellationToken.None);

        Assert.HasCount(1, runner.PatternCalls);
        Assert.IsNull(runner.PatternCalls[0]);
    }

    [TestMethod]
    [TestCategory("Process")]
    [Timeout(30_000)]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExecuteAsync_ForwardsPatternsToStreamingProcess(bool stderr)
    {
        using IGatedCommandExecutor executor = new GatedCommandExecutor(
            new FakeWorkspaceManager(), new PowerShellRunner(), NullLogger<GatedCommandExecutor>.Instance);
        var target = Path.Combine(TestFixtureFileSystem.FindRepositoryRoot(), "RoslynMcp.slnx");
        const string sentinel = "gated-executor-early-kill";
        const string reason = "test process emitted the sentinel";
        var stream = stderr ? "Error" : "Out";
        string[] arguments =
        [
            "-NoProfile", "-NonInteractive", "-Command",
            $"[Console]::{stream}.WriteLine('{sentinel}'); [Threading.Thread]::Sleep(25000)"
        ];
        EarlyKillPattern[] patterns = [new(new Regex(sentinel), reason)];

        var execution = await executor.ExecuteAsync(
            "workspace", target, arguments, TimeSpan.FromSeconds(20), patterns, CancellationToken.None);

        Assert.IsFalse(execution.Succeeded);
        Assert.AreEqual(reason, execution.EarlyKillReason);
        StringAssert.Contains(stderr ? execution.StdErr : execution.StdOut, sentinel);
        CollectionAssert.AreEqual(arguments, execution.Arguments.ToArray());
    }

    private static int GetGateCount(GatedCommandExecutor executor)
        => ((ICollection)GetGateDictionary(executor)).Count;

    private static bool GateContains(GatedCommandExecutor executor, string workspaceId)
        => ((IDictionary)GetGateDictionary(executor)).Contains(workspaceId);

    private static object GetGateDictionary(GatedCommandExecutor executor)
    {
        // _workspaceCommandGates is private (InternalsVisibleTo does not reach it), so read it via
        // reflection rather than widening the field's accessibility just for the test.
        var field = typeof(GatedCommandExecutor).GetField(
            "_workspaceCommandGates", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(field, "Expected private field _workspaceCommandGates to exist.");
        return field!.GetValue(executor)!;
    }

    private static SemaphoreSlim GetGlobalGate(GatedCommandExecutor executor)
    {
        var field = typeof(GatedCommandExecutor).GetField(
            "_globalCommandGate",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(field, "Expected private field _globalCommandGate to exist.");
        return (SemaphoreSlim)field.GetValue(executor)!;
    }

    /// <summary>
    /// Stub runner — <see cref="GatedCommandExecutor.ExecuteAsync(string, string, IReadOnlyList{string}, TimeSpan, CancellationToken)"/>
    /// calls it once per invocation; returns a trivial success result without spawning a process.
    /// </summary>
    private sealed class StubCommandRunner : IDotnetCommandRunner
    {
        public List<IReadOnlyList<EarlyKillPattern>?> PatternCalls { get; } = [];

        public Task<CommandExecutionDto> RunAsync(
            string workingDirectory, string targetPath, IReadOnlyList<string> arguments,
            IReadOnlyList<EarlyKillPattern>? earlyKillPatterns, CancellationToken ct)
        {
            PatternCalls.Add(earlyKillPatterns);
            return Task.FromResult(new CommandExecutionDto(
                "dotnet", arguments, workingDirectory, targetPath, 0, true, 1, string.Empty, string.Empty));
        }
    }

    private sealed class ControlledCommandRunner : IDotnetCommandRunner
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => _entered.Task;

        public void Release() => _release.TrySetResult();

        public async Task<CommandExecutionDto> RunAsync(
            string workingDirectory, string targetPath, IReadOnlyList<string> arguments,
            IReadOnlyList<EarlyKillPattern>? earlyKillPatterns, CancellationToken ct)
        {
            _entered.TrySetResult();
            await _release.Task.WaitAsync(ct);
            return new CommandExecutionDto(
                "dotnet", arguments, workingDirectory, targetPath, 0, true, 1, string.Empty, string.Empty);
        }
    }

    private sealed class PowerShellRunner : IDotnetCommandRunner
    {
        private readonly IDotnetCommandRunner _runner = new DotnetCommandRunner();

        public Task<CommandExecutionDto> RunAsync(
            string workingDirectory, string targetPath, IReadOnlyList<string> arguments,
            IReadOnlyList<EarlyKillPattern>? earlyKillPatterns, CancellationToken ct) =>
            _runner.RunAsync(workingDirectory, targetPath, arguments, earlyKillPatterns,
                OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh", ct);
    }

    /// <summary>
    /// Minimal <see cref="IWorkspaceManager"/> stand-in exposing <see cref="RaiseWorkspaceClosed"/>
    /// so the test can drive the close/eviction signal. Unused members throw to surface accidental
    /// coupling immediately.
    /// </summary>
    private sealed class FakeWorkspaceManager : IWorkspaceManager
    {
        public event Action<string>? WorkspaceClosed;
        public event Action<string>? WorkspaceReloaded;

        public int WorkspaceClosedSubscriberCount => WorkspaceClosed?.GetInvocationList().Length ?? 0;

        public void RaiseWorkspaceClosed(string workspaceId) => WorkspaceClosed?.Invoke(workspaceId);
        public void RaiseWorkspaceReloaded(string workspaceId) => WorkspaceReloaded?.Invoke(workspaceId);

        // ----- Unused by GatedCommandExecutor's gate lifecycle; throw to surface coupling -----
        public Task<WorkspaceStatusDto> LoadAsync(string path, EvictPolicy evictPolicy, CancellationToken ct) => throw new NotSupportedException();
        public Task<WorkspaceStatusDto> ReloadAsync(string workspaceId, CancellationToken ct) => throw new NotSupportedException();
        public bool ContainsWorkspace(string workspaceId) => throw new NotSupportedException();
        public bool IsStale(string workspaceId) => throw new NotSupportedException();
        public bool Close(string workspaceId) => throw new NotSupportedException();
        public IReadOnlyList<WorkspaceStatusDto> ListWorkspaces() => throw new NotSupportedException();
        public WorkspaceStatusDto GetStatus(string workspaceId) => throw new NotSupportedException();
        public Task<WorkspaceStatusDto> GetStatusAsync(string workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ProjectGraphDto GetProjectGraph(string workspaceId) => throw new NotSupportedException();
        public Task<IReadOnlyList<GeneratedDocumentDto>> GetSourceGeneratedDocumentsAsync(string workspaceId, string? projectName, CancellationToken ct) => throw new NotSupportedException();
        public Task<string?> GetSourceTextAsync(string workspaceId, string filePath, CancellationToken ct) => throw new NotSupportedException();
        public int GetCurrentVersion(string workspaceId) => throw new NotSupportedException();
        public Solution GetCurrentSolution(string workspaceId) => throw new NotSupportedException();
        public bool TryApplyChanges(string workspaceId, Solution newSolution) => throw new NotSupportedException();
        public void RestoreVersion(string workspaceId, int version) => throw new NotSupportedException();
        public Project? GetProject(string workspaceId, string projectNameOrPath) => throw new NotSupportedException();
    }
}
