using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Shared infrastructure for executing <c>dotnet</c> CLI commands with global and per-workspace
/// concurrency gates and timeout enforcement. Used by <see cref="BuildService"/>
/// and <see cref="TestRunnerService"/> to eliminate duplicated gating and process logic.
/// </summary>
public sealed class GatedCommandExecutor : IGatedCommandExecutor
{
    private static readonly Action<ILogger, string, string, int, Exception?> LogCommandExecuted =
        LoggerMessage.Define<string, string, int>(
            LogLevel.Information, new EventId(1, nameof(LogCommandExecuted)),
            "Executed dotnet command for {TargetPath}: {Arguments} (ExitCode={ExitCode})");

    private readonly SemaphoreSlim _globalCommandGate;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _workspaceCommandGates = new(StringComparer.Ordinal);

    private readonly IWorkspaceManager _workspaceManager;
    private readonly IDotnetCommandRunner _commandRunner;
    private readonly ILogger<GatedCommandExecutor> _logger;

    public GatedCommandExecutor(
        IWorkspaceManager workspaceManager,
        IDotnetCommandRunner commandRunner,
        ILogger<GatedCommandExecutor> logger)
    {
        _workspaceManager = workspaceManager;
        _commandRunner = commandRunner;
        _logger = logger;
        var globalLimit = Math.Clamp(Environment.ProcessorCount / 4, 1, 4);
        _globalCommandGate = new SemaphoreSlim(globalLimit, globalLimit);
    }

    public Task<CommandExecutionDto> ExecuteAsync(
        string workspaceId,
        string targetPath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken ct)
        => ExecuteAsync(workspaceId, targetPath, arguments, timeout, earlyKillPatterns: null, ct);

    public async Task<CommandExecutionDto> ExecuteAsync(
        string workspaceId,
        string targetPath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        IReadOnlyList<EarlyKillPattern>? earlyKillPatterns,
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
                    execution = await _commandRunner
                        .RunAsync(workingDirectory, targetPath, arguments, earlyKillPatterns, timeoutCts.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeoutCts.IsCancellationRequested)
                {
                    throw new TimeoutException(
                        $"The command 'dotnet {string.Join(" ", arguments)}' exceeded the timeout of {timeout.TotalMinutes:F1} minute(s).");
                }

                LogCommandExecuted(_logger, targetPath, string.Join(" ", arguments), execution.ExitCode, null);

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

    public ProjectStatusDto ResolveProject(string workspaceId, string projectName)
    {
        var project = _workspaceManager.GetStatus(workspaceId).Projects
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Name, projectName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate.FilePath, projectName, StringComparison.OrdinalIgnoreCase));

        return project ?? throw new InvalidOperationException(
            $"Project '{projectName}' was not found in workspace '{workspaceId}'.");
    }

    internal static string GetWorkingDirectory(string targetPath)
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
