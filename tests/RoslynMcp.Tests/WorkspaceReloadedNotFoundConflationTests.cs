using System.Text.Json;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression for <c>workspace-reloaded-during-call-conflates-notfound</c> (P3):
/// when <c>get_source_text</c> (or any tool) is called with a nonexistent file path
/// concurrently with an auto-reload, the gate stamps <c>StaleAction="auto-reloaded"</c>.
/// Without the fix, <see cref="ToolErrorHandler"/> classified the resulting
/// <c>KeyNotFoundException</c> as <c>WorkspaceReloadedDuringCall</c>, hiding the true
/// cause (the path is invalid). With the fix, the gate retries after the reload, and if
/// the second attempt also fails with "Document not found", it stamps
/// <see cref="GateMetricsBuilder.ReloadConfirmedNotFound"/> so the classifier returns
/// <c>category=NotFound</c> — the caller-visible category that reflects the true cause.
/// </summary>
[TestClass]
public sealed class WorkspaceReloadedNotFoundConflationTests
{
    /// <summary>
    /// Core regression: a "Document not found" error that survives the post-reload retry must
    /// surface as <c>NotFound</c>, not <c>WorkspaceReloadedDuringCall</c>, even when
    /// <c>StaleAction="auto-reloaded"</c> is stamped. Verifies the
    /// <see cref="GateMetricsBuilder.ReloadConfirmedNotFound"/> flag is respected by
    /// <see cref="ToolErrorHandler.ClassifyAndFormat"/>.
    /// </summary>
    [TestMethod]
    public async Task DocumentNotFound_WithAutoReloadedAndReloadConfirmedNotFound_SurfacesNotFound()
    {
        var result = await ToolExecutionTestHarness.RunAsync(
            "get_source_text",
            () =>
            {
                // Simulate the gate having auto-reloaded the workspace AND confirmed that
                // the retry also failed with "Document not found" (bad path, not a race).
                if (AmbientGateMetrics.Current is { } m)
                {
                    m.StaleAction = "auto-reloaded";
                    m.StaleReloadMs = 150;
                    m.RetriedAfterReload = true;
                    m.ReloadConfirmedNotFound = true;
                }

                throw new KeyNotFoundException(
                    "Document not found: /workspace/src/DoesNotExist.cs. " +
                    "Verify the path is correct and the file is part of the loaded solution.");
            });

        var doc = JsonDocument.Parse(result);
        var category = doc.RootElement.GetProperty("category").GetString();
        Assert.AreEqual(
            "NotFound",
            category,
            $"A bad-path error confirmed by retry must surface as NotFound, not WorkspaceReloadedDuringCall. Got: {category}; full payload: {result}");
    }

    /// <summary>
    /// Complementary positive test: a symbol-resolution <c>KeyNotFoundException</c> (not a
    /// document-not-found path) with <c>StaleAction="auto-reloaded"</c> and
    /// <c>ReloadConfirmedNotFound</c> NOT set must still return
    /// <c>WorkspaceReloadedDuringCall</c> — the pre-existing race-aware behaviour must not
    /// be weakened.
    /// </summary>
    [TestMethod]
    public async Task SymbolResolutionFail_WithAutoReloaded_StillSurfacesWorkspaceReloadedDuringCall()
    {
        var result = await ToolExecutionTestHarness.RunAsync(
            "symbol_impact_sweep",
            () =>
            {
                if (AmbientGateMetrics.Current is { } m)
                {
                    m.StaleAction = "auto-reloaded";
                    m.StaleReloadMs = 80;
                    // ReloadConfirmedNotFound is NOT set — this is a symbol race, not a bad path.
                }

                throw new KeyNotFoundException(
                    "No symbol could be resolved for the supplied symbol handle. " +
                    "The handle may be from a previous workspace version...");
            });

        var doc = JsonDocument.Parse(result);
        var category = doc.RootElement.GetProperty("category").GetString();
        Assert.AreEqual(
            "WorkspaceReloadedDuringCall",
            category,
            $"Symbol-resolution failure after auto-reload must retain WorkspaceReloadedDuringCall category. Got: {category}; full payload: {result}");
    }

    /// <summary>
    /// Validates that setting <c>ReloadConfirmedNotFound=true</c> without
    /// <c>StaleAction="auto-reloaded"</c> has no effect — the flag is only checked inside
    /// the reload-race branch so a plain document-not-found (no reload) still routes through
    /// the standard handler dictionary to <c>NotFound</c> as expected.
    /// </summary>
    [TestMethod]
    public async Task DocumentNotFound_WithoutAutoReloaded_FallsThroughToNotFound()
    {
        var result = await ToolExecutionTestHarness.RunAsync(
            "get_source_text",
            () =>
            {
                // Confirm that without the auto-reload stamp, the flag is irrelevant.
                if (AmbientGateMetrics.Current is { } m)
                {
                    m.ReloadConfirmedNotFound = true;
                    // StaleAction is intentionally left null
                }

                throw new KeyNotFoundException(
                    "Document not found: /workspace/src/DoesNotExist.cs.");
            });

        var doc = JsonDocument.Parse(result);
        var category = doc.RootElement.GetProperty("category").GetString();
        Assert.AreEqual(
            "NotFound",
            category,
            $"Without auto-reload stamp, document-not-found must still be NotFound. Got: {category}");
    }
}
