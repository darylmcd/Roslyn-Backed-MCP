namespace RoslynMcp.Tests;

/// <summary>
/// Regression for <c>get-syntax-tree-range-truncates-at-statement</c>: the original
/// implementation used <c>TextSpan.Contains(n.Span)</c> which silently dropped any
/// top-level node whose span was not fully within the requested line range. The fix
/// changes the predicate to <c>TextSpan.OverlapsWith(n.Span)</c> so that nodes which
/// start before <c>startLine</c> but end within the range (or start within the range
/// but end after <c>endLine</c>) are correctly included.
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class GetSyntaxTreeRangeOverlapTests : SharedWorkspaceTestBase
{
    private static string WorkspaceId { get; set; } = null!;
    private static string AnimalServicePath { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        WorkspaceId = await GetOrLoadWorkspaceIdAsync(SampleSolutionPath, CancellationToken.None);

        // AnimalService.cs has multiple methods starting at different lines which makes
        // it ideal for testing partial-overlap: a method that starts before startLine
        // but ends inside the range would be dropped by Contains but kept by OverlapsWith.
        AnimalServicePath = WorkspaceManager.GetCurrentSolution(WorkspaceId)
            .Projects.SelectMany(p => p.Documents)
            .First(d => d.Name.Equals("AnimalService.cs", StringComparison.OrdinalIgnoreCase))
            .FilePath!;
    }

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    /// <summary>
    /// Requests lines 20-27 of AnimalService.cs.
    /// MakeThemSpeak() starts at line 16 and ends at line 23, so its span overlaps the
    /// requested range [20, 27] but is NOT fully contained within it.
    /// With the old Contains predicate the method would be silently dropped;
    /// with OverlapsWith it must appear in the returned children.
    /// </summary>
    [TestMethod]
    public async Task GetSyntaxTree_Range_IncludesNodeThatStartsBeforeStartLine()
    {
        // AnimalService.cs (1-based):
        //   line 16: public void MakeThemSpeak(...)   <-- starts before our range
        //   line 20:     foreach (var animal in animals)
        //   line 23:     }
        //   line 24: }                                 <-- MakeThemSpeak ends here
        //   line 25: public int CountAnimals(List<IAnimal> ...)
        //   line 27:     return animals.Count;
        // We request [20, 27] so MakeThemSpeak overlaps from the left.
        const int startLine = 20;
        const int endLine = 27;

        var result = await SyntaxService.GetSyntaxTreeAsync(
            WorkspaceId, AnimalServicePath,
            startLine: startLine, endLine: endLine,
            maxDepth: 1, CancellationToken.None);

        Assert.IsNotNull(result, "expected a non-null result for a valid range");

        // At least one child should be a method declaration that starts before line 20
        // (MakeThemSpeak starts at line 16). With Contains that node is dropped; with
        // OverlapsWith it must be present.
        var children = result.Children ?? [];
        Assert.IsTrue(children.Count > 0, "expected at least one child node in range [20, 27]");

        var hasOverlappingNode = children.Any(c => c.StartLine < startLine);
        Assert.IsTrue(hasOverlappingNode,
            $"expected at least one child whose StartLine < {startLine} (overlapping from left), " +
            $"but all children start at or after line {startLine}. " +
            $"Children: [{string.Join(", ", children.Select(c => $"{c.Kind}@{c.StartLine}"))}]. " +
            $"This indicates the old span.Contains predicate is still in use.");
    }

    /// <summary>
    /// Full-file call (no startLine/endLine) must continue to work and return the class
    /// declaration as the root node.
    /// </summary>
    [TestMethod]
    public async Task GetSyntaxTree_NoRange_ReturnsFullTree()
    {
        var result = await SyntaxService.GetSyntaxTreeAsync(
            WorkspaceId, AnimalServicePath,
            startLine: null, endLine: null,
            maxDepth: 2, CancellationToken.None);

        Assert.IsNotNull(result, "expected a non-null result for full-file call");
        Assert.AreNotEqual("TruncationNotice", result.Kind,
            "full-file call on a small file must not truncate at maxDepth=2 with default budgets");
    }
}
