using System.Diagnostics;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests.Helpers;

/// <summary>
/// In-process simulation of what <c>StructuredCallToolFilter</c> does for each
/// <c>tools/call</c> dispatch, intended as a unit-test harness for
/// <see cref="ToolErrorHandler.ClassifyAndFormat"/> and
/// <see cref="ToolErrorHandler.InjectMetaIfPossible"/>. Keeping this logic in the test
/// project (instead of shipping a redundant path in production) means:
/// <list type="bullet">
///   <item>Production has a single error boundary — the filter — with no sibling wrappers.</item>
///   <item>Unit tests can still assert classifier + envelope + <c>_meta</c> behavior
///         without constructing a real <c>RequestContext</c>, transport, and DI graph.</item>
///   <item>Filter-level regressions live in <c>StructuredCallToolFilterTests</c>
///         (end-to-end path, including pre-binding failures).</item>
/// </list>
/// This harness deliberately mirrors the filter's control flow byte-for-byte except
/// for logging, so tests written against it remain valid regression surfaces for the
/// production filter.
/// </summary>
internal static class ToolExecutionTestHarness
{
    /// <summary>
    /// Runs <paramref name="action"/> under an ambient gate-metrics scope. On success,
    /// injects <c>_meta</c> into the JSON result. On exception (other than
    /// <see cref="OperationCanceledException"/>, which is rethrown), classifies and
    /// formats the exception into the standard error envelope and injects <c>_meta</c>.
    /// Returns the final JSON string.
    /// </summary>
    public static async Task<string> RunAsync(string toolName, Func<Task<string>> action)
    {
        ArgumentException.ThrowIfNullOrEmpty(toolName);
        ArgumentNullException.ThrowIfNull(action);

        using var scope = AmbientGateMetrics.BeginRequest();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var result = await action().ConfigureAwait(false);
            stopwatch.Stop();
            RecordElapsed(stopwatch.ElapsedMilliseconds);
            return ToolErrorHandler.InjectMetaIfPossible(result, toolName);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            RecordElapsed(stopwatch.ElapsedMilliseconds);
            var envelope = ToolErrorHandler.ClassifyAndFormat(ex, toolName);
            return ToolErrorHandler.InjectMetaIfPossible(envelope, toolName);
        }
    }

    private static void RecordElapsed(long elapsedMs)
    {
        if (AmbientGateMetrics.Current is { } metrics)
        {
            metrics.ElapsedMs = elapsedMs;
        }
    }
}
