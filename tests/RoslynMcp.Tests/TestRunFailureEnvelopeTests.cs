using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// Covers the structured failure envelope for <c>test_run</c> — backlog row
/// <c>test-run-bare-exception-envelope</c>. When <c>dotnet test</c> exits without
/// TRX output (MSBuild file locks, build failures, timeouts) the parser must
/// emit a typed envelope instead of letting a bare invocation error escape to
/// ToolErrorHandler.
/// </summary>
[TestClass]
public sealed class TestRunFailureEnvelopeTests
{
    private static CommandExecutionDto FakeExecution(int exitCode, string stdOut, string stdErr, string? earlyKillReason = null) =>
        new(
            Command: "dotnet",
            Arguments: ["test", "sample.csproj", "--nologo"],
            WorkingDirectory: "C:/fake/workdir",
            TargetPath: "C:/fake/workdir/sample.csproj",
            ExitCode: exitCode,
            Succeeded: exitCode == 0,
            DurationMs: 1234,
            StdOut: stdOut,
            StdErr: stdErr,
            EarlyKillReason: earlyKillReason);

    [TestMethod]
    public void ParseTestRun_SuccessWithNoTrx_LeavesEnvelopeNull()
    {
        var execution = FakeExecution(exitCode: 0, stdOut: "Test run complete.", stdErr: string.Empty);

        var result = DotnetOutputParser.ParseTestRun(execution, []);

        Assert.IsNull(result.FailureEnvelope, "Successful runs must not carry a failure envelope.");
        Assert.AreEqual(0, result.Total);
        Assert.AreEqual(0, result.Failed);
    }

    [TestMethod]
    public void ParseTestRun_NoTrxAndStdErrMsb3027_EmitsRetryableFileLockEnvelope()
    {
        const string stdErr =
            "error MSB3027: Could not copy \"obj/Debug/net10.0/RoslynMcp.Tests.dll\" to \"bin/Debug/net10.0/RoslynMcp.Tests.dll\". " +
            "Exceeded retry count of 10. Failed. The file is locked by: \"testhost.exe (12345)\"";
        var execution = FakeExecution(exitCode: 1, stdOut: "Build started", stdErr: stdErr);

        var result = DotnetOutputParser.ParseTestRun(execution, []);

        Assert.IsNotNull(result.FailureEnvelope, "File lock failure must populate the envelope.");
        Assert.AreEqual("FileLock", result.FailureEnvelope!.ErrorKind);
        Assert.IsTrue(result.FailureEnvelope.IsRetryable,
            "MSBuild file-lock failures are transient and should be marked retryable.");
        StringAssert.Contains(result.FailureEnvelope.StdErrTail ?? string.Empty, "MSB3027");
        StringAssert.Contains(result.FailureEnvelope.Summary, "testhost.exe");
        Assert.AreEqual(1, result.Failed);
    }

    [TestMethod]
    public void ParseTestRun_NoTrxAndStdOutMsb3021_EmitsRetryableFileLockEnvelope()
    {
        // MSB3021 variant — the lock may surface in StdOut rather than StdErr depending
        // on how dotnet test forwards child-process streams. Both paths must classify.
        const string stdOut = "CSC : error MSB3021: Unable to copy file. Access to the path is denied.";
        var execution = FakeExecution(exitCode: 1, stdOut: stdOut, stdErr: string.Empty);

        var result = DotnetOutputParser.ParseTestRun(execution, []);

        Assert.IsNotNull(result.FailureEnvelope);
        Assert.AreEqual("FileLock", result.FailureEnvelope!.ErrorKind);
        Assert.IsTrue(result.FailureEnvelope.IsRetryable);
    }

    [TestMethod]
    public void ParseTestRun_NoTrxAndBuildFailedMarker_EmitsNonRetryableBuildFailure()
    {
        const string stdOut = "CS0103: The name 'Oops' does not exist in the current context.\nBuild FAILED.";
        var execution = FakeExecution(exitCode: 1, stdOut: stdOut, stdErr: string.Empty);

        var result = DotnetOutputParser.ParseTestRun(execution, []);

        Assert.IsNotNull(result.FailureEnvelope);
        Assert.AreEqual("BuildFailure", result.FailureEnvelope!.ErrorKind);
        Assert.IsFalse(result.FailureEnvelope.IsRetryable,
            "Build failures require a source fix before retrying.");
        StringAssert.Contains(result.FailureEnvelope.StdOutTail ?? string.Empty, "CS0103");
    }

