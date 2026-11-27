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
/// <see cref="TestTempRoot.ReapAbandonedRuns()"/> must uphold.
/// </para>
/// </summary>
[TestClass]
public sealed class TestTempRootTests
{
    private const int SiblingPid = 123;
    private readonly string _parent = Path.Combine(TestTempRoot.Current, "reap-" + Guid.NewGuid().ToString("N"));
    private string CurrentRoot => Path.Combine(_parent, "run-1-00000000");

    [TestCleanup]
    public void Cleanup() => TestFixtureFileSystem.DeleteDirectoryIfExists(_parent);

    private string CreateSiblingRun(string name = "run-123-00000001")
    {
        var dir = Path.Combine(_parent, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "fixture.txt"), "in-flight work");
        return dir;
    }

    private void Reap(Func<int, bool?> ownerAbsent, TimeSpan clockAdvance) =>
        TestTempRoot.ReapAbandonedRuns(_parent, CurrentRoot, DateTime.UtcNow + clockAdvance, ownerAbsent);

    private static TimeSpan PastStaleThreshold => TestTempRoot.StaleRunAge + TimeSpan.FromHours(1);

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
        var sibling = CreateSiblingRun();

        Reap(pid => pid == SiblingPid ? true : null, TimeSpan.Zero);

        Assert.IsTrue(Directory.Exists(sibling), "Even proven absence does not bypass the age threshold.");
        Assert.IsTrue(File.Exists(Path.Combine(sibling, "fixture.txt")));
    }

    [TestMethod]
    public void ReapAbandonedRuns_RemovesASiblingRunOlderThanTheStaleThreshold()
    {
        var sibling = CreateSiblingRun();

        Reap(pid => pid == SiblingPid ? true : null, PastStaleThreshold);

        Assert.IsFalse(Directory.Exists(sibling), "An old run with a proven absent owner should be reaped.");
    }

    [TestMethod]
    [DataRow("present")]
    [DataRow("unknown")]
    [DataRow("probe-failed")]
    public void ReapAbandonedRuns_PreservesAnOldRunUnlessItsOwnerIsProvenAbsent(string evidence)
    {
        var sibling = CreateSiblingRun();

        Reap(pid => pid != SiblingPid ? null : evidence switch
        {
            "present" => false,
            "unknown" => null,
            _ => throw new IOException("Injected owner probe failure."),
        }, PastStaleThreshold);

        Assert.IsTrue(File.Exists(Path.Combine(sibling, "fixture.txt")));
    }

    [TestMethod]
    [DataRow("run-sibling-00000001")]
    [DataRow("run-0-00000001")]
    [DataRow("run-2147483648-00000001")]
    [DataRow("run-123-0000000z")]
    [DataRow("run-123-123")]
    public void ReapAbandonedRuns_PreservesNamesWithoutAValidRunOwner(string name)
    {
        var sibling = CreateSiblingRun(name);
        var probes = 0;

        Reap(pid =>
        {
            probes++;
            return pid == SiblingPid ? true : null;
        }, PastStaleThreshold);

        Assert.AreEqual(0, probes);
        Assert.IsTrue(File.Exists(Path.Combine(sibling, "fixture.txt")));
    }

    [TestMethod]
    public void ReapAbandonedRuns_NeverRemovesThisRunEvenWhenItLooksStale()
    {
        var current = CreateSiblingRun(Path.GetFileName(CurrentRoot));

        Reap(pid => pid == 1 ? true : null, TimeSpan.FromDays(365));

        Assert.IsTrue(File.Exists(Path.Combine(current, "fixture.txt")));
    }

    [TestMethod]
    public void ReapAbandonedRuns_NeverDeletesTheParentItself()
    {
        var abandoned = CreateSiblingRun();

        Reap(pid => pid == SiblingPid ? true : null, TimeSpan.FromDays(365));

        Assert.IsFalse(Directory.Exists(abandoned));
        Assert.IsTrue(Directory.Exists(_parent));
    }

    [TestMethod]
    public void ReapAbandonedRuns_IgnoresNonRunDirectories()
    {
        var foreign = CreateSiblingRun("unrelated-123-00000001");

        Reap(pid => pid == SiblingPid ? true : null, PastStaleThreshold);

        Assert.IsTrue(Directory.Exists(foreign));
    }
}
