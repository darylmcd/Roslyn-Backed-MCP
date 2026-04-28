using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression guard for <c>mcp-error-category-workspace-evicted-on-host-recycle</c>
/// (P3, self-audit on 2026-04-27).
///
/// <para>
/// Observed failure: when the MCP stdio host gracefully recycles (PID change with
/// <c>previousRecycleReason="graceful"</c>), every workspace-scoped tool call from the
/// prior session returned a bare <see cref="System.Collections.Generic.KeyNotFoundException"/>
/// with <c>category="NotFound"</c> — indistinguishable from a typo'd <c>workspaceId</c>.
/// Agents had no signal that the correct recovery was <c>workspace_load</c> to rehydrate
/// the prior solution.
/// </para>
///
/// <para>
/// Remediation: <see cref="WorkspaceManager"/> now records evicted workspace ids on
/// <see cref="WorkspaceManager.Close"/> / <see cref="WorkspaceManager.Dispose"/> with the
/// original <c>loadedAt</c>, and consults <see cref="WorkspaceEvictionRegistry"/> for the
/// cross-process recycle signal published by <c>Program.cs</c> at startup. Workspace
/// lookup misses now branch into three paths:
/// </para>
///
/// <list type="number">
///   <item><description><b>In-process eviction</b> — the manager has a recorded
///     <c>loadedAt</c> for the id. Throws <see cref="WorkspaceEvictedException"/> with
///     both timestamps populated.</description></item>
///   <item><description><b>Cross-process recycle</b> — the registry reports a recycle
///     and the manager has zero live sessions. Throws
///     <see cref="WorkspaceEvictedException"/> with <c>workspaceLoadedAt=null</c> (the
///     prior process's timestamp was lost with the process).</description></item>
///   <item><description><b>Genuine miss</b> — never-loaded id, no recycle context.
///     Throws plain <see cref="System.Collections.Generic.KeyNotFoundException"/>
///     surfacing as <c>category="NotFound"</c> (existing behavior).</description></item>
/// </list>
///
/// <para>
/// The classifier in <c>ToolErrorHandler</c> registers
/// <see cref="WorkspaceEvictedException"/> ahead of the base
/// <see cref="System.Collections.Generic.KeyNotFoundException"/> entry so the
/// dictionary's insertion-order walk produces <c>category="WorkspaceEvicted"</c> with
/// both timestamps embedded in the envelope message.
/// </para>
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class WorkspaceManagerEvictionTests
{
    private static string s_repositoryRootPath = null!;
    private static string s_sampleSolutionPath = null!;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        s_repositoryRootPath = TestFixtureFileSystem.FindRepositoryRoot();
        s_sampleSolutionPath = TestFixtureFileSystem.FindFixturePath(
            s_repositoryRootPath,
            "SampleSolution",
            "SampleSolution.slnx",
            "SampleSolution.sln");
    }

    /// <summary>
    /// Tests that exercise <see cref="WorkspaceEvictionRegistry"/> directly — a
    /// process-wide static — must reset the registry on cleanup so no other test class
    /// observes a stale recycle signal. The same isolation discipline applies to
    /// <see cref="HostProcessMetadataSnapshotProvider.Reset"/>; our tests do not touch
    /// that provider.
    /// <para>
    /// This class is also marked <see cref="DoNotParallelizeAttribute"/> at the class
    /// level so it cannot race with itself; combined with the parallel-class scope
    /// declared in <c>AssemblyInfo.cs</c>, the only window where another class could
    /// observe a stale recycle signal is between the test body's
    /// <see cref="WorkspaceEvictionRegistry.PublishRecycleContext"/> call and this
    /// cleanup running. Each publishing test wraps its setup in a try/finally for
    /// belt-and-suspenders safety.
    /// </para>
    /// </summary>
    [TestCleanup]
    public void TestCleanup() => WorkspaceEvictionRegistry.Reset();

    [ClassCleanup]
    public static void ClassCleanup() => WorkspaceEvictionRegistry.Reset();

    /// <summary>
    /// Path 1: in-process eviction via <see cref="WorkspaceManager.Close"/> records the
    /// id with its original <c>loadedAt</c>. A subsequent state-read against the closed
    /// id throws <see cref="WorkspaceEvictedException"/> classified as
    /// <c>category="WorkspaceEvicted"</c> in the structured envelope.
    /// </summary>
    [TestMethod]
    public async Task ClosedWorkspace_GetStatus_Throws_WorkspaceEvicted_With_LoadedAt()
    {
        using var manager = CreateManager();
        var path = TestFixtureFileSystem.CreateSampleSolutionCopy(s_repositoryRootPath, s_sampleSolutionPath);
        var loadedBeforeAtUtc = DateTimeOffset.UtcNow.AddMilliseconds(-1);

        try
        {
            var status = await manager.LoadAsync(path, CancellationToken.None);
            Assert.IsTrue(manager.Close(status.WorkspaceId),
                "Close must succeed for a freshly-loaded workspace.");

            // Direct exception assertion — manager throws WorkspaceEvictedException, the
            // most-specific shape consumers can branch on without going through the
            // ToolErrorHandler dictionary.
            var ex = Assert.ThrowsException<WorkspaceEvictedException>(() =>
                manager.GetStatus(status.WorkspaceId));

            Assert.AreEqual(status.WorkspaceId, ex.WorkspaceId,
                "WorkspaceId must round-trip onto the typed exception.");
            Assert.IsNotNull(ex.WorkspaceLoadedAtUtc,
                "Same-process eviction must carry the recorded loadedAt.");
            Assert.IsTrue(ex.WorkspaceLoadedAtUtc!.Value >= loadedBeforeAtUtc,
                "WorkspaceLoadedAt must be at or after the pre-load watermark.");
            Assert.AreEqual(WorkspaceEvictionRegistry.ServerStartedAtUtc, ex.ServerStartedAtUtc,
                "ServerStartedAt must match the registry's reported value.");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(Path.GetDirectoryName(path)!);
        }
    }

    /// <summary>
    /// End-to-end through the same classifier path the MCP filter uses: the typed
    /// exception surfaces as <c>category="WorkspaceEvicted"</c> with both timestamps
    /// embedded in the message. This is the contract the backlog row called for —
    /// agents reading the envelope must see a category that distinguishes recycle
    /// eviction from a typo'd <c>workspaceId</c>.
    /// </summary>
    [TestMethod]
    public async Task ClosedWorkspace_ToolEnvelope_Carries_WorkspaceEvicted_Category()
    {
        const string workspaceId = "ws-evicted-fixture-id";
        var loadedAtUtc = DateTimeOffset.Parse("2026-04-27T08:15:30.0000000+00:00");
        var serverStartedAtUtc = DateTimeOffset.Parse("2026-04-27T08:14:00.0000000+00:00");

        // No PublishRecycleContext here — this test just exercises the classifier path
        // for a manually-constructed WorkspaceEvictedException, no manager state needed.

        var result = await ToolExecutionTestHarness.RunAsync(
            "workspace_changes",
            () => throw new WorkspaceEvictedException(
                workspaceId,
                serverStartedAtUtc,
                loadedAtUtc,
                $"Workspace '{workspaceId}' was evicted from the live session set."));

        using var json = JsonDocument.Parse(result);
        Assert.IsTrue(json.RootElement.GetProperty("error").GetBoolean(),
            "Eviction errors must carry error=true on the envelope.");
        Assert.AreEqual("WorkspaceEvicted",
            json.RootElement.GetProperty("category").GetString(),
            "Category MUST be the structured 'WorkspaceEvicted' marker, not 'NotFound'.");
        Assert.AreEqual("workspace_changes",
            json.RootElement.GetProperty("tool").GetString(),
            "Tool name must be preserved in the envelope.");
        Assert.AreEqual(nameof(WorkspaceEvictedException),
            json.RootElement.GetProperty("exceptionType").GetString(),
            "exceptionType must be the most-derived type, not the base KeyNotFoundException.");

        var message = json.RootElement.GetProperty("message").GetString()!;
        StringAssert.Contains(message, $"serverStartedAt={serverStartedAtUtc:O}",
            "Envelope must surface serverStartedAt so callers can correlate with server_info.");
        StringAssert.Contains(message, $"workspaceLoadedAt={loadedAtUtc:O}",
            "Envelope must surface workspaceLoadedAt for same-process evictions.");
    }

    /// <summary>
    /// Path 2: cross-process recycle eviction. <see cref="WorkspaceEvictionRegistry"/>
    /// reports a recycle signal AND the manager has zero live sessions. A workspace
    /// lookup against an id the prior process owned (any id, since we have no record)
    /// throws <see cref="WorkspaceEvictedException"/> with <c>WorkspaceLoadedAtUtc=null</c>
    /// — the prior <c>loadedAt</c> was lost with the prior process.
    /// </summary>
    [TestMethod]
    public void HostRecycled_AnyLookup_Throws_WorkspaceEvicted_WithoutLoadedAt()
    {
        var serverStartedAtUtc = DateTimeOffset.Parse("2026-04-27T09:00:00.0000000+00:00");
        WorkspaceEvictionRegistry.PublishRecycleContext(
            serverStartedAtUtc,
            previousRecycleReason: "graceful");

        using var manager = CreateManager();

        // Any id — the manager has no live sessions and the registry says we just
        // recycled, so this is unambiguously a recycle eviction (the prior process
        // owned the now-missing id).
        const string priorWorkspaceId = "abc123def456ghi789";
        var ex = Assert.ThrowsException<WorkspaceEvictedException>(() =>
            manager.GetStatus(priorWorkspaceId));

        Assert.AreEqual(priorWorkspaceId, ex.WorkspaceId);
        Assert.AreEqual(serverStartedAtUtc, ex.ServerStartedAtUtc,
            "ServerStartedAt must be the registry's published value, not DateTime.UtcNow.");
        Assert.IsNull(ex.WorkspaceLoadedAtUtc,
            "Cross-process recycle leaves loadedAt unrecoverable; envelope omits the field.");
        StringAssert.Contains(ex.Message, "graceful",
            "Recycle reason must surface in the message so callers can branch on the cause.");
    }

    /// <summary>
    /// Path 3: legitimate miss. No prior recycle, no in-process eviction record. The
    /// manager throws plain <see cref="System.Collections.Generic.KeyNotFoundException"/>
    /// — NOT <see cref="WorkspaceEvictedException"/> — preserving the existing
    /// <c>category="NotFound"</c> envelope for callers that genuinely typo'd the id.
    /// </summary>
    [TestMethod]
    public void NeverLoaded_AndNoRecycle_Throws_PlainKeyNotFoundException()
    {
        // Reset the registry to a cold-start state — no prior recycle signal.
        WorkspaceEvictionRegistry.Reset();
        using var manager = CreateManager();

        const string typoedWorkspaceId = "this-id-was-never-issued";
        var ex = Assert.ThrowsException<KeyNotFoundException>(() =>
            manager.GetStatus(typoedWorkspaceId));

        Assert.IsFalse(ex is WorkspaceEvictedException,
            "A miss with no recycle context and no prior eviction record must NOT be " +
            "elevated to WorkspaceEvictedException — that would over-classify legitimate " +
            "typos as eviction recoveries.");
        StringAssert.Contains(ex.Message, typoedWorkspaceId,
            "The plain envelope must still surface the requested id for diagnosis.");
    }

    /// <summary>
    /// Edge case: cross-process recycle context is published, but the current process
    /// has at least one workspace loaded. A miss for a different id is still a typo,
    /// not a recycle eviction — the recycle would have evicted EVERYTHING, so a live
    /// session being present means the caller is asking about a fresh-process id that
    /// was never issued.
    /// </summary>
    [TestMethod]
    public async Task HostRecycled_ButLiveSessionExists_TypoStillThrowsKeyNotFoundException()
    {
        WorkspaceEvictionRegistry.PublishRecycleContext(
            DateTimeOffset.UtcNow,
            previousRecycleReason: "graceful");

        using var manager = CreateManager();
        var path = TestFixtureFileSystem.CreateSampleSolutionCopy(s_repositoryRootPath, s_sampleSolutionPath);

        try
        {
            var loaded = await manager.LoadAsync(path, CancellationToken.None);
            Assert.AreNotEqual(string.Empty, loaded.WorkspaceId,
                "LoadAsync must return a non-empty id (sanity).");

            const string typoedId = "still-a-typo-because-there-is-a-live-session";
            var ex = Assert.ThrowsException<KeyNotFoundException>(() =>
                manager.GetStatus(typoedId));
            Assert.IsFalse(ex is WorkspaceEvictedException,
                "When a live session exists in the current process, a miss is a typo " +
                "regardless of the recycle signal — recycle would have wiped everything.");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(Path.GetDirectoryName(path)!);
        }
    }

    /// <summary>
    /// Catch-site compatibility check: <see cref="WorkspaceEvictedException"/> derives
    /// from <see cref="System.Collections.Generic.KeyNotFoundException"/> so existing
    /// <c>catch (KeyNotFoundException)</c> sites (e.g.
    /// <c>WorkspaceExecutionGate.AutoReloadAsync</c>'s defensive catch) continue to
    /// observe the lookup miss — they just lose the structural eviction signal,
    /// which is acceptable because those sites already handle the miss as a
    /// generic "workspace gone" condition.
    /// </summary>
    [TestMethod]
    public void WorkspaceEvictedException_IsAssignableTo_KeyNotFoundException()
    {
        var ex = new WorkspaceEvictedException(
            "any-id",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            "test message");

        Assert.IsTrue(ex is KeyNotFoundException,
            "Catch-site contract: WorkspaceEvictedException must be assignable to " +
            "KeyNotFoundException so existing handlers do not regress.");
    }

    private static WorkspaceManager CreateManager()
    {
        return new WorkspaceManager(
            NullLogger<WorkspaceManager>.Instance,
            new PreviewStore(),
            new FileWatcherService(NullLogger<FileWatcherService>.Instance),
            new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 4 });
    }
}
