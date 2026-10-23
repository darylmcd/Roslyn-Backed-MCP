using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

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
    /// Per-attempt bound for awaiting the staleness signal off
    /// <see cref="IFileWatcherService.WaitForStaleAsync"/>. The wait is event-driven (it completes
    /// the instant the watcher flips the flag), so a slow-but-delivered OS event still passes
    /// well inside this window — this is a ceiling for a hung/never-fired watcher, not a
    /// fixed sleep. Generous because the CI host occasionally stalls under load.
    /// </summary>
    private const int WatcherFlushTimeoutMs = 5000;

    /// <summary>
    /// Number of times we re-touch the file and re-await the signal before failing. Guards the
    /// genuinely-dropped-event case: <see cref="System.IO.FileSystemWatcher"/> can silently drop
    /// a single event under buffer pressure, so one rewrite + re-wait turns a one-in-a-thousand
    /// dropped event into a pass instead of a CI-gating flake.
    /// </summary>
    private const int WatcherWriteAttempts = 3;

    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [TestMethod]
    public void ConfigComparisonRead_DoesNotBlockUndoDeletion()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Requires Windows.");

        var configPath = Path.Combine(Path.GetTempPath(), $"roslyn-config-read-{Guid.NewGuid():N}.editorconfig");
        File.WriteAllText(configPath, "[*.cs]\nindent_size = 4\n");
        try
        {
            // Undo deletes a newly created config while the filesystem watcher can be
            // comparing its bytes. The comparison handle must permit that delete on Windows.
            using var comparison = FileWatcherService.OpenConfigReadStream(configPath);
            File.Delete(configPath);
            Assert.IsFalse(File.Exists(configPath));
        }
        finally
        {
            if (File.Exists(configPath)) File.Delete(configPath);
        }
    }

    [TestMethod]
    public Task ConfigWriteOperation_RejectsDriftBeforeAcquiringWriteAuthority()
        => EditorConfigWriteFixture.RunAsync("[*.cs]\nindent_size = 4\n"u8.ToArray(), async fixture =>
        {
            var before = "[*.cs]\nindent_size = 4\n"u8.ToArray();
            var external = "[*.cs]\nindent_size = 8\n"u8.ToArray();
            var operation = fixture.CreateOperation(before, "[*.cs]\nindent_size = 2\n"u8.ToArray());
            await File.WriteAllBytesAsync(fixture.ConfigPath, external);
            await Assert.ThrowsExactlyAsync<EditorConfigConcurrentEditException>(() => fixture.Coordinator.RunOwnedWriteAsync(operation, CancellationToken.None));
            CollectionAssert.AreEqual(external, await File.ReadAllBytesAsync(fixture.ConfigPath));
            Assert.IsFalse(operation.Applied);
        });

    [TestMethod]
    public Task FailedNewConfigWriteBeforeBytes_DoesNotCreateFileOrStaleness()
        => EditorConfigWriteFixture.RunAsync(null, async fixture =>
        {
            var primary = new IOException("before write");
            var attempts = 0;
            fixture.OnAuthorize = (_, _, _) => { attempts++; throw primary; };
            var operation = fixture.CreateOperation(null, "[*.cs]\nindent_size = 4\n"u8.ToArray());
            var observed = await Assert.ThrowsExactlyAsync<IOException>(() => fixture.Coordinator.RunOwnedWriteAsync(operation, CancellationToken.None));
            Assert.AreEqual(1, attempts);
            Assert.AreSame(primary, observed);
            Assert.IsFalse(File.Exists(fixture.ConfigPath));
            Assert.IsFalse(fixture.Watcher.IsStale(fixture.WorkspaceId));
        });

    [TestMethod]
    [DataRow(false, "create")]
    [DataRow(false, "change")]
    [DataRow(false, "delete")]
    [DataRow(true, "create")]
    [DataRow(true, "change")]
    [DataRow(true, "delete")]
    public async Task ExternalEditorConfigChange_InsideWorkspaceOrAncestor_IsExternalEdit(bool ancestor, string action)
    {
        var root = Path.Combine(Path.GetTempPath(), $"roslyn-config-watch-{Guid.NewGuid():N}");
        var workspaceRoot = Path.Combine(root, "workspace");
        Directory.CreateDirectory(workspaceRoot);
        var solutionPath = Path.Combine(workspaceRoot, "Sample.slnx");
        await File.WriteAllTextAsync(solutionPath, "<Solution />");
        var configPath = ancestor
            ? Path.Combine(root, ".editorconfig")
            : Path.Combine(workspaceRoot, "nested", ".editorconfig");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        if (action != "create")
        {
            await File.WriteAllTextAsync(configPath, "[*.cs]\nindent_size = 4\n");
        }

        using var watcher = new FileWatcherService(NullLogger<FileWatcherService>.Instance);
        var workspaceId = Guid.NewGuid().ToString("N");
        try
        {
            watcher.Watch(workspaceId, solutionPath);
            if (action == "delete")
            {
                File.Delete(configPath);
            }
            else
            {
                await File.WriteAllTextAsync(configPath, "[*.cs]\nindent_size = 8\n");
            }
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await watcher.WaitForStaleAsync(workspaceId, timeout.Token);
            Assert.AreEqual(StaleReasons.ExternalEdit, watcher.GetStaleReason(workspaceId));
        }
        finally
        {
            watcher.Unwatch(workspaceId);
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public Task FailedOwnedEditorConfigWrite_TracksOnlyActualByteChanges()
        => EditorConfigWriteFixture.RunAsync("[*.cs]\nindent_size = 4\n"u8.ToArray(), async fixture =>
        {
            var before = "[*.cs]\nindent_size = 4\n"u8.ToArray();
            var after = "[*.cs]\nindent_size = 8\n"u8.ToArray();
            foreach (var desired in new[] { before, after })
            {
                var primary = new IOException("after actual native transition");
                var faults = 0;
                fixture.OnAuthorize = (native, _, _) =>
                {
                    if (faults == 0 && HasCompletedConfigTransition(native, fixture.ConfigPath, desired))
                    {
                        CollectionAssert.AreEqual(desired, File.ReadAllBytes(fixture.ConfigPath));
                        faults++;
                        throw primary;
                    }
                    return Task.CompletedTask;
                };
                var operation = fixture.CreateOperation(before, desired);
                var observed = await Assert.ThrowsExactlyAsync<IOException>(() => fixture.Coordinator.RunOwnedWriteAsync(operation, CancellationToken.None));
                Assert.AreEqual(1, faults, "The fault must follow the actual completed native transition.");
                Assert.AreSame(primary, observed);
                CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(fixture.ConfigPath));
                Assert.IsFalse(fixture.Watcher.IsStale(fixture.WorkspaceId));
                await Task.Delay(300);
                Assert.IsFalse(fixture.Watcher.IsStale(fixture.WorkspaceId), "Delayed events for restored or unchanged bytes must remain clean.");
            }
            var forwardFaults = 0;
            var inverseDenials = 0;
            fixture.OnAuthorize = (native, paths, _) =>
            {
                if (!HasCompletedConfigTransition(native, fixture.ConfigPath, after)) return Task.CompletedTask;
                if (forwardFaults == 0)
                {
                    CollectionAssert.AreEqual(after, File.ReadAllBytes(fixture.ConfigPath));
                    forwardFaults++;
                    throw new IOException("after forward transition");
                }
                if (paths.Count > 1)
                {
                    inverseDenials++;
                    throw new UnauthorizedAccessException("The actual inverse source admission is denied.");
                }
                return Task.CompletedTask;
            };
            var failedInverse = fixture.CreateOperation(before, after);
            await Assert.ThrowsExactlyAsync<EditorConfigUnrecoveredWriteException>(() => fixture.Coordinator.RunOwnedWriteAsync(failedInverse, CancellationToken.None));
            Assert.AreEqual(1, forwardFaults);
            Assert.AreEqual(1, inverseDenials);
            Assert.IsTrue(failedInverse.Applied);
            Assert.AreEqual(StaleReasons.Apply, fixture.Watcher.GetStaleReason(fixture.WorkspaceId));
            CollectionAssert.AreEqual(after, await File.ReadAllBytesAsync(fixture.ConfigPath));
        });

    [TestMethod]
    public Task OwnedEditorConfigWrite_DoesNotMaskLaterDifferentExternalBytes()
        => EditorConfigWriteFixture.RunAsync(null, async fixture =>
        {
            var first = "[*.cs]\nindent_size = 4\n"u8.ToArray();
            var external = "[*.cs]\nindent_size = 8\n"u8.ToArray();
            await fixture.Coordinator.RunOwnedWriteAsync(fixture.CreateOperation(null, first), CancellationToken.None);
            Assert.AreEqual(StaleReasons.Apply, fixture.Watcher.GetStaleReason(fixture.WorkspaceId));
            await File.WriteAllBytesAsync(fixture.ConfigPath, external);
            Assert.IsTrue(SpinWait.SpinUntil(() => fixture.Watcher.GetStaleReason(fixture.WorkspaceId) == StaleReasons.ExternalEdit, TimeSpan.FromSeconds(5)));
            await fixture.Coordinator.RunOwnedWriteAsync(fixture.CreateOperation(external, "[*.cs]\nindent_size = 2\n"u8.ToArray()), CancellationToken.None);
            Assert.AreEqual(StaleReasons.ExternalEdit, fixture.Watcher.GetStaleReason(fixture.WorkspaceId), "An owned write must preserve the actual prior external attribution.");
        });

    [TestMethod]
    public Task OwnedEditorConfigWrite_WindowsConcurrentPlanningEditIsPreserved()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Requires Windows.");
        return AssertConcurrentPlanningEditAsync(replace: false);
    }

    [TestMethod]
    public Task OwnedEditorConfigWrite_PosixSameInodeEditBeforeWriteIsExternalEdit()
    {
        if (!OperatingSystem.IsLinux()) Assert.Inconclusive("Requires Linux.");
        return AssertConcurrentPlanningEditAsync(replace: false);
    }

    [TestMethod]
    public Task OwnedEditorConfigWrite_PosixRenameReplacementRefusesSuccess()
    {
        if (!OperatingSystem.IsLinux()) Assert.Inconclusive("Requires Linux.");
        return AssertConcurrentPlanningEditAsync(replace: true);
    }

    /// <summary>
    /// Core validation scenario from the plan: load workspace → write to a tracked
    /// <c>.cs</c> via <see cref="System.IO.File"/> (simulating Claude Code's <c>Edit</c> tool
    /// writing outside the server apply channel) → <c>workspace_status</c> must report
    /// <c>isStale=true, staleReason="external-edit"</c>.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task AwaitedOwnedWrite_ReconcilesExactPathEventsAndPreservesUnrelatedExternalEdit(bool replaceConfig)
        => EditorConfigWriteFixture.RunAsync("[*.cs]\nindent_size = 4\n"u8.ToArray(), async fixture =>
        {
            var before = "[*.cs]\nindent_size = 4\n"u8.ToArray();
            var desired = "[*.cs]\nindent_size = 8\n"u8.ToArray();
            var foreign = "[*.cs]\nindent_size = 2\n"u8.ToArray();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var observedEvent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var paused = false;
            fixture.OnAuthorize = async (native, _, ct) =>
            {
                if (paused || !HasCompletedConfigTransition(native, fixture.ConfigPath, desired)) return;
                paused = true;
                CollectionAssert.AreEqual(desired, File.ReadAllBytes(fixture.ConfigPath));
                entered.TrySetResult();
                await release.Task.WaitAsync(ct);
            };
            FileSystemWatcher? observer = null;
            Task? running = null;
            Exception? primary = null;
            try
            {
                running = fixture.Coordinator.RunOwnedWriteAsync(fixture.CreateOperation(before, desired), CancellationToken.None);
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                observer = new FileSystemWatcher(fixture.Root, ".editorconfig") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size };
                observer.Changed += (_, _) => observedEvent.TrySetResult();
                observer.EnableRaisingEvents = true;
                await File.WriteAllBytesAsync(fixture.ConfigPath, replaceConfig ? foreign : desired);
                await observedEvent.Task.WaitAsync(TimeSpan.FromSeconds(10));
                if (!replaceConfig)
                {
                    await File.WriteAllTextAsync(Path.Combine(fixture.Root, "External.cs"), "public class External { }");
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await fixture.Watcher.WaitForStaleAsync(fixture.WorkspaceId, timeout.Token);
                    Assert.AreEqual(StaleReasons.ExternalEdit, fixture.Watcher.GetStaleReason(fixture.WorkspaceId));
                }
                release.TrySetResult();
                Exception? failure = null;
                try { await running; }
                catch (Exception caught) { failure = caught; }
                finally { running = null; }
                Assert.IsTrue(paused, "The real completed native transition must reach its awaited current grant.");
                if (replaceConfig) Assert.IsInstanceOfType<EditorConfigConcurrentEditException>(failure);
                else Assert.IsNull(failure);
                CollectionAssert.AreEqual(replaceConfig ? foreign : desired, await File.ReadAllBytesAsync(fixture.ConfigPath));
                Assert.AreEqual(StaleReasons.ExternalEdit, fixture.Watcher.GetStaleReason(fixture.WorkspaceId));
            }
            catch (Exception failure) { primary = failure; throw; }
            finally
            {
                release.TrySetResult();
                await CleanupFailureCollector.RunAfterFailureAsync("Awaited config event fixture cleanup failed.", primary,
                    async () => { if (running is not null) await running; },
                    CleanupFailureCollector.FromAction(() => observer?.Dispose()));
            }
        });

    private static bool HasCompletedConfigTransition(PhysicalMutationRecovery operation, string path, byte[] bytes)
        => operation.CompletedSourceTransition(path) is { OriginalText: not null } completed
            && completed.OriginalBytes.AsSpan().SequenceEqual(bytes);

    private static Task AssertConcurrentPlanningEditAsync(bool replace)
        => EditorConfigWriteFixture.RunAsync("[*.cs]\nindent_size = 4\n"u8.ToArray(), async fixture =>
        {
            var before = "[*.cs]\nindent_size = 4\n"u8.ToArray();
            var external = "[*.cs]\nindent_size = 8\n"u8.ToArray();
            var replacement = Path.Combine(fixture.Root, "replacement.editorconfig");
            await File.WriteAllBytesAsync(replacement, external);
            var mutations = 0;
            fixture.OnAuthorize = (_, paths, _) =>
            {
                if (mutations == 0 && paths.Count > 1 && paths.Contains(fixture.ConfigPath))
                {
                    if (replace) File.Move(replacement, fixture.ConfigPath, overwrite: true);
                    else
                    {
                        using var competing = new FileStream(fixture.ConfigPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                        competing.SetLength(0);
                        competing.Write(external);
                    }
                    CollectionAssert.AreEqual(external, File.ReadAllBytes(fixture.ConfigPath));
                    mutations++;
                }
                return Task.CompletedTask;
            };
            var operation = fixture.CreateOperation(before, "[*.cs]\nindent_size = 2\n"u8.ToArray());
            Exception? failure = null;
            try { await fixture.Coordinator.RunOwnedWriteAsync(operation, CancellationToken.None); }
            catch (Exception caught) { failure = caught; }
            Assert.AreEqual(1, mutations, "The actual source-transition admission callback must change the physical input before the verdict.");
            Assert.IsInstanceOfType<PublicInvalidOperationException>(failure);
            Assert.IsFalse(operation.Applied);
            Assert.AreEqual(StaleReasons.ExternalEdit, fixture.Watcher.GetStaleReason(fixture.WorkspaceId));
            CollectionAssert.AreEqual(external, await File.ReadAllBytesAsync(fixture.ConfigPath));
        });

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
            await WaitForStaleAsync(workspace.WorkspaceId, trackedFile, CancellationToken.None);

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
            await WaitForStaleAsync(workspace.WorkspaceId, trackedFile, CancellationToken.None);
            Assert.AreEqual(StaleReasons.ExternalEdit,
                WorkspaceManager.GetStaleReason(workspace.WorkspaceId),
                "Precondition: watcher attributed the write as external-edit.");

            var ex = Assert.ThrowsExactly<InvalidOperationException>(
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
            await WaitForStaleAsync(workspace.WorkspaceId, trackedFile, CancellationToken.None);
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
    /// Writes to a <c>.cs</c> file inside a simulated worktree subdirectory
    /// (<c>.worktrees/agent-xxx/</c>) must NOT flip the primary workspace's
    /// <c>IsStale</c> flag. This guards the cross-workspace contamination scenario where
    /// workspace A monitors <c>C:\Repo\</c> recursively and worktree workspace B lives under
    /// <c>C:\Repo\.worktrees\agent-xxx\</c> — every <c>apply_*</c> write to B previously
    /// triggered a spurious stale-reload on A.
    /// </summary>
    [TestMethod]
    public async Task WorktreeSubdirectoryWrite_DoesNotFlipIsStale_OnPrimaryWorkspace()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);

        var initialStatus = WorkspaceManager.GetStatus(workspace.WorkspaceId);
        Assert.IsFalse(initialStatus.IsStale, "Precondition: a freshly loaded workspace is not stale.");

        // Create a simulated worktree subdirectory under the workspace root — this mirrors
        // the layout where a parallel worktree workspace lives at
        // <solutionRoot>/.worktrees/agent-xxx/Solution.slnx
        var worktreePath = workspace.GetPath(".worktrees", "agent-xxx");
        Directory.CreateDirectory(worktreePath);

        try
        {
            // Write a .cs file inside .worktrees/agent-xxx/ — must NOT flip isStale on workspace A.
            var worktreeFile = Path.Combine(worktreePath, $"WorktreeProbe{Guid.NewGuid():N}.cs");
            await File.WriteAllTextAsync(worktreeFile, "// worktree workspace probe\nnamespace Probe;\n", CancellationToken.None);

            // Wait up to WatcherFlushTimeoutMs for any spurious watcher event that would flip the flag.
            await Task.Delay(WatcherFlushTimeoutMs, CancellationToken.None).ConfigureAwait(false);

            var status = WorkspaceManager.GetStatus(workspace.WorkspaceId);
            Assert.IsFalse(status.IsStale,
                "Writes under .worktrees/ must not contaminate the primary workspace's isStale flag. " +
                "Ensure ShouldIgnorePath excludes paths containing a .worktrees directory segment.");
        }
        finally
        {
            if (Directory.Exists(worktreePath))
                Directory.Delete(worktreePath, recursive: true);
        }
    }

    /// <summary>
    /// Awaits the watcher's actual staleness signal via
    /// <see cref="IFileWatcherService.WaitForStaleAsync"/> rather than polling a wall-clock
    /// window, so a slow-but-delivered OS event passes deterministically. To survive the
    /// genuinely-dropped-event case (<see cref="System.IO.FileSystemWatcher"/> can drop a single
    /// event under buffer pressure), it re-touches <paramref name="trackedFile"/> and re-awaits
    /// up to <see cref="WatcherWriteAttempts"/> times before failing — so only a watcher that
    /// never fires across multiple writes (a real regression) fails the test.
    /// </summary>
    /// <param name="workspaceId">Workspace whose staleness flag to await.</param>
    /// <param name="trackedFile">
    /// The tracked file already mutated by the caller. Re-touched on a dropped event to re-arm
    /// the watcher; its content is preserved (the caller's <c>finally</c> restores the original).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    private static async Task WaitForStaleAsync(string workspaceId, string trackedFile, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= WatcherWriteAttempts; attempt++)
        {
            try
            {
                using var timeout = new CancellationTokenSource(WatcherFlushTimeoutMs);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
                await FileWatcher.WaitForStaleAsync(workspaceId, linked.Token).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // This attempt's bound elapsed without the watcher firing — likely a dropped OS
                // event. Re-touch the file (appending file-type-neutral trailing whitespace) to
                // re-arm the watcher, then re-await. A watcher that never fires across all attempts
                // is a real regression.
                if (attempt == WatcherWriteAttempts)
                {
                    break;
                }

                var current = await File.ReadAllTextAsync(trackedFile, ct).ConfigureAwait(false);
                await File.WriteAllTextAsync(
                    trackedFile,
                    current + new string(' ', attempt) + "\n",
                    ct).ConfigureAwait(false);
            }
        }

        Assert.Fail(
            $"FileSystemWatcher did not flip isStale across {WatcherWriteAttempts} writes " +
            $"(each awaited up to {WatcherFlushTimeoutMs} ms). The watcher likely isn't registered " +
            "against the path — a dropped single event would have been recovered by the re-touch.");
    }
}

