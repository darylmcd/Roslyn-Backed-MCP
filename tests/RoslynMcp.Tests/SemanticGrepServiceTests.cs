using System.Text.Json;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

[DoNotParallelize]
[TestClass]
public sealed class SemanticGrepServiceTests : SharedWorkspaceTestBase
{
    private static string WorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        WorkspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);
    }

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task SemanticGrep_Identifiers_FindsConsoleInvocationsButNotStringContent()
    {
        // AnimalService.cs contains `Console.WriteLine($"{animal.Name} says {sound}")` —
        // identifier scope must hit `Console`, but must NOT hit a string literal that
        // happens to contain the word "Console".
        var hits = await SemanticGrepService.SearchAsync(
            WorkspaceId, @"^Console$", "identifiers", projectFilter: null, limit: 500, CancellationToken.None);

        Assert.IsTrue(hits.Count >= 1, $"Expected at least one identifier hit for 'Console', got {hits.Count}.");
        Assert.IsTrue(hits.All(h => h.TokenKind == "identifier"),
            $"All hits must be identifier-kind. Got: {string.Join(",", hits.Select(h => h.TokenKind).Distinct())}");
        Assert.IsTrue(hits.Any(h => h.FilePath.EndsWith("AnimalService.cs", StringComparison.OrdinalIgnoreCase)),
            "Expected an identifier hit for 'Console' in AnimalService.cs.");

        // 1-based line/column.
        foreach (var h in hits)
        {
            Assert.IsTrue(h.Line >= 1, $"Line number must be 1-based; got {h.Line}.");
            Assert.IsTrue(h.Column >= 1, $"Column number must be 1-based; got {h.Column}.");
        }
    }

    [TestMethod]
    public async Task SemanticGrep_Strings_FindsLiteralContentButNotIdentifiers()
    {
        // AnimalService.cs has the interpolated string with text " says " between holes.
        // String scope should hit the literal text, not the surrounding identifiers.
        var hits = await SemanticGrepService.SearchAsync(
            WorkspaceId, @"says", "strings", projectFilter: null, limit: 500, CancellationToken.None);

        Assert.IsTrue(hits.All(h => h.TokenKind == "string"),
            $"All hits must be string-kind. Got: {string.Join(",", hits.Select(h => h.TokenKind).Distinct())}");
        Assert.IsTrue(hits.Any(h => h.FilePath.EndsWith("AnimalService.cs", StringComparison.OrdinalIgnoreCase)),
            "Expected a string-literal hit for 'says' in AnimalService.cs.");
    }

    [TestMethod]
    public async Task SemanticGrep_Comments_DoesNotMatchIdentifiersOrStrings()
    {
        // Run identifiers + strings scope and assert NO comment-kind hits leak in.
        var idHits = await SemanticGrepService.SearchAsync(
            WorkspaceId, @"Animal", "identifiers", projectFilter: null, limit: 500, CancellationToken.None);
        Assert.IsTrue(idHits.All(h => h.TokenKind == "identifier"),
            "Identifier-scope must not return string or comment hits.");

        // Comment scope should never return identifier-kind hits.
        var commentHits = await SemanticGrepService.SearchAsync(
            WorkspaceId, @".+", "comments", projectFilter: null, limit: 500, CancellationToken.None);
        Assert.IsTrue(commentHits.All(h => h.TokenKind == "comment"),
            "Comment-scope must only return comment-kind hits.");
    }

    [TestMethod]
    public async Task SemanticGrep_All_UnionsAllThreeBuckets()
    {
        var allHits = await SemanticGrepService.SearchAsync(
            WorkspaceId, @"Animal", "all", projectFilter: null, limit: 500, CancellationToken.None);
        var idHits = await SemanticGrepService.SearchAsync(
            WorkspaceId, @"Animal", "identifiers", projectFilter: null, limit: 500, CancellationToken.None);

        Assert.IsTrue(allHits.Count >= idHits.Count,
            $"Scope='all' must return at least as many hits as 'identifiers'. all={allHits.Count}, identifiers={idHits.Count}.");

        var distinctKinds = allHits.Select(h => h.TokenKind).Distinct().ToList();
        foreach (var kind in distinctKinds)
        {
            CollectionAssert.Contains(new[] { "identifier", "string", "comment" }, kind,
                $"Unexpected token kind '{kind}' under scope='all'.");
        }
    }

    [TestMethod]
    public async Task SemanticGrep_Limit_CapsResultCount()
    {
        var capped = await SemanticGrepService.SearchAsync(
            WorkspaceId, @".", "identifiers", projectFilter: null, limit: 5, CancellationToken.None);
        Assert.IsTrue(capped.Count <= 5, $"limit=5 must cap result list; got {capped.Count}.");
    }

    [TestMethod]
    public async Task SemanticGrep_InvalidScope_Throws()
    {
        await Assert.ThrowsExceptionAsync<ArgumentException>(
            () => SemanticGrepService.SearchAsync(
                WorkspaceId, @"x", "definitely-not-a-scope", projectFilter: null, limit: 10, CancellationToken.None));
    }

    [TestMethod]
    public async Task SemanticGrep_EmptyPattern_Throws()
    {
        await Assert.ThrowsExceptionAsync<ArgumentException>(
            () => SemanticGrepService.SearchAsync(
                WorkspaceId, "", "identifiers", projectFilter: null, limit: 10, CancellationToken.None));
    }

    [TestMethod]
    public async Task SemanticGrep_InvalidRegex_Throws()
    {
        await Assert.ThrowsExceptionAsync<ArgumentException>(
            () => SemanticGrepService.SearchAsync(
                WorkspaceId, "(unclosed", "identifiers", projectFilter: null, limit: 10, CancellationToken.None));
    }

    // ----- Pagination envelope tests (PR for compact-semantic-grep-pagination, gh #760 split) -----
    //
    // These tests exercise the wrapper layer in AnalysisTools.SemanticGrep, not the service.
    // The service still hard-caps collection at `limit`; the wrapper slices the returned list
    // via Skip(offset).Take(limit) and emits the { count, totalCount, hasMore, offset, limit,
    // items } envelope. Mirrors the contract established by FindReflectionUsages and FindTypeUsages.

    /// <summary>
    /// Bounded-collection contract: when offset=0 and limit &lt; the actual hit count the
    /// response must report hasMore=true, count==limit, and a totalCount strictly greater
    /// than count. Uses the "." all-identifier pattern, which on the shared sample workspace
    /// always produces a totalCount well above 5 (most identifier tokens are single-character-
    /// or-longer, so they match ".").
    /// </summary>
    [TestMethod]
    public async Task SemanticGrep_WrapperLimitSmallerThanTotal_ReportsHasMoreAndPagedCount()
    {
        const int Limit = 5;

        var json = await AnalysisTools.SemanticGrep(
            gate: WorkspaceExecutionGate,
            semanticGrepService: SemanticGrepService,
            workspaceId: WorkspaceId,
            pattern: ".",
            scope: "identifiers",
            projectName: null,
            limit: Limit,
            offset: 0,
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var count = root.GetProperty("count").GetInt32();
        var totalCount = root.GetProperty("totalCount").GetInt32();
        var hasMore = root.GetProperty("hasMore").GetBoolean();

        Assert.AreEqual(Limit, count, "count must equal the requested limit when totalCount > limit.");
        Assert.IsTrue(totalCount > Limit,
            $"Sample workspace totalCount ({totalCount}) must exceed Limit ({Limit}) for this test to be meaningful.");
        Assert.IsTrue(hasMore, "hasMore must be true when totalCount > offset + count.");

        // Echoed paging knobs let callers route follow-up requests off the same envelope shape.
        Assert.AreEqual(0, root.GetProperty("offset").GetInt32());
        Assert.AreEqual(Limit, root.GetProperty("limit").GetInt32());

        // items array must contain exactly `count` entries.
        var items = root.GetProperty("items");
        Assert.AreEqual(count, items.GetArrayLength(),
            "items.Length must equal count (the paged slice size).");
    }

    /// <summary>
    /// Pagination is deterministic: the first window (offset=0, limit=N) and a second
    /// window (offset=N, limit=N) must return different first items. Service results are
    /// sorted by ascending file/line/column, so the items[0] of the two windows must differ
    /// whenever totalCount &gt; N. Both windows must report the same totalCount.
    /// </summary>
    [TestMethod]
    public async Task SemanticGrep_WrapperDifferentOffsets_ReturnDifferentSlicesSameTotal()
    {
        const int Limit = 5;

        var firstJson = await AnalysisTools.SemanticGrep(
            gate: WorkspaceExecutionGate,
            semanticGrepService: SemanticGrepService,
            workspaceId: WorkspaceId,
            pattern: ".",
            scope: "identifiers",
            projectName: null,
            limit: Limit,
            offset: 0,
            ct: CancellationToken.None);

        var secondJson = await AnalysisTools.SemanticGrep(
            gate: WorkspaceExecutionGate,
            semanticGrepService: SemanticGrepService,
            workspaceId: WorkspaceId,
            pattern: ".",
            scope: "identifiers",
            projectName: null,
            limit: Limit,
            offset: Limit,
            ct: CancellationToken.None);

        using var first = JsonDocument.Parse(firstJson);
        using var second = JsonDocument.Parse(secondJson);

        var firstTotal = first.RootElement.GetProperty("totalCount").GetInt32();
        var secondTotal = second.RootElement.GetProperty("totalCount").GetInt32();
        Assert.AreEqual(firstTotal, secondTotal,
            "totalCount must be stable across paging requests against the same workspace.");

        Assert.AreEqual(0, first.RootElement.GetProperty("offset").GetInt32());
        Assert.AreEqual(Limit, second.RootElement.GetProperty("offset").GetInt32());

        // items[0] of the two windows must differ since the service returns results sorted
        // deterministically and totalCount > 2*Limit.
        Assert.IsTrue(firstTotal > 2 * Limit,
            $"Sample workspace totalCount ({firstTotal}) must exceed 2*Limit ({2 * Limit}) for the disjoint-window assertion.");

        var firstItems = first.RootElement.GetProperty("items");
        var secondItems = second.RootElement.GetProperty("items");
        Assert.IsTrue(firstItems.GetArrayLength() > 0);
        Assert.IsTrue(secondItems.GetArrayLength() > 0);

        var firstItem0 = firstItems[0].GetRawText();
        var secondItem0 = secondItems[0].GetRawText();
        Assert.AreNotEqual(firstItem0, secondItem0,
            "offset=0 and offset=Limit windows must surface different first items (deterministic file/line/column sort).");
    }

    // ----- Tokenization-rule regression tests (semantic-grep-dotted-identifiers-tokenization-docs-gap, gh #768 §13.15) -----
    //
    // The `identifiers` scope walks SyntaxKind.IdentifierToken one token at a time, so a dotted
    // member-access expression like `Console.WriteLine` is THREE tokens — `Console`, `.`, `WriteLine`
    // — and no single identifier token's text contains both halves. A regex pattern that spans
    // the dot will therefore match 0 results under `scope="identifiers"`, regardless of how many
    // times the call site appears in source. The tool description now documents this explicitly;
    // these tests pin the behaviour so a future "fix" that changes the tokenization semantics
    // breaks loudly instead of silently shifting hit counts. The recoverable-path test exercises
    // the documented two-identifier-call + line-intersection workaround.

    /// <summary>
    /// Tokenization gap: `pattern="Console\.WriteLine"` against the `identifiers` scope must
    /// return 0 hits even though `Console.WriteLine($"...")` appears multiple times in the
    /// sample workspace's Program.cs / AnimalService.cs. The dot is a punctuation token; the
    /// two identifiers either side of it are separate IdentifierToken instances.
    /// </summary>
    [TestMethod]
    public async Task SemanticGrep_DottedPattern_IdentifierScope_ReturnsZero()
    {
        var hits = await SemanticGrepService.SearchAsync(
            WorkspaceId, @"Console\.WriteLine", "identifiers", projectFilter: null, limit: 500, CancellationToken.None);

        Assert.AreEqual(0, hits.Count,
            $"Dotted pattern under scope=\"identifiers\" must match 0 — identifier tokens never span the dot. " +
            $"Got {hits.Count} hits: {string.Join(", ", hits.Select(h => $"{h.FilePath}:{h.Line}:{h.Column}"))}.");
    }

    /// <summary>
    /// Documented recoverable path: split the dotted query into two independent identifier-scope
    /// calls (`^Console$`, `^WriteLine$`) and intersect the hits client-side by (filePath, line).
    /// The shared sample workspace's Program.cs has `Console.WriteLine(...)` on multiple lines,
    /// so the intersection must contain at least one (file, line) pair. This is the workaround
    /// users are pointed at by the updated tool description.
    /// </summary>
    [TestMethod]
    public async Task SemanticGrep_DottedPattern_TwoIdentifierCallsIntersected_RecoversDottedCallSites()
    {
        var consoleHits = await SemanticGrepService.SearchAsync(
            WorkspaceId, @"^Console$", "identifiers", projectFilter: null, limit: 500, CancellationToken.None);
        var writeLineHits = await SemanticGrepService.SearchAsync(
            WorkspaceId, @"^WriteLine$", "identifiers", projectFilter: null, limit: 500, CancellationToken.None);

        Assert.IsTrue(consoleHits.Count >= 1,
            $"Sanity: identifier scope must find `Console` in the sample workspace. Got {consoleHits.Count}.");
        Assert.IsTrue(writeLineHits.Count >= 1,
            $"Sanity: identifier scope must find `WriteLine` in the sample workspace. Got {writeLineHits.Count}.");

        // Intersect by (filePath, line) — the documented client-side recovery for dotted call sites.
        var consoleLines = consoleHits.Select(h => (h.FilePath, h.Line)).ToHashSet();
        var sharedLines = writeLineHits.Where(h => consoleLines.Contains((h.FilePath, h.Line))).ToList();

        Assert.IsTrue(sharedLines.Count >= 1,
            $"Documented recovery (`^Console$` ∩ `^WriteLine$` by (filePath, line)) must surface at least one " +
            $"`Console.WriteLine` call site. Console hits: {consoleHits.Count}, WriteLine hits: {writeLineHits.Count}, " +
            $"intersection: {sharedLines.Count}.");
    }
}
