using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class AnalyzerShadowStartupSweepRegressionTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ProcessParent_CurrentCreator_CanEstablishOwnerAfterSweep(bool stale)
    {
        var sharedParent = Path.Combine(TestTempRoot.Current, "shadow-startup-" + Guid.NewGuid().ToString("N"));
        var parent = Path.Combine(sharedParent, $"process-{Environment.ProcessId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(parent);
        try
        {
            SetAge(parent, stale);
            AnalyzerReferenceIsolation.SweepAbandonedRoots(sharedParent, NullLogger.Instance);

            using var marker = new FileStream(Path.Combine(parent, ".owner.lock"),
                FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            Assert.IsTrue(Directory.Exists(parent));
            Assert.IsTrue(marker.CanWrite);
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(sharedParent);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void WorkspaceParent_CurrentCreator_SurvivesAncestorInitializationSweep(bool stale) =>
        AssertWorkspaceInitialization($"workspace-{Environment.ProcessId}-{Guid.NewGuid():N}-workspace-with-hyphens", stale);

    [TestMethod]
    public void WorkspaceParent_LegacyUnknownCreator_SurvivesAncestorInitializationSweep() =>
        AssertWorkspaceInitialization("legacy-workspace", stale: false);

    private static void AssertWorkspaceInitialization(string name, bool stale)
    {
        var sharedParent = Path.Combine(TestTempRoot.Current, "shadow-startup-" + Guid.NewGuid().ToString("N"));
        var parent = Path.Combine(sharedParent, name);
        Directory.CreateDirectory(parent);
        try
        {
            SetAge(parent, stale);
            var source = Path.Combine(sharedParent, "source.dll");
            File.WriteAllBytes(source, [1, 2, 3, 4]);
            AnalyzerReferenceIsolation.SweepAbandonedRoots(sharedParent, NullLogger.Instance);

            Assert.IsTrue(Directory.Exists(parent), "Sweep must preserve an ancestor still owned by an initializing creator.");
            var copyDirectory = Path.Combine(parent, Guid.NewGuid().ToString("N"), "hash");
            Directory.CreateDirectory(copyDirectory);
            var copy = Path.Combine(copyDirectory, "fixture.dll");
            File.Copy(source, copy);
            CollectionAssert.AreEqual(File.ReadAllBytes(source), File.ReadAllBytes(copy));
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(sharedParent);
        }
    }

    private static void SetAge(string parent, bool stale)
    {
        if (!stale) return;
        var timestamp = DateTime.UtcNow - TimeSpan.FromDays(2);
        Directory.SetCreationTimeUtc(parent, timestamp);
        Directory.SetLastWriteTimeUtc(parent, timestamp);
    }
}
