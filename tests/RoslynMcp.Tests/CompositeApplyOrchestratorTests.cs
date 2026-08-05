using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

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
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort temp cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort temp cleanup.
        }
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

        // (a) The original write failure must still propagate — primary failure is not masked.
        await Assert.ThrowsExactlyAsync<IOException>(
            () => AtomicFileWriter.WriteAllTextAsync(path, "// content", CancellationToken.None, logger));

        // (b) Exactly one Warning naming the .tmp path is recorded.
        var warnings = logger.Entries.Where(e => e.Level == LogLevel.Warning).ToList();
        Assert.AreEqual(1, warnings.Count, "Exactly one Warning should be logged for the failed temp cleanup.");
        StringAssert.Contains(warnings[0].Message, tmp, "The warning must name the orphaned .tmp path.");

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
        var blocker = Path.Combine(_tempDir, "blocker");
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
        var orchestrator = new CompositeApplyOrchestrator(workspace, store, changeTracker: null, logger: logger);

        // Act
        var result = await orchestrator.ApplyCompositeAsync(token, CancellationToken.None);

        // Assert: failed, but the first write landed and is marked as a partial apply.
        Assert.IsFalse(result.Success, "A mid-loop failure must report success:false.");
        CollectionAssert.AreEqual(new[] { goodPath }, result.AppliedFiles.ToArray(), "Only the pre-failure write should be reported as applied.");
        Assert.IsNotNull(result.Error);
        StringAssert.StartsWith(result.Error, "Partial composite apply: ", "The message must carry the partial-apply marker.");
        Assert.AreEqual("// good content", await File.ReadAllTextAsync(goodPath), "The successful write must remain on disk.");
        Assert.IsFalse(File.Exists(goodPath + ".tmp"), "No .tmp artifact should remain from the successful write.");
        Assert.IsFalse(File.Exists(doomedPath), "The failing mutation must not have produced a file.");

        // A warning was logged for the partial failure.
        Assert.IsTrue(
            logger.Entries.Any(e => e.Level == LogLevel.Warning),
            "A warning must be logged when a composite apply fails partway through.");

        // The workspace must NOT be reloaded on a failed apply.
        Assert.IsFalse(workspace.ReloadCalled, "ReloadAsync must not run when the apply failed.");

        // Regression guard: the preview token is intentionally left valid (not invalidated) on failure.
        Assert.IsNotNull(store.Retrieve(token), "The preview token must remain valid after a partial failure.");
    }

    [TestMethod]
    public async Task ApplyComposite_FirstMutation_Failure_Is_Not_Marked_Partial()
    {
        // When nothing was written before the failure it is a clean failure, not a partial apply.
        var blocker = Path.Combine(_tempDir, "blocker");
        await File.WriteAllTextAsync(blocker, "i am a file");
        var doomedPath = Path.Combine(blocker, "nested.cs");

        var mutations = new List<CompositeFileMutation> { new(doomedPath, "// never written", DeleteFile: false) };
        var store = new CompositePreviewStore();
        var token = store.Store("ws-1", 1, "test composite", mutations);
        var orchestrator = new CompositeApplyOrchestrator(new RecordingWorkspaceManager(), store);

        var result = await orchestrator.ApplyCompositeAsync(token, CancellationToken.None);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, result.AppliedFiles.Count);
        Assert.IsNotNull(result.Error);
        Assert.IsFalse(result.Error!.StartsWith("Partial composite apply: ", StringComparison.Ordinal),
            "A failure with zero prior writes is a clean failure, not a partial apply.");
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
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