/// <summary>
/// Direct (no real OS file events) unit coverage of the
/// <see cref="IFileWatcherService.WaitForStaleAsync"/> ↔ <see cref="IFileWatcherService.ClearStale"/>
/// concurrency seam in <see cref="FileWatcherService"/>. A caller parks on
/// <c>WaitForStaleAsync</c> waiting for a not-yet-stale workspace to flip; a concurrent
/// <c>ClearStale</c> (e.g. a reload settling) re-arms the internal signal. The parked awaiter
/// MUST be released deterministically — the re-arm cancels the pending stale-wait — rather than
/// being stranded on the now-orphaned <see cref="System.Threading.Tasks.TaskCompletionSource"/>
/// until the caller's own <see cref="System.Threading.CancellationToken"/> deadline elapses.
/// </summary>
[TestClass]
public sealed class FileWatcherClearStaleAwaiterTests
{
    [TestMethod]
    public async Task WorkspaceRootInsideWorktrees_TrackedEditStillMarksStale()
    {
        var tempParent = Path.Combine(Path.GetTempPath(), $"rmcp-fw-root-{Guid.NewGuid():N}");
        var worktreeRoot = Path.Combine(tempParent, ".worktrees", "agent-1");
        Directory.CreateDirectory(worktreeRoot);
        var workspacePath = Path.Combine(worktreeRoot, "Sentinel.slnx");
        await File.WriteAllTextAsync(workspacePath, "<Solution />", CancellationToken.None);

        try
        {
            using var watcher = new FileWatcherService(NullLogger<FileWatcherService>.Instance);
            const string workspaceId = "ws-inside-worktree";
            watcher.Watch(workspaceId, workspacePath);

            var trackedPath = Path.Combine(worktreeRoot, "Tracked.cs");
            await File.WriteAllTextAsync(
                trackedPath,
                "namespace WorktreeProbe; internal sealed class Tracked { }",
                CancellationToken.None);

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await watcher.WaitForStaleAsync(workspaceId, timeout.Token).ConfigureAwait(false);
            Assert.IsTrue(watcher.IsStale(workspaceId),
                "The worktree root's ancestry must not suppress events for files inside that workspace.");
            Assert.AreEqual(StaleReasons.ExternalEdit, watcher.GetStaleReason(workspaceId));
        }
        finally
        {
            Directory.Delete(tempParent, recursive: true);
        }
    }