    [TestMethod]
    public void ParseTestRun_NoTrxAndUnknownFailure_EmitsUnknownEnvelope()
    {
        var execution = FakeExecution(exitCode: 139, stdOut: "something exploded", stdErr: "segfault");

        var result = DotnetOutputParser.ParseTestRun(execution, []);

        Assert.IsNotNull(result.FailureEnvelope);
        Assert.AreEqual("Unknown", result.FailureEnvelope!.ErrorKind);
        Assert.IsFalse(result.FailureEnvelope.IsRetryable);
        StringAssert.Contains(result.FailureEnvelope.Summary, "139");
    }

    [TestMethod]
    public void ParseTestRun_NoTrxFailure_TailsAreTruncatedTo2000Chars()
    {
        var longStdErr = new string('x', 5000) + "MSB3027";
        var execution = FakeExecution(exitCode: 1, stdOut: string.Empty, stdErr: longStdErr);

        var result = DotnetOutputParser.ParseTestRun(execution, []);

        Assert.IsNotNull(result.FailureEnvelope);
        Assert.IsNotNull(result.FailureEnvelope!.StdErrTail);
        Assert.AreEqual(2000, result.FailureEnvelope.StdErrTail!.Length,
            "StdErr tail should be capped at 2000 characters.");
        StringAssert.EndsWith(result.FailureEnvelope.StdErrTail, "MSB3027",
            "The tail must include the end of the StdErr stream, not the beginning.");
    }

    [TestMethod]
    public void ParseTestRun_EarlyKillReasonForFileLock_EmitsTerminatedEarlySummary()
    {
        // Item 4: simulate the runner killing dotnet test after the first MSB3027 line and
        // surfacing EarlyKillReason. The parser should classify as FileLock + retryable AND
        // surface the short-duration summary so callers see the fast-fail path, not the
        // ~10s exhausted-retry path.
        var execution = FakeExecution(
            exitCode: -1,
            stdOut: "Build started",
            stdErr: "error MSB3027: Could not copy ...",
            earlyKillReason: "MSBuild file lock (MSB3027/MSB3021)");

        var result = DotnetOutputParser.ParseTestRun(execution, []);

        Assert.IsNotNull(result.FailureEnvelope);
        Assert.AreEqual("FileLock", result.FailureEnvelope!.ErrorKind);
        Assert.IsTrue(result.FailureEnvelope.IsRetryable);
        StringAssert.Contains(result.FailureEnvelope.Summary, "terminated early");
        StringAssert.Contains(result.FailureEnvelope.Summary, "MSB3027/MSB3021");
    }

    [TestMethod]
    public void BuildTimeoutResult_ProducesNonRetryableTimeoutEnvelope()
    {
        var shell = new CommandExecutionDto(
            Command: "dotnet",
            Arguments: ["test"],
            WorkingDirectory: "C:/fake",
            TargetPath: "C:/fake/proj.csproj",
            ExitCode: -1,
            Succeeded: false,
            DurationMs: 600_000,
            StdOut: string.Empty,
            StdErr: "The command 'dotnet test' exceeded the timeout of 10.0 minute(s).");

        var result = DotnetOutputParser.BuildTimeoutResult(shell, shell.StdErr);

        Assert.IsNotNull(result.FailureEnvelope);
        Assert.AreEqual("Timeout", result.FailureEnvelope!.ErrorKind);
        Assert.IsFalse(result.FailureEnvelope.IsRetryable);
        StringAssert.Contains(result.FailureEnvelope.Summary, "timeout");
        Assert.AreEqual(1, result.Failed);
    }

    // ---------------------------------------------------------------------------------------
    // test-run-failures-pagination-truncation
    //
    // test_run serialized TestRunResultDto whole: the Failures list had no cap on COUNT and each
    // entry's Message/StackTrace was unbounded in LENGTH — unlike StdOut/StdErr, which
    // DotnetCommandRunner already bounds to 12000 chars, and unlike test_discover, which already
    // pages its testProjects array. A broad/unfiltered run over a large suite therefore produced
    // a payload that grew linearly with the failure count.
    //
    // Cap investigation (the row's acceptance criterion — measured, not assumed): there is NO
    // response-size constant anywhere to guard against. `rg` over src/ finds no
    // MaximumResponseSize/MaxResponseSize/ResponseSizeLimit/OutputSizeLimit, and a symbol sweep
    // over the pinned ModelContextProtocol 2.1.0 assemblies finds no
    // MaxMessageSize/MaximumMessageSize/MaxResponseSize/SizeLimit either. The MCP protocol
    // imposes no ceiling; the real ceiling is the consuming client's context budget. The tests
    // below therefore MEASURE the serialized payload rather than comparing it to a constant, and
    // pin the measured bound so a regression that removes either half of the fix shows up as a
    // size assertion failure.
    //
    // A large SYNTHETIC failure set is used instead of this repo's real 1000+-test suite — same
    // signal, without spinning up a slow `dotnet test` invocation.
    // ---------------------------------------------------------------------------------------

