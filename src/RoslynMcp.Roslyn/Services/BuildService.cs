using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace RoslynMcp.Roslyn.Services;

public sealed class BuildService : IBuildService, IDisposable
{
    private readonly SemaphoreSlim _globalCommandGate;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _workspaceCommandGates = new(StringComparer.Ordinal);

    private readonly IWorkspaceManager _workspaceManager;
    private readonly IDotnetCommandRunner _commandRunner;
    private readonly ILogger<BuildService> _logger;
    private readonly ValidationServiceOptions _options;

    public BuildService(
        IWorkspaceManager workspaceManager,
        IDotnetCommandRunner commandRunner,
        ILogger<BuildService> logger,
        ValidationServiceOptions? options = null)
    {
        _workspaceManager = workspaceManager;
        _commandRunner = commandRunner;
        _logger = logger;
        _options = options ?? new ValidationServiceOptions();
        var globalLimit = Math.Clamp(Environment.ProcessorCount / 4, 1, 4);
        _globalCommandGate = new SemaphoreSlim(globalLimit, globalLimit);
    }

    public async Task<BuildResultDto> BuildWorkspaceAsync(string workspaceId, CancellationToken ct)
    {
        var status = _workspaceManager.GetStatus(workspaceId);
        var targetPath = status.LoadedPath ?? throw new InvalidOperationException($"Workspace '{workspaceId}' is not loaded.");
        var execution = await RunDotnetCommandAsync(
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
        var project = ResolveProject(workspaceId, projectName);
        var execution = await RunDotnetCommandAsync(
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

    private async Task<CommandExecutionDto> RunDotnetCommandAsync(
        string workspaceId,
        string targetPath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var workspaceGate = _workspaceCommandGates.GetOrAdd(workspaceId, static _ => new SemaphoreSlim(1, 1));
        await _globalCommandGate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            await workspaceGate.WaitAsync(ct).ConfigureAwait(false);

            try
            {
                var workingDirectory = GetWorkingDirectory(targetPath);
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(timeout);

                CommandExecutionDto execution;
                try
                {
                    execution = await _commandRunner.RunAsync(workingDirectory, targetPath, arguments, timeoutCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeoutCts.IsCancellationRequested)
                {
                    throw new TimeoutException(
                        $"The command 'dotnet {string.Join(" ", arguments)}' exceeded the timeout of {timeout.TotalMinutes:F1} minute(s).");
                }

                _logger.LogInformation(
                    "Executed dotnet command for {TargetPath}: {Arguments} (ExitCode={ExitCode})",
                    targetPath,
                    string.Join(" ", arguments),
                    execution.ExitCode);

                return execution;
            }
            finally
            {
                workspaceGate.Release();
            }
        }
        finally
        {
            _globalCommandGate.Release();
        }
    }

    private ProjectStatusDto ResolveProject(string workspaceId, string projectName)
    {
        var project = _workspaceManager.GetStatus(workspaceId).Projects
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Name, projectName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate.FilePath, projectName, StringComparison.OrdinalIgnoreCase));

        return project ?? throw new InvalidOperationException(
            $"Project '{projectName}' was not found in workspace '{workspaceId}'.");
    }

    private static string GetWorkingDirectory(string targetPath)
    {
        if (Directory.Exists(targetPath))
        {
            return targetPath;
        }

        var directory = Path.GetDirectoryName(targetPath);
        return string.IsNullOrWhiteSpace(directory) ? Environment.CurrentDirectory : directory;
    }

    public void Dispose()
    {
        _globalCommandGate.Dispose();
        foreach (var kvp in _workspaceCommandGates)
        {
            kvp.Value.Dispose();
        }
        _workspaceCommandGates.Clear();
    }
}
