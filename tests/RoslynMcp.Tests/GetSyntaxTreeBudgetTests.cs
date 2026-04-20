namespace RoslynMcp.Tests;

/// <summary>
/// Regression for `get-syntax-tree-max-output-chars-incomplete-cap` (P3): Jellyfin
/// stress test 2026-04-15 §3 reproduced `get_syntax_tree` on EncodingHelper.cs (7889
/// lines) with `maxOutputChars=20000` returning ~229 KB (11× the configured leaf
/// cap) because structural JSON dominated the payload. Two new orthogonal budgets:
/// `maxNodes` caps total node count; `maxTotalBytes` caps estimated total response
/// size. The walker stops at whichever cap is hit first and emits a TruncationNotice.
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class GetSyntaxTreeBudgetTests : SharedWorkspaceTestBase
{
    private static string WorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        WorkspaceId = await GetOrLoadWorkspaceIdAsync(SampleSolutionPath, CancellationToken.None);
    }

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task GetSyntaxTree_MaxNodesCap_StopsAtLimit_AndEmitsTruncationNotice()
    {
        var docPath = WorkspaceManager.GetCurrentSolution(WorkspaceId)
            .Projects.SelectMany(p => p.Documents)
            .FirstOrDefault(d => d.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))?.FilePath;
        Assert.IsNotNull(docPath, "expected at least one .cs document in SampleSolution");

        // Tiny maxNodes cap forces truncation early. Default maxOutputChars/maxTotalBytes
        // are large so the node-count cap is the binding one.
        var result = await SyntaxService.GetSyntaxTreeAsync(
            WorkspaceId, docPath, startLine: null, endLine: null,
            maxDepth: 10, CancellationToken.None,
            maxOutputChars: 1_000_000,
            maxNodes: 5,
            maxTotalBytes: 10_000_000);

        Assert.IsNotNull(result);
        var counted = CountNodes(result);
        // The walker may emit slightly more nodes than maxNodes if a deeper tree is
        // already in flight when the cap trips; the binding constraint is that it stops
        // crisply (small constant overshoot) rather than running to completion.
        Assert.IsTrue(counted < 50, $"expected far fewer than 50 nodes when maxNodes=5; got {counted}");
        Assert.IsTrue(ContainsTruncationNotice(result), "tree must include a TruncationNotice when capped");
    }

    [TestMethod]
    public async Task GetSyntaxTree_MaxTotalBytesCap_StopsAtLimit_AndEmitsTruncationNotice()
    {
        var docPath = WorkspaceManager.GetCurrentSolution(WorkspaceId)
            .Projects.SelectMany(p => p.Documents)
            .FirstOrDefault(d => d.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))?.FilePath;
        Assert.IsNotNull(docPath);

        // Tiny maxTotalBytes (~5 nodes worth at 120 bytes/node overhead) forces early
        // truncation regardless of leaf-text density.
        var result = await SyntaxService.GetSyntaxTreeAsync(
            WorkspaceId, docPath, startLine: null, endLine: null,
            maxDepth: 10, CancellationToken.None,
            maxOutputChars: 1_000_000,
            maxNodes: 100_000,
            maxTotalBytes: 600);

        Assert.IsNotNull(result);
        Assert.IsTrue(ContainsTruncationNotice(result), "tree must include a TruncationNotice when byte-capped");
    }

    [TestMethod]
    public async Task GetSyntaxTree_DefaultBudgets_ProduceCompleteTreeOnSmallFile()
    {
        var docPath = WorkspaceManager.GetCurrentSolution(WorkspaceId)
            .Projects.SelectMany(p => p.Documents)
            .FirstOrDefault(d => d.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))?.FilePath;
        Assert.IsNotNull(docPath);

        var result = await SyntaxService.GetSyntaxTreeAsync(
            WorkspaceId, docPath, startLine: null, endLine: null,
            maxDepth: 3, CancellationToken.None);
        // Defaults (maxOutputChars=65536, maxNodes=5000, maxTotalBytes=65536) easily
        // accommodate any small SampleSolution file at maxDepth=3.

        Assert.IsNotNull(result);
        Assert.IsFalse(ContainsTruncationNotice(result),
            "small file at maxDepth=3 must not trip any default budget");
    }

    private static int CountNodes(RoslynMcp.Core.Models.SyntaxNodeDto node)
    {
        var count = 1;
        if (node.Children is not null)
        {
            foreach (var c in node.Children) count += CountNodes(c);
        }
        return count;
    }

    private static bool ContainsTruncationNotice(RoslynMcp.Core.Models.SyntaxNodeDto node)
    {
        if (node.Kind == "TruncationNotice") return true;
        if (node.Children is null) return false;
        foreach (var c in node.Children)
        {
            if (ContainsTruncationNotice(c)) return true;
        }
        return false;
    }
}
