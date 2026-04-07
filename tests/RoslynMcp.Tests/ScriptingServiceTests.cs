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
        var service = new ScriptingService(NullLogger<ScriptingService>.Instance);
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
    public async Task EvaluateAsync_SimpleExpression_ReturnsResult()
    {
        var service = new ScriptingService(NullLogger<ScriptingService>.Instance);
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
        var service = new ScriptingService(NullLogger<ScriptingService>.Instance);
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
}
