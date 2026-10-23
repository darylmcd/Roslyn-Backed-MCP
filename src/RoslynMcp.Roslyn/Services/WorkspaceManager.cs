using System.Collections.Concurrent;
using System.Collections.Immutable;
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
public sealed partial class WorkspaceManager : IWorkspaceManager, IPhysicalSolutionCommitter, IWorkspaceEvaluationLoader, IWorkspaceRecoveryAuthority, IDisposable
{
    private const int MaxDiagnosticsPerWorkspace = 200;

    private static readonly Action<ILogger, string, string, int, Exception?> LogWorkspaceLoaded =
        LoggerMessage.Define<string, string, int>(
            LogLevel.Information, new EventId(1, nameof(LogWorkspaceLoaded)),
            "Loaded workspace {WorkspaceId} from {Path}, version {Version}");

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
    private readonly ForkInvocationPathPolicy _pathPolicy;
    /// <summary>Evaluates restore staleness independently from workspace publication.</summary>
    private readonly RestoreStalenessDetector _restoreStalenessDetector;
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
    private readonly ConcurrentDictionary<string, PhysicalMutationRecovery> _recoveries = new(StringComparer.Ordinal);
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
    private readonly object _rootRetirementSync = new();
    private readonly Dictionary<string, Task> _rootRetirementTasks = new(StringComparer.Ordinal);
    private bool _disposing;

    /// <inheritdoc />
    public event Action<string>? WorkspaceClosed;

    /// <inheritdoc />
    public event Action<string>? WorkspaceReloaded;

    public WorkspaceManager(
        ForkInvocationPathPolicy pathPolicy,
        ILogger<WorkspaceManager> logger,
        IPreviewStore previewStore,
        IFileWatcherService fileWatcher,
        WorkspaceManagerOptions? options = null,
        IWorkspaceCacheStore? cacheStore = null,
        Lazy<IWorkspaceExecutionGate>? evictionGate = null)
        : this(
            pathPolicy,
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
        ForkInvocationPathPolicy pathPolicy,
        ILogger<WorkspaceManager> logger,
        IPreviewStore previewStore,
        IFileWatcherService fileWatcher,
        WorkspaceManagerOptions? options,
        IWorkspaceCacheStore? cacheStore,
        Lazy<IWorkspaceExecutionGate>? evictionGate) =>
        new(
            pathPolicy,
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
        ForkInvocationPathPolicy pathPolicy,
        ILogger<WorkspaceManager> logger,
        IPreviewStore previewStore,
        IFileWatcherService fileWatcher,
        WorkspaceManagerOptions? options,
        IWorkspaceCacheStore? cacheStore,
        WorkspaceSessionLoader? sessionLoader,
        RestoreStalenessDetector? restoreStalenessDetector = null,
        Lazy<IWorkspaceExecutionGate>? evictionGate = null,
        bool ownsFileWatcher = true,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(pathPolicy);
        _pathPolicy = pathPolicy;
        _logger = logger;
        _previewStore = previewStore;
        _fileWatcher = fileWatcher;
        _ownsFileWatcher = ownsFileWatcher;
        _options = options ?? new WorkspaceManagerOptions();
        _cacheCoordinator = cacheStore is null ? null : new WorkspaceCacheCoordinator(cacheStore, logger);
        _sessionLoader = sessionLoader ?? new WorkspaceSessionLoader();
        _restoreStalenessDetector = restoreStalenessDetector ?? new RestoreStalenessDetector();
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

    public Task<WorkspaceStatusDto> LoadAsync(
        string path,
        EvictPolicy evictPolicy,
        IDictionary<string, string>? globalProperties,
        CancellationToken ct)
        => LoadCoreAsync(path, evictPolicy, globalProperties, SupplementalPackageReadAuthority.None, null, ct);

    private async Task<WorkspaceStatusDto> LoadCoreAsync(
        string path,
        EvictPolicy evictPolicy,
        IDictionary<string, string>? globalProperties,
        SupplementalPackageReadAuthority supplementalPackages,
        ForkInvocationAuthority? invocation,
        CancellationToken ct)
    {
        var forkInvocation = RequireInvocation(invocation);
        forkInvocation?.RequireSupplemental(supplementalPackages);
        var fullPath = forkInvocation is null ? ValidateWorkspacePath(path) : ValidateWorkspacePathLexically(path);
        if (_recoveries.Values.Any(recovery => FileSystemPath.Comparer.Equals(recovery.PreApplySolution.FilePath, fullPath)
            || recovery.PreApplySolution.Projects.Any(project => FileSystemPath.Comparer.Equals(project.FilePath, fullPath))))
            throw new PublicInvalidOperationException("Recover the failed physical operation before loading another evaluation of its source graph.");

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
        var skipDedup = forkInvocation is not null || globalProperties is { Count: > 0 }
            || !ReferenceEquals(supplementalPackages, SupplementalPackageReadAuthority.None);
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
                // parallel-mode-workspace-cap-lru-or-raise: LRU eviction path.
                // Pick the session with the smallest LastAccessedUtc, skipping any that
                // currently hold their LoadLock. LoadLock is acquired ONLY by
                // LoadIntoSessionAsync, so this filter excludes exactly the sessions that are
                // mid-workspace_load / mid-reload — and nothing else.
                //
                // lru-eviction-gate-layer-execution: the LoadLock filter alone does NOT exclude a
                // session with in-flight gated reads or writes, so candidate SELECTION is not a
                // safety boundary. Eviction EXECUTION supplies it: EvictForCapAsync runs Close()
                // under WorkspaceExecutionGate's per-workspace WRITER lock (see that method), the
                // same nesting workspace_close / workspace_reload use (WorkspaceTools.cs), so
                // in-flight readers/writers drain before the session is removed and disposed.
                // Pinned by
                // WorkspaceCapLruEvictionTests.LruEviction_WhileGatedReadInFlight_BlocksUntilReaderDrains.
                //
                // If all sessions are mid-load, fall through to the Strict error path.
                var lruCandidate = _sessions.Values
                    .Where(s => s.LoadLock.CurrentCount > 0 && !_recoveries.ContainsKey(s.WorkspaceId)
                        && s.WorkspaceId != forkInvocation?.SourceWorkspaceId && !supplementalPackages.ProtectsSource(s.WorkspaceId))
                    .OrderBy(s => s.LastAccessedUtc)
                    .FirstOrDefault();

                if (lruCandidate is not null)
                {
                    _logger.LogInformation(
                        "workspace_load: LRU eviction of workspace '{WorkspaceId}' (lastAccessed={LastAccessedUtc:O}) to make room for '{Path}'",
                        lruCandidate.WorkspaceId,
                        lruCandidate.LastAccessedUtc,
                        fullPath);
                    await EvictForCapAsync(lruCandidate.WorkspaceId, ct).ConfigureAwait(false);

                    // The eviction released a semaphore slot (or a concurrent caller already
                    // evicted this candidate and released it) — retry immediately.
                    if (!await _workspaceSlots.WaitAsync(0, ct).ConfigureAwait(false))
                    {
                        // The slot was consumed by a concurrent caller between the Close() and
                        // our retry. Fall through to the Strict error path.
                        goto strictError;
                    }
                    goto slotAcquired;
                }
            }

        strictError:
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
        var sessionAdded = false;
        var loadSucceeded = false;
        Exception? loadFailure = null;
        try
        {
            if (forkInvocation is not null)
            {
                forkInvocation.BindAllocated(session, fullPath, globalProperties);
                await _pathPolicy.ValidateAsync(workspaceId, [fullPath], ct).ConfigureAwait(false);
                var physical = ValidateWorkspacePath(fullPath);
                if (!FileSystemPath.Comparer.Equals(physical, fullPath))
                    throw new WorkspaceMutationOwnershipException("The fork entry point changed its physical identity.");
            }
            await LoadIntoSessionAsync(session, fullPath, globalProperties, expected: null, preservePreviews: false,
                supplementalPackages, invocation, ct).ConfigureAwait(false);

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
                    raceWinner.TouchAccess();
                    return BuildStatus(raceWinner);
                }
            }

