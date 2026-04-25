namespace RoslynMcp.Tests;

/// <summary>
/// Covers the PR 3 <c>semantic-search-zero-results-verbose-query</c> fallback work:
///   * A verbose natural-language query ("async methods returning Task&lt;bool&gt; inside
///     the Firewall namespace") must decompose into stopword-filtered tokens.
///   * When structured predicates find nothing, the token-OR fallback should match
///     symbols whose names contain any token.
///   * The Debug payload must always be populated so callers can see the parsed tokens,
///     applied predicates, and fallback strategy.
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class SemanticSearchFallbackTests : SharedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task SemanticSearch_StructuredQuery_ExposesAppliedPredicatesInDebug()
    {
        var workspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);
        var response = await CodePatternAnalyzer.SemanticSearchAsync(
            workspaceId, "async methods", "SampleLib", 50, CancellationToken.None);

        Assert.IsNotNull(response.Debug);
        Assert.AreEqual("structured", response.Debug!.FallbackStrategy);
        CollectionAssert.Contains(response.Debug.AppliedPredicates.ToList(), "keyword:async");
    }

    [TestMethod]
    public async Task SemanticSearch_AsynchronousQuery_DoesNotTreatAsyncAsKeyword()
    {
        var workspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);
        var response = await CodePatternAnalyzer.SemanticSearchAsync(
            workspaceId, "asynchronous methods", "SampleLib", 50, CancellationToken.None);

        Assert.IsNotNull(response.Debug);
        Assert.AreEqual("structured", response.Debug!.FallbackStrategy);
        CollectionAssert.DoesNotContain(response.Debug.AppliedPredicates.ToList(), "keyword:async");
        CollectionAssert.Contains(response.Debug.AppliedPredicates.ToList(), "keyword:method");
    }

    [TestMethod]
    public async Task SemanticSearch_VerboseNaturalLanguageQuery_FallsBackToTokenOrMatch()
    {
        var workspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);

        // This verbose query is designed to fail structured parsing (no "async", no "returning",
        // no "implementing" keywords that match a sample symbol). The token-OR fallback should
        // extract "Dog" from the query and match the SampleLib.Dog type.
        var response = await CodePatternAnalyzer.SemanticSearchAsync(
            workspaceId,
            "tell me about the Dog animal type inside the SampleLib namespace please",
            "SampleLib",
            50,
            CancellationToken.None);

        Assert.IsNotNull(response.Debug);
        Assert.IsTrue(response.Results.Count > 0, "Token-OR fallback should surface at least one match for 'Dog'.");
        Assert.AreEqual("token-or-match", response.Debug!.FallbackStrategy);
        StringAssert.Contains(
            response.Warning ?? string.Empty,
            "token-or name fallback",
            "Warning should explain the fallback strategy.");

        // Token list must have the stopwords stripped.
        CollectionAssert.Contains(response.Debug.ParsedTokens.ToList(), "Dog");
        CollectionAssert.DoesNotContain(response.Debug.ParsedTokens.ToList(), "the");
        CollectionAssert.DoesNotContain(response.Debug.ParsedTokens.ToList(), "inside");
        CollectionAssert.DoesNotContain(response.Debug.ParsedTokens.ToList(), "methods");
    }

    [TestMethod]
    public async Task SemanticSearch_NoHits_PopulatesDebugAndWarning()
    {
        var workspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);

        // Query that no symbol can match, and tokens that aren't substrings of anything.
        var response = await CodePatternAnalyzer.SemanticSearchAsync(
            workspaceId, "xyzzy qwertyzzz", "SampleLib", 50, CancellationToken.None);

        Assert.AreEqual(0, response.Results.Count);
        Assert.IsNotNull(response.Debug);
        Assert.AreEqual("none", response.Debug!.FallbackStrategy);
        Assert.IsNotNull(response.Warning);
        StringAssert.Contains(response.Warning!, "No symbols matched");
    }

    [TestMethod]
    public async Task SemanticSearch_Debug_ParsedTokensDropStopwords()
    {
        var workspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);
        var response = await CodePatternAnalyzer.SemanticSearchAsync(
            workspaceId,
            "return Task<bool> from the method inside the namespace",
            "SampleLib",
            50,
            CancellationToken.None);

        Assert.IsNotNull(response.Debug);

        // "Task" is intentionally preserved (users search for TaskRunner / TaskQueue variants).
        CollectionAssert.Contains(response.Debug!.ParsedTokens.ToList(), "Task");
        CollectionAssert.Contains(response.Debug.ParsedTokens.ToList(), "bool");

        // Noise words must be dropped.
        foreach (var stopword in new[] { "return", "from", "the", "method", "inside", "namespace" })
        {
            CollectionAssert.DoesNotContain(response.Debug.ParsedTokens.ToList(), stopword);
        }
    }

    // ── semantic-search-duplicate-results-and-fallback-signal ──

    /// <summary>
    /// Regression for <c>semantic-search-duplicate-results-and-fallback-signal</c>: partial
    /// classes, multi-targeted projects, and nested-type traversal previously caused the same
    /// symbol to be surfaced twice (identical fully-qualified name + identical SymbolHandle).
    /// The dedupe-by-handle guard added in <c>CollectSemanticSearchMatchesAsync</c> must
    /// guarantee all returned handles are unique for any query.
    /// </summary>
    [TestMethod]
    public async Task SemanticSearch_StructuredQuery_ResultHandlesAreUnique()
    {
        var workspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);

        // Query that exercises the structured path with multiple predicates ("classes" +
        // "implementing interface"), which is the exact scenario the backlog row called out
        // (`semantic_search("classes implementing IDisposable")` used to return JobProcessor
        // twice with identical SymbolHandle).
        var response = await CodePatternAnalyzer.SemanticSearchAsync(
            workspaceId, "classes implementing IDisposable", "SampleLib", 50, CancellationToken.None);

        Assert.IsTrue(response.Results.Count > 0, "Expected at least one IDisposable class.");
        var handles = response.Results.Select(r => r.SymbolHandle).ToList();
        var distinct = handles.Distinct(StringComparer.Ordinal).ToList();
        Assert.AreEqual(handles.Count, distinct.Count,
            "Each semantic-search result must have a unique SymbolHandle (dedupe guard).");
    }

    /// <summary>
    /// Each result must declare which matching strategy produced it via <c>MatchKind</c>.
    /// Structured-predicate hits report "structured".
    /// </summary>
    [TestMethod]
    public async Task SemanticSearch_StructuredQuery_PerResultMatchKindIsStructured()
    {
        var workspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);

        var response = await CodePatternAnalyzer.SemanticSearchAsync(
            workspaceId, "classes implementing IDisposable", "SampleLib", 50, CancellationToken.None);

        Assert.IsTrue(response.Results.Count > 0);
        Assert.IsTrue(
            response.Results.All(r => r.MatchKind == "structured"),
            "All structured-query results must report MatchKind == 'structured'.");
    }

    /// <summary>
    /// Verbose-query fallback path must propagate "token-or-match" as the per-result MatchKind
    /// so the caller can see why a particular row showed up.
    /// </summary>
    [TestMethod]
    public async Task SemanticSearch_VerboseQuery_PerResultMatchKindIsTokenOrMatch()
    {
        var workspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);

        var response = await CodePatternAnalyzer.SemanticSearchAsync(
            workspaceId,
            "tell me about the Dog animal type inside the SampleLib namespace please",
            "SampleLib",
            50,
            CancellationToken.None);

        Assert.IsTrue(response.Results.Count > 0);
        Assert.AreEqual("token-or-match", response.Debug!.FallbackStrategy);
        Assert.IsTrue(
            response.Results.All(r => r.MatchKind == "token-or-match"),
            "All token-or-fallback results must report MatchKind == 'token-or-match'.");
    }
}
