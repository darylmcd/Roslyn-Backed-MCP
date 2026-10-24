using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio;
using RoslynMcp.Host.Stdio.Security;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class WorkspaceForkSourceLifecycleTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Close_DuringForkRestore_RefusesBeforeAnyEffectsAndReleasesAfterCancellation(bool hostClose)
    {
        await using var fixture = await ForkFixture.CreateAsync();
        fixture.Manager.WorkspaceClosed += RootExpansionGrantRegistry.Revoke;
        RootExpansionGrantRegistry.Grant(fixture.SourceId);
        var operation = fixture.Start();
        try
        {
            await fixture.Runner.Entered.Task.WaitAsync(fixture.Timeout.Token);
            var solution = fixture.Manager.GetCurrentSolution(fixture.SourceId);
            var closed = 0;
            fixture.Manager.WorkspaceClosed += _ => closed++;
            var exception = hostClose
                ? await Assert.ThrowsAsync<PublicInvalidOperationException>(() => WorkspaceTools.CloseWorkspace(
                    fixture.Gate, fixture.Manager, fixture.Runner, fixture.SourceId, ct: fixture.Timeout.Token))
                : Assert.Throws<PublicInvalidOperationException>(() => fixture.Manager.Close(fixture.SourceId));
            StringAssert.Contains(exception.Message, "fork");
            Assert.AreEqual(0, closed);
            Assert.IsTrue(RootExpansionGrantRegistry.IsGranted(fixture.SourceId));
            Assert.IsFalse(fixture.Gate.RemovedIds.Contains(fixture.SourceId));
            Assert.IsTrue(fixture.Manager.ContainsWorkspace(fixture.SourceId));
            Assert.AreSame(solution, fixture.Manager.GetCurrentSolution(fixture.SourceId));
            Assert.IsNotNull(fixture.Store.Retrieve(fixture.Token));
            fixture.Caller.Cancel();
            fixture.Runner.Release.TrySetResult();
            await Assert.ThrowsAsync<OperationCanceledException>(() => operation);
            Assert.IsTrue(fixture.Manager.Close(fixture.SourceId));
            Assert.AreEqual(1, closed);
            Assert.IsFalse(RootExpansionGrantRegistry.IsGranted(fixture.SourceId));
            Assert.IsNull(fixture.Store.Retrieve(fixture.Token));
            Assert.IsFalse(fixture.Manager.Close(fixture.SourceId));
        }
        finally { await fixture.ReapAsync(operation); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Disposal_DuringForkRestore_CancelsAndDrainsBeforeDependencies(bool synchronousFirst)
    {
        await using var fixture = await ForkFixture.CreateAsync();
        var operation = fixture.Start();
        Task? stop = null;
        try
        {
            await fixture.Runner.Entered.Task.WaitAsync(fixture.Timeout.Token);
            // Exercise the existing IDisposable surface on old product too. The async
            // provider path must drain the same manager stop when it is added.
            if (synchronousFirst) ((IDisposable)fixture.Manager).Dispose();
            stop = fixture.Provider.DisposeAsync().AsTask();
            Assert.IsTrue(fixture.Runner.Cancelled.Task.IsCompleted,
                "Manager shutdown must deliver cancellation to its active real fork.");
            Assert.IsFalse(stop.IsCompleted, "Shutdown must await fork-owned native cleanup.");
            Assert.IsTrue(fixture.Manager.ContainsWorkspace(fixture.SourceId));
            Assert.IsNotNull(fixture.Store.Retrieve(fixture.Token));
            fixture.Runner.Release.TrySetResult();
            await Assert.ThrowsAsync<OperationCanceledException>(() => operation);
            await stop.WaitAsync(fixture.Timeout.Token);
            Assert.IsFalse(fixture.Manager.ContainsWorkspace(fixture.SourceId));
        }
        finally
        {
            await fixture.ReapAsync(operation);
            if (stop is not null) await stop.WaitAsync(fixture.Timeout.Token);
        }
    }

    [TestMethod]
    public async Task Lru_LateSourceRetention_ReconsidersOtherEligibleVictim()
    {
        await using var fixture = await ForkFixture.CreateAsync(capacity: 2);
        var other = await fixture.Manager.LoadAsync(fixture.CopyPath(), EvictPolicy.Strict, fixture.Timeout.Token);
        fixture.Gate.PauseNextWrite(fixture.SourceId);
        var load = fixture.Manager.LoadAsync(fixture.CopyPath(), EvictPolicy.Lru, fixture.Timeout.Token);
        Task<WorkspaceForkApplyResultDto>? operation = null;
        try
        {
            await fixture.Gate.Entered.Task.WaitAsync(fixture.Timeout.Token);
            operation = fixture.Start();
            await fixture.Runner.Entered.Task.WaitAsync(fixture.Timeout.Token);
            fixture.Gate.Release.TrySetResult();
            var loaded = await load.WaitAsync(fixture.Timeout.Token);
            Assert.IsTrue(fixture.Manager.ContainsWorkspace(fixture.SourceId), "Late retention must atomically defeat candidate removal.");
            Assert.IsFalse(fixture.Manager.ContainsWorkspace(other.WorkspaceId), "A later eligible victim must still be considered.");
            Assert.IsTrue(fixture.Manager.ContainsWorkspace(loaded.WorkspaceId));
            Assert.IsFalse(fixture.Gate.RemovedIds.Contains(fixture.SourceId), "Skipping a retained candidate cannot remove its live gate.");
            Assert.IsNotNull(fixture.Store.Retrieve(fixture.Token));
        }
        finally
        {
            fixture.Gate.Release.TrySetResult();
            if (operation is not null) await fixture.ReapAsync(operation);
            await load.WaitAsync(fixture.Timeout.Token);
        }
    }

    [TestMethod]
    public async Task MissingRoot_RetainedSource_RetriesAfterCallbackRelease()
    {
        var clock = new FakeTimeProvider();
        await using var fixture = await ForkFixture.CreateAsync(clock: clock);
        var operation = fixture.Start();
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Manager.WorkspaceClosed += id => { if (id == fixture.SourceId) closed.TrySetResult(); };
        try
        {
            await fixture.Runner.Entered.Task.WaitAsync(fixture.Timeout.Token);
            File.Delete(fixture.SourcePath);
            fixture.Gate.ObserveNextWrite();
            fixture.Watcher.RaiseMissing(fixture.SourceId);
            while (!fixture.Gate.Completed.Task.IsCompleted)
            {
                fixture.Timeout.Token.ThrowIfCancellationRequested();
                clock.Advance(TimeSpan.FromSeconds(2));
                await Task.Yield();
            }
            Assert.IsTrue(fixture.Manager.ContainsWorkspace(fixture.SourceId), "Missing-root retirement must postpone an active fork source.");
            fixture.Caller.Cancel();
            fixture.Runner.Release.TrySetResult();
            await Assert.ThrowsAsync<OperationCanceledException>(() => operation);
            while (!closed.Task.IsCompleted)
            {
                fixture.Timeout.Token.ThrowIfCancellationRequested();
                clock.Advance(TimeSpan.FromSeconds(2));
                await Task.Yield();
            }
            Assert.IsFalse(fixture.Manager.ContainsWorkspace(fixture.SourceId));
            Assert.AreEqual(1, fixture.Watcher.UnwatchCalls);
        }
        finally { await fixture.ReapAsync(operation); }
    }

    [TestMethod]
    public async Task ProviderShutdown_DrainsAlreadyAdmittedSlowRemoval_ThenClosesAdmission()
    {
        await using var fixture = await ForkFixture.CreateAsync();
        fixture.Watcher.PauseUnwatch = true;
        var closed = 0;
        fixture.Manager.WorkspaceClosed += _ => Interlocked.Increment(ref closed);
        var removal = Task.Run(() => fixture.Manager.Close(fixture.SourceId));
        Task? shutdown = null;
        try
        {
            await fixture.Watcher.UnwatchEntered.Task.WaitAsync(fixture.Timeout.Token);
            shutdown = fixture.Provider.DisposeAsync().AsTask();
            Assert.IsFalse(shutdown.IsCompleted, "Removal is not drained when only the session dictionary is empty.");
            Assert.AreEqual(0, fixture.Watcher.DisposeCount);
            fixture.Watcher.ReleaseUnwatch.Set();
            Assert.IsTrue(await removal.WaitAsync(fixture.Timeout.Token));
            await shutdown.WaitAsync(fixture.Timeout.Token);
            Assert.AreEqual(1, closed);
            Assert.AreEqual(1, fixture.Watcher.UnwatchCalls);
            Assert.AreEqual(1, fixture.Watcher.DisposeCount);
            Assert.Throws<ObjectDisposedException>(() => fixture.Manager.Close(fixture.SourceId));
            Assert.AreEqual(1, fixture.Watcher.UnwatchCalls, "Terminal shutdown must prevent late slow effects.");
        }
        finally
        {
            fixture.Watcher.ReleaseUnwatch.Set();
            await removal.WaitAsync(fixture.Timeout.Token);
            if (shutdown is not null) await shutdown.WaitAsync(fixture.Timeout.Token);
        }
    }

    [TestMethod]
    public async Task FailedRemoval_CompletesEveryCleanupAndReleasesSlotExactlyOnce()
    {
        await using var fixture = await ForkFixture.CreateAsync(capacity: 1);
        var closed = 0;
        fixture.Manager.WorkspaceClosed += _ => closed++;
        fixture.Watcher.ThrowOnUnwatch = true;
        var failure = Assert.Throws<Exception>(() => fixture.Manager.Close(fixture.SourceId));
        Assert.IsFalse(fixture.Manager.ContainsWorkspace(fixture.SourceId));
        Assert.IsNull(fixture.Store.Retrieve(fixture.Token));
        Assert.AreEqual(1, closed);
        Assert.IsInstanceOfType<AggregateException>(failure);
        fixture.Watcher.ThrowOnUnwatch = false;
        var next = await fixture.Manager.LoadAsync(fixture.CopyPath(), EvictPolicy.Strict, fixture.Timeout.Token);
        Assert.IsTrue(fixture.Manager.ContainsWorkspace(next.WorkspaceId), "Failed slow callbacks must not strand the slot.");
        Assert.IsFalse(fixture.Manager.Close(fixture.SourceId));
        Assert.AreEqual(1, closed);
    }

    [TestMethod]
    public async Task ManagerShutdown_ReentrantMixedCallers_ShareFailureAfterCallbackCleanup()
    {
        await using var fixture = await ForkFixture.CreateAsync();
        var operation = fixture.Start();
        var sentinel = new IOException("Cancellation callback failure.");
        Task? reentered = null;
        fixture.Runner.OnCancellation = () =>
        {
            ((IDisposable)fixture.Manager).Dispose();
            reentered = ((IAsyncDisposable)fixture.Manager).DisposeAsync().AsTask();
            throw sentinel;
        };
        try
        {
            await fixture.Runner.Entered.Task.WaitAsync(fixture.Timeout.Token);
            ((IDisposable)fixture.Manager).Dispose();
            Assert.IsTrue(fixture.Runner.Cancelled.Task.IsCompleted, "Stopping must cancel active fork callbacks.");
            var shutdown = ((IAsyncDisposable)fixture.Manager).DisposeAsync().AsTask();
            Assert.AreSame(shutdown, reentered, "Reentrant callers must observe the already-published shutdown.");
            Assert.IsFalse(shutdown.IsCompleted);
            Assert.IsTrue(fixture.Manager.ContainsWorkspace(fixture.SourceId));
            fixture.Runner.Release.TrySetResult();
            await Assert.ThrowsAsync<OperationCanceledException>(() => operation);
            var failure = await Assert.ThrowsAsync<AggregateException>(() => shutdown);
            Assert.IsTrue(failure.Flatten().InnerExceptions.Contains(sentinel));
            Assert.IsFalse(fixture.Manager.ContainsWorkspace(fixture.SourceId));
            var repeated = await Assert.ThrowsAsync<AggregateException>(() => ((IAsyncDisposable)fixture.Manager).DisposeAsync().AsTask());
            Assert.AreSame(failure, repeated);
            // Fixture provider will observe the same terminal failure too.
            fixture.ExpectedShutdownFailure = failure;
        }
        finally { await fixture.ReapAsync(operation); }
    }

    [TestMethod]
    public async Task ForkServiceShutdown_ReentrantCaller_DrainsWithoutDisposingExternalManager()
    {
        await using var fixture = await ForkFixture.CreateAsync(externalManager: true);
        var service = fixture.Provider.GetRequiredService<IWorkspaceForkApplyService>();
        var operation = fixture.Start();
        Task? reentered = null;
        fixture.Runner.OnCancellation = () => reentered = ((IAsyncDisposable)service).DisposeAsync().AsTask();
        try
        {
            await fixture.Runner.Entered.Task.WaitAsync(fixture.Timeout.Token);
            var providerStop = fixture.Provider.DisposeAsync().AsTask();
            Assert.IsTrue(fixture.Runner.Cancelled.Task.IsCompleted, "Provider must cancel and drain its fork before releasing dependencies.");
            var stop = ((IAsyncDisposable)service).DisposeAsync().AsTask();
            Assert.AreSame(stop, reentered);
            Assert.IsFalse(stop.IsCompleted);
            fixture.Runner.Release.TrySetResult();
            await Assert.ThrowsAsync<OperationCanceledException>(() => operation);
            await stop.WaitAsync(fixture.Timeout.Token);
            await providerStop.WaitAsync(fixture.Timeout.Token);
            Assert.IsTrue(fixture.Manager.ContainsWorkspace(fixture.SourceId));
            Assert.IsNotNull(fixture.Store.Retrieve(fixture.Token));
            var next = await fixture.Manager.LoadAsync(fixture.CopyPath(), EvictPolicy.Strict, fixture.Timeout.Token);
            Assert.IsTrue(fixture.Manager.ContainsWorkspace(next.WorkspaceId), "Provider cannot take ownership of the external manager.");
            Assert.IsTrue(fixture.Manager.Close(next.WorkspaceId));
        }
        finally { await fixture.ReapAsync(operation); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ActualHostRunAsync_ActiveFork_DrainsBeforeActualDependencies(bool cancelRun)
    {
        await using var fixture = await ForkFixture.CreateAsync(withHost: true);
        using var runCancellation = new CancellationTokenSource();
        var host = fixture.Host!;
        var run = host.RunAsync(runCancellation.Token);
        var operation = fixture.Start();
        try
        {
            await fixture.Runner.Entered.Task.WaitAsync(fixture.Timeout.Token);
            if (cancelRun) runCancellation.Cancel();
            else host.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();
            var completed = await Task.WhenAny(fixture.Runner.Cancelled.Task, run).WaitAsync(fixture.Timeout.Token);
            Assert.AreSame(fixture.Runner.Cancelled.Task, completed, "Host.RunAsync must cancel the actual active fork before its provider finishes.");
            Assert.IsFalse(run.IsCompleted);
            Assert.AreEqual(0, fixture.Watcher.DisposeCount);
            Assert.AreEqual(0, fixture.Runner.DisposeCount);
            // Real gate remains usable throughout the fork cancellation cleanup window.
            Assert.IsTrue(await fixture.Gate.RunWriteAsync(fixture.SourceId, _ => Task.FromResult(true), fixture.Timeout.Token, false));
            Assert.IsTrue(fixture.Manager.ContainsWorkspace(fixture.SourceId));
            fixture.Runner.Release.TrySetResult();
            await Assert.ThrowsAsync<OperationCanceledException>(() => operation);
            await run.WaitAsync(fixture.Timeout.Token);
            Assert.AreEqual(1, fixture.Watcher.DisposeCount);
            Assert.AreEqual(1, fixture.Runner.DisposeCount);
            Assert.IsFalse(fixture.Manager.ContainsWorkspace(fixture.SourceId));
        }
        finally
        {
            runCancellation.Cancel();
            await fixture.ReapAsync(operation);
            await run.WaitAsync(fixture.Timeout.Token);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LastOperationRelease_DuringInitialCancel_DoesNotCompleteShutdownEarly(bool providerStop)
    {
        await using var fixture = await ForkFixture.CreateAsync();
        fixture.Runner.UnregisterCancellation = true;
        // Inline the runner completion on the initiating cancellation callback thread.
        // This deterministically releases both operation counts before Cancel can return.
        fixture.Runner.Release = new TaskCompletionSource();
        using var callbackMayReturn = new ManualResetEventSlim(false);
        var callbackEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Runner.OnCancellation = () =>
        {
            callbackEntered.TrySetResult();
            fixture.Runner.Release.TrySetResult();
            Assert.IsTrue(callbackMayReturn.Wait(TimeSpan.FromMinutes(2)), "The test must release its owned cancellation callback.");
        };
        var operation = fixture.Start();
        Task? stop = null;
        try
        {
            await fixture.Runner.Entered.Task.WaitAsync(fixture.Timeout.Token);
            var service = fixture.Provider.GetRequiredService<IWorkspaceForkApplyService>();
            stop = providerStop
                ? Task.Run(async () => await fixture.Provider.DisposeAsync())
                : Task.Run(() => ((IDisposable)fixture.Manager).Dispose());
            var winner = await Task.WhenAny(callbackEntered.Task, stop).WaitAsync(fixture.Timeout.Token);
            Assert.AreSame(callbackEntered.Task, winner, "Shutdown must issue cancellation before it can complete.");
            await Assert.ThrowsAsync<OperationCanceledException>(() => operation.WaitAsync(fixture.Timeout.Token));
            // Source retention and the service's operation count have both drained while
            // the initiating Cancel is still executing the deliberately held callback.
            var shared = providerStop ? ((IAsyncDisposable)service).DisposeAsync().AsTask()
                : ((IAsyncDisposable)fixture.Manager).DisposeAsync().AsTask();
            Assert.IsFalse(shared.IsCompleted, "A drained callback cannot tear down CTS/resources until the initiating Cancel returns.");
            Assert.AreEqual(0, fixture.Watcher.DisposeCount);
            Assert.AreEqual(0, fixture.Runner.DisposeCount);
            Assert.IsTrue(fixture.Manager.ContainsWorkspace(fixture.SourceId));
            callbackMayReturn.Set();
            await stop.WaitAsync(fixture.Timeout.Token);
            await shared.WaitAsync(fixture.Timeout.Token);
        }
        finally
        {
            callbackMayReturn.Set();
            await fixture.ReapAsync(operation);
            if (stop is not null) await stop.WaitAsync(fixture.Timeout.Token);
        }
    }

    private sealed class ForkFixture : IAsyncDisposable
    {
        internal ServiceProvider Provider { get; }
        internal IWorkspaceManager Manager { get; }
        internal IPreviewStore Store { get; }
        internal PausedRunner Runner { get; }
        internal CancellationTokenSource Caller { get; } = new();
        internal CancellationTokenSource Timeout { get; } = new(TimeSpan.FromMinutes(2));
        internal string SourceId { get; }
        internal string Token { get; }
        private readonly string _root;
        private readonly List<string> _extraRoots = new();
        private readonly bool _externalManager;
        internal IHost? Host { get; }
        internal ObservedWatcher Watcher { get; }
        internal ObservedGate Gate { get; }
        internal string SourcePath { get; }
        internal Exception? ExpectedShutdownFailure { get; set; }

        private ForkFixture(ServiceProvider provider, PausedRunner runner, string sourceId, string token, string sourcePath, ObservedWatcher watcher, ObservedGate gate, bool externalManager, IHost? host)
        {
            Provider = provider;
            Manager = provider.GetRequiredService<IWorkspaceManager>();
            Store = provider.GetRequiredService<IPreviewStore>();
            Runner = runner;
            SourceId = sourceId;
            Token = token;
            _root = Path.GetDirectoryName(sourcePath)!;
            SourcePath = sourcePath;
            Watcher = watcher;
            Gate = gate;
            _externalManager = externalManager;
            Host = host;
        }

        internal static async Task<ForkFixture> CreateAsync(int capacity = 8, FakeTimeProvider? clock = null, bool externalManager = false, bool withHost = false)
        {
            var repository = TestFixtureFileSystem.FindRepositoryRoot();
            var sample = TestFixtureFileSystem.FindFixturePath(repository, "SampleSolution", "SampleSolution.slnx", "SampleSolution.sln");
            var path = TestFixtureFileSystem.CreateSampleSolutionCopy(repository, sample);
            var runner = new PausedRunner();
            var builder = withHost ? Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder() : null;
            var services = builder?.Services ?? new ServiceCollection();
            services.AddLogging();
            services.AddRoslynServices();
            services.AddRoslynMcpHostServices(new WorkspaceManagerOptions { MaxConcurrentWorkspaces = capacity },
                new ValidationServiceOptions(), new PreviewStoreOptions(), new ExecutionGateOptions(),
                new SecurityOptions(), new ScriptingServiceOptions());
            var watcher = new ObservedWatcher();
            ServiceProvider? provider = null;
            ObservedGate? gate = null;
            if (externalManager)
            {
                var store = new PreviewStore();
                var manager = new WorkspaceManager(NullLogger<WorkspaceManager>.Instance, store, watcher,
                    new WorkspaceManagerOptions { MaxConcurrentWorkspaces = capacity },
                    cacheStore: null, sessionLoader: null,
                    evictionGate: new Lazy<IWorkspaceExecutionGate>(() => gate!), timeProvider: clock);
                gate = new ObservedGate(new WorkspaceExecutionGate(new ExecutionGateOptions(), manager));
                services.AddSingleton<IPreviewStore>(store);
                services.AddSingleton<IWorkspaceManager>(manager);
                services.AddSingleton<IFileWatcherService>(watcher);
                services.AddSingleton<IWorkspaceExecutionGate>(gate);
            }
            else
            {
                services.AddSingleton<IFileWatcherService>(_ => watcher);
                services.AddSingleton<IWorkspaceExecutionGate>(sp => gate = new ObservedGate(new WorkspaceExecutionGate(
                    new ExecutionGateOptions(), sp.GetRequiredService<IWorkspaceManager>())));
                if (clock is not null)
                    services.AddSingleton<IWorkspaceManager>(sp => new WorkspaceManager(
                        sp.GetRequiredService<ILogger<WorkspaceManager>>(), sp.GetRequiredService<IPreviewStore>(), watcher,
                        new WorkspaceManagerOptions { MaxConcurrentWorkspaces = capacity }, cacheStore: null, sessionLoader: null,
                        evictionGate: sp.GetRequiredService<Lazy<IWorkspaceExecutionGate>>(), ownsFileWatcher: false, timeProvider: clock));
            }
            services.AddSingleton<IDotnetCommandRunner>(_ => runner);
            var host = builder?.Build();
            provider = host is null ? services.BuildServiceProvider() : (ServiceProvider)host.Services;
            try
            {
                var manager = provider.GetRequiredService<IWorkspaceManager>();
                var source = await manager.LoadAsync(path, EvictPolicy.Strict, CancellationToken.None);
                var solution = manager.GetCurrentSolution(source.WorkspaceId);
                var store = provider.GetRequiredService<IPreviewStore>();
                var token = store.Store(source.WorkspaceId, solution, source.WorkspaceVersion, "lifecycle fixture");
                // Resolve the actual fork service after all its real dependencies.
                _ = provider.GetRequiredService<IWorkspaceForkApplyService>();
                return new ForkFixture(provider, runner, source.WorkspaceId, token, path, watcher, gate!, externalManager, host);
            }
            catch
            {
                await provider.DisposeAsync();
                TestFixtureFileSystem.DeleteDirectoryIfExists(Path.GetDirectoryName(path)!);
                throw;
            }
        }

        internal string CopyPath()
        {
            var repository = TestFixtureFileSystem.FindRepositoryRoot();
            var sample = TestFixtureFileSystem.FindFixturePath(repository, "SampleSolution", "SampleSolution.slnx", "SampleSolution.sln");
            var path = TestFixtureFileSystem.CreateSampleSolutionCopy(repository, sample);
            _extraRoots.Add(Path.GetDirectoryName(path)!);
            return path;
        }

        internal Task<WorkspaceForkApplyResultDto> Start() => Provider.GetRequiredService<IWorkspaceForkApplyService>()
            .ApplyAsync(SourceId, Token, "keep", false, null, "lifecycle", Caller.Token);

        internal async Task ReapAsync(Task operation)
        {
            Runner.OnCancellation = null;
            Caller.Cancel();
            Runner.Release.TrySetResult();
            try { await operation.WaitAsync(Timeout.Token); }
            catch (OperationCanceledException) when (operation.IsCanceled) { }
        }

        public async ValueTask DisposeAsync()
        {
            Runner.Release.TrySetResult();
            Caller.Cancel();
            try { await Provider.DisposeAsync(); }
            catch (Exception exception) when (ReferenceEquals(exception, ExpectedShutdownFailure))
            {
                // ServiceProvider stops on a disposable's failure. The manager has already
                // completed its own cleanup; release the remaining fixture-owned watcher.
                if (Watcher.DisposeCount == 0) Watcher.Dispose();
            }
            if (_externalManager)
            {
                if (Manager is IAsyncDisposable asynchronous) await asynchronous.DisposeAsync();
                else ((IDisposable)Manager).Dispose();
                Gate.Dispose();
            }
            Host?.Dispose();
            Caller.Dispose();
            Timeout.Dispose();
            TestFixtureFileSystem.DeleteDirectoryIfExists(_root);
            foreach (var root in _extraRoots) TestFixtureFileSystem.DeleteDirectoryIfExists(root);
        }
    }

    private sealed class ObservedWatcher : IFileWatcherService
    {
        private readonly FileWatcherService _inner = new(NullLogger<FileWatcherService>.Instance);
        public event Action<string>? WorkspaceRootMissing;
        internal void RaiseMissing(string id) => WorkspaceRootMissing?.Invoke(id);
        internal bool PauseUnwatch { get; set; }
        internal bool ThrowOnUnwatch { get; set; }
        internal TaskCompletionSource UnwatchEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ManualResetEventSlim ReleaseUnwatch { get; } = new(false);
        internal int DisposeCount;
        internal int UnwatchCalls;
        public void Watch(string id, string path) => _inner.Watch(id, path);
        public void Unwatch(string id)
        {
            Interlocked.Increment(ref UnwatchCalls);
            UnwatchEntered.TrySetResult();
            if (PauseUnwatch) ReleaseUnwatch.Wait(TimeSpan.FromMinutes(2));
            Assert.AreEqual(0, DisposeCount, "An admitted removal must retain its watcher dependency.");
            _inner.Unwatch(id);
            if (ThrowOnUnwatch) throw new IOException("Unwatch failure.");
        }
        public bool IsStale(string id) => _inner.IsStale(id);
        public Task WaitForStaleAsync(string id, CancellationToken ct) => _inner.WaitForStaleAsync(id, ct);
        public string? GetStaleReason(string id) => _inner.GetStaleReason(id);
        public void MarkStale(string id, string reason) => _inner.MarkStale(id, reason);
        public void ClearStale(string id) => _inner.ClearStale(id);
        public void Dispose() { Interlocked.Increment(ref DisposeCount); _inner.Dispose(); }
    }

    private sealed class ObservedGate(WorkspaceExecutionGate inner) : IWorkspaceExecutionGate, IDisposable
    {
        private string? _pauseId;
        private int _observe;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Completed { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal HashSet<string> RemovedIds { get; } = new(StringComparer.Ordinal);
        internal void PauseNextWrite(string id) => _pauseId = id;
        internal void ObserveNextWrite() { Completed = new(TaskCreationOptions.RunContinuationsAsynchronously); _observe = 1; }
        public Task<T> RunReadAsync<T>(string id, Func<CancellationToken, Task<T>> action, CancellationToken ct) => inner.RunReadAsync(id, action, ct);
        public async Task<T> RunWriteAsync<T>(string id, Func<CancellationToken, Task<T>> action, CancellationToken ct, bool applyStalenessPolicy = true)
        {
            if (Interlocked.CompareExchange(ref _pauseId, null, id) == id)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(ct);
            }
            var result = await inner.RunWriteAsync(id, action, ct, applyStalenessPolicy);
            if (Interlocked.Exchange(ref _observe, 0) != 0) Completed.TrySetResult();
            return result;
        }
        public Task<T> RunLoadGateAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct) => inner.RunLoadGateAsync(action, ct);
        public void RemoveGate(string id) { lock (RemovedIds) RemovedIds.Add(id); inner.RemoveGate(id); }
        public void Dispose() => inner.Dispose();
    }

    private sealed class PausedRunner : IDotnetCommandRunner, IDisposable
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Action? OnCancellation { get; set; }
        internal bool UnregisterCancellation { get; set; }
        internal int DisposeCount;
        public void Dispose() => Interlocked.Increment(ref DisposeCount);

        public async Task<CommandExecutionDto> RunAsync(string workingDirectory, string targetPath,
            IReadOnlyList<string> arguments, CancellationToken ct)
        {
            var registration = ct.Register(() => { Cancelled.TrySetResult(); OnCancellation?.Invoke(); });
            try
            {
                Entered.TrySetResult();
                await Release.Task;
                Assert.AreEqual(0, DisposeCount, "Native cleanup cannot run after runner disposal.");
                ct.ThrowIfCancellationRequested();
                throw new AssertFailedException("The fixture must cancel its paused restore.");
            }
            finally
            {
                if (UnregisterCancellation) registration.Unregister();
                else registration.Dispose();
            }
        }
    }
}