    /// <summary>
    /// Regression for <c>filewatcher-waitforstale-clearstale-stranded-awaiter</c>: an awaiter parked
    /// on <see cref="FileWatcherService.WaitForStaleAsync"/> for a non-stale entry must be released
    /// when <see cref="FileWatcherService.ClearStale"/> re-arms the signal, instead of hanging on
    /// the replaced <see cref="System.Threading.Tasks.TaskCompletionSource"/> until its own
    /// cancellation deadline.
    ///
    /// Pre-fix this fails: <c>ClearStale</c> swapped in a fresh TCS without completing the outgoing
    /// one, so the parked task stayed pending forever. Post-fix the re-arm cancels the outgoing TCS
    /// — the contractually-documented outcome for an abandoned stale-wait.
    ///
    /// The assertion is deliberately timer-free. <c>ClearStale</c> calls
    /// <c>TaskCompletionSource.TrySetCanceled()</c> on the calling thread under the entry's reason
    /// lock, and a TCS's task state transitions synchronously with that call — the
    /// <c>RunContinuationsAsynchronously</c> flag defers only the scheduling of registered
    /// continuations, never the <c>IsCompleted</c>/<c>IsCanceled</c> property reads. So this test
    /// reads that state directly the instant <c>ClearStale</c> returns instead of awaiting a
    /// wall-clock bound; the old bounded await was a thread-pool-scheduling dependency and the
    /// cause of the registered flake (see <c>ai_docs/known-flakes.md</c> history), not a defect in
    /// <see cref="FileWatcherService"/>.
    ///
    /// <see cref="CancellationToken.None"/> at the park site is load-bearing for that: with a
    /// cancelable token <c>WaitForStaleAsync</c> hands back a <c>Task.WaitAsync</c> wrapper that
    /// resolves via a thread-pool continuation, whose state genuinely lags the underlying signal.
    /// </summary>
    [TestMethod]
    public async Task ClearStale_ReleasesAwaiterParkedOnPriorSignal_RatherThanStrandingIt()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"rmcp-fw-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        // FileWatcherService.Watch derives the watched directory from the parent of the supplied
        // path, so point it at a sentinel file inside the temp dir. The file need not exist — only
        // its parent directory must, which Watch verifies before registering the entry.
        var workspacePath = Path.Combine(tempDir, "Sentinel.slnx");

