namespace RoslynMcp.Tests;

[TestClass]
public sealed class RefactoringSuggestionTests : SharedWorkspaceTestBase
{
    private static string SampleWorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        SampleWorkspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);
    }

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task SuggestRefactorings_ReturnsResults()
    {
        var suggestions = await RefactoringSuggestionService.SuggestRefactoringsAsync(
            SampleWorkspaceId, projectFilter: null, limit: 50, CancellationToken.None);

        Assert.IsNotNull(suggestions);
        // Sample solution is small and clean — may or may not have suggestions
        // but the call should succeed without error
        foreach (var s in suggestions)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(s.Category));
            Assert.IsFalse(string.IsNullOrWhiteSpace(s.Severity));
            Assert.IsFalse(string.IsNullOrWhiteSpace(s.TargetSymbol));
            Assert.IsTrue(s.RecommendedTools.Count > 0);
        }
    }

    [TestMethod]
    public async Task SuggestRefactorings_RespectLimit()
    {
        var suggestions = await RefactoringSuggestionService.SuggestRefactoringsAsync(
            SampleWorkspaceId, projectFilter: null, limit: 2, CancellationToken.None);

        Assert.IsTrue(suggestions.Count <= 2);
    }
}
