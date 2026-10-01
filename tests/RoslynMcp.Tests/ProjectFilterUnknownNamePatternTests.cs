using RoslynMcp.Core.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// project-filter-unknown-name-pattern-deadcode: the reflection-usage, semantic-search,
/// duplicate-method, unused-symbol, duplicate-helper, dead-field and dead-local entry points must
/// reject a non-blank <c>projectName</c> that matches no loaded project (ArgumentException,
/// ParamName <c>projectName</c>) instead of returning an empty success-shaped result.
/// A known name and a blank name keep their previous behavior.
/// </summary>
[TestClass]
public sealed class ProjectFilterUnknownNamePatternTests : SharedWorkspaceTestBase
{
    private const string UnknownProject = "NoSuchProject.Typo";

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
    public async Task FindReflectionUsages_UnknownProject_Throws()
    {
        var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            CodePatternAnalyzer.FindReflectionUsagesDetailedAsync(WorkspaceId, UnknownProject, CancellationToken.None));

        AssertProjectNameRejection(ex);
    }

    [TestMethod]
    public async Task FindReflectionUsages_KnownProject_DoesNotThrow()
    {
        var scan = await CodePatternAnalyzer.FindReflectionUsagesDetailedAsync(WorkspaceId, "SampleLib", CancellationToken.None);

        Assert.IsNotNull(scan);
    }

    [TestMethod]
    public async Task SemanticSearch_UnknownProject_Throws()
    {
        var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            CodePatternAnalyzer.SemanticSearchAsync(WorkspaceId, "classes", UnknownProject, 10, CancellationToken.None));

        AssertProjectNameRejection(ex);
    }

    [TestMethod]
    public async Task SemanticSearch_BlankProject_SearchesWholeSolution()
    {
        var response = await CodePatternAnalyzer.SemanticSearchAsync(WorkspaceId, "classes", "  ", 10, CancellationToken.None);

        Assert.IsTrue(response.Results.Count > 0, "A blank projectName must search the whole solution.");
    }

    [TestMethod]
    public async Task FindDuplicatedMethods_UnknownProject_Throws()
    {
        var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            DuplicateMethodDetectorService.FindDuplicatedMethodsAsync(
                WorkspaceId, new DuplicateMethodAnalysisOptions { ProjectFilter = UnknownProject }, CancellationToken.None));

        AssertProjectNameRejection(ex);
    }

    [TestMethod]
    public async Task FindDuplicatedMethods_KnownProject_DoesNotThrow()
    {
        var groups = await DuplicateMethodDetectorService.FindDuplicatedMethodsAsync(
            WorkspaceId, new DuplicateMethodAnalysisOptions { ProjectFilter = "SampleLib" }, CancellationToken.None);

        Assert.IsNotNull(groups);
    }

    [TestMethod]
    public async Task FindUnusedSymbols_UnknownProject_Throws()
    {
        var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            UnusedCodeAnalyzer.FindUnusedSymbolsAsync(
                WorkspaceId, new UnusedSymbolsAnalysisOptions { ProjectFilter = UnknownProject }, CancellationToken.None));

        AssertProjectNameRejection(ex);
    }

    [TestMethod]
    public async Task FindUnusedSymbols_KnownProject_DoesNotThrow()
    {
        var results = await UnusedCodeAnalyzer.FindUnusedSymbolsAsync(
            WorkspaceId, new UnusedSymbolsAnalysisOptions { ProjectFilter = "SampleLib" }, CancellationToken.None);

        Assert.IsNotNull(results);
    }

    [TestMethod]
    public async Task FindDuplicateHelpers_UnknownProject_Throws()
    {
        var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            UnusedCodeAnalyzer.FindDuplicateHelpersAsync(
                WorkspaceId, new DuplicateHelperAnalysisOptions { ProjectFilter = UnknownProject }, CancellationToken.None));

        AssertProjectNameRejection(ex);
    }

    [TestMethod]
    public async Task FindDeadFields_UnknownProject_Throws()
    {
        var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            UnusedCodeAnalyzer.FindDeadFieldsAsync(
                WorkspaceId, new DeadFieldsAnalysisOptions { ProjectFilter = UnknownProject }, CancellationToken.None));

        AssertProjectNameRejection(ex);
    }

    [TestMethod]
    public async Task FindDeadLocals_UnknownProject_Throws()
    {
        var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            UnusedCodeAnalyzer.FindDeadLocalsAsync(
                WorkspaceId, new DeadLocalsAnalysisOptions { ProjectFilter = UnknownProject }, CancellationToken.None));

        AssertProjectNameRejection(ex);
    }

    private static void AssertProjectNameRejection(ArgumentException ex)
    {
        Assert.AreEqual("projectName", ex.ParamName);
        StringAssert.Contains(ex.Message, "matched 0 projects");
    }
}