    [TestMethod]
    public void ParseTestRun_FailureWithHugeMessageAndStackTrace_TruncatesFromTheHead()
    {
        var hugeMessage = "Assert.Equal() Failure: expected [A] but got [B]. " + new string('m', 5000);
        var hugeStackTrace = "   at RoslynMcp.Tests.SomeFixture.TestMethod() line 1\n" + new string('s', 5000);
        var trxPath = WriteTrxFixture([("RoslynMcp.Tests.SomeFixture.TestMethod", hugeMessage, hugeStackTrace)]);

        try
        {
            var execution = FakeExecution(exitCode: 1, stdOut: "Test run complete.", stdErr: string.Empty);
            var result = DotnetOutputParser.ParseTestRun(execution, [trxPath]);

            Assert.AreEqual(1, result.Failures.Count);
            var failure = result.Failures[0];

            Assert.AreEqual(500 + TruncationMarker.Length, failure.Message.Length,
                "A 5000+ char failure message must be head-truncated to exactly 500 chars + the marker.");
            StringAssert.StartsWith(failure.Message, "Assert.Equal() Failure: expected [A] but got [B].",
                "Truncation must keep the HEAD (the assertion text), not the tail.");
            StringAssert.EndsWith(failure.Message, TruncationMarker);

            Assert.IsNotNull(failure.StackTrace);
            Assert.AreEqual(1500 + TruncationMarker.Length, failure.StackTrace!.Length,
                "A 5000+ char stack trace must be head-truncated to exactly 1500 chars + the marker.");
            StringAssert.StartsWith(failure.StackTrace, "   at RoslynMcp.Tests.SomeFixture.TestMethod() line 1",
                "Truncation must keep the HEAD (the throw-site frame), not the tail.");
            StringAssert.EndsWith(failure.StackTrace, TruncationMarker);
        }
        finally
        {
            File.Delete(trxPath);
        }
    }

    [TestMethod]
    public void ParseTestRun_FailureAtExactlyTheLimit_PassesThroughUnchanged()
    {
        // Boundary: the marker must appear only when the value is STRICTLY longer than the cap.
        var exactMessage = new string('m', 500);
        var exactStackTrace = new string('s', 1500);
        var trxPath = WriteTrxFixture([("RoslynMcp.Tests.SomeFixture.AtLimit", exactMessage, exactStackTrace)]);

        try
        {
            var execution = FakeExecution(exitCode: 1, stdOut: string.Empty, stdErr: string.Empty);
            var result = DotnetOutputParser.ParseTestRun(execution, [trxPath]);

            Assert.AreEqual(1, result.Failures.Count);
            Assert.AreEqual(exactMessage, result.Failures[0].Message,
                "A message exactly at the cap must pass through byte-identical, with no marker.");
            Assert.AreEqual(exactStackTrace, result.Failures[0].StackTrace,
                "A stack trace exactly at the cap must pass through byte-identical, with no marker.");
        }
        finally
        {
            File.Delete(trxPath);
        }
    }

    [TestMethod]
    public void ParseTestRun_FailureWithoutStackTrace_LeavesStackTraceNull()
    {
        // StackTrace is stackEl?.Value — a TRX failure with no <StackTrace> element must keep
        // flowing through as null, not become an empty string or a marker-only value.
        var trxPath = WriteTrxFixture(
            [("RoslynMcp.Tests.SomeFixture.NoStack", "Assert failed.", null)]);

        try
        {
            var execution = FakeExecution(exitCode: 1, stdOut: string.Empty, stdErr: string.Empty);
            var result = DotnetOutputParser.ParseTestRun(execution, [trxPath]);

            Assert.AreEqual(1, result.Failures.Count);
            Assert.AreEqual("Assert failed.", result.Failures[0].Message);
            Assert.IsNull(result.Failures[0].StackTrace);
        }
        finally
        {
            File.Delete(trxPath);
        }
    }

