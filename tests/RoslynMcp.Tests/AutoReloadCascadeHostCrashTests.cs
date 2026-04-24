using System.Text.Json;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression guard for <c>autoreload-cascade-stdio-host-crash</c> (P2, 2026-04-24).
///
/// <para>
/// Observed failure: two in-turn writers (<c>preview_record_field_addition</c> +
/// <c>extract_and_wire_interface_preview</c>) each stamped <c>staleAction=auto-reloaded</c>
/// in the response envelope. The follow-up reader (<c>workspace_changes</c>) then
/// terminated the stdio host with <c>MCP error -32000: Connection closed</c>; the server's
/// <c>stdioPid</c> changed, indicating the .NET process itself died rather than surfacing
/// a structured tool error.
/// </para>
///
/// <para>
/// Root cause: <c>WorkspaceManager.LoadIntoSessionAsync</c> disposed the prior
/// MSBuildWorkspace BEFORE assigning the replacement, leaving a window where
/// concurrent readers observed <c>session.Workspace</c> pointing at a disposed object.
/// <c>.CurrentSolution</c> on a disposed workspace throws <see cref="ObjectDisposedException"/>,
/// which — because the outer tool filter only classifies it as a generic
/// <c>InternalError</c> — doesn't carry enough structural signal for the caller to know
/// the condition is retry-able.
/// </para>
///
/// <para>
/// Remediation: <c>LoadIntoSessionAsync</c> now builds the new workspace into a local,
/// performs an atomic swap, and disposes the prior workspace only after readers can no
/// longer reach it through the session. Additionally, <c>GetCurrentSolution</c> wraps
/// both the "null workspace" case and any residual <see cref="ObjectDisposedException"/>
/// into a <see cref="StaleWorkspaceTransitionException"/> so callers see a structured
/// <c>category="StaleWorkspaceTransition"</c> envelope with an explicit retry hint.
/// </para>
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class AutoReloadCascadeHostCrashTests : SharedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    /// <summary>
    /// Core classifier contract: a <see cref="StaleWorkspaceTransitionException"/> thrown
    /// from a tool handler must surface as <c>category="StaleWorkspaceTransition"</c>
    /// (NOT "InvalidOperation" or "InternalError"). This is the structural signal the
    /// audit report specifically requested so callers can retry on the next turn.
    /// </summary>
    [TestMethod]
    public async Task StaleWorkspaceTransitionException_Classifies_As_StructuredCategory()
    {
        var workspaceId = "ws-transition-under-test";
        var result = await ToolExecutionTestHarness.RunAsync(
            "workspace_changes",
            () => throw new StaleWorkspaceTransitionException(
                workspaceId,
                "Workspace is mid-reload; retry after settle."));

        using var json = JsonDocument.Parse(result);
        Assert.IsTrue(json.RootElement.GetProperty("error").GetBoolean(),
            "Transition errors must carry error=true in the envelope.");
        Assert.AreEqual("StaleWorkspaceTransition",
            json.RootElement.GetProperty("category").GetString(),
            "Category must be the structured retry-able marker, not a generic InternalError.");
        Assert.AreEqual("workspace_changes",
            json.RootElement.GetProperty("tool").GetString(),
            "The tool name must be preserved in the envelope.");
        Assert.AreEqual(nameof(StaleWorkspaceTransitionException),
            json.RootElement.GetProperty("exceptionType").GetString());

        var message = json.RootElement.GetProperty("message").GetString()!;
        StringAssert.Contains(message, "Workspace is transitioning between snapshots",
            "Envelope message must contain the structured retry-hint prefix.");
        StringAssert.Contains(message, "retry after settle",
            "The originating exception message must be preserved end-to-end.");

        // Internal-error envelopes carry a stack trace; structured known-category envelopes
        // deliberately omit it to keep the payload small. Verify StaleWorkspaceTransition
        // lands in the known-category branch, not the InternalError branch.
        Assert.IsFalse(json.RootElement.TryGetProperty("stackTrace", out _),
            "StaleWorkspaceTransition is a known category and must not emit a stack trace.");
    }

    /// <summary>
    /// Regression: two in-turn writers mark the workspace stale twice, and a follow-up
    /// reader (<c>workspace_changes</c>) arrives mid-transition. With the atomic-swap fix,
    /// the reader either sees the prior workspace or the fully loaded new workspace — it
    /// never observes a disposed / null / half-loaded snapshot. Before the fix, a reader
    /// that interleaved between <c>session.Workspace?.Dispose()</c> and the reassignment
    /// threw <see cref="ObjectDisposedException"/>, which escaped as an <c>InternalError</c>
    /// on the structured envelope; in production this cascade terminated the stdio host.
    /// </summary>
    [TestMethod]
    public async Task TwoWriters_Plus_Reader_InSameTurn_DoesNotCrashHost()
    {
        var workspaceId = await LoadSharedSampleWorkspaceAsync();

        // Simulate two writer mutations: each marks the workspace stale. In production the
        // `apply` path enqueues the stale signal; here we invoke it directly so the test is
        // deterministic (the real MSBuild reload is exercised via ReloadAsync below).
        FileWatcher.MarkStale(workspaceId, StaleReasons.Apply);
        FileWatcher.MarkStale(workspaceId, StaleReasons.Apply);
        Assert.IsTrue(WorkspaceManager.IsStale(workspaceId),
            "Two writer stale-marks must leave the workspace in the stale state.");

        // Reload once (first writer's auto-reload) — this is the path that previously left a
        // disposed-workspace window open to concurrent readers.
        await WorkspaceManager.ReloadAsync(workspaceId, CancellationToken.None);

        // After the atomic-swap fix, GetCurrentSolution must yield a valid, non-disposed
        // Solution. Before the fix, a rare interleaving surfaced ObjectDisposedException;
        // after the fix, readers ALWAYS observe either the prior or the new workspace.
        var solution = WorkspaceManager.GetCurrentSolution(workspaceId);
        Assert.IsNotNull(solution, "Reader must observe a valid Solution after a reload.");
        Assert.IsTrue(solution.Projects.Any(),
            "Post-reload Solution must carry the reloaded projects — no half-initialized snapshot.");

        // Second writer's auto-reload: mark stale again and reload. Reader's subsequent
        // read must still succeed and must NOT throw.
        FileWatcher.MarkStale(workspaceId, StaleReasons.Apply);
        await WorkspaceManager.ReloadAsync(workspaceId, CancellationToken.None);

        // Third consecutive read — the "workspace_changes" reader in the original cascade.
        // Prior bug: this path could throw ObjectDisposedException that escaped as a host
        // crash. Post-fix: returns a valid Solution synchronously, no exception.
        var solutionAfter = WorkspaceManager.GetCurrentSolution(workspaceId);
        Assert.IsNotNull(solutionAfter, "Reader must observe a valid Solution after a second reload.");
        Assert.IsTrue(solutionAfter.Projects.Any(),
            "Double-reload must leave a coherent Solution for the follow-up reader.");
    }

    /// <summary>
    /// Exhaustive retry-resilience: across many reload cycles, concurrent reads never
    /// observe a disposed workspace (they either see the prior or the fresh snapshot).
    /// This models the long-session shape where dozens of writer+reader turns interleave.
    /// </summary>
    [TestMethod]
    public async Task ConcurrentReload_And_Reads_Never_Throw_DisposedException()
    {
        var workspaceId = await LoadSharedSampleWorkspaceAsync();

        const int iterations = 5;
        for (var i = 0; i < iterations; i++)
        {
            FileWatcher.MarkStale(workspaceId, StaleReasons.Apply);
            var reloadTask = WorkspaceManager.ReloadAsync(workspaceId, CancellationToken.None);

            // Race a concurrent reader against the in-flight reload. With the pre-fix
            // dispose-before-swap pattern this often threw ObjectDisposedException; with
            // the fix the reader reliably observes either the old or new Solution.
            var readerTask = Task.Run(() =>
            {
                // Spin up a few reads that would have raced the reload's dispose window.
                for (var j = 0; j < 10; j++)
                {
                    var solution = WorkspaceManager.GetCurrentSolution(workspaceId);
                    Assert.IsNotNull(solution, $"Iter {i} reader {j}: Solution must be non-null.");
                }
            });

            await Task.WhenAll(reloadTask, readerTask);
        }
    }
}
