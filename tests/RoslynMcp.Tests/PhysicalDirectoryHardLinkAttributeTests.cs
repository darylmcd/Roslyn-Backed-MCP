using Microsoft.Win32.SafeHandles;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class PhysicalDirectoryHardLinkAttributeTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ReadOnlyCleanup_PreservesSurvivingExternalFileAttributes(bool hardLink)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The object-bound readonly attribute deletion path is Windows-specific.");
            return;
        }
        var fixture = Path.Combine(TestTempRoot.Current, "hardlink-attributes-" + Guid.NewGuid().ToString("N"));
        var issued = Path.Combine(fixture, "issued");
        var external = Path.Combine(fixture, "external.txt");
        var ownedEntry = Path.Combine(issued, "owned.txt");
        var bytes = "external file bytes must survive owned entry deletion"u8.ToArray();
        PhysicalDirectoryScope.OwnedRoot? owner = null;
        SafeFileHandle? externalHandle = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(fixture);
            owner = PhysicalDirectoryScope.CreateOwnedRoot(issued);
            await File.WriteAllBytesAsync(external, bytes);
            if (hardLink)
            {
                PhysicalFileLinks.CreateHardLink(ownedEntry, external);
            }
            else await File.WriteAllBytesAsync(ownedEntry, "independent owned file"u8.ToArray());
            File.SetAttributes(external, File.GetAttributes(external) | FileAttributes.ReadOnly);
            File.SetAttributes(ownedEntry, File.GetAttributes(ownedEntry) | FileAttributes.ReadOnly);
            externalHandle = File.OpenHandle(external, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var before = File.GetAttributes(externalHandle);
            Assert.IsTrue((before & FileAttributes.ReadOnly) != 0, "The retained external FILE object must actually be readonly before cleanup.");
            Assert.IsTrue((File.GetAttributes(ownedEntry) & FileAttributes.ReadOnly) != 0);
            CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(external));

            var outcome = owner.Delete(CancellationToken.None);

            Assert.AreEqual(PhysicalDirectoryCleanupOutcome.Removed, outcome);
            Assert.IsFalse(File.Exists(ownedEntry), "The readonly owned entry must be reclaimed.");
            Assert.IsFalse(Directory.Exists(issued));
            Assert.IsTrue(File.Exists(external));
            CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(external));
            Assert.AreEqual(before, File.GetAttributes(externalHandle),
                "Deleting an owned hardlink must preserve attributes on the surviving external FILE object; pathname metadata can be stale.");
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Hardlink attribute regression and fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => owner?.Dispose()),
                CleanupFailureCollector.FromAction(() => externalHandle?.Dispose()),
                CleanupFailureCollector.FromAction(() =>
                {
                    if (File.Exists(external)) File.SetAttributes(external, File.GetAttributes(external) & ~FileAttributes.ReadOnly);
                }),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(fixture)));
        }
    }

}
