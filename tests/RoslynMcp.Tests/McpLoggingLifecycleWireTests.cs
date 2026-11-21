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
        var (stateRoot, foreignWorkingDirectory) = CreateIsolatedRoots();

        // Seed the sink directory before launch. The dead pid is proven dead (an exited child, never a
        // hardcoded number); the Process object stays undisposed until the method ends so Windows cannot
        // recycle the pid. The live control is this test host's own pid: aged equally, it must survive.
        var logDirectory = Path.Combine(stateRoot, "roslyn-mcp", "logs");
        Directory.CreateDirectory(logDirectory);
        using var deadChild = ExitedProcess.StartAndAwaitExit();
        var agedStamp = DateTime.UtcNow - TimeSpan.FromDays(30);
        var deadSeed = SeedLogFile(logDirectory, deadChild.Id, agedStamp);
        var liveSeed = SeedLogFile(logDirectory, Environment.ProcessId, agedStamp);

        await using var host = HostLaunch.Start(stateRoot, foreignWorkingDirectory, sinkValue: "file");
        var process = host.Process;
        var stdout = host.Stdout;
        var stderr = host.Stderr;

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

            using var payload = ParseToolPayload(serverInfoResponse);
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
            await host.StopAsync();
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
    [Timeout(60000)]
    public async Task ProductionHost_UnsetSink_DefaultsToFileAndReportsLogPath()
    {
        var (stateRoot, foreignWorkingDirectory) = CreateIsolatedRoots();
        var logDirectory = Path.Combine(stateRoot, "roslyn-mcp", "logs");
        await using var host = HostLaunch.Start(stateRoot, foreignWorkingDirectory, sinkValue: null);
        var logFile = Path.Combine(logDirectory, $"roslyn-mcp-{host.Process.Id}.jsonl");

        try
        {
            await WaitForLineAsync(host.Stderr, "Startup surface:");
            await host.InitializeAsync();
            using var payload = await host.ReadServerInfoAsync(id: 2);

            var observability = payload.RootElement.GetProperty("observability");
            Assert.AreEqual("file", observability.GetProperty("sink").GetString());
            Assert.AreEqual(Path.GetFullPath(logFile), observability.GetProperty("logFile").GetString());
            Assert.AreEqual(Path.GetFullPath(logDirectory), observability.GetProperty("logDirectory").GetString());

            await WaitForConditionAsync(
                () => FindEvent(ReadOwnLogRecords(logFile), "ObservabilitySinkConfigured") is not null,
                "the startup record naming the sink and log path",
                TimeSpan.FromSeconds(20),
                () => DescribeSweepState(logDirectory, logFile));
            var record = FindEvent(ReadOwnLogRecords(logFile), "ObservabilitySinkConfigured")!.Value;
            Assert.AreEqual("Information", record.GetProperty("level").GetString());
            var fields = record.GetProperty("fields");
            Assert.AreEqual("file", fields.GetProperty("sink").GetString());
            Assert.AreEqual(Path.GetFullPath(logFile), fields.GetProperty("logFile").GetString());
            Assert.AreEqual(Path.GetFullPath(logDirectory), fields.GetProperty("logDirectory").GetString());
        }
        finally
        {
            await host.StopAsync();
        }

        Assert.IsTrue(File.Exists(logFile), "An unset sink variable must install the file sink.");
        Assert.IsEmpty(
            Directory.EnumerateFileSystemEntries(foreignWorkingDirectory).ToArray(),
            "A foreign-CWD launch must not write logs or metadata into its working directory.");
    }

    [TestMethod]
    [Timeout(60000)]
    public async Task ProductionHost_ExplicitDisabled_WritesNoLogFileAndReportsDisabled() =>
        await AssertDisabledHostAsync(extraEnvironment: null);

    /// <summary>
    /// Guard, not a regression test: the retention variables are sink-scoped, so an explicit
    /// <c>disabled</c> must never parse them. It passes on the old code too and fails if the retention
    /// parse is hoisted out of the file-sink branch of Program.cs.
    /// </summary>
    [TestMethod]
    [Timeout(60000)]
    public async Task ProductionHost_DisabledSink_IgnoresInvalidRetentionVariable() =>
        await AssertDisabledHostAsync(new Dictionary<string, string>
        {
            [JsonLinesRetentionOptions.MaxSegmentsVariable] = "0",
        });

    [TestMethod]
    [Timeout(60000)]
    public async Task ProductionHost_UnsetSink_InvalidRetentionVariable_FailsStartupNamingVariableWithoutEchoingValue()
    {
        const string sentinel = "NOT-A-NUMBER-7f3a9c1e";
        var (stateRoot, foreignWorkingDirectory) = CreateIsolatedRoots();
        await using var host = HostLaunch.Start(
            stateRoot,
            foreignWorkingDirectory,
            sinkValue: null,
            new Dictionary<string, string> { [JsonLinesRetentionOptions.MaxSegmentsVariable] = sentinel });

        var exitCode = await host.WaitForExitAsync(TimeSpan.FromSeconds(30));

        Assert.IsNotNull(exitCode, "The host stayed up with an invalid retention variable and the default sink.");
        Assert.AreNotEqual(0, exitCode.Value, "An invalid retention variable must fail startup (non-zero exit).");
        var stderr = string.Join("\n", host.Stderr.Snapshot());
        StringAssert.Contains(stderr, JsonLinesRetentionOptions.MaxSegmentsVariable);
        Assert.IsFalse(stderr.Contains(sentinel, StringComparison.Ordinal), "The rejected value must not be echoed on stderr.");
        Assert.IsFalse(
            string.Join("\n", host.Stdout.Snapshot()).Contains(sentinel, StringComparison.Ordinal),
            "The rejected value must not reach stdout.");
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

    private static async Task AssertDisabledHostAsync(IReadOnlyDictionary<string, string>? extraEnvironment)
    {
        var (stateRoot, foreignWorkingDirectory) = CreateIsolatedRoots();
        var logDirectory = Path.Combine(stateRoot, "roslyn-mcp", "logs");
        await using var host = HostLaunch.Start(stateRoot, foreignWorkingDirectory, sinkValue: "disabled", extraEnvironment);

        try
        {
            await WaitForLineAsync(host.Stderr, "Startup surface:");
            await host.InitializeAsync();
            using var payload = await host.ReadServerInfoAsync(id: 2);

            var observability = payload.RootElement.GetProperty("observability");
            Assert.AreEqual("disabled", observability.GetProperty("sink").GetString());
            Assert.AreEqual(JsonValueKind.Null, observability.GetProperty("logFile").ValueKind);
            Assert.AreEqual(JsonValueKind.Null, observability.GetProperty("logDirectory").ValueKind);
        }
        finally
        {
            await host.StopAsync();
        }

        Assert.IsFalse(
            Directory.Exists(logDirectory) &&
            Directory.EnumerateFiles(logDirectory, "roslyn-mcp-*.jsonl").Any(),
            "An explicit disabled sink must not write log files.");
    }

    /// <summary>
    /// Every host-spawning test gets a per-test state root under the test run's temp root, so a default-on
    /// file sink can never write into the developer's real per-user log directory.
    /// </summary>
    private static (string StateRoot, string ForeignWorkingDirectory) CreateIsolatedRoots()
    {
        var testRoot = Path.Combine(
            TestTempRoot.Current,
            nameof(McpLoggingLifecycleWireTests),
            Guid.NewGuid().ToString("N"));
        var foreignWorkingDirectory = Path.Combine(testRoot, "foreign-cwd");
        Directory.CreateDirectory(foreignWorkingDirectory);
        return (Path.Combine(testRoot, "state"), foreignWorkingDirectory);
    }

    private static JsonElement? FindEvent(IEnumerable<JsonElement> records, string eventName)
    {
        foreach (var record in records)
        {
            if (record.TryGetProperty("eventName", out var name) &&
                name.ValueKind == JsonValueKind.String &&
                name.GetString() == eventName)
            {
                return record;
            }
        }

        return null;
    }

    private static JsonDocument ParseToolPayload(string response)
    {
        using var document = JsonDocument.Parse(response);
        var text = document.RootElement
            .GetProperty("result")
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString();
        Assert.IsNotNull(text);
        return JsonDocument.Parse(text);
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

    /// <summary>
    /// One launched production host with captured stdout/stderr. Always isolated: the state root must live
    /// under <see cref="TestTempRoot.Current"/> and the sink variable is set or removed explicitly, so an
    /// inherited developer value can never change a result or send logs to the real per-user directory.
    /// </summary>
    private sealed class HostLaunch : IAsyncDisposable
    {
        private readonly Task _pumps;

        private HostLaunch(Process process)
        {
            Process = process;
            _pumps = Task.WhenAll(
                PumpLinesAsync(process.StandardOutput, Stdout),
                PumpLinesAsync(process.StandardError, Stderr));
        }

        public Process Process { get; }

        public CapturedLineStream Stdout { get; } = new();

        public CapturedLineStream Stderr { get; } = new();

        public static HostLaunch Start(
            string stateRoot,
            string workingDirectory,
            string? sinkValue,
            IReadOnlyDictionary<string, string>? extraEnvironment = null)
        {
            Assert.IsTrue(
                Path.GetFullPath(stateRoot).StartsWith(Path.GetFullPath(TestTempRoot.Current), StringComparison.OrdinalIgnoreCase),
                "A wire test must isolate XDG_STATE_HOME under the test temp root.");
            var hostDll = typeof(HostAssemblyMarker).Assembly.Location;
            Assert.IsTrue(File.Exists(hostDll), $"Build the host before the wire test: {hostDll}");
            var process = new Process
            {
                StartInfo = new ProcessStartInfo("dotnet", $"\"{hostDll}\"")
                {
                    WorkingDirectory = workingDirectory,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            var environment = process.StartInfo.Environment;
            environment["XDG_STATE_HOME"] = stateRoot;
            if (sinkValue is null)
            {
                environment.Remove(ServerObservabilityOptions.EnvironmentVariableName);
            }
            else
            {
                environment[ServerObservabilityOptions.EnvironmentVariableName] = sinkValue;
            }

            foreach (var variable in new[]
            {
                JsonLinesRetentionOptions.MaxSegmentBytesVariable,
                JsonLinesRetentionOptions.MaxSegmentsVariable,
                JsonLinesRetentionOptions.RetentionDaysVariable,
                JsonLinesRetentionOptions.MaxTotalBytesVariable,
            })
            {
                environment.Remove(variable);
            }

            foreach (var (name, value) in extraEnvironment ?? new Dictionary<string, string>())
            {
                environment[name] = value;
            }

            Assert.IsTrue(process.Start());
            return new HostLaunch(process);
        }

        public async Task InitializeAsync()
        {
            await WriteMessageAsync(
                Process,
                """
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2026-07-28","capabilities":{},"clientInfo":{"name":"wire-test","version":"1.0"}}}
                """);
            await WaitForResponseAsync(Stdout, id: 1);
            await WriteMessageAsync(
                Process,
                """
                {"jsonrpc":"2.0","method":"notifications/initialized","params":{}}
                """);
        }

        public async Task<JsonDocument> ReadServerInfoAsync(int id)
        {
            await WriteMessageAsync(
                Process,
                $$$$"""
                {"jsonrpc":"2.0","id":{{{{id}}}},"method":"tools/call","params":{"name":"server_info","arguments":{}}}
                """);
            return ParseToolPayload(await WaitForResponseAsync(Stdout, id));
        }

        /// <summary>Exit code once the process exits within <paramref name="timeout"/>; null when it is still running.</summary>
        public async Task<int?> WaitForExitAsync(TimeSpan timeout)
        {
            try
            {
                await Process.WaitForExitAsync().WaitAsync(timeout);
            }
            catch (TimeoutException)
            {
                return null;
            }

            await _pumps;
            return Process.ExitCode;
        }

        public async Task StopAsync()
        {
            await StopProcessAsync(Process);
            await _pumps;
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync();
            Process.Dispose();
        }
    }

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
