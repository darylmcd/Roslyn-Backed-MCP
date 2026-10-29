using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Threading;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Manages named <c>MSBuildWorkspace</c> sessions, providing load, reload, query, and
/// solution-mutation operations. Each session is independently versioned and monitored
/// for file-system staleness via <see cref="RoslynMcp.Core.Services.IFileWatcherService"/>.
/// </summary>
public sealed class WorkspaceManager : IWorkspaceManager, IDisposable, IAsyncDisposable
{
    private const int MaxDiagnosticsPerWorkspace = 200;

    private static readonly Action<ILogger, string, string, int, Exception?> LogWorkspaceLoaded =
        LoggerMessage.Define<string, string, int>(
            LogLevel.Information, new EventId(1, nameof(LogWorkspaceLoaded)),
            "Loaded workspace {WorkspaceId} from {Path}, version {Version}");

    private static readonly Action<ILogger, string, int, Exception?> LogChangesApplied =
        LoggerMessage.Define<string, int>(
            LogLevel.Information, new EventId(2, nameof(LogChangesApplied)),
            "Applied workspace changes to {WorkspaceId}, new version {Version}");

    private static readonly Action<ILogger, string, Exception?> LogChangesApplyFailed =
        LoggerMessage.Define<string>(
            LogLevel.Warning, new EventId(3, nameof(LogChangesApplyFailed)),
            "Failed to apply workspace changes for {WorkspaceId}");

    private static readonly Action<ILogger, string, Exception?> LogWorkspaceClosed =
        LoggerMessage.Define<string>(
            LogLevel.Information, new EventId(4, nameof(LogWorkspaceClosed)),
            "Closed workspace {WorkspaceId}");

    private static readonly Action<ILogger, string, int, string, Exception?> LogSessionNotFound =
        LoggerMessage.Define<string, int, string>(
            LogLevel.Warning, new EventId(5, nameof(LogSessionNotFound)),
            "Workspace '{WorkspaceId}' not found. Active sessions ({Count}): [{ActiveIds}]");

    private readonly ILogger<WorkspaceManager> _logger;
    private readonly IPreviewStore _previewStore;
    private readonly IFileWatcherService _fileWatcher;
    internal IEditorConfigWriteCoordinator? EditorConfigWriteCoordinator => _fileWatcher as IEditorConfigWriteCoordinator;
    private readonly bool _ownsFileWatcher;
    private readonly WorkspaceManagerOptions _options;
    /// <summary>
    /// workspace-load-uses-cache-fast-path: optional persistent-cache coordinator. Null when
    /// not wired, preserving legacy no-cache load behavior.
    /// </summary>
    private readonly WorkspaceCacheCoordinator? _cacheCoordinator;
    /// <summary>
    /// workspace-manager-loadintosession-split: per-load workspace construction (MSBuild
    /// init + global-properties wiring + open + analyzer-reference retargeting). Injectable
    /// for failure-path testing via <see cref="WorkspaceSessionLoader"/>'s virtual surface.
    /// </summary>
    private readonly WorkspaceSessionLoader _sessionLoader;
    /// <summary>
    /// workspace-manager-decompose-restore-and-analyzer-subsystems: restore-staleness detection
    /// (restore-race wait + package-version drift check), extracted alongside
    /// <see cref="_analyzerReferenceStripper"/> so the concern is independently testable.
    /// </summary>
    private readonly RestoreStalenessDetector _restoreStalenessDetector;
    /// <summary>
    /// workspace-manager-decompose-restore-and-analyzer-subsystems: unresolved-analyzer-
    /// reference stripping, extracted from <see cref="LoadIntoSessionAsync"/>.
    /// </summary>
    private readonly UnresolvedAnalyzerReferenceStripper _analyzerReferenceStripper;
    /// <summary>
    /// lru-eviction-gate-layer-execution: lazily-resolved back-reference to the execution gate,
    /// used to run cap-pressure LRU eviction under the evicted workspace's per-workspace WRITER
    /// lock so in-flight gated readers/writers drain before the session is disposed.
    /// <para>
    /// The indirection is load-bearing: <see cref="WorkspaceExecutionGate"/>'s own constructor
    /// takes <see cref="IWorkspaceManager"/>, so a direct back-reference would be a
    /// constructor-level DI cycle. <see cref="Lazy{T}"/> defers the container lookup to first
    /// <c>.Value</c> access — by which point both singletons are already constructed and cached.
    /// </para>
    /// <para>
    /// <see langword="null"/> when unwired (direct test construction). On that path eviction
    /// falls back to the historic ungated <see cref="Close"/>, which keeps the constructor
    /// change purely additive for the test call sites that do not need the guarantee.
    /// </para>
    /// </summary>
    private readonly Lazy<IWorkspaceExecutionGate>? _evictionGate;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, WorkspaceSession> _sessions = new(StringComparer.Ordinal);
    /// <summary>
    /// mcp-error-category-workspace-evicted-on-host-recycle + workspace-id-recovery-hints:
    /// tracks workspace ids that were closed (or disposed) IN THIS PROCESS along with their
    /// original <c>loadedAt</c> AND <c>loadedPath</c> so a subsequent lookup can throw
    /// <see cref="WorkspaceEvictedException"/> with the recorded timestamp and the exact
    /// rehydration path instead of a bare <see cref="KeyNotFoundException"/>. Bounded so a
    /// long-lived host that loads/closes many workspaces doesn't grow this map without bound.
    /// </summary>
    private readonly ConcurrentDictionary<string, EvictedSessionRecord> _evictedWorkspaces = new(StringComparer.Ordinal);
    private const int MaxEvictedWorkspaceRecords = 128;

    private readonly record struct EvictedSessionRecord(DateTimeOffset LoadedAtUtc, string LoadedPath);
    /// <summary>Limits concurrent workspace sessions; paired with <see cref="Close"/> and <see cref="Dispose"/>.</summary>
    private readonly SemaphoreSlim _workspaceSlots;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly object _lifecycleSync = new();
    private readonly Dictionary<string, Task> _rootRetirementTasks = new(StringComparer.Ordinal);
    private bool _disposing;
    private bool _removalAdmissionClosed;
    private readonly record struct RetainedForkSource(int Count, string PhysicalLoadedPath);
    private readonly Dictionary<WorkspaceSession, RetainedForkSource> _sourceOperations = new();
    private readonly HashSet<string> _forkStorageDeletions = new(FileSystemPath.Comparer);
    private int _sourceOperationCount;
    private TaskCompletionSource? _sourceOperationsDrained;
    private int _slowRemovalCount;
    private TaskCompletionSource? _slowRemovalsDrained;
    private Task? _shutdownTask;
    private ExceptionDispatchInfo? _shutdownFailure;

