namespace RoslynMcp.Tests;

/// <summary>
/// Verifies which selection-range code actions surface via the CodeActionService
/// pipeline when Roslyn's built-in CodeRefactoringProviders from
/// Microsoft.CodeAnalysis.CSharp.Features are invoked with non-zero-length TextSpans.
///
/// Findings (Roslyn 5.x on MSBuildWorkspace):
///   - Introduce parameter: WORKS (expression → parameter with call-site updates)
///   - Inline temporary variable: WORKS
///   - Extract method: NOT AVAILABLE (provider requires internal IDE services)
///   - Introduce local variable: NOT AVAILABLE (provider requires internal IDE services)
/// </summary>
[TestClass]
public sealed class SelectionRangeCodeActionTests : SharedWorkspaceTestBase
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

    private static string FindDocumentPath(string name)
    {
        var solution = WorkspaceManager.GetCurrentSolution(SampleWorkspaceId);
        return solution.Projects
            .SelectMany(p => p.Documents)
            .First(d => d.Name == name).FilePath!;
    }

    /// <summary>
    /// Confirms that extract method is NOT available via the code-action pipeline
    /// on MSBuildWorkspace. The built-in ExtractMethodCodeRefactoringProvider
    /// requires internal IDE workspace services that are absent from MSBuildWorkspace.
    /// This documents the gap and justifies a custom implementation (Phase 2).
    /// </summary>
    [TestMethod]
    public async Task GetCodeActions_WithStatementSelection_ExtractMethodNotAvailable()
    {
        var filePath = FindDocumentPath("RefactoringProbe.cs");

        // Lines 13-15: var sum = ...; var doubled = ...; Console.WriteLine(doubled);
        var actions = await CodeActionService.GetCodeActionsAsync(
            SampleWorkspaceId,
            filePath,
            startLine: 13,
            startColumn: 9,
            endLine: 15,
            endColumn: 39,
            CancellationToken.None);

        var extractActions = actions
            .Where(a => a.Title.Contains("Extract", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Document the gap: extract method does not surface in MSBuildWorkspace
        Assert.AreEqual(0, extractActions.Count,
            "Extract method should not be available — the Roslyn provider requires internal IDE services. " +
            "If this assertion fails, the provider has been fixed upstream and Phase 2 can be skipped.");
    }

    /// <summary>
    /// Verifies that "Introduce parameter" refactoring surfaces when selecting
    /// an expression. This is a working selection-range refactoring in MSBuildWorkspace.
    /// </summary>
    [TestMethod]
    public async Task GetCodeActions_WithExpressionSelection_SurfacesIntroduceParameter()
    {
        var filePath = FindDocumentPath("RefactoringProbe.cs");

        // Line 22: return Math.PI * radius * radius;
        // Select the expression: Math.PI * radius * radius
        var actions = await CodeActionService.GetCodeActionsAsync(
            SampleWorkspaceId,
            filePath,
            startLine: 22,
            startColumn: 16,
            endLine: 22,
            endColumn: 41,
            CancellationToken.None);

        Assert.IsTrue(actions.Count > 0,
            "Expected at least one code action for an expression selection.");

        var introduceParamActions = actions
            .Where(a => a.Title.Contains("Introduce parameter", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.IsTrue(introduceParamActions.Count > 0,
            "Expected 'Introduce parameter' refactoring for a selected expression.");
    }

    /// <summary>
    /// Verifies that "Inline temporary variable" refactoring surfaces when
    /// selecting a single-use variable declaration. This is a working
    /// selection-range refactoring in MSBuildWorkspace.
    /// </summary>
    [TestMethod]
    public async Task GetCodeActions_WithVariableSelection_SurfacesInlineTemporary()
    {
        var filePath = FindDocumentPath("RefactoringProbe.cs");

        // Line 28: var greeting = $"Hello, {name}!";
        var actions = await CodeActionService.GetCodeActionsAsync(
            SampleWorkspaceId,
            filePath,
            startLine: 28,
            startColumn: 9,
            endLine: 28,
            endColumn: 43,
            CancellationToken.None);

        Assert.IsTrue(actions.Count > 0,
            "Expected at least one code action for a variable declaration selection.");

        var inlineActions = actions
            .Where(a => a.Title.Contains("Inline temporary", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.IsTrue(inlineActions.Count > 0,
            "Expected 'Inline temporary variable' refactoring for a single-use variable.");
    }
}
