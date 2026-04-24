using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression guard for <see cref="ApplyResultDto.MutatedSymbol"/> handle rotation on rename.
/// After a rename apply, the old <c>symbolHandle</c> captured by the agent at preview time no
/// longer resolves (MetadataName and DisplayName both change). The apply response rotates the
/// handle so agents can keep chaining without re-issuing <c>symbol_search</c> with the new name.
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class PostApplySymbolRotationTests : SharedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task RenameApply_Returns_MutatedSymbol_With_New_Name_And_Fresh_Handle()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        var workspaceId = loadResult.WorkspaceId;

        try
        {
            var animalServicePath = Path.Combine(solutionDir, "SampleLib", "AnimalService.cs");
            var locator = SymbolLocator.ByMetadataName("SampleLib.AnimalService.GetAllAnimals");

            var preview = await RefactoringService.PreviewRenameAsync(
                workspaceId, locator, "GetAllAnimalsRotated", CancellationToken.None);
            Assert.IsFalse(string.IsNullOrWhiteSpace(preview.PreviewToken));

            var apply = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);

            Assert.IsTrue(apply.Success, apply.Error);
            Assert.IsNotNull(apply.MutatedSymbol, "Rename apply must return a rotated handle in MutatedSymbol.");
            Assert.AreEqual("GetAllAnimalsRotated", apply.MutatedSymbol!.Name);
            Assert.IsFalse(
                string.IsNullOrWhiteSpace(apply.MutatedSymbol.SymbolHandle),
                "Rotated SymbolDto must carry a fresh SymbolHandle resolvable against the post-apply solution.");
            StringAssert.Contains(
                apply.MutatedSymbol.FullyQualifiedName,
                "GetAllAnimalsRotated",
                "FullyQualifiedName on the rotated symbol must reflect the new name.");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
            TryDeleteDirectory(solutionDir);
        }
    }

    [TestMethod]
    public async Task OrganizeUsingsApply_Does_Not_Set_MutatedSymbol()
    {
        // Non-rename applies leave MutatedSymbol null — the existing handle (if the agent captured
        // one) still resolves because neither MetadataName nor DisplayName changed.
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        var workspaceId = loadResult.WorkspaceId;

        try
        {
            var targetFile = Path.Combine(solutionDir, "SampleLib", "AnimalService.cs");
            var preview = await RefactoringService.PreviewOrganizeUsingsAsync(
                workspaceId, targetFile, CancellationToken.None);
            Assert.IsFalse(string.IsNullOrWhiteSpace(preview.PreviewToken));

            var apply = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);

            Assert.IsTrue(apply.Success, apply.Error);
            Assert.IsNull(
                apply.MutatedSymbol,
                "Non-rename applies must leave MutatedSymbol null — only rename changes symbol identity.");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
            TryDeleteDirectory(solutionDir);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }
}
