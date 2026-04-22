using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public class WorkspaceExecutionGateTests
{
    private const string WorkspaceA = "ws-a";
    private const string WorkspaceB = "ws-b";

    private static WorkspaceExecutionGate CreateGate(FakeGateWorkspaceManager? manager = null)
    {
        return new WorkspaceExecutionGate(
            new ExecutionGateOptions(),
            manager ?? new FakeGateWorkspaceManager());
    }

    [TestMethod]
    public async Task RunReadAsync_TwoConcurrentReads_RunInParallel()
    {
        var gate = CreateGate();
        var tracker = new ConcurrencyTracker();

        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = gate.RunReadAsync(WorkspaceA, async ct =>
        {
            tracker.Enter();
            firstStarted.SetResult();
            await releaseFirst.Task.WaitAsync(ct);
            tracker.Leave();
            return 1;
        }, CancellationToken.None);

        await firstStarted.Task;

        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = gate.RunReadAsync(WorkspaceA, async ct =>
        {
            tracker.Enter();
            secondStarted.SetResult();
            tracker.Leave();
            return 2;
        }, CancellationToken.None);

        await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(2, tracker.MaxConcurrent, "Two concurrent reads should overlap under the per-workspace RW lock.");

        releaseFirst.SetResult();
        await Task.WhenAll(first, second);
    }

    [TestMethod]
    public async Task RunWriteAsync_BlocksReader()
    {
        var gate = CreateGate();

        var writerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWriter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readerStarted = false;

        var writer = gate.RunWriteAsync(WorkspaceA, async ct =>
        {
            writerStarted.SetResult();
            await releaseWriter.Task.WaitAsync(ct);
            return 1;
        }, CancellationToken.None);

        await writerStarted.Task;

        var reader = gate.RunReadAsync(WorkspaceA, ct =>
        {
            readerStarted = true;
            return Task.FromResult(2);
        }, CancellationToken.None);

        await Task.Delay(50);
        Assert.IsFalse(readerStarted, "Reader should not start while writer holds the lock.");

        releaseWriter.SetResult();
        await Task.WhenAll(writer, reader);
        Assert.IsTrue(readerStarted);
    }

    [TestMethod]
    public async Task RunReadAsync_BlocksWriter()
    {
        var gate = CreateGate();

        var readerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseReader = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writerStarted = false;

        var reader = gate.RunReadAsync(WorkspaceA, async ct =>
        {
            readerStarted.SetResult();
            await releaseReader.Task.WaitAsync(ct);
            return 1;
        }, CancellationToken.None);

        await readerStarted.Task;

        var writer = gate.RunWriteAsync(WorkspaceA, ct =>
        {
            writerStarted = true;
            return Task.FromResult(2);
        }, CancellationToken.None);

        await Task.Delay(50);
        Assert.IsFalse(writerStarted, "Writer should not start while reader holds the lock.");

        releaseReader.SetResult();
        await Task.WhenAll(reader, writer);
        Assert.IsTrue(writerStarted);
    }

    [TestMethod]
    public async Task RunWriteAsync_TwoWritesSerialize()
    {
        var gate = CreateGate();
        var tracker = new ConcurrencyTracker();

        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = gate.RunWriteAsync(WorkspaceA, async ct =>
        {
            tracker.Enter();
            firstStarted.SetResult();
            await releaseFirst.Task.WaitAsync(ct);
            tracker.Leave();
            return 1;
        }, CancellationToken.None);

        await firstStarted.Task;

        var second = gate.RunWriteAsync(WorkspaceA, ct =>
        {
            tracker.Enter();
            tracker.Leave();
            return Task.FromResult(2);
        }, CancellationToken.None);

        await Task.Delay(50);
        Assert.IsFalse(second.IsCompleted);
        Assert.AreEqual(1, tracker.MaxConcurrent);

        releaseFirst.SetResult();
        await Task.WhenAll(first, second);
        Assert.AreEqual(1, tracker.MaxConcurrent);
    }

    [TestMethod]
    public async Task DifferentWorkspaces_RunIndependently()
    {
        var gate = CreateGate();
        var aTracker = new ConcurrencyTracker();
        var bTracker = new ConcurrencyTracker();

        var aStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseA = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var a = gate.RunWriteAsync(WorkspaceA, async ct =>
        {
            aTracker.Enter();
            aStarted.SetResult();
            await releaseA.Task.WaitAsync(ct);
            aTracker.Leave();
            return 1;
        }, CancellationToken.None);

        await aStarted.Task;

        var b = gate.RunWriteAsync(WorkspaceB, ct =>
        {
            bTracker.Enter();
            bTracker.Leave();
            return Task.FromResult(2);
        }, CancellationToken.None);

        await b.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(1, bTracker.MaxConcurrent);

        releaseA.SetResult();
        await a;
    }

    [TestMethod]
    public async Task QueuedWriter_RunsBeforeLaterReaders()
    {
        // FIFO fairness sanity: read → write → reads queued; first read releases;
        // writer must complete before any of the queued reads.
        var gate = CreateGate();
        var order = new List<string>();
        var orderLock = new object();

        void Record(string label)
        {
            lock (orderLock)
            {
                order.Add(label);
            }
        }

        var firstReadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var firstRead = gate.RunReadAsync(WorkspaceA, async ct =>
        {
            Record("R1");
            firstReadStarted.SetResult();
            await releaseFirstRead.Task.WaitAsync(ct);
            return 0;
        }, CancellationToken.None);

        await firstReadStarted.Task;

        var writer = gate.RunWriteAsync(WorkspaceA, ct =>
        {
            Record("W");
            return Task.FromResult(0);
        }, CancellationToken.None);

        // Give the writer time to queue.
        await Task.Delay(50);

        var laterRead = gate.RunReadAsync(WorkspaceA, ct =>
        {
            Record("R2");
            return Task.FromResult(0);
        }, CancellationToken.None);

        // Give the later read time to queue.
        await Task.Delay(50);

        releaseFirstRead.SetResult();
        await Task.WhenAll(firstRead, writer, laterRead);

        CollectionAssert.AreEqual(new[] { "R1", "W", "R2" }, order,
            "Queued writer should run before subsequent readers (FIFO fairness, no writer starvation).");
    }

    [TestMethod]
    public async Task RunReadAsync_ThrowsWhenWorkspaceMissing()
    {
        var gate = CreateGate(new FakeGateWorkspaceManager(containsAny: false));
        await Assert.ThrowsExactlyAsync<KeyNotFoundException>(() =>
            gate.RunReadAsync(WorkspaceA, _ => Task.FromResult(0), CancellationToken.None));
    }

    [TestMethod]
    public async Task RunWriteAsync_ThrowsWhenWorkspaceMissing()
    {
        var gate = CreateGate(new FakeGateWorkspaceManager(containsAny: false));
        await Assert.ThrowsExactlyAsync<KeyNotFoundException>(() =>
            gate.RunWriteAsync(WorkspaceA, _ => Task.FromResult(0), CancellationToken.None));
    }

    [TestMethod]
    public async Task RemoveGate_AfterWrite_BlocksFutureCalls()
    {
        var manager = new FakeGateWorkspaceManager();
        var gate = CreateGate(manager);

        await gate.RunWriteAsync(WorkspaceA, _ => Task.FromResult(0), CancellationToken.None);

        manager.RemoveWorkspace(WorkspaceA);
        gate.RemoveGate(WorkspaceA);

        await Assert.ThrowsExactlyAsync<KeyNotFoundException>(() =>
            gate.RunReadAsync(WorkspaceA, _ => Task.FromResult(0), CancellationToken.None));
    }

    [TestMethod]
    public async Task WorkspaceClose_DoubleAcquire_BlocksUntilReaderReleases()
    {
        // Reproduces the workspace_close path: outer load gate + inner RunWriteAsync. The close
        // must wait for an in-flight reader before removing the workspace.
        var manager = new FakeGateWorkspaceManager();
        var gate = CreateGate(manager);

        var readerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseReader = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closeCompleted = false;

        var reader = gate.RunReadAsync(WorkspaceA, async ct =>
        {
            readerStarted.SetResult();
            await releaseReader.Task.WaitAsync(ct);
            return 0;
        }, CancellationToken.None);

        await readerStarted.Task;

        var close = gate.RunLoadGateAsync(_ =>
            gate.RunWriteAsync(WorkspaceA, _ =>
            {
                manager.RemoveWorkspace(WorkspaceA);
                gate.RemoveGate(WorkspaceA);
                closeCompleted = true;
                return Task.FromResult(0);
            }, CancellationToken.None), CancellationToken.None);

        await Task.Delay(50);
        Assert.IsFalse(closeCompleted, "Close must wait for in-flight reader to release.");

        releaseReader.SetResult();
        await Task.WhenAll(reader, close);
        Assert.IsTrue(closeCompleted);
    }

    [TestMethod]
    public async Task RunReadAsync_RespectsRequestTimeout()
    {
        var gate = new WorkspaceExecutionGate(
            new ExecutionGateOptions
            {
                RequestTimeout = TimeSpan.FromMilliseconds(150),
            },
            new FakeGateWorkspaceManager());

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() =>
            gate.RunReadAsync(WorkspaceA, async ct =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
                return 0;
            }, CancellationToken.None));
    }

    [TestMethod]
    public async Task RunReadAsync_RespectsRateLimit()
    {
        var gate = new WorkspaceExecutionGate(
            new ExecutionGateOptions
            {
                RateLimitMaxRequests = 3,
                RateLimitWindow = TimeSpan.FromSeconds(60),
            },
            new FakeGateWorkspaceManager());

        for (var i = 0; i < 3; i++)
        {
            await gate.RunReadAsync(WorkspaceA, _ => Task.FromResult(0), CancellationToken.None);
        }

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            gate.RunReadAsync(WorkspaceA, _ => Task.FromResult(0), CancellationToken.None));
    }

    // ---------- Item 1: staleness policy ----------

    [TestMethod]
    public async Task StalenessPolicy_AutoReload_TriggersReloadBeforeToolRuns()
    {
        var manager = new FakeGateWorkspaceManager();
        manager.MarkStale(WorkspaceA);

        var gate = new WorkspaceExecutionGate(
            new ExecutionGateOptions { OnStale = StalenessPolicy.AutoReload },
            manager);

        using var scope = AmbientGateMetrics.BeginRequest();

        var staleSeen = false;
        await gate.RunReadAsync(WorkspaceA, _ =>
        {
            staleSeen = manager.IsStale(WorkspaceA);
            return Task.FromResult(0);
        }, CancellationToken.None);

        Assert.AreEqual(1, manager.ReloadCount, "Auto-reload should fire exactly once for a stale workspace.");
        Assert.IsFalse(staleSeen, "Tool should observe a fresh workspace after auto-reload.");
        var metrics = AmbientGateMetrics.Current;
        Assert.IsNotNull(metrics);
        Assert.AreEqual("auto-reloaded", metrics!.StaleAction);
        Assert.IsNotNull(metrics.StaleReloadMs);
    }

    [TestMethod]
    public async Task StalenessPolicy_Warn_DoesNotReloadButStampsMetrics()
    {
        var manager = new FakeGateWorkspaceManager();
        manager.MarkStale(WorkspaceA);

        var gate = new WorkspaceExecutionGate(
            new ExecutionGateOptions { OnStale = StalenessPolicy.Warn },
            manager);

        using var scope = AmbientGateMetrics.BeginRequest();

        await gate.RunReadAsync(WorkspaceA, _ => Task.FromResult(0), CancellationToken.None);

        Assert.AreEqual(0, manager.ReloadCount, "Warn policy must not auto-reload.");
        var metrics = AmbientGateMetrics.Current;
        Assert.IsNotNull(metrics);
        Assert.AreEqual("warn", metrics!.StaleAction);
        Assert.IsNull(metrics.StaleReloadMs);
    }

    [TestMethod]
    public async Task StalenessPolicy_Off_IsNoOp()
    {
        var manager = new FakeGateWorkspaceManager();
        manager.MarkStale(WorkspaceA);

        var gate = new WorkspaceExecutionGate(
            new ExecutionGateOptions { OnStale = StalenessPolicy.Off },
            manager);

        using var scope = AmbientGateMetrics.BeginRequest();

        await gate.RunReadAsync(WorkspaceA, _ => Task.FromResult(0), CancellationToken.None);

        Assert.AreEqual(0, manager.ReloadCount, "Off policy must not auto-reload.");
        var metrics = AmbientGateMetrics.Current;
        Assert.IsNotNull(metrics);
        Assert.IsNull(metrics!.StaleAction);
    }

    [TestMethod]
    public async Task StalenessPolicy_FreshWorkspace_SkipsReloadAndMetricStamping()
    {
        var manager = new FakeGateWorkspaceManager();

        var gate = new WorkspaceExecutionGate(
            new ExecutionGateOptions { OnStale = StalenessPolicy.AutoReload },
            manager);

        using var scope = AmbientGateMetrics.BeginRequest();

        await gate.RunReadAsync(WorkspaceA, _ => Task.FromResult(0), CancellationToken.None);

        Assert.AreEqual(0, manager.ReloadCount, "Fresh workspace should never trigger reload.");
        var metrics = AmbientGateMetrics.Current;
        Assert.IsNotNull(metrics);
        Assert.IsNull(metrics!.StaleAction);
    }

    // dr-9-9-response-claims: when ReloadAsync throws KeyNotFoundException (workspace was
    // closed between the stale check and the reload attempt), the gate must NOT stamp
    // StaleAction = "auto-reloaded" — nothing actually reloaded, so the response envelope
    // would otherwise advertise a reload that never ran.
    [TestMethod]
    public async Task StalenessPolicy_AutoReload_ReloadThrowsKeyNotFound_LeavesStaleActionUnset()
    {
        var manager = new FakeGateWorkspaceManager { ReloadThrowsKeyNotFound = true };
        manager.MarkStale(WorkspaceA);

        var gate = new WorkspaceExecutionGate(
            new ExecutionGateOptions { OnStale = StalenessPolicy.AutoReload },
            manager);

        using var scope = AmbientGateMetrics.BeginRequest();

        await gate.RunReadAsync(WorkspaceA, _ => Task.FromResult(0), CancellationToken.None);

        Assert.AreEqual(1, manager.ReloadCount, "Reload should have been attempted once.");
        var metrics = AmbientGateMetrics.Current;
        Assert.IsNotNull(metrics);
        Assert.IsNull(metrics!.StaleAction,
            "StaleAction must remain null when reload swallows KeyNotFoundException — the envelope would otherwise falsely claim an auto-reload.");
        Assert.IsNull(metrics.StaleReloadMs,
            "StaleReloadMs must also remain null when no reload completed.");
    }

    /// <summary>
    /// workspace-close-missing-solution-on-disk: closing must not run the stale auto-reload
    /// preamble (reload requires the solution file on disk).
    /// </summary>
    [TestMethod]
    public async Task RunWriteAsync_SkipStaleness_WhenStale_DoesNotInvokeReload()
    {
        var manager = new FakeGateWorkspaceManager();
        manager.MarkStale(WorkspaceA);
        manager.ReloadThrowsFileNotFound = true;

        var gate = new WorkspaceExecutionGate(
            new ExecutionGateOptions { OnStale = StalenessPolicy.AutoReload },
            manager);

        var ran = false;
        await gate.RunWriteAsync(
            WorkspaceA,
            _ =>
            {
                ran = true;
                return Task.FromResult(0);
            },
            CancellationToken.None,
            applyStalenessPolicy: false).ConfigureAwait(false);

        Assert.IsTrue(ran);
        Assert.AreEqual(0, manager.ReloadCount, "Close-style writes must not auto-reload stale workspaces.");
    }

    [TestMethod]
    public async Task RunWriteAsync_Default_WhenStaleAndReloadThrowsFileNotFound_Propagates()
    {
        var manager = new FakeGateWorkspaceManager();
        manager.MarkStale(WorkspaceA);
        manager.ReloadThrowsFileNotFound = true;

        var gate = new WorkspaceExecutionGate(
            new ExecutionGateOptions { OnStale = StalenessPolicy.AutoReload },
            manager);

        await Assert.ThrowsExactlyAsync<FileNotFoundException>(() =>
            gate.RunWriteAsync(WorkspaceA, _ => Task.FromResult(0), CancellationToken.None)).ConfigureAwait(false);

        Assert.AreEqual(1, manager.ReloadCount);
    }

    private sealed class ConcurrencyTracker
    {
        private int _current;
        public int MaxConcurrent { get; private set; }

        public void Enter()
        {
            var n = Interlocked.Increment(ref _current);
            // Volatile.Read+Write to keep MaxConcurrent observable from any thread without a lock.
            if (n > MaxConcurrent) MaxConcurrent = n;
        }

        public void Leave() => Interlocked.Decrement(ref _current);
    }

    private sealed class FakeGateWorkspaceManager : IWorkspaceManager
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _removed = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _stale = new();
        private readonly bool _containsAny;

        public int ReloadCount;

        /// <summary>
        /// When true, <see cref="ReloadAsync"/> increments <see cref="ReloadCount"/>
        /// and then throws <see cref="KeyNotFoundException"/> — simulates the
        /// race where the workspace was closed between the stale check and the
        /// reload attempt. Used by the <c>dr-9-9-response-claims</c> regression.
        /// </summary>
        public bool ReloadThrowsKeyNotFound { get; set; }

        /// <summary>
        /// When true, <see cref="ReloadAsync"/> increments <see cref="ReloadCount"/> and throws
        /// <see cref="FileNotFoundException"/> — simulates a missing on-disk solution while the
        /// in-memory session is still registered (<c>workspace-close-missing-solution-on-disk</c>).
        /// </summary>
        public bool ReloadThrowsFileNotFound { get; set; }

        public FakeGateWorkspaceManager(bool containsAny = true)
        {
            _containsAny = containsAny;
        }

        public void RemoveWorkspace(string workspaceId) => _removed.TryAdd(workspaceId, 0);

        public void MarkStale(string workspaceId) => _stale.TryAdd(workspaceId, 0);

        public void ClearStaleFlag(string workspaceId) => _stale.TryRemove(workspaceId, out _);

        public event Action<string>? WorkspaceClosed { add { } remove { } }
        public event Action<string>? WorkspaceReloaded { add { } remove { } }

        public Task<WorkspaceStatusDto> LoadAsync(string path, CancellationToken ct) => throw new NotSupportedException();

        public Task<WorkspaceStatusDto> ReloadAsync(string workspaceId, CancellationToken ct)
        {
            Interlocked.Increment(ref ReloadCount);
            if (ReloadThrowsKeyNotFound)
            {
                throw new KeyNotFoundException($"Workspace '{workspaceId}' was closed between the stale check and the reload attempt.");
            }

            if (ReloadThrowsFileNotFound)
            {
                throw new FileNotFoundException(
                    $"Workspace path was not found: {workspaceId}",
                    workspaceId);
            }

            ClearStaleFlag(workspaceId);
            return Task.FromResult(new WorkspaceStatusDto(
                WorkspaceId: workspaceId,
                LoadedPath: null,
                WorkspaceVersion: 1,
                SnapshotToken: $"{workspaceId}:1",
                LoadedAtUtc: DateTime.UtcNow,
                ProjectCount: 0,
                DocumentCount: 0,
                Projects: [],
                IsLoaded: true,
                IsStale: false,
                WorkspaceDiagnostics: []));
        }

        public bool ContainsWorkspace(string workspaceId)
        {
            if (!_containsAny) return false;
            return !_removed.ContainsKey(workspaceId);
        }

        public bool IsStale(string workspaceId) => _stale.ContainsKey(workspaceId);

        public bool Close(string workspaceId) { _removed.TryAdd(workspaceId, 0); return true; }

        public IReadOnlyList<WorkspaceStatusDto> ListWorkspaces() => [];

        public WorkspaceStatusDto GetStatus(string workspaceId) => throw new NotSupportedException();
        public Task<WorkspaceStatusDto> GetStatusAsync(string workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ProjectGraphDto GetProjectGraph(string workspaceId) => throw new NotSupportedException();
        public Task<IReadOnlyList<GeneratedDocumentDto>> GetSourceGeneratedDocumentsAsync(string workspaceId, string? projectName, CancellationToken ct) => throw new NotSupportedException();
        public Task<string?> GetSourceTextAsync(string workspaceId, string filePath, CancellationToken ct) => Task.FromResult<string?>(null);
        public int GetCurrentVersion(string workspaceId) => 1;
        public void RestoreVersion(string workspaceId, int version) { }
        public Microsoft.CodeAnalysis.Solution GetCurrentSolution(string workspaceId) => throw new NotSupportedException();
        public bool TryApplyChanges(string workspaceId, Microsoft.CodeAnalysis.Solution newSolution) => throw new NotSupportedException();
        public Microsoft.CodeAnalysis.Project? GetProject(string workspaceId, string projectNameOrPath) => null;
    }
}
