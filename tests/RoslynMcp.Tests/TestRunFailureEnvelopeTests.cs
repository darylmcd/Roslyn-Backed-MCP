using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// Covers the structured failure envelope for <c>test_run</c> — backlog row
/// <c>test-run-failure-envelope</c> (P2). When <c>dotnet test</c> exits without
/// TRX output (MSBuild file locks, build failures, timeouts) the parser must
/// emit a typed envelope instead of letting a bare invocation error escape to
/// ToolErrorHandler.
/// </summary>
[TestClass]
public sealed class TestRunFailureEnvelopeTests
{
    private static CommandExecutionDto FakeExecution(int exitCode, string stdOut, string stdErr) =>
        new(
            Command: "dotnet",
            Arguments: ["test", "sample.csproj", "--nologo"],
            WorkingDirectory: "C:/fake/workdir",
            TargetPath: "C:/fake/workdir/sample.csproj",
            ExitCode: exitCode,
            Succeeded: exitCode == 0,
            DurationMs: 1234,
            StdOut: stdOut,
            StdErr: stdErr);

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
}
