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

    [TestMethod]
    public async Task AtomicFileWriter_RoundTrips_Content_And_Leaves_No_Temp_Artifact()
    {
        var path = Path.Combine(_tempDir, "target.cs");
        const string content = "// hello atomic world\nclass C {}\n";

        await AtomicFileWriter.WriteAllTextAsync(path, content, CancellationToken.None);

        Assert.AreEqual(content, await File.ReadAllTextAsync(path));
        Assert.IsFalse(File.Exists(path + ".tmp"), "The .tmp sibling must not survive a successful write.");
    }

    [TestMethod]
    public async Task AtomicFileWriter_Overwrites_Existing_File_Atomically()
    {
        var path = Path.Combine(_tempDir, "target.cs");
        await File.WriteAllTextAsync(path, "original");

        await AtomicFileWriter.WriteAllTextAsync(path, "replacement", CancellationToken.None);

        Assert.AreEqual("replacement", await File.ReadAllTextAsync(path));
        Assert.IsFalse(File.Exists(path + ".tmp"));
    }

    [TestMethod]
    public async Task AtomicFileWriter_Logs_Warning_When_Temp_Cleanup_Fails_After_Write_Failure()
    {
        // Windows-only: an exclusive FileShare.None lock on the pre-created .tmp forces BOTH the
        // initial write (File.WriteAllTextAsync to the locked .tmp) AND the subsequent cleanup
        // delete to fail — so we exercise the swallowed-cleanup catch with a genuine failure, not a
        // no-op. On non-Windows, a held handle does not block delete the same way, so we skip.
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Deterministic temp-lock only reproduces on Windows (FileShare.None semantics).");
            return;
        }

        var path = Path.Combine(_tempDir, "target.cs");
        var tmp = path + ".tmp";

        // Pre-create and hold the .tmp with an exclusive lock so the write to it throws IOException
        // and the cleanup delete also throws (still locked) — the path the fix must observe.
        using var hold = new FileStream(tmp, FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        var logger = new RecordingLogger<CompositeApplyOrchestrator>();
        var sink = new CapturingServerObservabilitySink();
        var reporter = new ServerObservabilityReporter(sink);
        using var correlationScope = RequestCorrelationContext.Begin();
        var correlationId = RequestCorrelationContext.Current
            ?? throw new AssertFailedException("The request correlation scope must publish an id.");

        // (a) The original write failure must still propagate — primary failure is not masked.
        await Assert.ThrowsExactlyAsync<IOException>(
            () => AtomicFileWriter.WriteAllTextAsync(
                path,
                "// content",
                CancellationToken.None,
                logger,
                exceptionReporter: reporter));

        // (b) Exactly one Warning is recorded, and it is redacted: no raw exception attached, no
        // absolute temp/target paths, no caught-exception message text — only the stable cleanup
        // category, the target file name, and the shared secret-safe projection
        // (atomic-file-cleanup-error-detail-redaction).
        var warnings = logger.Entries.Where(e => e.Level == LogLevel.Warning).ToList();
        Assert.AreEqual(1, warnings.Count, "Exactly one Warning should be logged for the failed temp cleanup.");
        Assert.IsNull(warnings[0].Exception, "The caught cleanup exception must not be attached to the log record.");
        Assert.IsFalse(warnings[0].Message.Contains(_tempDir, StringComparison.OrdinalIgnoreCase), "The warning must not contain the absolute directory path.");
        Assert.IsFalse(warnings[0].Message.Contains(tmp, StringComparison.OrdinalIgnoreCase), "The warning must not contain the absolute .tmp path.");
        StringAssert.Contains(warnings[0].Message, "CompositeApplyTempCleanup", "The warning must carry the stable cleanup category token.");
        StringAssert.Contains(warnings[0].Message, Path.GetFileName(path), "The warning must name the target file (file name only).");
        StringAssert.Contains(warnings[0].Message, $"correlationId={correlationId}", "The warning must carry the active request correlation id.");
        StringAssert.Contains(warnings[0].Message, nameof(IOException), "The warning must carry the exception-type topology from the shared projection.");
        Assert.HasCount(1, sink.Events);
        Assert.AreEqual(correlationId, sink.Events.Single().Exception.CorrelationId);

        // (c) The .tmp still exists — cleanup genuinely failed (it is still locked open), not a no-op.
        Assert.IsTrue(File.Exists(tmp), "The locked .tmp must remain on disk because its cleanup delete failed.");
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
        Assert.IsFalse(File.Exists(goodPath + ".tmp"), "No .tmp artifact should remain from the successful write.");
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

    private sealed class RecordingWorkspaceManager : IWorkspaceManager
    {
        public bool ReloadCalled { get; private set; }

        public event Action<string>? WorkspaceClosed { add { } remove { } }
        public event Action<string>? WorkspaceReloaded { add { } remove { } }

        public Task<WorkspaceStatusDto> LoadAsync(string path, EvictPolicy evictPolicy, CancellationToken ct) => throw new NotSupportedException();

        public Task<WorkspaceStatusDto> ReloadAsync(string workspaceId, CancellationToken ct)
        {
            ReloadCalled = true;
            return Task.FromResult(GetStatus(workspaceId));
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
        public int GetCurrentVersion(string workspaceId) => throw new NotSupportedException();
        public void RestoreVersion(string workspaceId, int version) => throw new NotSupportedException();
        public Solution GetCurrentSolution(string workspaceId) => throw new NotSupportedException();
        public bool TryApplyChanges(string workspaceId, Solution newSolution) => throw new NotSupportedException();
        public Project? GetProject(string workspaceId, string projectNameOrPath) => null;
    }
}
