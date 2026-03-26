namespace RoslynMcp.Tests;

[TestClass]
public sealed class DeadCodeIntegrationTests : TestBase
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
    public async Task Remove_Dead_Code_Preview_And_Apply_Removes_Unused_Field()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;

        try
        {
            var dogFilePath = Path.Combine(copiedRoot, "SampleLib", "Dog.cs");
            var originalContents = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
            var insertionIndex = originalContents.LastIndexOf("}", StringComparison.Ordinal);

            Assert.AreNotEqual(-1, insertionIndex, "Expected Dog.cs to contain a closing brace.");

            var seededContents = originalContents.Insert(
                insertionIndex,
                Environment.NewLine + Environment.NewLine + "    private void UnusedTestOnlyMethod()" + Environment.NewLine + "    {" + Environment.NewLine + "    }" + Environment.NewLine);
            await File.WriteAllTextAsync(dogFilePath, seededContents, CancellationToken.None);

            var status = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
            var workspaceId = status.WorkspaceId;
            var unusedSymbols = await UnusedCodeAnalyzer.FindUnusedSymbolsAsync(workspaceId, "SampleLib", includePublic: false, limit: 50, CancellationToken.None);
            var unusedField = unusedSymbols.First(symbol => symbol.SymbolName == "UnusedTestOnlyMethod");

            var preview = await DeadCodeService.PreviewRemoveDeadCodeAsync(
                workspaceId,
                new RoslynMcp.Core.Models.DeadCodeRemovalDto([unusedField.SymbolHandle!]),
                CancellationToken.None);

            var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, CancellationToken.None);

            Assert.IsTrue(applyResult.Success, applyResult.Error);
            var contents = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
            Assert.IsFalse(contents.Contains("UnusedTestOnlyMethod"));
        }
        finally
        {
            DeleteDirectoryIfExists(copiedRoot);
        }
    }
}