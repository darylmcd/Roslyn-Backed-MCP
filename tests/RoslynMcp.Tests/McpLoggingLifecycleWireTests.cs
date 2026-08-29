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

        var logDirectory = Path.Combine(stateRoot, "roslyn-mcp", "logs");
        var logFiles = Directory.GetFiles(logDirectory, "roslyn-mcp-*.jsonl");
        Assert.HasCount(1, logFiles);
        var records = File.ReadLines(logFiles[0])
            .Select(static line => JsonDocument.Parse(line))
            .ToArray();
        try
        {
            Assert.IsTrue(records.Length > 1, "The file sink must capture the full ILogger stream.");
            Assert.IsTrue(
                records.Select(static record => record.RootElement.GetProperty("category").GetString())
                    .Distinct(StringComparer.Ordinal)
                    .Count() > 1,
                "The file sink captured only one logger category instead of the full stream.");
            var completion = records.Single(record =>
                record.RootElement.GetProperty("eventId").GetInt32() == 2101 &&
                record.RootElement.GetProperty("message").GetString() is { } message &&
                message.Contains("Tool server_info completed", StringComparison.Ordinal));
            var completionRoot = completion.RootElement;
            StringAssert.Contains(completionRoot.GetProperty("message").GetString(), "outcome=success");
            StringAssert.Contains(completionRoot.GetProperty("message").GetString(), "elapsedMs=");
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
                record.RootElement.GetProperty("eventId").GetInt32() == 2104 &&
                record.RootElement.GetProperty("message").GetString() is { } message &&
                message.Contains("Tool workspace_load failed", StringComparison.Ordinal));
            var failureRoot = failure.RootElement;
            StringAssert.Contains(failureRoot.GetProperty("message").GetString(), "outcome=expected-error");
            StringAssert.Contains(failureRoot.GetProperty("message").GetString(), "elapsedMs=");
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
