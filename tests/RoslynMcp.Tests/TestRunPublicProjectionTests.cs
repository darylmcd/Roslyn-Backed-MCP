using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Host.Stdio.Middleware;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class TestRunPublicProjectionTests
{
    private const string _sensitiveFilter = "FullyQualifiedName~PRIVATE-FILTER-SENTINEL";

    private static readonly string _sensitiveWorkingDirectory = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "roslynmcp-private-workspace-sentinel"));

    private static readonly string _sensitiveTargetPath = Path.Combine(
        _sensitiveWorkingDirectory,
        "Sensitive.Tests.csproj");

    private static readonly string _sensitiveResultsDirectory = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "roslynmcp-private-results-sentinel"));

    [TestMethod]
    public async Task RunTests_OrdinaryResult_RedactsPublicExecutionWithoutMutatingInternalDiagnostics()
    {
        var result = CreateSensitiveTestRunResult(isTimeout: false);
        var runner = new FixedTestRunnerService(result);

        var json = await ValidationTools.RunTests(
            new PassthroughGate(), runner, workspaceId: "ws-public-execution-success",
            projectName: null, filter: _sensitiveFilter,
            progress: null, ct: CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        AssertPublicExecutionIsRedacted(document.RootElement.GetProperty("execution"), json);
        Assert.AreEqual(JsonValueKind.Null, document.RootElement.GetProperty("failureEnvelope").ValueKind);
        AssertInternalExecutionIsUnchanged(result.Execution);
    }

    [TestMethod]
    public async Task RunTests_TimeoutResult_RedactsExecutionAndFailureOutputTails()
    {
        var result = CreateSensitiveTestRunResult(isTimeout: true);
        var runner = new FixedTestRunnerService(result);

        var json = await ValidationTools.RunTests(
            new PassthroughGate(), runner, workspaceId: "ws-public-execution-timeout",
            projectName: null, filter: _sensitiveFilter,
            progress: null, ct: CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        AssertPublicExecutionIsRedacted(root.GetProperty("execution"), json);

        var envelope = root.GetProperty("failureEnvelope");
        Assert.AreEqual("Timeout", envelope.GetProperty("errorKind").GetString());
        AssertPublicTextIsRedacted(envelope.GetProperty("summary").GetString(), "failureEnvelope.summary");
        AssertPublicTextIsRedacted(envelope.GetProperty("stdOutTail").GetString(), "failureEnvelope.stdOutTail");
        AssertPublicTextIsRedacted(envelope.GetProperty("stdErrTail").GetString(), "failureEnvelope.stdErrTail");
        AssertInternalExecutionIsUnchanged(result.Execution);
    }

    [TestMethod]
    public void ValidateWorkspace_EmbeddedTestRun_UsesTheSamePublicProjection()
    {
        var result = CreateSensitiveTestRunResult(isTimeout: true);
        var dto = new WorkspaceValidationDto(
            OverallStatus: "timeout",
            ChangedFilePaths: [],
            UnknownFilePaths: [],
            CompileResult: new CompileCheckDto(
                Success: false,
                ErrorCount: 0,
                WarningCount: 0,
                TotalDiagnostics: 0,
                ReturnedDiagnostics: 0,
                Offset: 0,
                Limit: 200,
                HasMore: false,
                Diagnostics: [],
                ElapsedMs: 1,
                Cancelled: true),
            ErrorDiagnostics: [],
            ErrorCount: 0,
            WarningCount: 0,
            DiscoveredTests: [],
            DotnetTestFilter: null,
            TestRunResult: result,
            Warnings: []);

        var json = ValidationBundleTools.RenderValidationResponse(dto, responseFormat: null);

        using var document = JsonDocument.Parse(json);
        var publicTestRun = document.RootElement.GetProperty("testRunResult");
        AssertPublicExecutionIsRedacted(publicTestRun.GetProperty("execution"), json);
        AssertPublicTextIsRedacted(
            publicTestRun.GetProperty("failureEnvelope").GetProperty("stdErrTail").GetString(),
            "testRunResult.failureEnvelope.stdErrTail");
        AssertInternalExecutionIsUnchanged(result.Execution);
    }

    [TestMethod]
    public async Task BuildWorkspace_SharedCommandExecution_UsesTheSamePublicProjection()
    {
        var execution = CreateSensitiveTestRunResult(isTimeout: false).Execution;
        var buildResult = new BuildResultDto(
            execution,
            Diagnostics: [],
            ErrorCount: 0,
            WarningCount: 0);

        var json = await ValidationTools.BuildWorkspace(
            new PassthroughGate(),
            new FixedBuildService(buildResult),
            workspaceId: "ws-public-build-execution",
            progress: null,
            ct: CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        AssertPublicExecutionIsRedacted(document.RootElement.GetProperty("execution"), json);
        AssertInternalExecutionIsUnchanged(execution);
    }

    [TestMethod]
    public void RunTests_ShortFilterRepeatedInCapturedOutput_DoesNotLeak()
    {
        const string shortFilter = "e";
        var repeatedOutput = new string(shortFilter[0], 12_000);
        var execution = new CommandExecutionDto(
            Command: "dotnet",
            Arguments: ["test", "--filter", shortFilter],
            WorkingDirectory: ".",
            TargetPath: "Sample.Tests.csproj",
            ExitCode: -1,
            Succeeded: false,
            DurationMs: 10,
            StdOut: repeatedOutput,
            StdErr: repeatedOutput,
            EarlyKillReason: shortFilter);
        var result = new TestRunResultDto(
            execution,
            Total: 0,
            Passed: 0,
            Failed: 1,
            Skipped: 0,
            Failures: [],
            FailureEnvelope: new TestRunFailureEnvelopeDto(
                ErrorKind: "Timeout",
                IsRetryable: false,
                Summary: shortFilter,
                StdOutTail: execution.StdOut,
                StdErrTail: execution.StdErr));

        var projected = TestRunPublicProjection.Create(result);

        CollectionAssert.Contains(projected.Execution.Arguments.ToList(), TestRunPublicProjection.RedactedValue);
        Assert.AreEqual(execution.StdOut.Length, projected.Execution.StdOut.Length);
        Assert.AreEqual(execution.StdErr.Length, projected.Execution.StdErr.Length);
        Assert.IsFalse(projected.Execution.StdOut.Contains(shortFilter, StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(projected.Execution.StdErr.Contains(shortFilter, StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(projected.Execution.EarlyKillReason!.Contains(shortFilter, StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(projected.FailureEnvelope!.Summary.Contains(shortFilter, StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(projected.FailureEnvelope.StdOutTail!.Contains(shortFilter, StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(projected.FailureEnvelope.StdErrTail!.Contains(shortFilter, StringComparison.OrdinalIgnoreCase));
        CollectionAssert.Contains(execution.Arguments.ToList(), shortFilter);
        StringAssert.Contains(execution.StdOut, shortFilter);
        StringAssert.Contains(execution.StdErr, shortFilter);
        StringAssert.Contains(execution.EarlyKillReason, shortFilter);
        StringAssert.Contains(result.FailureEnvelope!.Summary, shortFilter);
    }

    [TestMethod]
    public void CommandExecutionProjection_PreservesLegacyPropertySetAndNonSensitiveValues()
    {
        var execution = new CommandExecutionDto(
            Command: "dotnet",
            Arguments: ["build", "Sample.csproj"],
            WorkingDirectory: ".",
            TargetPath: "Sample.csproj",
            ExitCode: 0,
            Succeeded: true,
            DurationMs: 42,
            StdOut: "ordinary stdout",
            StdErr: "ordinary stderr",
            EarlyKillReason: "ordinary reason");

        var projected = JsonSerializer.SerializeToElement(
            TestRunPublicProjection.CreateExecution(execution),
            JsonDefaults.Indented);

        CollectionAssert.AreEqual(
            new[]
            {
                "command",
                "arguments",
                "workingDirectory",
                "targetPath",
                "exitCode",
                "succeeded",
                "durationMs",
                "stdOut",
                "stdErr",
                "earlyKillReason",
            },
            projected.EnumerateObject().Select(static property => property.Name).ToArray());
        Assert.AreEqual(execution.Command, projected.GetProperty("command").GetString());
        Assert.AreEqual(execution.ExitCode, projected.GetProperty("exitCode").GetInt32());
        Assert.AreEqual(execution.Succeeded, projected.GetProperty("succeeded").GetBoolean());
        Assert.AreEqual(execution.DurationMs, projected.GetProperty("durationMs").GetInt64());
        Assert.AreEqual(execution.StdOut, projected.GetProperty("stdOut").GetString());
        Assert.AreEqual(execution.StdErr, projected.GetProperty("stdErr").GetString());
        Assert.AreEqual(execution.EarlyKillReason, projected.GetProperty("earlyKillReason").GetString());
    }

    [TestMethod]
    public void TestRunProjection_PreservesLegacyPropertySetAndFailureMetadata()
    {
        var execution = new CommandExecutionDto(
            "dotnet", ["test"], ".", "Sample.Tests.csproj", 1, false, 10, "out", "err");
        var failure = new TestFailureDto("sample", "Tests.Sample", "failed", "stack");
        var envelope = new TestRunFailureEnvelopeDto("BuildFailure", false, "summary", "out", "err");
        var result = new TestRunResultDto(execution, 3, 1, 1, 1, [failure], envelope);

        var projected = JsonSerializer.SerializeToElement(
            TestRunPublicProjection.Create(result),
            JsonDefaults.Indented);

        CollectionAssert.AreEqual(
            new[]
            {
                "execution",
                "total",
                "passed",
                "failed",
                "skipped",
                "failures",
                "failureEnvelope",
            },
            projected.EnumerateObject().Select(static property => property.Name).ToArray());
        Assert.AreEqual(3, projected.GetProperty("total").GetInt32());
        Assert.AreEqual("Tests.Sample", projected.GetProperty("failures")[0]
            .GetProperty("fullyQualifiedName").GetString());
        Assert.AreEqual("BuildFailure", projected.GetProperty("failureEnvelope")
            .GetProperty("errorKind").GetString());
        Assert.AreEqual("summary", projected.GetProperty("failureEnvelope")
            .GetProperty("summary").GetString());
    }

    [TestMethod]
    public async Task RunTests_RawWireProjection_RedactsKnownInputsAcrossBothProtocolEras()
    {
        const string shortFilter = "zQ7";
        var protocolEras = new (string? Requested, string Expected)[]
        {
            ("2025-11-25", "2025-11-25"),
            (null, "2026-07-28"),
        };

        foreach (var (requested, expected) in protocolEras)
        {
            var result = CreateSensitiveTestRunResult(isTimeout: true, shortFilter);
            await using var harness = await CreateWireHarnessAsync(result, requested, expected);
            Assert.AreEqual(expected, harness.Client.NegotiatedProtocolVersion);

            var priorMessageCount = harness.RawServerMessages.Count;
            var callResult = await harness.Client.CallToolAsync(
                "test_run",
                new Dictionary<string, object?>
                {
                    ["workspaceId"] = "wire-workspace",
                    ["filter"] = shortFilter,
                },
                cancellationToken: CancellationToken.None);

            Assert.IsFalse(callResult.IsError is true);
            var rawFrame = FindSingleNewResultFrame(harness.RawServerMessages, priorMessageCount);
            Assert.IsFalse(rawFrame.Contains(shortFilter, StringComparison.Ordinal));
            Assert.IsFalse(rawFrame.Contains(
                "roslynmcp-private-workspace-sentinel",
                StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(rawFrame.Contains(
                "roslynmcp-private-results-sentinel",
                StringComparison.OrdinalIgnoreCase));
            using var publicPayload = ParseTextPayload(rawFrame);
            var publicJson = publicPayload.RootElement.GetRawText();
            var publicExecution = publicPayload.RootElement.GetProperty("execution");
            AssertPublicExecutionIsRedacted(publicExecution, publicJson);
            var publicEnvelope = publicPayload.RootElement.GetProperty("failureEnvelope");
            AssertPublicTextIsRedacted(
                publicEnvelope.GetProperty("summary").GetString(),
                "wire.failureEnvelope.summary");
            AssertPublicTextIsRedacted(
                publicEnvelope.GetProperty("stdOutTail").GetString(),
                "wire.failureEnvelope.stdOutTail");
            AssertPublicTextIsRedacted(
                publicEnvelope.GetProperty("stdErrTail").GetString(),
                "wire.failureEnvelope.stdErrTail");
            var arguments = publicPayload.RootElement.GetProperty("execution")
                .GetProperty("arguments")
                .EnumerateArray()
                .Select(static argument => argument.GetString())
                .ToArray();
            CollectionAssert.Contains(arguments, TestRunPublicProjection.RedactedValue);
            CollectionAssert.Contains(arguments, TestRunPublicProjection.RedactedResultsDirectory);
        }
    }

    private static TestRunResultDto CreateSensitiveTestRunResult(
        bool isTimeout,
        string? sensitiveFilter = null)
    {
        sensitiveFilter ??= _sensitiveFilter;
        var output =
            $"target={_sensitiveTargetPath}; results={_sensitiveResultsDirectory}; filter={sensitiveFilter}";
        var execution = new CommandExecutionDto(
            Command: "dotnet",
            Arguments:
            [
                "test",
                _sensitiveTargetPath,
                "--results-directory",
                _sensitiveResultsDirectory,
                "--filter",
                sensitiveFilter,
            ],
            WorkingDirectory: _sensitiveWorkingDirectory,
            TargetPath: _sensitiveTargetPath,
            ExitCode: isTimeout ? -1 : 0,
            Succeeded: !isTimeout,
            DurationMs: isTimeout ? 600_000 : 125,
            StdOut: "stdout " + output,
            StdErr: "stderr " + output);

        return new TestRunResultDto(
            execution,
            Total: isTimeout ? 0 : 1,
            Passed: isTimeout ? 0 : 1,
            Failed: isTimeout ? 1 : 0,
            Skipped: 0,
            Failures: [],
            FailureEnvelope: isTimeout
                ? new TestRunFailureEnvelopeDto(
                    ErrorKind: "Timeout",
                    IsRetryable: false,
                    Summary: "timeout " + output,
                    StdOutTail: execution.StdOut,
                    StdErrTail: execution.StdErr)
                : null);
    }

    private static async Task<InMemoryMcpClientServerHarness> CreateWireHarnessAsync(
        TestRunResultDto result,
        string? requestedProtocolVersion,
        string expectedProtocolVersion)
    {
        var services = new ServiceCollection();
        services.AddLogging(static logging => logging.ClearProviders());
        services.AddRoslynMcpHostServices(
            new WorkspaceManagerOptions(),
            new ValidationServiceOptions(),
            new PreviewStoreOptions(),
            new ExecutionGateOptions(),
            new SecurityOptions { PathValidationFailOpen = true },
            new ScriptingServiceOptions());
        services.AddSingleton<IWorkspaceExecutionGate>(new PassthroughGate());
        services.AddSingleton<ITestRunnerService>(new FixedTestRunnerService(result));
        services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation
                {
                    Name = "test-run-public-wire",
                    Version = "1.0.0",
                };
            })
            .WithToolsFromAssembly(typeof(HostAssemblyMarker).Assembly)
            .WithResourcesFromAssembly(typeof(HostAssemblyMarker).Assembly)
            .WithPromptsFromAssembly(typeof(HostAssemblyMarker).Assembly)
            .WithRequestFilters(static filters =>
                filters.AddCallToolFilter(StructuredCallToolFilter.Create));
        services.AddRoslynMcpSurfaceRegistrationPolicy(ToolTierSelection.All);

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;
        return await InMemoryMcpClientServerHarness.CreateAsync(
            transportName: $"test-run-public-wire-{expectedProtocolVersion}",
            clientCapabilities: new ClientCapabilities(),
            clientHandlers: new McpClientHandlers(),
            disposalFailureContext: "test-run-public-wire",
            cancellationToken: CancellationToken.None,
            protocolVersion: requestedProtocolVersion,
            serverOptions: options,
            serverServicesFactory: () => provider,
            captureServerMessages: true);
    }

    private static string FindSingleNewResultFrame(
        IReadOnlyList<string> rawMessages,
        int priorMessageCount)
    {
        var resultFrames = rawMessages
            .Skip(priorMessageCount)
            .Where(static rawMessage =>
            {
                using var document = JsonDocument.Parse(rawMessage);
                return document.RootElement.TryGetProperty("result", out _);
            })
            .ToArray();

        Assert.HasCount(1, resultFrames);
        return resultFrames[0];
    }

    private static JsonDocument ParseTextPayload(string rawFrame)
    {
        using var frame = JsonDocument.Parse(rawFrame);
        var text = frame.RootElement.GetProperty("result")
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString();
        Assert.IsNotNull(text);
        return JsonDocument.Parse(text);
    }

    private static void AssertPublicExecutionIsRedacted(JsonElement execution, string json)
    {
        Assert.AreEqual(".", execution.GetProperty("workingDirectory").GetString());
        Assert.AreEqual("Sensitive.Tests.csproj", execution.GetProperty("targetPath").GetString());

        var arguments = execution.GetProperty("arguments")
            .EnumerateArray()
            .Select(static argument => argument.GetString())
            .ToArray();
        CollectionAssert.AreEqual(
            new string?[]
            {
                "test",
                "Sensitive.Tests.csproj",
                "--results-directory",
                TestRunPublicProjection.RedactedResultsDirectory,
                "--filter",
                TestRunPublicProjection.RedactedValue,
            },
            arguments);

        AssertPublicTextIsRedacted(execution.GetProperty("stdOut").GetString(), "execution.stdOut");
        AssertPublicTextIsRedacted(execution.GetProperty("stdErr").GetString(), "execution.stdErr");
        AssertPublicTextIsRedacted(json, "serialized response");
    }

    private static void AssertPublicTextIsRedacted(string? value, string field)
    {
        Assert.IsNotNull(value, $"{field} should remain available for diagnosis.");
        Assert.IsFalse(value.Contains(_sensitiveFilter, StringComparison.Ordinal),
            $"{field} leaked the caller-supplied filter: {value}");
        Assert.IsFalse(value.Contains("roslynmcp-private-workspace-sentinel", StringComparison.OrdinalIgnoreCase),
            $"{field} leaked the workspace path: {value}");
        Assert.IsFalse(value.Contains("roslynmcp-private-results-sentinel", StringComparison.OrdinalIgnoreCase),
            $"{field} leaked the results path: {value}");
    }

    private static void AssertInternalExecutionIsUnchanged(CommandExecutionDto execution)
    {
        Assert.AreEqual(_sensitiveWorkingDirectory, execution.WorkingDirectory);
        Assert.AreEqual(_sensitiveTargetPath, execution.TargetPath);
        CollectionAssert.Contains(execution.Arguments.ToList(), _sensitiveResultsDirectory);
        CollectionAssert.Contains(execution.Arguments.ToList(), _sensitiveFilter);
        StringAssert.Contains(execution.StdOut, _sensitiveTargetPath);
        StringAssert.Contains(execution.StdErr, _sensitiveFilter);
    }

    private sealed class FixedTestRunnerService(TestRunResultDto result) : ITestRunnerService
    {
        public Task<TestRunResultDto> RunTestsAsync(
            string workspaceId, string? projectName, string? filter, CancellationToken ct) =>
            Task.FromResult(result);
    }

    private sealed class FixedBuildService(BuildResultDto result) : IBuildService
    {
        public Task<BuildResultDto> BuildWorkspaceAsync(string workspaceId, CancellationToken ct) =>
            Task.FromResult(result);

        public Task<BuildResultDto> BuildProjectAsync(
            string workspaceId, string projectName, CancellationToken ct) =>
            Task.FromResult(result);
    }

    private sealed class PassthroughGate : IWorkspaceExecutionGate
    {
        public Task<T> RunReadAsync<T>(
            string workspaceId, Func<CancellationToken, Task<T>> action, CancellationToken ct) =>
            action(ct);

        public Task<T> RunWriteAsync<T>(
            string workspaceId,
            Func<CancellationToken, Task<T>> action,
            CancellationToken ct,
            bool applyStalenessPolicy = true) =>
            action(ct);

        public Task<T> RunLoadGateAsync<T>(
            Func<CancellationToken, Task<T>> action, CancellationToken ct) =>
            action(ct);

        public void RemoveGate(string workspaceId) { }
    }
}
