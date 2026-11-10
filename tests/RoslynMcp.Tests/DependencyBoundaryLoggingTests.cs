using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Diagnostics;
using RoslynMcp.Host.Stdio.Middleware;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// Pins the dependency-boundary records: one process record per command (owned by the runner), one gate
/// record per gated command (queue wait, timeout/cancel), and one record per <c>workspace_load</c>. Every
/// record carries only the allowlisted fields and joins the tool record by correlation id.
/// </summary>
[TestClass]
public sealed class DependencyBoundaryLoggingTests
{
    private const string Sentinel = "SECRET-SENTINEL-private-filter";
    private static readonly string[] ProcessFields = ["targetKind", "verb", "outcome", "exitCode", "durationMs"];
    private static readonly string[] GateFields = ["targetKind", "verb", "outcome", "queueWaitMs", "durationMs", "exitCode"];
    private static readonly string[] LoadFields = ["outcome", "reused", "projectCount", "durationMs"];

    private sealed class LogHarness : IDisposable
    {
        private readonly JsonLinesFileLoggerProvider _provider;

        public LogHarness()
        {
            var logDirectory = Path.Combine(TestTempRoot.Current, nameof(DependencyBoundaryLoggingTests), Guid.NewGuid().ToString("N"));
            _provider = new JsonLinesFileLoggerProvider(
                logDirectory,
                JsonLinesRetentionOptions.Create(JsonLinesRetentionOptions.MinimumSegmentBytes * 64, maxSegments: 2),
                processId: 4321,
                utcNow: () => DateTimeOffset.UtcNow);
            Factory = LoggerFactory.Create(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Trace);
                logging.AddProvider(_provider);
            });
        }

        public ILoggerFactory Factory { get; }

        public List<JsonElement> Records(string eventName)
        {
            var result = new List<JsonElement>();
            // The provider creates its file on the first record; no file means no records.
        foreach (var line in File.Exists(_provider.FilePath) ? File.ReadAllLines(_provider.FilePath) : [])
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement.Clone();
                if (root.GetProperty("eventName").ValueKind == JsonValueKind.String
                    && root.GetProperty("eventName").GetString() == eventName)
                {
                    result.Add(root);
                }
            }

            return result;
        }

        public void Dispose()
        {
            Factory.Dispose();
            _provider.Dispose();
        }
    }

    [TestMethod]
    public async Task Runner_Success_EmitsOneProcessRecordWithDuration()
    {
        using var logs = new LogHarness();
        var runner = new DotnetCommandRunner(logs.Factory.CreateLogger<DotnetCommandRunner>());

        var execution = await runner.RunAsync(
            Environment.CurrentDirectory, Environment.CurrentDirectory, ["--version"], null, "dotnet", CancellationToken.None);

        var record = logs.Records("CommandProcessCompleted").Single();
        var fields = record.GetProperty("fields");
        Assert.AreEqual(0, execution.ExitCode);
        Assert.AreEqual("succeeded", fields.GetProperty("outcome").GetString());
        Assert.AreEqual(0, fields.GetProperty("exitCode").GetInt32());
        Assert.AreEqual("--version", fields.GetProperty("verb").GetString());
        Assert.AreEqual("directory", fields.GetProperty("targetKind").GetString());
        Assert.IsGreaterThanOrEqualTo(0L, fields.GetProperty("durationMs").GetInt64());
        Assert.AreEqual(3003, record.GetProperty("eventId").GetInt32());
    }

    [TestMethod]
    public async Task Runner_NonzeroExit_RecordsFailedOutcomeAndExitCode()
    {
        using var logs = new LogHarness();
        var runner = new DotnetCommandRunner(logs.Factory.CreateLogger<DotnetCommandRunner>());

        var execution = await runner.RunAsync(
            Environment.CurrentDirectory, Environment.CurrentDirectory, ["nosuchverb"], null, "dotnet", CancellationToken.None);

        var fields = logs.Records("CommandProcessCompleted").Single().GetProperty("fields");
        Assert.AreNotEqual(0, execution.ExitCode);
        Assert.AreEqual("failed", fields.GetProperty("outcome").GetString());
        Assert.AreEqual(execution.ExitCode, fields.GetProperty("exitCode").GetInt32());
        Assert.AreEqual("nosuchverb", fields.GetProperty("verb").GetString());
    }

    [TestMethod]
    public async Task Runner_Cancellation_StillEmitsOutcome()
    {
        using var logs = new LogHarness();
        var runner = new DotnetCommandRunner(logs.Factory.CreateLogger<DotnetCommandRunner>());
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await runner.RunAsync(
            Environment.CurrentDirectory, Environment.CurrentDirectory, ["--version"], null, "dotnet", cancelled.Token));

        var fields = logs.Records("CommandProcessCompleted").Single().GetProperty("fields");
        Assert.AreEqual("cancelled", fields.GetProperty("outcome").GetString());
        Assert.AreEqual(JsonValueKind.Null, fields.GetProperty("exitCode").ValueKind);
    }

    [TestMethod]
    public async Task Runner_StartFailure_StillEmitsErrorOutcome()
    {
        using var logs = new LogHarness();
        var runner = new DotnetCommandRunner(logs.Factory.CreateLogger<DotnetCommandRunner>());

        await Assert.ThrowsAsync<System.ComponentModel.Win32Exception>(async () => await runner.RunAsync(
            Environment.CurrentDirectory, Environment.CurrentDirectory, ["help"], null,
            Path.Combine(TestTempRoot.Current, "no-such-executable-" + Guid.NewGuid().ToString("N")), CancellationToken.None));

        var fields = logs.Records("CommandProcessCompleted").Single().GetProperty("fields");
        Assert.AreEqual("error", fields.GetProperty("outcome").GetString());
    }

    [TestMethod]
    public async Task ProcessRecord_CarriesOnlyAllowlistedFields_NoArgumentsFiltersOrPaths()
    {
        using var logs = new LogHarness();
        var runner = new DotnetCommandRunner(logs.Factory.CreateLogger<DotnetCommandRunner>());
        var privateRoot = Path.Combine(TestTempRoot.Current, "admission-root-" + Sentinel);
        Directory.CreateDirectory(privateRoot);

        // Shape of the admission-augmented command the runner really sees: caller filter text plus an
        // appended execution property that names an absolute root.
        _ = await runner.RunAsync(
            privateRoot, Path.Combine(privateRoot, "Private.slnx"),
            ["nosuchverb", "--filter", Sentinel, $"-p:ExecutionRoot={privateRoot}"],
            null, "dotnet", CancellationToken.None);

        var record = logs.Records("CommandProcessCompleted").Single();
        CollectionAssert.AreEquivalent(ProcessFields, record.GetProperty("fields").EnumerateObject().Select(static p => p.Name).ToArray());
        var line = record.GetRawText();
        Assert.IsFalse(line.Contains(Sentinel, StringComparison.Ordinal), line);
        Assert.IsFalse(line.Contains("ExecutionRoot", StringComparison.Ordinal), line);
        Assert.IsFalse(line.Contains("admission-root-", StringComparison.Ordinal), line);
        Assert.AreEqual("solution", record.GetProperty("fields").GetProperty("targetKind").GetString());
    }

    [TestMethod]
    public async Task Gate_QueueWaitIsRecordedForTheSecondCommand()
    {
        using var logs = new LogHarness();
        using var manager = CreateManager(logs);
        var runner = new BlockingRunner();
        using var executor = new GatedCommandExecutor(manager, runner, logs.Factory.CreateLogger<GatedCommandExecutor>());
        var target = Path.Combine(TestTempRoot.Current, "Gate.slnx");

        var first = executor.ExecuteAsync("ws-queue", target, ["build", $"--filter {Sentinel}"], TimeSpan.FromMinutes(1), CancellationToken.None);
        await runner.Started.Task;
        var second = executor.ExecuteAsync("ws-queue", target, ["test"], TimeSpan.FromMinutes(1), CancellationToken.None);
        await Task.Delay(300);
        runner.Release();
        await Task.WhenAll(first, second);

        var records = logs.Records("CommandGateCompleted");
        Assert.HasCount(2, records);
        var waited = records.Select(static r => r.GetProperty("fields").GetProperty("queueWaitMs").GetInt64()).Max();
        Assert.IsGreaterThanOrEqualTo(200L, waited, "The queued command must report its wait.");
        foreach (var record in records)
        {
            var fields = record.GetProperty("fields");
            CollectionAssert.AreEquivalent(GateFields, fields.EnumerateObject().Select(static p => p.Name).ToArray());
            Assert.AreEqual("completed", fields.GetProperty("outcome").GetString());
            Assert.AreEqual("solution", fields.GetProperty("targetKind").GetString());
            Assert.IsFalse(record.GetRawText().Contains(Sentinel, StringComparison.Ordinal));
        }
    }

    [TestMethod]
    public async Task Gate_Timeout_RecordsTimeoutOutcome()
    {
        using var logs = new LogHarness();
        using var manager = CreateManager(logs);
        using var executor = new GatedCommandExecutor(manager, new BlockingRunner(), logs.Factory.CreateLogger<GatedCommandExecutor>());

        await Assert.ThrowsAsync<TimeoutException>(async () => await executor.ExecuteAsync(
            "ws-timeout", Path.Combine(TestTempRoot.Current, "T.csproj"), ["build"], TimeSpan.FromMilliseconds(150), CancellationToken.None));

        var fields = logs.Records("CommandGateCompleted").Single().GetProperty("fields");
        Assert.AreEqual("timeout", fields.GetProperty("outcome").GetString());
        Assert.AreEqual("project", fields.GetProperty("targetKind").GetString());
        Assert.AreEqual(JsonValueKind.Null, fields.GetProperty("exitCode").ValueKind);
    }

    [TestMethod]
    public async Task Gate_CallerCancellation_RecordsCancelledOutcome()
    {
        using var logs = new LogHarness();
        using var manager = CreateManager(logs);
        var runner = new BlockingRunner();
        using var executor = new GatedCommandExecutor(manager, runner, logs.Factory.CreateLogger<GatedCommandExecutor>());
        using var cancellation = new CancellationTokenSource();

        var pending = executor.ExecuteAsync(
            "ws-cancel", Path.Combine(TestTempRoot.Current, "C.csproj"), ["build"], TimeSpan.FromMinutes(1), cancellation.Token);
        await runner.Started.Task;
        await cancellation.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await pending);

        var fields = logs.Records("CommandGateCompleted").Single().GetProperty("fields");
        Assert.AreEqual("cancelled", fields.GetProperty("outcome").GetString());
    }

    [TestMethod]
    [TestCategory("RepoSolution")]
    public async Task WorkspaceLoad_RecordsSuccessReusedAndFailure()
    {
        using var logs = new LogHarness();
        using var manager = CreateManager(logs);
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var solution = TestFixtureFileSystem.CreateSampleSolutionCopy(
            repoRoot, Path.Combine(repoRoot, "samples", "SampleSolution", "SampleSolution.slnx"));

        var status = await manager.LoadAsync(solution, CancellationToken.None);
        var reusedStatus = await manager.LoadAsync(solution, CancellationToken.None);
        Assert.AreEqual(status.WorkspaceId, reusedStatus.WorkspaceId);
        await Assert.ThrowsAsync<Exception>(async () => await manager.LoadAsync(
            Path.Combine(Path.GetDirectoryName(solution)!, "Missing-" + Sentinel + ".slnx"), CancellationToken.None));

        var records = logs.Records("WorkspaceLoadCompleted");
        Assert.HasCount(3, records);
        foreach (var record in records)
        {
            CollectionAssert.AreEquivalent(LoadFields, record.GetProperty("fields").EnumerateObject().Select(static p => p.Name).ToArray());
            Assert.IsFalse(record.GetRawText().Contains(Sentinel, StringComparison.Ordinal));
            Assert.AreEqual(6, record.GetProperty("eventId").GetInt32());
        }

        var fresh = records[0].GetProperty("fields");
        Assert.AreEqual("succeeded", fresh.GetProperty("outcome").GetString());
        Assert.IsFalse(fresh.GetProperty("reused").GetBoolean());
        Assert.AreEqual(status.ProjectCount, fresh.GetProperty("projectCount").GetInt32());
        Assert.IsGreaterThanOrEqualTo(0L, fresh.GetProperty("durationMs").GetInt64());
        var reused = records[1].GetProperty("fields");
        Assert.AreEqual("succeeded", reused.GetProperty("outcome").GetString());
        Assert.IsTrue(reused.GetProperty("reused").GetBoolean());
        var failed = records[2].GetProperty("fields");
        Assert.AreEqual("failed", failed.GetProperty("outcome").GetString());
        Assert.AreEqual(JsonValueKind.Null, failed.GetProperty("projectCount").ValueKind);
    }

    [TestMethod]
    [TestCategory("RepoSolution")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task BoundaryRecords_JoinTheToolRecordByCorrelationIdInsideTheProductionDispatchScope()
    {
        using var logs = new LogHarness();
        using var manager = CreateManager(logs);
        using var services = new ServiceCollection().AddSingleton(logs.Factory).BuildServiceProvider();
        var runner = new DotnetCommandRunner(logs.Factory.CreateLogger<DotnetCommandRunner>());
        using var executor = new GatedCommandExecutor(manager, runner, logs.Factory.CreateLogger<GatedCommandExecutor>());
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var solution = TestFixtureFileSystem.CreateSampleSolutionCopy(
            repoRoot, Path.Combine(repoRoot, "samples", "SampleSolution", "SampleSolution.slnx"));

        await using var harness = await InMemoryMcpClientServerHarness.CreateAsync(
            transportName: "boundary-correlation",
            clientCapabilities: new ClientCapabilities(),
            clientHandlers: new McpClientHandlers(),
            disposalFailureContext: "boundary-correlation",
            cancellationToken: CancellationToken.None);
        var context = new RequestContext<CallToolRequestParams>(
            harness.Server,
            new JsonRpcRequest { Method = RequestMethods.ToolsCall },
            new CallToolRequestParams { Name = "boundary_probe" })
        {
            Services = services,
        };
        string? correlationId = null;
        var handler = StructuredCallToolFilter.Create(async (_, ct) =>
        {
            correlationId = RequestCorrelationContext.Current;
            await manager.LoadAsync(solution, ct);
            await executor.ExecuteAsync("ws-join", solution, ["--version"], TimeSpan.FromMinutes(1), ct);
            return new CallToolResult { Content = [new TextContentBlock { Text = "{}" }] };
        });

        // Production opens the correlation id in RequestCorrelationMessageFilter, outside this pipeline.
        using (RequestCorrelationContext.Begin())
        {
            await handler(context, CancellationToken.None);
        }

        Assert.IsFalse(string.IsNullOrEmpty(correlationId));
        foreach (var name in new[] { "ToolCompleted", "WorkspaceLoadCompleted", "CommandProcessCompleted", "CommandGateCompleted" })
        {
            var record = logs.Records(name).Single();
            Assert.AreEqual(correlationId, record.GetProperty("correlationId").GetString(), name);
        }
    }

    private static WorkspaceManager CreateManager(LogHarness logs)
    {
        MsBuildInitializer.EnsureInitialized();
        return new WorkspaceManager(
            logs.Factory.CreateLogger<WorkspaceManager>(),
            new PreviewStore(),
            new FileWatcherService(NullLogger<FileWatcherService>.Instance),
            new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 4 },
            cacheStore: null,
            sessionLoader: new WorkspaceSessionLoader());
    }

    private sealed class BlockingRunner : IDotnetCommandRunner
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _release.TrySetResult();

        public async Task<CommandExecutionDto> RunAsync(
            string workingDirectory, string targetPath, IReadOnlyList<string> arguments, CancellationToken ct)
        {
            Started.TrySetResult();
            await _release.Task.WaitAsync(ct);
            return new CommandExecutionDto(
                Command: "dotnet", Arguments: arguments, WorkingDirectory: workingDirectory, TargetPath: targetPath,
                ExitCode: 0, Succeeded: true, DurationMs: 1, StdOut: "", StdErr: "");
        }
    }
}
