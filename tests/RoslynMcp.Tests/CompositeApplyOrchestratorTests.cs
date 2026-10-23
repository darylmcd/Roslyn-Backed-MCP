using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Diagnostics;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;
using RoslynMcp.Tests.TestInfrastructure;

namespace RoslynMcp.Tests;

/// <summary>Exercises composite orchestration, physical transitions, encoding and operation-owned undo.</summary>
[TestClass]
public sealed class CompositeApplyOrchestratorTests
{
    private string _tempDir = string.Empty;

    [TestInitialize]
    public void Init()
    {
        _tempDir = Path.Combine(TestTempRoot.Current, "roslynmcp-atomicwrite-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        TestFixtureFileSystem.DeleteDirectoryIfExists(_tempDir);
    }

    [TestMethod]
    [DataRow("delete")]
    [DataRow("encoded-write")]
    [DataRow("pre-write-failure")]
    [DataRow("partial-apply")]
    [DataRow("reload-failure")]
    [DataRow("successful-invalidation")]
    public async Task ApplyComposite_OrchestrationScenarios_PreserveMutationAndTokenContracts(string scenario)
    {
        var primaryPath = Path.Combine(_tempDir, "primary.cs");
        var mutations = new List<CompositeFileMutation>();
        Exception? reloadFailure = null;

        switch (scenario)
        {
            case "delete":
                await File.WriteAllTextAsync(primaryPath, "delete me");
                mutations.Add(new CompositeFileMutation(primaryPath, null, DeleteFile: true));
                break;
            case "encoded-write":
                await File.WriteAllBytesAsync(
                    primaryPath,
                    Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("old")).ToArray());
                mutations.Add(new CompositeFileMutation(primaryPath, "new", DeleteFile: false));
                break;
            case "pre-write-failure":
                mutations.Add(new CompositeFileMutation(CreateBlockedChildPath(), "never", DeleteFile: false));
                break;
            case "partial-apply":
                mutations.Add(new CompositeFileMutation(primaryPath, "applied", DeleteFile: false));
                mutations.Add(new CompositeFileMutation(CreateBlockedChildPath(), "never", DeleteFile: false));
                break;
            case "reload-failure":
                mutations.Add(new CompositeFileMutation(primaryPath, "applied", DeleteFile: false));
                reloadFailure = new InvalidOperationException("reload failed");
                break;
            case "successful-invalidation":
                mutations.Add(new CompositeFileMutation(primaryPath, "applied", DeleteFile: false));
                break;
            default:
                Assert.Fail($"Unknown scenario '{scenario}'.");
                break;
        }

        var store = new CompositePreviewStore();
        var token = store.Store("ws-1", 1, scenario, mutations);
        using var workspace = new RecordingWorkspaceManager(reloadFailure);
        var orchestrator = new CompositeApplyOrchestrator(workspace, store);

        var result = await orchestrator.ApplyCompositeAsync(token, CancellationToken.None);

        switch (scenario)
        {
            case "delete":
                Assert.IsTrue(result.Success, result.Error);
                Assert.IsFalse(File.Exists(primaryPath));
                break;
            case "encoded-write":
                Assert.IsTrue(result.Success, result.Error);
                Assert.IsTrue((await File.ReadAllBytesAsync(primaryPath)).AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));
                break;
            case "pre-write-failure":
                Assert.IsFalse(result.Success);
                Assert.AreEqual(0, result.AppliedFiles.Count);
                Assert.IsFalse(workspace.ReloadCalled);
                break;
            case "partial-apply":
                Assert.IsFalse(result.Success);
                CollectionAssert.AreEqual(new[] { primaryPath }, result.AppliedFiles.ToArray());
                Assert.IsFalse(workspace.ReloadCalled);
                break;
            case "reload-failure":
                Assert.IsFalse(result.Success);
                CollectionAssert.AreEqual(new[] { primaryPath }, result.AppliedFiles.ToArray());
                Assert.IsNotNull(store.Retrieve(token));
                break;
            case "successful-invalidation":
                Assert.IsTrue(result.Success, result.Error);
                Assert.IsNull(store.Retrieve(token));
                break;
        }

