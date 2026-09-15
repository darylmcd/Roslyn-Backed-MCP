using System.Diagnostics;

namespace RoslynMcp.Tests.Helpers;

internal static class PwshScriptRunner
{
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(10);

    internal static async Task<PwshScriptResult> RunAsync(
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default,
        string description = "PowerShell")
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (timeout is { } timeoutValue && timeoutValue <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "The process timeout must be positive.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh",
            WorkingDirectory = workingDirectory ?? string.Empty,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (environment is not null)
        {
            foreach (var (name, value) in environment)
            {
                startInfo.Environment[name] = value;
            }
        }

        return await RunConfiguredProcessAsync(
            startInfo,
            timeout,
            cancellationToken,
            description).ConfigureAwait(false);
    }

    internal static Task<PwshScriptResult> RunExecutableAsync(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default,
        string description = "external process")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(arguments);
        if (timeout is { } timeoutValue && timeoutValue <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "The process timeout must be positive.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory ?? string.Empty,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return RunConfiguredProcessAsync(
            startInfo,
            timeout,
            cancellationToken,
            description);
    }

    private static async Task<PwshScriptResult> RunConfiguredProcessAsync(
        ProcessStartInfo startInfo,
        TimeSpan? timeout,
        CancellationToken cancellationToken,
        string description)
    {
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start {description}.");
        // These verifier processes are non-interactive. Some execute native grandchildren
        // (notably Git), which must see EOF instead of inheriting a test host's open stdin pipe.
        process.StandardInput.Close();
        using var readerCancellation = new CancellationTokenSource();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(readerCancellation.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(readerCancellation.Token);
        var outputTask = Task.WhenAll(stdoutTask, stderrTask);
        using var timeoutCancellation = timeout is null
            ? null
            : new CancellationTokenSource(timeout.Value);
        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellation?.Token ?? CancellationToken.None);

        try
        {
            await process.WaitForExitAsync(waitCancellation.Token).ConfigureAwait(false);
            var output = await outputTask.WaitAsync(waitCancellation.Token).ConfigureAwait(false);
            return new PwshScriptResult(process.ExitCode, output[0], output[1]);
        }
        catch (OperationCanceledException exception)
        {
            var output = await RequestTerminationAndDrainAsync(
                process,
                outputTask,
                description).ConfigureAwait(false);
            if (timeoutCancellation?.IsCancellationRequested == true && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"{description} timed out after {timeout!.Value.TotalSeconds:g} seconds. Output:" +
                    $"{Environment.NewLine}{output}",
                    exception);
            }

            throw;
        }
        finally
        {
            // A descendant can keep a pipe open after its parent exits. Cancel and observe
            // both owned reads before Process.Dispose closes their streams.
            await readerCancellation.CancelAsync().ConfigureAwait(false);
            try
            {
                await outputTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (readerCancellation.IsCancellationRequested)
            {
                // Expected when cleanup exhausted its drain budget before EOF.
            }
        }
    }

    private static async Task<string> RequestTerminationAndDrainAsync(
        Process process,
        Task<string[]> outputTask,
        string description)
    {
        if (!process.HasExited)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) when (process.HasExited)
            {
                // The process exited between the state check and termination request.
            }
        }

        using var cleanupCancellation = new CancellationTokenSource(CleanupTimeout);
        try
        {
            await process.WaitForExitAsync(cleanupCancellation.Token).ConfigureAwait(false);
            var output = await outputTask.WaitAsync(cleanupCancellation.Token)
                .ConfigureAwait(false);
            return string.Join(Environment.NewLine, output);
        }
        catch (OperationCanceledException) when (cleanupCancellation.IsCancellationRequested)
        {
            // Preserve the caller's cancellation or configured-timeout classification.
            // The outer finally cancels and observes both readers even without EOF.
            return $"{description} did not terminate and drain redirected output within " +
                $"{CleanupTimeout.TotalSeconds:g} seconds after process-tree termination was requested.";
        }
    }
}

internal sealed record PwshScriptResult(int ExitCode, string StdOut, string StdErr)
{
    internal string AllOutput => PowerShellOutputNormalizer.Normalize(
        StdOut + Environment.NewLine + StdErr);
}
