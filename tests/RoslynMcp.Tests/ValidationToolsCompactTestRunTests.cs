using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression guard for <c>test_run</c>'s opt-in <c>compact</c> parameter (gh #1421): trims
/// <c>execution.stdOut</c>/<c>stdErr</c>/<c>command</c>/<c>arguments</c>/<c>workingDirectory</c>
/// and, when there is nothing to paginate, the failure-pagination fields — but ONLY when the
/// caller passes <c>compact=true</c>. The default (<c>compact</c> omitted) must reproduce the
/// pre-existing wire shape byte-for-byte, since this is the one published repo in the portfolio
/// and an unannounced default-shape change would silently break existing consumers.
/// </summary>
[TestClass]
public sealed class ValidationToolsCompactTestRunTests
{
    [TestMethod]
    public async Task RunTests_DefaultCompactOmitted_PreservesExistingVerboseShape()
    {
        var gate = new PassThroughGate();
        var runner = new StubTestRunnerService(CleanRunResult());

        var json = await ValidationTools.RunTests(
            gate,
            runner,
            workspaceId: "ws",
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var execution = root.GetProperty("execution");
        Assert.IsTrue(execution.TryGetProperty("stdOut", out _), "Default shape must keep stdOut.");
        Assert.IsTrue(execution.TryGetProperty("stdErr", out _), "Default shape must keep stdErr.");
        Assert.IsTrue(execution.TryGetProperty("command", out _), "Default shape must keep command.");
        Assert.IsTrue(execution.TryGetProperty("arguments", out _), "Default shape must keep arguments.");
        Assert.IsTrue(execution.TryGetProperty("workingDirectory", out _), "Default shape must keep workingDirectory.");
        Assert.IsTrue(root.TryGetProperty("failuresOffset", out _), "Default shape must keep failuresOffset even with zero failures.");
        Assert.IsTrue(root.TryGetProperty("failuresLimit", out _), "Default shape must keep failuresLimit even with zero failures.");
        Assert.IsTrue(root.TryGetProperty("hasMoreFailures", out _), "Default shape must keep hasMoreFailures even with zero failures.");
    }

    [TestMethod]
    public async Task RunTests_CompactTrue_CleanRun_OmitsExecutionDetailAndPagination()
    {
        var gate = new PassThroughGate();
        var runner = new StubTestRunnerService(CleanRunResult());

        var json = await ValidationTools.RunTests(
            gate,
            runner,
            workspaceId: "ws",
            compact: true,
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var execution = root.GetProperty("execution");

        Assert.IsFalse(execution.TryGetProperty("stdOut", out _), "compact=true must omit stdOut on a clean run.");
        Assert.IsFalse(execution.TryGetProperty("stdErr", out _), "compact=true must omit stdErr on a clean run.");
        Assert.IsFalse(execution.TryGetProperty("command", out _), "compact=true must omit command on a clean run.");
        Assert.IsFalse(execution.TryGetProperty("arguments", out _), "compact=true must omit arguments on a clean run.");
        Assert.IsFalse(execution.TryGetProperty("workingDirectory", out _), "compact=true must omit workingDirectory on a clean run.");
        Assert.AreEqual("proj.csproj", execution.GetProperty("targetPath").GetString(), "targetPath still goes through the existing redaction projection in compact mode.");
        Assert.AreEqual(0, execution.GetProperty("exitCode").GetInt32());
        Assert.IsTrue(execution.GetProperty("succeeded").GetBoolean());
        Assert.AreEqual(1, execution.GetProperty("durationMs").GetInt64());

        Assert.IsFalse(root.TryGetProperty("failuresOffset", out _), "compact=true must omit failuresOffset when there is nothing to paginate.");
        Assert.IsFalse(root.TryGetProperty("failuresLimit", out _), "compact=true must omit failuresLimit when there is nothing to paginate.");
        Assert.IsFalse(root.TryGetProperty("hasMoreFailures", out _), "compact=true must omit hasMoreFailures when there is nothing to paginate.");
        Assert.AreEqual(0, root.GetProperty("failuresTotal").GetInt32(), "failuresTotal is an aggregate count, always present.");
        Assert.AreEqual(1, root.GetProperty("total").GetInt32());
        Assert.AreEqual(1, root.GetProperty("passed").GetInt32());
    }

    [TestMethod]
    public async Task RunTests_CompactTrue_RunWithFailures_KeepsPaginationFields()
    {
        var gate = new PassThroughGate();
        var runner = new StubTestRunnerService(FailingRunResult());

        var json = await ValidationTools.RunTests(
            gate,
            runner,
            workspaceId: "ws",
            compact: true,
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.IsTrue(root.TryGetProperty("failuresOffset", out _), "compact=true must still page failures when failuresTotal > 0.");
        Assert.IsTrue(root.TryGetProperty("failuresLimit", out _), "compact=true must still page failures when failuresTotal > 0.");
        Assert.IsTrue(root.TryGetProperty("hasMoreFailures", out _), "compact=true must still page failures when failuresTotal > 0.");
        Assert.AreEqual(1, root.GetProperty("failuresTotal").GetInt32());
        Assert.AreEqual(1, root.GetProperty("failures").GetArrayLength());

        var execution = root.GetProperty("execution");
        Assert.IsFalse(execution.TryGetProperty("stdOut", out _), "compact=true still trims execution detail on a failing run — only pagination is data-conditional.");
    }

    private static TestRunResultDto CleanRunResult() => new(
        Execution: new CommandExecutionDto(
            Command: "dotnet",
            Arguments: ["test"],
            WorkingDirectory: "C:/fake",
            TargetPath: "C:/fake/proj.csproj",
            ExitCode: 0,
            Succeeded: true,
            DurationMs: 1,
            StdOut: "Passed!",
            StdErr: string.Empty),
        Total: 1,
        Passed: 1,
        Failed: 0,
        Skipped: 0,
        Failures: []);

    private static TestRunResultDto FailingRunResult() => new(
        Execution: new CommandExecutionDto(
            Command: "dotnet",
            Arguments: ["test"],
            WorkingDirectory: "C:/fake",
            TargetPath: "C:/fake/proj.csproj",
            ExitCode: 1,
            Succeeded: false,
            DurationMs: 1,
            StdOut: "Failed!",
            StdErr: string.Empty),
        Total: 1,
        Passed: 0,
        Failed: 1,
        Skipped: 0,
        Failures: [new TestFailureDto("SomeTest", "Ns.SomeTest", "assert failed", null)]);

    private sealed class StubTestRunnerService(TestRunResultDto result) : ITestRunnerService
    {
        public Task<TestRunResultDto> RunTestsAsync(
            string workspaceId,
            string? projectName,
            string? filter,
            CancellationToken ct)
            => Task.FromResult(result);
    }

    private sealed class PassThroughGate : IWorkspaceExecutionGate
    {
        public Task<T> RunReadAsync<T>(string workspaceId, Func<CancellationToken, Task<T>> action, CancellationToken ct)
            => action(ct);

        public Task<T> RunWriteAsync<T>(string workspaceId, Func<CancellationToken, Task<T>> action, CancellationToken ct, bool applyStalenessPolicy = true)
            => action(ct);

        public Task<T> RunLoadGateAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
            => throw new NotSupportedException("test_run must not route through RunLoadGateAsync.");

        public void RemoveGate(string workspaceId)
            => throw new NotSupportedException("test_run must not call RemoveGate.");
    }
}
