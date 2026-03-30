using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace RoslynMcp.Roslyn.Services;

public sealed class TestRunnerService : ITestRunnerService, IDisposable
{
    private readonly SemaphoreSlim _globalCommandGate;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _workspaceCommandGates = new(StringComparer.Ordinal);

    private readonly IWorkspaceManager _workspaceManager;
    private readonly IDotnetCommandRunner _commandRunner;
    private readonly ILogger<TestRunnerService> _logger;
    private readonly ValidationServiceOptions _options;

    public TestRunnerService(
        IWorkspaceManager workspaceManager,
        IDotnetCommandRunner commandRunner,
        ILogger<TestRunnerService> logger,
        ValidationServiceOptions? options = null)
    {
        _workspaceManager = workspaceManager;
        _commandRunner = commandRunner;
        _logger = logger;
        _options = options ?? new ValidationServiceOptions();
        var globalLimit = Math.Clamp(Environment.ProcessorCount / 4, 1, 4);
        _globalCommandGate = new SemaphoreSlim(globalLimit, globalLimit);
    }

    public async Task<TestRunResultDto> RunTestsAsync(string workspaceId, string? projectName, string? filter, CancellationToken ct)
    {
        var status = _workspaceManager.GetStatus(workspaceId);

        if (projectName is not null)
        {
            var resolved = ResolveProject(workspaceId, projectName);
            if (!resolved.IsTestProject)
            {
                throw new InvalidOperationException(
                    $"Project '{projectName}' is not a test project. " +
                    $"Available test projects: {string.Join(", ", status.Projects.Where(p => p.IsTestProject).Select(p => p.Name))}");
            }
        }
        else if (!status.Projects.Any(p => p.IsTestProject))
        {
            throw new InvalidOperationException(
                $"No test projects found in workspace '{workspaceId}'. " +
                "Ensure the workspace contains projects with a test SDK reference (e.g., MSTest, xUnit, NUnit).");
        }

        var targetPath = projectName is null
            ? status.LoadedPath
            : ResolveProject(workspaceId, projectName).FilePath;

        if (string.IsNullOrWhiteSpace(targetPath))
        {
            throw new InvalidOperationException($"Workspace '{workspaceId}' is not loaded.");
        }

        var resultsDirectory = Path.Combine(Path.GetTempPath(), "RoslynMcpTestResults", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(resultsDirectory);
        var trxPath = Path.Combine(resultsDirectory, "results.trx");

        try
        {
            var arguments = new List<string>
            {
                "test",
                targetPath,
                "--nologo",
                "--logger",
                $"trx;LogFileName={Path.GetFileName(trxPath)}",
                "--results-directory",
                resultsDirectory
            };

            if (!string.IsNullOrWhiteSpace(filter))
            {
                arguments.Add("--filter");
                arguments.Add(filter);
            }

            var execution = await RunDotnetCommandAsync(
                workspaceId,
                targetPath,
                arguments,
                _options.TestTimeout,
                ct).ConfigureAwait(false);
            return DotnetOutputParser.ParseTestRun(execution, trxPath);
        }
        finally
        {
            if (Directory.Exists(resultsDirectory))
            {
                Directory.Delete(resultsDirectory, recursive: true);
            }
        }
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