            if (!_sessions.TryAdd(workspaceId, session))
            {
                throw new InvalidOperationException($"Workspace '{workspaceId}' already exists.");
            }

            sessionAdded = true;
            forkInvocation?.Registered(session);
            _fileWatcher.Watch(workspaceId, session.LoadedPath!);
            if (forkInvocation is not null) await forkInvocation.AssertCurrentAsync(ct).ConfigureAwait(false);
            var published = session.Capture()?.Solution
                ?? throw InvalidOperationErrors.Internal("The loaded workspace has no published native snapshot.");
            if (!NativeCompilerContextCertificate.TryGet(published, out var publishedContext) || publishedContext is null)
                throw InvalidOperationErrors.Internal("The loaded workspace has no owned load-freshness evidence.");
            await publishedContext.AssertLoadCurrentAsync(published, workspaceId, _pathPolicy, supplementalPackages, null, ct).ConfigureAwait(false);
            forkInvocation?.AssertCurrent();
            var status = BuildStatus(session);
            loadSucceeded = true;
            return status;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
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

            loadFailure = ex;
            throw;
        }
        catch (Exception ex)
        {
            loadFailure = ex;
            throw;
        }
        finally
        {
            if (!loadSucceeded)
            {
                var ownsRegistration = sessionAdded && _sessions.TryRemove(new KeyValuePair<string, WorkspaceSession>(workspaceId, session));
                if (!sessionAdded || ownsRegistration)
                {
                    try
                    {
                        OwnedResourceCleanup.DisposePreservingFailure("Failed to release an unpublished workspace session.", loadFailure,
                            OwnedResourceCleanup.FromAction(() =>
                            {
                                if (!ownsRegistration) return;
                                lock (_mutationLock) _mutationEpochs.Remove(session);
                                _fileWatcher.Unwatch(workspaceId);
                            }), session);
                    }
                    finally { _workspaceSlots.Release(); }
                }
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
    private async Task EvictForCapAsync(string workspaceId, CancellationToken ct)
    {
        if (_evictionGate is null)
        {
            Close(workspaceId);
            return;
        }

        var gate = _evictionGate.Value;
        try
        {
            await gate.RunWriteAsync(
                workspaceId,
                _ =>
                {
                    Close(workspaceId);
                    return Task.FromResult(true);
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
        catch (WorkspaceCloseCleanupException failure) when (failure.IsFor(this, workspaceId))
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure.RemoveGatePreservingFailure(gate)).Throw();
            throw;
        }

        gate.RemoveGate(workspaceId);
    }

    public Task<WorkspaceStatusDto> ReloadAsync(string workspaceId, CancellationToken ct)
        => ReloadCoreAsync(workspaceId, null, ct);

    Task<WorkspaceStatusDto> IWorkspaceEvaluationLoader.ReloadWithInvocationAuthorityAsync(string workspaceId,
        ForkInvocationAuthority invocation, CancellationToken ct) => ReloadCoreAsync(workspaceId, invocation, ct);

    private async Task<WorkspaceStatusDto> ReloadCoreAsync(string workspaceId, ForkInvocationAuthority? invocation, CancellationToken ct)
    {
        RequireInvocation(invocation);
        EnsureMutationAllowed(workspaceId, CurrentPhysicalPaths(workspaceId));
        var session = GetRequiredSession(workspaceId);
        await LoadIntoSessionAsync(session, session.LoadedPath ?? throw new InvalidOperationException(
            $"Workspace '{workspaceId}' is not loaded."),
            invocation, ct).ConfigureAwait(false);
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

    Task<WorkspaceStatusDto> IWorkspaceEvaluationLoader.LoadWithPropertiesAsync(string path,
        IDictionary<string, string> properties, SupplementalPackageReadAuthority supplementalPackages, ForkInvocationAuthority? invocation, CancellationToken ct) =>
        LoadCoreAsync(path, EvictPolicy.Lru, properties, supplementalPackages, invocation, ct);

    Task IPhysicalSolutionCommitter.CommitPhysicalSolutionAsync(string workspaceId, Solution? expected, CancellationToken ct)
    {
        EnsureMutationAllowed(workspaceId, expected is null ? CurrentPhysicalPaths(workspaceId)
            : SolutionFileMutationPlanner.ChangedPaths(GetCurrentSolution(workspaceId), expected));
        return CommitPhysicalSolutionCoreAsync(workspaceId, expected, recovery: null, ct);
    }

    private async Task CommitPhysicalSolutionCoreAsync(string workspaceId, Solution? expected, PhysicalMutationRecovery? recovery, CancellationToken ct)
    {
        if (expected is null)
        {
            await CommitPhysicalGraphCoreAsync(workspaceId, null, recovery, ct).ConfigureAwait(false);
            return;
        }
        var session = GetRequiredSession(workspaceId);
        var layout = await PhysicalGraphLayout.CreateAsync(workspaceId, session.LoadedPath
            ?? throw InvalidOperationErrors.Internal("Physical publication requires a loaded entry point."), expected, _pathPolicy, ct).ConfigureAwait(false);
        await _pathPolicy.ValidateAsync(workspaceId, layout.Packages.PathsRequiringCurrentGrant(layout.InputFiles), ct).ConfigureAwait(false);
        var identities = System.Collections.Immutable.ImmutableArray.CreateBuilder<PhysicalInputIdentity>();
        foreach (var path in layout.InputFiles.Where(path => !layout.ConfigurationInputs.Any(input => FileSystemPath.Comparer.Equals(input.FilePath, path))
            && !layout.ConditionInputs.Any(input => FileSystemPath.Comparer.Equals(input.FilePath, path))))
            identities.Add(await PhysicalInputIdentity.CaptureAsync(path, ct).ConfigureAwait(false));
        identities.AddRange(layout.ConfigurationInputs);
        identities.AddRange(layout.ConditionInputs);
        identities.AddRange(layout.Packages.Inputs);
        var properties = BuildPathRelocation.KnownPathProperties;
        var expectation = new PhysicalGraphExpectation(expected, static path => path, properties,
            EvaluatedExecutionIdentity.Capture(expected, static path => path, properties, ct),
            PhysicalInputIdentity.MergeConsistent(identities), layout.Packages, layout.SdkInputs);
        await CommitPhysicalGraphCoreAsync(workspaceId, expectation, recovery, ct).ConfigureAwait(false);
    }

    private async Task CommitPhysicalGraphCoreAsync(string workspaceId, PhysicalGraphExpectation? expected, PhysicalMutationRecovery? recovery, CancellationToken ct)
    {
        lock (_mutationLock)
        {
            if (recovery?.HasRetainedSourceTransitions == true ||
                _activeMutations.TryGetValue(workspaceId, out var active) && active.HasRetainedSourceTransitions)
                throw new WorkspaceMutationOwnershipException("Source transition custody must be resolved before publishing the physical workspace.");
            if (expected is null && (recovery?.HasUnverifiedSourceNamespaces == true ||
                _activeMutations.TryGetValue(workspaceId, out var changed) && changed.HasUnverifiedSourceNamespaces))
                throw new WorkspaceMutationOwnershipException("Source namespace changes require an independently prepared graph expectation.");
        }
        var session = GetRequiredSession(workspaceId);
        var path = session.LoadedPath ?? throw InvalidOperationErrors.Internal("The physical workspace has no loaded path.");
        var properties = session.Workspace?.Properties.ToDictionary(pair => pair.Key, pair => pair.Value);
        await LoadIntoSessionAsync(session, path, properties, expected, preservePreviews: true,
            SupplementalPackageReadAuthority.None, null, ct).ConfigureAwait(false);
        lock (_mutationLock)
        {
            recovery?.SourceNamespacesVerified();
            if (_activeMutations.TryGetValue(workspaceId, out var completed)) completed.SourceNamespacesVerified();
        }
        if (recovery is not null) _recoveries.TryRemove(new KeyValuePair<string, PhysicalMutationRecovery>(workspaceId, recovery));
        _fileWatcher.ClearStale(workspaceId);
        RaiseWorkspaceReloaded(workspaceId);
    }

    public bool Close(string workspaceId)
    {
        EnsureMutationAllowed(workspaceId, []);
        return CloseCore(workspaceId, recordEviction: true);
    }

    private bool CloseCore(string workspaceId, bool recordEviction)
    {
        if (_recoveries.ContainsKey(workspaceId)) return false;
        if (!_sessions.TryRemove(workspaceId, out var session))
        {
            return false;
        }

        // mcp-error-category-workspace-evicted-on-host-recycle + workspace-id-recovery-hints:
        // record the original loadedAt and loadedPath before the session is disposed so a
        // subsequent lookup against this id can surface the structured
        // WorkspaceEvictedException with the timestamp AND the rehydration path populated.
        try
        {
            OwnedResourceCleanup.Dispose("Workspace session cleanup failed.", null,
                OwnedResourceCleanup.FromAction(() => { lock (_mutationLock) _mutationEpochs.Remove(session); }),
                OwnedResourceCleanup.FromAction(() =>
                {
                    if (recordEviction) RecordEviction(workspaceId, session.LoadedAtUtc, session.LoadedPath);
                }),
                OwnedResourceCleanup.FromAction(() => _fileWatcher.Unwatch(workspaceId)),
                OwnedResourceCleanup.FromAction(() => _previewStore.InvalidateAll(workspaceId)),
                session,
                OwnedResourceCleanup.FromAction(() => _workspaceSlots.Release()),
                OwnedResourceCleanup.FromAction(() => RaiseWorkspaceClosed(workspaceId)),
                OwnedResourceCleanup.FromAction(() => LogWorkspaceClosed(_logger, workspaceId, null)));
        }
        catch (AggregateException failure)
        {
            throw new WorkspaceCloseCleanupException(this, workspaceId, failure.InnerExceptions);
        }
        return true;
    }

    private void OnWorkspaceRootMissing(string workspaceId)
    {
        lock (_rootRetirementSync)
        {
            if (_disposing || _rootRetirementTasks.ContainsKey(workspaceId))
            {
                return;
            }

            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var task = Task.Run(async () =>
            {
                await start.Task.ConfigureAwait(false);
                await RetireMissingWorkspaceAsync(workspaceId).ConfigureAwait(false);
            });
            _rootRetirementTasks.Add(workspaceId, task);
            start.SetResult();
        }
    }

    private async Task RetireMissingWorkspaceAsync(string workspaceId)
    {
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
                    try { CloseCore(workspaceId, recordEviction: false); }
                    catch (WorkspaceCloseCleanupException failure) when (failure.IsFor(this, workspaceId))
                    {
                        _logger.LogWarning(failure,
                            "Retired workspace {WorkspaceId}, but resource cleanup failed", workspaceId);
                    }
                    return;
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
                                      CloseCore(workspaceId, recordEviction: false);
                            return Task.FromResult(retired);
                        },
                        _lifetimeCancellation.Token,
                        applyStalenessPolicy: false).ConfigureAwait(false);
                }
                catch (KeyNotFoundException)
                {
                    return;
                }
                catch (WorkspaceCloseCleanupException failure) when (failure.IsFor(this, workspaceId))
                {
                    _logger.LogWarning(failure.RemoveGatePreservingFailure(gate),
                        "Retired workspace {WorkspaceId}, but resource cleanup failed", workspaceId);
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
                        // not fault this manager-owned task (or make Dispose throw while joining
                        // it), but it remains operationally visible for diagnosis.
                        _logger.LogWarning(
                            exception,
                            "Retired workspace {WorkspaceId}, but failed to remove its execution gate",
                            workspaceId);
                    }
                }

                return;
            }
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            lock (_rootRetirementSync)
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
        EnsureMutationAllowed(workspaceId, []);
        var session = GetRequiredSession(workspaceId);
        session.SetVersion(version);
        AdvanceMutationEpoch(session);
    }

    public Solution GetCurrentSolution(string workspaceId)
    {
        var session = GetRequiredSession(workspaceId);
        var loaded = session.Capture();
        if (loaded is null)
            throw new StaleWorkspaceTransitionException(workspaceId,
                "The workspace has no published loaded graph. Retry after workspace_reload settles.");
        // The immutable snapshot remains readable if the native workspace is disposed after
        // this capture. Never combine a workspace and solution from different publications.
        return loaded.Solution;
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
        var solution = session.Capture()?.Solution;
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

    public void Dispose()
    {
        Task[] retirementTasks;
        lock (_rootRetirementSync)
        {
            if (_disposing)
            {
                return;
            }

            _disposing = true;
            retirementTasks = _rootRetirementTasks.Values.ToArray();
        }

        // Join lifecycle-owned retirement work before releasing sessions and capacity.
        // Synchronous Dispose from a retirement notification requires separate reentrancy
        // qualification; tracked by workspace-retirement-dispose-reentrancy.
        OwnedResourceCleanup.Dispose("Workspace manager shutdown cleanup failed.", null,
            OwnedResourceCleanup.FromAction(() => _fileWatcher.WorkspaceRootMissing -= OnWorkspaceRootMissing),
            OwnedResourceCleanup.FromAction(() => _lifetimeCancellation.Cancel()),
            OwnedResourceCleanup.FromAction(() => Task.WhenAll(retirementTasks).GetAwaiter().GetResult()),
            _ownsFileWatcher ? _fileWatcher : null,
            OwnedResourceCleanup.FromAction(() =>
            {
                var sessions = _sessions.ToArray();
                OwnedResourceCleanup.Dispose("Workspace shutdown session cleanup failed.", null,
                    sessions.Select(pair => OwnedResourceCleanup.FromAction(() =>
                    {
                        if (!_sessions.TryRemove(pair)) return;
                        var session = pair.Value;
                        OwnedResourceCleanup.Dispose("Workspace shutdown resource cleanup failed.", null,
                            OwnedResourceCleanup.FromAction(() => RecordEviction(pair.Key, session.LoadedAtUtc, session.LoadedPath)),
                            session,
                            OwnedResourceCleanup.FromAction(() => _workspaceSlots.Release()),
                            OwnedResourceCleanup.FromAction(() => RaiseWorkspaceClosed(pair.Key)));
                    })).ToArray());
            }),
            OwnedResourceCleanup.FromAction(() =>
            {
                lock (_mutationLock)
                {
                    var sourceOperations = _recoveries.Values.Concat(_activeMutations.Values).Distinct().ToArray();
                    try
                    {
                        OwnedResourceCleanup.Dispose("Workspace source custody shutdown failed.", null,
                            sourceOperations.Select(operation => OwnedResourceCleanup.FromAction(operation.ReleaseSourceTransitionHandles)).ToArray());
                    }
                    finally
                    {
                        _recoveries.Clear();
                        _activeMutations.Clear();
                        _mutationEpochs.Clear();
                        _mutationStates.Clear();
                    }
                }
            }),
            _workspaceSlots,
            _lifetimeCancellation);
    }

    private static void StampCacheHitMetric(WorkspaceCacheWriteResult? result)
    {
        if (result is not null && AmbientGateMetrics.Current is { } metrics)
        {
            metrics.CacheHit = result.CacheHit;
        }
    }

    private Task LoadIntoSessionAsync(WorkspaceSession session, string path, ForkInvocationAuthority? invocation, CancellationToken ct)
        => LoadIntoSessionAsync(session, path,
            session.Workspace?.Properties.ToDictionary(pair => pair.Key, pair => pair.Value),
            expected: null, preservePreviews: false, SupplementalPackageReadAuthority.None, invocation, ct);

    private async Task LoadIntoSessionAsync(
        WorkspaceSession session,
        string path,
        IDictionary<string, string>? globalProperties,
        PhysicalGraphExpectation? expected,
        bool preservePreviews,
        SupplementalPackageReadAuthority supplementalPackages,
        ForkInvocationAuthority? invocation,
        CancellationToken ct)
    {
        var forkInvocation = RequireInvocation(invocation);
        forkInvocation?.RequireDestination(session, path, globalProperties);
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
        Exception? nativeLoadFailure = null;
        Task AuthorizeLoadInputsAsync(IReadOnlyList<string> paths, CancellationToken token) =>
            _pathPolicy.ValidateAsync(session.WorkspaceId, paths, token);
        try
        {
            if (expected is not null) await expected.AssertBuildInputsAsync(session.WorkspaceId, _pathPolicy, ct).ConfigureAwait(false);
            // Hash only the current-authorized entry and deserialize the owned cache
            // candidate. Untrusted cached coordinates are compared after native load;
            // they never authorize or initiate physical reference observations.
            WorkspaceCacheProbe? cachedProbe = null;
            // Skip cache probe when caller passed global property overrides — different
            // Configuration / RuntimeIdentifier / etc. produce different MSBuild graph hashes
            // and the post-load graph/reference comparison validates the actual
            // overridden snapshot before any cache hit is reported.
            if (_cacheCoordinator is not null && globalProperties is not { Count: > 0 })
            {
                cachedProbe = await _cacheCoordinator.TryProbeAsync(path, AuthorizeLoadInputsAsync, ct).ConfigureAwait(false);
            }

            // A persisted candidate has no current native reference authority. It must
            // never bypass the restore-race wait before the native snapshot exists.
            await _restoreStalenessDetector.WaitForStableRestoreArtifactsAsync(path,
                globalProperties is null ? new Dictionary<string, string>() : new Dictionary<string, string>(globalProperties),
                AuthorizeLoadInputsAsync, _options.RestoreRaceWaitMs, _logger, ct).ConfigureAwait(false);

            // workspace-manager-loadintosession-split: delegate MSBuild creation + global-
            // properties wiring + Open + analyzer-reference retargeting to the loader. The
            // loader self-disposes on failure before returning, so the manager only handles
            // disposal of post-loader-success state (strip-analyzers, project-statuses,
            // cache-write failures).
            var loaded = await _sessionLoader.CreateAndOpenAsync(
                session.WorkspaceId,
                path,
                globalProperties,
                _pathPolicy,
                diagnosticsSink,
                _logger,
                supplementalPackages, null,
                ct).ConfigureAwait(false);
            newWorkspace = loaded.Workspace;
            newLease = loaded.Lease;

            // unresolved-analyzer-reference-crash: strip UnresolvedAnalyzerReference entries
            // from every project before any downstream caller can see them. SymbolFinder,
            // Compilation.GetDiagnostics, and Roslyn-internal switches over AnalyzerReference
            // subtypes throw "Unexpected value 'UnresolvedAnalyzerReference'" otherwise. The
            // earlier per-service guards in CompilationCache/FixAllService are now unnecessary.
            var analyzerProjection = await EvaluatedProjectGraph.WithPersistedReferenceMetadataAsync(
                session.WorkspaceId, loaded.Context, _pathPolicy, ct).ConfigureAwait(false);
            var loadedSolution = analyzerProjection.Solution;
            var redactor = new MsBuildCommandProperties.Redactor(MsBuildCommandProperties.Append([], newWorkspace.Properties));
            var compilerContextUnavailable = loaded.Context.Availability != NativeCompilerContextAvailability.Certified;
            var preparationDiagnostics = analyzerProjection.Diagnostics.AsEnumerable();
            if (compilerContextUnavailable)
                preparationDiagnostics = preparationDiagnostics.Append(new DiagnosticDto(
                    Id: "WORKSPACE_COMPILER_CONTEXT_UNAVAILABLE",
                    Message: loaded.Context.RecoveryGuidance,
                    Severity: WorkspaceDiagnosticSeverityClassifier.Classify(WorkspaceDiagnosticKind.Warning, ""),
                    Category: "Workspace", FilePath: null, StartLine: null, StartColumn: null,
                    EndLine: null, EndColumn: null, Location: null));
            foreach (var diagnostic in preparationDiagnostics)
            {
                diagnosticsSink.Queue.Enqueue(diagnostic with
                {
                    Message = redactor.Text(diagnostic.Message),
                    FilePath = diagnostic.FilePath is null ? null : redactor.Text(diagnostic.FilePath)
                });
                while (diagnosticsSink.Queue.Count > MaxDiagnosticsPerWorkspace) diagnosticsSink.Queue.TryDequeue(out _);
            }
            EnsureWorkspacePathsArePhysical(loadedSolution, requirePhysicalDocuments: !compilerContextUnavailable);
            if (expected is not null)
            {
                await expected.AssertBuildInputsAsync(session.WorkspaceId, _pathPolicy, ct).ConfigureAwait(false);
                await SolutionFileMutationPlanner.AssertEquivalentAsync(expected.Solution, loadedSolution,
                    expected.MapPath, static path => path, verifyDocuments: true, expected.PathProperties, ct).ConfigureAwait(false);
                expected.ExecutionIdentity.AssertEquivalent(loadedSolution, ct);
                await expected.AssertBuildInputsAsync(session.WorkspaceId, _pathPolicy, ct).ConfigureAwait(false);
            }


            // workspace-load-uses-cache-fast-path: capture the post-load project graph + per-
            // project metadata-reference list for cache write-back. Building the graph first
            // (so the third cache-key component — graphHash — can be computed) lets a cache hit
            // be validated against the just-loaded snapshot, and a cache miss persist a fresh
            // entry. Best-effort: serialization failure or store-write failure must NOT abort
            // the load. Validating the cache means: (a) graph hash matches what we just observed,
            // and (b) metadata-reference timestamps still match disk. Either mismatch downgrades
            // the result to a cache miss and writes a fresh entry.
            var projectStatuses = BuildProjectStatuses(loadedSolution, ct);
            var documentPaths = BuildDocumentPathIndex(loadedSolution);
            // Capability restrictions require grants or physical paths, not a restore.
            // Keep restore readiness tied to the selecting package-input epoch.
            var restoreRequired = loaded.Context.Availability == NativeCompilerContextAvailability.InvalidRestoreAssets
                                  || await _restoreStalenessDetector.DetectRestoreRequiredAsync(loadedSolution, AuthorizeLoadedInputsAsync, _logger, ct).ConfigureAwait(false) ||
                                  RestoreStalenessDetector.HasRestoreRequiredWorkspaceDiagnostics(diagnosticsSink.Queue);
            // restore-required-vs-build-conflation: an unresolved-analyzer warning is a missing
            // BUILD output, distinct from a missing NuGet package. Track it separately so the
            // summary hint, readiness verdict, and autoRestore gate route to `dotnet build`
            // rather than a no-op `dotnet restore`.
            var buildRequired = WorkspaceDiagnosticClassifier.HasBuildRequired(diagnosticsSink.Queue);

            Task AuthorizeLoadedInputsAsync(IReadOnlyList<string> paths, CancellationToken token) =>
                loaded.Context.AuthorizeCurrentInputsAsync(loadedSolution, session.WorkspaceId, _pathPolicy, paths, token);

            if (_cacheCoordinator is not null && compilerContextUnavailable)
            {
                // Metadata caching is optional; semantic-only snapshots do not mint
                // intrinsic SDK/image read authority for nonessential cache work.
                StampCacheHitMetric(new WorkspaceCacheWriteResult(CacheHit: false));
            }
            else if (_cacheCoordinator is not null && cachedProbe is not null)
            {
                var result = await _cacheCoordinator.ResolveAndWriteAsync(loadedSolution, cachedProbe, AuthorizeLoadedInputsAsync, ct)
                    .ConfigureAwait(false);
                StampCacheHitMetric(result);
            }
            else if (_cacheCoordinator is not null)
            {
                var result = await _cacheCoordinator.WriteFreshEntryAsync(loadedSolution, path, AuthorizeLoadedInputsAsync, ct)
                    .ConfigureAwait(false);
                StampCacheHitMetric(result);
            }

            // autoreload-cascade-stdio-host-crash: atomic swap. Assign all session state AFTER
            // the new workspace is fully loaded so concurrent readers never observe a mid-reload
            // session (disposed workspace, empty project list, or mismatched LoadedPath).
            await supplementalPackages.AssertCurrentAsync(ct).ConfigureAwait(false);
            if (forkInvocation is not null) await forkInvocation.AssertCurrentAsync(ct).ConfigureAwait(false);
            await loaded.Context.AssertLoadCurrentAsync(loadedSolution, session.WorkspaceId, _pathPolicy, supplementalPackages, expected is null ? null : PhysicalInputClosure.FromExpectation(expected), ct).ConfigureAwait(false);
            forkInvocation?.AssertCurrent();
            void Publish()
            {
                session.Publish(newWorkspace!, loadedSolution);
                swapped = true;
                session.AnalyzerLease = newLease;
                session.WorkspaceDiagnostics = diagnosticsSink.Queue;
                session.ProjectStatuses = projectStatuses;
                session.DocumentPaths = documentPaths;
                session.LoadedPath = path;
                session.RestoreRequired = restoreRequired;
                session.BuildRequired = buildRequired;
                session.LoadedAtUtc = DateTimeOffset.UtcNow;
                session.IncrementVersion();
                AdvanceMutationEpoch(session);
                // format-range-apply-preview-token-lifetime: drop only the tokens whose pinned
                // workspace-version range has been exceeded by this reload bump (default span = 1,
                // so a preview → single-reload → apply sequence inside the TTL window still
                // redeems). Replaces the prior unconditional `InvalidateAll` that surfaced as
                // "Preview token not found or expired" on every `*_apply` racing a watcher-driven
                // auto-reload. The captured Roslyn `Solution` snapshots are immutable graphs and
                // remain readable after the prior `MSBuildWorkspace` is disposed below — the apply
                // path's `RebaseModifiedSolutionOntoCurrentAsync` matches documents by file path,
                // so the post-reload `ProjectId`/`DocumentId` lineage divergence is handled.
                if (!preservePreviews)
                    _previewStore.InvalidateOnVersionBump(session.WorkspaceId, session.Version);

            }
            if (forkInvocation is null) Publish();
            else forkInvocation.Publish(session, Publish);
            LogWorkspaceLoaded(_logger, session.WorkspaceId, path, session.Version, null);
        }
        catch (Exception failure)
        {
            nativeLoadFailure = failure;
            throw;
        }
        finally
        {
            try
            {
                // Clear the workspace reference before releasing its analyzer lease so
                // collectible assemblies are not rooted by this async frame during cleanup.
                OwnedResourceCleanup.DisposePreservingFailure("Failed to release native workspace load resources.", nativeLoadFailure,
                    OwnedResourceCleanup.FromAction(() =>
                    {
                        try { if (swapped) oldWorkspace?.Dispose(); else newWorkspace?.Dispose(); }
                        finally { if (swapped) oldWorkspace = null; else newWorkspace = null; }
                    }), swapped ? oldLease : newLease);
            }
            finally { session.LoadLock.Release(); }
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

    private static string ValidateWorkspacePathLexically(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new PublicArgumentException(WorkspaceSessionLoader.SupportedPathCorrection, nameof(path));
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
            && !fullPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)
            && !fullPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            throw new PublicArgumentException(WorkspaceSessionLoader.SupportedPathCorrection, nameof(path));
        return fullPath;
    }

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

    private static void EnsureWorkspacePathsArePhysical(Solution solution, bool requirePhysicalDocuments)
    {
        foreach (var project in solution.Projects)
        {
            EnsurePhysicalPath(project.FilePath);
            // Unavailable certificates preserve native semantic documents, including
            // aliases, but cannot authorize compiler images or physical mutations.
            if (!requirePhysicalDocuments) continue;
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
        foreach (var project in current.Projects.Concat(next.Projects))
            EnsurePhysicalPath(project.FilePath);

        var changes = next.GetChanges(current);
        if (changes.GetAddedProjects().Any() || changes.GetRemovedProjects().Any())
        {
            EnsurePhysicalPath(current.FilePath);
            EnsurePhysicalPath(next.FilePath);
        }
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
        if (!Path.IsPathFullyQualified(path))
            throw new PublicInvalidOperationException("Workspace physical paths must be fully qualified.");

        var physicalPath = PhysicalPathResolver.Resolve(path);
        var lexicalPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (FileSystemPath.Comparer.Equals(lexicalPath, physicalPath)) return;

        throw PhysicalPathRequired();
    }

    internal static InvalidOperationException PhysicalPathRequired() => new(
        "Workspace project or modified document path has a filesystem-link component. " +
        "Load and edit through the physical path.");

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

    private ImmutableArray<ProjectStatusDto> BuildProjectStatuses(Solution solution, CancellationToken ct)
    {
        if (solution.ProjectIds.Count == 0) return [];
        var graph = EvaluatedBuildGraph.Source(solution, ct);
        return solution.Projects.Select(project =>
        {
            var contexts = EvaluatedBuildGraph.MatchingContexts(project, graph).Select(node => node.Inputs).ToArray();
            if (contexts.Length == 0) throw new PublicInvalidOperationException("The loaded semantic project has no matching evaluated context.");
            var projectDoc = Helpers.ProjectMetadataParser.LoadProjectDocument(project.FilePath, _logger);
            var outputTypes = contexts.Select(context => Helpers.ProjectMetadataParser.GetOutputType(context, projectDoc))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var outputType = outputTypes.Length == 1 ? outputTypes[0] : project.CompilationOptions?.OutputKind switch
            {
                OutputKind.ConsoleApplication => "Exe",
                OutputKind.WindowsApplication => "WinExe",
                OutputKind.DynamicallyLinkedLibrary => "Library",
                OutputKind.NetModule => "Module",
                OutputKind.WindowsRuntimeMetadata => "WinMDObj",
                OutputKind.WindowsRuntimeApplication => "AppContainerExe",
                _ => "unknown"
            };
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
                TargetFrameworks: contexts.SelectMany(context => Helpers.ProjectMetadataParser.GetTargetFrameworks(context, projectDoc))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                // Status reports test capability across matching contexts; producers still require a unique context.
                IsTestProject: Helpers.ProjectMetadataParser.IsTestProject(contexts),
                AssemblyName: Helpers.ProjectMetadataParser.GetAssemblyName(project),
                OutputType: outputType);
        }).ToImmutableArray();
    }

    private sealed class WorkspaceSession(string workspaceId) : IDisposable
    {
        private int _version;
        private int _disposed;

        public string WorkspaceId { get; } = workspaceId;
        public SemaphoreSlim LoadLock { get; } = new(1, 1);
        public ConcurrentQueue<DiagnosticDto> WorkspaceDiagnostics { get; set; } = new();
        public ImmutableArray<ProjectStatusDto> ProjectStatuses { get; set; } = ImmutableArray<ProjectStatusDto>.Empty;
        public ImmutableHashSet<string> DocumentPaths { get; set; } = ImmutableHashSet.Create<string>(FileSystemPath.Comparer);
        public sealed record LoadedGraph(MSBuildWorkspace Workspace, Solution Solution);
        private LoadedGraph? _loaded;
        public MSBuildWorkspace? Workspace
        {
            get => Volatile.Read(ref _loaded)?.Workspace;
            set => Volatile.Write(ref _loaded, value is null ? null : new LoadedGraph(value, value.CurrentSolution));
        }
        public LoadedGraph? Capture() => Volatile.Read(ref _loaded);
        public void Publish(MSBuildWorkspace workspace, Solution solution)
        {
            Volatile.Write(ref _loaded, new LoadedGraph(workspace, solution));
        }

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
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            MSBuildWorkspace? workspace = Interlocked.Exchange(ref _loaded, null)?.Workspace;
            var lease = AnalyzerLease;
            AnalyzerLease = null;
            // Release the workspace reference before its analyzer lease so the solution
            // graph does not retain collectible assemblies during owned loader cleanup.
            OwnedResourceCleanup.Dispose("Workspace session resource cleanup failed.", null,
                OwnedResourceCleanup.FromAction(() =>
                {
                    try { workspace?.Dispose(); }
                    finally { workspace = null; }
                }), lease, LoadLock);
        }
    }
}
