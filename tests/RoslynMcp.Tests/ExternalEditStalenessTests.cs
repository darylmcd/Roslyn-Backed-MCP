using RoslynMcp.Core.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Covers <c>workspace-stale-after-external-edit-feedback</c>: when a tracked
/// <c>.cs</c>/<c>.csproj</c>/<c>.slnx</c> file changes on disk outside the server's apply
/// channel, <see cref="RoslynMcp.Roslyn.Services.FileWatcherService"/> must flip the
/// workspace's <c>isStale</c> flag AND record a <c>staleReason</c> of
/// <see cref="StaleReasons.ExternalEdit"/> so <c>workspace_status</c> can distinguish a
/// self-applied edit (<see cref="StaleReasons.Apply"/>) from a drift-by-external-tool.
///
/// Additionally verifies that
/// <see cref="RoslynMcp.Roslyn.Services.WorkspaceManager.EnsureFreshForWritePreview(string)"/>
/// refuses with an error envelope pointing at <c>workspace_reload</c> when the staleness
/// is attributed to an external edit — the intended gate for write-preview tools
/// (<c>change_signature_preview</c>, <c>move_type_to_file_preview</c>, etc.) so they
/// don't silently clobber the external change at <c>*_apply</c> time.
///
/// Uses an isolated sample-solution copy so real-file writes don't leak into the shared
/// fixture cache used by other tests.
/// </summary>
[TestClass]
public sealed class ExternalEditStalenessTests : IsolatedWorkspaceTestBase
{
    /// <summary>
    /// Upper bound for how long we wait on the OS-level <see cref="System.IO.FileSystemWatcher"/>
    /// to flush a <c>Changed</c> event after a <c>File.WriteAllText</c>. On Windows this is
    /// typically 5–30 ms, but the CI host occasionally runs at 100+ ms under load; 2 seconds
    /// is a generous ceiling that still catches a "watcher never fires" regression.
    /// </summary>
    private const int WatcherFlushTimeoutMs = 2000;

