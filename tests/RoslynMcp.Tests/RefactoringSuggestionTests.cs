namespace RoslynMcp.Tests;

[DoNotParallelize]
[TestClass]
public sealed class RefactoringSuggestionTests : IsolatedWorkspaceTestBase
{
    private static string SampleWorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        SampleWorkspaceId = await GetOrLoadWorkspaceIdAsync(SampleSolutionPath, CancellationToken.None);
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
}
