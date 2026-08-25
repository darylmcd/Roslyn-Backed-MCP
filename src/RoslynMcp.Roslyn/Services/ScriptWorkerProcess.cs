using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

internal sealed record ScriptWorkerRequest(string Code, string[]? Imports, int TimeoutSeconds);

internal interface IScriptWorkerProcess
{
    IScriptWorkerSession Start(ScriptWorkerRequest request);
}

internal interface IScriptWorkerSession : IDisposable
{
    string Name { get; }
    bool WaitForExit(int milliseconds);
    ScriptExecutionOutcome GetOutcome();
    void Terminate();
}

internal sealed class ScriptWorkerProcess(ILogger logger) : IScriptWorkerProcess
{
    internal const int MaxIpcCharacters = 1024 * 1024;
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(5);
    private const int TerminationTimeoutMilliseconds = 5_000;

    public IScriptWorkerSession Start(ScriptWorkerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var serializedRequest = JsonSerializer.Serialize(request);
        if (serializedRequest.Length > MaxIpcCharacters)
        {
            throw new ArgumentException("The script worker request exceeds the bounded IPC limit.", nameof(request));
        }

        var startInfo = CreateStartInfo();
        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = false };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("The script worker process did not start.");
            }

            WriteRequestAsync(process.StandardInput, serializedRequest)
                .WaitAsync(StartupTimeout)
                .GetAwaiter()
                .GetResult();
            var stdout = ReadBoundedAsync(process.StandardOutput);
            var stderr = ReadBoundedAsync(process.StandardError);

            logger.LogDebug("evaluate_csharp: started isolated worker process {WorkerProcessId}.", process.Id);
            return new Session(process, stdout, stderr, logger);
        }
        catch (Exception startFailure)
        {
            var cleanupFailure = CleanupFailedStart(process);
            if (cleanupFailure is not null)
            {
                throw new AggregateException(
                    "The script worker failed to start and could not be fully reclaimed.",
                    startFailure,
                    cleanupFailure);
            }
            throw;
        }
    }

    private static Exception? CleanupFailedStart(Process process)
    {
        Exception? failure = null;
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                if (!process.WaitForExit(TerminationTimeoutMilliseconds))
                {
                    throw new TimeoutException("The failed script worker did not terminate within the cleanup bound.");
                }
            }
        }
        catch (InvalidOperationException)
        {
            // Process.Start failed before an operating-system process existed.
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        try
        {
            process.Dispose();
        }
        catch (Exception ex)
        {
            failure = failure is null ? ex : new AggregateException(failure, ex);
        }
        return failure;
    }

    private static ProcessStartInfo CreateStartInfo()
    {
        var entryAssemblyPath = ResolveWorkerEntryAssemblyPath();
        if (string.IsNullOrWhiteSpace(entryAssemblyPath))
        {
            throw new InvalidOperationException("The script worker entry assembly could not be resolved.");
        }

        var processPath = Environment.ProcessPath;
        var entryFileName = Path.GetFileNameWithoutExtension(entryAssemblyPath);
        var processFileName = Path.GetFileNameWithoutExtension(processPath);
        var launchesEntryDirectly = !string.IsNullOrWhiteSpace(processPath) &&
            string.Equals(entryFileName, processFileName, StringComparison.OrdinalIgnoreCase);

        var startInfo = new ProcessStartInfo
        {
            FileName = launchesEntryDirectly ? processPath! : ResolveDotnetHost(),
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (!launchesEntryDirectly)
        {
            startInfo.ArgumentList.Add(entryAssemblyPath);
        }
        startInfo.ArgumentList.Add(ScriptWorkerProtocol.WorkerArgument);
        return startInfo;
    }

    private static string? ResolveWorkerEntryAssemblyPath()
    {
        var currentEntry = Assembly.GetEntryAssembly()?.Location;
        if (string.Equals(
                Path.GetFileNameWithoutExtension(currentEntry),
                "RoslynMcp.Host.Stdio",
                StringComparison.OrdinalIgnoreCase))
        {
            return currentEntry;
        }

        // Test hosts and library consumers run under a different entry assembly. The project
        // reference copies the packaged worker-capable host beside the caller, so prefer that
        // explicit artifact instead of recursively launching testhost or the consumer process.
        var packagedHost = Path.Combine(AppContext.BaseDirectory, "RoslynMcp.Host.Stdio.dll");
        return File.Exists(packagedHost) ? packagedHost : currentEntry;
    }

    private static string ResolveDotnetHost()
    {
        var configuredHost = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        return string.IsNullOrWhiteSpace(configuredHost) ? "dotnet" : configuredHost;
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader)
    {
        var buffer = new char[4096];
        var value = new StringBuilder();
        while (true)
        {
            var count = await reader.ReadAsync(buffer).ConfigureAwait(false);
            if (count == 0)
            {
                return value.ToString();
            }

            if (value.Length + count > MaxIpcCharacters)
            {
                throw new InvalidDataException("The script worker exceeded the bounded IPC output limit.");
            }
            value.Append(buffer, 0, count);
        }
    }

    private static async Task WriteRequestAsync(StreamWriter writer, string request)
    {
        await writer.WriteLineAsync(request).ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);
        writer.Close();
    }

    private sealed class Session(
        Process process,
        Task<string> stdout,
        Task<string> stderr,
        ILogger logger) : IScriptWorkerSession
    {
        private int _disposed;
        private readonly string _name = $"process-{process.Id}";

        public string Name => _name;

        public bool WaitForExit(int milliseconds)
        {
            ThrowIfReaderFailed();
            return process.WaitForExit(milliseconds);
        }

        public ScriptExecutionOutcome GetOutcome()
        {
            process.WaitForExit();
            var output = stdout.GetAwaiter().GetResult();
            _ = stderr.GetAwaiter().GetResult();
            if (process.ExitCode != 0)
            {
                return ScriptExecutionOutcome.Runtime(
                    new InvalidOperationException($"The isolated script worker exited with code {process.ExitCode}."));
            }

            var response = JsonSerializer.Deserialize<ScriptExecutionOutcome>(output.Trim());
            return response.Kind == ScriptExecutionOutcomeKind.None
                ? ScriptExecutionOutcome.Runtime(new InvalidDataException("The isolated script worker returned an invalid response."))
                : response;
        }

        public void Terminate()
        {
            if (process.HasExited)
            {
                return;
            }

            process.Kill(entireProcessTree: true);
            if (!process.WaitForExit(TerminationTimeoutMilliseconds))
            {
                throw new TimeoutException("The isolated script worker did not terminate within the cleanup bound.");
            }
            ObserveReadersAfterTermination();
            logger.LogWarning("evaluate_csharp: terminated isolated worker process {WorkerProcessId}.", process.Id);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }
            process.Dispose();
        }

        private void ThrowIfReaderFailed()
        {
            if (stdout.IsFaulted)
            {
                stdout.GetAwaiter().GetResult();
            }
            if (stderr.IsFaulted)
            {
                stderr.GetAwaiter().GetResult();
            }
        }

        private void ObserveReadersAfterTermination()
        {
            try
            {
                Task.WhenAll(stdout, stderr).GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException)
            {
                logger.LogWarning(
                    "evaluate_csharp: isolated worker output closed with a bounded transport failure during termination.");
            }
        }
    }
}

