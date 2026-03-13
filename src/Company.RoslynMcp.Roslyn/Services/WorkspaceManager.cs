using Company.RoslynMcp.Core.Models;
using Company.RoslynMcp.Core.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;

namespace Company.RoslynMcp.Roslyn.Services;

public sealed class WorkspaceManager : IWorkspaceManager, IDisposable
{
    private readonly ILogger<WorkspaceManager> _logger;
    private readonly IPreviewStore _previewStore;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private MSBuildWorkspace? _workspace;
    private string? _loadedPath;
    private int _version;
    private readonly List<string> _loadWarnings = new();

    public int CurrentVersion => _version;
    public bool IsLoaded => _workspace is not null && _loadedPath is not null;

    public WorkspaceManager(ILogger<WorkspaceManager> logger, IPreviewStore previewStore)
    {
        _logger = logger;
        _previewStore = previewStore;
    }

    public async Task<WorkspaceStatusDto> LoadAsync(string path, CancellationToken ct)
    {
        await _loadLock.WaitAsync(ct);
        try
        {
            _workspace?.Dispose();
            _workspace = MSBuildWorkspace.Create();
            _loadWarnings.Clear();

            _workspace.RegisterWorkspaceFailedHandler(args =>
            {
                _loadWarnings.Add(args.Diagnostic.Message);
                _logger.LogWarning("Workspace diagnostic: {Message}", args.Diagnostic.Message);
            });

            var fullPath = Path.GetFullPath(path);

            if (fullPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
                fullPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
            {
                await _workspace.OpenSolutionAsync(fullPath, cancellationToken: ct);
            }
            else if (fullPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                await _workspace.OpenProjectAsync(fullPath, cancellationToken: ct);
            }
            else
            {
                throw new ArgumentException($"Path must end with .sln, .slnx, or .csproj: {path}");
            }

            _loadedPath = fullPath;
            _version++;
            _previewStore.InvalidateAll();
            _logger.LogInformation("Loaded workspace from {Path}, version {Version}", fullPath, _version);

            return GetStatus();
        }
        finally
        {
            _loadLock.Release();
        }
    }

    public async Task<WorkspaceStatusDto> ReloadAsync(CancellationToken ct)
    {
        if (_loadedPath is null)
            throw new InvalidOperationException("No workspace is loaded. Call workspace_load first.");

        return await LoadAsync(_loadedPath, ct);
    }

    public WorkspaceStatusDto GetStatus()
    {
        if (_workspace is null || _loadedPath is null)
        {
            return new WorkspaceStatusDto(
                SolutionPath: null,
                WorkspaceVersion: _version,
                Projects: [],
                IsLoaded: false,
                LoadWarnings: null);
        }

        var solution = _workspace.CurrentSolution;
        var projects = solution.Projects.Select(p => new ProjectStatusDto(
            Name: p.Name,
            FilePath: p.FilePath ?? "unknown",
            DocumentCount: p.Documents.Count(),
            ProjectReferences: p.ProjectReferences
                .Select(pr => solution.GetProject(pr.ProjectId)?.Name ?? "unknown")
                .ToList(),
            TargetFramework: GetTargetFramework(p)
        )).ToList();

        return new WorkspaceStatusDto(
            SolutionPath: _loadedPath,
            WorkspaceVersion: _version,
            Projects: projects,
            IsLoaded: true,
            LoadWarnings: _loadWarnings.Count > 0 ? _loadWarnings.ToList() : null);
    }

    public Solution GetCurrentSolution()
    {
        if (_workspace is null)
            throw new InvalidOperationException("No workspace is loaded. Call workspace_load first.");
        return _workspace.CurrentSolution;
    }

    public bool TryApplyChanges(Solution newSolution)
    {
        if (_workspace is null)
            throw new InvalidOperationException("No workspace is loaded.");

        var result = _workspace.TryApplyChanges(newSolution);
        if (result)
        {
            _version++;
            _previewStore.InvalidateAll();
            _logger.LogInformation("Applied workspace changes, new version {Version}", _version);
        }
        else
        {
            _logger.LogWarning("Failed to apply workspace changes");
        }
        return result;
    }

    public void IncrementVersion()
    {
        _version++;
        _previewStore.InvalidateAll();
    }

    public void Dispose()
    {
        _workspace?.Dispose();
        _loadLock.Dispose();
    }

    private static string GetTargetFramework(Project project)
    {
        var parseOptions = project.ParseOptions;
        if (parseOptions is Microsoft.CodeAnalysis.CSharp.CSharpParseOptions csharpOptions)
        {
            return $"C# {csharpOptions.LanguageVersion}";
        }
        return "unknown";
    }
}
