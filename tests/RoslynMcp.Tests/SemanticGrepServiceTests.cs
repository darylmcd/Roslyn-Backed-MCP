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
}
