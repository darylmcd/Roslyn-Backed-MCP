using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;
using System.Threading;
using System.Xml.Linq;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Manages named <c>MSBuildWorkspace</c> sessions, providing load, reload, query, and
/// solution-mutation operations. Each session is independently versioned and monitored
/// for file-system staleness via <see cref="RoslynMcp.Core.Services.IFileWatcherService"/>.
/// </summary>
public sealed class WorkspaceManager : IWorkspaceManager, IDisposable
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
    private readonly WorkspaceManagerOptions _options;
    private readonly ConcurrentDictionary<string, WorkspaceSession> _sessions = new(StringComparer.Ordinal);
    /// <summary>Limits concurrent workspace sessions; paired with <see cref="Close"/> and <see cref="Dispose"/>.</summary>
    private readonly SemaphoreSlim _workspaceSlots;

    /// <inheritdoc />
    public event Action<string>? WorkspaceClosed;

    /// <inheritdoc />
    public event Action<string>? WorkspaceReloaded;

    public WorkspaceManager(
        ILogger<WorkspaceManager> logger,
        IPreviewStore previewStore,
        IFileWatcherService fileWatcher,
        WorkspaceManagerOptions? options = null)
    {
        _logger = logger;
        _previewStore = previewStore;
        _fileWatcher = fileWatcher;
        _options = options ?? new WorkspaceManagerOptions();
        var max = _options.MaxConcurrentWorkspaces > 0 ? _options.MaxConcurrentWorkspaces : 8;
        _workspaceSlots = new SemaphoreSlim(max, max);
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
                && string.Equals(candidate.LoadedPath, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }
        return null;
    }

    public async Task<WorkspaceStatusDto> LoadAsync(string path, CancellationToken ct)
    {
        var fullPath = ValidateWorkspacePath(path);

        // workspace-session-deduplication: if the caller is loading a path that is already
        // tracked by a live session, return that session's status instead of spinning up a
        // second WorkspaceId. Prevents the "one host, two distinct IDs for the same solution"
        // pattern from the 2026-04-08 roslyn-backed-mcp audit and keeps workspace_list aligned
        // with operator expectations. Matches the "idempotent for repeated loads" language
        // already in the workspace_load tool description.
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

        if (!await _workspaceSlots.WaitAsync(0, ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"The server is already tracking {_options.MaxConcurrentWorkspaces} workspaces. Close an existing workspace before loading another.");
        }
        var workspaceId = Guid.NewGuid().ToString("N");
        var session = new WorkspaceSession(workspaceId);
        var sessionAdded = false;
        try
        {
            await LoadIntoSessionAsync(session, fullPath, ct).ConfigureAwait(false);

            // Second dedup check, this time for the race window between our scan above and
            // another concurrent LoadAsync call that won the semaphore with the same path.
            // Whichever caller lost the race returns the winner's session and releases its
            // slot in the finally block so we do not leak a slot per racing caller.
            var raceWinner = FindSessionByLoadedPath(fullPath);
            if (raceWinner is not null)
            {
                _logger.LogInformation(
                    "workspace_load: race lost; returning winner '{WorkspaceId}' for path '{Path}' instead of new '{NewId}'",
                    raceWinner.WorkspaceId,
                    fullPath,
                    workspaceId);
                session.Dispose();
                raceWinner.TouchAccess();
                return BuildStatus(raceWinner);
            }

            if (!_sessions.TryAdd(workspaceId, session))
            {
                throw new InvalidOperationException($"Workspace '{workspaceId}' already exists.");
            }

            sessionAdded = true;
            _fileWatcher.Watch(workspaceId, session.LoadedPath!);
            return BuildStatus(session);
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

            session.Dispose();
            throw;
        }
        catch
        {
            session.Dispose();
            throw;
        }
        finally
        {
            if (!sessionAdded)
            {
                _workspaceSlots.Release();
            }
        }
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

    public bool Close(string workspaceId)
    {
        if (!_sessions.TryRemove(workspaceId, out var session))
        {
            return false;
        }

        _fileWatcher.Unwatch(workspaceId);
        _previewStore.InvalidateAll(workspaceId);
        session.Dispose();
        _workspaceSlots.Release();
        RaiseWorkspaceClosed(workspaceId);
        LogWorkspaceClosed(_logger, workspaceId, null);
        return true;
    }

    /// <summary>
    /// Notifies subscribers (e.g., <see cref="ICompilationCache"/>) that a workspace has been
    /// closed. Wrapped so that handler exceptions cannot break the close path.
    /// </summary>
    private void RaiseWorkspaceClosed(string workspaceId)
    {
        var handler = WorkspaceClosed;
        if (handler is null) return;

        try
        {
            handler(workspaceId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WorkspaceClosed handler threw for {WorkspaceId}", workspaceId);
        }
    }

    /// <summary>
    /// Notifies subscribers that a workspace has been reloaded. Wrapped so that handler
    /// exceptions cannot break the reload path.
    /// </summary>
    private void RaiseWorkspaceReloaded(string workspaceId)
    {
        var handler = WorkspaceReloaded;
        if (handler is null) return;

        try
        {
            handler(workspaceId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WorkspaceReloaded handler threw for {WorkspaceId}", workspaceId);
        }
    }

    public IReadOnlyList<WorkspaceStatusDto> ListWorkspaces()
    {
        return _sessions.Values.Select(BuildStatus).ToList();
    }

    public WorkspaceStatusDto GetStatus(string workspaceId)
    {
        var session = GetRequiredSession(workspaceId);

        // Acquire LoadLock to avoid reading partially-updated session state
        // during a concurrent LoadIntoSessionAsync.
        if (!session.LoadLock.Wait(TimeSpan.FromSeconds(5)))
        {
            _logger.LogWarning("GetStatus timed out waiting for LoadLock on {WorkspaceId}", workspaceId);
            throw new TimeoutException(
                $"Workspace '{workspaceId}' is currently loading. Try again shortly.");
        }

        try
        {
            return BuildStatus(session);
        }
        finally
        {
            session.LoadLock.Release();
        }
    }

    public async Task<WorkspaceStatusDto> GetStatusAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        var session = GetRequiredSession(workspaceId);

        if (!await session.LoadLock.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false))
        {
            _logger.LogWarning("GetStatus timed out waiting for LoadLock on {WorkspaceId}", workspaceId);
            throw new TimeoutException(
                $"Workspace '{workspaceId}' is currently loading. Try again shortly.");
        }

        try
        {
            return BuildStatus(session);
        }
        finally
        {
            session.LoadLock.Release();
        }
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
                        string.Equals(Path.GetFullPath(d.FilePath), Path.GetFullPath(filePath), StringComparison.OrdinalIgnoreCase));
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
        return session.Workspace?.CurrentSolution
            ?? throw new InvalidOperationException($"Workspace '{workspaceId}' is not loaded.");
    }

    /// <summary>
    /// solution-project-index-by-name: per-(workspaceId, version) index from project name and
    /// absolute file path → Project, lazily built on first lookup. Older entries for the same
    /// workspace are pruned automatically when the version bumps.
    /// </summary>
    private readonly ConcurrentDictionary<string, ProjectIndexEntry> _projectIndex = new(StringComparer.Ordinal);

    private sealed record ProjectIndexEntry(int Version, IReadOnlyDictionary<string, Project> ByName);

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

        return entry.ByName.TryGetValue(projectNameOrPath, out var hit) ? hit : null;
    }

    private static ProjectIndexEntry BuildProjectIndex(int version, Solution solution)
    {
        var dict = new Dictionary<string, Project>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in solution.Projects)
        {
            if (!string.IsNullOrEmpty(project.Name) && !dict.ContainsKey(project.Name))
            {
                dict[project.Name] = project;
            }
            if (!string.IsNullOrEmpty(project.FilePath))
            {
                var fullPath = Path.GetFullPath(project.FilePath);
                if (!dict.ContainsKey(fullPath))
                {
                    dict[fullPath] = project;
                }
                if (!dict.ContainsKey(project.FilePath))
                {
                    dict[project.FilePath] = project;
                }
            }
        }
        return new ProjectIndexEntry(version, dict);
    }

    public bool TryApplyChanges(string workspaceId, Solution newSolution)
    {
        var session = GetRequiredSession(workspaceId);
        if (session.Workspace is null)
        {
            throw new InvalidOperationException($"Workspace '{workspaceId}' is not loaded.");
        }

        var result = session.Workspace.TryApplyChanges(newSolution);
        if (result)
        {
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
            // workspace moves don't need to invalidate tokens. InvalidateAll remains wired
            // to workspace-lifecycle events (Close, LoadIntoSessionAsync) where the underlying
            // MSBuildWorkspace is disposed and the captured Solution references become
            // orphaned.
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
        _fileWatcher.Dispose();

        // Capture session ids before dispose so we can raise WorkspaceClosed for each one.
        // This lets singletons like ICompilationCache free per-workspace state on host shutdown
        // (or test-assembly teardown) instead of leaking until process exit.
        var ids = _sessions.Keys.ToArray();
        foreach (var session in _sessions.Values)
        {
            session.Dispose();
        }
        var n = _sessions.Count;
        _sessions.Clear();
        for (var i = 0; i < n; i++)
        {
            _workspaceSlots.Release();
        }

        foreach (var id in ids)
        {
            RaiseWorkspaceClosed(id);
        }

        _workspaceSlots.Dispose();
    }

    private async Task LoadIntoSessionAsync(WorkspaceSession session, string path, CancellationToken ct)
    {
        await session.LoadLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            session.Workspace?.Dispose();
            MsBuildInitializer.EnsureInitialized();
            session.Workspace = MSBuildWorkspace.Create();
            session.WorkspaceDiagnostics = new ConcurrentQueue<DiagnosticDto>();
            session.ProjectStatuses = ImmutableArray<ProjectStatusDto>.Empty;
            var fullPath = path;

            session.Workspace.RegisterWorkspaceFailedHandler(args =>
            {
                // Normalize at ingress so workspace_load, workspace_status, project_diagnostics,
                // and the roslyn://workspaces resource all see the same severity. Previously
                // only project_diagnostics applied the downgrade, leaving the other readers
                // surfacing pruning-style informational messages as Error.
                var severity = WorkspaceDiagnosticSeverityClassifier.Classify(
                    args.Diagnostic.Kind, args.Diagnostic.Message);
                var dto = new DiagnosticDto(
                    Id: $"WORKSPACE_{args.Diagnostic.Kind}".ToUpperInvariant(),
                    Message: args.Diagnostic.Message,
                    Severity: severity,
                    Category: "Workspace",
                    FilePath: null,
                    StartLine: null,
                    StartColumn: null,
                    EndLine: null,
                    EndColumn: null);
                session.WorkspaceDiagnostics.Enqueue(dto);
                while (session.WorkspaceDiagnostics.Count > MaxDiagnosticsPerWorkspace)
                {
                    session.WorkspaceDiagnostics.TryDequeue(out _);
                }

                _logger.LogWarning(
                    "Workspace {WorkspaceId} diagnostic: {Message}",
                    session.WorkspaceId,
                    args.Diagnostic.Message);
            });

            // dr-9-10-initial-does-not-wait-for-concurrent-to-finaliz: if a concurrent
            // out-of-process `dotnet restore` is mutating `obj/project.assets.json` or
            // `obj/*.dgspec.json` while we call OpenSolutionAsync, MSBuild latches onto an
            // in-flight assets file and Roslyn captures stale MetadataReference handles
            // (surfacing as CS1705 later, per the 2026-04-15 samplesolution experimental
            // promotion audit). Poll the relevant obj/ artefacts and wait for their mtimes
            // to stabilise before opening. Bounded by RestoreRaceWaitMs (default 2000 ms,
            // 0 disables entirely); no-op when no such files exist.
            await WaitForStableRestoreArtifactsAsync(fullPath, ct).ConfigureAwait(false);

            if (fullPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
                fullPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
            {
                await session.Workspace.OpenSolutionAsync(fullPath, cancellationToken: ct).ConfigureAwait(false);
            }
            else if (fullPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                await session.Workspace.OpenProjectAsync(fullPath, cancellationToken: ct).ConfigureAwait(false);
            }
            else
            {
                throw new ArgumentException($"Path must end with .sln, .slnx, or .csproj: {path}");
            }

            // unresolved-analyzer-reference-crash: strip UnresolvedAnalyzerReference entries
            // from every project before any downstream caller can see them. SymbolFinder,
            // Compilation.GetDiagnostics, and Roslyn-internal switches over AnalyzerReference
            // subtypes throw "Unexpected value 'UnresolvedAnalyzerReference'" otherwise. The
            // earlier per-service guards in CompilationCache/FixAllService are now unnecessary.
            await StripUnresolvedAnalyzerReferencesAsync(session, ct).ConfigureAwait(false);

            session.ProjectStatuses = BuildProjectStatuses(session.Workspace.CurrentSolution);
            session.LoadedPath = fullPath;
            session.RestoreRequired = DetectRestoreRequired(session.ProjectStatuses) ||
                                      HasRestoreRequiredWorkspaceDiagnostics(session.WorkspaceDiagnostics);
            session.LoadedAtUtc = DateTimeOffset.UtcNow;
            session.IncrementVersion();
            _previewStore.InvalidateAll(session.WorkspaceId);

            LogWorkspaceLoaded(_logger, session.WorkspaceId, fullPath, session.Version, null);
        }
        finally
        {
            session.LoadLock.Release();
        }
    }

    /// <summary>
    /// Interval between mtime samples inside the restore-race stability probe. Chosen so the
    /// stable window is roughly 250 ms (two samples separated by this interval) — long enough
    /// that a `dotnet restore` in its final write phase cannot squeeze an asset rewrite inside
    /// it, short enough that a no-op pre-check returns within ~1.5 × <c>StableWindowMs</c>.
    /// </summary>
    private const int RestoreRaceSampleIntervalMs = 125;

    /// <summary>
    /// Required stable window (in milliseconds) that every detected restore artefact must
    /// hold its mtime for before <see cref="LoadIntoSessionAsync"/> hands the solution to
    /// MSBuild. Two consecutive samples separated by <see cref="RestoreRaceSampleIntervalMs"/>
    /// produce a ~250 ms window.
    /// </summary>
    private const int RestoreRaceStableWindowMs = 250;

    /// <summary>
    /// dr-9-10-initial-does-not-wait-for-concurrent-to-finaliz — best-effort wait for a
    /// concurrent out-of-process <c>dotnet restore</c> to finish before MSBuild opens the
    /// solution.
    /// </summary>
    /// <remarks>
    /// Enumerates <c>obj/project.assets.json</c> and <c>obj/*.dgspec.json</c> under the
    /// workspace root, polls their <see cref="File.GetLastWriteTimeUtc"/>, and returns once
    /// every file's mtime has been stable for
    /// <see cref="RestoreRaceStableWindowMs"/> ms — or the
    /// <see cref="WorkspaceManagerOptions.RestoreRaceWaitMs"/> cap fires. No-op when the cap
    /// is zero, when no artefacts exist (typical on a pristine checkout before first build),
    /// or when all artefacts are already stable on the first sample (typical on a healthy
    /// load after a completed restore).
    /// </remarks>
    private async Task WaitForStableRestoreArtifactsAsync(string fullPath, CancellationToken ct)
    {
        var capMs = _options.RestoreRaceWaitMs;
        if (capMs <= 0)
        {
            return;
        }

        var rootDirectory = fullPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(fullPath)
            : Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(rootDirectory) || !Directory.Exists(rootDirectory))
        {
            return;
        }

        // Enumerate each project's obj/ directory artefacts once up-front and track their
        // timestamps in a small dictionary. EnumerateFiles is recursive but bounded by the
        // solution's own directory tree, so on real solutions this is a few dozen tiny stats.
        var artefacts = EnumerateRestoreArtefacts(rootDirectory);
        if (artefacts.Count == 0)
        {
            return;
        }

        var deadline = DateTime.UtcNow.AddMilliseconds(capMs);
        var lastSnapshot = new Dictionary<string, DateTime>(artefacts.Count, StringComparer.OrdinalIgnoreCase);
        var stableSince = new Dictionary<string, DateTime>(artefacts.Count, StringComparer.OrdinalIgnoreCase);

        // Seed the snapshot. A file that does not exist yet is tracked as DateTime.MinValue
        // so the stability check catches the "appears mid-poll" case (restore creating the
        // first project.assets.json while we race it).
        var seedNow = DateTime.UtcNow;
        foreach (var path in artefacts)
        {
            lastSnapshot[path] = SafeGetLastWriteTimeUtc(path);
            stableSince[path] = seedNow;
        }

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var now = DateTime.UtcNow;
            var allStable = true;

            foreach (var path in artefacts)
            {
                var currentMtime = SafeGetLastWriteTimeUtc(path);
                if (currentMtime != lastSnapshot[path])
                {
                    // Mtime moved — reset the stability window for this file.
                    lastSnapshot[path] = currentMtime;
                    stableSince[path] = now;
                    allStable = false;
                    continue;
                }

                if ((now - stableSince[path]).TotalMilliseconds < RestoreRaceStableWindowMs)
                {
                    allStable = false;
                }
            }

            if (allStable)
            {
                return;
            }

            if (now >= deadline)
            {
                _logger.LogWarning(
                    "workspace_load: restore-race wait hit {CapMs} ms cap for '{Path}' without reaching a stable mtime window. Proceeding with load — callers may observe CS1705 drift; re-run workspace_reload after the concurrent restore finishes if so.",
                    capMs,
                    fullPath);
                return;
            }

            // Bound the delay so we never overshoot the deadline by more than one interval.
            var remainingMs = (int)Math.Max(0, (deadline - now).TotalMilliseconds);
            var delayMs = Math.Min(RestoreRaceSampleIntervalMs, remainingMs);
            if (delayMs == 0)
            {
                // Last-iteration guard: if we've arrived at the deadline, take one final
                // reading on the next loop and exit via the deadline branch.
                continue;
            }

            await Task.Delay(delayMs, ct).ConfigureAwait(false);
        }
    }

    private static List<string> EnumerateRestoreArtefacts(string rootDirectory)
    {
        // Enumerate every obj/ directory under the solution root. EnumerateDirectories with
        // SearchOption.AllDirectories is bounded by the solution tree; a typical solution
        // has one obj/ directory per project. We then look only for the two files Roslyn /
        // MSBuild actually consume during OpenSolutionAsync — project.assets.json and the
        // project's <Project>.dgspec.json — so deep nested Debug/Release/TFM subdirectories
        // do not inflate the probe set.
        var results = new List<string>();
        try
        {
            foreach (var objDir in Directory.EnumerateDirectories(rootDirectory, "obj", SearchOption.AllDirectories))
            {
                try
                {
                    var assetsPath = Path.Combine(objDir, "project.assets.json");
                    if (File.Exists(assetsPath))
                    {
                        results.Add(assetsPath);
                    }

                    foreach (var dgspec in Directory.EnumerateFiles(objDir, "*.dgspec.json", SearchOption.TopDirectoryOnly))
                    {
                        results.Add(dgspec);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Tolerate transient IO errors (a concurrent restore may delete/recreate
                    // subdirectories). The probe is best-effort; skip and continue.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort: if we cannot enumerate at all, skip the wait entirely.
        }

        return results;
    }

    private static DateTime SafeGetLastWriteTimeUtc(string path)
    {
        try
        {
            // File.Exists check collapses the "file deleted mid-probe" case into MinValue,
            // which the caller treats as a change in the next sample (restore rewrote it).
            return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return DateTime.MinValue;
        }
    }

    private bool DetectRestoreRequired(ImmutableArray<ProjectStatusDto> projects)
    {
        foreach (var project in projects)
        {
            if (IsRestoreRequired(project.FilePath))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsRestoreRequired(string projectFilePath)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath) || !File.Exists(projectFilePath))
        {
            return false;
        }

        try
        {
            var expectedPackages = CollectExpectedPackages(projectFilePath);
            if (expectedPackages.Count == 0)
            {
                return false;
            }

            var assets = LoadAssetsPackageVersions(projectFilePath);
            if (assets is null)
            {
                return true;
            }

            foreach (var (packageId, expectation) in expectedPackages)
            {
                if (!assets.Value.PackageVersions.TryGetValue(packageId, out var assetVersions))
                {
                    return true;
                }

                if (!string.IsNullOrWhiteSpace(expectation.RequestedVersion) &&
                    !PackageVersionMatches(expectation.RequestedVersion!, assetVersions))
                {
                    return true;
                }

                if (expectation.UsesCentralVersion)
                {
                    if (string.IsNullOrWhiteSpace(expectation.RequestedVersion) ||
                        !assets.Value.CentralPackageVersions.TryGetValue(packageId, out var centralVersions) ||
                        !PackageVersionMatches(expectation.RequestedVersion!, centralVersions))
                    {
                        return true;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            _logger.LogDebug(ex, "restore-drift detection skipped for '{ProjectFilePath}'", projectFilePath);
        }

        return false;
    }

    private static Dictionary<string, PackageExpectation> CollectExpectedPackages(string projectFilePath)
    {
        var expected = new Dictionary<string, PackageExpectation>(StringComparer.OrdinalIgnoreCase);
        var centralPackages = LoadCentralPackageVersions(MsBuildMetadataHelper.FindDirectoryPackagesProps(projectFilePath));

        foreach (var documentPath in EnumeratePackageReferenceDocuments(projectFilePath))
        {
            XDocument document;
            try
            {
                document = XDocument.Load(documentPath, LoadOptions.PreserveWhitespace);
            }
            catch (System.Xml.XmlException)
            {
                continue;
            }

            foreach (var element in document
                         .Descendants()
                         .Where(candidate => string.Equals(candidate.Name.LocalName, "PackageReference", StringComparison.OrdinalIgnoreCase)))
            {
                var packageId = GetXmlValue(element, "Include") ?? GetXmlValue(element, "Update");
                if (string.IsNullOrWhiteSpace(packageId))
                {
                    continue;
                }

                var explicitVersion =
                    GetXmlValue(element, "VersionOverride") ??
                    GetChildValue(element, "VersionOverride") ??
                    GetXmlValue(element, "Version") ??
                    GetChildValue(element, "Version");

                centralPackages.TryGetValue(packageId, out var centralVersion);
                var usesCentralVersion = string.IsNullOrWhiteSpace(explicitVersion) &&
                                         !string.IsNullOrWhiteSpace(centralVersion);
                expected[packageId] = new PackageExpectation(
                    usesCentralVersion ? centralVersion : explicitVersion,
                    usesCentralVersion);
            }
        }

        return expected;
    }

    private static IEnumerable<string> EnumeratePackageReferenceDocuments(string projectFilePath)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var buildPropsPath = FindNearestFile(projectFilePath, "Directory.Build.props");
        if (!string.IsNullOrWhiteSpace(buildPropsPath) && seen.Add(buildPropsPath))
        {
            yield return buildPropsPath;
        }

        if (seen.Add(projectFilePath))
        {
            yield return projectFilePath;
        }

        var buildTargetsPath = FindNearestFile(projectFilePath, "Directory.Build.targets");
        if (!string.IsNullOrWhiteSpace(buildTargetsPath) && seen.Add(buildTargetsPath))
        {
            yield return buildTargetsPath;
        }
    }

    private static string? FindNearestFile(string path, string fileName)
    {
        var directory = Path.GetDirectoryName(path);
        while (!string.IsNullOrWhiteSpace(directory))
        {
            var candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        return null;
    }

    private static Dictionary<string, string> LoadCentralPackageVersions(string? packagesPropsPath)
    {
        var centralPackages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(packagesPropsPath) || !File.Exists(packagesPropsPath))
        {
            return centralPackages;
        }

        XDocument document;
        try
        {
            document = XDocument.Load(packagesPropsPath, LoadOptions.PreserveWhitespace);
        }
        catch (System.Xml.XmlException)
        {
            return centralPackages;
        }

        foreach (var element in document
                     .Descendants()
                     .Where(candidate => string.Equals(candidate.Name.LocalName, "PackageVersion", StringComparison.OrdinalIgnoreCase)))
        {
            var packageId = GetXmlValue(element, "Include");
            var version = GetXmlValue(element, "Version") ?? GetChildValue(element, "Version");
            if (!string.IsNullOrWhiteSpace(packageId) && !string.IsNullOrWhiteSpace(version))
            {
                centralPackages[packageId] = version;
            }
        }

        return centralPackages;
    }

    private static (Dictionary<string, HashSet<string>> PackageVersions, Dictionary<string, HashSet<string>> CentralPackageVersions)? LoadAssetsPackageVersions(string projectFilePath)
    {
        var projectDirectory = Path.GetDirectoryName(projectFilePath);
        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            return null;
        }

        var assetsPath = Path.Combine(projectDirectory, "obj", "project.assets.json");
        if (!File.Exists(assetsPath))
        {
            return null;
        }

        using var stream = File.OpenRead(assetsPath);
        using var document = JsonDocument.Parse(stream);
        if (!document.RootElement.TryGetProperty("project", out var project) ||
            !project.TryGetProperty("frameworks", out var frameworks) ||
            frameworks.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var packageVersions = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var centralPackageVersions = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var framework in frameworks.EnumerateObject())
        {
            if (framework.Value.TryGetProperty("dependencies", out var dependencies) &&
                dependencies.ValueKind == JsonValueKind.Object)
            {
                foreach (var dependency in dependencies.EnumerateObject())
                {
                    if (!dependency.Value.TryGetProperty("version", out var versionElement) ||
                        versionElement.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    AddVersion(packageVersions, dependency.Name, versionElement.GetString());
                }
            }

            if (framework.Value.TryGetProperty("centralPackageVersions", out var centralVersions) &&
                centralVersions.ValueKind == JsonValueKind.Object)
            {
                foreach (var centralVersion in centralVersions.EnumerateObject())
                {
                    AddVersion(centralPackageVersions, centralVersion.Name, centralVersion.Value.GetString());
                }
            }
        }

        return (packageVersions, centralPackageVersions);
    }

    private static void AddVersion(Dictionary<string, HashSet<string>> versions, string packageId, string? version)
    {
        if (string.IsNullOrWhiteSpace(packageId) || string.IsNullOrWhiteSpace(version))
        {
            return;
        }

        if (!versions.TryGetValue(packageId, out var values))
        {
            values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            versions[packageId] = values;
        }

        values.Add(version.Trim());
    }

    private static bool PackageVersionMatches(string expectedVersion, IReadOnlySet<string> actualVersions)
    {
        var expected = expectedVersion.Trim();
        if (string.IsNullOrWhiteSpace(expected))
        {
            return false;
        }

        if (actualVersions.Contains(expected))
        {
            return true;
        }

        if (expected.StartsWith("[", StringComparison.Ordinal) || expected.StartsWith("(", StringComparison.Ordinal))
        {
            return false;
        }

        return actualVersions.Contains($"[{expected}, )");
    }

    private static string? GetXmlValue(XElement element, string localName)
    {
        return element.Attributes().FirstOrDefault(attribute =>
            string.Equals(attribute.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase))?.Value?.Trim();
    }

    private static string? GetChildValue(XElement element, string localName)
    {
        return element.Elements().FirstOrDefault(child =>
            string.Equals(child.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase))?.Value?.Trim();
    }

    private static bool HasRestoreRequiredWorkspaceDiagnostics(IEnumerable<DiagnosticDto> diagnostics)
    {
        var suspiciousDiagnostics = 0;
        foreach (var diagnostic in diagnostics)
        {
            if (string.Equals(diagnostic.Id, "WORKSPACE_UNRESOLVED_ANALYZER", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(diagnostic.Id, "CS0234", StringComparison.OrdinalIgnoreCase))
            {
                suspiciousDiagnostics++;
                continue;
            }

            var message = diagnostic.Message;
            if (message is null)
            {
                continue;
            }

            if (message.Contains("could not be found", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("does not exist in the namespace", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("could not load file or assembly", StringComparison.OrdinalIgnoreCase))
            {
                suspiciousDiagnostics++;
            }
        }

        return suspiciousDiagnostics >= 3;
    }

    /// <summary>
    /// Removes <see cref="UnresolvedAnalyzerReference"/> entries from every project in the
    /// loaded workspace. These entries are produced when an analyzer file referenced by the
    /// project cannot be located (typical for analyzer projects targeting netstandard2.0 whose
    /// build output is not yet on disk). Leaving them in place causes Roslyn-internal switches
    /// over AnalyzerReference subtypes to throw
    /// <c>InvalidOperationException("Unexpected value 'UnresolvedAnalyzerReference'")</c>
    /// from SymbolFinder, Compilation.GetDiagnostics, and similar APIs. Each strip emits a
    /// <c>WORKSPACE_UNRESOLVED_ANALYZER</c> warning (severity Warning, not Error) so callers
    /// can still discover that something was filtered.
    /// </summary>
    private async Task StripUnresolvedAnalyzerReferencesAsync(WorkspaceSession session, CancellationToken ct)
    {
        if (session.Workspace is null) return;

        var originalSolution = session.Workspace.CurrentSolution;
        var solution = originalSolution;
        var strippedCount = 0;
        var newDiagnostics = new List<DiagnosticDto>();

        foreach (var project in originalSolution.Projects)
        {
            var unresolved = project.AnalyzerReferences
                .OfType<UnresolvedAnalyzerReference>()
                .ToList();

            if (unresolved.Count == 0) continue;

            foreach (var reference in unresolved)
            {
                solution = solution.RemoveAnalyzerReference(project.Id, reference);
                strippedCount++;

                var displayName = reference.Display ?? reference.FullPath ?? "<unknown>";
                newDiagnostics.Add(new DiagnosticDto(
                    Id: "WORKSPACE_UNRESOLVED_ANALYZER",
                    Message: $"Unresolved analyzer reference removed from project '{project.Name}': {displayName}. " +
                             "This typically indicates a missing analyzer build output (netstandard2.0 analyzer project) " +
                             "or an unresolved package path. Run `dotnet build` on the analyzer project, then `workspace_reload`.",
                    Severity: WorkspaceDiagnosticSeverityClassifier.Classify(WorkspaceDiagnosticKind.Warning, ""),
                    Category: "Workspace",
                    FilePath: project.FilePath,
                    StartLine: null,
                    StartColumn: null,
                    EndLine: null,
                    EndColumn: null));
            }
        }

        if (strippedCount == 0) return;

        // csproj-reserialization-msbuildworkspace (P2) — create-file-apply-csproj-side-effect-all-projects.
        //
        // MSBuildWorkspace.TryApplyChanges reserializes csprojs that had an analyzer-reference
        // removed: MSBuild reprojects the project file to emit the updated AnalyzerReference
        // itemgroup, and in doing so adds a UTF-8 BOM, flips LF→CRLF, collapses blank lines, and
        // strips the trailing newline. The semantics are identical but the on-disk bytes drift,
        // which shows up as noise in git diff — exactly the pattern every recent subagent on the
        // 2026-04-16 backlog-sweep observed on samples/GeneratedDocumentSolution/ConsumerLib/
        // ConsumerLib.csproj during verify-release.ps1 runs (that csproj has an
        // `OutputItemType="Analyzer"` project reference to ConsumerLib.Generators, which registers
        // as UnresolvedAnalyzerReference on first load before the generator output is built).
        //
        // Snapshot ALL csproj bytes before TryApplyChanges; restore any whose post-apply XML is
        // semantically equivalent to the snapshot (trivia-only diff). Csprojs with a legitimate
        // semantic change (should not occur here since we only strip analyzer refs, but defensive)
        // keep their new bytes.
        var csprojSnapshots = await CsprojSemanticEquality.SnapshotProjectsAsync(
            originalSolution.Projects
                .Select(project => project.FilePath)
                .Where(path => !string.IsNullOrWhiteSpace(path))!,
            _logger,
            ct).ConfigureAwait(false);

        if (!session.Workspace.TryApplyChanges(solution))
        {
            // Should be impossible — analyzer reference removal is supported by every workspace
            // implementation. Log and leave the unresolved entries in place; the per-service
            // guards in CompilationCache/FixAllService were removed as part of this change so
            // surface the failure loudly.
            _logger.LogWarning(
                "Workspace {WorkspaceId}: TryApplyChanges failed when stripping {Count} UnresolvedAnalyzerReference entries; downstream tools may still crash on them.",
                session.WorkspaceId, strippedCount);
            return;
        }

        // Restore trivia-only drift introduced by TryApplyChanges's csproj reserialization.
        await CsprojSemanticEquality.RestoreTriviaOnlyDriftAsync(
            csprojSnapshots,
            skipPaths: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            _logger,
            operationTag: "csproj-reserialization-msbuildworkspace/strip-unresolved-analyzer",
            ct).ConfigureAwait(false);

        foreach (var dto in newDiagnostics)
        {
            session.WorkspaceDiagnostics.Enqueue(dto);
            while (session.WorkspaceDiagnostics.Count > MaxDiagnosticsPerWorkspace)
            {
                session.WorkspaceDiagnostics.TryDequeue(out _);
            }
        }

        _logger.LogInformation(
            "Workspace {WorkspaceId}: stripped {Count} UnresolvedAnalyzerReference entries to prevent downstream crashes.",
            session.WorkspaceId, strippedCount);
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
                RestoreRequired: session.RestoreRequired);
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
            RestoreRequired: session.RestoreRequired);
    }

    private WorkspaceSession GetRequiredSession(string workspaceId)
    {
        if (!_sessions.TryGetValue(workspaceId, out var session))
        {
            var activeCount = _sessions.Count;
            var activeIds = string.Join(", ", _sessions.Keys.Take(5));
            LogSessionNotFound(_logger, workspaceId, activeCount, activeIds, null);

            throw new KeyNotFoundException(
                $"Workspace '{workspaceId}' was not found. " +
                $"There are {activeCount} active session(s). " +
                "The session may have been lost due to a server restart or process exit. " +
                "Use workspace_load to create a new session.");
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
            throw new ArgumentException("A workspace path is required.", nameof(path));
        }

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Workspace path was not found: {fullPath}", fullPath);
        }

        if (!fullPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) &&
            !fullPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase) &&
            !fullPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Path must end with .sln, .slnx, or .csproj: {path}");
        }

        return fullPath;
    }

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
                OutputType: Helpers.ProjectMetadataParser.GetOutputType(projectDoc));
        }).ToImmutableArray();
    }

    private readonly record struct PackageExpectation(string? RequestedVersion, bool UsesCentralVersion);

    private sealed class WorkspaceSession(string workspaceId) : IDisposable
    {
        private int _version;

        public string WorkspaceId { get; } = workspaceId;
        public SemaphoreSlim LoadLock { get; } = new(1, 1);
        public ConcurrentQueue<DiagnosticDto> WorkspaceDiagnostics { get; set; } = new();
        public ImmutableArray<ProjectStatusDto> ProjectStatuses { get; set; } = ImmutableArray<ProjectStatusDto>.Empty;
        public MSBuildWorkspace? Workspace { get; set; }
        public string? LoadedPath { get; set; }
        public bool RestoreRequired { get; set; }
        public DateTimeOffset LoadedAtUtc { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset LastAccessedUtc { get; set; } = DateTimeOffset.UtcNow;
        public int Version => Volatile.Read(ref _version);
        public int IncrementVersion() => Interlocked.Increment(ref _version);
        public void SetVersion(int value) => Interlocked.Exchange(ref _version, value);

        public void TouchAccess() => LastAccessedUtc = DateTimeOffset.UtcNow;

        public void Dispose()
        {
            Workspace?.Dispose();
            LoadLock.Dispose();
        }
    }
}