internal static class ScriptWorkerProtocol
{
    internal const string WorkerArgument = "--roslyn-mcp-script-worker";

    public static int Run(TextReader input, TextWriter output)
    {
        try
        {
            var payload = ReadBoundedLine(input);
            if (string.IsNullOrEmpty(payload))
            {
                return 64;
            }

            var request = JsonSerializer.Deserialize<ScriptWorkerRequest>(payload);
            if (request is null || request.TimeoutSeconds <= 0)
            {
                return 64;
            }

            var protocolOutput = output;
            var originalOut = Console.Out;
            var originalError = Console.Error;
            ScriptExecutionOutcome outcome;
            try
            {
                Console.SetOut(TextWriter.Null);
                Console.SetError(TextWriter.Null);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(request.TimeoutSeconds));
                var options = ScriptingService.BuildScriptOptions(request.Imports);
                outcome = ScriptingService.ExecuteScript(request.Code, options, timeout.Token);
            }
            catch (OperationCanceledException)
            {
                outcome = ScriptExecutionOutcome.TimedOut();
            }
            catch (Exception ex)
            {
                outcome = ScriptExecutionOutcome.Runtime(ex);
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }

            protocolOutput.Write(JsonSerializer.Serialize(outcome));
            protocolOutput.Flush();
            return 0;
        }
        catch (Exception)
        {
            Console.Error.WriteLine("[roslyn-mcp-script-worker] Internal worker failure.");
            return 70;
        }
    }

    private static string? ReadBoundedLine(TextReader input)
    {
        var value = new StringBuilder();
        while (true)
        {
            var next = input.Read();
            if (next is -1 or '\n')
            {
                return value.Length == 0 ? null : value.ToString().TrimEnd('\r');
            }
            if (value.Length == ScriptWorkerProcess.MaxIpcCharacters)
            {
                throw new InvalidDataException("The script worker request exceeds the bounded IPC limit.");
            }
            value.Append((char)next);
        }
    }
}