    /// <summary>
    /// Poll interval while waiting for the watcher event. Smaller than the flush timeout so
    /// the test finishes within ~50 ms on a healthy host.
    /// </summary>
    private const int WatcherPollIntervalMs = 25;

    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    /// <summary>
    /// Core validation scenario from the plan: load workspace → write to a tracked
    /// <c>.cs</c> via <see cref="System.IO.File"/> (simulating Claude Code's <c>Edit</c> tool
    /// writing outside the server apply channel) → <c>workspace_status</c> must report
    /// <c>isStale=true, staleReason="external-edit"</c>.
    /// </summary>
    [TestMethod]
    public async Task ExternalCsFileWrite_FlipsIsStale_AndSetsReasonToExternalEdit()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);

        var initialStatus = WorkspaceManager.GetStatus(workspace.WorkspaceId);
        Assert.IsFalse(initialStatus.IsStale, "Precondition: a freshly loaded workspace is not stale.");
        Assert.IsNull(initialStatus.StaleReason, "Precondition: staleReason is null on a fresh load.");

        var trackedFile = workspace.GetPath("SampleLib", "Dog.cs");
        Assert.IsTrue(File.Exists(trackedFile), "Precondition: sample fixture must include SampleLib/Dog.cs.");

        // Simulate an external tool (e.g. Claude Code's Edit tool) overwriting the file via
        // direct disk IO — no MSBuildWorkspace.TryApplyChanges, no server EditService path.
        var original = await File.ReadAllTextAsync(trackedFile, CancellationToken.None);
        var mutated = original + $"\n// external-edit probe {Guid.NewGuid():N}\n";
        await File.WriteAllTextAsync(trackedFile, mutated, CancellationToken.None);

        try
        {
            await WaitForStaleAsync(workspace.WorkspaceId, CancellationToken.None);

            var status = WorkspaceManager.GetStatus(workspace.WorkspaceId);
            Assert.IsTrue(status.IsStale,
                "FileSystemWatcher must flip isStale=true after an external .cs write.");
            Assert.AreEqual(StaleReasons.ExternalEdit, status.StaleReason,
                "Watcher-driven marks must attribute to 'external-edit' — the reason a write-preview tool will refuse on.");
        }
        finally
        {
            // Restore so IsolatedWorkspaceScope's directory cleanup doesn't have to reason
            // about the mutation (the copy is disposable anyway, but explicit is kinder).
            await File.WriteAllTextAsync(trackedFile, original, CancellationToken.None);
        }
    }

    /// <summary>
    /// External-edit staleness must gate write-preview tools: calling
    /// <see cref="RoslynMcp.Roslyn.Services.WorkspaceManager.EnsureFreshForWritePreview"/> on
    /// a workspace flagged with <c>staleReason="external-edit"</c> must throw a specific
    /// error message that includes the word "stale" and points at <c>workspace_reload</c>
    /// so the existing <c>ToolErrorHandler</c> surfaces the reload hint. This is the
    /// contract <c>change_signature_preview</c> and every other write-preview tool inherits
    /// once wired through this method.
    /// </summary>
    [TestMethod]
    public async Task EnsureFreshForWritePreview_RefusesWithReloadHint_WhenExternalEdit()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);

        // Baseline: no staleness, no throw.
        WorkspaceManager.EnsureFreshForWritePreview(workspace.WorkspaceId);

        var trackedFile = workspace.GetPath("SampleLib", "Dog.cs");
        var original = await File.ReadAllTextAsync(trackedFile, CancellationToken.None);
        var mutated = original + $"\n// external-edit probe {Guid.NewGuid():N}\n";
        await File.WriteAllTextAsync(trackedFile, mutated, CancellationToken.None);

        try
        {
            await WaitForStaleAsync(workspace.WorkspaceId, CancellationToken.None);
            Assert.AreEqual(StaleReasons.ExternalEdit,
                WorkspaceManager.GetStaleReason(workspace.WorkspaceId),
                "Precondition: watcher attributed the write as external-edit.");

            var ex = Assert.ThrowsException<InvalidOperationException>(
                () => WorkspaceManager.EnsureFreshForWritePreview(workspace.WorkspaceId),
                "Write-preview gate must throw when staleReason is external-edit.");

            StringAssert.Contains(ex.Message, "stale",
                "Error message must contain 'stale' so ToolErrorHandler appends the reload hint.");
            StringAssert.Contains(ex.Message, "workspace_reload",
                "Error message must point the caller at workspace_reload as the remedy.");
            StringAssert.Contains(ex.Message, StaleReasons.ExternalEdit,
                "Error message must name the reason so downstream tools can classify the failure.");
        }
        finally
        {
            await File.WriteAllTextAsync(trackedFile, original, CancellationToken.None);
        }
    }

    /// <summary>
    /// A self-attributed apply (<see cref="StaleReasons.Apply"/>) does NOT trigger the
    /// write-preview refusal. The gate is specifically for external edits — server-initiated
    /// apply writes are expected to settle via auto-reload on the next read. This test
    /// verifies <see cref="IFileWatcherService.MarkStale(string, string)"/> honors the
    /// attribution and that <c>EnsureFreshForWritePreview</c> is a no-op for the
    /// self-apply case.
    /// </summary>
    [TestMethod]
    public async Task EnsureFreshForWritePreview_DoesNotRefuse_WhenSelfAttributedApply()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);

        FileWatcher.MarkStale(workspace.WorkspaceId, StaleReasons.Apply);

        var status = WorkspaceManager.GetStatus(workspace.WorkspaceId);
        Assert.IsTrue(status.IsStale, "Explicit MarkStale must flip isStale=true.");
        Assert.AreEqual(StaleReasons.Apply, status.StaleReason,
            "Explicit MarkStale must record the 'apply' attribution.");

        // Should NOT throw — the server owns this staleness window.
        WorkspaceManager.EnsureFreshForWritePreview(workspace.WorkspaceId);
    }

    /// <summary>
    /// <see cref="StaleReasons.Restore"/> (undo / revert paths) behaves like
    /// <see cref="StaleReasons.Apply"/>: the server owns the write, so write-preview tools
    /// should not refuse. Documents the third valid reason value.
    /// </summary>
    [TestMethod]
    public async Task EnsureFreshForWritePreview_DoesNotRefuse_WhenSelfAttributedRestore()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);

        FileWatcher.MarkStale(workspace.WorkspaceId, StaleReasons.Restore);

        Assert.AreEqual(StaleReasons.Restore,
            WorkspaceManager.GetStaleReason(workspace.WorkspaceId));

        // Should NOT throw.
        WorkspaceManager.EnsureFreshForWritePreview(workspace.WorkspaceId);
    }

    /// <summary>
    /// After <c>workspace_reload</c> clears the stale flag, the reason must reset to
    /// <see langword="null"/> and the write-preview gate must stop refusing. This is the
    /// "caller accepted the external edit and reloaded" recovery path the error message
    /// points to.
    /// </summary>
    [TestMethod]
    public async Task WorkspaceReload_ClearsStaleReason()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);

        var trackedFile = workspace.GetPath("SampleLib", "Dog.cs");
        var original = await File.ReadAllTextAsync(trackedFile, CancellationToken.None);
        var mutated = original + $"\n// external-edit probe {Guid.NewGuid():N}\n";
        await File.WriteAllTextAsync(trackedFile, mutated, CancellationToken.None);

        try
        {
            await WaitForStaleAsync(workspace.WorkspaceId, CancellationToken.None);
            Assert.AreEqual(StaleReasons.ExternalEdit,
                WorkspaceManager.GetStaleReason(workspace.WorkspaceId));

            await WorkspaceManager.ReloadAsync(workspace.WorkspaceId, CancellationToken.None);

            var status = WorkspaceManager.GetStatus(workspace.WorkspaceId);
            Assert.IsFalse(status.IsStale, "Reload must clear the stale flag.");
            Assert.IsNull(status.StaleReason, "Reload must clear the reason alongside the flag.");

            // Gate should now permit the write-preview.
            WorkspaceManager.EnsureFreshForWritePreview(workspace.WorkspaceId);
        }
        finally
        {
            await File.WriteAllTextAsync(trackedFile, original, CancellationToken.None);
        }
    }

    /// <summary>
    /// <c>workspace_status</c>'s staleReason field must be omitted on the wire (via
    /// <c>WhenWritingNull</c>) when not stale. This keeps the shape backwards compatible for
    /// clients that parsed the pre-bundle DTO.
    /// </summary>
    [TestMethod]
    public async Task WorkspaceStatus_SerializesWithoutStaleReason_WhenNotStale()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);

        var status = WorkspaceManager.GetStatus(workspace.WorkspaceId);
        Assert.IsFalse(status.IsStale);
        Assert.IsNull(status.StaleReason);

        var json = System.Text.Json.JsonSerializer.Serialize(status);
        Assert.IsFalse(
            json.Contains("staleReason", StringComparison.Ordinal),
            "staleReason must be omitted from the wire shape when null — keep the field backwards compatible.");
    }

    /// <summary>
    /// Polls <see cref="RoslynMcp.Roslyn.Services.WorkspaceManager.IsStale"/> until it flips
    /// or the timeout fires. Required because <see cref="System.IO.FileSystemWatcher"/>
    /// delivers events asynchronously; a naive assert immediately after the write races the
    /// OS-level event dispatcher.
    /// </summary>
    private static async Task WaitForStaleAsync(string workspaceId, CancellationToken ct)
    {
        var deadline = Environment.TickCount64 + WatcherFlushTimeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (WorkspaceManager.IsStale(workspaceId))
            {
                return;
            }
            await Task.Delay(WatcherPollIntervalMs, ct).ConfigureAwait(false);
        }

        Assert.Fail(
            $"FileSystemWatcher did not flip isStale within {WatcherFlushTimeoutMs} ms of the external write. " +
            "Either the watcher isn't registered against the worktree path, or the OS dropped the event.");
    }
}
