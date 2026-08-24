namespace RoslynMcp.Tests;

[TestClass]
public sealed class RefactoringSuggestionTests : IsolatedWorkspaceTestBase
{
    private static IsolatedWorkspaceScope? SuggestionWorkspace { get; set; }

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        SuggestionWorkspace = await CreateSuggestionWorkspaceAsync();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        SuggestionWorkspace?.Dispose();
        SuggestionWorkspace = null;
    }

    [TestMethod]
    public async Task SuggestRefactorings_ReturnsResults()
    {
        var suggestions = await RefactoringSuggestionService.SuggestRefactoringsAsync(
            GetSuggestionWorkspaceId(), projectFilter: "SampleLib", limit: 50, CancellationToken.None);

        Assert.IsTrue(suggestions.Any(s =>
                s.Category == "complexity" && s.TargetSymbol == "Evaluate"),
            $"The fixture's deliberately complex Evaluate method must produce a complexity suggestion. " +
            $"Got: [{string.Join(", ", suggestions.Select(s => $"{s.Category}:{s.TargetSymbol}"))}]");
        Assert.IsTrue(suggestions.Any(s =>
                s.Category == "parameter-count" && s.TargetSymbol == "Evaluate"),
            "The fixture's seven-parameter Evaluate method must produce a parameter-count suggestion.");
        foreach (var s in suggestions)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(s.Category));
            Assert.IsFalse(string.IsNullOrWhiteSpace(s.Severity));
            Assert.IsFalse(string.IsNullOrWhiteSpace(s.TargetSymbol));
            Assert.IsTrue(s.RecommendedTools.Count > 0);
        }
    }

    [TestMethod]
    public async Task SuggestRefactorings_RespectsLimit()
    {
        var allSuggestions = await RefactoringSuggestionService.SuggestRefactoringsAsync(
            GetSuggestionWorkspaceId(), projectFilter: "SampleLib", limit: 50, CancellationToken.None);
        var limitedSuggestions = await RefactoringSuggestionService.SuggestRefactoringsAsync(
            GetSuggestionWorkspaceId(), projectFilter: "SampleLib", limit: 1, CancellationToken.None);

        Assert.IsTrue(allSuggestions.Count > 1,
            "The positive fixture must produce more suggestions than the requested limit.");
        Assert.AreEqual(1, limitedSuggestions.Count,
            "A limit of one must truncate a non-empty multi-suggestion result to exactly one item.");
        Assert.AreEqual(allSuggestions[0].Category, limitedSuggestions[0].Category,
            "Limiting must preserve the highest-ranked suggestion category.");
        Assert.AreEqual(allSuggestions[0].TargetSymbol, limitedSuggestions[0].TargetSymbol,
            "Limiting must preserve the highest-ranked suggestion target.");
        Assert.AreEqual(allSuggestions[0].Description, limitedSuggestions[0].Description,
            "Limiting must preserve the highest-ranked suggestion details.");
    }

    [TestMethod]
    public async Task SuggestRefactorings_FacadeType_DoesNotEmitCohesionSplitSuggestion()
    {
        // suggest-refactorings-facade-extraction-false-positive: A zero-field facade implementing
        // an interface with all-delegating public methods previously surfaced as a top-severity
        // "Split <type>" cohesion suggestion because the cohesion loop only filtered on
        // Lcom4Score + FilePath. After the fix, the loop additionally skips any type whose
        // LifecyclePattern is non-null (e.g., "action-triad" or "facade"), so the aggregator
        // never emits a Category="cohesion" suggestion for a detected lifecycle pattern.
        await using var workspace = CreateIsolatedWorkspaceCopy();

        var filePath = workspace.GetPath("SampleLib", "SuggestRefactoringsFacadeSubject.cs");
        await File.WriteAllTextAsync(filePath, """
namespace SampleLib;

public interface IFacadeSurface
{
    string ReadOne();
    string ReadTwo();
    int ReadThree();
}

public class SuggestRefactoringsFacadeSubject : IFacadeSurface
{
    public string ReadOne() => "one";

    public string ReadTwo() => "two";

    public int ReadThree() => 3;
}
""", CancellationToken.None);

        await workspace.LoadAsync(CancellationToken.None);

        var suggestions = await RefactoringSuggestionService.SuggestRefactoringsAsync(
            workspace.WorkspaceId, projectFilter: null, limit: 100, CancellationToken.None);

        var facadeCohesionSuggestion = suggestions.FirstOrDefault(s =>
            s.Category == "cohesion" && s.TargetSymbol == "SuggestRefactoringsFacadeSubject");
        Assert.IsNull(facadeCohesionSuggestion,
            "Facade/adapter types must not produce a cohesion 'Split' suggestion — the lifecycle pattern softens the recommendation upstream and the aggregator must respect it.");
    }

    private static async Task<IsolatedWorkspaceScope> CreateSuggestionWorkspaceAsync()
    {
        var workspace = CreateIsolatedWorkspaceCopy();
        return await InitializeWithCleanupAsync(
            workspace,
            static async (scope, ct) =>
            {
                var filePath = scope.GetPath("SampleLib", "RefactoringSuggestionSubject.cs");
                await File.WriteAllTextAsync(filePath, """
namespace SampleLib;

public sealed class RefactoringSuggestionSubject
{
    public int Evaluate(int a, int b, int c, int d, int e, int f, int g)
    {
        var score = 0;
        if (a > 0) score++;
        if (b > 0) score++;
        if (c > 0) score++;
        if (d > 0) score++;
        if (e > 0) score++;
        if (f > 0) score++;
        if (g > 0) score++;
        if (a > b) score++;
        if (b > c) score++;
        if (c > d) score++;
        if (d > e) score++;
        if (e > f) score++;
        if (f > g) score++;
        if (a + b > c) score++;
        if (d + e > f) score++;
        return score;
    }
}
""", ct).ConfigureAwait(false);
                await scope.LoadAsync(ct).ConfigureAwait(false);
            },
            CancellationToken.None).ConfigureAwait(false);
    }

    private static string GetSuggestionWorkspaceId() =>
        SuggestionWorkspace?.WorkspaceId
        ?? throw new InvalidOperationException("The class-private suggestion workspace was not initialized.");
}
