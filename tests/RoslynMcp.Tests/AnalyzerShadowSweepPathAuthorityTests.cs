using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class AnalyzerShadowSweepPathAuthorityTests
{
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void Sweep_DoesNotFollowSharedOrWorkspaceLink(bool sharedLink, bool symbolicLink)
    {
        if (!OperatingSystem.IsWindows()) return;

        var root = Path.Combine(TestTempRoot.Current, "shadow-path-" + Guid.NewGuid().ToString("N"));
        var shared = Path.Combine(root, "shared");
        var external = Path.Combine(root, "external");
        var workspaceName = "workspace-999999-" + Guid.NewGuid().ToString("N") + "-external";
        var externalWorkspace = sharedLink ? Path.Combine(external, workspaceName) : external;
        var stale = Path.Combine(externalWorkspace, "stale");
        var payload = Path.Combine(stale, "payload.bin");
        var link = sharedLink ? shared : Path.Combine(shared, workspaceName);
        Directory.CreateDirectory(stale);
        if (!sharedLink) Directory.CreateDirectory(shared);
        File.WriteAllBytes(payload, [1, 2, 3, 4]);
        var timestamp = DateTime.UtcNow - TimeSpan.FromDays(3);
        Directory.SetCreationTimeUtc(stale, timestamp);
        Directory.SetLastWriteTimeUtc(stale, timestamp);
        var before = File.GetAttributes(payload);
        var directoryAttributes = File.GetAttributes(stale);
        try
        {
            if (symbolicLink)
            {
                try
                {
                    Directory.CreateSymbolicLink(link, sharedLink ? external : externalWorkspace);
                }
                catch (IOException ex) when ((ex.HResult & 0xffff) == 1314)
                {
                    Assert.Inconclusive("Directory symlinks require a privilege unavailable on this host.");
                }
            }
            else WindowsDirectoryJunction.Create(link, sharedLink ? external : externalWorkspace);

            AnalyzerReferenceIsolation.SweepAbandonedRoots(shared, NullLogger.Instance);

            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(payload));
            Assert.AreEqual(before, File.GetAttributes(payload));
            Assert.AreEqual(directoryAttributes, File.GetAttributes(stale));
            Assert.IsTrue(Directory.Exists(stale));
        }
        finally
        {
            if (Directory.Exists(link)) Directory.Delete(link);
            TestFixtureFileSystem.DeleteDirectoryIfExists(root);
        }
    }
}
