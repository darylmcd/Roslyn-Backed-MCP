using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Immutable;
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

    private readonly ILogger<WorkspaceManager> _logger;
    private readonly IPreviewStore _previewStore;
    private readonly IFileWatcherService _fileWatcher;
    private readonly WorkspaceManagerOptions _options;
    private readonly ConcurrentDictionary<string, WorkspaceSession> _sessions = new(StringComparer.Ordinal);

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
    }

    public async Task<WorkspaceStatusDto> LoadAsync(string path, CancellationToken ct)
    {
        var fullPath = ValidateWorkspacePath(path);
        if (_sessions.Count >= _options.MaxConcurrentWorkspaces)
        {
            throw new InvalidOperationException(
                $"The server is already tracking {_sessions.Count} workspaces. Close an existing workspace before loading another.");
        }

        var workspaceId = Guid.NewGuid().ToString("N");
        var session = new WorkspaceSession(workspaceId);

        try
        {
            await LoadIntoSessionAsync(session, fullPath, ct).ConfigureAwait(false);
            if (!_sessions.TryAdd(workspaceId, session))
            {
                throw new InvalidOperationException($"Workspace '{workspaceId}' already exists.");
            }

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
    }

    public async Task<WorkspaceStatusDto> ReloadAsync(string workspaceId, CancellationToken ct)
    {
        var session = GetRequiredSession(workspaceId);
        await LoadIntoSessionAsync(session, session.LoadedPath ?? throw new InvalidOperationException(
            $"Workspace '{workspaceId}' is not loaded."),
            ct).ConfigureAwait(false);
        _fileWatcher.ClearStale(workspaceId);
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
        _logger.LogInformation("Closed workspace {WorkspaceId}", workspaceId);
        return true;
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
                .Take(_options.MaxSourceGeneratedDocuments - results.Count)
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
                        projectResults.AddRange(Directory.EnumerateFiles(objDirectory, "*.g.cs", SearchOption.AllDirectories)
                            .Take(_options.MaxSourceGeneratedDocuments - results.Count)
                            .Select(filePath => new GeneratedDocumentDto(
                                ProjectName: project.Name,
                                HintName: Path.GetFileName(filePath),
                                FilePath: filePath)));
                    }
                }
            }

            results.AddRange(projectResults);
        }

        return results;
    }

    public async Task<string?> GetSourceTextAsync(string workspaceId, string filePath, CancellationToken ct)
    {
        var solution = GetCurrentSolution(workspaceId);
        var document = Helpers.SymbolResolver.FindDocument(solution, filePath);
        if (document is null) return null;

        var text = await document.GetTextAsync(ct).ConfigureAwait(false);
        return text.ToString();
    }

    public int GetCurrentVersion(string workspaceId)
    {
        return GetRequiredSession(workspaceId).Version;
    }

    public Solution GetCurrentSolution(string workspaceId)
    {
        var session = GetRequiredSession(workspaceId);
        return session.Workspace?.CurrentSolution
            ?? throw new InvalidOperationException($"Workspace '{workspaceId}' is not loaded.");
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
            _logger.LogInformation(
                "Applied workspace changes to {WorkspaceId}, new version {Version}",
                workspaceId,
                session.Version);
        }
        else
        {
            _logger.LogWarning("Failed to apply workspace changes for {WorkspaceId}", workspaceId);
        }

        return result;
    }

    public void Dispose()
    {
        _fileWatcher.Dispose();
        foreach (var session in _sessions.Values)
        {
            session.Dispose();
        }
        _sessions.Clear();
    }

    private async Task LoadIntoSessionAsync(WorkspaceSession session, string path, CancellationToken ct)
    {
        await session.LoadLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            session.Workspace?.Dispose();
            session.Workspace = MSBuildWorkspace.Create();
            session.WorkspaceDiagnostics = new ConcurrentQueue<DiagnosticDto>();
            session.ProjectStatuses = ImmutableArray<ProjectStatusDto>.Empty;
            var fullPath = path;

            session.Workspace.RegisterWorkspaceFailedHandler(args =>
            {
                var severity = args.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure ? "Error" : "Warning";
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

            session.ProjectStatuses = BuildProjectStatuses(session.Workspace.CurrentSolution);
            session.LoadedPath = fullPath;
            session.LoadedAtUtc = DateTimeOffset.UtcNow;
            session.IncrementVersion();
            _previewStore.InvalidateAll(session.WorkspaceId);

            _logger.LogInformation(
                "Loaded workspace {WorkspaceId} from {Path}, version {Version}",
                session.WorkspaceId,
                fullPath,
                session.Version);
        }
        finally
        {
            session.LoadLock.Release();
        }
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
        if (_sessions.TryGetValue(workspaceId, out var session))
        {
            return session;
        }

        throw new KeyNotFoundException($"Workspace '{workspaceId}' was not found.");
    }

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
                TargetFrameworks: Helpers.ProjectMetadataParser.GetTargetFrameworks(projectDoc),
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
        public int Version => Volatile.Read(ref _version);
        public int IncrementVersion() => Interlocked.Increment(ref _version);

        public void Dispose()
        {
            Workspace?.Dispose();
            LoadLock.Dispose();
        }
    }
}
