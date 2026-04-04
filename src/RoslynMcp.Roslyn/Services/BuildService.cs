using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

public sealed class BuildService : IBuildService
{
    private readonly IWorkspaceManager _workspaceManager;
    private readonly IGatedCommandExecutor _executor;
    private readonly ILogger<BuildService> _logger;
    private readonly ValidationServiceOptions _options;

    public BuildService(
        IWorkspaceManager workspaceManager,
        IGatedCommandExecutor executor,
        ILogger<BuildService> logger,
        ValidationServiceOptions? options = null)
    {
        _workspaceManager = workspaceManager;
        _executor = executor;
        _logger = logger;
        _options = options ?? new ValidationServiceOptions();
    }

    public async Task<BuildResultDto> BuildWorkspaceAsync(string workspaceId, CancellationToken ct)
    {
        var status = await _workspaceManager.GetStatusAsync(workspaceId, ct).ConfigureAwait(false);
        var targetPath = status.LoadedPath ?? throw new InvalidOperationException($"Workspace '{workspaceId}' is not loaded.");
        var execution = await _executor.ExecuteAsync(
            workspaceId,
            targetPath,
            ["build", targetPath, "--nologo"],
            _options.BuildTimeout,
            ct).ConfigureAwait(false);
        var diagnostics = DotnetOutputParser.ParseBuildDiagnostics($"{execution.StdOut}{Environment.NewLine}{execution.StdErr}");

        return new BuildResultDto(
            execution,
            diagnostics,
            diagnostics.Count(d => d.Severity == "Error"),
            diagnostics.Count(d => d.Severity == "Warning"));
    }

    public async Task<BuildResultDto> BuildProjectAsync(string workspaceId, string projectName, CancellationToken ct)
    {
        var project = _executor.ResolveProject(workspaceId, projectName);
        var execution = await _executor.ExecuteAsync(
            workspaceId,
            project.FilePath,
            ["build", project.FilePath, "--nologo"],
            _options.BuildTimeout,
            ct).ConfigureAwait(false);
        var diagnostics = DotnetOutputParser.ParseBuildDiagnostics($"{execution.StdOut}{Environment.NewLine}{execution.StdErr}");

        return new BuildResultDto(
            execution,
            diagnostics,
            diagnostics.Count(d => d.Severity == "Error"),
            diagnostics.Count(d => d.Severity == "Warning"));
    }
}