    [TestMethod]
    public async Task RunTests_LargeUnfilteredFailureCount_PaginatesInsteadOfUnboundedPayload()
    {
        const int syntheticFailureCount = 400;
        var runner = new LargeFailureSetTestRunnerService(
            total: 1247, passed: 847, failed: syntheticFailureCount, skipped: 0, failureCount: syntheticFailureCount);

        var json = await ValidationTools.RunTests(
            new PassthroughGate(),
            runner,
            workspaceId: "ws-test-run-large-failure-set",
            projectName: null,
            filter: null,
            progress: null,
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.IsFalse(root.TryGetProperty("error", out _),
            "A run that produced valid TRX output (even with many failures) must return a structured " +
            $"result, not an error envelope. Actual: {json[..Math.Min(json.Length, 500)]}");

        // Aggregate counts always reflect the FULL run, never truncated.
        Assert.AreEqual(1247, root.GetProperty("total").GetInt32());
        Assert.AreEqual(847, root.GetProperty("passed").GetInt32());
        Assert.AreEqual(syntheticFailureCount, root.GetProperty("failed").GetInt32());
        Assert.AreEqual(0, root.GetProperty("skipped").GetInt32());

        // The per-failure detail array is capped at the default limit (25), with paging metadata
        // so callers can tell more exist — same shape family as test_discover's offset/limit/hasMore.
        Assert.AreEqual(0, root.GetProperty("failuresOffset").GetInt32());
        Assert.AreEqual(25, root.GetProperty("failuresLimit").GetInt32());
        Assert.AreEqual(syntheticFailureCount, root.GetProperty("failuresTotal").GetInt32());
        Assert.IsTrue(root.GetProperty("hasMoreFailures").GetBoolean());
        Assert.AreEqual(25, root.GetProperty("failures").GetArrayLength(),
            "Default failuresLimit=25 must cap the returned array regardless of how many tests failed.");
    }

    [TestMethod]
    public async Task RunTests_WorstCasePayload_StaysWithinTheMeasuredCeiling()
    {
        // Acceptance criterion for test-run-failures-pagination-truncation: no MCP/SDK/in-repo
        // response-size constant exists (verified by grep over src/ and a symbol sweep over the
        // pinned ModelContextProtocol 2.1.0 assemblies), so the guard has to be an explicitly
        // MEASURED budget rather than a comparison against a protocol constant.
        //
        // This drives the true worst case at the shipped defaults: every returned failure carries
        // a Message and StackTrace already at the DotnetOutputParser caps (500 / 1500 chars).
        // MEASURED 2026-08-13 by this very test: 56,923 chars (~56KB) of indented JSON for the
        // default page of 25 — roughly 14k tokens, comparable to test_discover's documented
        // "1000 cases is roughly 350KB". The SAME 400-failure run with the count cap lifted
        // measures 904,200 chars (~883KB), ~16x larger — the unbounded shape this row fixes.
        const int syntheticFailureCount = 400;
        var runner = new LargeFailureSetTestRunnerService(
            total: 400, passed: 0, failed: syntheticFailureCount, skipped: 0,
            failureCount: syntheticFailureCount, maxLengthDetail: true);

        var json = await ValidationTools.RunTests(
            new PassthroughGate(),
            runner,
            workspaceId: "ws-test-run-worst-case",
            projectName: null,
            filter: null,
            progress: null,
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual(25, doc.RootElement.GetProperty("failures").GetArrayLength());

        Assert.IsTrue(json.Length < 80_000,
            "Worst-case paginated test_run response must stay within the measured ~56KB budget " +
            $"(80KB assertion headroom); was {json.Length} chars. Both halves of the fix are load-bearing: " +
            "per-entry truncation bounds each failure, pagination bounds the count.");

        // Sanity-check the counterfactual: the same run WITHOUT the count cap is an order of
        // magnitude larger, which is exactly the unbounded-payload shape this row fixes.
        var unpaginated = await ValidationTools.RunTests(
            new PassthroughGate(),
            runner,
            workspaceId: "ws-test-run-worst-case-unpaged",
            projectName: null,
            filter: null,
            failuresOffset: 0,
            failuresLimit: syntheticFailureCount,
            progress: null,
            ct: CancellationToken.None);

        Assert.IsTrue(unpaginated.Length > 500_000,
            "The unpaginated payload should be several hundred KB — this is the hazard the default " +
            $"failuresLimit guards against. Was {unpaginated.Length} chars.");
    }

    [TestMethod]
    public async Task RunTests_FailuresOffsetAndLimit_PageThroughAllFailures()
    {
        const int syntheticFailureCount = 30;
        var runner = new LargeFailureSetTestRunnerService(
            total: 30, passed: 0, failed: syntheticFailureCount, skipped: 0, failureCount: syntheticFailureCount);

        var firstPage = await ValidationTools.RunTests(
            new PassthroughGate(), runner, workspaceId: "ws-page-1", projectName: null, filter: null,
            failuresOffset: 0, failuresLimit: 20, progress: null, ct: CancellationToken.None);
        var secondPage = await ValidationTools.RunTests(
            new PassthroughGate(), runner, workspaceId: "ws-page-2", projectName: null, filter: null,
            failuresOffset: 20, failuresLimit: 20, progress: null, ct: CancellationToken.None);

        using var firstDoc = JsonDocument.Parse(firstPage);
        using var secondDoc = JsonDocument.Parse(secondPage);

        Assert.AreEqual(20, firstDoc.RootElement.GetProperty("failures").GetArrayLength());
        Assert.IsTrue(firstDoc.RootElement.GetProperty("hasMoreFailures").GetBoolean());

        Assert.AreEqual(10, secondDoc.RootElement.GetProperty("failures").GetArrayLength(),
            "The second page should return the remaining 10 failures (30 total - 20 already paged).");
        Assert.IsFalse(secondDoc.RootElement.GetProperty("hasMoreFailures").GetBoolean(),
            "hasMoreFailures must be false once the offset+returned count reaches the total.");

        // Paging must not perturb the aggregate counts on either page.
        Assert.AreEqual(30, firstDoc.RootElement.GetProperty("failed").GetInt32());
        Assert.AreEqual(30, secondDoc.RootElement.GetProperty("failed").GetInt32());
        Assert.AreEqual(30, secondDoc.RootElement.GetProperty("failuresTotal").GetInt32());
    }

    [TestMethod]
    public async Task RunTests_FailuresOffsetPastTotal_ReturnsEmptyPageNotAnError()
    {
        var runner = new LargeFailureSetTestRunnerService(
            total: 5, passed: 0, failed: 5, skipped: 0, failureCount: 5);

        var json = await ValidationTools.RunTests(
            new PassthroughGate(), runner, workspaceId: "ws-page-past-end", projectName: null, filter: null,
            failuresOffset: 500, failuresLimit: 25, progress: null, ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.IsFalse(root.TryGetProperty("error", out _),
            $"An offset past the end is an empty page, not an error. Actual: {json}");
        Assert.AreEqual(0, root.GetProperty("failures").GetArrayLength());
        Assert.AreEqual(5, root.GetProperty("failuresTotal").GetInt32());
        Assert.IsFalse(root.GetProperty("hasMoreFailures").GetBoolean(),
            "hasMoreFailures must be false past the end, not true.");
    }

    [TestMethod]
    public async Task RunTests_TimeoutEnvelopePath_StillCarriesPagingFields()
    {
        // The FailureEnvelope path emits Failed=1 with an EMPTY Failures list. The paging fields
        // must still be present (and coherent) so a client never has to branch on their absence.
        var runner = new TimeoutEnvelopeTestRunnerService();

        var json = await ValidationTools.RunTests(
            new PassthroughGate(), runner, workspaceId: "ws-timeout-envelope", projectName: null, filter: null,
            progress: null, ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.AreEqual(1, root.GetProperty("failed").GetInt32());
        Assert.AreEqual("Timeout", root.GetProperty("failureEnvelope").GetProperty("errorKind").GetString());
        Assert.AreEqual(0, root.GetProperty("failures").GetArrayLength());
        Assert.AreEqual(0, root.GetProperty("failuresTotal").GetInt32());
        Assert.AreEqual(0, root.GetProperty("failuresOffset").GetInt32());
        Assert.AreEqual(25, root.GetProperty("failuresLimit").GetInt32());
        Assert.IsFalse(root.GetProperty("hasMoreFailures").GetBoolean());
    }

    [TestMethod]
    [DataRow(0, 0, "failuresLimit")]
    [DataRow(0, -1, "failuresLimit")]
    [DataRow(-1, 25, "failuresOffset")]
    public async Task RunTests_InvalidPagingArguments_ReturnInvalidArgumentEnvelope(
        int failuresOffset, int failuresLimit, string expectedParameter)
    {
        // Mirrors DiscoverTests' guard, which also lives INSIDE the try — so the ArgumentException
        // is classified into the tool's structured InvalidArgument envelope rather than escaping.
        var runner = new LargeFailureSetTestRunnerService(
            total: 1, passed: 0, failed: 1, skipped: 0, failureCount: 1);

        var json = await ValidationTools.RunTests(
            new PassthroughGate(), runner, workspaceId: "ws-bad-paging", projectName: null, filter: null,
            failuresOffset: failuresOffset, failuresLimit: failuresLimit,
            progress: null, ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.IsTrue(root.GetProperty("error").GetBoolean(), $"Envelope: {json}");
        Assert.AreEqual("InvalidArgument", root.GetProperty("category").GetString());
        Assert.AreEqual("test_run", root.GetProperty("tool").GetString());
        StringAssert.Contains(root.GetProperty("message").GetString() ?? string.Empty, expectedParameter);
    }

    /// <summary>
    /// Mirrors <c>DotnetOutputParser.TruncationMarker</c>. Kept as a literal here on purpose: the
    /// marker is part of the tool's observable response contract, so the test must fail if the
    /// production constant is changed rather than silently tracking it.
    /// </summary>
    private const string TruncationMarker = "... [truncated]";

    private static string WriteTrxFixture(IReadOnlyList<(string TestName, string Message, string? StackTrace)> failures)
    {
        const string ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        var unitTestResults = string.Join("\n", failures.Select(f =>
        {
            var stackElement = f.StackTrace is null
                ? string.Empty
                : $"\n          <StackTrace>{System.Security.SecurityElement.Escape(f.StackTrace)}</StackTrace>";
            return $"""
                <UnitTestResult testName="{f.TestName}" outcome="Failed">
                  <Output>
                    <ErrorInfo>
                      <Message>{System.Security.SecurityElement.Escape(f.Message)}</Message>{stackElement}
                    </ErrorInfo>
                  </Output>
                </UnitTestResult>
                """;
        }));

        var xml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <TestRun xmlns="{ns}">
              <Results>
            {unitTestResults}
              </Results>
              <ResultSummary outcome="Failed">
                <Counters total="{failures.Count}" executed="{failures.Count}" passed="0" failed="{failures.Count}" notExecuted="0" />
              </ResultSummary>
            </TestRun>
            """;

        var path = Path.Combine(Path.GetTempPath(), $"roslynmcp-testrun-fixture-{Guid.NewGuid():N}.trx");
        File.WriteAllText(path, xml);
        return path;
    }

    /// <summary>
    /// Returns a synthetic large failure set. With <c>maxLengthDetail</c> every failure carries a
    /// Message/StackTrace already at the DotnetOutputParser caps (500 / 1500 chars), which is the
    /// true worst case a paginated response can serialize.
    /// </summary>
    private sealed class LargeFailureSetTestRunnerService : ITestRunnerService
    {
        private readonly int _total;
        private readonly int _passed;
        private readonly int _failed;
        private readonly int _skipped;
        private readonly int _failureCount;
        private readonly bool _maxLengthDetail;

        public LargeFailureSetTestRunnerService(
            int total, int passed, int failed, int skipped, int failureCount, bool maxLengthDetail = false)
        {
            _total = total;
            _passed = passed;
            _failed = failed;
            _skipped = skipped;
            _failureCount = failureCount;
            _maxLengthDetail = maxLengthDetail;
        }

        public Task<TestRunResultDto> RunTestsAsync(
            string workspaceId, string? projectName, string? filter, CancellationToken ct)
        {
            var failures = Enumerable.Range(0, _failureCount)
                .Select(i => new TestFailureDto(
                    DisplayName: $"TestMethod_Should_Do_Something_When_Given_Input_{i}",
                    FullyQualifiedName: $"RoslynMcp.Tests.SomeNamespace.SomeFixture{i}.TestMethod_{i}",
                    Message: _maxLengthDetail
                        ? new string('m', 500) + TruncationMarker
                        : $"Assert.Equal() Failure: expected [ExpectedValue{i}] but got [ActualValue{i}].",
                    StackTrace: _maxLengthDetail
                        ? new string('s', 1500) + TruncationMarker
                        : string.Join("\n", Enumerable.Range(0, 10).Select(f =>
                            $"   at RoslynMcp.Tests.SomeFixture{i}.TestMethod{f}() line {100 + f}"))))
                .ToList();

            var execution = new CommandExecutionDto(
                Command: "dotnet",
                Arguments: ["test"],
                WorkingDirectory: "C:/fake",
                TargetPath: "C:/fake/proj.sln",
                ExitCode: 1,
                Succeeded: false,
                DurationMs: 120_370,
                StdOut: string.Empty,
                StdErr: string.Empty);

            return Task.FromResult(new TestRunResultDto(
                execution, _total, _passed, _failed, _skipped, failures));
        }
    }

    private sealed class TimeoutEnvelopeTestRunnerService : ITestRunnerService
    {
        public Task<TestRunResultDto> RunTestsAsync(
            string workspaceId, string? projectName, string? filter, CancellationToken ct)
        {
            var shell = new CommandExecutionDto(
                Command: "dotnet",
                Arguments: ["test"],
                WorkingDirectory: "C:/fake",
                TargetPath: "C:/fake/proj.csproj",
                ExitCode: -1,
                Succeeded: false,
                DurationMs: 600_000,
                StdOut: string.Empty,
                StdErr: "The command 'dotnet test' exceeded the timeout of 10.0 minute(s).");

            return Task.FromResult(DotnetOutputParser.BuildTimeoutResult(shell, shell.StdErr));
        }
    }

    [TestMethod]
    public async Task RunTests_ToolRunnerThrows_ReturnsStructuredEnvelopeWithSchemaHint()
    {
        var json = await ValidationTools.RunTests(
            new PassthroughGate(),
            new ThrowingTestRunnerService(new InvalidOperationException("no test projects matched filter")),
            workspaceId: "ws-test-run-envelope",
            projectName: "Missing.Tests",
            filter: "FullyQualifiedName~Nothing",
            progress: null,
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.IsTrue(root.GetProperty("error").GetBoolean());
        Assert.AreEqual("InvalidOperation", root.GetProperty("category").GetString());
        Assert.AreEqual("test_run", root.GetProperty("tool").GetString());
        Assert.AreEqual(nameof(InvalidOperationException), root.GetProperty("exceptionType").GetString());
        Assert.IsFalse((root.GetProperty("message").GetString() ?? string.Empty)
            .Contains("no test projects matched filter", StringComparison.Ordinal));
        Assert.IsTrue(root.TryGetProperty("schemaHint", out var schemaHint),
            $"test_run exception envelope must carry schemaHint. Envelope: {json}");
        StringAssert.Contains(schemaHint.GetString() ?? string.Empty, "test_run(");
        StringAssert.Contains(schemaHint.GetString() ?? string.Empty, "workspaceId");
    }

    // host-tools-layer-test-coverage-gap: build_workspace, build_project, test_discover,
    // test_related, and test_related_files now attach the same schemaHint-on-failure recovery
    // guidance as test_run on ANY error category (previously only ever hinting on
    // InvalidArgument via the global filter default). Each test drives the shim with a service
    // stub that throws InvalidOperationException — a non-InvalidArgument category — and asserts
    // the returned envelope carries a catalog-backed schemaHint for the tool.

    [TestMethod]
    public async Task BuildWorkspace_ServiceThrows_ReturnsStructuredEnvelopeWithSchemaHint()
    {
        var json = await ValidationTools.BuildWorkspace(
            new PassthroughGate(),
            new ThrowingBuildService(new InvalidOperationException("workspace build blew up")),
            workspaceId: "ws-build-workspace-envelope",
            progress: null,
            ct: CancellationToken.None);

        AssertNonInvalidArgumentEnvelopeHasSchemaHint(json, "build_workspace", "workspace build blew up");
    }

    [TestMethod]
    public async Task BuildProject_ServiceThrows_ReturnsStructuredEnvelopeWithSchemaHint()
    {
        var json = await ValidationTools.BuildProject(
            new PassthroughGate(),
            new ThrowingBuildService(new InvalidOperationException("project build blew up")),
            workspaceId: "ws-build-project-envelope",
            projectName: "Missing.Project",
            ct: CancellationToken.None);

        AssertNonInvalidArgumentEnvelopeHasSchemaHint(json, "build_project", "project build blew up");
    }

    [TestMethod]
    public async Task DiscoverTests_ServiceThrows_ReturnsStructuredEnvelopeWithSchemaHint()
    {
        var json = await ValidationTools.DiscoverTests(
            new PassthroughGate(),
            new ThrowingTestDiscoveryService(new InvalidOperationException("discovery blew up")),
            workspaceId: "ws-test-discover-envelope",
            projectName: null,
            nameFilter: null,
            offset: 0,
            limit: 50,
            ct: CancellationToken.None);

        AssertNonInvalidArgumentEnvelopeHasSchemaHint(json, "test_discover", "discovery blew up");
    }

    [TestMethod]
    public async Task FindRelatedTests_ServiceThrows_ReturnsStructuredEnvelopeWithSchemaHint()
    {
        var json = await ValidationTools.FindRelatedTests(
            new PassthroughGate(),
            new ThrowingTestDiscoveryService(new InvalidOperationException("related-symbol blew up")),
            workspaceId: "ws-test-related-envelope",
            filePath: null,
            line: null,
            column: null,
            symbolHandle: null,
            metadataName: "Some.Namespace.SomeType",
            maxResults: 100,
            ct: CancellationToken.None);

        AssertNonInvalidArgumentEnvelopeHasSchemaHint(json, "test_related", "related-symbol blew up");
    }

    [TestMethod]
    public async Task FindRelatedTestsForFiles_ServiceThrows_ReturnsStructuredEnvelopeWithSchemaHint()
    {
        var json = await ValidationTools.FindRelatedTestsForFiles(
            new PassthroughGate(),
            new ThrowingTestDiscoveryService(new InvalidOperationException("related-files blew up")),
            workspaceId: "ws-test-related-files-envelope",
            filePaths: ["C:/fake/Changed.cs"],
            maxResults: 100,
            ct: CancellationToken.None);

        AssertNonInvalidArgumentEnvelopeHasSchemaHint(json, "test_related_files", "related-files blew up");
    }

    private static void AssertNonInvalidArgumentEnvelopeHasSchemaHint(string json, string toolName, string secretFragment)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.IsTrue(root.GetProperty("error").GetBoolean());
        Assert.AreEqual("InvalidOperation", root.GetProperty("category").GetString(),
            $"Expected a non-InvalidArgument category for {toolName}. Envelope: {json}");
        Assert.AreEqual(toolName, root.GetProperty("tool").GetString());
        Assert.AreEqual(nameof(InvalidOperationException), root.GetProperty("exceptionType").GetString());
        Assert.IsFalse((root.GetProperty("message").GetString() ?? string.Empty)
            .Contains(secretFragment, StringComparison.Ordinal));
        Assert.IsTrue(root.TryGetProperty("schemaHint", out var schemaHint),
            $"{toolName} exception envelope must carry schemaHint on a non-InvalidArgument failure. Envelope: {json}");
        StringAssert.Contains(schemaHint.GetString() ?? string.Empty, $"{toolName}(");
        StringAssert.Contains(schemaHint.GetString() ?? string.Empty, "workspaceId");
    }

    private sealed class PassthroughGate : IWorkspaceExecutionGate
    {
        public Task<T> RunReadAsync<T>(string workspaceId, Func<CancellationToken, Task<T>> action, CancellationToken ct) =>
            action(ct);

        public Task<T> RunWriteAsync<T>(
            string workspaceId,
            Func<CancellationToken, Task<T>> action,
            CancellationToken ct,
            bool applyStalenessPolicy = true) =>
            action(ct);

        public Task<T> RunLoadGateAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct) =>
            action(ct);

        public void RemoveGate(string workspaceId) { }
    }

    private sealed class ThrowingTestRunnerService : ITestRunnerService
    {
        private readonly Exception _exception;

        public ThrowingTestRunnerService(Exception exception)
        {
            _exception = exception;
        }

        public Task<TestRunResultDto> RunTestsAsync(
            string workspaceId,
            string? projectName,
            string? filter,
            CancellationToken ct) =>
            throw _exception;
    }

    private sealed class ThrowingBuildService : IBuildService
    {
        private readonly Exception _exception;

        public ThrowingBuildService(Exception exception)
        {
            _exception = exception;
        }

        public Task<BuildResultDto> BuildWorkspaceAsync(string workspaceId, CancellationToken ct) =>
            throw _exception;

        public Task<BuildResultDto> BuildProjectAsync(string workspaceId, string projectName, CancellationToken ct) =>
            throw _exception;
    }

    private sealed class ThrowingTestDiscoveryService : ITestDiscoveryService
    {
        private readonly Exception _exception;

        public ThrowingTestDiscoveryService(Exception exception)
        {
            _exception = exception;
        }

        public Task<TestDiscoveryDto> DiscoverTestsAsync(string workspaceId, CancellationToken ct) =>
            throw _exception;

        public Task<RelatedTestsForSymbolDto> FindRelatedTestsAsync(
            string workspaceId, SymbolLocator locator, int maxResults, CancellationToken ct) =>
            throw _exception;

        public Task<RelatedTestsForFilesDto> FindRelatedTestsForFilesAsync(
            string workspaceId, IReadOnlyList<string> filePaths, int maxResults, CancellationToken ct) =>
            throw _exception;
    }
}
