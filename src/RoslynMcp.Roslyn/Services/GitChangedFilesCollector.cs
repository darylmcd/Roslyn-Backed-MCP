using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Owns Git invocation, termination, and redirected streams for validation scope collection.</summary>
internal sealed class GitChangedFilesCollector
{
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(10);
    private readonly Func<Exception, WorkspaceValidationFailureOperation, WorkspaceValidationFailureDetail> _createUnexpectedFailure;
    private readonly ILogger? _logger;
    private readonly Action<Process> _killProcessTree;
    private readonly Func<Process, CancellationToken, Task> _waitForGitExitAsync;
    private readonly Action<ProcessStartInfo>? _configureStartInfo;
    private readonly Func<StreamReader, CancellationToken, Task<string>> _readOutputAsync;

    internal GitChangedFilesCollector(
        Func<Exception, WorkspaceValidationFailureOperation, WorkspaceValidationFailureDetail> createUnexpectedFailure,
        ILogger? logger = null,
        Action<Process>? killProcessTree = null,
        Func<Process, CancellationToken, Task>? waitForGitExitAsync = null,
        Action<ProcessStartInfo>? configureStartInfo = null,
        Func<StreamReader, CancellationToken, Task<string>>? readOutputAsync = null)
    {
        _createUnexpectedFailure = createUnexpectedFailure;
        _logger = logger;
        _killProcessTree = killProcessTree ?? (static process => process.Kill(entireProcessTree: true));
        _waitForGitExitAsync = waitForGitExitAsync ?? (static (process, token) => process.WaitForExitAsync(token));
        _configureStartInfo = configureStartInfo;
        _readOutputAsync = readOutputAsync ?? (static (reader, token) => reader.ReadToEndAsync(token));
    }

    internal async Task<(string StdOut, IReadOnlyList<string> Warnings, bool TimedOut)> CollectAsync(
        string solutionDirectory,
        TimeSpan timeout,
        CancellationToken ct)
    {
        // Fast pre-check: a `.git` directory / file (submodule, worktree) must exist somewhere
        // at or above the solution directory. If none, we're demonstrably outside a repo and
        // can skip the git invocation entirely — saves ~20 ms and gives a precise warning
        // instead of the noisier "git exited 128" message.
        if (!IsInsideGitRepository(solutionDirectory))
        {
            return (string.Empty, new[]
            {
                "git repository not found at or above the loaded workspace; validated full workspace."
            }, false);
        }

        ct.ThrowIfCancellationRequested();
        ProcessStartInfo startInfo;
        try
        {
            startInfo = new ProcessStartInfo
            {
                FileName = "git",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            RemoveAmbientGitRepositoryOverrides(startInfo);
            startInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";
            startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
            startInfo.ArgumentList.Add("-C");
            startInfo.ArgumentList.Add(solutionDirectory);
            startInfo.ArgumentList.Add("status");
            startInfo.ArgumentList.Add("--porcelain=v1");
            startInfo.ArgumentList.Add("-z");
            startInfo.ArgumentList.Add("-uall");
            _configureStartInfo?.Invoke(startInfo);
        }
        catch (Exception ex)
        {
            return (string.Empty, new[]
            {
                _createUnexpectedFailure(ex, WorkspaceValidationFailureOperation.GitConfiguration).Summary
            }, false);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return (string.Empty, new[]
                {
                    "git failed to start; validated full workspace."
                }, false);
            }
        }
        catch (Exception ex)
        {
            // File-not-found (git not on PATH), Win32Exception, etc.
            return (string.Empty, new[]
            {
                _createUnexpectedFailure(ex, WorkspaceValidationFailureOperation.GitStart).Summary
            }, false);
        }

        using var readerCancellation = new CancellationTokenSource();
        Task<string> stdoutTask = Task.FromResult(string.Empty);
        Task<string> stderrTask = Task.FromResult(string.Empty);
        Task<string[]>? outputTask = null;
        string stdout;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        try
        {
            process.StandardInput.Close();
            stdoutTask = _readOutputAsync(process.StandardOutput, readerCancellation.Token);
            stderrTask = _readOutputAsync(process.StandardError, readerCancellation.Token);
            outputTask = Task.WhenAll(stdoutTask, stderrTask);
            await _waitForGitExitAsync(process, timeoutCts.Token).ConfigureAwait(false);
            var output = await outputTask.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            stdout = output[0];
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            return (string.Empty, new[]
            {
                $"git status exceeded the timeout of {timeout.TotalSeconds:F0} second(s); retryable=true; validated full workspace."
            }, true);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return (string.Empty, new[]
            {
                _createUnexpectedFailure(ex, WorkspaceValidationFailureOperation.GitStatus).Summary
            }, false);
        }
        finally
        {
            await TerminateAndDrainAsync(process, outputTask ?? Task.WhenAll(stdoutTask, stderrTask), readerCancellation)
                .ConfigureAwait(false);
        }

        if (process.ExitCode != 0)
        {
            return (string.Empty, new[]
            {
                $"git status exited non-zero (exit {process.ExitCode}); validated full workspace."
            }, false);
        }

        return (stdout, Array.Empty<string>(), false);
    }

