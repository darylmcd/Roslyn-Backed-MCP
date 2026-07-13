using System.Text.Json;
using RoslynMcp.Host.Stdio.Middleware;

namespace RoslynMcp.Tests;

/// <summary>
/// Coverage for the <c>workspace-auto-load-on-demand</c> initiative's discovery layer
/// (<see cref="SolutionDiscoveryHelper"/>). Pins the deterministic, filesystem-driven strategies:
/// file-anchored walk-up (solution preferred over project), the conservative unique/ambiguous/none
/// classification, the bounded top-level + one-level scan used by query-anchored discovery, and the
/// <c>filePath</c>-like argument extraction. The query-anchored RPC wrapper itself
/// (<c>roots/list</c>) needs a live transport and is covered at the scan seam
/// (<see cref="SolutionDiscoveryHelper.ScanDirectoriesForSolutions"/>) plus the server-null guard.
/// </summary>
[TestClass]
public sealed class SolutionDiscoveryHelperTests
{
    private string _root = null!;

    [TestInitialize]
    public void Init()
    {
        _root = Path.Combine(Path.GetTempPath(), "rmcp-disc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* best-effort temp cleanup */ }
    }

    // ── file-anchored walk-up ───────────────────────────────────────────────

    [TestMethod]
    public void TryDiscoverFromFilePath_WalksUpToNearestSolution()
    {
        var nested = Path.Combine(_root, "src", "Project");
        Directory.CreateDirectory(nested);
        var sln = Path.Combine(_root, "Sample.slnx");
        File.WriteAllText(sln, "<Solution />");
        var source = Path.Combine(nested, "Class1.cs");
        File.WriteAllText(source, "// source");

        var result = SolutionDiscoveryHelper.TryDiscoverFromFilePath(Args(("filePath", source)));

        Assert.AreEqual(SolutionDiscoveryHelper.DiscoveryStatus.Unique, result.Status);
        Assert.AreEqual(sln, result.UniquePath);
    }

    [TestMethod]
    public void TryDiscoverFromFilePath_PrefersSolutionOverProjectAtSameLevel()
    {
        var sln = Path.Combine(_root, "Sample.slnx");
        var csproj = Path.Combine(_root, "Sample.csproj");
        File.WriteAllText(sln, "<Solution />");
        File.WriteAllText(csproj, "<Project />");
        var source = Path.Combine(_root, "Class1.cs");
        File.WriteAllText(source, "// source");

        var result = SolutionDiscoveryHelper.TryDiscoverFromFilePath(Args(("filePath", source)));

        Assert.AreEqual(SolutionDiscoveryHelper.DiscoveryStatus.Unique, result.Status);
        Assert.AreEqual(sln, result.UniquePath, "Solution files must win over project files at the same level.");
    }

    [TestMethod]
    public void TryDiscoverFromFilePath_FallsBackToProjectWhenNoSolution()
    {
        var csproj = Path.Combine(_root, "Only.csproj");
        File.WriteAllText(csproj, "<Project />");
        var source = Path.Combine(_root, "Class1.cs");
        File.WriteAllText(source, "// source");

        var result = SolutionDiscoveryHelper.TryDiscoverFromFilePath(Args(("filePath", source)));

        Assert.AreEqual(SolutionDiscoveryHelper.DiscoveryStatus.Unique, result.Status);
        Assert.AreEqual(csproj, result.UniquePath);
    }

    [TestMethod]
    public void TryDiscoverFromFilePath_TwoSolutions_IsAmbiguous()
    {
        File.WriteAllText(Path.Combine(_root, "A.slnx"), "<Solution />");
        File.WriteAllText(Path.Combine(_root, "B.slnx"), "<Solution />");
        var source = Path.Combine(_root, "Class1.cs");
        File.WriteAllText(source, "// source");

        var result = SolutionDiscoveryHelper.TryDiscoverFromFilePath(Args(("filePath", source)));

        Assert.AreEqual(SolutionDiscoveryHelper.DiscoveryStatus.Ambiguous, result.Status);
        Assert.AreEqual(2, result.Candidates.Count);
        Assert.IsNull(result.UniquePath);
    }

    [TestMethod]
    public void TryDiscoverFromFilePath_NoSolutionAnywhere_IsNone()
    {
        var nested = Path.Combine(_root, "src");
        Directory.CreateDirectory(nested);
        var source = Path.Combine(nested, "Class1.cs");
        File.WriteAllText(source, "// source");

        var result = SolutionDiscoveryHelper.TryDiscoverFromFilePath(Args(("filePath", source)));

        Assert.AreEqual(SolutionDiscoveryHelper.DiscoveryStatus.None, result.Status);
    }

    [TestMethod]
    public void TryDiscoverFromFilePath_NoFilePathArgument_IsNone()
    {
        Assert.AreEqual(SolutionDiscoveryHelper.DiscoveryStatus.None,
            SolutionDiscoveryHelper.TryDiscoverFromFilePath(null).Status);
        Assert.AreEqual(SolutionDiscoveryHelper.DiscoveryStatus.None,
            SolutionDiscoveryHelper.TryDiscoverFromFilePath(Args(("query", "Foo"))).Status);
    }

    [TestMethod]
    public void TryDiscoverFromFilePath_AcceptsPathAndFileKeysToo()
    {
        var sln = Path.Combine(_root, "Sample.slnx");
        File.WriteAllText(sln, "<Solution />");
        var source = Path.Combine(_root, "Class1.cs");
        File.WriteAllText(source, "// source");

        Assert.AreEqual(sln, SolutionDiscoveryHelper.TryDiscoverFromFilePath(Args(("path", source))).UniquePath);
        Assert.AreEqual(sln, SolutionDiscoveryHelper.TryDiscoverFromFilePath(Args(("file", source))).UniquePath);
    }

    [TestMethod]
    public void TryDiscoverFromFilePath_SolutionBeyondDepthCap_IsNone()
    {
        // solutiondiscoveryhelper-hotpath-perf: the ancestor walk-up is capped at 8 hops. Nest the
        // source file 9 directory levels below the root and place the only solution at the root, so
        // reaching it would require a 9th hop. The cap must stop the walk first → None (an uncapped
        // walk would find the solution and return Unique).
        var deep = _root;
        for (var i = 1; i <= 9; i++)
        {
            deep = Path.Combine(deep, "l" + i);
        }

        Directory.CreateDirectory(deep);
        File.WriteAllText(Path.Combine(_root, "Sample.slnx"), "<Solution />");
        var source = Path.Combine(deep, "Class1.cs");
        File.WriteAllText(source, "// source");

        var result = SolutionDiscoveryHelper.TryDiscoverFromFilePath(Args(("filePath", source)));

        Assert.AreEqual(SolutionDiscoveryHelper.DiscoveryStatus.None, result.Status,
            "A solution more than 8 ancestor hops above the anchor file must not be discovered.");
    }

    // ── scan seam (query-anchored core) ─────────────────────────────────────

    [TestMethod]
    public void ScanDirectoriesForSolutions_FindsTopLevelAndOneLevelDown()
    {
        var sub = Path.Combine(_root, "nested");
        Directory.CreateDirectory(sub);
        var deep = Path.Combine(sub, "deeper");
        Directory.CreateDirectory(deep);

        File.WriteAllText(Path.Combine(sub, "Found.slnx"), "<Solution />");
        File.WriteAllText(Path.Combine(deep, "TooDeep.slnx"), "<Solution />"); // 2 levels down — must NOT be found

        var result = SolutionDiscoveryHelper.ScanDirectoriesForSolutions(new[] { _root }, CancellationToken.None);

        Assert.AreEqual(SolutionDiscoveryHelper.DiscoveryStatus.Unique, result.Status,
            "Only the top-level + one-level-down .slnx should be discovered; deeper trees are out of bounds.");
        StringAssert.Contains(result.UniquePath!, "Found.slnx");
    }

    [TestMethod]
    public void ScanDirectoriesForSolutions_TwoDistinct_IsAmbiguous()
    {
        File.WriteAllText(Path.Combine(_root, "Top.slnx"), "<Solution />");
        var sub = Path.Combine(_root, "child");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "Child.slnx"), "<Solution />");

