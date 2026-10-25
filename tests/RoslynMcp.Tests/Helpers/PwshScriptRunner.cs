using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace RoslynMcp.Tests.Helpers;

internal static class PwshScriptRunner
{
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(10);

    internal static Task<PwshScriptResult> RunAsync(
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default,
        string description = "PowerShell")
        => RunExecutableAsync(
            OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh",
            arguments,
            environment,
            workingDirectory,
            timeout,
            cancellationToken,
            description);

    internal static Task<PwshScriptResult> RunExecutableAsync(
        string fileName,
        IEnumerable<string> arguments,
        IReadOnlyDictionary<string, string?>? environment,
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

        if (environment is not null)
        {
            foreach (var (name, value) in environment)
            {
                startInfo.Environment[name] = value;
            }
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
        var runnerStarted = Stopwatch.GetTimestamp();
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start {description}.");
        // Unix needs a live process to retrieve StartTime; retain the best-effort observation.
        var processStartedUtc = CaptureDiagnostic(() => process.StartTime.ToUniversalTime().ToString("O"));
        // These verifier processes are non-interactive. Some execute native grandchildren
        // (notably Git), which must see EOF instead of inheriting a test host's open stdin pipe.
        process.StandardInput.Close();
        using var readerCancellation = new CancellationTokenSource();
        var stdoutTask = ProcessPipeReader.ReadToEndAsync(process.StandardOutput, readerCancellation.Token);
        var stderrTask = ProcessPipeReader.ReadToEndAsync(process.StandardError, readerCancellation.Token);
        var outputTask = Task.WhenAll(stdoutTask, stderrTask);
        using var timeoutCancellation = timeout is null
            ? null
            : new CancellationTokenSource(timeout.Value);
        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellation?.Token ?? CancellationToken.None);

        var phase = "process exit";
        var phaseStarted = runnerStarted;
        try
        {
            await process.WaitForExitAsync(waitCancellation.Token).ConfigureAwait(false);
            phase = "output drain";
            phaseStarted = Stopwatch.GetTimestamp();
            var output = await outputTask.WaitAsync(waitCancellation.Token).ConfigureAwait(false);
            return new PwshScriptResult(process.ExitCode, output[0], output[1]);
        }
        catch (OperationCanceledException exception)
        {
            var sampledAt = Stopwatch.GetTimestamp();
            var sampleUtc = DateTime.UtcNow.ToString("O");
            var processExited = CaptureDiagnostic(() => process.HasExited.ToString());
            var processExitedUtc = processExited == bool.TrueString
                ? CaptureDiagnostic(() => process.ExitTime.ToUniversalTime().ToString("O"))
                : processExited == bool.FalseString ? "running" : "unknown";
            // Windows exit time comes from the OS; Unix reports a runtime wait observation.
            // Phase elapsed always describes the managed observation, independently of UTC.
            // Each pipe snapshot observes its own instant without consuming any bytes.
            var stdoutPipe = CaptureDiagnostic(() => CapturePipeSnapshot(process.StandardOutput, runnerStarted));
            var stderrPipe = CaptureDiagnostic(() => CapturePipeSnapshot(process.StandardError, runnerStarted));
            var diagnostics = $"phase={phase}; processExited={processExited}; " +
                $"elapsedMs={(long)Stopwatch.GetElapsedTime(runnerStarted, sampledAt).TotalMilliseconds}; " +
                $"phaseElapsedMs={(long)Stopwatch.GetElapsedTime(phaseStarted, sampledAt).TotalMilliseconds}; " +
                $"timeoutRequested={timeoutCancellation?.IsCancellationRequested == true}; " +
                $"callerCancellationRequested={cancellationToken.IsCancellationRequested}; " +
                $"sampleUtc={sampleUtc}; processStartUtc={processStartedUtc}; processExitUtc={processExitedUtc}; " +
                $"exitTimeSource={(OperatingSystem.IsWindows() ? "windows-kernel" : "runtime-wait-observation")}; " +
                $"stdoutPipe={stdoutPipe}; stderrPipe={stderrPipe}; " +
                $"stdout={stdoutTask.Status}; stderr={stderrTask.Status}; " +
                $"workerThreads={ThreadPool.ThreadCount}; queuedWork={ThreadPool.PendingWorkItemCount}";
            var output = await RequestTerminationAndDrainAsync(
                process,
                outputTask,
                description).ConfigureAwait(false);
            if (timeoutCancellation?.IsCancellationRequested == true && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"{description} timed out after {timeout!.Value.TotalSeconds:g} seconds. {diagnostics}. Output:" +
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

    internal static string CaptureDiagnostic(Func<string> capture)
    {
        try { return capture(); }
        catch (Win32Exception exception) { return $"error:{exception.GetType().Name}:{exception.NativeErrorCode}"; }
        catch (Exception exception) { return $"error:{exception.GetType().Name}"; }
    }

    private static string CapturePipeSnapshot(StreamReader reader, long runnerStarted)
    {
        var sampledAt = Stopwatch.GetTimestamp();
        var state = "unsupported";
        if (OperatingSystem.IsWindows())
        {
            // Peek never consumes output. The existing reader only reads available bytes,
            // and Process keeps the SafeFileHandle alive until both readers are observed.
            var handle = ((FileStream)reader.BaseStream).SafeFileHandle;
            if (PeekNamedPipe(handle, IntPtr.Zero, 0, IntPtr.Zero, out var available, IntPtr.Zero))
                state = $"available; bytes={available}";
            else
            {
                var error = Marshal.GetLastPInvokeError();
                state = error == 109 ? "eof; error=109" : $"error; error={error}";
            }
        }

        return $"{{sampleElapsedMs={(long)Stopwatch.GetElapsedTime(runnerStarted, sampledAt).TotalMilliseconds}; state={state}}}";
    }

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekNamedPipe(SafeFileHandle pipe, IntPtr buffer, uint size,
        IntPtr bytesRead, out uint available, IntPtr bytesLeft);

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
