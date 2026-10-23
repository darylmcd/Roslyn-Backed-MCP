using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

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
        _workspaceManager.WorkspaceClosed += RemoveWorkspaceGate;
    }

    /// <summary>
    /// Prunes the per-workspace command gate when a workspace is closed or LRU-evicted, so the
    /// <see cref="_workspaceCommandGates"/> dictionary stays bounded across repeated
    /// load/close cycles instead of accumulating one <see cref="SemaphoreSlim"/> per distinct
    /// workspaceId for the process lifetime.
    /// </summary>
    /// <remarks>
    /// Deliberately does NOT call <see cref="SemaphoreSlim.Dispose"/> on the removed semaphore:
    /// <see cref="ExecuteAsync(string, string, IReadOnlyList{string}, TimeSpan, IReadOnlyList{EarlyKillPattern}?, CancellationToken)"/>
    /// can be mid-flight against this gate for the closing workspace (the command-execution gate
    /// and the workspace-lifecycle gate are independent locking systems), and disposing a
    /// <see cref="SemaphoreSlim"/> concurrently with an in-flight <c>WaitAsync</c>/<c>Release</c>
    /// is documented as unsafe. Removing the dictionary entry alone satisfies bounded growth; the
    /// orphaned semaphore is GC'd once the in-flight caller's local reference goes out of scope.
    /// </remarks>
    private void RemoveWorkspaceGate(string workspaceId) => _workspaceCommandGates.TryRemove(workspaceId, out _);

    public Task<CommandExecutionDto> ExecuteAsync(
        string workspaceId,
        string targetPath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken ct)
        => ExecuteAsync(workspaceId, targetPath, arguments, timeout, earlyKillPatterns: null, ct);

    public Task<CommandExecutionDto> ExecuteAsync(
        string workspaceId,
        string targetPath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        IReadOnlyList<EarlyKillPattern>? earlyKillPatterns,
        CancellationToken ct)
    {
        return ExecuteCoreAsync(workspaceId, targetPath, arguments, timeout, earlyKillPatterns,
            (execution, _) => Task.FromResult(MsBuildCommandProperties.Redact(execution, arguments)), null, ct);
    }

    public Task<CommandExecutionDto> ExecuteAuthorizedAsync(
        string workspaceId, string targetPath, IReadOnlyList<string> arguments, TimeSpan timeout,
        Func<CancellationToken, Task> authorizeStart, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(authorizeStart);
        return ExecuteCoreAsync(workspaceId, targetPath, arguments, timeout, null,
            (execution, _) => Task.FromResult(MsBuildCommandProperties.Redact(execution, arguments)), authorizeStart, ct);
    }

    public Task<TestRunResultDto> ExecuteTestAsync(
        string workspaceId, string targetPath, IReadOnlyList<string> arguments,
        Func<CommandExecutionDto, CancellationToken, Task<TestRunResultDto>> collectResults,
        TimeSpan timeout, IReadOnlyList<EarlyKillPattern>? earlyKillPatterns,
        Func<CancellationToken, Task> authorizeStart, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(authorizeStart);
        ArgumentNullException.ThrowIfNull(collectResults);
        return ExecuteCoreAsync(workspaceId, targetPath, arguments, timeout, earlyKillPatterns,
            async (execution, token) => MsBuildCommandProperties.Redact(await collectResults(execution, token).ConfigureAwait(false), arguments), authorizeStart, ct);
    }

    public Task<BuildCommandRun> ExecuteBuildAsync(BuildCommandPlan plan, TimeSpan timeout, CancellationToken ct) =>
        ExecuteCoreAsync(plan.WorkspaceId, plan.TargetPath, plan.Arguments, timeout, null, (execution, _) =>
        {
            var redactor = new MsBuildCommandProperties.Redactor(plan.Arguments);
            var diagnostics = DotnetOutputParser.ParseBuildDiagnostics(execution.StdOut + Environment.NewLine + execution.StdErr);
            return Task.FromResult(new BuildCommandRun(plan with
            {
                TargetPath = redactor.Text(plan.TargetPath),
                Arguments = plan.Arguments.Select(redactor.Text).ToArray()
            }, redactor.Execution(execution), execution.DurationMs)
            {
                ParsingState = new BuildCommandParsingState(diagnostics.ToImmutableArray(), plan.Arguments.ToImmutableArray())
            });
        }, null, ct);

    private async Task<TResult> ExecuteCoreAsync<TResult>(
        string workspaceId, string targetPath, IReadOnlyList<string> arguments, TimeSpan timeout,
        IReadOnlyList<EarlyKillPattern>? earlyKillPatterns, Func<CommandExecutionDto, CancellationToken, Task<TResult>> project,
        Func<CancellationToken, Task>? authorizeStart, CancellationToken ct)
    {
        var redactor = new MsBuildCommandProperties.Redactor(arguments);
        var workspaceGate = _workspaceCommandGates.GetOrAdd(workspaceId, static _ => new SemaphoreSlim(1, 1));
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        var globalGateAcquired = false;
        var workspaceGateAcquired = false;
        var authorizing = false;

        try
        {
            await _globalCommandGate.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            globalGateAcquired = true;
            await workspaceGate.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            workspaceGateAcquired = true;

            if (authorizeStart is not null)
            {
                authorizing = true;
                await authorizeStart(timeoutCts.Token).ConfigureAwait(false);
                timeoutCts.Token.ThrowIfCancellationRequested();
                authorizing = false;
            }

            var workingDirectory = GetWorkingDirectory(targetPath);
            var execution = await _commandRunner
                .RunAsync(workingDirectory, targetPath, arguments, earlyKillPatterns, timeoutCts.Token)
                .ConfigureAwait(false);

            LogCommandExecuted(_logger, redactor.Text(targetPath), redactor.Text(string.Join(" ", arguments)), execution.ExitCode, null);

            return await project(execution, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException oce) when (!ct.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            // The caught OCE is carried as InnerException so cancellation provenance (which token
            // fired, original stack trace) survives the reclassification for logs/diagnostics.
            throw new TimeoutException(
                $"The command 'dotnet {redactor.Text(string.Join(" ", arguments))}' did not complete within the total timeout budget of {timeout.TotalMinutes:F1} minute(s), including queue wait.",
                redactor.HasValues ? new OperationCanceledException("The command timeout elapsed.", oce.CancellationToken) : oce);
        }
        catch (OperationCanceledException exception) when (redactor.HasValues)
        {
            throw new OperationCanceledException(redactor.Text(exception.Message), exception.CancellationToken);
        }
        catch (Exception exception) when (authorizing)
        {
            throw redactor.Failure(exception);
        }
        catch (Exception exception) when (redactor.HasValues)
        {
            // Exception messages, Data, and nested chains can echo argv or raw process output.
            // Preserve the failure category without exporting untrusted diagnostic payloads.
            throw new InvalidOperationException("Command execution or result processing failed (" + exception.GetType().Name + ").");
        }
        finally
        {
            if (workspaceGateAcquired)
            {
                workspaceGate.Release();
            }

            if (globalGateAcquired)
            {
                _globalCommandGate.Release();
            }
        }
    }

    public ProjectStatusDto ResolveProject(string workspaceId, string projectName) =>
        WorkspaceProjectResolver.Resolve(_workspaceManager, workspaceId, projectName);

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
        _workspaceManager.WorkspaceClosed -= RemoveWorkspaceGate;
        _globalCommandGate.Dispose();
        foreach (var kvp in _workspaceCommandGates)
        {
            kvp.Value.Dispose();
        }
        _workspaceCommandGates.Clear();
    }
}
