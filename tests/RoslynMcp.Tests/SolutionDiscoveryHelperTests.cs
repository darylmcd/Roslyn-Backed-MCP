using System.Text.Json;
using RoslynMcp.Host.Stdio.Middleware;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Coverage for the <c>workspace-auto-load-on-demand</c> initiative's discovery layer
/// (<see cref="SolutionDiscoveryHelper"/>). Pins the deterministic, filesystem-driven strategies:
/// file-anchored walk-up (solution preferred over project), the conservative unique/ambiguous/none
/// classification, the bounded top-level + one-level scan used by query-anchored discovery, and the
/// <c>filePath</c>-like argument extraction. Query-anchored discovery is driven by explicit
/// <see cref="SecurityOptions.SanctionedRoots"/> and never depends on client capabilities.
/// </summary>
[TestClass]
public sealed class SolutionDiscoveryHelperTests
{
    private string _root = null!;

    [TestInitialize]
    public void Init()
    {
        _root = Path.Combine(TestTempRoot.Current, "rmcp-disc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        TestFixtureFileSystem.DeleteDirectoryIfExists(_root);
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

    [TestMethod]
    public void ScanDirectoriesForSolutions_DoesNotFollowLinkedChildOutsideConfiguredRoot()
    {
        var outsideRoot = Path.Combine(TestTempRoot.Current, "rmcp-disc-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outsideRoot);
        File.WriteAllText(Path.Combine(outsideRoot, "Outside.slnx"), "<Solution />");
        var linkedChild = Path.Combine(_root, "linked-child");

        try
        {
            if (!TestFixtureFileSystem.TryCreateDirectoryLink(linkedChild, outsideRoot))
            {
                Assert.Inconclusive("Directory links are unavailable in this test environment.");
                return;
            }

            var result = SolutionDiscoveryHelper.ScanDirectoriesForSolutions(
                [_root],
                CancellationToken.None);

            Assert.AreEqual(SolutionDiscoveryHelper.DiscoveryStatus.None, result.Status,
                "Query discovery must not follow a linked child beyond the configured physical root.");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(_root);
            TestFixtureFileSystem.DeleteDirectoryIfExists(outsideRoot);
        }
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

    [TestMethod]
    public async Task TryDiscoverAsync_QueryOnly_UsesConfiguredRoots()
    {
        var solution = Path.Combine(_root, "Configured.slnx");
        File.WriteAllText(solution, "<Solution />");

        var result = await SolutionDiscoveryHelper.TryDiscoverAsync(
            Args(("query", "Foo")),
            server: null,
            cancellationToken: CancellationToken.None,
            securityOptions: new SecurityOptions { SanctionedRoots = [_root] });

        Assert.AreEqual(SolutionDiscoveryHelper.DiscoveryStatus.Unique, result.Status);
        Assert.AreEqual(solution, result.UniquePath);
    }

    [TestMethod]
    public async Task TryDiscoverAsync_QueryOnly_ConfiguredRootsPreserveAmbiguousAndEmptyResults()
    {
        var emptyRoot = Path.Combine(_root, "empty");
        var ambiguousRoot = Path.Combine(_root, "ambiguous");
        Directory.CreateDirectory(emptyRoot);
        Directory.CreateDirectory(ambiguousRoot);
        File.WriteAllText(Path.Combine(ambiguousRoot, "One.slnx"), "<Solution />");
        File.WriteAllText(Path.Combine(ambiguousRoot, "Two.slnx"), "<Solution />");

        var empty = await SolutionDiscoveryHelper.TryDiscoverAsync(
            Args(("query", "Foo")),
            server: null,
            cancellationToken: CancellationToken.None,
            securityOptions: new SecurityOptions { SanctionedRoots = [emptyRoot] });
        var ambiguous = await SolutionDiscoveryHelper.TryDiscoverAsync(
            Args(("query", "Foo")),
            server: null,
            cancellationToken: CancellationToken.None,
            securityOptions: new SecurityOptions { SanctionedRoots = [ambiguousRoot] });

        Assert.AreEqual(SolutionDiscoveryHelper.DiscoveryStatus.None, empty.Status);
        Assert.AreEqual(SolutionDiscoveryHelper.DiscoveryStatus.Ambiguous, ambiguous.Status);
        Assert.HasCount(2, ambiguous.Candidates);
    }

    [TestMethod]
    public async Task TryDiscoverAsync_FileAnchorOutsideConfiguredRoot_ReturnsNoneWithoutCandidateLeak()
    {
        var configuredRoot = Path.Combine(_root, "configured");
        var outsideRoot = Path.Combine(TestTempRoot.Current, "rmcp-disc-anchor-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(configuredRoot);
        Directory.CreateDirectory(outsideRoot);
        File.WriteAllText(Path.Combine(outsideRoot, "Outside.slnx"), "<Solution />");
        var outsideFile = Path.Combine(outsideRoot, "Class1.cs");
        File.WriteAllText(outsideFile, "// outside source");

        try
        {
            var result = await SolutionDiscoveryHelper.TryDiscoverAsync(
                Args(("filePath", outsideFile)),
                server: null,
                cancellationToken: CancellationToken.None,
                securityOptions: new SecurityOptions { SanctionedRoots = [configuredRoot] });

            Assert.AreEqual(SolutionDiscoveryHelper.DiscoveryStatus.None, result.Status);
            Assert.IsEmpty(result.Candidates,
                "An out-of-bound anchor must not enumerate or disclose nearby solution candidates.");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(outsideRoot);
        }
    }

    [TestMethod]
    public async Task TryDiscoverAsync_FileAnchorInsideConfiguredRoot_RemainsUnique()
    {
        var solution = Path.Combine(_root, "Inside.slnx");
        var source = Path.Combine(_root, "src", "Class1.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(solution, "<Solution />");
        File.WriteAllText(source, "// inside source");

        var result = await SolutionDiscoveryHelper.TryDiscoverAsync(
            Args(("filePath", source)),
            server: null,
            cancellationToken: CancellationToken.None,
            securityOptions: new SecurityOptions { SanctionedRoots = [_root] });

        Assert.AreEqual(SolutionDiscoveryHelper.DiscoveryStatus.Unique, result.Status);
        Assert.AreEqual(solution, result.UniquePath);
    }

    [TestMethod]
    public async Task TryDiscoverAsync_FileAnchor_DoesNotWalkAboveConfiguredRoot()
    {
        var configuredRoot = Path.Combine(_root, "configured", "src");
        var source = Path.Combine(configuredRoot, "nested", "Class1.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, "// inside source");
        File.WriteAllText(Path.Combine(_root, "OutsideBoundary.slnx"), "<Solution />");

        var result = await SolutionDiscoveryHelper.TryDiscoverAsync(
            Args(("filePath", source)),
            server: null,
            cancellationToken: CancellationToken.None,
            securityOptions: new SecurityOptions { SanctionedRoots = [configuredRoot] });

        Assert.AreEqual(SolutionDiscoveryHelper.DiscoveryStatus.None, result.Status);
        Assert.IsEmpty(
            result.Candidates,
            "File-anchored discovery must stop at the configured boundary instead of enumerating parents above it.");
    }

    [TestMethod]
    public async Task TryDiscoverAsync_LinkedSolutionFileOutsideBoundary_IsNotDisclosedOrSelected()
    {
        var source = Path.Combine(_root, "src", "Class1.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, "// inside source");

        var outsideRoot = Path.Combine(TestTempRoot.Current, "rmcp-disc-linked-solution-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outsideRoot);
        var outsideSolution = Path.Combine(outsideRoot, "Outside.slnx");
        File.WriteAllText(outsideSolution, "<Solution />");
        var linkedSolution = Path.Combine(_root, "Linked.slnx");

        try
        {
            if (!TestFixtureFileSystem.TryCreateFileLink(linkedSolution, outsideSolution))
            {
                Assert.Inconclusive("File links are unavailable in this test environment.");
                return;
            }

            var fileAnchored = await SolutionDiscoveryHelper.TryDiscoverAsync(
                Args(("filePath", source)),
                server: null,
                cancellationToken: CancellationToken.None,
                securityOptions: new SecurityOptions { SanctionedRoots = [_root] });
            var queryAnchored = await SolutionDiscoveryHelper.TryDiscoverAsync(
                Args(("query", "Foo")),
                server: null,
                cancellationToken: CancellationToken.None,
                securityOptions: new SecurityOptions { SanctionedRoots = [_root] });

            Assert.AreEqual(SolutionDiscoveryHelper.DiscoveryStatus.None, fileAnchored.Status);
            Assert.AreEqual(SolutionDiscoveryHelper.DiscoveryStatus.None, queryAnchored.Status);
            Assert.IsEmpty(fileAnchored.Candidates);
            Assert.IsEmpty(queryAnchored.Candidates);
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(outsideRoot);
        }
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
