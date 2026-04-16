using System.Text.Json;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression for `symbol-impact-sweep-race-with-auto-reload` (P4): when the workspace
/// execution gate auto-reloads mid-call and the symbol locator can no longer be resolved
/// (because the handle was from the pre-reload compilation), tools surfaced a generic
/// <c>NotFound</c> envelope that read as "this symbol doesn't exist" even though the
/// real cause was a reload race. ToolErrorHandler now detects the signal by inspecting
/// <c>AmbientGateMetrics.Current.StaleAction</c> and returns a distinct
/// <c>WorkspaceReloadedDuringCall</c> category so the caller knows to re-resolve and
/// retry rather than treat the symbol as missing.
/// </summary>
[TestClass]
public sealed class ToolErrorHandlerWorkspaceReloadRaceTests
{
    [TestMethod]
    public async Task KeyNotFoundException_WithAutoReloadedStaleAction_SurfacesWorkspaceReloadedDuringCall()
    {
        var result = await ToolExecutionTestHarness.RunAsync(
            "symbol_impact_sweep",
            () =>
            {
                // ExecuteAsync opens an AmbientGateMetrics scope for us; simulate the gate
                // stamping StaleAction="auto-reloaded" before the symbol resolver throws.
                if (AmbientGateMetrics.Current is { } m)
                {
                    m.StaleAction = "auto-reloaded";
                    m.StaleReloadMs = 120;
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
            $"expected race-aware category, got {category}; full payload: {result}");

        var message = doc.RootElement.GetProperty("message").GetString();
        StringAssert.Contains(
            message,
            "auto-reloaded",
            $"message must mention the reload so callers can distinguish race from true miss; got: {message}");
        StringAssert.Contains(message, "retry");
    }

    [TestMethod]
    public async Task KeyNotFoundException_WithoutStaleAction_FallsThroughToGenericNotFound()
    {
        var result = await ToolExecutionTestHarness.RunAsync(
            "symbol_info",
            () => throw new KeyNotFoundException(
                "No symbol could be resolved for metadata name 'Foo.Bar'."));

        var doc = JsonDocument.Parse(result);
        Assert.AreEqual(
            "NotFound",
            doc.RootElement.GetProperty("category").GetString(),
            "without a reload signal the classifier must still emit the standard NotFound");
    }

    [TestMethod]
    public async Task KeyNotFoundException_WithWarnStaleAction_FallsThroughToGenericNotFound()
    {
        // Only the reload-completed case ("auto-reloaded") is race-prone. A "warn"
        // stamp means the workspace was stale but no reload fired, so the usual
        // NotFound envelope remains the correct signal.
        var result = await ToolExecutionTestHarness.RunAsync(
            "find_references",
            () =>
            {
                if (AmbientGateMetrics.Current is { } m) m.StaleAction = "warn";
                throw new KeyNotFoundException("No symbol could be resolved.");
            });

        var doc = JsonDocument.Parse(result);
        Assert.AreEqual(
            "NotFound",
            doc.RootElement.GetProperty("category").GetString());
    }
}
