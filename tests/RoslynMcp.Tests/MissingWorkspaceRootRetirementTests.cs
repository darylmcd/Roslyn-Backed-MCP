namespace RoslynMcp.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

[DoNotParallelize]
[TestClass]
public sealed class MissingWorkspaceRootRetirementTests : SharedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task DeletedWorkspaceRoot_RetiresSessionAndRaisesClosedOnce()
    {
        var solutionPath = CreateSampleSolutionCopy();
        var root = Path.GetDirectoryName(solutionPath)!;
        var status = await WorkspaceManager.LoadAsync(solutionPath, CancellationToken.None);
        var closedCount = 0;
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnClosed(string workspaceId)
        {
            if (workspaceId == status.WorkspaceId)
            {
                Interlocked.Increment(ref closedCount);
                closed.TrySetResult();
            }
        }

        WorkspaceManager.WorkspaceClosed += OnClosed;
        try
        {
            Directory.Delete(root, recursive: true);

            var completed = await Task.WhenAny(closed.Task, Task.Delay(TimeSpan.FromSeconds(10)));

            Assert.AreSame(closed.Task, completed,
                "Deleting a loaded worktree root must retire its workspace session.");
            Assert.AreEqual(1, Volatile.Read(ref closedCount));
            Assert.IsFalse(WorkspaceManager.ContainsWorkspace(status.WorkspaceId));
            Assert.IsFalse(WorkspaceManager.ListWorkspaces()
                .Any(workspace => workspace.WorkspaceId == status.WorkspaceId));
        }
        finally
        {
            WorkspaceManager.WorkspaceClosed -= OnClosed;
            WorkspaceManager.Close(status.WorkspaceId);
            DeleteDirectoryIfExists(root);
        }
    }

    [TestMethod]
    public async Task DeletedSolutionFile_DrainsInFlightReadBeforeRetirement()
    {
        var solutionPath = CreateSampleSolutionCopy();
        var root = Path.GetDirectoryName(solutionPath)!;
        var status = await WorkspaceManager.LoadAsync(solutionPath, CancellationToken.None);
        var readStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnClosed(string workspaceId)
        {
            if (workspaceId == status.WorkspaceId)
            {
                closed.TrySetResult();
            }
        }

        WorkspaceManager.WorkspaceClosed += OnClosed;
        try
        {
            var inFlightRead = WorkspaceExecutionGate.RunReadAsync(
                status.WorkspaceId,
                async _ =>
                {
                    readStarted.TrySetResult();
                    await releaseRead.Task;
                    return true;
                },
                CancellationToken.None);
            await readStarted.Task;

            File.Delete(solutionPath);
            await Task.Delay(TimeSpan.FromMilliseconds(750));

            Assert.IsFalse(closed.Task.IsCompleted,
                "Retirement must wait for the per-workspace write gate to drain active readers.");
            Assert.IsTrue(WorkspaceManager.ContainsWorkspace(status.WorkspaceId));

            releaseRead.TrySetResult();
            await inFlightRead;
            var completed = await Task.WhenAny(closed.Task, Task.Delay(TimeSpan.FromSeconds(10)));

            Assert.AreSame(closed.Task, completed,
                "Deleting the loaded solution file must retire the session after readers drain.");
            Assert.IsFalse(WorkspaceManager.ContainsWorkspace(status.WorkspaceId));
        }
        finally
        {
            releaseRead.TrySetResult();
            WorkspaceManager.WorkspaceClosed -= OnClosed;
            WorkspaceManager.Close(status.WorkspaceId);
            DeleteDirectoryIfExists(root);
        }
    }

    [TestMethod]
    public async Task TransientGateFailure_RetriesUntilMissingWorkspaceRetires()
    {
        var solutionPath = CreateSampleSolutionCopy();
        var root = Path.GetDirectoryName(solutionPath)!;
        var watcher = new SignalingFileWatcher();
        var gate = new RetryingGate();
        using var manager = CreateManager(watcher, gate);

        try
        {
            var status = await manager.LoadAsync(solutionPath, CancellationToken.None);
            File.Delete(solutionPath);
            watcher.RaiseRootMissing(status.WorkspaceId);

            await gate.Succeeded.Task.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.IsTrue(gate.Attempts >= 2,
                "A transient write-gate failure must retain and retry the lifecycle signal.");
            Assert.IsFalse(manager.ContainsWorkspace(status.WorkspaceId));
            Assert.AreEqual(1, gate.RemoveGateCalls);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [TestMethod]
    public async Task Dispose_JoinsInFlightRetirementBeforeDisposingDependencies()
    {
        var solutionPath = CreateSampleSolutionCopy();
        var root = Path.GetDirectoryName(solutionPath)!;
        var watcher = new SignalingFileWatcher();
        var gate = new BlockingGate();
        var manager = CreateManager(watcher, gate);

        try
        {
            var status = await manager.LoadAsync(solutionPath, CancellationToken.None);
            File.Delete(solutionPath);
            watcher.RaiseRootMissing(status.WorkspaceId);
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

            var dispose = Task.Run(manager.Dispose);
            await Task.Delay(TimeSpan.FromMilliseconds(200));

            Assert.IsFalse(dispose.IsCompleted,
                "Dispose must join manager-owned retirement work before tearing down dependencies.");
            Assert.IsFalse(watcher.Disposed,
                "The watcher must remain alive until the retirement task has drained.");

            gate.Release.TrySetResult();
            await dispose.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.IsTrue(watcher.Disposed);
            Assert.AreEqual(1, gate.RemoveGateCalls);
        }
        finally
        {
            gate.Release.TrySetResult();
            manager.Dispose();
            DeleteDirectoryIfExists(root);
        }
    }

    private static WorkspaceManager CreateManager(
        IFileWatcherService watcher,
        IWorkspaceExecutionGate gate) =>
        new(
            NullLogger<WorkspaceManager>.Instance,
            new PreviewStore(),
            watcher,
            new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 4 },
            cacheStore: null,
            evictionGate: new Lazy<IWorkspaceExecutionGate>(() => gate));

    private sealed class SignalingFileWatcher : IFileWatcherService
    {
        public event Action<string>? WorkspaceRootMissing;

        public bool Disposed { get; private set; }

        public void RaiseRootMissing(string workspaceId) => WorkspaceRootMissing?.Invoke(workspaceId);

        public void Watch(string workspaceId, string workspacePath) { }

        public void Unwatch(string workspaceId) { }

        public bool IsStale(string workspaceId) => false;

        public Task WaitForStaleAsync(string workspaceId, CancellationToken ct) => Task.CompletedTask;

        public string? GetStaleReason(string workspaceId) => null;

        public void MarkStale(string workspaceId, string reason) { }

        public void ClearStale(string workspaceId) { }

        public void Dispose() => Disposed = true;
    }

    private sealed class RetryingGate : IWorkspaceExecutionGate
    {
        private int _attempts;
        private int _removeGateCalls;

        public int Attempts => Volatile.Read(ref _attempts);

        public int RemoveGateCalls => Volatile.Read(ref _removeGateCalls);

        public TaskCompletionSource Succeeded { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<T> RunReadAsync<T>(
            string workspaceId,
            Func<CancellationToken, Task<T>> action,
            CancellationToken ct) => action(ct);

        public async Task<T> RunWriteAsync<T>(
            string workspaceId,
            Func<CancellationToken, Task<T>> action,
            CancellationToken ct,
            bool applyStalenessPolicy = true)
        {
            if (Interlocked.Increment(ref _attempts) == 1)
            {
                throw new TimeoutException("Injected transient gate failure.");
            }

            var result = await action(ct);
            Succeeded.TrySetResult();
            return result;
        }

        public Task<T> RunLoadGateAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct) =>
            action(ct);

        public void RemoveGate(string workspaceId) => Interlocked.Increment(ref _removeGateCalls);
    }

    private sealed class BlockingGate : IWorkspaceExecutionGate
    {
        private int _removeGateCalls;

        public int RemoveGateCalls => Volatile.Read(ref _removeGateCalls);

        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<T> RunReadAsync<T>(
            string workspaceId,
            Func<CancellationToken, Task<T>> action,
            CancellationToken ct) => action(ct);

        public async Task<T> RunWriteAsync<T>(
            string workspaceId,
            Func<CancellationToken, Task<T>> action,
            CancellationToken ct,
            bool applyStalenessPolicy = true)
        {
            Entered.TrySetResult();
            await Release.Task;
            return await action(CancellationToken.None);
        }

        public Task<T> RunLoadGateAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct) =>
            action(ct);

        public void RemoveGate(string workspaceId) => Interlocked.Increment(ref _removeGateCalls);
    }
}
