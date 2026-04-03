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

    [TestMethod]
    public async Task Extension_Method_With_Callers_Not_Reported_As_Unused()
    {
        var status = await WorkspaceManager.LoadAsync(SampleSolutionPath, CancellationToken.None);
        var workspaceId = status.WorkspaceId;

        var unusedSymbols = await UnusedCodeAnalyzer.FindUnusedSymbolsAsync(
            workspaceId, "SampleLib", includePublic: true, limit: 200, CancellationToken.None);

        var unusedNames = unusedSymbols.Select(s => s.SymbolName).ToHashSet();

        Assert.IsFalse(unusedNames.Contains("Describe"),
            "Extension method 'Describe' should not be reported as unused — it is called from SampleApp via obj.Describe() syntax.");
        Assert.IsFalse(unusedNames.Contains("AnimalExtensions"),
            "Static class 'AnimalExtensions' should not be reported as unused — its extension method is called from SampleApp.");
    }

    [TestMethod]
    public async Task Implicit_Conversion_Overload_With_Callers_Not_Reported_As_Unused()
    {
        var status = await WorkspaceManager.LoadAsync(SampleSolutionPath, CancellationToken.None);
        var workspaceId = status.WorkspaceId;

        var unusedSymbols = await UnusedCodeAnalyzer.FindUnusedSymbolsAsync(
            workspaceId, "SampleLib", includePublic: true, limit: 200, CancellationToken.None);

        // The IEnumerable<IAnimal> overload of CountAnimals is called from SampleApp
        // with an IAnimal[] argument (implicit conversion). It should not be reported as unused.
        var unusedCountAnimals = unusedSymbols.Where(s => s.SymbolName == "CountAnimals").ToList();

        Assert.AreEqual(0, unusedCountAnimals.Count,
            $"CountAnimals overload(s) should not be reported as unused. Found: {string.Join(", ", unusedCountAnimals.Select(s => $"{s.ContainingType}.{s.SymbolName}"))}");
    }

    [TestMethod]
    public async Task TestMethod_Attributed_Methods_Are_Not_Reported_As_Unused()
    {
        var status = await WorkspaceManager.LoadAsync(SampleSolutionPath, CancellationToken.None);
        var workspaceId = status.WorkspaceId;

        var unusedSymbols = await UnusedCodeAnalyzer.FindUnusedSymbolsAsync(
            workspaceId, "SampleLib.Tests", includePublic: true, limit: 200, CancellationToken.None);

        var unusedNames = unusedSymbols.Select(s => s.SymbolName).ToHashSet(StringComparer.Ordinal);

        Assert.IsFalse(unusedNames.Contains("CountAnimals_Returns_Total_Count"),
            "[TestMethod] entry point should be treated as framework-invoked and not reported as unused.");
        Assert.IsFalse(unusedNames.Contains("GetAllAnimals_Returns_Dog_And_Cat"),
            "[TestMethod] entry point should be treated as framework-invoked and not reported as unused.");
    }
}
