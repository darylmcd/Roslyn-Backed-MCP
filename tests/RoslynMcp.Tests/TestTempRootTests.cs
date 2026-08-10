namespace RoslynMcp.Tests;

/// <summary>
/// Guards the per-run temp-root isolation that replaced the shared-parent delete
/// (row <c>test-temp-root-shared-cleanup-race</c>).
/// <para>
/// The regression these tests exist for: <c>[AssemblyCleanup]</c> used to delete
/// <c>%TEMP%/RoslynMcpTests</c> recursively, so whichever test assembly finished first destroyed
/// every concurrently-running assembly's in-flight fixtures. Per-test GUIDs did not help — they
/// only made the LEAF unique, while the deleted tree was the shared parent above it. The
/// load-bearing property is therefore not "roots are unique" but "no run ever deletes a directory
/// belonging to a live sibling run", which
/// <see cref="TestTempRoot.ReapAbandonedRuns(DateTime)"/> must uphold.
/// </para>
/// </summary>
[TestClass]
public sealed class TestTempRootTests
{
    private readonly List<string> _created = [];

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var dir in _created)
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(dir);
        }
    }

    private string CreateSiblingRun(string label)
    {
        var dir = Path.Combine(
            TestTempRoot.SharedParent,
            TestTempRoot.RunDirectoryPrefix + label + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "fixture.txt"), "in-flight work");
        _created.Add(dir);
        return dir;
    }

    private static void Backdate(string dir, TimeSpan age)
    {
        var stamp = DateTime.UtcNow - age;
        Directory.SetLastWriteTimeUtc(dir, stamp);
        Directory.SetCreationTimeUtc(dir, stamp);
    }

    [TestMethod]
    public void Current_IsAPerProcessSubdirectoryOfTheSharedParent()
    {
        StringAssert.StartsWith(TestTempRoot.Current, TestTempRoot.SharedParent);
        Assert.AreNotEqual(
            TestTempRoot.SharedParent.TrimEnd(Path.DirectorySeparatorChar),
            TestTempRoot.Current.TrimEnd(Path.DirectorySeparatorChar),
            "Current must be a SUBDIRECTORY of the shared parent — if it ever equals the parent, " +
            "cleanup deletes every concurrent run's fixtures again.");

        var leaf = Path.GetFileName(TestTempRoot.Current);
        StringAssert.StartsWith(leaf, TestTempRoot.RunDirectoryPrefix);
        StringAssert.Contains(
            leaf,
            Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "The run directory must identify its owning process so an abandoned root is attributable.");
    }

    [TestMethod]
    public void Current_ResolvesToTheSameDirectoryOnEveryAccess()
    {
        // Behavioral, not a self-comparison: if `Current` were ever changed from a cached
        // initializer to a recomputed expression (`=> Path.Combine(..., Guid.NewGuid()...)`),
        // every access would mint a fresh path, cleanup would delete a directory no test wrote
        // to, and each test would silently get its own root. Writing through one access and
        // reading through another is what proves the value is cached.
        Directory.CreateDirectory(TestTempRoot.Current);
        var marker = Path.Combine(TestTempRoot.Current, "stability-" + Guid.NewGuid().ToString("N")[..8] + ".txt");
        File.WriteAllText(marker, "written via one access");

        var observed = Path.Combine(TestTempRoot.Current, Path.GetFileName(marker));
        Assert.IsTrue(File.Exists(observed), "TestTempRoot.Current changed between accesses — it must be computed once per process.");
        File.Delete(observed);
    }

    [TestMethod]
    public void ReapAbandonedRuns_LeavesAFreshSiblingRunIntact()
    {
        // THE regression. A concurrently-running assembly's root is young; reaping must skip it.
        var sibling = CreateSiblingRun("sibling-fresh");

        TestTempRoot.ReapAbandonedRuns(DateTime.UtcNow);

        Assert.IsTrue(
            Directory.Exists(sibling),
            "A fresh sibling run directory was deleted — this is the shared-parent race reintroduced.");
        Assert.IsTrue(
            File.Exists(Path.Combine(sibling, "fixture.txt")),
            "The sibling run's in-flight fixture file was destroyed.");
    }

    [TestMethod]
    public void ReapAbandonedRuns_RemovesASiblingRunOlderThanTheStaleThreshold()
    {
        var sibling = CreateSiblingRun("sibling-abandoned");
        Backdate(sibling, TestTempRoot.StaleRunAge + TimeSpan.FromHours(1));

        TestTempRoot.ReapAbandonedRuns(DateTime.UtcNow);

        Assert.IsFalse(
            Directory.Exists(sibling),
            "An abandoned run directory was not reaped — isolating the root would leak %TEMP% forever.");
    }

    [TestMethod]
    public void ReapAbandonedRuns_NeverRemovesThisRunEvenWhenItLooksStale()
    {
        Directory.CreateDirectory(TestTempRoot.Current);
        var marker = Path.Combine(TestTempRoot.Current, "own-run-marker.txt");
        File.WriteAllText(marker, "this run is still going");

        // Anchor far in the future so the age check would classify this run as abandoned.
        TestTempRoot.ReapAbandonedRuns(DateTime.UtcNow + TestTempRoot.StaleRunAge + TimeSpan.FromDays(365));

        Assert.IsTrue(File.Exists(marker), "The reaper deleted the running assembly's own temp root.");
        File.Delete(marker);
    }

    [TestMethod]
    public void ReapAbandonedRuns_NeverDeletesTheSharedParentItself()
    {
        Directory.CreateDirectory(TestTempRoot.SharedParent);

        TestTempRoot.ReapAbandonedRuns(DateTime.UtcNow + TimeSpan.FromDays(365));

        Assert.IsTrue(
            Directory.Exists(TestTempRoot.SharedParent),
            "The shared parent must survive — deleting it is the original defect.");
    }

    [TestMethod]
    public void ReapAbandonedRuns_IgnoresNonRunDirectories()
    {
        // Anything not matching the run- prefix is not ours to reap, however old it looks.
        var foreign = Path.Combine(TestTempRoot.SharedParent, "unrelated-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(foreign);
        _created.Add(foreign);
        Backdate(foreign, TestTempRoot.StaleRunAge + TimeSpan.FromDays(30));

        TestTempRoot.ReapAbandonedRuns(DateTime.UtcNow);

        Assert.IsTrue(Directory.Exists(foreign), "A directory outside the run- namespace was reaped.");
    }
}