        var result = SolutionDiscoveryHelper.ScanDirectoriesForSolutions(new[] { _root }, CancellationToken.None);

        Assert.AreEqual(SolutionDiscoveryHelper.DiscoveryStatus.Ambiguous, result.Status);
        Assert.AreEqual(2, result.Candidates.Count);
    }

    [TestMethod]
    public void ScanDirectoriesForSolutions_EmptyOrMissing_IsNone()
    {
        Assert.AreEqual(SolutionDiscoveryHelper.DiscoveryStatus.None,
            SolutionDiscoveryHelper.ScanDirectoriesForSolutions(new[] { _root }, CancellationToken.None).Status);
        Assert.AreEqual(SolutionDiscoveryHelper.DiscoveryStatus.None,
            SolutionDiscoveryHelper.ScanDirectoriesForSolutions(
                new[] { Path.Combine(_root, "does-not-exist") }, CancellationToken.None).Status);
    }

    // ── TTL cache around the root scan ──────────────────────────────────────

    [TestMethod]
    public void ScanDirectoriesForSolutionsCached_WithinTtl_ReturnsStaleResult()
    {
        // solutiondiscoveryhelper-hotpath-perf: the cached wrapper memoizes the root scan for a
        // short TTL keyed by the root set. Prime the cache with a single-solution result, then add
        // a second solution to the same root: a fresh scan would now be Ambiguous, but a cache hit
        // within the TTL must return the original (stale) Unique result — proving the second call
        // did not re-walk the tree.
        File.WriteAllText(Path.Combine(_root, "First.slnx"), "<Solution />");
        var roots = new[] { _root };

        var first = SolutionDiscoveryHelper.ScanDirectoriesForSolutionsCached(roots, CancellationToken.None);
        Assert.AreEqual(SolutionDiscoveryHelper.DiscoveryStatus.Unique, first.Status);

        File.WriteAllText(Path.Combine(_root, "Second.slnx"), "<Solution />");

        var second = SolutionDiscoveryHelper.ScanDirectoriesForSolutionsCached(roots, CancellationToken.None);
        Assert.AreEqual(SolutionDiscoveryHelper.DiscoveryStatus.Unique, second.Status,
            "A cache hit within the TTL must reuse the prior scan; the newly-added second solution " +
            "must not turn the cached Unique result into Ambiguous until the TTL expires.");
        Assert.AreEqual(first.UniquePath, second.UniquePath);
    }

    [TestMethod]
    public void ScanDirectoriesForSolutionsCached_EmptyRoots_IsNone()
    {
        Assert.AreEqual(SolutionDiscoveryHelper.DiscoveryStatus.None,
            SolutionDiscoveryHelper.ScanDirectoriesForSolutionsCached(
                Array.Empty<string>(), CancellationToken.None).Status);
    }

    [TestMethod]
    public async Task TryDiscoverAsync_NullServerAndNoFilePath_IsNone()
    {
        // No filePath argument and no server (so no roots) → discovery yields nothing, leaving the
        // caller to fall back to the binder/elicitation path rather than guessing.
        var result = await SolutionDiscoveryHelper.TryDiscoverAsync(
            Args(("query", "Foo")), server: null, CancellationToken.None);
        Assert.AreEqual(SolutionDiscoveryHelper.DiscoveryStatus.None, result.Status);
    }

    private static Dictionary<string, JsonElement> Args(params (string Key, string Value)[] pairs)
    {
        var dict = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (key, value) in pairs)
        {
            dict[key] = JsonSerializer.SerializeToElement(value);
        }

        return dict;
    }
}
