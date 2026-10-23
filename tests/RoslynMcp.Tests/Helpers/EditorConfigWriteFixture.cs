using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests.Helpers;

/// <summary>Watcher unit ownership with real held source transitions; issues no compiler certificate.</summary>
internal sealed class EditorConfigWriteFixture
{
    private AdhocWorkspace? _workspace;
    private FileWatcherService? _watcher;
    private readonly List<PhysicalMutationRecovery> _operations = [];
    private PhysicalDirectoryScope? _root;

    private EditorConfigWriteFixture(string root)
    {
        Root = Path.GetFullPath(root);
        ConfigPath = Path.Combine(Root, ".editorconfig");
    }

    internal string Root { get; }
    internal string ConfigPath { get; }
    internal string WorkspaceId { get; } = Guid.NewGuid().ToString("N");
    internal FileWatcherService Watcher => _watcher ?? throw new InvalidOperationException("The fixture watcher has not been acquired.");
    internal IEditorConfigWriteCoordinator Coordinator => Watcher;
    internal Func<PhysicalMutationRecovery, IReadOnlyList<string>, CancellationToken, Task>? OnAuthorize { get; set; }

    internal EditorConfigWriteOperation CreateOperation(byte[]? before, byte[] desired)
    {
        var snapshot = before is null ? new FileSnapshotDto(ConfigPath, null) : FileSnapshotDto.FromExistingBytes(ConfigPath, before);
        var operation = new PhysicalMutationRecovery(WorkspaceId, "Watcher config unit operation", _workspace!.CurrentSolution,
            0, [snapshot], new object()) { CapturedGraphFiles = [], CapturedLayout = null };
        _operations.Add(operation);
        var desiredIssued = false;
        void AssertIssued(PhysicalMutationRecovery actual, FileSnapshotDto observed)
        {
            Assert.AreSame(operation, actual);
            Assert.IsTrue(FileSystemPath.Comparer.Equals(ConfigPath, observed.FilePath));
            _root!.AssertCurrentPath();
            var bytes = observed.OriginalText is null ? null : observed.OriginalBytes.ToArray();
            Assert.IsTrue(EditorConfigBytes.AreEqual(before, bytes) || desiredIssued && EditorConfigBytes.AreEqual(desired, bytes),
                "Only the exact original bytes or registered desired intent belong to this operation.");
        }
        async Task Authorize(IReadOnlyList<string> paths, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _root!.AssertCurrentPath();
            foreach (var path in paths)
                Assert.IsTrue(FileSystemPath.Comparer.Equals(Root, path) || FileSystemPath.IsStrictDescendant(Root, path));
            if (OnAuthorize is { } callback) await callback(operation, paths, ct);
            ct.ThrowIfCancellationRequested();
            _root.AssertCurrentPath();
            foreach (var path in paths) WorkspaceManager.EnsurePhysicalPath(path);
        }
        var authority = new FailClosedWorkspaceManagerStub
        {
            AuthorizePathsHandler = (_, paths, ct) => Authorize(paths, ct),
            AssertKnownFileReadHandler = AssertIssued,
            AcquireSourceCustodyHandler = async (actual, sourcePath, source, leaf, temporary, ct) =>
            {
                Assert.AreSame(operation, actual);
                Assert.IsTrue(FileSystemPath.Comparer.Equals(ConfigPath, sourcePath));
                var custodyPath = Path.Combine(Root, "custody");
                await Authorize([sourcePath, source.Path, custodyPath, Path.Combine(custodyPath, leaf), Path.Combine(source.Path, temporary)], ct);
                var parent = PhysicalDirectoryScope.OpenRoute(custodyPath);
                try { return new PhysicalSourceCustody(source, parent, leaf, parent.AssertCurrentPath, parent.AssertCurrentPath, Authorize); }
                catch (Exception failure)
                {
                    OwnedResourceCleanup.DisposePreservingFailure("Watcher unit custody acquisition failed.", failure, parent);
                    throw;
                }
            }
        };
        var lease = new FixtureLease(intended =>
        {
            Assert.IsTrue(FileSystemPath.Comparer.Equals(ConfigPath, intended.FilePath));
            CollectionAssert.AreEqual(desired, intended.OriginalBytes.ToArray());
            desiredIssued = true;
            AssertIssued(operation, intended);
        });
        return new EditorConfigWriteOperation(operation, authority, lease, snapshot, desired, null);
    }

    internal static async Task RunAsync(byte[]? initialConfig, Func<EditorConfigWriteFixture, Task> body)
    {
        var path = Path.Combine(TestTempRoot.Current, nameof(EditorConfigWriteFixture), Guid.NewGuid().ToString("N"));
        EditorConfigWriteFixture? fixture = null;
        Exception? primary = null;
        try
        {
            fixture = new(path);
            fixture._workspace = new AdhocWorkspace();
            fixture._watcher = new FileWatcherService(NullLogger<FileWatcherService>.Instance);
            Directory.CreateDirectory(path);
            Directory.CreateDirectory(Path.Combine(path, "custody"));
            fixture._root = PhysicalDirectoryScope.OpenRoute(path);
            var solutionPath = Path.Combine(path, "Watcher.slnx");
            await File.WriteAllTextAsync(solutionPath, "<Solution />");
            if (initialConfig is not null) await File.WriteAllBytesAsync(fixture.ConfigPath, initialConfig);
            fixture.Watcher.Watch(fixture.WorkspaceId, solutionPath);
            Assert.IsFalse(fixture.Watcher.IsStale(fixture.WorkspaceId), "The watched initial namespace must be established before external actors run.");
            await body(fixture);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            var cleanup = new List<Func<ValueTask>>();
            if (fixture is not null)
            {
                cleanup.AddRange(fixture._operations.Select(operation => CleanupFailureCollector.FromAction(operation.ReleaseSourceTransitionHandles)));
                cleanup.Add(CleanupFailureCollector.FromAction(() => OwnedResourceCleanup.Dispose("Watcher unit owners failed to release.", null,
                    fixture._watcher, fixture._workspace, fixture._root)));
            }
            cleanup.Add(CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(path)));
            await CleanupFailureCollector.RunAfterFailureAsync("Editorconfig write unit fixture cleanup failed.", primary, cleanup.ToArray());
        }
    }

    private sealed class FixtureLease(Action<FileSnapshotDto> beforeWrite) : IWorkspaceMutationLease
    {
        public void BeforeWrite(FileSnapshotDto intendedPost) => beforeWrite(intendedPost);
        public void Complete(IReadOnlyList<FileSnapshotDto> intendedPostFiles) => throw new InvalidOperationException("The watcher unit fixture cannot publish workspace authority.");
        public void CancelBeforeSourceTransition(IPhysicalRestorationAuthority verification) => throw new InvalidOperationException("The watcher unit fixture cannot cancel native workspace authority.");
        public Task AbortAsync(CancellationToken ct) => throw new InvalidOperationException("The watcher unit fixture cannot abort a workspace epoch.");
        public void Dispose() { }
    }
}
