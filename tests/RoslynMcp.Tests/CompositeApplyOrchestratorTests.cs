using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Diagnostics;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.TestInfrastructure;

namespace RoslynMcp.Tests;

/// <summary>
/// Unit coverage for <see cref="CompositeApplyOrchestrator"/> and the shared
/// <c>AtomicFileWriter</c> primitive it (and the other source-mutation services) now route
/// disk writes through. Focused on the atomic-write round-trip and the mid-loop partial-apply
/// failure contract — see plan initiative refactor-services-non-atomic-write-rollback.
/// These tests operate purely on a temp directory + hand-rolled fakes, so they need no loaded
/// MSBuild workspace and are safe to run in parallel with the workspace-bound suites.
/// </summary>
[TestClass]
public sealed class CompositeApplyOrchestratorTests
{
    private string _tempDir = string.Empty;

    [TestInitialize]
    public void Init()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "roslynmcp-atomicwrite-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        TestFixtureFileSystem.DeleteDirectoryIfExists(_tempDir);
    }

    private void AssertNoTempArtifacts(string path) =>
        Assert.IsEmpty(Directory.EnumerateFiles(_tempDir, "*.tmp"));

    [TestMethod]
    public async Task AtomicFileWriter_RoundTrips_Content_And_Leaves_No_Temp_Artifact()
    {
        var path = Path.Combine(_tempDir, "target.cs");
        const string content = "// hello atomic world\nclass C {}\n";

        await AtomicFileWriter.WriteAllTextAsync(path, content, CancellationToken.None);

        Assert.AreEqual(content, await File.ReadAllTextAsync(path));
        AssertNoTempArtifacts(path);
    }

    [TestMethod]
    public async Task AtomicFileWriter_Overwrites_Existing_File_Atomically()
    {
        var path = Path.Combine(_tempDir, "target.cs");
        await File.WriteAllTextAsync(path, "original");

        await AtomicFileWriter.WriteAllTextAsync(path, "replacement", CancellationToken.None);

        Assert.AreEqual("replacement", await File.ReadAllTextAsync(path));
        AssertNoTempArtifacts(path);
    }

    [TestMethod]
    public async Task AtomicFileWriter_OverlappingWritesKeepSeparateTempFiles()
    {
        var path = Path.Combine(_tempDir, "target.cs");
        var firstPrepared = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = AtomicFileWriter.WriteAtomicAsync(path, async (_, stream) =>
        {
            await stream.WriteAsync("first"u8.ToArray());
            firstPrepared.SetResult();
            await releaseFirst.Task;
        }, CancellationToken.None, logger: null, exceptionReporter: null);

        await firstPrepared.Task;
        try
        {
            await AtomicFileWriter.WriteAtomicAsync(path,
                (_, stream) => stream.WriteAsync("second"u8.ToArray()).AsTask(),
                CancellationToken.None, logger: null, exceptionReporter: null);
        }
        finally
        {
            releaseFirst.SetResult();
        }

        await first;
        Assert.AreEqual("first", await File.ReadAllTextAsync(path));
        AssertNoTempArtifacts(path);
    }

    [TestMethod]
    public async Task AtomicFileWriter_UsesShortSameDirectoryTempForLongTargetName()
    {
        var path = Path.Combine(_tempDir, new string('x', 120) + ".cs");
        string? tempPath = null;
        await AtomicFileWriter.WriteAtomicAsync(path, async (tmp, stream) =>
        {
            tempPath = tmp;
            await stream.WriteAsync("content"u8.ToArray());
        }, CancellationToken.None, logger: null, exceptionReporter: null);

        Assert.IsNotNull(tempPath);
        Assert.AreEqual(Path.GetDirectoryName(path), Path.GetDirectoryName(tempPath));
        Assert.IsTrue(Path.GetFileName(tempPath).Length < 30);
        Assert.AreEqual("content", await File.ReadAllTextAsync(path));
        AssertNoTempArtifacts(path);
    }

    [TestMethod]
    public async Task AtomicFileWriter_CancelAfterTempWrite_DoesNotReplaceTarget()
    {
        var path = Path.Combine(_tempDir, "target.cs");
        await File.WriteAllTextAsync(path, "original");
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            AtomicFileWriter.WriteAtomicAsync(path, async (_, stream) =>
            {
                await stream.WriteAsync("replacement"u8.ToArray());
                cancellation.Cancel();
            }, cancellation.Token, logger: null, exceptionReporter: null));

        Assert.AreEqual("original", await File.ReadAllTextAsync(path));
        AssertNoTempArtifacts(path);
    }

    [TestMethod]
    public async Task AtomicFileWriter_ReplacedTempNameNeverCommitsForeignBytes()
    {
        var path = Path.Combine(_tempDir, "target.cs");
        await File.WriteAllTextAsync(path, "original");
        string? tempPath = null;

        await Assert.ThrowsExactlyAsync<IOException>(() =>
            AtomicFileWriter.WriteAtomicAsync(path, async (tmp, stream) =>
            {
                tempPath = tmp;
                await stream.WriteAsync("intended"u8.ToArray());
                File.Move(tmp, tmp + ".held");
                await File.WriteAllTextAsync(tmp, "foreign");
            }, CancellationToken.None, logger: null, exceptionReporter: null));

        Assert.AreEqual("original", await File.ReadAllTextAsync(path));
        Assert.IsNotNull(tempPath);
        Assert.AreEqual("foreign", await File.ReadAllTextAsync(tempPath));
    }

    [TestMethod]
    public async Task AtomicFileWriter_SymlinkSwapCannotRedirectWriteOrCommit()
    {
        if (!OperatingSystem.IsLinux()) return;
        var path = Path.Combine(_tempDir, "target.cs");
        var foreignPath = Path.Combine(_tempDir, "foreign.cs");
        await File.WriteAllTextAsync(path, "original");
        await File.WriteAllTextAsync(foreignPath, "foreign");
        string? tempPath = null;

        await Assert.ThrowsExactlyAsync<IOException>(() =>
            AtomicFileWriter.WriteAtomicAsync(path, async (tmp, stream) =>
            {
                tempPath = tmp;
                File.Move(tmp, tmp + ".held");
                File.CreateSymbolicLink(tmp, foreignPath);
                await stream.WriteAsync("intended"u8.ToArray());
            }, CancellationToken.None, logger: null, exceptionReporter: null));

        Assert.AreEqual("original", await File.ReadAllTextAsync(path));
        Assert.AreEqual("foreign", await File.ReadAllTextAsync(foreignPath));
        Assert.IsNotNull(tempPath);
        Assert.IsTrue(new FileInfo(tempPath).LinkTarget is not null,
            "Foreign symlink must remain untouched by ownership-aware cleanup.");
    }

    [TestMethod]
    public async Task AtomicFileWriter_DanglingSymlinkSwapIsReportedAndPreserved()
    {
        if (!OperatingSystem.IsLinux()) return;
        var path = Path.Combine(_tempDir, "target.cs");
        var logger = new RecordingLogger<CompositeApplyOrchestrator>();
        string? tempPath = null;

        await Assert.ThrowsExactlyAsync<IOException>(() =>
            AtomicFileWriter.WriteAtomicAsync(path, (tmp, _) =>
            {
                tempPath = tmp;
                File.Move(tmp, tmp + ".held");
                File.CreateSymbolicLink(tmp, Path.Combine(_tempDir, "missing"));
                throw new IOException("Injected failure.");
            }, CancellationToken.None, logger, exceptionReporter: null));

        Assert.IsNotNull(tempPath);
        Assert.IsFalse(File.Exists(Path.Combine(_tempDir, "missing")));
        Assert.IsNotNull(new FileInfo(tempPath).LinkTarget);
        Assert.HasCount(1, logger.Entries.Where(e => e.Level == LogLevel.Warning));
    }

    [TestMethod]
    public async Task AtomicFileWriter_SymlinkedParentCanBeWritten()
    {
        if (!OperatingSystem.IsLinux()) return;
        var physical = Path.Combine(_tempDir, "physical");
        Directory.CreateDirectory(physical);
        var alias = Path.Combine(_tempDir, "alias");
        Directory.CreateSymbolicLink(alias, physical);
        var path = Path.Combine(alias, "target.cs");

        await AtomicFileWriter.WriteAllTextAsync(path, "content", CancellationToken.None);

        Assert.AreEqual("content", await File.ReadAllTextAsync(Path.Combine(physical, "target.cs")));
    }

    [TestMethod]
    public void AtomicFileWriter_NormalizesWindowsUncHandlePath()
    {
        Assert.AreEqual(@"\\server\share\file.tmp",
            AtomicFileWriter.NormalizeWindowsHandlePath(@"\\?\UNC\server\share\file.tmp"));
        Assert.AreEqual(@"C:\folder\file.tmp",
            AtomicFileWriter.NormalizeWindowsHandlePath(@"\\?\C:\folder\file.tmp"));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AtomicFileWriter_ReplaceWaitsForWindowsReader(bool shareDelete)
    {
        if (!OperatingSystem.IsWindows()) return;

        var path = Path.Combine(_tempDir, "target.editorconfig");
        await File.WriteAllTextAsync(path, "original");
        Task replace;
        using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read,
            shareDelete ? FileShare.ReadWrite | FileShare.Delete : FileShare.ReadWrite))
        {
            replace = AtomicFileWriter.WriteAllTextAsync(path, "replacement", CancellationToken.None);
            var first = await Task.WhenAny(replace, Task.Delay(TimeSpan.FromMilliseconds(500)));
            Assert.AreNotSame(replace, first);
        }

        await replace;
        Assert.AreEqual("replacement", await File.ReadAllTextAsync(path));
    }

    [TestMethod]
    public async Task AtomicFileWriter_PersistentReaderFailsWithinBound_AndCleansTemp()
    {
        if (!OperatingSystem.IsWindows()) return;

        var path = Path.Combine(_tempDir, "target.editorconfig");
        await File.WriteAllTextAsync(path, "original");
        using var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var started = Stopwatch.StartNew();
        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() =>
            AtomicFileWriter.WriteAllTextAsync(path, "replacement", CancellationToken.None));
        Assert.IsTrue(started.Elapsed < TimeSpan.FromSeconds(5), "A persistent lock must fail within the retry bound.");
        Assert.AreEqual("original", await File.ReadAllTextAsync(path));
        AssertNoTempArtifacts(path);
    }

    [TestMethod]
    public async Task AtomicFileWriter_CancellationStopsSharingWait_AndCleansTemp()
    {
        if (!OperatingSystem.IsWindows()) return;

        var path = Path.Combine(_tempDir, "target.editorconfig");
        await File.WriteAllTextAsync(path, "original");
        using var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            AtomicFileWriter.WriteAllTextAsync(path, "replacement", cancellation.Token));
        Assert.AreEqual("original", await File.ReadAllTextAsync(path));
        AssertNoTempArtifacts(path);
    }

    [TestMethod]
    public async Task AtomicFileWriter_LogsWarningAndPreservesForeignTempAfterWriteFailure()
    {
        var path = Path.Combine(_tempDir, "target.cs");
        string? tmp = null;

        var logger = new RecordingLogger<CompositeApplyOrchestrator>();
        var sink = new CapturingServerObservabilitySink();
        var reporter = new ServerObservabilityReporter(sink);
        using var correlationScope = RequestCorrelationContext.Begin();
        var correlationId = RequestCorrelationContext.Current
            ?? throw new AssertFailedException("The request correlation scope must publish an id.");

        // (a) The original write failure must still propagate — primary failure is not masked.
        await Assert.ThrowsExactlyAsync<IOException>(
            () => AtomicFileWriter.WriteAtomicAsync(path, async (candidate, stream) =>
            {
                tmp = candidate;
                await stream.WriteAsync("// content"u8.ToArray());
                File.Move(candidate, candidate + ".held");
                await File.WriteAllTextAsync(candidate, "foreign");
                throw new IOException("Injected write failure after temp replacement.");
            }, CancellationToken.None, logger, reporter));

        // (b) Exactly one Warning is recorded, and it is redacted: no raw exception attached, no
        // absolute temp/target paths, no caught-exception message text — only the stable cleanup
        // category, the target file name, and the shared secret-safe projection
        // (atomic-file-cleanup-error-detail-redaction).
        var warnings = logger.Entries.Where(e => e.Level == LogLevel.Warning).ToList();
        Assert.AreEqual(1, warnings.Count, "Exactly one Warning should be logged for the failed temp cleanup.");
        Assert.IsNull(warnings[0].Exception, "The caught cleanup exception must not be attached to the log record.");
        Assert.IsFalse(warnings[0].Message.Contains(_tempDir, StringComparison.OrdinalIgnoreCase), "The warning must not contain the absolute directory path.");
        Assert.IsNotNull(tmp);
        Assert.IsFalse(warnings[0].Message.Contains(tmp, StringComparison.OrdinalIgnoreCase), "The warning must not contain the absolute .tmp path.");
        StringAssert.Contains(warnings[0].Message, "CompositeApplyTempCleanup", "The warning must carry the stable cleanup category token.");
        StringAssert.Contains(warnings[0].Message, Path.GetFileName(path), "The warning must name the target file (file name only).");
        StringAssert.Contains(warnings[0].Message, $"correlationId={correlationId}", "The warning must carry the active request correlation id.");
        StringAssert.Contains(warnings[0].Message, nameof(IOException), "The warning must carry the exception-type topology from the shared projection.");
        Assert.HasCount(1, sink.Events);
        Assert.AreEqual(correlationId, sink.Events.Single().Exception.CorrelationId);

        // The name now belongs to another writer; cleanup must not delete it.
        Assert.AreEqual("foreign", await File.ReadAllTextAsync(tmp));
    }

    [TestMethod]
    public async Task AtomicFileWriter_LogsWarningWhenOwnedTempDeletionFails()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(_tempDir, "target.cs");
        string? tmp = null;
        var logger = new RecordingLogger<CompositeApplyOrchestrator>();
        try
        {
            await Assert.ThrowsExactlyAsync<IOException>(() =>
                AtomicFileWriter.WriteAtomicAsync(path, (candidate, _) =>
                {
                    tmp = candidate;
                    File.SetAttributes(candidate, FileAttributes.ReadOnly);
                    throw new IOException("Injected write failure.");
                }, CancellationToken.None, logger, exceptionReporter: null));

            Assert.IsNotNull(tmp);
            Assert.IsTrue(File.Exists(tmp), "Read-only owned temp must survive failed cleanup.");
            Assert.HasCount(1, logger.Entries.Where(e => e.Level == LogLevel.Warning));
        }
        finally
        {
            if (tmp is not null && File.Exists(tmp))
            {
                File.SetAttributes(tmp, FileAttributes.Normal);
                File.Delete(tmp);
            }
        }
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
        var workspace = new RecordingWorkspaceManager(reloadFailure);
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

        var workspace = new RecordingWorkspaceManager();
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
        AssertNoTempArtifacts(goodPath);
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
        var orchestrator = new CompositeApplyOrchestrator(
            new RecordingWorkspaceManager(),
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
    /// <c>AtomicFileWriter.WriteAllTextAsync</c> with no <see cref="Encoding"/>, so a UTF-8-BOM or
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
        var orchestrator = new CompositeApplyOrchestrator(new RecordingWorkspaceManager(), store);

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
        var orchestrator = new CompositeApplyOrchestrator(new RecordingWorkspaceManager(), store);

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
        var workspace = new RecordingWorkspaceManager();
        using var changeTracker = new ChangeTracker(workspace);
        using var undoService = new UndoService(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<UndoService>.Instance,
            workspace,
            changeTracker);

        // A previous, independently revertable apply on an unrelated file.
        var priorPath = Path.Combine(_tempDir, "prior.cs");
        await File.WriteAllTextAsync(priorPath, "prior-original");
        undoService.CaptureBeforeApply(
            workspaceId,
            "prior apply",
            preApplySolution: null,
            [FileSnapshotDto.FromExistingBytes(priorPath, await File.ReadAllBytesAsync(priorPath))]);
        await File.WriteAllTextAsync(priorPath, "prior-applied");
        changeTracker.RecordChange(workspaceId, "prior apply", [priorPath], "apply_text_edit");

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

    private sealed class RecordingWorkspaceManager(Exception? reloadFailure = null) : IWorkspaceManager
    {
        public bool ReloadCalled { get; private set; }

        public event Action<string>? WorkspaceClosed { add { } remove { } }
        public event Action<string>? WorkspaceReloaded { add { } remove { } }

        public Task<WorkspaceStatusDto> LoadAsync(string path, EvictPolicy evictPolicy, CancellationToken ct) => throw new NotSupportedException();

        public Task<WorkspaceStatusDto> ReloadAsync(string workspaceId, CancellationToken ct)
        {
            ReloadCalled = true;
            return reloadFailure is null
                ? Task.FromResult(GetStatus(workspaceId))
                : Task.FromException<WorkspaceStatusDto>(reloadFailure);
        }

        public bool ContainsWorkspace(string workspaceId) => !string.IsNullOrWhiteSpace(workspaceId);
        public bool IsStale(string workspaceId) => false;
        public bool Close(string workspaceId) => throw new NotSupportedException();
        public IReadOnlyList<WorkspaceStatusDto> ListWorkspaces() => [];
        public WorkspaceStatusDto GetStatus(string workspaceId) =>
            new(
                WorkspaceId: workspaceId,
                LoadedPath: "C:\\repo\\Sample.slnx",
                WorkspaceVersion: 1,
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
        public int GetCurrentVersion(string workspaceId) => 1;
        public void RestoreVersion(string workspaceId, int version) { }
        public Solution GetCurrentSolution(string workspaceId) => throw new NotSupportedException();
        public bool TryApplyChanges(string workspaceId, Solution newSolution) => throw new NotSupportedException();
        public Project? GetProject(string workspaceId, string projectNameOrPath) => null;
    }
}