    /// <summary>
    /// Retains the exact registered source session until the callback and its cleanup finish.
    /// This is logical lifecycle ownership; it does not lock the source against edits/reload.
    /// </summary>
    internal async Task<WorkspaceForkApplyResultDto> RunWithRetainedForkSourceAsync(
        string workspaceId, Func<CancellationToken, Task<WorkspaceForkApplyResultDto>> operation, CancellationToken ct)
    {
        WorkspaceSession session;
        string? loadedPath;
        lock (_lifecycleSync)
        {
            ObjectDisposedException.ThrowIf(_disposing, this);
            if (!_sessions.TryGetValue(workspaceId, out session!))
                throw CreateWorkspaceNotFoundException(workspaceId);
            loadedPath = session.LoadedPath;
        }
        var physicalPath = PhysicalPathResolver.Resolve(loadedPath!);
        lock (_lifecycleSync)
        {
            ObjectDisposedException.ThrowIf(_disposing, this);
            if (!_sessions.TryGetValue(workspaceId, out var current) || !ReferenceEquals(session, current))
                throw CreateWorkspaceNotFoundException(workspaceId);
            _sourceOperations.TryGetValue(session, out var retained);
            if (!FileSystemPath.Comparer.Equals(loadedPath, session.LoadedPath) ||
                (retained.Count > 0 && !FileSystemPath.Comparer.Equals(retained.PhysicalLoadedPath, physicalPath)) ||
                _forkStorageDeletions.Any(root => ForkPathAdmission.Contains(root, physicalPath)) ||
                !File.Exists(physicalPath))
                throw new PublicInvalidOperationException("The fork source backing file changed or is being deleted. Reopen the source and recreate the preview before retrying.");
            if (_sourceOperationCount++ == 0)
                _sourceOperationsDrained = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _sourceOperations[session] = new(retained.Count + 1, physicalPath);
        }

        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetimeCancellation.Token);
            return await operation(linked.Token).ConfigureAwait(false);
        }
        finally
        {
            lock (_lifecycleSync)
            {
                var retained = _sourceOperations[session];
                var remaining = retained.Count - 1;
                if (remaining == 0) _sourceOperations.Remove(session);
                else _sourceOperations[session] = retained with { Count = remaining };
                if (--_sourceOperationCount == 0) _sourceOperationsDrained!.TrySetResult();
            }
        }
    }

    /// <summary>
    /// Atomically admits deletion against logical source registration, including sources
    /// inside an enclosing victim. Physical ownership and slow deletion stay with the caller.
    /// </summary>
    internal IDisposable? TryReserveForkStorageDeletion(string path)
    {
        var physicalRoot = PhysicalPathResolver.Resolve(path);
        lock (_lifecycleSync)
        {
            ObjectDisposedException.ThrowIf(_removalAdmissionClosed, this);
            if (_sourceOperations.Values.Any(source => ForkPathAdmission.Contains(physicalRoot, source.PhysicalLoadedPath)) ||
                _forkStorageDeletions.Any(root => ForkPathAdmission.Contains(root, physicalRoot) || ForkPathAdmission.Contains(physicalRoot, root)))
                return null;
            _forkStorageDeletions.Add(physicalRoot);
            return new ForkStorageDeletionReservation(this, physicalRoot);
        }
    }

    private sealed class ForkStorageDeletionReservation(WorkspaceManager owner, string root) : IDisposable
    {
        private WorkspaceManager? _owner = owner;
        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _owner, null);
            if (current is null) return;
            lock (current._lifecycleSync) current._forkStorageDeletions.Remove(root);
        }
    }


    /// <inheritdoc />
    public event Action<string>? WorkspaceClosed;

    /// <inheritdoc />
    public event Action<string>? WorkspaceReloaded;

    public WorkspaceManager(
        ILogger<WorkspaceManager> logger,
        IPreviewStore previewStore,
        IFileWatcherService fileWatcher,
        WorkspaceManagerOptions? options = null,
        IWorkspaceCacheStore? cacheStore = null,
        Lazy<IWorkspaceExecutionGate>? evictionGate = null)
        : this(
            logger,
            previewStore,
            fileWatcher,
            options,
            cacheStore,
            sessionLoader: null,
            evictionGate: evictionGate,
            ownsFileWatcher: true)
    {
    }

    /// <summary>
    /// Creates a manager for an <see cref="IServiceProvider"/> that owns the supplied watcher.
    /// The manager still detaches its event subscription during disposal, but leaves watcher
    /// disposal to the provider so one singleton has one disposal owner.
    /// </summary>
    internal static WorkspaceManager CreateProviderOwned(
        ILogger<WorkspaceManager> logger,
        IPreviewStore previewStore,
        IFileWatcherService fileWatcher,
        WorkspaceManagerOptions? options,
        IWorkspaceCacheStore? cacheStore,
        Lazy<IWorkspaceExecutionGate>? evictionGate) =>
        new(
            logger,
            previewStore,
            fileWatcher,
            options,
            cacheStore,
            sessionLoader: null,
            evictionGate: evictionGate,
            ownsFileWatcher: false);

    /// <summary>
    /// Internal constructor used by <see cref="WorkspaceSessionLoaderFailureTests"/> to inject
    /// a throwing <see cref="WorkspaceSessionLoader"/> double and verify that
    /// <see cref="LoadIntoSessionAsync"/> preserves the autoreload-cascade invariant
    /// (session.Workspace stays pointed at the prior non-disposed workspace on partial-load
    /// failure).
    /// </summary>
    internal WorkspaceManager(
        ILogger<WorkspaceManager> logger,
        IPreviewStore previewStore,
        IFileWatcherService fileWatcher,
        WorkspaceManagerOptions? options,
        IWorkspaceCacheStore? cacheStore,
        WorkspaceSessionLoader? sessionLoader,
        RestoreStalenessDetector? restoreStalenessDetector = null,
        UnresolvedAnalyzerReferenceStripper? analyzerReferenceStripper = null,
        Lazy<IWorkspaceExecutionGate>? evictionGate = null,
        bool ownsFileWatcher = true,
        TimeProvider? timeProvider = null)
    {
        _logger = logger;
        _previewStore = previewStore;
        _fileWatcher = fileWatcher;
        _ownsFileWatcher = ownsFileWatcher;
        _options = options ?? new WorkspaceManagerOptions();
        _cacheCoordinator = cacheStore is null ? null : new WorkspaceCacheCoordinator(cacheStore, logger);
        _sessionLoader = sessionLoader ?? new WorkspaceSessionLoader();
        _restoreStalenessDetector = restoreStalenessDetector ?? new RestoreStalenessDetector();
        _analyzerReferenceStripper = analyzerReferenceStripper ?? new UnresolvedAnalyzerReferenceStripper();
        _evictionGate = evictionGate;
        _timeProvider = timeProvider ?? TimeProvider.System;
        var max = _options.MaxConcurrentWorkspaces > 0 ? _options.MaxConcurrentWorkspaces : 8;
        _workspaceSlots = new SemaphoreSlim(max, max);
        _fileWatcher.WorkspaceRootMissing += OnWorkspaceRootMissing;
    }

    public bool ContainsWorkspace(string workspaceId) =>
        !string.IsNullOrWhiteSpace(workspaceId) && _sessions.ContainsKey(workspaceId);

    /// <inheritdoc />
    public bool IsStale(string workspaceId) =>
        ContainsWorkspace(workspaceId) && _fileWatcher.IsStale(workspaceId);

    /// <inheritdoc />
    public string? GetStaleReason(string workspaceId) =>
        ContainsWorkspace(workspaceId) ? _fileWatcher.GetStaleReason(workspaceId) : null;

    /// <inheritdoc />
    public void EnsureFreshForWritePreview(string workspaceId)
    {
        // workspace-stale-after-external-edit-feedback: write-preview tools (change_signature_
        // preview, move_type_to_file_preview, etc.) must refuse when the file watcher has
        // observed an external edit the server did NOT produce. Applying against a drifted
        // tree would compose edits from a snapshot that no longer matches disk, so the caller
        // could silently clobber the external change at *_apply time. Auto-reload is the wrong
        // remedy here — it would transparently swallow the drift. The caller must explicitly
        // decide (workspace_reload to accept the new on-disk state, or revert the external
        // edit and retry).
        //
        // The message contains "stale" so ToolErrorHandler.BuildExceptionMessage (which scans
        // for stale-workspace indicators) appends the workspace_reload suggestion to the error
        // envelope; the explicit workspace_reload mention below is the human-readable hint.
        var reason = GetStaleReason(workspaceId);
        if (reason == StaleReasons.ExternalEdit)
        {
            throw new InvalidOperationException(
                $"Workspace '{workspaceId}' is stale due to an external edit (staleReason='{reason}'). " +
                "A tracked .cs/.csproj/.slnx file changed on disk outside this server's apply channel. " +
                "Write-preview tools refuse in this state to avoid composing edits against a drifted snapshot. " +
                "Call workspace_reload to accept the on-disk state, then re-run the preview.");
        }
    }

    /// <summary>
    /// Returns the first live session whose <see cref="WorkspaceSession.LoadedPath"/> matches
    /// the given full path (case-insensitive on Windows where paths are case-insensitive).
    /// Used by <see cref="LoadAsync"/> to implement the
    /// <c>workspace-session-deduplication</c> fix so repeat loads of the same path return
    /// the existing session instead of spinning up a new <c>WorkspaceId</c>.
    /// </summary>
    private WorkspaceSession? FindSessionByLoadedPath(string fullPath)
    {
        foreach (var candidate in _sessions.Values)
        {
            if (!string.IsNullOrWhiteSpace(candidate.LoadedPath)
                && string.Equals(candidate.LoadedPath, fullPath, FileSystemPath.Comparison))
            {
                return candidate;
            }
        }
        return null;
    }

    public Task<WorkspaceStatusDto> LoadAsync(string path, CancellationToken ct)
        => LoadAsync(path, EvictPolicy.Strict, globalProperties: null, ct);

    public Task<WorkspaceStatusDto> LoadAsync(string path, EvictPolicy evictPolicy, CancellationToken ct)
        => LoadAsync(path, evictPolicy, globalProperties: null, ct);

    public Task<WorkspaceStatusDto> LoadAsync(
        string path,
        IDictionary<string, string>? globalProperties,
        CancellationToken ct)
        => LoadAsync(path, EvictPolicy.Strict, globalProperties, ct);

    public async Task<WorkspaceStatusDto> LoadAsync(
        string path,
        EvictPolicy evictPolicy,
        IDictionary<string, string>? globalProperties,
        CancellationToken ct)
    {
        var fullPath = ValidateWorkspacePath(path);

        // workspace-session-deduplication: if the caller is loading a path that is already
        // tracked by a live session, return that session's status instead of spinning up a
        // second WorkspaceId. Prevents the "one host, two distinct IDs for the same solution"
        // pattern from the 2026-04-08 roslyn-backed-mcp audit and keeps workspace_list aligned
        // with operator expectations. Matches the "idempotent for repeated loads" language
        // already in the workspace_load tool description.
        //
        // When the caller passes MSBuild global properties (e.g. Configuration=Release), skip
        // dedup so the new evaluation actually runs — an existing session was loaded with
        // different globals and would not satisfy the caller's request.
        var skipDedup = globalProperties is { Count: > 0 };
        if (!skipDedup)
        {
            var existing = FindSessionByLoadedPath(fullPath);
            if (existing is not null)
            {
                existing.TouchAccess();
                _logger.LogInformation(
                    "workspace_load: returning existing workspace '{WorkspaceId}' for path '{Path}' (idempotent)",
                    existing.WorkspaceId,
                    fullPath);
                return BuildStatus(existing);
            }
        }

        if (!await _workspaceSlots.WaitAsync(0, ct).ConfigureAwait(false))
        {
            if (evictPolicy == EvictPolicy.Lru)
            {
                // A finite snapshot permits reconsidering later victims when retention wins
                // after selection. Removal rechecks the exact session under lifecycle sync.
                WorkspaceSession[] candidates;
                lock (_lifecycleSync)
                {
                    candidates = _sessions.Values
                        .Where(session => session.LoadLock.CurrentCount > 0 && !_sourceOperations.ContainsKey(session))
                        .OrderBy(session => session.LastAccessedUtc).ToArray();
                }
                foreach (var candidate in candidates)
                {
                    ct.ThrowIfCancellationRequested();
                    await EvictForCapAsync(candidate, ct).ConfigureAwait(false);
                    if (await _workspaceSlots.WaitAsync(0, ct).ConfigureAwait(false)) goto slotAcquired;
                }
            }

            bool retainedSource;
            lock (_lifecycleSync) retainedSource = _sourceOperationCount > 0;
            if (retainedSource)
                throw new PublicInvalidOperationException(
                    "Workspace capacity is full and an active fork retains its source workspace. " +
                    "Close another idle workspace, increase workspace capacity, or wait for or cancel the fork before closing its source.");

            var activeWorkspaces = _sessions.Values
                .Select(s => new
                {
                    id = s.WorkspaceId,
                    loadedAtUtc = s.LoadedAtUtc,
                    lastAccessedUtc = s.LastAccessedUtc,
                    path = s.LoadedPath ?? string.Empty,
                })
                .OrderBy(s => s.lastAccessedUtc)
                .ToList();
            var lruCandidateId = activeWorkspaces.FirstOrDefault()?.id;
            throw new InvalidOperationException(
                $"The server is already tracking {_options.MaxConcurrentWorkspaces} workspaces. " +
                $"Close an existing workspace before loading another (or pass evictPolicy=lru to auto-evict the idle workspace). " +
                $"activeWorkspaces={JsonSerializer.Serialize(activeWorkspaces)}" +
                (lruCandidateId is not null ? $" lruCandidate={lruCandidateId}" : string.Empty));
        }

    slotAcquired:
        var workspaceId = Guid.NewGuid().ToString("N");
        var session = new WorkspaceSession(workspaceId);
        // True once the registered session owns the capacity slot. From then on only the exact-session
        // removal path (RemoveSessionEffects) returns it; this method returns it only while still false.
        var slotTransferred = false;
        try
        {
            await LoadIntoSessionAsync(session, fullPath, globalProperties, ct).ConfigureAwait(false);

            // Second dedup check, this time for the race window between our scan above and
            // another concurrent LoadAsync call that won the semaphore with the same path.
            // Whichever caller lost the race returns the winner's session and releases its
            // slot in the finally block so we do not leak a slot per racing caller. Skipped
            // under global-property overrides for the same reason as the pre-load dedup above.
            if (!skipDedup)
            {
                var raceWinner = FindSessionByLoadedPath(fullPath);
                if (raceWinner is not null)
                {
                    _logger.LogInformation(
                        "workspace_load: race lost; returning winner '{WorkspaceId}' for path '{Path}' instead of new '{NewId}'",
                        raceWinner.WorkspaceId,
                        fullPath,
                        workspaceId);
                    // The loser was never registered: a dispose failure must not fail the winner's successful status.
                    var loserFailures = new List<Exception>();
                    AttemptCleanup(session.Dispose, loserFailures);
                    if (loserFailures.Count > 0)
                        _logger.LogError(
                            new AggregateException(loserFailures),
                            "Disposing race-losing workspace '{WorkspaceId}' failed",
                            workspaceId);
                    raceWinner.TouchAccess();
                    return BuildStatus(raceWinner);
                }
            }

            if (!_sessions.TryAdd(workspaceId, session))
            {
                throw new InvalidOperationException($"Workspace '{workspaceId}' already exists.");
            }

            slotTransferred = true;
            _fileWatcher.Watch(workspaceId, session.LoadedPath!);
            return BuildStatus(session);
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException)
            {
                // Capture any workspace diagnostics collected before the failure
                var diagnostics = session.WorkspaceDiagnostics.ToArray();
                if (diagnostics.Length > 0)
                {
                    _logger.LogError(
                        ex,
                        "Workspace load failed for {Path} with {DiagnosticCount} diagnostics captured",
                        fullPath,
                        diagnostics.Length);
                    foreach (var diag in diagnostics.Take(10))
                    {
                        _logger.LogError("  [{Severity}] {Message}", diag.Severity, diag.Message);
                    }
                }
            }

            // Compensate without replacing the primary failure or cancellation: cleanup failures are logged.
            var cleanupFailures = new List<Exception>();
            if (slotTransferred)
            {
                // Registered: remove exactly this session through the single removal path (unwatch, preview
                // invalidation, dispose, slot return, WorkspaceClosed). It is a no-op if a concurrent close,
                // eviction or shutdown already removed (and so owns the slot return for) this session.
                AttemptCleanup(
                    () => CloseCore(workspaceId, recordEviction: false, refuseRetained: false, expected: session),
                    cleanupFailures);
            }
            else
            {
                AttemptCleanup(session.Dispose, cleanupFailures);
            }

            if (cleanupFailures.Count > 0)
                _logger.LogError(
                    new AggregateException(cleanupFailures),
                    "Cleanup after failed workspace load of '{WorkspaceId}' failed; original failure is rethrown",
                    workspaceId);
            throw;
        }
        finally
        {
            if (!slotTransferred)
            {
                _workspaceSlots.Release();
            }
        }
    }

    /// <summary>
    /// lru-eviction-gate-layer-execution: close a cap-pressure LRU eviction candidate under
    /// <see cref="IWorkspaceExecutionGate"/>'s per-workspace WRITER lock, so any in-flight gated
    /// reader or writer on that workspace drains before the session is removed and disposed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Shape is copied from <c>workspace_close</c> (<c>WorkspaceTools.CloseWorkspace</c>):
    /// <c>RunWriteAsync(id, …, applyStalenessPolicy: false)</c> — staleness policy is off because
    /// an auto-reload of a workspace we are about to dispose is pure waste and can throw when the
    /// on-disk solution is already gone — followed by <c>RemoveGate(id)</c> STRICTLY AFTER the
    /// write completes, never from inside the closure, so the registry entry is dropped only once
    /// the writer lock it guards has been released. Doing so also fixes an adjacent leak: the old
    /// direct-<see cref="Close"/> eviction path never called <c>RemoveGate</c>, so every
    /// LRU-evicted workspace left an orphaned lock entry in the gate's registry.
    /// </para>
    /// <para>
    /// Reentrancy is safe. In production <c>LoadAsync</c> already runs inside
    /// <c>gate.RunLoadGateAsync</c>, whose <c>WithGlobalThrottle</c> depth counter lets this
    /// nested <c>RunWriteAsync</c> reuse the throttle slot instead of consuming a second one —
    /// the identical nesting <c>workspace_close</c> and <c>workspace_reload</c> already rely on.
    /// The writer lock taken here is for the EVICTED workspace, never the one being loaded, so it
    /// cannot self-deadlock.
    /// </para>
    /// <para>
    /// When no gate is wired (<c>_evictionGate is null</c> — direct test construction) this falls
    /// back to the historic ungated <see cref="Close"/> so the added constructor parameter stays
    /// purely additive.
    /// </para>
    /// </remarks>
    private async Task EvictForCapAsync(WorkspaceSession candidate, CancellationToken ct)
    {
        var workspaceId = candidate.WorkspaceId;
        if (_evictionGate is null)
        {
            CloseCore(workspaceId, recordEviction: true, refuseRetained: false, expected: candidate);
            return;
        }

        var gate = _evictionGate.Value;
        var removed = false;
        try
        {
            await gate.RunWriteAsync(
                workspaceId,
                _ =>
                {
                    removed = CloseCore(workspaceId, recordEviction: true, refuseRetained: false, expected: candidate);
                    return Task.FromResult(removed);
                },
                ct,
                applyStalenessPolicy: false).ConfigureAwait(false);
        }
        catch (KeyNotFoundException)
        {
            // A concurrent caller (another LRU eviction, or an explicit workspace_close) already
            // removed this candidate between our selection scan and the writer-lock acquisition.
            // The gate raises KeyNotFoundException from either its pre-lock existence check or
            // EnsureWorkspaceStillExists inside the lock. Nothing left to evict — and that caller
            // owns the RemoveGate for the id — so return and let the caller's slot retry decide
            // between proceeding and the Strict error path, mirroring the existing
            // "slot consumed by a concurrent caller" fallback.
            return;
        }

        if (removed) gate.RemoveGate(workspaceId);
    }

    public async Task<WorkspaceStatusDto> ReloadAsync(string workspaceId, CancellationToken ct)
    {
        var session = GetRequiredSession(workspaceId);
        await LoadIntoSessionAsync(session, session.LoadedPath ?? throw new InvalidOperationException(
            $"Workspace '{workspaceId}' is not loaded."),
            ct).ConfigureAwait(false);
        _fileWatcher.ClearStale(workspaceId);
        // Item #7: explicit reload signal for per-workspace caches. The version bump inside
        // LoadIntoSessionAsync is enough for caches that version-check on every read, but
        // firing the event synchronously here (a) makes the invalidation contract explicit,
        // (b) lets caches drop references immediately rather than on next read (freeing
        // memory sooner when a large workspace is reloaded), and (c) defends against future
        // caches that forget to version-check.
        RaiseWorkspaceReloaded(workspaceId);
        return BuildStatus(session);
    }

    public bool Close(string workspaceId) => CloseCore(workspaceId, recordEviction: true, refuseRetained: true, expected: null);

    private bool CloseCore(string workspaceId, bool recordEviction, bool refuseRetained, WorkspaceSession? expected)
    {
        WorkspaceSession session;
        lock (_lifecycleSync)
        {
            ObjectDisposedException.ThrowIf(_removalAdmissionClosed, this);
            if (!_sessions.TryGetValue(workspaceId, out session!) || (expected is not null && !ReferenceEquals(session, expected)))
                return false;
            if (_sourceOperations.ContainsKey(session))
            {
                if (refuseRetained)
                    throw new PublicInvalidOperationException("Workspace is busy as a fork source. Wait for the fork to finish or cancel it before closing the workspace.");
                return false;
            }
            if (!_sessions.TryRemove(new KeyValuePair<string, WorkspaceSession>(workspaceId, session))) return false;
            if (_slowRemovalCount++ == 0)
                _slowRemovalsDrained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        try { RemoveSessionEffects(session, recordEviction); }
        finally
        {
            lock (_lifecycleSync)
            {
                if (--_slowRemovalCount == 0) _slowRemovalsDrained!.TrySetResult();
            }
        }
        return true;
    }

    private void RemoveSessionEffects(WorkspaceSession session, bool recordEviction)
    {
        var failures = new List<Exception>();
        var workspaceId = session.WorkspaceId;
        if (recordEviction) AttemptCleanup(() => RecordEviction(workspaceId, session.LoadedAtUtc, session.LoadedPath), failures);
        AttemptCleanup(() => _fileWatcher.Unwatch(workspaceId), failures);
        AttemptCleanup(() => _previewStore.InvalidateAll(workspaceId), failures);
        AttemptCleanup(session.Dispose, failures);
        AttemptCleanup(() => _workspaceSlots.Release(), failures);
        AttemptCleanup(() => RaiseWorkspaceClosed(workspaceId), failures);
        AttemptCleanup(() => LogWorkspaceClosed(_logger, workspaceId, null), failures);
        if (failures.Count > 0) throw new AggregateException("Workspace removal failed.", failures);
    }

    private static void AttemptCleanup(Action cleanup, List<Exception> failures)
    {
        try { cleanup(); }
        catch (Exception exception) { failures.Add(exception); }
    }

    private void OnWorkspaceRootMissing(string workspaceId)
    {
        TaskCompletionSource start;
        lock (_lifecycleSync)
        {
            if (_disposing || _rootRetirementTasks.ContainsKey(workspaceId)) return;
            start = new TaskCompletionSource();
            // The first await is a publication barrier, not cleanup. It cannot resume
            // until after the lifecycle lock is released.
            var task = RetireMissingWorkspaceAsync(workspaceId, start.Task);
            _rootRetirementTasks.Add(workspaceId, task);
        }
        start.SetResult();
    }

    private async Task RetireMissingWorkspaceAsync(string workspaceId, Task start)
    {
        await start.ConfigureAwait(false);
        try
        {
            var retryDelay = TimeSpan.FromMilliseconds(300);
            while (!_lifetimeCancellation.IsCancellationRequested)
            {
                await Task.Delay(retryDelay, _timeProvider, _lifetimeCancellation.Token).ConfigureAwait(false);
                if (!IsWorkspaceBackingPathConfirmedMissing(workspaceId))
                {
                    return;
                }

                if (_evictionGate is null)
                {
                    if (CloseCore(workspaceId, recordEviction: false, refuseRetained: false, expected: null)) return;
                    continue;
                }

                var gate = _evictionGate.Value;
                var retired = false;
                try
                {
                    await gate.RunWriteAsync(
                        workspaceId,
                        _ =>
                        {
                            retired = IsWorkspaceBackingPathConfirmedMissing(workspaceId) &&
                                      CloseCore(workspaceId, recordEviction: false, refuseRetained: false, expected: null);
                            return Task.FromResult(retired);
                        },
                        _lifetimeCancellation.Token,
                        applyStalenessPolicy: false).ConfigureAwait(false);
                }
                catch (KeyNotFoundException)
                {
                    return;
                }
                catch (Exception exception) when (!_lifetimeCancellation.IsCancellationRequested)
                {
                    _logger.LogWarning(
                        exception,
                        "Failed to retire missing workspace backing path for {WorkspaceId}; retrying",
                        workspaceId);
                    retryDelay = TimeSpan.FromMilliseconds(Math.Min(retryDelay.TotalMilliseconds * 2, 2000));
                    continue;
                }

                if (retired)
                {
                    try
                    {
                        gate.RemoveGate(workspaceId);
                    }
                    catch (Exception exception)
                    {
                        // The session is already retired. A gate-registry cleanup failure must
                        // not prevent manager-owned retirement completion, but remains
                        // operationally visible for diagnosis.
                        _logger.LogWarning(
                            exception,
                            "Retired workspace {WorkspaceId}, but failed to remove its execution gate",
                            workspaceId);
                    }
                }

                if (retired) return;
                // A still-retained source remains eligible for a later retirement pass.
            }
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            lock (_lifecycleSync)
            {
                _rootRetirementTasks.Remove(workspaceId);
            }
        }
    }

    private bool IsWorkspaceBackingPathConfirmedMissing(string workspaceId)
    {
        if (!_sessions.TryGetValue(workspaceId, out var session) ||
            string.IsNullOrWhiteSpace(session.LoadedPath))
        {
            return false;
        }

        try
        {
            _ = File.GetAttributes(session.LoadedPath);
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return true;
        }
        catch (FileNotFoundException)
        {
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Records an in-process workspace eviction so a subsequent lookup against the same id
    /// can surface a structured <see cref="WorkspaceEvictedException"/> with both the
    /// original <c>loadedAt</c> and <c>loadedPath</c>. Bounded eviction: once
    /// <see cref="MaxEvictedWorkspaceRecords"/> entries are tracked we drop a single
    /// oldest record (best-effort; the map is not strictly time-ordered, so "oldest" is
    /// whatever the dictionary enumerator surfaces first — adequate for a degraded "we used
    /// to know about this id" signal where the exact timestamp ordering does not matter).
    /// </summary>
    private void RecordEviction(string workspaceId, DateTimeOffset loadedAtUtc, string? loadedPath)
    {
        _evictedWorkspaces[workspaceId] = new EvictedSessionRecord(loadedAtUtc, loadedPath ?? string.Empty);

        if (_evictedWorkspaces.Count > MaxEvictedWorkspaceRecords)
        {
            foreach (var key in _evictedWorkspaces.Keys)
            {
                if (_evictedWorkspaces.Count <= MaxEvictedWorkspaceRecords) break;
                _evictedWorkspaces.TryRemove(key, out _);
            }
        }
    }

    /// <summary>
    /// Notifies subscribers (e.g., <see cref="ICompilationCache"/>) that a workspace has been
    /// closed. Wrapped so that handler exceptions cannot break the close path.
    /// </summary>
    private void RaiseWorkspaceClosed(string workspaceId)
    {
        RaiseWorkspaceLifecycleEvent(WorkspaceClosed, nameof(WorkspaceClosed), workspaceId);
    }

    /// <summary>
    /// Notifies subscribers that a workspace has been reloaded. Wrapped so that handler
    /// exceptions cannot break the reload path.
    /// </summary>
    private void RaiseWorkspaceReloaded(string workspaceId)
    {
        RaiseWorkspaceLifecycleEvent(WorkspaceReloaded, nameof(WorkspaceReloaded), workspaceId);
    }

    private void RaiseWorkspaceLifecycleEvent(
        Action<string>? handlers,
        string eventName,
        string workspaceId)
    {
        if (handlers is null) return;

        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action<string>)handler)(workspaceId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "{EventName} handler threw for {WorkspaceId}; exceptionType={ExceptionType}",
                    eventName,
                    workspaceId,
                    ex.GetType().FullName);
            }
        }
    }

    public IReadOnlyList<WorkspaceStatusDto> ListWorkspaces()
    {
        return _sessions.Values.Select(BuildStatus).ToList();
    }

    public IReadOnlyList<string> FindWorkspaceIdsContainingFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return [];
        }

        string fullPath;
        try
        {
            fullPath = PhysicalPathResolver.Resolve(filePath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException)
        {
            return [];
        }

        return _sessions.Values
            .Where(session => session.DocumentPaths.Contains(fullPath))
            .Select(session => session.WorkspaceId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
    }

    public WorkspaceStatusDto GetStatus(string workspaceId)
    {
        var session = GetRequiredSession(workspaceId);

        // workspace-status-verbose-5s-timeout-race-on-ready-workspace: do NOT acquire LoadLock here.
        // BuildStatus only reads thread-safe fields: ProjectStatuses is an ImmutableArray (atomic
        // reference replacement), WorkspaceDiagnostics is a ConcurrentQueue (concurrent-read safe),
        // and the remaining scalar reads (LoadedPath, Workspace, Version, LoadedAtUtc, RestoreRequired)
        // are individual reference/value reads that the field types and Volatile.Read on Version
        // already make safe. Any cross-field "snapshot consistency" the lock might have preserved
        // is illusory — other callers of BuildStatus (LoadAsync's race-winner path, fresh-load return,
        // ReloadAsync return, ListWorkspaces enumeration) already invoke it WITHOUT the lock, and the
        // outer gate.RunReadAsync at the tool layer already serializes against concurrent writes via
        // the per-workspace reader/writer lock. Pre-fix, this Wait(5s) raced an in-flight
        // LoadIntoSessionAsync (which holds LoadLock for the entire MSBuild load — 30+ seconds on
        // medium solutions) and timed out on a workspace that workspace_status had just reported
        // isReady=true moments earlier.
        return BuildStatus(session);
    }

    public Task<WorkspaceStatusDto> GetStatusAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        var session = GetRequiredSession(workspaceId);

        // See GetStatus above: BuildStatus reads only thread-safe fields, so no LoadLock is required.
        // The outer gate.RunReadAsync already serializes against concurrent writes.
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(BuildStatus(session));
    }

    public ProjectGraphDto GetProjectGraph(string workspaceId)
    {
        var session = GetRequiredSession(workspaceId);
        var projects = session.ProjectStatuses.Select(project => new ProjectGraphNodeDto(
            ProjectName: project.Name,
            FilePath: project.FilePath,
            AssemblyName: project.AssemblyName,
            IsTestProject: project.IsTestProject,
            OutputType: project.OutputType,
            TargetFrameworks: project.TargetFrameworks,
            ProjectReferences: project.ProjectReferences)).ToList();

        return new ProjectGraphDto(workspaceId, projects);
    }

    public async Task<IReadOnlyList<GeneratedDocumentDto>> GetSourceGeneratedDocumentsAsync(
        string workspaceId,
        string? projectName,
        CancellationToken ct)
    {
        var solution = GetCurrentSolution(workspaceId);
        var results = new List<GeneratedDocumentDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in solution.Projects.Where(project =>
                     projectName is null || string.Equals(project.Name, projectName, StringComparison.OrdinalIgnoreCase)))
        {
            if (results.Count >= _options.MaxSourceGeneratedDocuments)
            {
                break;
            }

            var generatedDocuments = await project.GetSourceGeneratedDocumentsAsync(ct).ConfigureAwait(false);
            var projectResults = generatedDocuments.Select(document => new GeneratedDocumentDto(
                ProjectName: project.Name,
                HintName: document.Name,
                FilePath: document.FilePath ?? document.Name))
                .ToList();

            if (projectResults.Count == 0 &&
                results.Count < _options.MaxSourceGeneratedDocuments &&
                !string.IsNullOrWhiteSpace(project.FilePath))
            {
                var projectDirectory = Path.GetDirectoryName(project.FilePath);
                if (!string.IsNullOrWhiteSpace(projectDirectory))
                {
                    var objDirectory = Path.Combine(projectDirectory, "obj");
                    if (Directory.Exists(objDirectory))
                    {
                        // Deduplicate Debug/Release/platform copies of the same generated file name
                        projectResults.AddRange(Directory
                            .EnumerateFiles(objDirectory, "*.g.cs", SearchOption.AllDirectories)
                            .GroupBy(filePath => Path.GetFileName(filePath), StringComparer.OrdinalIgnoreCase)
                            .Select(g => g.OrderBy(f => f.Length).First())
                            .Select(filePath => new GeneratedDocumentDto(
                                ProjectName: project.Name,
                                HintName: Path.GetFileName(filePath),
                                FilePath: filePath)));
                    }
                }
            }

            foreach (var generated in projectResults)
            {
                if (results.Count >= _options.MaxSourceGeneratedDocuments)
                {
                    break;
                }

                if (seen.Add($"{generated.ProjectName}\u001F{generated.HintName}"))
                {
                    results.Add(generated);
                }
            }
        }

        return results;
    }

    public async Task<string?> GetSourceTextAsync(string workspaceId, string filePath, CancellationToken ct)
    {
        var solution = GetCurrentSolution(workspaceId);
        var canonicalFilePath = PhysicalPathResolver.Resolve(filePath);
        var document = Helpers.SymbolResolver.FindDocument(solution, filePath);

        // Also search source-generated documents if not found in regular documents
        if (document is null)
        {
            foreach (var project in solution.Projects)
            {
                try
                {
                    var sourceGenDocs = await project.GetSourceGeneratedDocumentsAsync(ct).ConfigureAwait(false);
                    document = sourceGenDocs.FirstOrDefault(d =>
                        d.FilePath is not null &&
                        FileSystemPath.Comparer.Equals(PhysicalPathResolver.Resolve(d.FilePath), canonicalFilePath));
                    if (document is not null) break;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogDebug(ex, "Skipping source-generated doc search in project {ProjectName}", project.Name);
                }
            }
        }

        if (document is null) return null;

        try
        {
            var text = await document.GetTextAsync(ct).ConfigureAwait(false);
            return text.ToString();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Fall back to reading from disk if workspace text retrieval fails
            _logger.LogWarning(ex, "Failed to get text from workspace for {FilePath}, falling back to disk read", filePath);
            var resolvedPath = document.FilePath ?? filePath;
            if (File.Exists(resolvedPath))
            {
                return await File.ReadAllTextAsync(resolvedPath, ct).ConfigureAwait(false);
            }
            return null;
        }
    }

    public int GetCurrentVersion(string workspaceId)
    {
        return GetRequiredSession(workspaceId).Version;
    }

    public void RestoreVersion(string workspaceId, int version)
    {
        GetRequiredSession(workspaceId).SetVersion(version);
    }

    public Solution GetCurrentSolution(string workspaceId)
    {
        var session = GetRequiredSession(workspaceId);
        var workspace = session.Workspace;
        if (workspace is null)
        {
            // autoreload-cascade-stdio-host-crash: a null workspace observed after the session
            // was registered means a reload failed mid-flight (or is executing on another turn
            // and has not yet completed the atomic swap). Surface as StaleWorkspaceTransition
            // so the caller sees a retry-able envelope rather than a generic invalid-operation
            // that historically let the raw exception terminate the stdio host.
            throw new StaleWorkspaceTransitionException(
                workspaceId,
                $"Workspace '{workspaceId}' is in the middle of a reload transition or its last " +
                "reload failed. Retry the read after workspace_reload settles, or call " +
                "workspace_reload explicitly to re-establish a valid snapshot.");
        }

        try
        {
            return workspace.CurrentSolution;
        }
        catch (ObjectDisposedException ex)
        {
            // Defensive: even with the atomic swap in LoadIntoSessionAsync, if a caller held a
            // captured reference to the previous workspace across a reload, reading its solution
            // can observe the disposed instance. Translate that to a structured transition error
            // (retry-able) rather than letting it bubble as a generic InternalError that killed
            // the stdio host in the 2026-04-24 cascade.
            throw new StaleWorkspaceTransitionException(
                workspaceId,
                $"Workspace '{workspaceId}' snapshot reference was disposed during a concurrent " +
                "reload. Retry the read to observe the fresh snapshot.",
                ex);
        }
    }

    /// <summary>
    /// solution-project-index-by-name: names and raw paths cached per workspace/version.
    /// Physical paths are indexed only for fallback lookup.
    /// A different observed workspace version rebuilds the entry.
    /// </summary>
    private readonly ConcurrentDictionary<string, ProjectIndexEntry> _projectIndex = new(StringComparer.Ordinal);

    internal sealed record ProjectIndexHit(int Order, Project Project);

    internal sealed class ProjectIndexEntry(
        int version,
        IReadOnlyDictionary<string, ProjectIndexHit> byName,
        IReadOnlyDictionary<string, ProjectIndexHit> byLexicalPath,
        IReadOnlyList<(string Path, Project Project)> physicalCandidates)
    {
        public int Version { get; } = version;
        private readonly IReadOnlyDictionary<string, ProjectIndexHit> _byName = byName;
        private readonly IReadOnlyDictionary<string, ProjectIndexHit> _byLexicalPath = byLexicalPath;
        private readonly IReadOnlyList<(string Path, Project Project)> _physicalCandidates = physicalCandidates;
        private readonly object _physicalPathsLock = new();
        private Dictionary<string, Project>? _physicalPaths;

        public Project? Find(string nameOrPath)
        {
            _byName.TryGetValue(nameOrPath, out var name);
            _byLexicalPath.TryGetValue(nameOrPath, out var lexical);
            // Preserve solution order when name and platform-aware lexical path both match.
            // A newly recognized alias must not displace an existing literal name.
            if (name is not null)
                return lexical is null || name.Order <= lexical.Order ? name.Project : lexical.Project;
            if (lexical is not null) return lexical.Project;

            if (nameOrPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0
                || (Path.IsPathRooted(nameOrPath) && !Path.IsPathFullyQualified(nameOrPath))) return null;

            var physicalPath = PhysicalPathResolver.Resolve(nameOrPath);
            lock (_physicalPathsLock)
            {
                if (_physicalPaths is null)
                {
                    var physicalPaths = new Dictionary<string, Project>(FileSystemPath.Comparer);
                    foreach (var candidate in _physicalCandidates)
                        physicalPaths.TryAdd(PhysicalPathResolver.Resolve(candidate.Path), candidate.Project);
                    // Publish only after all candidates resolve; a failed build leaves no cached map.
                    _physicalPaths = physicalPaths;
                }
                return _physicalPaths.TryGetValue(physicalPath, out var physical) ? physical : null;
            }
        }
    }

    public Project? GetProject(string workspaceId, string projectNameOrPath)
    {
        if (string.IsNullOrWhiteSpace(projectNameOrPath)) return null;

        var session = GetRequiredSession(workspaceId);
        var solution = session.Workspace?.CurrentSolution;
        if (solution is null) return null;

        var version = session.Version;
        var entry = _projectIndex.AddOrUpdate(
            workspaceId,
            _ => BuildProjectIndex(version, solution),
            (_, existing) => existing.Version == version ? existing : BuildProjectIndex(version, solution));

        return entry.Find(projectNameOrPath);
    }

    internal static ProjectIndexEntry BuildProjectIndex(int version, Solution solution)
    {
        var names = new Dictionary<string, ProjectIndexHit>(StringComparer.OrdinalIgnoreCase);
        var lexicalPaths = new Dictionary<string, ProjectIndexHit>(FileSystemPath.Comparer);
        var order = 0;
        foreach (var project in solution.Projects)
        {
            var hit = new ProjectIndexHit(order++, project);
            if (!string.IsNullOrEmpty(project.Name)) names.TryAdd(project.Name, hit);
            if (!string.IsNullOrEmpty(project.FilePath))
            {
                lexicalPaths.TryAdd(project.FilePath, hit);
            }
        }
        var physicalCandidates = lexicalPaths.OrderBy(pair => pair.Value.Order)
            .Select(pair => (pair.Key, pair.Value.Project)).ToArray();
        return new ProjectIndexEntry(version, names, lexicalPaths, physicalCandidates);
    }

    public bool TryApplyChanges(string workspaceId, Solution newSolution)
    {
        var session = GetRequiredSession(workspaceId);
        if (session.Workspace is null)
        {
            throw new InvalidOperationException($"Workspace '{workspaceId}' is not loaded.");
        }

        EnsureChangedDocumentPathsArePhysical(session.Workspace.CurrentSolution, newSolution);
        var result = session.Workspace.TryApplyChanges(newSolution);
        if (result)
        {
            session.DocumentPaths = BuildDocumentPathIndex(session.Workspace.CurrentSolution);
            session.IncrementVersion();
            // workspace-stale-after-external-edit-feedback: attribute the pending watcher fire
            // (MSBuildWorkspace.TryApplyChanges writes .cs/.csproj files to disk on its way
            // out) to reason="apply" BEFORE the watcher event lands. Last-writer-wins inside
            // the stale window, so a racing watcher event for an unrelated external edit may
            // still overwrite "apply" with "external-edit" — which is the correct escalation.
            _fileWatcher.MarkStale(workspaceId, StaleReasons.Apply);
            // preview-token-cross-coupling-bundle (BREAKING): do NOT InvalidateAll sibling
            // preview tokens on a successful apply. Each PreviewEntry holds its own immutable
            // Roslyn Solution snapshot captured at preview time; sibling `*_apply` calls must
            // not destroy those references. The apply path rebases each token's snapshot
            // against the CURRENT solution via `modifiedSolution.GetChanges(currentSolution)`
            // at redemption time (RefactoringService.ApplyRefactoringAsync), so unrelated
            // workspace moves don't need to invalidate tokens.
            //
            // format-range-apply-preview-token-lifetime: sibling-apply bumps the session
            // version (above) but does NOT call any preview-store invalidation here — pruning
            // is reload- and close-driven only. The pinned-range eviction triggered by the
            // next reload uses the live `session.Version` so any version bumps that have
            // happened in the meantime (sibling apply, prior reload) count against each
            // token's pinned ceiling at that point.
            LogChangesApplied(_logger, workspaceId, session.Version, null);
        }
        else
        {
            LogChangesApplyFailed(_logger, workspaceId, null);
        }

        return result;
    }

    public void Dispose()
    {
        var shutdown = RequestStop();
        // Pending cleanup is owned by the same observed completion as DisposeAsync.
        // Completed cleanup can report its captured outcome without synchronously joining.
        if (shutdown.IsCompleted) _shutdownFailure?.Throw();
    }

    public ValueTask DisposeAsync() => new(RequestStop());

    private Task RequestStop()
    {
        TaskCompletionSource<Exception?> cancellationIssued;
        Task shutdown;
        lock (_lifecycleSync)
        {
            if (_shutdownTask is not null) return _shutdownTask;
            _disposing = true;
            cancellationIssued = new TaskCompletionSource<Exception?>();
            shutdown = ShutdownAsync(cancellationIssued.Task,
                _sourceOperationsDrained?.Task ?? Task.CompletedTask, _rootRetirementTasks.Values.ToArray());
            _shutdownTask = shutdown;
            // Observe failures even when the only caller uses deferred synchronous Dispose.
            // DisposeAsync still awaits and reports the original shared failure.
            _ = shutdown.ContinueWith(static completed => { _ = completed.Exception; },
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        Exception? cancellationFailure = null;
        try { _lifetimeCancellation.Cancel(); }
        catch (Exception exception) { cancellationFailure = exception; }
        // Cancellation callbacks can reenter Dispose. Shared ownership is already published;
        // teardown cannot dispose the CTS until the initiating Cancel call has returned.
        cancellationIssued.SetResult(cancellationFailure);
        return shutdown;
    }

    private async Task ShutdownAsync(Task<Exception?> cancellationIssued, Task sourceDrained, Task[] retirementTasks)
    {
        var cancellationFailure = await cancellationIssued.ConfigureAwait(false);
        var failures = new List<Exception>();
        if (cancellationFailure is not null) failures.Add(cancellationFailure);
        try { await sourceDrained.ConfigureAwait(false); }
        catch (Exception exception) { failures.Add(exception); }
        try { await Task.WhenAll(retirementTasks).ConfigureAwait(false); }
        catch (Exception exception) { failures.Add(exception); }

        Task removalsDrained;
        lock (_lifecycleSync)
        {
            _removalAdmissionClosed = true;
            removalsDrained = _slowRemovalsDrained?.Task ?? Task.CompletedTask;
        }
        await removalsDrained.ConfigureAwait(false);

        WorkspaceSession[] sessions;
        lock (_lifecycleSync)
        {
            sessions = _sessions.Values.ToArray();
            _sessions.Clear();
        }
        AttemptCleanup(() => _fileWatcher.WorkspaceRootMissing -= OnWorkspaceRootMissing, failures);
        foreach (var session in sessions)
            AttemptCleanup(() => RemoveSessionEffects(session, recordEviction: true), failures);
        if (_ownsFileWatcher) AttemptCleanup(_fileWatcher.Dispose, failures);
        AttemptCleanup(_workspaceSlots.Dispose, failures);
        AttemptCleanup(_lifetimeCancellation.Dispose, failures);
        if (failures.Count > 0)
        {
            AttemptCleanup(() => _logger.LogError(new AggregateException(failures.ToArray()),
                "Workspace manager shutdown cleanup failed"), failures);
            var failure = new AggregateException("Workspace manager shutdown failed.", failures);
            _shutdownFailure = ExceptionDispatchInfo.Capture(failure);
            throw failure;
        }
    }

    private static void StampCacheHitMetric(WorkspaceCacheWriteResult? result)
    {
        if (result is not null && AmbientGateMetrics.Current is { } metrics)
        {
            metrics.CacheHit = result.CacheHit;
        }
    }

    private Task LoadIntoSessionAsync(WorkspaceSession session, string path, CancellationToken ct)
        => LoadIntoSessionAsync(session, path, globalProperties: null, ct);

    private async Task LoadIntoSessionAsync(
        WorkspaceSession session,
        string path,
        IDictionary<string, string>? globalProperties,
        CancellationToken ct)
    {
        await session.LoadLock.WaitAsync(ct).ConfigureAwait(false);
        // autoreload-cascade-stdio-host-crash: build the new workspace + diagnostics queue into
        // locals, then swap them onto the session atomically on success. Previously we disposed
        // the old `session.Workspace` before creating the new one (and also overwrote
        // `session.WorkspaceDiagnostics` mid-flight). That left readers with a window where
        // `session.Workspace` pointed at a disposed MSBuildWorkspace — `.CurrentSolution` on a
        // disposed workspace throws `ObjectDisposedException`, and the 2026-04-24 two-writer +
        // reader cascade showed this path could terminate the stdio host. With the swap-on-
        // success pattern, readers always see EITHER the prior loaded workspace OR the fully
        // initialized new workspace; the disposed object is never observable.
        MSBuildWorkspace? newWorkspace = null;
        Helpers.AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease? newLease = null;
        var diagnosticsSink = new WorkspaceDiagnosticsSink(MaxDiagnosticsPerWorkspace);
        var oldWorkspace = session.Workspace;
        var oldLease = session.AnalyzerLease;
        var swapped = false;
        try
        {
            // Reload retains the evaluated session's globals, including the cache-probe policy.
            // Read them under LoadLock so a concurrent reload cannot replace this workspace.
            if (globalProperties is null && oldWorkspace is not null)
            {
                globalProperties = new Dictionary<string, string>(oldWorkspace.Properties, StringComparer.OrdinalIgnoreCase);
            }

            // workspace-load-uses-cache-fast-path: probe the persistent cache before the
            // restore-race wait so a warm-cache hit can elide it. Computing the solution-content
            // hash + sdk-version (the first two key components) is cheap (one file read + an
            // SHA-256). The third key component — the MSBuild graph hash — is only known after
            // OpenSolutionAsync, so the cache-key triple is finalized post-load and we look up
            // by (solution, sdk) here as a candidate-set probe. The probe is best-effort: any
            // failure leaves cachedCandidate null and the cold path runs unchanged.
            WorkspaceCacheProbe? cachedProbe = null;
            // Skip cache probe when caller passed global property overrides — different
            // Configuration / RuntimeIdentifier / etc. produce different MSBuild graph hashes
            // and the post-load comparison would invalidate any hit anyway. Probing under
            // overrides also risks short-circuiting the restore-race wait against a graph that
            // was captured under different globals.
            if (_cacheCoordinator is not null && globalProperties is not { Count: > 0 })
            {
                cachedProbe = await _cacheCoordinator.TryProbeAsync(path, ct).ConfigureAwait(false);
            }

            // dr-9-10-initial-does-not-wait-for-concurrent-to-finaliz: if a concurrent
            // out-of-process `dotnet restore` is mutating `obj/project.assets.json` or
            // `obj/*.dgspec.json` while we call OpenSolutionAsync, MSBuild latches onto an
            // in-flight assets file and Roslyn captures stale MetadataReference handles
            // (surfacing as CS1705 later, per the 2026-04-15 samplesolution experimental
            // promotion audit). Poll the relevant obj/ artefacts and wait for their mtimes
            // to stabilise before opening. Bounded by RestoreRaceWaitMs (default 2000 ms,
            // 0 disables entirely); no-op when no such files exist.
            //
            // workspace-load-uses-cache-fast-path: when a usable cache entry exists for this
            // (solution, sdk) pair AND every cached metadata-reference is still on disk with the
            // same mtime, the warm process already saw stable artefacts on the prior load — skip
            // the wait. Cache validation against the actual MSBuild graph happens after
            // OpenSolutionAsync (the cache key's third component requires the post-load graph),
            // so a probe-stage fast-path that is later invalidated by the post-load comparison
            // costs at most one missed restore-race wait — which is exactly the cost the wait is
            // designed to bound (max RestoreRaceWaitMs). Acceptable degradation.
            var skipRestoreRaceWait = cachedProbe is not null && cachedProbe.MetadataReferencesStillStable;
            if (!skipRestoreRaceWait)
            {
                await _restoreStalenessDetector.WaitForStableRestoreArtifactsAsync(path, _options.RestoreRaceWaitMs, _logger, ct).ConfigureAwait(false);
            }

            // workspace-manager-loadintosession-split: delegate MSBuild creation + global-
            // properties wiring + Open + analyzer-reference retargeting to the loader. The
            // loader self-disposes on failure before returning, so the manager only handles
            // disposal of post-loader-success state (strip-analyzers, project-statuses,
            // cache-write failures).
            (newWorkspace, newLease) = await _sessionLoader.CreateAndOpenAsync(
                session.WorkspaceId,
                path,
                globalProperties,
                diagnosticsSink,
                _logger,
                ct).ConfigureAwait(false);

            // unresolved-analyzer-reference-crash: strip UnresolvedAnalyzerReference entries
            // from every project before any downstream caller can see them. SymbolFinder,
            // Compilation.GetDiagnostics, and Roslyn-internal switches over AnalyzerReference
            // subtypes throw "Unexpected value 'UnresolvedAnalyzerReference'" otherwise. The
            // earlier per-service guards in CompilationCache/FixAllService are now unnecessary.
            await _analyzerReferenceStripper.StripAsync(session.WorkspaceId, newWorkspace, diagnosticsSink.Queue, MaxDiagnosticsPerWorkspace, _logger, ct).ConfigureAwait(false);

            EnsureWorkspacePathsArePhysical(newWorkspace.CurrentSolution);

            // workspace-load-uses-cache-fast-path: capture the post-load project graph + per-
            // project metadata-reference list for cache write-back. Building the graph first
            // (so the third cache-key component — graphHash — can be computed) lets a cache hit
            // be validated against the just-loaded snapshot, and a cache miss persist a fresh
            // entry. Best-effort: serialization failure or store-write failure must NOT abort
            // the load. Validating the cache means: (a) graph hash matches what we just observed,
            // and (b) metadata-reference timestamps still match disk. Either mismatch downgrades
            // the result to a cache miss and writes a fresh entry.
            var projectStatuses = BuildProjectStatuses(newWorkspace.CurrentSolution);
            var documentPaths = BuildDocumentPathIndex(newWorkspace.CurrentSolution);
            var restoreRequired = _restoreStalenessDetector.DetectRestoreRequired(projectStatuses, newWorkspace.Properties, _logger) ||
                                  RestoreStalenessDetector.HasRestoreRequiredWorkspaceDiagnostics(diagnosticsSink.Queue);
            // restore-required-vs-build-conflation: an unresolved-analyzer warning is a missing
            // BUILD output, distinct from a missing NuGet package. Track it separately so the
            // summary hint, readiness verdict, and autoRestore gate route to `dotnet build`
            // rather than a no-op `dotnet restore`.
            var buildRequired = WorkspaceDiagnosticClassifier.HasBuildRequired(diagnosticsSink.Queue);

            if (_cacheCoordinator is not null && cachedProbe is not null)
            {
                var result = await _cacheCoordinator.ResolveAndWriteAsync(newWorkspace.CurrentSolution, cachedProbe, ct)
                    .ConfigureAwait(false);
                StampCacheHitMetric(result);
            }
            else if (_cacheCoordinator is not null)
            {
                var result = await _cacheCoordinator.WriteFreshEntryAsync(newWorkspace.CurrentSolution, path, ct)
                    .ConfigureAwait(false);
                StampCacheHitMetric(result);
            }

            // autoreload-cascade-stdio-host-crash: atomic swap. Assign all session state AFTER
            // the new workspace is fully loaded so concurrent readers never observe a mid-reload
            // session (disposed workspace, empty project list, or mismatched LoadedPath).
            session.Workspace = newWorkspace;
            session.AnalyzerLease = newLease;
            session.WorkspaceDiagnostics = diagnosticsSink.Queue;
            session.ProjectStatuses = projectStatuses;
            session.DocumentPaths = documentPaths;
            session.LoadedPath = path;
            session.RestoreRequired = restoreRequired;
            session.BuildRequired = buildRequired;
            session.LoadedAtUtc = DateTimeOffset.UtcNow;
            session.IncrementVersion();
            // format-range-apply-preview-token-lifetime: drop only the tokens whose pinned
            // workspace-version range has been exceeded by this reload bump (default span = 1,
            // so a preview → single-reload → apply sequence inside the TTL window still
            // redeems). Replaces the prior unconditional `InvalidateAll` that surfaced as
            // "Preview token not found or expired" on every `*_apply` racing a watcher-driven
            // auto-reload. The captured Roslyn `Solution` snapshots are immutable graphs and
            // remain readable after the prior `MSBuildWorkspace` is disposed below — the apply
            // path's `RebaseModifiedSolutionOntoCurrentAsync` matches documents by file path,
            // so the post-reload `ProjectId`/`DocumentId` lineage divergence is handled.
            _previewStore.InvalidateOnVersionBump(session.WorkspaceId, session.Version);

            swapped = true;
            LogWorkspaceLoaded(_logger, session.WorkspaceId, path, session.Version, null);
        }
        finally
        {
            // workspace-native-load-cleanup-gate-release: every disposal step is attempted
            // independently (a throwing workspace must not skip its lease) and LoadLock is released
            // in a terminal finally that no disposal failure can bypass. The lock was acquired
            // above the try, so this finally only runs when this frame owns it. Cleanup failures
            // are logged, never thrown: on the failure branch a primary exception or cancellation
            // is propagating and must not be replaced; on the swap branch the reload is already
            // committed (state swapped, version bumped, previews invalidated) and must not be
            // reported as failed.
            var cleanupFailures = new List<Exception>();
            try
            {
                if (swapped)
                {
                    // Successful swap: oldWorkspace/oldLease were captured before the swap and are
                    // no longer referenced by the session. Safe to dispose — lease after workspace
                    // so Roslyn has released the old analyzer references first.
                    AttemptCleanup(() =>
                    {
                        // Null before disposing: this async frame's local would otherwise keep the
                        // old solution graph (and its loaded analyzer assemblies) rooted while the
                        // lease's GC-assisted delete retries run.
                        var retired = oldWorkspace;
                        oldWorkspace = null;
                        retired?.Dispose();
                    }, cleanupFailures);
                    AttemptCleanup(() => oldLease?.Dispose(), cleanupFailures);
                }
                else
                {
                    // Load failed mid-way after the loader returned: dispose the half-initialized
                    // new workspace (and its analyzer shadow lease) and leave the session's prior
                    // state untouched. Readers continue to see the previous valid workspace rather
                    // than a broken one (or null). If the loader itself threw, it already disposed
                    // both — `newWorkspace`/`newLease` are null here so this is a no-op — the
                    // autoreload-cascade invariant holds.
                    AttemptCleanup(() =>
                    {
                        var abandoned = newWorkspace;
                        newWorkspace = null;
                        abandoned?.Dispose();
                    }, cleanupFailures);
                    AttemptCleanup(() => newLease?.Dispose(), cleanupFailures);
                }
            }
            finally
            {
                session.LoadLock.Release();
            }

            if (cleanupFailures.Count > 0)
            {
                _logger.LogError(
                    new AggregateException(cleanupFailures),
                    swapped
                        ? "Disposing the retired workspace resources of reloaded workspace '{WorkspaceId}' failed; the reload is committed"
                        : "Disposing the half-loaded workspace resources of workspace '{WorkspaceId}' failed; the original load failure is preserved",
                    session.WorkspaceId);
            }
        }
    }

    private WorkspaceStatusDto BuildStatus(WorkspaceSession session)
    {
        var isStale = _fileWatcher.IsStale(session.WorkspaceId);
        // workspace-stale-after-external-edit-feedback: emit the reason alongside the flag so
        // callers can distinguish a self-apply from a genuine external edit. Serialization is
        // gated on WhenWritingNull, so a fresh workspace doesn't pay a byte.
        var staleReason = isStale ? _fileWatcher.GetStaleReason(session.WorkspaceId) : null;

        if (session.Workspace is null || session.LoadedPath is null)
        {
            return new WorkspaceStatusDto(
                WorkspaceId: session.WorkspaceId,
                LoadedPath: null,
                WorkspaceVersion: session.Version,
                SnapshotToken: $"{session.WorkspaceId}:{session.Version}",
                LoadedAtUtc: session.LoadedAtUtc,
                ProjectCount: 0,
                DocumentCount: 0,
                Projects: [],
                IsLoaded: false,
                IsStale: isStale,
                WorkspaceDiagnostics: session.WorkspaceDiagnostics.ToArray(),
                StaleReason: staleReason,
                RestoreRequired: session.RestoreRequired,
                BuildRequired: session.BuildRequired);
        }

        var projects = session.ProjectStatuses;

        return new WorkspaceStatusDto(
            WorkspaceId: session.WorkspaceId,
            LoadedPath: session.LoadedPath,
            WorkspaceVersion: session.Version,
            SnapshotToken: $"{session.WorkspaceId}:{session.Version}",
            LoadedAtUtc: session.LoadedAtUtc,
            ProjectCount: projects.Length,
            DocumentCount: projects.Sum(project => project.DocumentCount),
            Projects: projects,
            IsLoaded: true,
            IsStale: isStale,
            WorkspaceDiagnostics: session.WorkspaceDiagnostics.ToArray(),
            StaleReason: staleReason,
            RestoreRequired: session.RestoreRequired,
            BuildRequired: session.BuildRequired);
    }

    /// <inheritdoc />
    public WorkspaceNotFoundException CreateWorkspaceNotFoundException(string workspaceId) =>
        new(workspaceId,
            $"Workspace '{workspaceId}' was not found. " +
            $"There are {_sessions.Count} active session(s). " +
            "The session may have been lost due to a server restart or process exit. " +
            "Use workspace_list to see active sessions, then workspace_load to create a new one.");

    private WorkspaceSession GetRequiredSession(string workspaceId)
    {
        if (!_sessions.TryGetValue(workspaceId, out var session))
        {
            var activeCount = _sessions.Count;
            var activeIds = string.Join(", ", _sessions.Keys.Take(5));
            LogSessionNotFound(_logger, workspaceId, activeCount, activeIds, null);

            // mcp-error-category-workspace-evicted-on-host-recycle: distinguish a typo'd
            // workspaceId from a workspace that was evicted by an explicit Close, by host
            // disposal, or by a graceful prior-process recycle. The structural signal lets
            // callers branch on category="WorkspaceEvicted" and recover via workspace_load
            // (rehydrate the prior solution) instead of double-checking the id they sent.
            //
            // Detection precedence:
            //   1. In-process eviction record — exact match on workspaceId. Carries the
            //      original loadedAt so the envelope can surface both timestamps.
            //   2. Cross-process recycle signal — Program.cs publishes the prior process's
            //      recycle reason via WorkspaceEvictionRegistry. When the registry reports
            //      a recycle AND this manager has zero live sessions (the prior process
            //      owned the missing id), surface as WorkspaceEvicted with no loadedAt
            //      (the prior loadedAt was lost with the prior process).
            //   3. Otherwise the lookup is genuinely a typo or a never-loaded id — fall
            //      through to the existing KeyNotFoundException path.
            if (_evictedWorkspaces.TryGetValue(workspaceId, out var evictedRecord))
            {
                throw new WorkspaceEvictedException(
                    workspaceId,
                    WorkspaceEvictionRegistry.ServerStartedAtUtc,
                    evictedRecord.LoadedAtUtc,
                    evictedRecord.LoadedPath,
                    $"Workspace '{workspaceId}' was evicted from the live session set " +
                    $"(originally loaded at {evictedRecord.LoadedAtUtc:O} from '{evictedRecord.LoadedPath}'). " +
                    $"Call workspace_load(path: \"{evictedRecord.LoadedPath}\") to rehydrate, " +
                    "then re-issue this call against the new workspaceId.");
            }

            if (WorkspaceEvictionRegistry.WasHostRecycled && activeCount == 0)
            {
                throw new WorkspaceEvictedException(
                    workspaceId,
                    WorkspaceEvictionRegistry.ServerStartedAtUtc,
                    $"Workspace '{workspaceId}' belongs to a prior host process that exited " +
                    $"(reason: {WorkspaceEvictionRegistry.PreviousRecycleReason ?? "unknown"}). " +
                    $"The current process started at {WorkspaceEvictionRegistry.ServerStartedAtUtc:O} " +
                    "and has no live sessions. Call workspace_load with the original solution path " +
                    "to rehydrate, then re-issue this call against the new workspaceId.");
            }

            throw CreateWorkspaceNotFoundException(workspaceId);
        }

        session.TouchAccess();

        // Log if session has been idle for a long time (informational)
        var idleMinutes = (DateTimeOffset.UtcNow - session.LastAccessedUtc).TotalMinutes;
        if (idleMinutes > 60 && session.Workspace is not null)
        {
            _logger.LogDebug(
                "Workspace '{WorkspaceId}' has been idle for {Minutes:F0} minutes (loaded: {Path})",
                workspaceId, idleMinutes, session.LoadedPath);
        }

        return session;
    }

    /// <summary>
    /// Checks whether a workspace session exists without throwing.
    /// </summary>
    public bool HasSession(string workspaceId) => _sessions.ContainsKey(workspaceId);

    private static string ValidateWorkspacePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new PublicArgumentException(WorkspaceSessionLoader.SupportedPathCorrection, nameof(path));
        }

        var fullPath = PhysicalPathResolver.Resolve(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Workspace path was not found: {fullPath}", fullPath);
        }

        if (!fullPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) &&
            !fullPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase) &&
            !fullPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            throw new PublicArgumentException(WorkspaceSessionLoader.SupportedPathCorrection, nameof(path));
        }

        return fullPath;
    }

    private static void EnsureWorkspacePathsArePhysical(Solution solution)
    {
        foreach (var project in solution.Projects)
        {
            EnsurePhysicalPath(project.FilePath);
            var projectDirectory = Path.GetDirectoryName(project.FilePath);
            foreach (var document in project.Documents
                         .Concat<TextDocument>(project.AdditionalDocuments)
                         .Concat(project.AnalyzerConfigDocuments))
            {
                if (string.IsNullOrWhiteSpace(document.FilePath)) continue;

                var physicalPath = PhysicalPathResolver.Resolve(document.FilePath);
                // MSBuild imports some source documents from outside the project (for example,
                // SDK files in the NuGet cache). Those paths are read-only in this workspace:
                // TryApplyChanges rejects edits to any document whose path is not physical.
                if (IsPathUnderDirectory(physicalPath, projectDirectory))
                {
                    EnsurePhysicalPath(document.FilePath);
                }
            }
        }
    }

    internal static void EnsureChangedDocumentPathsArePhysical(Solution current, Solution next)
    {
        foreach (var project in next.Projects)
        {
            EnsurePhysicalPath(project.FilePath);
        }

        var changes = next.GetChanges(current);
        foreach (var project in changes.GetAddedProjects().Concat(changes.GetRemovedProjects()))
        {
            foreach (var document in project.Documents
                         .Concat<TextDocument>(project.AdditionalDocuments)
                         .Concat(project.AnalyzerConfigDocuments))
            {
                EnsurePhysicalPath(document.FilePath);
            }
        }

        foreach (var change in changes.GetProjectChanges())
        {
            foreach (var id in change.GetChangedDocuments()
                         .Concat(change.GetAddedDocuments())
                         .Concat(change.GetRemovedDocuments()))
            {
                EnsurePhysicalPath(current.GetDocument(id)?.FilePath);
                EnsurePhysicalPath(next.GetDocument(id)?.FilePath);
            }

            foreach (var id in change.GetChangedAdditionalDocuments()
                         .Concat(change.GetAddedAdditionalDocuments())
                         .Concat(change.GetRemovedAdditionalDocuments()))
            {
                EnsurePhysicalPath(current.GetAdditionalDocument(id)?.FilePath);
                EnsurePhysicalPath(next.GetAdditionalDocument(id)?.FilePath);
            }

            foreach (var id in change.GetChangedAnalyzerConfigDocuments()
                         .Concat(change.GetAddedAnalyzerConfigDocuments())
                         .Concat(change.GetRemovedAnalyzerConfigDocuments()))
            {
                EnsurePhysicalPath(current.GetAnalyzerConfigDocument(id)?.FilePath);
                EnsurePhysicalPath(next.GetAnalyzerConfigDocument(id)?.FilePath);
            }
        }
    }

    internal static void EnsurePhysicalPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        var physicalPath = PhysicalPathResolver.Resolve(path);
        if (FileSystemPath.Comparer.Equals(path, physicalPath)) return;

        throw new InvalidOperationException(
            "Workspace project or modified document path has a filesystem-link component. " +
            "Load and edit through the physical path.");
    }

    private static bool IsPathUnderDirectory(string path, string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return false;
        var prefix = directory.EndsWith(Path.DirectorySeparatorChar)
            ? directory
            : directory + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, FileSystemPath.Comparison);
    }

    private static ImmutableHashSet<string> BuildDocumentPathIndex(Solution solution) =>
        solution.Projects
            .SelectMany(project => project.Documents)
            .Select(document => document.FilePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => PhysicalPathResolver.Resolve(path!))
            .ToImmutableHashSet(FileSystemPath.Comparer);

    private ImmutableArray<ProjectStatusDto> BuildProjectStatuses(Solution solution)
    {
        return solution.Projects.Select(project =>
        {
            var projectDoc = Helpers.ProjectMetadataParser.LoadProjectDocument(project.FilePath, _logger);
            return new ProjectStatusDto(
                // project-graph-missing-metadata-fields: never emit an empty Name / FilePath
                // into the status DTO. ResolveProjectName falls back through AssemblyName →
                // filename stem → "unknown"; ResolveProjectPath falls back to "unknown".
                Name: Helpers.ProjectMetadataParser.ResolveProjectName(project),
                FilePath: Helpers.ProjectMetadataParser.ResolveProjectPath(project),
                DocumentCount: project.Documents.Count(),
                ProjectReferences: project.ProjectReferences
                    .Select(reference =>
                    {
                        var referenced = solution.GetProject(reference.ProjectId);
                        return referenced is null
                            ? "unknown"
                            : Helpers.ProjectMetadataParser.ResolveProjectName(referenced);
                    })
                    .ToList(),
                TargetFrameworks: Helpers.ProjectMetadataParser.GetTargetFrameworks(project, projectDoc, _logger),
                IsTestProject: Helpers.ProjectMetadataParser.IsTestProject(projectDoc),
                AssemblyName: Helpers.ProjectMetadataParser.GetAssemblyName(project),
                OutputType: Helpers.ProjectMetadataParser.GetOutputType(project, projectDoc, _logger));
        }).ToImmutableArray();
    }

    private sealed class WorkspaceSession(string workspaceId) : IDisposable
    {
        private int _version;

        public string WorkspaceId { get; } = workspaceId;
        public SemaphoreSlim LoadLock { get; } = new(1, 1);
        public ConcurrentQueue<DiagnosticDto> WorkspaceDiagnostics { get; set; } = new();
        public ImmutableArray<ProjectStatusDto> ProjectStatuses { get; set; } = ImmutableArray<ProjectStatusDto>.Empty;
        public ImmutableHashSet<string> DocumentPaths { get; set; } = ImmutableHashSet.Create<string>(FileSystemPath.Comparer);
        public MSBuildWorkspace? Workspace { get; set; }

        /// <summary>
        /// analyzer-shadow-loader-lifecycle: owns the per-load analyzer shadow-copy loaders'
        /// collectible load contexts + on-disk shadow root. Swapped atomically alongside
        /// <see cref="Workspace"/> and released exactly once — by <see cref="Dispose"/> (close,
        /// eviction, host shutdown) or by the reload swap's old-lease disposal.
        /// </summary>
        public Helpers.AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease? AnalyzerLease { get; set; }

        public string? LoadedPath { get; set; }
        public bool RestoreRequired { get; set; }
        public bool BuildRequired { get; set; }
        public DateTimeOffset LoadedAtUtc { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset LastAccessedUtc { get; set; } = DateTimeOffset.UtcNow;
        public int Version => Volatile.Read(ref _version);
        public int IncrementVersion() => Interlocked.Increment(ref _version);
        public void SetVersion(int value) => Interlocked.Exchange(ref _version, value);

        public void TouchAccess() => LastAccessedUtc = DateTimeOffset.UtcNow;

        public void Dispose()
        {
            Workspace?.Dispose();
            // Lease after workspace so Roslyn has released the analyzer references before the
            // load contexts are unloaded and the shadow root is deleted; the Workspace field is
            // nulled first so this frame's session reference does not keep the solution graph
            // (and through it the loaded analyzer assemblies) rooted while the lease's
            // GC-assisted delete retries run. Lease disposal is idempotent, so the
            // eviction/close/host-shutdown paths can all reach here safely.
            Workspace = null;
            AnalyzerLease?.Dispose();
            LoadLock.Dispose();
        }
    }
}