        string CreateBlockedChildPath()
        {
            var blocker = Path.Combine(_tempDir, $"blocker-{scenario}");
            File.WriteAllText(blocker, "not a directory");
            return Path.Combine(blocker, "child.cs");
        }
    }

    [TestMethod]
    public async Task ApplyComposite_MidLoop_Failure_Applies_Prior_Writes_Marks_Partial_And_Logs()
    {
        // Arrange: a first valid write, then a second mutation whose parent path is an existing
        // FILE — so Directory.CreateDirectory throws IOException mid-loop, after the first write
        // already hit disk. This exercises the "partial composite apply" branch deterministically
        // and cross-platform.
        const string sentinel = "SECRET-SENTINEL-composite";
        var blocker = Path.Combine(_tempDir, sentinel);
        await File.WriteAllTextAsync(blocker, "i am a file, not a directory");

        var goodPath = Path.Combine(_tempDir, "good.cs");
        var doomedPath = Path.Combine(blocker, "nested.cs"); // parent 'blocker' is a file

        var mutations = new List<CompositeFileMutation>
        {
            new(goodPath, "// good content", DeleteFile: false),
            new(doomedPath, "// never written", DeleteFile: false),
        };

        var store = new CompositePreviewStore();
        var token = store.Store("ws-1", 1, "test composite", mutations);

        using var workspace = new RecordingWorkspaceManager();
        var logger = new RecordingLogger<CompositeApplyOrchestrator>();
        var sink = new CapturingServerObservabilitySink();
        var reporter = new ServerObservabilityReporter(sink);
        var orchestrator = new CompositeApplyOrchestrator(
            workspace,
            store,
            changeTracker: null,
            logger: logger,
            exceptionReporter: reporter);

        // Act
        var result = await orchestrator.ApplyCompositeAsync(token, CancellationToken.None);

        // Assert: failed, but the first write landed and is marked as a partial apply.
        Assert.IsFalse(result.Success, "A mid-loop failure must report success:false.");
        CollectionAssert.AreEqual(new[] { goodPath }, result.AppliedFiles.ToArray(), "Only the pre-failure write should be reported as applied.");
        Assert.IsNotNull(result.Error);
        StringAssert.StartsWith(result.Error, "Partial composite apply: ", "The message must carry the partial-apply marker.");
        StringAssert.Contains(result.Error, "mutation[1]");
        StringAssert.Contains(result.Error, "correlationId=");
        Assert.IsFalse(result.Error.Contains(sentinel, StringComparison.Ordinal));
        Assert.IsFalse(result.Error.Contains(_tempDir, StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual("// good content", await File.ReadAllTextAsync(goodPath), "The successful write must remain on disk.");
        Assert.IsEmpty(Directory.EnumerateFiles(_tempDir, "*.tmp"));
        Assert.IsEmpty(Directory.EnumerateFiles(_tempDir, ".rmcp-prepared-*"));
        Assert.IsFalse(File.Exists(doomedPath), "The failing mutation must not have produced a file.");

        // A warning was logged for the partial failure.
        Assert.IsTrue(
            logger.Entries.Any(e => e.Level == LogLevel.Warning),
            "A warning must be logged when a composite apply fails partway through.");

        // The workspace must NOT be reloaded on a failed apply.
        Assert.IsFalse(workspace.ReloadCalled, "ReloadAsync must not run when the apply failed.");

        // The in-memory-only store preserves its existing retry contract. Persistent stores claim
        // before mutation and therefore fail closed instead of making a partial operation replayable.
        Assert.IsNotNull(store.Retrieve(token), "The preview token must remain valid after a partial failure.");

        Assert.HasCount(1, sink.Events);
        Assert.AreEqual("CompositeApply", sink.Events.Single().Category);
        Assert.IsFalse(JsonSerializer.Serialize(sink.Events.Single()).Contains(sentinel, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ApplyComposite_FirstMutation_Failure_Is_Not_Marked_Partial()
    {
        // When nothing was written before the failure it is a clean failure, not a partial apply.
        const string sentinel = "SECRET-SENTINEL-composite-first";
        var blocker = Path.Combine(_tempDir, sentinel);
        await File.WriteAllTextAsync(blocker, "i am a file");
        var doomedPath = Path.Combine(blocker, "nested.cs");

        var mutations = new List<CompositeFileMutation> { new(doomedPath, "// never written", DeleteFile: false) };
        var store = new CompositePreviewStore();
        var token = store.Store("ws-1", 1, "test composite", mutations);
        var sink = new CapturingServerObservabilitySink();
        using var workspace = new RecordingWorkspaceManager();
        var orchestrator = new CompositeApplyOrchestrator(
            workspace,
            store,
            exceptionReporter: new ServerObservabilityReporter(sink));

        var result = await orchestrator.ApplyCompositeAsync(token, CancellationToken.None);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, result.AppliedFiles.Count);
        Assert.IsNotNull(result.Error);
        Assert.IsFalse(result.Error!.StartsWith("Partial composite apply: ", StringComparison.Ordinal),
            "A failure with zero prior writes is a clean failure, not a partial apply.");
        StringAssert.Contains(result.Error, "correlationId=");
        Assert.IsFalse(result.Error.Contains(sentinel, StringComparison.Ordinal));
        Assert.HasCount(1, sink.Events);
    }

    /// <summary>
    /// Regression guard for <c>composite-apply-undo-encoding-still-lossy</c>:
    /// <c>apply_composite_preview</c> wrote every mutation through
    /// a writer without the original <see cref="Encoding"/>, so a UTF-8-BOM or
    /// UTF-16 source file was silently re-encoded as UTF-8-no-BOM on every composite apply. This is
    /// the third write path PR #1157 (<c>mutation-write-paths-drop-original-encoding</c>) deferred.
    /// The <c>utf8-nobom</c> row is the inverse guard: a file that had no BOM must not gain one.
    /// </summary>
    [TestMethod]
    [DataRow("utf8-nobom")]
    [DataRow("utf8-bom")]
    [DataRow("utf16-bom")]
    public async Task ApplyComposite_Preserves_Original_File_Encoding(string encodingKind)
    {
        var path = Path.Combine(_tempDir, "target.cs");
        Encoding encoding = encodingKind switch
        {
            "utf16-bom" => new UnicodeEncoding(bigEndian: false, byteOrderMark: true),
            "utf8-bom" => new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            _ => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };
        var preamble = encoding.GetPreamble();

        const string originalContent = "class C { }\n";
        await File.WriteAllBytesAsync(
            path,
            preamble.Concat(encoding.GetBytes(originalContent)).ToArray(),
            CancellationToken.None);

        const string updatedContent = "class C { int Marker; }\n";
        var store = new CompositePreviewStore();
        var token = store.Store(
            "ws-1",
            1,
            "encoding round-trip",
            [new CompositeFileMutation(path, updatedContent, DeleteFile: false)]);
        using var workspace = new RecordingWorkspaceManager();
        var orchestrator = new CompositeApplyOrchestrator(workspace, store);

        var result = await orchestrator.ApplyCompositeAsync(token, CancellationToken.None);
        Assert.IsTrue(result.Success, result.Error);

        var afterBytes = await File.ReadAllBytesAsync(path, CancellationToken.None);
        Assert.IsTrue(
            afterBytes.AsSpan().StartsWith(preamble),
            $"apply_composite_preview must re-emit the original {encodingKind} byte-order mark instead of rewriting the file as UTF-8-no-BOM.");
        if (preamble.Length == 0)
        {
            Assert.IsFalse(
                afterBytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()),
                "A file that had no BOM must not gain one.");
        }

        Assert.AreEqual(
            updatedContent,
            encoding.GetString(afterBytes, preamble.Length, afterBytes.Length - preamble.Length),
            "The mutation must be readable back through the original encoding.");
    }

    /// <summary>
    /// Companion guard to <see cref="ApplyComposite_Preserves_Original_File_Encoding"/>: a mutation
    /// that CREATES a file (no pre-apply bytes on disk) must keep the historic UTF-8-no-BOM default
    /// — <c>ResolveWriteEncoding(null)</c> must not be allowed to start emitting a preamble.
    /// </summary>
    [TestMethod]
    public async Task ApplyComposite_NewFile_Is_Written_Utf8_Without_Bom()
    {
        var path = Path.Combine(_tempDir, "created.cs");
        Assert.IsFalse(File.Exists(path));

        var store = new CompositePreviewStore();
        var token = store.Store(
            "ws-1",
            1,
            "new file",
            [new CompositeFileMutation(path, "class New { }\n", DeleteFile: false)]);
        using var workspace = new RecordingWorkspaceManager();
        var orchestrator = new CompositeApplyOrchestrator(workspace, store);

        var result = await orchestrator.ApplyCompositeAsync(token, CancellationToken.None);
        Assert.IsTrue(result.Success, result.Error);

        var afterBytes = await File.ReadAllBytesAsync(path, CancellationToken.None);
        Assert.IsFalse(
            afterBytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()),
            "A newly created file must not gain a BOM.");
        Assert.AreEqual("class New { }\n", Encoding.UTF8.GetString(afterBytes));
    }

    /// <summary>
    /// Regression guard for <c>apply-composite-no-undo-capture</c>: the composite apply recorded a
    /// change but never captured an undo snapshot, so <c>revert_last_apply</c> immediately after
    /// <c>apply_composite_preview</c> reverted the PREVIOUS apply instead. The revert must restore the
    /// composite's modified, created, and deleted files, leave the previous apply intact, and the
    /// composite must appear in <c>workspace_changes</c>.
    /// </summary>
    [TestMethod]
    public async Task ApplyComposite_RevertLastApply_Restores_Composite_Files_Not_Previous_Apply()
    {
        const string workspaceId = "ws-1";
        using var workspace = new RecordingWorkspaceManager();
        using var changeTracker = new ChangeTracker(workspace);
        using var undoService = new UndoService(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<UndoService>.Instance,
            workspace,
            changeTracker);

        // A previous, independently revertable apply on an unrelated file.
        var priorPath = Path.Combine(_tempDir, "prior.cs");
        await File.WriteAllTextAsync(priorPath, "prior-original");
        await PublishTrackedFileMutationAsync(workspace, undoService, changeTracker,
            workspaceId, "prior apply", priorPath, "prior-applied");

        // Composite: modify an existing file, create a new one, delete an existing one.
        var modifiedPath = Path.Combine(_tempDir, "modified.cs");
        var createdPath = Path.Combine(_tempDir, "sub", "created.cs");
        var deletedPath = Path.Combine(_tempDir, "deleted.cs");
        var modifiedOriginal = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble()
            .Concat(Encoding.UTF8.GetBytes("class Modified { }\n"))
            .ToArray();
        await File.WriteAllBytesAsync(modifiedPath, modifiedOriginal);
        await File.WriteAllTextAsync(deletedPath, "class Deleted { }\n");

        var store = new CompositePreviewStore();
        var token = store.Store(
            workspaceId,
            1,
            "composite",
            [
                new CompositeFileMutation(modifiedPath, "class Modified { int X; }\n", DeleteFile: false),
                new CompositeFileMutation(createdPath, "class Created { }\n", DeleteFile: false),
                new CompositeFileMutation(deletedPath, null, DeleteFile: true),
            ]);
        var orchestrator = new CompositeApplyOrchestrator(
            workspace,
            store,
            changeTracker,
            undoService: undoService);

        var result = await orchestrator.ApplyCompositeAsync(token, CancellationToken.None);
        Assert.IsTrue(result.Success, result.Error);
        Assert.IsTrue(File.Exists(createdPath));
        Assert.IsFalse(File.Exists(deletedPath));

        // (3) workspace_changes records the composite apply.
        var changes = changeTracker.GetChanges(workspaceId);
        Assert.HasCount(2, changes);
        Assert.AreEqual("apply_composite_preview", changes[^1].ToolName);
        CollectionAssert.AreEquivalent(
            new[] { modifiedPath, createdPath, deletedPath },
            changes[^1].AffectedFiles.ToArray());

        // (1) revert_last_apply restores the composite's files.
        Assert.IsTrue(await undoService.RevertAsync(workspaceId, CancellationToken.None));
        CollectionAssert.AreEqual(modifiedOriginal, await File.ReadAllBytesAsync(modifiedPath));
        Assert.IsFalse(File.Exists(createdPath), "A file the composite created must be removed on revert.");
        Assert.AreEqual("class Deleted { }\n", await File.ReadAllTextAsync(deletedPath));

        // (2) The previous apply is untouched.
        Assert.AreEqual("prior-applied", await File.ReadAllTextAsync(priorPath));
    }

    [TestMethod]
    public async Task ApplyComposite_CaseDistinctPaths_UsePlatformPathIdentity()
    {
        const string workspaceId = "ws-1";
        var workspace = new RecordingWorkspaceManager();
        using var changeTracker = new ChangeTracker(workspace);
        using var undoService = new UndoService(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<UndoService>.Instance,
            workspace,
            changeTracker);
        var upperPath = Path.Combine(_tempDir, "Feature.cs");
        var lowerPath = Path.Combine(_tempDir, "feature.cs");
        await File.WriteAllTextAsync(upperPath, "upper-original");
        if (!OperatingSystem.IsWindows())
        {
            await File.WriteAllTextAsync(lowerPath, "lower-original");
        }

        var store = new CompositePreviewStore();
        var token = store.Store(
            workspaceId,
            1,
            "composite",
            [
                new CompositeFileMutation(upperPath, "upper-applied", DeleteFile: false),
                new CompositeFileMutation(lowerPath, "lower-applied", DeleteFile: false),
            ]);
        var orchestrator = new CompositeApplyOrchestrator(
            workspace, store, changeTracker, undoService: undoService);

        var result = await orchestrator.ApplyCompositeAsync(token, CancellationToken.None);

        Assert.IsTrue(result.Success, result.Error);
        var expectedFiles = OperatingSystem.IsWindows() ? 1 : 2;
        Assert.HasCount(expectedFiles, result.AppliedFiles);
        Assert.HasCount(expectedFiles, changeTracker.GetChanges(workspaceId)[^1].AffectedFiles);

        Assert.IsTrue(await undoService.RevertAsync(workspaceId, CancellationToken.None));
        Assert.AreEqual("upper-original", await File.ReadAllTextAsync(upperPath));
        Assert.AreEqual(
            OperatingSystem.IsWindows() ? "upper-original" : "lower-original",
            await File.ReadAllTextAsync(lowerPath),
            "Linux keeps an independent snapshot per case-distinct file; Windows addresses one file.");
    }

    [TestMethod]
    public async Task ApplyComposite_PartialFailure_ReportsCaseDistinctAppliedFilesByPlatformIdentity()
    {
        var upperPath = Path.Combine(_tempDir, "Feature.cs");
        var lowerPath = Path.Combine(_tempDir, "feature.cs");
        var blocker = Path.Combine(_tempDir, "blocker-case-distinct");
        await File.WriteAllTextAsync(blocker, "not a directory");
        var store = new CompositePreviewStore();
        var token = store.Store(
            "ws-1",
            1,
            "composite",
            [
                new CompositeFileMutation(upperPath, "upper", DeleteFile: false),
                new CompositeFileMutation(lowerPath, "lower", DeleteFile: false),
                new CompositeFileMutation(Path.Combine(blocker, "child.cs"), "never", DeleteFile: false),
            ]);
        var orchestrator = new CompositeApplyOrchestrator(new RecordingWorkspaceManager(), store);

        var result = await orchestrator.ApplyCompositeAsync(token, CancellationToken.None);

        Assert.IsFalse(result.Success);
        Assert.HasCount(OperatingSystem.IsWindows() ? 1 : 2, result.AppliedFiles);
    }

    [TestMethod]
    public async Task RevertBySequence_CaseDistinctFiles_UsePlatformPathIdentity()
    {
        const string workspaceId = "ws-1";
        var workspace = new RecordingWorkspaceManager();
        using var changeTracker = new ChangeTracker(workspace);
        using var undoService = new UndoService(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<UndoService>.Instance,
            workspace,
            changeTracker);
        var upperPath = Path.Combine(_tempDir, "Feature.cs");
        var lowerPath = Path.Combine(_tempDir, "feature.cs");
        await File.WriteAllTextAsync(upperPath, "original");
        await PublishTrackedFileMutationAsync(workspace, undoService, changeTracker,
            workspaceId, "apply A", upperPath, "applied-a");
        if (!OperatingSystem.IsWindows())
        {
            await File.WriteAllTextAsync(lowerPath, "lower-original");
        }

        await PublishTrackedFileMutationAsync(workspace, undoService, changeTracker,
            workspaceId, "apply B", lowerPath, "applied-b");
        var changes = changeTracker.GetChanges(workspaceId);

        var revert = await undoService.RevertBySequenceAsync(
            workspaceId, changes[0].SequenceNumber, CancellationToken.None);

        if (OperatingSystem.IsWindows())
        {
            Assert.IsFalse(revert.Reverted);
            Assert.AreEqual("dependency-blocked", revert.Reason);
            CollectionAssert.AreEqual(new[] { changes[1].SequenceNumber }, revert.BlockingSequences!.ToArray());
        }
        else
        {
            Assert.IsTrue(revert.Reverted, revert.Reason);
            Assert.IsNull(revert.Reason);
            Assert.AreEqual("original", await File.ReadAllTextAsync(upperPath));
            Assert.AreEqual("applied-b", await File.ReadAllTextAsync(lowerPath));
        }
    }

    [TestMethod]
    public async Task NativeCaseDistinctCompositePaths_KeepBothWritesAndUndoSnapshots()
    {
        if (!OperatingSystem.IsLinux()) Assert.Inconclusive("Requires a native case-sensitive filesystem.");
        var upper = Path.Combine(_tempDir, "Case.cs");
        var lower = Path.Combine(_tempDir, "case.cs");
        await File.WriteAllTextAsync(upper, "upper before");
        await File.WriteAllTextAsync(lower, "lower before");
        using var manager = new RecordingWorkspaceManager();
        using var undo = new UndoService(Microsoft.Extensions.Logging.Abstractions.NullLogger<UndoService>.Instance, manager);
        var store = new CompositePreviewStore();
        var token = store.Store("ws-1", manager.GetCurrentVersion("ws-1"), "case-distinct",
            [new CompositeFileMutation(upper, "upper after"), new CompositeFileMutation(lower, "lower after")]);
        var service = new CompositeApplyOrchestrator(manager, store, undoService: undo);
        var result = await service.ApplyCompositeAsync(token, CancellationToken.None);
        Assert.IsTrue(result.Success, result.Error);
        CollectionAssert.AreEquivalent(new[] { upper, lower }, result.AppliedFiles.ToArray());
        Assert.AreEqual("upper after", await File.ReadAllTextAsync(upper));
        Assert.AreEqual("lower after", await File.ReadAllTextAsync(lower));
        Assert.IsTrue(await undo.RevertAsync("ws-1", CancellationToken.None));
        Assert.AreEqual("upper before", await File.ReadAllTextAsync(upper));
        Assert.AreEqual("lower before", await File.ReadAllTextAsync(lower));
    }

    [TestMethod]
    public async Task CompositeCommit_ExternalBytesCannotBecomeCertifiedPostState()
    {
        var path = Path.Combine(_tempDir, "late-external.cs");
        await File.WriteAllTextAsync(path, "original");
        using var manager = new RecordingWorkspaceManager
        {
            AfterMutationCommit = ct => File.WriteAllTextAsync(path, "external", ct)
        };
        using var undo = new UndoService(Microsoft.Extensions.Logging.Abstractions.NullLogger<UndoService>.Instance, manager);
        var store = new CompositePreviewStore();
        var token = store.Store("ws-1", manager.GetCurrentVersion("ws-1"), "late external replacement",
            [new CompositeFileMutation(path, "intended")]);
        var service = new CompositeApplyOrchestrator(manager, store, undoService: undo);
        await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => service.ApplyCompositeAsync(token, CancellationToken.None));
        Assert.AreEqual("external", await File.ReadAllTextAsync(path));
        Assert.IsNotNull(manager.GetPendingRecovery("ws-1"));
        Assert.IsNotNull(store.Retrieve(token));
    }

    [TestMethod]
    public async Task CompositeWrite_ChangedLaterEntryCannotBeOverwritten()
    {
        var first = Path.Combine(_tempDir, "first.cs");
        var second = Path.Combine(_tempDir, "second.cs");
        await File.WriteAllTextAsync(first, "first original");
        await File.WriteAllTextAsync(second, "second original");
        using var manager = new RecordingWorkspaceManager();
        var store = new CompositePreviewStore();
        var token = store.Store("ws-1", manager.GetCurrentVersion("ws-1"), "changed later entry",
            [new CompositeFileMutation(first, "first intended"), new CompositeFileMutation(second, "second intended")]);
        var service = new CompositeApplyOrchestrator(manager, store, null, null, null, null,
            async (path, expected, bytes, operation, authority, ct) =>
            {
                await operation.TransitionSourceAsync(path, expected, bytes, PhysicalSourceTransitionMode.FreshReplacement, authority, null, ct);
                if (path == first) await File.WriteAllTextAsync(second, "external", ct);
            });
        await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => service.ApplyCompositeAsync(token, CancellationToken.None));
        Assert.AreEqual("first intended", await File.ReadAllTextAsync(first));
        Assert.AreEqual("external", await File.ReadAllTextAsync(second));
        Assert.IsNotNull(manager.GetPendingRecovery("ws-1"));
        Assert.IsNotNull(store.Retrieve(token));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CompositeSequentialSamePath_PublishesFinalIntentAndRestoresExactEntry(bool recreate)
    {
        var path = Path.Combine(_tempDir, "sequence.cs");
        var original = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("original")).ToArray();
        await File.WriteAllBytesAsync(path, original);
        using var manager = new RecordingWorkspaceManager();
        using var undo = new UndoService(Microsoft.Extensions.Logging.Abstractions.NullLogger<UndoService>.Instance, manager);
        var store = new CompositePreviewStore();
        var alternate = OperatingSystem.IsWindows() ? path.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : path;
        var token = store.Store("ws-1", manager.GetCurrentVersion("ws-1"), "same physical file sequence",
            [new CompositeFileMutation(path, recreate ? null : "middle", DeleteFile: recreate), new CompositeFileMutation(alternate, "final")]);
        var result = await new CompositeApplyOrchestrator(manager, store, undoService: undo).ApplyCompositeAsync(token, CancellationToken.None);
        Assert.IsTrue(result.Success, result.Error);
        CollectionAssert.AreEqual(new[] { path }, result.AppliedFiles.ToArray());
        Assert.AreEqual("final", await File.ReadAllTextAsync(path));
        Assert.AreEqual(!recreate, (await File.ReadAllBytesAsync(path)).AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));
        Assert.IsTrue(await undo.RevertAsync("ws-1", CancellationToken.None));
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(path));
    }

    private static async Task PublishTrackedFileMutationAsync(
        RecordingWorkspaceManager workspace, UndoService undoService, ChangeTracker changeTracker,
        string workspaceId, string description, string path, string content)
    {
        var before = FileSnapshotDto.FromExistingBytes(path, await File.ReadAllBytesAsync(path));
        var physical = await workspace.CaptureRecoveryAsync(workspaceId, description, [before], CancellationToken.None);
        var operation = await undoService.PrepareBeforeApplyAsync(physical, CancellationToken.None);
        using (var lease = await workspace.BeginMutationAsync(physical, CancellationToken.None))
        {
            lease.BeforeWrite(FileSnapshotDto.FromExistingBytes(path, Encoding.UTF8.GetBytes(content)));
            await File.WriteAllTextAsync(path, content);
            lease.Complete([FileSnapshotDto.FromExistingBytes(path, await File.ReadAllBytesAsync(path))]);
        }
        await operation.PublishAsync(CancellationToken.None);
        changeTracker.RecordChange(workspaceId, description, [path], "apply_text_edit");
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception), exception));
    }

    private sealed class RecordingWorkspaceManager(Exception? reloadFailure = null) : IWorkspaceManager, IPhysicalSolutionCommitter, IWorkspaceRecoveryAuthority, IDisposable
    {
        private readonly AdhocWorkspace _workspace = new();
        internal Func<CancellationToken, Task>? AfterMutationCommit { get; init; }
        public void Dispose() => _workspace.Dispose();
        private int _version = 1;
        public bool ReloadCalled { get; private set; }
        private readonly Dictionary<string, object> _sessions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, PhysicalMutationRecovery> _pending = new(StringComparer.Ordinal);
        private readonly Dictionary<PhysicalMutationRecovery, FixtureOperation> _operations = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<string, PhysicalMutationRecovery> _active = new(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _epochs = new(StringComparer.Ordinal);

        private sealed class FixtureOperation(long epoch)
        {
            public long CapturedEpoch { get; } = epoch;
            public Dictionary<string, FileSnapshotDto> WrittenPaths { get; } = new(FileSystemPath.Comparer);
            public long? CompletedEpoch { get; set; }
            public Solution? PostSolution { get; set; }
            public IReadOnlyList<FileSnapshotDto> PostFiles { get; set; } = [];
        }

        private object Session(string workspaceId)
        {
            if (!_sessions.TryGetValue(workspaceId, out var identity))
                _sessions.Add(workspaceId, identity = new object());
            return identity;
        }

        public Task AuthorizePathsAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct) =>
            new OwnedTestWorkspacePathPolicy().ValidateAsync(workspaceId, paths, ct);

        public async Task<PhysicalSourceCustody> AcquireSourceCustodyAsync(PhysicalMutationRecovery operation,
            string sourcePath, PhysicalDirectoryScope sourceParent, string leafName, string temporaryName, CancellationToken ct)
        {
            AssertOwner();
            await AuthorizePathsAsync(operation.WorkspaceId, [sourcePath, sourceParent.Path,
                Path.Combine(sourceParent.Path, temporaryName)], ct);
            AssertOwner();
            var parent = PhysicalDirectoryScope.OpenRoute(sourceParent.Path);
            return new PhysicalSourceCustody(sourceParent, parent, leafName, AssertOwner, AssertOwner,
                (paths, token) => AuthorizePathsAsync(operation.WorkspaceId, paths, token));

            void AssertOwner()
            {
                Require(operation);
                if (!ReferenceEquals(_active.GetValueOrDefault(operation.WorkspaceId), operation)
                    && !ReferenceEquals(_pending.GetValueOrDefault(operation.WorkspaceId), operation))
                    throw new WorkspaceMutationOwnershipException("The fixture custody is outside its registered recovery owner.");
            }
        }

        public void EnsureMutationAllowed(string workspaceId, IReadOnlyList<string> paths)
        {
            foreach (var path in paths) WorkspaceManager.EnsurePhysicalPath(path);
            var requested = paths.ToHashSet(FileSystemPath.Comparer);
            if (_pending.Concat(_active).Any(pair => pair.Key == workspaceId
                || pair.Value.Files.Any(file => requested.Contains(file.FilePath))))
                throw new PublicInvalidOperationException("Recover or complete the current operation before mutating its workspace or files.");
        }

        private static async Task<bool> MatchesAsync(IReadOnlyList<FileSnapshotDto> files, CancellationToken ct)
        {
            foreach (var file in files)
            {
                var current = await FileSnapshotCapture.CaptureAsync(file.FilePath, null, ct);
                if (!file.OriginalBytes.IsDefault)
                {
                    if (current.OriginalBytes.IsDefault || !current.OriginalBytes.AsSpan().SequenceEqual(file.OriginalBytes.AsSpan())) return false;
                }
                else if (current.OriginalText != file.OriginalText) return false;
            }
            return true;
        }

        private FixtureOperation Require(PhysicalMutationRecovery operation)
        {
            if (!ReferenceEquals(Session(operation.WorkspaceId), operation.SessionIdentity)
                || !_operations.TryGetValue(operation, out var state))
                throw new WorkspaceMutationOwnershipException("Unknown fixture operation.");
            return state;
        }

        public async Task<PhysicalMutationRecovery> CaptureRecoveryAsync(string workspaceId, string description,
            IReadOnlyList<FileSnapshotDto> beforeFiles, CancellationToken ct)
        {
            await AuthorizePathsAsync(workspaceId, beforeFiles.Select(file => file.FilePath).ToArray(), ct);
            EnsureMutationAllowed(workspaceId, beforeFiles.Select(file => file.FilePath).ToArray());
            if (!await MatchesAsync(beforeFiles, ct)) throw new PublicInvalidOperationException("Fixture capture bytes changed.");
            var recovery = new PhysicalMutationRecovery(workspaceId, description, GetCurrentSolution(workspaceId),
                GetCurrentVersion(workspaceId), beforeFiles.ToImmutableArray(), Session(workspaceId))
            {
                CapturedGraphFiles = [], // The fixture owns an empty Adhoc graph; physical mutation files are captured separately.
                CapturedLayout = null
            };
            _operations.Add(recovery, new FixtureOperation(_epochs.GetValueOrDefault(workspaceId)));
            return recovery;
        }

        public async Task<IWorkspaceMutationLease> BeginMutationAsync(PhysicalMutationRecovery operation, CancellationToken ct)
        {
            await AuthorizePathsAsync(operation.WorkspaceId, operation.Files.Select(file => file.FilePath).ToArray(), ct);
            var state = Require(operation);
            EnsureMutationAllowed(operation.WorkspaceId, operation.Files.Select(file => file.FilePath).ToArray());
            if (_epochs.GetValueOrDefault(operation.WorkspaceId) != (state.CompletedEpoch ?? state.CapturedEpoch)
                || !ReferenceEquals(GetCurrentSolution(operation.WorkspaceId), state.CompletedEpoch is null ? operation.PreApplySolution : state.PostSolution)
                || !await MatchesAsync(state.CompletedEpoch is null ? operation.Files : state.PostFiles, ct))
                throw new WorkspaceMutationOwnershipException("Fixture operation was superseded.");
            _active.Add(operation.WorkspaceId, operation);
            if (state.CompletedEpoch is null) operation.RecoveryFiles = [];
            return new FixtureLease(this, operation, _version,
                state.CompletedEpoch is null ? operation.Files : state.PostFiles);
        }

        private void RequireOwner(PhysicalMutationRecovery operation)
        {
            Require(operation);
            if (!ReferenceEquals(_active.GetValueOrDefault(operation.WorkspaceId), operation))
                throw new WorkspaceMutationOwnershipException("Fixture operation does not own the mutation.");
        }

        public async Task EnsurePreparedOperationCurrentAsync(PhysicalMutationRecovery operation, CancellationToken ct)
        {
            await AuthorizePathsAsync(operation.WorkspaceId, operation.Files.Select(file => file.FilePath).ToArray(), ct);
            var state = Require(operation);
            EnsureMutationAllowed(operation.WorkspaceId, operation.Files.Select(file => file.FilePath).ToArray());
            if (state.CompletedEpoch is not null
                || state.CapturedEpoch != _epochs.GetValueOrDefault(operation.WorkspaceId)
                || operation.PreApplyVersion != GetCurrentVersion(operation.WorkspaceId)
                || !ReferenceEquals(operation.PreApplySolution, GetCurrentSolution(operation.WorkspaceId))
                || !await MatchesAsync(operation.Files, ct))
                throw new WorkspaceMutationOwnershipException("Fixture preparation authority expired.");
        }

        public async Task EnsureOperationCurrentAsync(PhysicalMutationRecovery operation, CancellationToken ct)
        {
            await AuthorizePathsAsync(operation.WorkspaceId, operation.Files.Select(file => file.FilePath).ToArray(), ct);
            var state = Require(operation);
            if (_pending.ContainsKey(operation.WorkspaceId)) throw new PublicInvalidOperationException("Recover the pending fixture operation first.");
            if (_active.TryGetValue(operation.WorkspaceId, out var active))
            {
                if (!ReferenceEquals(active, operation)) throw new WorkspaceMutationOwnershipException("Another fixture operation owns the mutation.");
                return;
            }
            if (state.CompletedEpoch != _epochs.GetValueOrDefault(operation.WorkspaceId)
                || !ReferenceEquals(state.PostSolution, GetCurrentSolution(operation.WorkspaceId)))
                throw new WorkspaceMutationOwnershipException("Fixture operation was superseded.");
            if (!await MatchesAsync(state.PostFiles, ct)) throw new PublicInvalidOperationException("Fixture operation post bytes changed.");
        }

        public void RestoreCapturedVersion(PhysicalMutationRecovery operation)
        {
            RequireOwner(operation);
            _version = operation.PreApplyVersion;
        }

        public async Task CommitMutationAsync(PhysicalMutationRecovery operation, Solution? expected, CancellationToken ct)
        {
            RequireOwner(operation);
            await CommitCoreAsync(operation.WorkspaceId, expected, ct);
            if (AfterMutationCommit is { } callback) await callback(ct);
        }

        public Task<IPhysicalRestorationAuthority> StageMutationAsync(PhysicalMutationRecovery operation,
            IReadOnlyList<FileSnapshotDto> desiredFiles, CancellationToken ct) => throw new NotSupportedException("This orchestration fixture cannot issue native mutation verification.");
        public Task VerifyMutationNamespacesAsync(PhysicalMutationRecovery operation, IPhysicalRestorationAuthority restoration,
            MutationNamespaceTarget target, CancellationToken ct) => throw new NotSupportedException("This orchestration fixture cannot verify native namespaces.");

        public async Task<IPhysicalRestorationAuthority> StageRestorationAsync(PhysicalMutationRecovery operation,
            IReadOnlyList<FileSnapshotDto> desiredFiles, PhysicalGraphLayout? priorLayout, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Require(operation);
            if (!ReferenceEquals(_pending.GetValueOrDefault(operation.WorkspaceId), operation))
                await EnsureOperationCurrentAsync(operation, ct);
            Assert.IsTrue(desiredFiles.All(file => operation.Files.Any(original =>
                FileSystemPath.Comparer.Equals(original.FilePath, file.FilePath))), "Restoration must remain within captured ownership.");
            // This orchestration fixture has an empty Adhoc graph. Real staged MSBuild graph
            // preservation is exercised by the workspace integration fixtures.
            return new BorrowedAuthority(GetCurrentSolution(operation.WorkspaceId));
        }

        public async Task CommitRestorationAsync(PhysicalMutationRecovery operation, PhysicalGraphExpectation expected,
            bool retainedRecovery, CancellationToken ct)
        {
            if (retainedRecovery) await EnsureRecoveryCurrentAsync(operation, ct);
            else RequireOwner(operation);
            await CommitCoreAsync(operation.WorkspaceId, expected.Solution, ct);
            if (retainedRecovery)
            {
                _epochs[operation.WorkspaceId] = _epochs.GetValueOrDefault(operation.WorkspaceId) + 1;
                _pending.Remove(operation.WorkspaceId);
            }
        }

        private sealed class BorrowedAuthority(Solution solution) : IPhysicalRestorationAuthority
        {
            private bool _disposed;
            public PhysicalGraphExpectation Expected => !_disposed
                ? new(solution, static path => path, new HashSet<string>(StringComparer.Ordinal),
                    EvaluatedExecutionIdentity.Capture(solution, static path => path, new HashSet<string>(StringComparer.Ordinal), CancellationToken.None), [], InstalledPackageAuthority.Merge([]), [])
                : throw new ObjectDisposedException(nameof(BorrowedAuthority));
            public bool PreservesOriginalIdentity => !_disposed
                ? true : throw new ObjectDisposedException(nameof(BorrowedAuthority));
            public Task AssertSourceBeforeEffectAsync(PhysicalMutationRecovery operation, IReadOnlyList<string> sourceCoordinates, CancellationToken ct)
                => throw new NotSupportedException("This borrowed semantic fixture cannot issue a native source proof.");
            public void Dispose() => _disposed = true;
        }

        public PhysicalMutationRecovery? GetPendingRecovery(string workspaceId) => _pending.GetValueOrDefault(workspaceId);

        public void RetainRecovery(PhysicalMutationRecovery recovery)
        {
            var state = Require(recovery);
            if (ReferenceEquals(_pending.GetValueOrDefault(recovery.WorkspaceId), recovery)) return;
            if (!ReferenceEquals(_active.GetValueOrDefault(recovery.WorkspaceId), recovery)
                && (state.CompletedEpoch != _epochs.GetValueOrDefault(recovery.WorkspaceId)
                    || !ReferenceEquals(state.PostSolution, GetCurrentSolution(recovery.WorkspaceId))))
                throw new WorkspaceMutationOwnershipException("A superseded fixture operation cannot retain recovery.");
            if (_pending.TryGetValue(recovery.WorkspaceId, out var pending) && !ReferenceEquals(pending, recovery))
                throw new WorkspaceMutationOwnershipException("A different fixture recovery already owns the workspace.");
            _pending[recovery.WorkspaceId] = recovery;
        }

        public void AssertKnownFileRead(PhysicalMutationRecovery operation, FileSnapshotDto observed)
        {
            var state = Require(operation);
            if (!ReferenceEquals(_pending.GetValueOrDefault(operation.WorkspaceId), operation))
            {
                if (!ReferenceEquals(_active.GetValueOrDefault(operation.WorkspaceId), operation)
                    && (state.CompletedEpoch != _epochs.GetValueOrDefault(operation.WorkspaceId)
                        || !ReferenceEquals(state.PostSolution, GetCurrentSolution(operation.WorkspaceId))))
                    throw new WorkspaceMutationOwnershipException("The fixture read operation was superseded.");
            }
            var entry = operation.Files.Single(file => FileSystemPath.Comparer.Equals(file.FilePath, observed.FilePath));
            static bool Same(FileSnapshotDto known, FileSnapshotDto actual) => known.OriginalText is null ? actual.OriginalText is null
                : actual.OriginalText is not null && !known.OriginalBytes.IsDefault && !actual.OriginalBytes.IsDefault
                    && known.OriginalBytes.AsSpan().SequenceEqual(actual.OriginalBytes.AsSpan());
            if (Same(entry, observed)
                || state.WrittenPaths.TryGetValue(observed.FilePath, out var intended) && Same(intended, observed)
                || operation.CompletedSourceTransition(observed.FilePath) is { } transitioned && Same(transitioned, observed)
                || state.PostFiles.Any(file => FileSystemPath.Comparer.Equals(file.FilePath, observed.FilePath) && Same(file, observed))) return;
            _pending[operation.WorkspaceId] = operation;
            throw new WorkspaceMutationOwnershipException("The fixture physical read has conflicting bytes.");
        }

        public async Task EnsureRecoveryCurrentAsync(PhysicalMutationRecovery recovery, CancellationToken ct)
        {
            await AuthorizePathsAsync(recovery.WorkspaceId, recovery.Files.Select(file => file.FilePath).ToArray(), ct);
            Require(recovery);
            if (!ReferenceEquals(_pending.GetValueOrDefault(recovery.WorkspaceId), recovery))
                throw new WorkspaceMutationOwnershipException("Fixture recovery is no longer current.");
        }

        private sealed class FixtureLease(RecordingWorkspaceManager manager, PhysicalMutationRecovery operation,
            int entryVersion, IReadOnlyList<FileSnapshotDto> entryFiles) : IWorkspaceMutationLease
        {
            private bool _finished;
            public void BeforeWrite(FileSnapshotDto intendedPost)
            {
                var path = intendedPost.FilePath;
                manager.RequireOwner(operation);
                if (!operation.Files.Any(file => FileSystemPath.Comparer.Equals(file.FilePath, path)))
                    throw new WorkspaceMutationOwnershipException("Fixture write is outside the captured operation.");
                manager.Require(operation).WrittenPaths[path] = intendedPost;
                if (!operation.RecoveryFiles.Any(file => FileSystemPath.Comparer.Equals(file.FilePath, path)))
                    operation.RecoveryFiles = operation.RecoveryFiles.Add(operation.Files.Single(file =>
                        FileSystemPath.Comparer.Equals(file.FilePath, path)));
            }
            public void Complete(IReadOnlyList<FileSnapshotDto> files)
            {
                manager.RequireOwner(operation);
                Assert.IsTrue(files.Select(file => file.FilePath).ToHashSet(FileSystemPath.Comparer)
                    .SetEquals(operation.Files.Select(file => file.FilePath)), "Fixture completion must cover exact ownership.");
                var state = manager.Require(operation);
                var epoch = manager._epochs.GetValueOrDefault(operation.WorkspaceId) + 1;
                manager._epochs[operation.WorkspaceId] = epoch;
                state.CompletedEpoch = epoch;
                state.PostSolution = manager.GetCurrentSolution(operation.WorkspaceId);
                var before = operation.Files.ToDictionary(file => file.FilePath, FileSystemPath.Comparer);
                state.PostFiles = files.Where(file =>
                {
                    var original = before[file.FilePath];
                    return original.OriginalText is null ? file.OriginalText is not null
                        : file.OriginalText is null || original.OriginalBytes.IsDefault || file.OriginalBytes.IsDefault
                            || !original.OriginalBytes.AsSpan().SequenceEqual(file.OriginalBytes.AsSpan());
                }).ToArray();
                operation.RecoveryFiles = state.PostFiles.Select(file => before[file.FilePath]).ToImmutableArray();
                manager._active.Remove(operation.WorkspaceId);
                _finished = true;
            }
            public void CancelBeforeSourceTransition(IPhysicalRestorationAuthority verification) => throw new NotSupportedException("The orchestration fixture has no verified native unused authority.");
            public async Task AbortAsync(CancellationToken ct)
            {
                await manager.AuthorizePathsAsync(operation.WorkspaceId, entryFiles.Select(file => file.FilePath).ToArray(), ct);
                manager.RequireOwner(operation);
                Assert.AreEqual(entryVersion, manager._version, "Fixture abort must restore the entry version.");
                Assert.IsTrue(await MatchesAsync(entryFiles, ct), "Fixture abort must restore the entry bytes.");
                manager._active.Remove(operation.WorkspaceId);
                _finished = true;
            }
            public void Dispose()
            {
                if (_finished) return;
                manager.RetainRecovery(operation);
                manager._active.Remove(operation.WorkspaceId);
                _finished = true;
            }
        }

        public async Task CommitPhysicalSolutionAsync(string workspaceId, Solution? expected, CancellationToken ct)
        {
            EnsureMutationAllowed(workspaceId, []);
            await CommitCoreAsync(workspaceId, expected, ct);
            _epochs[workspaceId] = _epochs.GetValueOrDefault(workspaceId) + 1;
        }

        private Task CommitCoreAsync(string workspaceId, Solution? expected, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ReloadCalled = true;
            if (reloadFailure is not null) return Task.FromException(reloadFailure);
            if (expected is not null && !_workspace.TryApplyChanges(expected))
                throw new InvalidOperationException("Controlled workspace rejected expected solution.");
            _version++;
            return Task.CompletedTask;
        }

        public event Action<string>? WorkspaceClosed { add { } remove { } }
        public event Action<string>? WorkspaceReloaded { add { } remove { } }

        public Task<WorkspaceStatusDto> LoadAsync(string path, EvictPolicy evictPolicy, CancellationToken ct) => throw new NotSupportedException();

        public async Task<WorkspaceStatusDto> ReloadAsync(string workspaceId, CancellationToken ct)
        {
            EnsureMutationAllowed(workspaceId, []);
            await CommitCoreAsync(workspaceId, expected: null, ct);
            _epochs[workspaceId] = _epochs.GetValueOrDefault(workspaceId) + 1;
            return GetStatus(workspaceId);
        }

        public bool ContainsWorkspace(string workspaceId) => !string.IsNullOrWhiteSpace(workspaceId);
        public bool IsStale(string workspaceId) => false;
        public bool Close(string workspaceId) => throw new NotSupportedException();
        public IReadOnlyList<WorkspaceStatusDto> ListWorkspaces() => [];
        public WorkspaceStatusDto GetStatus(string workspaceId) =>
            new(
                WorkspaceId: workspaceId,
                LoadedPath: "C:\\repo\\Sample.slnx",
                WorkspaceVersion: _version,
                SnapshotToken: "snapshot",
                LoadedAtUtc: DateTimeOffset.UtcNow,
                ProjectCount: 0,
                DocumentCount: 0,
                Projects: [],
                IsLoaded: true,
                IsStale: false,
                WorkspaceDiagnostics: []);
        public Task<WorkspaceStatusDto> GetStatusAsync(string workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(GetStatus(workspaceId));
        public ProjectGraphDto GetProjectGraph(string workspaceId) => throw new NotSupportedException();
        public Task<IReadOnlyList<GeneratedDocumentDto>> GetSourceGeneratedDocumentsAsync(string workspaceId, string? projectName, CancellationToken ct) => throw new NotSupportedException();
        public Task<string?> GetSourceTextAsync(string workspaceId, string filePath, CancellationToken ct) => throw new NotSupportedException();
        public int GetCurrentVersion(string workspaceId) => _version;
        public void RestoreVersion(string workspaceId, int version)
        {
            EnsureMutationAllowed(workspaceId, []);
            _version = version;
            _epochs[workspaceId] = _epochs.GetValueOrDefault(workspaceId) + 1;
        }
        public Task<System.Collections.Immutable.ImmutableArray<RoslynMcp.Roslyn.Contracts.AdmittedModuleImage>> ReadAdmittedImagesAsync(
            string workspaceId, Microsoft.CodeAnalysis.ProjectId projectId, Microsoft.CodeAnalysis.PortableExecutableReference reference, CancellationToken ct) =>
            throw new NotSupportedException();

        public Solution GetCurrentSolution(string workspaceId) => _workspace.CurrentSolution;

        public Project? GetProject(string workspaceId, string projectNameOrPath) => null;
    }
}
