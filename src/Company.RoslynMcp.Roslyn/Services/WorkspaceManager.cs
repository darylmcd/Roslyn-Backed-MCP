using Company.RoslynMcp.Core.Models;
using Company.RoslynMcp.Core.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Xml.Linq;

namespace Company.RoslynMcp.Roslyn.Services;

public sealed class WorkspaceManager : IWorkspaceManager, IDisposable
{
    private readonly ILogger<WorkspaceManager> _logger;
    private readonly IPreviewStore _previewStore;
    private readonly ConcurrentDictionary<string, WorkspaceSession> _sessions = new(StringComparer.Ordinal);

    public WorkspaceManager(ILogger<WorkspaceManager> logger, IPreviewStore previewStore)
    {
        _logger = logger;
        _previewStore = previewStore;
    }

    public async Task<WorkspaceStatusDto> LoadAsync(string path, CancellationToken ct)
    {
        var workspaceId = Guid.NewGuid().ToString("N");
        var session = new WorkspaceSession(workspaceId);
        _sessions[workspaceId] = session;
        await LoadIntoSessionAsync(session, path, ct);
        return BuildStatus(session);
    }

    public async Task<WorkspaceStatusDto> ReloadAsync(string workspaceId, CancellationToken ct)
    {
        var session = GetRequiredSession(workspaceId);
        await LoadIntoSessionAsync(session, session.LoadedPath ?? throw new InvalidOperationException(
            $"Workspace '{workspaceId}' is not loaded."),
            ct);
        return BuildStatus(session);
    }

    public WorkspaceStatusDto GetStatus(string workspaceId)
    {
        return BuildStatus(GetRequiredSession(workspaceId));
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
            var generatedDocuments = await project.GetSourceGeneratedDocumentsAsync(ct);
            var projectResults = generatedDocuments.Select(document => new GeneratedDocumentDto(
                ProjectName: project.Name,
                HintName: document.Name,
                FilePath: document.FilePath ?? document.Name)).ToList();

            if (projectResults.Count == 0 && !string.IsNullOrWhiteSpace(project.FilePath))
            {
                var projectDirectory = Path.GetDirectoryName(project.FilePath);
                if (!string.IsNullOrWhiteSpace(projectDirectory))
                {
                    var objDirectory = Path.Combine(projectDirectory, "obj");
                    if (Directory.Exists(objDirectory))
                    {
                        projectResults.AddRange(Directory.EnumerateFiles(objDirectory, "*.g.cs", SearchOption.AllDirectories)
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
            session.Version++;
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
        foreach (var session in _sessions.Values)
        {
            session.Dispose();
        }
    }

    private async Task LoadIntoSessionAsync(WorkspaceSession session, string path, CancellationToken ct)
    {
        await session.LoadLock.WaitAsync(ct);
        try
        {
            session.Workspace?.Dispose();
            session.Workspace = MSBuildWorkspace.Create();
            session.WorkspaceDiagnostics = new ConcurrentQueue<DiagnosticDto>();
            session.ProjectStatuses = ImmutableArray<ProjectStatusDto>.Empty;

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
                _logger.LogWarning(
                    "Workspace {WorkspaceId} diagnostic: {Message}",
                    session.WorkspaceId,
                    args.Diagnostic.Message);
            });

            var fullPath = Path.GetFullPath(path);

            if (fullPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
                fullPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
            {
                await session.Workspace.OpenSolutionAsync(fullPath, cancellationToken: ct);
            }
            else if (fullPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                await session.Workspace.OpenProjectAsync(fullPath, cancellationToken: ct);
            }
            else
            {
                throw new ArgumentException($"Path must end with .sln, .slnx, or .csproj: {path}");
            }

            session.ProjectStatuses = BuildProjectStatuses(session.Workspace.CurrentSolution);
            session.LoadedPath = fullPath;
            session.LoadedAtUtc = DateTimeOffset.UtcNow;
            session.Version++;
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

    private static IReadOnlyList<string> GetTargetFrameworks(string? projectFilePath)
    {
        var document = LoadProjectDocument(projectFilePath);
        if (document is null)
        {
            return ["unknown"];
        }

        var targetFramework = document.Descendants("TargetFramework").Select(element => element.Value.Trim());
        var targetFrameworks = document.Descendants("TargetFrameworks")
            .SelectMany(element => element.Value
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var allFrameworks = targetFramework.Concat(targetFrameworks).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return allFrameworks.Count > 0 ? allFrameworks : ["unknown"];
    }

    private static bool IsTestProject(string? projectFilePath)
    {
        var document = LoadProjectDocument(projectFilePath);
        if (document is null)
        {
            return false;
        }

        var isTestProject = document.Descendants("IsTestProject").FirstOrDefault()?.Value;
        return bool.TryParse(isTestProject, out var parsed) && parsed;
    }

    private static string GetOutputType(string? projectFilePath)
    {
        var document = LoadProjectDocument(projectFilePath);
        return document?.Descendants("OutputType").FirstOrDefault()?.Value.Trim() ?? "Library";
    }

    private static string GetAssemblyName(Project project)
    {
        if (project.CompilationOptions?.AssemblyIdentityComparer is not null && !string.IsNullOrWhiteSpace(project.AssemblyName))
        {
            return project.AssemblyName;
        }

        return project.Name;
    }

    private static XDocument? LoadProjectDocument(string? projectFilePath)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath) || !File.Exists(projectFilePath))
        {
            return null;
        }

        try
        {
            return XDocument.Load(projectFilePath);
        }
        catch
        {
            return null;
        }
    }

    private static ImmutableArray<ProjectStatusDto> BuildProjectStatuses(Solution solution)
    {
        return solution.Projects.Select(project => new ProjectStatusDto(
                Name: project.Name,
                FilePath: project.FilePath ?? "unknown",
                DocumentCount: project.Documents.Count(),
                ProjectReferences: project.ProjectReferences
                    .Select(reference => solution.GetProject(reference.ProjectId)?.Name ?? "unknown")
                    .ToList(),
                TargetFrameworks: GetTargetFrameworks(project.FilePath),
                IsTestProject: IsTestProject(project.FilePath),
                AssemblyName: GetAssemblyName(project),
                OutputType: GetOutputType(project.FilePath)))
            .ToImmutableArray();
    }

    private sealed class WorkspaceSession(string workspaceId) : IDisposable
    {
        public string WorkspaceId { get; } = workspaceId;
        public SemaphoreSlim LoadLock { get; } = new(1, 1);
        public ConcurrentQueue<DiagnosticDto> WorkspaceDiagnostics { get; set; } = new();
        public ImmutableArray<ProjectStatusDto> ProjectStatuses { get; set; } = ImmutableArray<ProjectStatusDto>.Empty;
        public MSBuildWorkspace? Workspace { get; set; }
        public string? LoadedPath { get; set; }
        public DateTimeOffset LoadedAtUtc { get; set; } = DateTimeOffset.UtcNow;
        public int Version { get; set; }

        public void Dispose()
        {
            Workspace?.Dispose();
            LoadLock.Dispose();
        }
    }
}
