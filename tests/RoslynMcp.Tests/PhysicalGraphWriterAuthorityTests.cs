using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class PhysicalGraphWriterAuthorityTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Copy_OverwritesOwnedEntryWithoutChangingExternalHardlinkInode(bool hardLink)
    {
        var fixture = Path.Combine(TestTempRoot.Current, "owned-copy-write-" + Guid.NewGuid().ToString("N"));
        var issued = Path.Combine(fixture, "issued");
        var source = Path.Combine(fixture, "reviewed.cs");
        var external = Path.Combine(fixture, "external.cs");
        var target = Path.Combine(issued, "reviewed.cs");
        var reviewed = "public class Reviewed { public const int Value = 7; }"u8.ToArray();
        var unowned = "external original bytes must survive owned copy"u8.ToArray();
        PhysicalDirectoryScope.OwnedRoot? owner = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(fixture);
            owner = PhysicalDirectoryScope.CreateOwnedRoot(issued);
            await File.WriteAllBytesAsync(source, reviewed);
            await File.WriteAllBytesAsync(external, unowned);
            if (hardLink) PhysicalFileLinks.CreateHardLink(target, external);
            else await File.WriteAllBytesAsync(target, "previous owned content"u8.ToArray());
            var plan = new PhysicalGraphCopyPlan([new(source, target)], [issued], []);
            await plan.CopyAsync(owner, CancellationToken.None);
            CollectionAssert.AreEqual(reviewed, await File.ReadAllBytesAsync(target), "The reviewed copy must actually complete and preserve overlay support.");
            CollectionAssert.AreEqual(unowned, await File.ReadAllBytesAsync(external),
                "An owned directory entry grants no write authority over surviving external hardlinks to its old inode.");
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Owned writer regression and fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => owner?.Dispose()),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(fixture)));
        }
    }

    [TestMethod]
    public async Task Copy_SwappedAncestorLinkCannotGrantExternalWriteAuthority()
    {
        var fixture = Path.Combine(TestTempRoot.Current, "owned-copy-ancestor-" + Guid.NewGuid().ToString("N"));
        var issued = Path.Combine(fixture, "issued");
        var nested = Path.Combine(issued, "nested");
        var retained = Path.Combine(issued, "retained");
        var external = Path.Combine(fixture, "external");
        var source = Path.Combine(fixture, "reviewed.cs");
        var sentinel = Path.Combine(external, "reviewed.cs");
        PhysicalDirectoryScope.OwnedRoot? owner = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(fixture);
            owner = PhysicalDirectoryScope.CreateOwnedRoot(issued);
            Directory.CreateDirectory(nested);
            Directory.CreateDirectory(external);
            await File.WriteAllTextAsync(source, "reviewed source bytes");
            await File.WriteAllTextAsync(sentinel, "unrelated external bytes");
            Directory.Move(nested, retained);
            if (OperatingSystem.IsWindows()) WindowsDirectoryJunction.Create(nested, external);
            else Directory.CreateSymbolicLink(nested, external);
            Assert.AreEqual("unrelated external bytes", await File.ReadAllTextAsync(Path.Combine(nested, "reviewed.cs")),
                "The actual swapped ancestor must resolve to the external sentinel before the write attempt.");
            var plan = new PhysicalGraphCopyPlan([new(source, Path.Combine(nested, "reviewed.cs"))], [], []);
            Exception? observed = null;
            try { await plan.CopyAsync(owner, CancellationToken.None); }
            catch (Exception failure) when (failure is IOException or InvalidOperationException) { observed = failure; }
            Assert.IsNotNull(observed, "A namespace alias is not permission to write its external target.");
            Assert.AreEqual("unrelated external bytes", await File.ReadAllTextAsync(sentinel));
            Assert.HasCount(0, Directory.GetFileSystemEntries(retained));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Owned ancestor writer regression and fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => owner?.Dispose()),
                CleanupFailureCollector.FromAction(() =>
                {
                    if (Directory.Exists(nested) && (File.GetAttributes(nested) & FileAttributes.ReparsePoint) != 0) Directory.Delete(nested);
                }),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(fixture)));
        }
    }
    [TestMethod]
    public async Task OwnedCopyAndOverlay_PreserveExecutableUnixModeOnTheirFreshInodes()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Unix executable permission bits are qualified on the Unix runner.");
            return;
        }
        var fixture = Path.Combine(TestTempRoot.Current, "owned-copy-mode-" + Guid.NewGuid().ToString("N"));
        var issued = Path.Combine(fixture, "issued");
        var source = Path.Combine(fixture, "tool.sh");
        var target = Path.Combine(issued, "tool.sh");
        PhysicalDirectoryScope.OwnedRoot? owner = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(fixture);
            owner = PhysicalDirectoryScope.CreateOwnedRoot(issued);
            await File.WriteAllTextAsync(source, "#!/bin/sh\nexit 7\n");
            var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute;
            File.SetUnixFileMode(source, mode);
            var plan = new PhysicalGraphCopyPlan([new(source, target)], [issued], []);
            await plan.CopyAsync(owner, CancellationToken.None);
            Assert.AreEqual(mode, File.GetUnixFileMode(target), "Reviewed executable files must remain executable after isolated copying.");
            Assert.AreEqual(await File.ReadAllTextAsync(source), await File.ReadAllTextAsync(target));
            await owner.WriteFileAsync("tool.sh", "#!/bin/sh\nexit 9\n"u8.ToArray(), CancellationToken.None);
            Assert.AreEqual("#!/bin/sh\nexit 9\n", await File.ReadAllTextAsync(target));
            Assert.AreEqual(mode, File.GetUnixFileMode(target), "Fresh-inode overlay replacement must preserve the original owned entry's executable mode.");
            Assert.AreEqual(mode, File.GetUnixFileMode(source));
            Assert.AreEqual("#!/bin/sh\nexit 7\n", await File.ReadAllTextAsync(source));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Owned mode regression fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => owner?.Dispose()),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(fixture)));
        }
    }

    [TestMethod]
    public async Task OwnedCopy_PreservesWindowsBasicAttributesAndLastWriteTime()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows file attributes are qualified on the Windows runner.");
            return;
        }
        var fixture = Path.Combine(TestTempRoot.Current, "owned-copy-attributes-" + Guid.NewGuid().ToString("N"));
        var issued = Path.Combine(fixture, "issued");
        var source = Path.Combine(fixture, "tool.cmd");
        var target = Path.Combine(issued, "tool.cmd");
        PhysicalDirectoryScope.OwnedRoot? owner = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(fixture);
            owner = PhysicalDirectoryScope.CreateOwnedRoot(issued);
            await File.WriteAllTextAsync(source, "@exit /b 7");
            var modified = new DateTime(2025, 1, 2, 3, 4, 6, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(source, modified);
            var attributes = FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.Archive;
            File.SetAttributes(source, attributes);
            var plan = new PhysicalGraphCopyPlan([new(source, target)], [issued], []);
            await plan.CopyAsync(owner, CancellationToken.None);
            Assert.AreEqual("@exit /b 7", await File.ReadAllTextAsync(target));
            Assert.AreEqual(attributes, File.GetAttributes(target));
            Assert.AreEqual(File.GetLastWriteTimeUtc(source), File.GetLastWriteTimeUtc(target));
            Assert.AreEqual(attributes, File.GetAttributes(source));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Owned Windows metadata fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => owner?.Dispose()),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(fixture)));
        }
    }

    [TestMethod]
    public async Task OwnedCopyAndHardlinkOverlay_PreserveWindowsAlternateStreamsOnFreshInodes()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("NTFS alternate streams are qualified on the Windows runner.");
            return;
        }
        var fixture = Path.Combine(TestTempRoot.Current, "owned-copy-streams-" + Guid.NewGuid().ToString("N"));
        var issued = Path.Combine(fixture, "issued");
        var source = Path.Combine(fixture, "source.cmd");
        var copied = Path.Combine(issued, "copied.cmd");
        var external = Path.Combine(fixture, "external.cmd");
        var overlay = Path.Combine(issued, "overlay.cmd");
        PhysicalDirectoryScope.OwnedRoot? owner = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(fixture);
            owner = PhysicalDirectoryScope.CreateOwnedRoot(issued);
            await File.WriteAllTextAsync(source, "source main stream");
            await File.WriteAllTextAsync(source + ":reviewed-metadata", "source alternate stream");
            var plan = new PhysicalGraphCopyPlan([new(source, copied)], [issued], []);
            await plan.CopyAsync(owner, CancellationToken.None);
            Assert.AreEqual("source main stream", await File.ReadAllTextAsync(copied));
            Assert.AreEqual("source alternate stream", await File.ReadAllTextAsync(copied + ":reviewed-metadata"));
            await File.WriteAllTextAsync(external, "external original main stream");
            await File.WriteAllTextAsync(external + ":reviewed-metadata", "external original alternate stream");
            var originalCreation = new DateTime(2024, 2, 3, 4, 5, 6, DateTimeKind.Utc);
            File.SetCreationTimeUtc(external, originalCreation);
            PhysicalFileLinks.CreateHardLink(overlay, external);
            await owner.WriteFileAsync("overlay.cmd", "reviewed replacement main stream"u8.ToArray(), CancellationToken.None);
            Assert.AreEqual("reviewed replacement main stream", await File.ReadAllTextAsync(overlay));
            Assert.AreEqual("external original alternate stream", await File.ReadAllTextAsync(overlay + ":reviewed-metadata"));
            Assert.AreEqual(originalCreation, File.GetCreationTimeUtc(overlay), "Overlay replacement must preserve the old entry's original creation time on the fresh inode.");
            Assert.AreEqual(originalCreation, File.GetCreationTimeUtc(external));
            Assert.AreEqual("external original main stream", await File.ReadAllTextAsync(external));
            Assert.AreEqual("external original alternate stream", await File.ReadAllTextAsync(external + ":reviewed-metadata"));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Owned Windows alternate-stream fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => owner?.Dispose()),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(fixture)));
        }
    }

}