        using var watcher = new FileWatcherService(NullLogger<FileWatcherService>.Instance);
        try
        {
            const string workspaceId = "ws-clearstale-awaiter";
            watcher.Watch(workspaceId, workspacePath);

            Assert.IsFalse(
                watcher.IsStale(workspaceId),
                "Precondition: a freshly-watched entry is not stale, so WaitForStaleAsync parks.");

            // Park on the current signal. CancellationToken.None is deliberate: it makes
            // WaitForStaleAsync return the raw signal task rather than a Task.WaitAsync wrapper, so
            // the task's state flips synchronously inside ClearStale and is assertable without any
            // timer. Passing a cancelable token here would reintroduce the scheduling race.
            var parked = watcher.WaitForStaleAsync(workspaceId, CancellationToken.None);
            Assert.IsFalse(
                parked.IsCompleted,
                "Precondition: the awaiter is genuinely parked (entry is not stale yet).");

            // ClearStale re-arms the signal. The outgoing TCS the awaiter holds must be
            // completed/canceled by this call, synchronously, before it returns.
            watcher.ClearStale(workspaceId);

            // Pass/fail is decided here, by a synchronous state read with no scheduling dependency.
            Assert.IsTrue(
                parked.IsCompleted,
                "ClearStale must resolve the awaiter parked on the prior signal synchronously, " +
                "before it returns; the task was still pending, so the outgoing " +
                "TaskCompletionSource was replaced without being completed/canceled (the " +
                "stranded-awaiter regression).");
            Assert.IsTrue(
                parked.IsCanceled,
                "A re-arm (ClearStale) abandons the pending stale-wait, so the awaiter should be " +
                "canceled — not completed-as-stale, which would be a spurious wakeup since the " +
                "entry is now clean (IsStale=false, StaleReason=null).");

            // Non-gating hang-guard. The assertions above already decided the outcome, so on a
            // passing run this completes instantly; it stays to observe the task (an unexpected
            // fault surfaces here instead of going unobserved) and, being bounded, can only ever
            // fail the run rather than hang it.
            try
            {
                await parked.WaitAsync(TimeSpan.FromSeconds(30));
            }
            catch (OperationCanceledException)
            {
                // Expected path: the re-arm canceled the abandoned stale-wait.
            }
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort temp cleanup; a lingering FileSystemWatcher handle can briefly hold
                // the directory. Not material to the assertion under test.
            }
        }
    }
}
