using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ScaffoldingIntegrationTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        InitializeServices();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        DisposeServices();
    }

    [TestMethod]
    public async Task Scaffold_Type_Preview_And_Apply_Creates_New_Type_File()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var targetFilePath = workspace.GetPath("SampleLib", "Generated", "Bird.cs");

        var preview = await ScaffoldingService.PreviewScaffoldTypeAsync(
            workspace.WorkspaceId,
            new ScaffoldTypeDto("SampleLib", "Bird", "class", "SampleLib.Generated"),
            CancellationToken.None);

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, CancellationToken.None);

        Assert.IsTrue(applyResult.Success, applyResult.Error);
        Assert.IsTrue(File.Exists(targetFilePath));
        var contents = await File.ReadAllTextAsync(targetFilePath, CancellationToken.None);
        StringAssert.Contains(contents, "namespace SampleLib.Generated");
        StringAssert.Contains(contents, "class Bird");
    }

    [TestMethod]
    public async Task Scaffold_Test_Preview_And_Apply_Creates_New_Test_File()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var targetFilePath = workspace.GetPath("SampleLib.Tests", "DogGeneratedTests.cs");

        var preview = await ScaffoldingService.PreviewScaffoldTestAsync(
            workspace.WorkspaceId,
            new ScaffoldTestDto("SampleLib.Tests", "Dog", "Speak"),
            CancellationToken.None);

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, CancellationToken.None);

        Assert.IsTrue(applyResult.Success, applyResult.Error);
        Assert.IsTrue(File.Exists(targetFilePath));
        var contents = await File.ReadAllTextAsync(targetFilePath, CancellationToken.None);
        StringAssert.Contains(contents, "[TestMethod]");
        StringAssert.Contains(contents, "Speak_Needs_Test");
    }
}