    private static void RemoveAmbientGitRepositoryOverrides(ProcessStartInfo startInfo)
    {
        string[] repositoryOverrides =
        [
            "GIT_DIR",
            "GIT_WORK_TREE",
            "GIT_COMMON_DIR",
            "GIT_INDEX_FILE",
            "GIT_OBJECT_DIRECTORY",
            "GIT_ALTERNATE_OBJECT_DIRECTORIES",
        ];

        foreach (var variable in repositoryOverrides)
        {
            startInfo.Environment.Remove(variable);
        }
    }

    private async Task TerminateAndDrainAsync(
        Process process, Task<string[]> outputTask, CancellationTokenSource readerCancellation)
    {
        using var cleanupCancellation = new CancellationTokenSource(CleanupTimeout);
        try
        {
            if (!process.HasExited)
                TryKillProcessTree(process);
            await process.WaitForExitAsync(cleanupCancellation.Token).ConfigureAwait(false);
            await outputTask.WaitAsync(cleanupCancellation.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _createUnexpectedFailure(exception, WorkspaceValidationFailureOperation.GitStatus);
        }
        finally
        {
            // On a failed termination or inherited pipe, cancel the reads independently of
            // the caller and observe BOTH tasks before disposing their streams.
            await readerCancellation.CancelAsync().ConfigureAwait(false);
            try
            {
                await outputTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (readerCancellation.IsCancellationRequested)
            {
                // The cleanup budget expired before EOF; both cancelled readers are observed.
            }
            catch (Exception exception)
            {
                _createUnexpectedFailure(exception, WorkspaceValidationFailureOperation.GitStatus);
            }
        }
    }

    internal void TryKillProcessTree(Process process)
    {
        try
        {
            _killProcessTree(process);
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
            // The child exited between the state check and the termination request.
        }
        catch (Exception exception)
        {
            var detail = _createUnexpectedFailure(exception, WorkspaceValidationFailureOperation.GitStatus);
            _logger?.LogWarning(
                "Failed to kill git process tree for process {ProcessId}. {Summary}",
                process.Id, detail.Summary);
        }
    }

    /// <summary>
    /// Walks from <paramref name="startDirectory"/> upward looking for a <c>.git</c> entry (a
    /// directory for a normal clone, a file for a submodule / linked worktree). Returns false
    /// when we hit the filesystem root without finding one.
    /// </summary>
    private static bool IsInsideGitRepository(string startDirectory)
    {
        if (string.IsNullOrWhiteSpace(startDirectory))
            return false;

        DirectoryInfo? current;
        try
        {
            current = new DirectoryInfo(startDirectory);
        }
        catch
        {
            return false;
        }

        while (current is not null)
        {
            var gitEntry = Path.Combine(current.FullName, ".git");
            if (Directory.Exists(gitEntry) || File.Exists(gitEntry))
            {
                return true;
            }
            current = current.Parent;
        }
        return false;
    }

}
