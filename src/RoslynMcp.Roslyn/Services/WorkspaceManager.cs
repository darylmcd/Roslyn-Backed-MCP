using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Immutable;
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
            _previewStore.InvalidateAll(workspaceId);
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
            StripUnresolvedAnalyzerReferences(session);

            session.ProjectStatuses = BuildProjectStatuses(session.Workspace.CurrentSolution);
            session.LoadedPath = fullPath;
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
    private void StripUnresolvedAnalyzerReferences(WorkspaceSession session)
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
                WorkspaceDiagnostics: session.WorkspaceDiagnostics.ToArray());
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
            WorkspaceDiagnostics: session.WorkspaceDiagnostics.ToArray());
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
                Name: project.Name,
                FilePath: project.FilePath ?? "unknown",
                DocumentCount: project.Documents.Count(),
                ProjectReferences: project.ProjectReferences
                    .Select(reference => solution.GetProject(reference.ProjectId)?.Name ?? "unknown")
                    .ToList(),
                TargetFrameworks: Helpers.ProjectMetadataParser.GetTargetFrameworks(project, projectDoc, _logger),
                IsTestProject: Helpers.ProjectMetadataParser.IsTestProject(projectDoc),
                AssemblyName: Helpers.ProjectMetadataParser.GetAssemblyName(project),
                OutputType: Helpers.ProjectMetadataParser.GetOutputType(projectDoc));
        }).ToImmutableArray();
    }

    private sealed class WorkspaceSession(string workspaceId) : IDisposable
    {
        private int _version;

        public string WorkspaceId { get; } = workspaceId;
        public SemaphoreSlim LoadLock { get; } = new(1, 1);
        public ConcurrentQueue<DiagnosticDto> WorkspaceDiagnostics { get; set; } = new();
        public ImmutableArray<ProjectStatusDto> ProjectStatuses { get; set; } = ImmutableArray<ProjectStatusDto>.Empty;
        public MSBuildWorkspace? Workspace { get; set; }
        public string? LoadedPath { get; set; }
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
