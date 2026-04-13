namespace RoslynMcp.Tests;

[TestClass]
public sealed class FixAllServiceIntegrationTests : SharedWorkspaceTestBase
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
    public async Task PreviewFixAll_UnknownDiagnostic_Returns_NoProvider_Guidance()
    {
        var result = await FixAllService.PreviewFixAllAsync(
            WorkspaceId,
            diagnosticId: "ZZZ_DOES_NOT_EXIST_999",
            scope: "solution",
            filePath: null,
            projectName: null,
            CancellationToken.None).ConfigureAwait(false);

        Assert.IsTrue(string.IsNullOrEmpty(result.PreviewToken));
        Assert.AreEqual(0, result.FixedCount);
        Assert.IsFalse(string.IsNullOrEmpty(result.GuidanceMessage), "Expected guidance when no fixer is available.");
        StringAssert.Contains(result.GuidanceMessage ?? "", "No code fix provider");
    }

    /// <summary>
    /// CS8019 is handled by <see cref="RefactoringService.PreviewCodeFixAsync"/> today; <see cref="FixAllService"/>
    /// only sees fixers loaded Features/analyzer assemblies and may not register a CS8019 FixAll provider.
    /// Pin the structured response so callers get guidance instead of an empty silent failure.
    /// </summary>
    [TestMethod]
    public async Task PreviewFixAll_CS8019_Returns_Guidance_When_No_FixAll_Provider_Loaded()
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var animalServiceFile = solution.Projects
            .SelectMany(project => project.Documents)
            .First(document => document.Name == "AnimalService.cs");

        var preview = await FixAllService.PreviewFixAllAsync(
            WorkspaceId,
            diagnosticId: "CS8019",
            scope: "document",
            filePath: animalServiceFile.FilePath!,
            projectName: null,
            CancellationToken.None).ConfigureAwait(false);

        Assert.IsTrue(string.IsNullOrEmpty(preview.PreviewToken));
        Assert.AreEqual(0, preview.FixedCount);
        StringAssert.Contains(preview.GuidanceMessage ?? "", "No code fix provider");
    }
}
