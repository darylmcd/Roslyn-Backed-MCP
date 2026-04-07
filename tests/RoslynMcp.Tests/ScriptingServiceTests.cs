using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ScriptingServiceTests
{
    [TestMethod]
    [Timeout(30_000)]
    public async Task EvaluateAsync_InfiniteLoop_ReturnsTimeoutWithinBudgetPlusGrace()
    {
        var service = new ScriptingService(NullLogger<ScriptingService>.Instance, new ScriptingServiceOptions());
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await service.EvaluateAsync(
            "while(true){}",
            imports: null,
            CancellationToken.None,
            onProgress: null,
            timeoutSecondsOverride: 1).ConfigureAwait(false);
        sw.Stop();

        Assert.IsFalse(result.Success, "Infinite loop should not succeed.");
        Assert.IsFalse(string.IsNullOrEmpty(result.Error), "Expected error message.");
        StringAssert.Contains(result.Error!, "forcibly abandoned");

        // Default WatchdogGraceSeconds is 10; effective timeout 1 → hard deadline 11s + buffer
        Assert.IsTrue(sw.Elapsed.TotalSeconds < 22, $"Expected completion under ~22s, took {sw.Elapsed.TotalSeconds:F1}s");
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task EvaluateAsync_InfiniteLoop_TightGraceCompletesQuickly()
    {
        // FLAG-5C: With a tight grace window (1s) and budget (1s), a non-cancellable infinite
        // loop must return within ~3s — proves the dedicated-thread + Timer hard deadline path.
        var options = new ScriptingServiceOptions { WatchdogGraceSeconds = 1 };
        var service = new ScriptingService(NullLogger<ScriptingService>.Instance, options);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await service.EvaluateAsync(
            "while(true){}",
            imports: null,
            CancellationToken.None,
            onProgress: null,
            timeoutSecondsOverride: 1).ConfigureAwait(false);
        sw.Stop();

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Error!, "forcibly abandoned");
        Assert.IsTrue(sw.Elapsed.TotalSeconds < 5, $"Expected sub-5s completion, took {sw.Elapsed.TotalSeconds:F1}s");
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task EvaluateAsync_RepeatedInfiniteLoops_FailFastUnderAbandonedCap()
    {
        // FLAG-5C: Issue infinite-loop probes against a service whose abandoned-thread cap
        // is small. The first MaxAbandonedEvaluations probes return "forcibly abandoned"
        // within budget+grace each. After that the abandoned-cap kicks in and subsequent
        // calls fail fast with the at-capacity error. Total wall-clock stays bounded — the
        // thread pool itself must NOT be starved.
        var options = new ScriptingServiceOptions
        {
            WatchdogGraceSeconds = 1,
            MaxConcurrentEvaluations = 2,
            ConcurrencySlotAcquireTimeoutSeconds = 30,
            MaxAbandonedEvaluations = 3,
        };
        var service = new ScriptingService(NullLogger<ScriptingService>.Instance, options);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var abandoned = 0;
        var capacityErrors = 0;
        for (var i = 0; i < 8; i++)
        {
            var result = await service.EvaluateAsync(
                "while(true){}",
                imports: null,
                CancellationToken.None,
                onProgress: null,
                timeoutSecondsOverride: 1).ConfigureAwait(false);
            Assert.IsFalse(result.Success, $"Iteration {i}: infinite loop should not succeed.");
            if (result.Error!.Contains("forcibly abandoned", StringComparison.Ordinal))
                abandoned++;
            else if (result.Error.Contains("abandoned worker threads", StringComparison.Ordinal) ||
                     result.Error.Contains("at capacity", StringComparison.Ordinal))
                capacityErrors++;
            else
                Assert.Fail($"Iteration {i} returned unexpected error: {result.Error}");
        }
        sw.Stop();

        Assert.IsTrue(abandoned >= 1, $"Expected at least one abandoned response, got {abandoned}");
        Assert.IsTrue(capacityErrors >= 1, $"Expected at least one capacity error, got {capacityErrors}");
        Assert.AreEqual(8, abandoned + capacityErrors);

        // Total stays bounded — no thread pool starvation.
        Assert.IsTrue(sw.Elapsed.TotalSeconds < 50, $"Repeated infinite loops took {sw.Elapsed.TotalSeconds:F1}s — possible thread leak or starvation");
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task EvaluateAsync_MixedSuccessAndInfinite_StaysHealthy()
    {
        // FLAG-5C: After an infinite-loop call returns, the next normal call must still work.
        // Proves the slot release / abandoned-thread bookkeeping doesn't break the gate.
        var options = new ScriptingServiceOptions { WatchdogGraceSeconds = 1 };
        var service = new ScriptingService(NullLogger<ScriptingService>.Instance, options);

        var hung = await service.EvaluateAsync("while(true){}", null, CancellationToken.None, null, 1).ConfigureAwait(false);
        Assert.IsFalse(hung.Success);

        var ok = await service.EvaluateAsync("21 + 34", null, CancellationToken.None, null, 5).ConfigureAwait(false);
        Assert.IsTrue(ok.Success, ok.Error);
        Assert.AreEqual("55", ok.ResultValue);
    }

    [TestMethod]
    public async Task EvaluateAsync_SimpleExpression_ReturnsResult()
    {
        var service = new ScriptingService(NullLogger<ScriptingService>.Instance, new ScriptingServiceOptions());
        var result = await service.EvaluateAsync(
            "21 + 34",
            imports: null,
            CancellationToken.None,
            onProgress: null,
            timeoutSecondsOverride: 5).ConfigureAwait(false);

        Assert.IsTrue(result.Success, result.Error);
        Assert.AreEqual("55", result.ResultValue);
    }

    [TestMethod]
    public async Task EvaluateAsync_RuntimeError_ReturnsCaughtError()
    {
        var service = new ScriptingService(NullLogger<ScriptingService>.Instance, new ScriptingServiceOptions());
        var result = await service.EvaluateAsync(
            "throw new System.InvalidOperationException(\"boom\");",
            imports: null,
            CancellationToken.None,
            onProgress: null,
            timeoutSecondsOverride: 5).ConfigureAwait(false);

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Error?.Contains("InvalidOperationException", StringComparison.Ordinal), result.Error);
        StringAssert.Contains(result.Error!, "boom");
    }

    [TestMethod]
    public async Task EvaluateAsync_CompilationError_ReturnsDiagnostics()
    {
        var service = new ScriptingService(NullLogger<ScriptingService>.Instance, new ScriptingServiceOptions());
        var result = await service.EvaluateAsync(
            "this is not valid C#",
            imports: null,
            CancellationToken.None,
            onProgress: null,
            timeoutSecondsOverride: 5).ConfigureAwait(false);

        Assert.IsFalse(result.Success);
        Assert.IsNotNull(result.CompilationErrors);
        Assert.IsTrue(result.CompilationErrors!.Count > 0);
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task EvaluateAsync_OuterCancellation_PropagatesPromptly()
    {
        // FLAG-5C: outer cancellation must surface as OperationCanceledException without
        // waiting for the script worker to honor the inner timeout.
        var options = new ScriptingServiceOptions { WatchdogGraceSeconds = 30 };
        var service = new ScriptingService(NullLogger<ScriptingService>.Instance, options);
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(500));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(async () =>
            await service.EvaluateAsync("while(true){}", null, cts.Token, null, 30).ConfigureAwait(false))
            .ConfigureAwait(false);
        sw.Stop();

        Assert.IsTrue(sw.Elapsed.TotalSeconds < 5, $"Outer cancellation should propagate quickly, took {sw.Elapsed.TotalSeconds:F1}s");
    }
}
