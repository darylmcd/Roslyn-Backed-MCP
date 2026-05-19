using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// validate-workspace-25s-internalvalidationtimeoutexception-on-medium-solution (gh #759):
/// before this fix, when a validation phase exceeded the 25-second internal timeout the
/// service raised a private <c>InternalValidationTimeoutException</c> that
/// <see cref="IWorkspaceValidationService.ValidateAsync"/> did NOT catch. The exception
/// escaped through <c>WorkspaceExecutionGate.RunReadAsync</c> and surfaced as a bare SDK
/// error string at the MCP transport — instead of the structured
/// <see cref="WorkspaceValidationDto"/> with <c>OverallStatus=="timeout"</c> that
/// <see cref="IWorkspaceValidationService.ValidateRecentGitChangesAsync"/> already returned.
///
/// This test class pins the structured-timeout contract on the <c>ValidateAsync</c>
/// entry point by injecting a near-zero internal phase timeout
/// (<c>validationPhaseTimeout: TimeSpan.FromMilliseconds(1)</c>) together with a
/// <see cref="SlowCompileCheckService"/> stub that blocks until cancellation. The phase
/// timeout fires, the catch added in this fix triggers, and the response carries the
/// timeout DTO with <c>compileResult.cancelled=true</c> and a <c>warnings</c> entry
/// naming the timed-out phase.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class WorkspaceValidationTimeoutTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task ValidateAsync_WhenPhaseExceedsInternalTimeout_ReturnsTimeoutDto()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();

        // Inject a near-zero phase timeout + a compile_check stub that blocks until
        // cancellation. The internal CTS is linked to the outer token, so when the 1ms
        // timer fires the stub's Task.Delay observes cancellation, RunValidationPhaseAsync
        // converts the OperationCanceledException into InternalValidationTimeoutException,
        // and the new catch in ValidateAsync converts that into the structured DTO.
        var timeoutService = new WorkspaceValidationService(
            new SlowCompileCheckService(),
            DiagnosticService,
            TestDiscoveryService,
            TestRunnerService,
            WorkspaceManager,
            ChangeTracker,
            gitStatusTimeout: TimeSpan.FromSeconds(5),
            validationPhaseTimeout: TimeSpan.FromMilliseconds(1));

        var changedFiles = new[]
        {
            workspace.GetPath("SampleLib", "AnimalService.cs"),
        };

        var result = await timeoutService.ValidateAsync(
            workspace.WorkspaceId,
            changedFiles,
            runTests: false,
            ct: CancellationToken.None);

        Assert.AreEqual("timeout", result.OverallStatus,
            "ValidateAsync must convert the internal phase-timeout exception into a structured 'timeout' verdict — mirroring ValidateRecentGitChangesAsync.");
        Assert.IsTrue(result.CompileResult.Cancelled,
            "CompileResult.Cancelled must be true on a timeout to signal the result is not authoritative.");
        Assert.IsTrue(result.Warnings.Count > 0,
            $"Timeout result should include at least one warning naming the timed-out phase; got [{string.Join("; ", result.Warnings)}].");
        Assert.IsTrue(
            result.Warnings.Any(w => w.Contains("compile_check", StringComparison.Ordinal)
                && w.Contains("retryable=true", StringComparison.Ordinal)),
            $"Expected a retryable compile_check timeout warning; got [{string.Join("; ", result.Warnings)}].");

        // The structured retry envelope on TestRunResult is the standard timeout-shape contract.
        // It is populated even when runTests=false because CreateTimeoutResult stamps it
        // unconditionally — the bundle wants a single failure shape regardless of run mode.
        Assert.IsNotNull(result.TestRunResult?.FailureEnvelope,
            "Timeout result should include the structured retry envelope.");
        Assert.AreEqual("Timeout", result.TestRunResult.FailureEnvelope.ErrorKind);
        Assert.IsTrue(result.TestRunResult.FailureEnvelope.IsRetryable);
    }

    [TestMethod]
    public async Task ValidateAsync_WhenChangedFilePathsIsNull_TimeoutResultStillReturnsEmptyChangedFilePaths()
    {
        // Regression for the null-guard in CreateTimeoutResult's caller: pre-fix the catch
        // would have NRE'd when callers passed null. Pass null explicitly and assert the
        // result is a well-formed timeout DTO with an empty ChangedFilePaths list.
        await using var workspace = await CreateIsolatedWorkspaceAsync();

        var timeoutService = new WorkspaceValidationService(
            new SlowCompileCheckService(),
            DiagnosticService,
            TestDiscoveryService,
            TestRunnerService,
            WorkspaceManager,
            ChangeTracker,
            gitStatusTimeout: TimeSpan.FromSeconds(5),
            validationPhaseTimeout: TimeSpan.FromMilliseconds(1));

        var result = await timeoutService.ValidateAsync(
            workspace.WorkspaceId,
            changedFilePaths: null,
            runTests: false,
            ct: CancellationToken.None);

        Assert.AreEqual("timeout", result.OverallStatus);
        Assert.IsNotNull(result.ChangedFilePaths,
            "Timeout DTO must always carry a non-null ChangedFilePaths list even when the caller supplied null.");
    }

    [TestMethod]
    public async Task ValidateAsync_WhenCallerCancels_StillPropagatesOperationCanceled()
    {
        // Negative-control: the catch in ValidateAsync intentionally does NOT catch
        // OperationCanceledException — cooperative cancellation is a protocol signal that
        // must bubble to the SDK transport. This test makes sure we didn't accidentally
        // swallow it alongside the timeout exception.
        await using var workspace = await CreateIsolatedWorkspaceAsync();

        var service = new WorkspaceValidationService(
            new SlowCompileCheckService(),
            DiagnosticService,
            TestDiscoveryService,
            TestRunnerService,
            WorkspaceManager,
            ChangeTracker);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        // ThrowsExceptionAsync<OperationCanceledException> is strict-typed and rejects the
        // TaskCanceledException subclass that Task.Delay actually throws. Catch the base
        // class manually so subclass-vs-base differences don't fail the assertion.
        OperationCanceledException? caught = null;
        try
        {
            await service.ValidateAsync(
                workspace.WorkspaceId,
                changedFilePaths: null,
                runTests: false,
                ct: cts.Token);
        }
        catch (OperationCanceledException ex)
        {
            caught = ex;
        }

        Assert.IsNotNull(caught,
            "Cooperative cancellation must propagate as OperationCanceledException (or a subclass like TaskCanceledException) — the new InternalValidationTimeoutException catch must NOT swallow it.");
    }

    /// <summary>
    /// Local copy of the stub from <see cref="ValidateRecentGitChangesTests"/>: blocks on
    /// <c>Task.Delay(Timeout.InfiniteTimeSpan)</c> so the only exit is cancellation. The
    /// stub class isn't shared because the tests live in different fixtures and the stub
    /// is trivial; duplicating it keeps the timeout-test files independently readable.
    /// </summary>
    private sealed class SlowCompileCheckService : ICompileCheckService
    {
        public async Task<CompileCheckDto> CheckAsync(
            string workspaceId,
            CompileCheckOptions options,
            CancellationToken ct)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("unreachable");
        }
    }
}
