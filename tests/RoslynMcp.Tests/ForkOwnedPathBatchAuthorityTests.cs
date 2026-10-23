using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

public sealed partial class ForkInvocationManagerAuthorityTests
{
    [TestMethod]
    [DataRow("pre-alias")]
    [DataRow("post-alias")]
    [DataRow("post-create")]
    [DataRow("post-revoke")]
    public async Task OwnedBatchMapping_PreservesActualManagerIssuedCallbackAuthority(string actor)
    {
        NuGetInstallationFixture? fixture = null;
        WorkspaceManager? manager = null;
        PhysicalDirectoryScope.OwnedRoot? owner = null;
        IDisposable? principal = null, copyScope = null;
        var privateRoot = Path.Combine(TestTempRoot.Current, "owned-mapping-" + Guid.NewGuid().ToString("N"));
        var foreignRoot = Path.Combine(TestTempRoot.Current, "foreign-mapping-" + Guid.NewGuid().ToString("N"));
        string? privateParent = null;
        var linked = false;
        Exception? primary = null;
        try
        {
            fixture = await NuGetInstallationFixture.CreateAsync("Owned.Mapping", "1.0.0",
                new Dictionary<string, byte[]> { ["content/readme.txt"] = "owned package"u8.ToArray() });
            var entry = Path.Combine(fixture.Root, "source", "Entry.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(entry)!);
            var originalBytes = "public class Entry { }"u8.ToArray();
            await File.WriteAllBytesAsync(entry, originalBytes);
            var observed = new OwnedBatchCallbackPolicy();
            var policy = new ForkInvocationPathPolicy(observed);
            manager = CreateManager(policy, new FileWatcherService(NullLogger<FileWatcherService>.Instance), 2, new PreviewStore());
            var loaded = await manager.LoadAsync(Path.Combine(fixture.Root, "Owner.csproj"), fixture.LoadProperties, CancellationToken.None);
            var source = manager.GetCurrentSolution(loaded.WorkspaceId);
            Assert.IsTrue(source.Projects.SelectMany(project => project.Documents).Any(document =>
                FileSystemPath.Comparer.Equals(document.FilePath, entry)), "The nested actor input must belong to the actual native loaded source.");
            var layout = await PhysicalGraphLayout.CreateAsync(loaded.WorkspaceId,
                manager.GetStatus(loaded.WorkspaceId).LoadedPath!, source, policy, CancellationToken.None);
            Directory.CreateDirectory(privateRoot);
            owner = PhysicalDirectoryScope.CreateOwnedRoot(Path.Combine(privateRoot, "issued"));
            var target = layout.MapTo(owner.Path, entry);
            privateParent = Path.GetDirectoryName(target)!;
            var relative = Path.GetRelativePath(owner.Path, target);
            Assert.IsTrue(FileSystemPath.IsStrictDescendant(owner.Path, privateParent), "The actor needs a proper held-root descendant.");
            owner.EnsureDirectories(Path.GetDirectoryName(relative)!, CancellationToken.None);
            if (actor != "post-create") await owner.WriteFileAsync(relative, originalBytes, CancellationToken.None);
            else Assert.IsFalse(File.Exists(target), "The mapped private leaf must actually be absent before admission.");
            Directory.CreateDirectory(foreignRoot);
            var sentinel = Path.Combine(foreignRoot, Path.GetFileName(target));
            await File.WriteAllBytesAsync(sentinel, originalBytes);
            var operation = ((IWorkspaceEvaluationLoader)manager).CaptureStageInvocationAuthority(loaded.WorkspaceId, source);
            principal = policy.Enter(operation);
            copyScope = operation.EnterOwnedCopy(owner, layout);
            var sourceCalls = 0;
            var acted = false;
            void Alias()
            {
                Directory.Move(privateParent, privateParent + "-retained");
                if (OperatingSystem.IsWindows()) WindowsDirectoryJunction.Create(privateParent, foreignRoot);
                else Directory.CreateSymbolicLink(privateParent, foreignRoot);
                linked = true;
                acted = true;
            }
            if (actor == "pre-alias") Alias();
            observed.OnValidate = (id, paths) =>
            {
                if (!paths.Contains(entry, FileSystemPath.Comparer)) return;
                Assert.AreEqual(loaded.WorkspaceId, id, "The current grant must use the exact original session.");
                Assert.IsFalse(paths.Any(path => FileSystemPath.IsStrictDescendant(owner.Path, path)), "Private prefixes are not original grants.");
                sourceCalls++;
                if (acted) return;
                if (actor == "post-alias") Alias();
                else if (actor == "post-create") { File.WriteAllBytes(target, originalBytes); acted = true; }
                else if (actor == "post-revoke") { copyScope.Dispose(); acted = true; }
            };
            Exception? observedFailure = null;
            try { await policy.ValidateAsync(loaded.WorkspaceId, [target], CancellationToken.None); }
            catch (Exception failure) { observedFailure = failure; }
            Assert.IsTrue(acted, "The actual native alias, regular-file creation or issued scope revocation must execute.");
            CollectionAssert.AreEqual(originalBytes, await File.ReadAllBytesAsync(sentinel), "Private admission must not alter foreign bytes.");
            CollectionAssert.AreEqual(originalBytes, await File.ReadAllBytesAsync(entry), "The original source is untouched by mapping.");
            if (actor == "pre-alias") Assert.AreEqual(0, sourceCalls, "A known private alias must refuse before original-target callbacks.");
            else Assert.AreEqual(1, sourceCalls, "The actor must execute at the exact mapped original grant.");
            if (actor == "post-create")
            {
                Assert.IsTrue(File.Exists(target));
                CollectionAssert.AreEqual(originalBytes, await File.ReadAllBytesAsync(target));
                Assert.IsNull(observedFailure, "Fresh post-callback mapping may admit a newly created physical regular file.");
            }
            else
            {
                Assert.IsNotNull(observedFailure);
                Assert.IsInstanceOfType<InvalidOperationException>(observedFailure);
            }
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Manager-issued owned mapping and fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => manager?.Dispose()),
                CleanupFailureCollector.FromAction(() => copyScope?.Dispose()),
                CleanupFailureCollector.FromAction(() => principal?.Dispose()),
                CleanupFailureCollector.FromAction(() => owner?.Dispose()),
                CleanupFailureCollector.FromAction(() => { if (linked) Directory.Delete(privateParent!); }),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(privateRoot)),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(foreignRoot)),
                CleanupFailureCollector.FromAction(() => fixture?.Dispose()));
        }
    }

    private sealed class OwnedBatchCallbackPolicy : IWorkspacePathPolicy
    {
        private readonly OwnedTestWorkspacePathPolicy _owned = new();
        internal Action<string, IReadOnlyList<string>>? OnValidate { get; set; }
        public async Task ValidateAsync(string id, IReadOnlyList<string> paths, CancellationToken ct)
        {
            await _owned.ValidateAsync(id, paths, ct);
            OnValidate?.Invoke(id, paths);
        }
    }
}
