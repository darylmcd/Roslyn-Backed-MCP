namespace RoslynMcp.Tests;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Services;

// donotparallelize-audit-wave-16: [DoNotParallelize] removed. Each test deletes only its own GUID-named sample
// copy under TestTempRoot.Current; the FileWatcherService root/identity watchers are filtered to that exact
// directory/solution name, and WorkspaceClosed handlers ignore every id but the test's own. Tests 3-4 use a
// private WorkspaceManager with a fake watcher and gate. Negative-timing assertions (not retired while a read
// is held; Dispose blocked on the gate) cannot flip under load, and positive waits carry 10 s budgets. Verified
// with 3x runs alone, 3x alongside its wave-16 siblings, and 3x in a concurrent fixture-copy mix, green every time.
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
        // The retirement retry loop waits on the injected clock (300 ms, then 600 ms after the injected
        // failure), so the test drives time itself instead of racing a real timer under suite load.
        var clock = new FakeTimeProvider();
        using var manager = CreateManager(watcher, gate, clock);

        try
        {
            var status = await manager.LoadAsync(solutionPath, CancellationToken.None);
            File.Delete(solutionPath);
            watcher.RaiseRootMissing(status.WorkspaceId);

            // Production calls RemoveGate strictly AFTER RunWriteAsync returns, while Succeeded fires
            // inside RunWriteAsync. Await the RemoveGate signal itself instead of reading the counter
            // in that window (a race that surfaced once the class began running in parallel).
            // Each pass advances past the 2 s backoff ceiling; a pass before the retirement task has
            // registered its delay is harmless because the next pass fires the timer once it exists.
            using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!gate.GateRemoved.Task.IsCompleted)
            {
                guard.Token.ThrowIfCancellationRequested();
                clock.Advance(TimeSpan.FromSeconds(2));
                await Task.Yield();
            }

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
    public async Task Dispose_DefersInFlightRetirement_AsyncDisposalDrainsBeforeDependencies()
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
            await dispose.WaitAsync(TimeSpan.FromSeconds(10));
            var shutdown = ((IAsyncDisposable)(object)manager).DisposeAsync().AsTask();
            Assert.IsFalse(shutdown.IsCompleted,
                "Synchronous Dispose must return promptly while async disposal drains the same retirement.");
            Assert.IsFalse(watcher.Disposed,
                "The watcher must remain alive until the retirement task has drained.");

            gate.Release.TrySetResult();
            await shutdown.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.IsTrue(watcher.Disposed);
            Assert.AreEqual(1, gate.RemoveGateCalls);
        }
        finally
        {
            gate.Release.TrySetResult();
            manager.Dispose();
            if ((object)manager is IAsyncDisposable asynchronous) await asynchronous.DisposeAsync();
            DeleteDirectoryIfExists(root);
        }
    }

    [TestMethod]
    public async Task ProviderDispose_DrainsAdmittedRootRetirementThroughTheProductionGate()
    {
        var solutionPath = CreateSampleSolutionCopy();
        var root = Path.GetDirectoryName(solutionPath)!;
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var watcher = new SignalingFileWatcher
        {
            OnUnwatch = () =>
            {
                entered.TrySetResult();
                release.Wait(guard.Token);
            },
        };
        var logger = new RetirementLogger();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRoslynServices();
        services.AddSingleton<ILogger<WorkspaceManager>>(logger);
        services.AddSingleton<IFileWatcherService>(_ => watcher);
        await using var provider = services.BuildServiceProvider();

        Task? shutdown = null;
        try
        {
            var manager = provider.GetRequiredService<IWorkspaceManager>();
            var status = await manager.LoadAsync(solutionPath, EvictPolicy.Strict, guard.Token);
            File.Delete(solutionPath);
            watcher.RaiseRootMissing(status.WorkspaceId);
            await entered.Task.WaitAsync(guard.Token);

            shutdown = provider.DisposeAsync().AsTask();
            Assert.IsFalse(shutdown.IsCompleted);
            Assert.IsFalse(watcher.Disposed);
            release.Set();
            await shutdown.WaitAsync(guard.Token);

            Assert.IsFalse(manager.ContainsWorkspace(status.WorkspaceId));
            Assert.IsTrue(watcher.Disposed);
            Assert.IsNull(logger.RetirementFailure,
                $"An admitted retirement must release the production gate cleanly. {logger.RetirementFailure}");
        }
        finally
        {
            release.Set();
            if (shutdown is not null) await shutdown.WaitAsync(guard.Token);
            DeleteDirectoryIfExists(root);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ProviderDispose_LeavesLateRootRetirementToManagerShutdown(bool resolveGate)
    {
        var solutionPath = CreateSampleSolutionCopy();
        var root = Path.GetDirectoryName(solutionPath)!;
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var clock = new RetirementClock();
        var watcher = new SignalingFileWatcher();
        var logger = new RetirementLogger();
        var providerStop = new PausedProviderDisposal();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRoslynServices();
        services.AddSingleton<ILogger<WorkspaceManager>>(logger);
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IFileWatcherService>(_ => watcher);
        services.AddSingleton<PausedProviderDisposal>(_ => providerStop);
        await using var provider = services.BuildServiceProvider();

        Task? shutdown = null;
        Task? retirement = null;
        try
        {
            var manager = (WorkspaceManager)provider.GetRequiredService<IWorkspaceManager>();
            var status = await manager.LoadAsync(solutionPath, EvictPolicy.Strict, guard.Token);
            _ = provider.GetRequiredService<PausedProviderDisposal>();
            if (resolveGate)
                _ = provider.GetRequiredService<IWorkspaceExecutionGate>();
            File.Delete(solutionPath);

            shutdown = provider.DisposeAsync().AsTask();
            await providerStop.Entered.Task.WaitAsync(guard.Token);
            watcher.RaiseRootMissing(status.WorkspaceId);
            retirement = manager.GetRootRetirementTaskForTesting(status.WorkspaceId);
            Assert.IsNotNull(retirement, "The manager must still be live while the provider has stopped resolving services.");
            await clock.TimerCreated.Task.WaitAsync(guard.Token);
            clock.Advance();
            await retirement.WaitAsync(guard.Token);
            Assert.IsTrue(manager.ContainsWorkspace(status.WorkspaceId));
            Assert.IsNull(logger.RetirementFailure);

            providerStop.Release.SetResult();
            await shutdown.WaitAsync(guard.Token);
            Assert.IsFalse(manager.ContainsWorkspace(status.WorkspaceId));
        }
        finally
        {
            providerStop.Release.TrySetResult();
            if (shutdown is not null) await shutdown.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            if (retirement is not null) await retirement.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            DeleteDirectoryIfExists(root);
        }
    }

    private static WorkspaceManager CreateManager(
        IFileWatcherService watcher,
        IWorkspaceExecutionGate gate,
        TimeProvider? timeProvider = null) =>
        new(
            NullLogger<WorkspaceManager>.Instance,
            new PreviewStore(),
            watcher,
            new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 4 },
            cacheStore: null,
            sessionLoader: null,
            evictionGate: new Lazy<IWorkspaceExecutionGate>(() => gate),
            timeProvider: timeProvider);

    private sealed class SignalingFileWatcher : IFileWatcherService
    {
        public event Action<string>? WorkspaceRootMissing;

        public bool Disposed { get; private set; }
        internal Action? OnUnwatch { get; init; }

        public void RaiseRootMissing(string workspaceId) => WorkspaceRootMissing?.Invoke(workspaceId);

        public void Watch(string workspaceId, string workspacePath) { }

        public void Unwatch(string workspaceId) => OnUnwatch?.Invoke();

        public bool IsStale(string workspaceId) => false;

        public Task WaitForStaleAsync(string workspaceId, CancellationToken ct) => Task.CompletedTask;

        public string? GetStaleReason(string workspaceId) => null;

        public void MarkStale(string workspaceId, string reason) { }

        public bool ClearStaleIfNoExternalEditSince(string workspaceId, DateTime sinceUtc) => false;

        public void Dispose() => Disposed = true;
    }

    private sealed class RetirementLogger : ILogger<WorkspaceManager>
    {
        internal Exception? RetirementFailure { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning &&
                formatter(state, exception).StartsWith("Failed to retire missing workspace backing path", StringComparison.Ordinal))
            {
                RetirementFailure = exception;
            }
        }
    }

    private sealed class PausedProviderDisposal : IAsyncDisposable
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask DisposeAsync()
        {
            Entered.SetResult();
            return new(Release.Task);
        }
    }

    private sealed class RetirementClock : TimeProvider
    {
        private readonly FakeTimeProvider _clock = new();
        internal TaskCompletionSource TimerCreated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override DateTimeOffset GetUtcNow() => _clock.GetUtcNow();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = _clock.CreateTimer(callback, state, dueTime, period);
            TimerCreated.TrySetResult();
            return timer;
        }

        internal void Advance() => _clock.Advance(TimeSpan.FromSeconds(1));
    }

    private sealed class RetryingGate : IWorkspaceExecutionGate
    {
        private int _attempts;
        private int _removeGateCalls;

        public int Attempts => Volatile.Read(ref _attempts);

        public int RemoveGateCalls => Volatile.Read(ref _removeGateCalls);

        public TaskCompletionSource Succeeded { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource GateRemoved { get; } =
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

        public void RemoveGate(string workspaceId)
        {
            Interlocked.Increment(ref _removeGateCalls);
            GateRemoved.TrySetResult();
        }
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
