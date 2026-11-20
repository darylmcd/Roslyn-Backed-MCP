using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using RoslynMcp.Host.Stdio;
using RoslynMcp.Host.Stdio.Diagnostics;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class McpLoggingLifecycleWireTests
{
    [TestMethod]
    [Timeout(60000)]
    public async Task ProductionHost_EmitsNoProtocolLoggingBeforeOrAfterInitialization()
    {
        var hostDll = typeof(HostAssemblyMarker).Assembly.Location;
        Assert.IsTrue(File.Exists(hostDll), $"Build the host before the wire test: {hostDll}");
        var testRoot = Path.Combine(
            TestTempRoot.Current,
            nameof(McpLoggingLifecycleWireTests),
            Guid.NewGuid().ToString("N"));
        var foreignWorkingDirectory = Path.Combine(testRoot, "foreign-cwd");
        var stateRoot = Path.Combine(testRoot, "state");
        Directory.CreateDirectory(foreignWorkingDirectory);

        // Seed the sink directory before launch. The dead pid is proven dead (an exited child, never a
        // hardcoded number); the Process object stays undisposed until the method ends so Windows cannot
        // recycle the pid. The live control is this test host's own pid: aged equally, it must survive.
        var logDirectory = Path.Combine(stateRoot, "roslyn-mcp", "logs");
        Directory.CreateDirectory(logDirectory);
        using var deadChild = ExitedProcess.StartAndAwaitExit();
        var agedStamp = DateTime.UtcNow - TimeSpan.FromDays(30);
        var deadSeed = SeedLogFile(logDirectory, deadChild.Id, agedStamp);
        var liveSeed = SeedLogFile(logDirectory, Environment.ProcessId, agedStamp);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("dotnet", $"\"{hostDll}\"")
            {
                WorkingDirectory = foreignWorkingDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        process.StartInfo.Environment[ServerObservabilityOptions.EnvironmentVariableName] = "file";
        process.StartInfo.Environment["XDG_STATE_HOME"] = stateRoot;
        Assert.IsTrue(process.Start());

        var stdout = new CapturedLineStream();
        var stderr = new CapturedLineStream();
        var stdoutPump = PumpLinesAsync(process.StandardOutput, stdout);
        var stderrPump = PumpLinesAsync(process.StandardError, stderr);

        try
        {
            await WaitForLineAsync(stderr, "Startup surface:");
            await Task.Delay(TimeSpan.FromMilliseconds(100));
            Assert.IsEmpty(stdout.Snapshot(), "The host emitted a protocol frame before initialize.");

            // The startup sweep runs off the startup path: wait for its observable effect (the dead file gone
            // and its summary record written), never a fixed delay.
            var ownFile = Path.Combine(logDirectory, $"roslyn-mcp-{process.Id}.jsonl");
            await WaitForConditionAsync(
                () => !File.Exists(deadSeed) && ReadSweepSummary(ownFile) is { } summary &&
                    summary.GetProperty("fields").GetProperty("deleted").GetInt32() >= 1,
                "the startup sweep to delete the dead-pid file and log its summary",
                TimeSpan.FromSeconds(20),
                () => DescribeSweepState(logDirectory, ownFile));

            await WriteMessageAsync(
                process,
                """
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2026-07-28","capabilities":{},"clientInfo":{"name":"wire-test","version":"1.0"}}}
                """);
            await WaitForResponseAsync(stdout, id: 1);
            await WriteMessageAsync(
                process,
                """
                {"jsonrpc":"2.0","method":"notifications/initialized","params":{}}
                """);
            await WriteMessageAsync(
                process,
                """
                {"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"server_info","arguments":{}}}
                """);
            var serverInfoResponse = await WaitForResponseAsync(stdout, id: 2);

            using var document = JsonDocument.Parse(serverInfoResponse);
            var text = document.RootElement
                .GetProperty("result")
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetString();
            Assert.IsNotNull(text);
            using var payload = JsonDocument.Parse(text);
            Assert.IsFalse(payload.RootElement
                .GetProperty("capabilities")
                .GetProperty("logging")
                .GetBoolean());

            await WriteMessageAsync(
                process,
                """
                {"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"workspace_load","arguments":{}}}
                """);
            await WaitForResponseAsync(stdout, id: 3);

            // Complete one final request immediately before EOF. The SDK transport owns its
            // buffered stdout stream and flushes every send; a Console.Out shutdown hook cannot
            // reach that buffer. The final frame must remain observable through clean EOF.
            await WriteMessageAsync(
                process,
                """
                {"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"server_heartbeat","arguments":{}}}
                """);
            await WaitForResponseAsync(stdout, id: 4);

            process.StandardInput.Close();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            await StopProcessAsync(process);
            await Task.WhenAll(stdoutPump, stderrPump);
        }

        Assert.IsTrue(
            stdout.Snapshot().Any(line => HasResponseId(line, 4)),
            "The final response queued immediately before stdin EOF must be flushed by the SDK transport.");

        foreach (var line in stdout.Snapshot())
        {
            using var document = JsonDocument.Parse(line);
            Assert.IsFalse(
                document.RootElement.TryGetProperty("method", out var method) &&
                method.GetString() == "notifications/message",
                $"Protocol logging frame leaked onto stdout: {line}");
        }

        Assert.IsNotEmpty(stderr.Snapshot(), "Operational host logs must remain available on stderr.");

        var hostLogFile = Path.Combine(logDirectory, $"roslyn-mcp-{process.Id}.jsonl");
        Assert.IsTrue(File.Exists(hostLogFile), "The host must write its own pid-named file.");
        CollectionAssert.AreEquivalent(
            new[] { hostLogFile, liveSeed },
            Directory.GetFiles(logDirectory, "roslyn-mcp-*.jsonl"),
            "After the sweep only the host's own file and the live-pid control remain.");
        Assert.IsFalse(File.Exists(deadSeed));
        var records = File.ReadLines(hostLogFile)
            .Select(static line => JsonDocument.Parse(line))
            .ToArray();
        try
        {
            var sweep = records.Single(static record =>
                record.RootElement.GetProperty("eventName").GetString() == "LogRetentionSweepCompleted").RootElement;
            Assert.AreEqual("Information", sweep.GetProperty("level").GetString());
            Assert.AreEqual(1, sweep.GetProperty("fields").GetProperty("deleted").GetInt32());
            Assert.IsGreaterThanOrEqualTo(1, sweep.GetProperty("fields").GetProperty("skippedLive").GetInt32());
            Assert.IsTrue(records.Length > 1, "The file sink must capture the full ILogger stream.");
            Assert.IsTrue(
                records.Select(static record => record.RootElement.GetProperty("category").GetString())
                    .Distinct(StringComparer.Ordinal)
                    .Count() > 1,
                "The file sink captured only one logger category instead of the full stream.");
            var completion = records.Single(record =>
                record.RootElement.GetProperty("eventName").GetString() == "ToolCompleted" &&
                record.RootElement.GetProperty("fields").GetProperty("toolName").GetString() == "server_info");
            var completionRoot = completion.RootElement;
            Assert.AreEqual("success", completionRoot.GetProperty("fields").GetProperty("outcome").GetString());
            Assert.IsTrue(completionRoot.GetProperty("fields").GetProperty("elapsedMs").GetDouble() >= 0);
            var correlationId = completionRoot.GetProperty("correlationId").GetString();
            Assert.IsNotNull(correlationId);
            Assert.AreEqual(32, correlationId.Length);
            Assert.IsTrue(
                correlationId.All(static value => value is >= '0' and <= '9' or >= 'a' and <= 'f'));
            var timestamp = completionRoot.GetProperty("ts").GetString();
            Assert.IsNotNull(timestamp);
            Assert.IsTrue(timestamp.EndsWith('Z'));
            Assert.IsTrue(DateTimeOffset.TryParse(timestamp, out _));

            var failure = records.Single(record =>
                record.RootElement.GetProperty("eventName").GetString() == "ToolFailed" &&
                record.RootElement.GetProperty("fields").GetProperty("toolName").GetString() == "workspace_load");
            var failureRoot = failure.RootElement;
            Assert.AreEqual("expected-error", failureRoot.GetProperty("fields").GetProperty("outcome").GetString());
            Assert.IsTrue(failureRoot.GetProperty("fields").GetProperty("elapsedMs").GetDouble() >= 0);
            var failureCorrelationId = failureRoot.GetProperty("correlationId").GetString();
            Assert.IsNotNull(failureCorrelationId);
            Assert.AreEqual(32, failureCorrelationId.Length);
            Assert.AreNotEqual(
                correlationId,
                failureCorrelationId,
                "Independent tool requests must carry independent correlation ids.");
        }
        finally
        {
            foreach (var record in records)
            {
                record.Dispose();
            }
        }

        Assert.IsEmpty(
            Directory.EnumerateFileSystemEntries(foreignWorkingDirectory).ToArray(),
            "A foreign-CWD launch must not write logs or metadata into its working directory.");
    }

    [TestMethod]
    public void TransportDisconnectDiagnostics_EmitsTypeAndCorrelationWithoutRawDetail()
    {
        const string secretMessage = "provider-secret-disconnect-detail";
        const string secretPath = "C:/private/customer/solution.slnx";
        var logger = new ListLogger<McpLoggingLifecycleWireTests>();

        var correlationId = TransportDisconnectDiagnostics.Log(
            logger,
            new IOException($"{secretMessage} at {secretPath}"));

        Assert.HasCount(1, logger.Entries);
        var entry = logger.Entries[0];
        Assert.AreEqual(LogLevel.Warning, entry.Level);
        Assert.IsNull(entry.Exception, "Transport disconnects must not attach the raw exception object.");
        StringAssert.Contains(entry.Message, "category=transport-disconnected");
        StringAssert.Contains(entry.Message, "exceptionType=IOException");
        StringAssert.Contains(entry.Message, $"correlationId={correlationId}");
        Assert.IsFalse(entry.Message.Contains(secretMessage, StringComparison.Ordinal));
        Assert.IsFalse(entry.Message.Contains(secretPath, StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(32, correlationId.Length);
        Assert.IsTrue(correlationId.All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f'));
    }

    private static string SeedLogFile(string logDirectory, int pid, DateTime lastWriteUtc)
    {
        var path = Path.Combine(logDirectory, $"roslyn-mcp-{pid}.jsonl");
        File.WriteAllText(path, "{\"seed\":true}\n");
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
        return path;
    }

    private static async Task WaitForConditionAsync(
        Func<bool> condition,
        string description,
        TimeSpan timeout,
        Func<string> diagnostics)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            if (condition())
            {
                return;
            }

            if (clock.Elapsed >= timeout)
            {
                Assert.Fail($"Timed out after {timeout.TotalSeconds:0}s waiting for {description}. {diagnostics()}");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }
    }

    /// <summary>
    /// Reads the host's own, still-open log without blocking its writer (FileShare.ReadWrite | Delete).
    /// A partially written last line fails to parse and is skipped; a file that is not there yet yields no records.
    /// </summary>
    private static IEnumerable<JsonElement> ReadOwnLogRecords(string ownFile)
    {
        var records = new List<JsonElement>();
        try
        {
            using var stream = new FileStream(ownFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line)
            {
                try
                {
                    using var document = JsonDocument.Parse(line);
                    records.Add(document.RootElement.Clone());
                }
                catch (JsonException)
                {
                    // Partial last line while the host is mid-write.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not created yet, or briefly unavailable: treated as "no records yet".
        }

        return records;
    }

    private static JsonElement? ReadSweepSummary(string ownFile)
    {
        foreach (var record in ReadOwnLogRecords(ownFile))
        {
            if (record.TryGetProperty("eventName", out var name) &&
                name.ValueKind == JsonValueKind.String &&
                name.GetString() == "LogRetentionSweepCompleted")
            {
                return record;
            }
        }

        return null;
    }

    private static string DescribeSweepState(string logDirectory, string ownFile)
    {
        var listing = Directory.Exists(logDirectory)
            ? string.Join(", ", Directory.GetFileSystemEntries(logDirectory).Select(Path.GetFileName))
            : "<log directory missing>";
        string tail;
        try
        {
            using var stream = new FileStream(ownFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var lines = new List<string>();
            while (reader.ReadLine() is { } line)
            {
                lines.Add(line);
            }

            tail = string.Join(" | ", lines.TakeLast(10));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            tail = $"<own log unreadable: {ex.GetType().Name}>";
        }

        return $"Directory: [{listing}]. Own log tail: [{tail}].";
    }

    private static bool HasResponseId(string message, int id)
    {
        using var document = JsonDocument.Parse(message);
        return document.RootElement.TryGetProperty("id", out var responseId) &&
            responseId.ValueKind == JsonValueKind.Number &&
            responseId.GetInt32() == id;
    }

    private static async Task StopProcessAsync(Process process)
    {
        if (process.HasExited)
        {
            return;
        }

        process.StandardInput.Close();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
    }

    private static async Task PumpLinesAsync(StreamReader reader, CapturedLineStream destination)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                destination.Add(line);
            }
        }

        destination.Complete();
    }

    private static async Task WriteMessageAsync(Process process, string json)
    {
        await process.StandardInput.WriteLineAsync(json);
        await process.StandardInput.FlushAsync();
    }

    private static async Task<string> WaitForResponseAsync(
        CapturedLineStream messages,
        int id) =>
        await messages.WaitForMatchAsync(
            message =>
            {
                using var document = JsonDocument.Parse(message);
                return document.RootElement.TryGetProperty("id", out var responseId) &&
                    responseId.ValueKind == JsonValueKind.Number &&
                    responseId.GetInt32() == id;
            },
            $"MCP response id {id}",
            TimeSpan.FromSeconds(20));

    private static async Task WaitForLineAsync(
        CapturedLineStream lines,
        string expectedText) =>
        _ = await lines.WaitForMatchAsync(
            line => line.Contains(expectedText, StringComparison.Ordinal),
            $"stderr marker '{expectedText}'",
            TimeSpan.FromSeconds(20));

    private sealed class CapturedLineStream
    {
        private readonly ConcurrentQueue<string> _history = new();
        private readonly Channel<string> _arrivals = Channel.CreateUnbounded<string>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true,
            });

        public void Add(string line)
        {
            _history.Enqueue(line);
            Assert.IsTrue(_arrivals.Writer.TryWrite(line));
        }

        public void Complete() => _arrivals.Writer.TryComplete();

        public string[] Snapshot() => _history.ToArray();

        public async Task<string> WaitForMatchAsync(
            Func<string, bool> predicate,
            string description,
            TimeSpan timeout)
        {
            using var timeoutSource = new CancellationTokenSource(timeout);
            try
            {
                await foreach (var line in _arrivals.Reader.ReadAllAsync(timeoutSource.Token))
                {
                    if (predicate(line))
                    {
                        return line;
                    }
                }
            }
            catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
            {
            }

            var snapshot = Snapshot();
            var boundaryMatch = snapshot.FirstOrDefault(predicate);
            if (boundaryMatch is not null)
            {
                return boundaryMatch;
            }

            Assert.Fail(
                $"Timed out waiting for {description}. " +
                $"Captured before the timeout boundary={string.Join(" | ", snapshot)}");
            return string.Empty;
        }
    }
}
