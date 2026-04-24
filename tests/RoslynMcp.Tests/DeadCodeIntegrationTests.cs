using RoslynMcp.Core.Services;

namespace RoslynMcp.Tests;

[DoNotParallelize]
[TestClass]
public sealed class DeadCodeIntegrationTests : SharedWorkspaceTestBase
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
            var unusedSymbols = await UnusedCodeAnalyzer.FindUnusedSymbolsAsync(
                workspaceId,
                new UnusedSymbolsAnalysisOptions { ProjectFilter = "SampleLib", IncludePublic = false, Limit = 50 },
                CancellationToken.None);
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
        var workspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);

        var unusedSymbols = await UnusedCodeAnalyzer.FindUnusedSymbolsAsync(
            workspaceId,
            new UnusedSymbolsAnalysisOptions { ProjectFilter = "SampleLib", IncludePublic = true, Limit = 200 },
            CancellationToken.None);

        var unusedNames = unusedSymbols.Select(s => s.SymbolName).ToHashSet();

        Assert.IsFalse(unusedNames.Contains("Describe"),
            "Extension method 'Describe' should not be reported as unused — it is called from SampleApp via obj.Describe() syntax.");
        Assert.IsFalse(unusedNames.Contains("AnimalExtensions"),
            "Static class 'AnimalExtensions' should not be reported as unused — its extension method is called from SampleApp.");
    }

    [TestMethod]
    public async Task Implicit_Conversion_Overload_With_Callers_Not_Reported_As_Unused()
    {
        var workspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);

        var unusedSymbols = await UnusedCodeAnalyzer.FindUnusedSymbolsAsync(
            workspaceId,
            new UnusedSymbolsAnalysisOptions { ProjectFilter = "SampleLib", IncludePublic = true, Limit = 200 },
            CancellationToken.None);

        // The IEnumerable<IAnimal> overload of CountAnimals is called from SampleApp
        // with an IAnimal[] argument (implicit conversion). It should not be reported as unused.
        var unusedCountAnimals = unusedSymbols.Where(s => s.SymbolName == "CountAnimals").ToList();

        Assert.AreEqual(0, unusedCountAnimals.Count,
            $"CountAnimals overload(s) should not be reported as unused. Found: {string.Join(", ", unusedCountAnimals.Select(s => $"{s.ContainingType}.{s.SymbolName}"))}");
    }

    [TestMethod]
    public async Task TestMethod_Attributed_Methods_Are_Not_Reported_As_Unused()
    {
        var workspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);

        var unusedSymbols = await UnusedCodeAnalyzer.FindUnusedSymbolsAsync(
            workspaceId,
            new UnusedSymbolsAnalysisOptions { ProjectFilter = "SampleLib.Tests", IncludePublic = true, Limit = 200 },
            CancellationToken.None);

        var unusedNames = unusedSymbols.Select(s => s.SymbolName).ToHashSet(StringComparer.Ordinal);

        Assert.IsFalse(unusedNames.Contains("CountAnimals_Returns_Total_Count"),
            "[TestMethod] entry point should be treated as framework-invoked and not reported as unused.");
        Assert.IsFalse(unusedNames.Contains("GetAllAnimals_Returns_Dog_And_Cat"),
            "[TestMethod] entry point should be treated as framework-invoked and not reported as unused.");
    }

    [TestMethod]
    public async Task FindUnusedSymbols_Default_ExcludesConventionInvokedTypes()
    {
        var workspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);

        var unusedSymbols = await UnusedCodeAnalyzer.FindUnusedSymbolsAsync(
            workspaceId,
            new UnusedSymbolsAnalysisOptions { ProjectFilter = "SampleLib", IncludePublic = true, Limit = 500 },
            CancellationToken.None);

        var unusedNames = unusedSymbols.Select(s => s.SymbolName).ToHashSet(StringComparer.Ordinal);

        // Convention shapes — none should be reported with the default excludeConventionInvoked=true.
        Assert.IsFalse(unusedNames.Contains("SampleValidator"),
            "FluentValidation AbstractValidator<T> subclass must be excluded under default excludeConventionInvoked.");
        Assert.IsFalse(unusedNames.Contains("ChatHub"),
            "SignalR Hub subclass must be excluded under default excludeConventionInvoked.");
        Assert.IsFalse(unusedNames.Contains("IndexModel"),
            "Razor PageModel subclass must be excluded under default excludeConventionInvoked.");
        Assert.IsFalse(unusedNames.Contains("MyDbContextModelSnapshot"),
            "EF *ModelSnapshot must be excluded under default excludeConventionInvoked.");
        Assert.IsFalse(unusedNames.Contains("LoggingMiddleware"),
            "ASP.NET middleware (Invoke/InvokeAsync(HttpContext) shape) must be excluded under default excludeConventionInvoked.");
        Assert.IsFalse(unusedNames.Contains("HealthResponseWriter"),
            "ASP.NET HealthCheck response-writer shape (HttpContext, HealthReport) must be excluded under default excludeConventionInvoked.");
        Assert.IsFalse(unusedNames.Contains("SampleMcpToolCatalog"),
            "[McpServerToolType] holder must be excluded under default excludeConventionInvoked.");
        Assert.IsFalse(unusedNames.Contains("SampleMcpPromptCatalog"),
            "[McpServerPromptType] holder must be excluded under default excludeConventionInvoked.");
        Assert.IsFalse(unusedNames.Contains("SampleMcpResourceCatalog"),
            "[McpServerResourceType] holder must be excluded under default excludeConventionInvoked.");
        Assert.IsFalse(unusedNames.Contains("WebHostSerialCollection"),
            "xUnit [CollectionDefinition] holder must be excluded under default excludeConventionInvoked.");

        // Negative control: the attribute simply contains 'McpServerToolType' as a substring
        // but is not on the whitelist. It must NOT be excluded.
        Assert.IsTrue(unusedNames.Contains("NotConventionInvokedHolder"),
            "Attributes that are not on the whitelist must not accidentally trigger the convention filter.");
    }

    [TestMethod]
    public async Task FindUnusedSymbols_ExcludeConventionInvokedFalse_IncludesConventionTypes()
    {
        var workspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);

        var unusedSymbols = await UnusedCodeAnalyzer.FindUnusedSymbolsAsync(
            workspaceId,
            new UnusedSymbolsAnalysisOptions
            {
                ProjectFilter = "SampleLib",
                IncludePublic = true,
                Limit = 500,
                ExcludeConventionInvoked = false,
            },
            CancellationToken.None);

        var unusedNames = unusedSymbols.Select(s => s.SymbolName).ToHashSet(StringComparer.Ordinal);

        // With the filter off, every convention shape should appear because none of them are
        // referenced anywhere in the sample solution.
        Assert.IsTrue(unusedNames.Contains("SampleValidator"),
            "Convention exclusion off — SampleValidator should be reported as unused.");
        Assert.IsTrue(unusedNames.Contains("ChatHub"),
            "Convention exclusion off — ChatHub should be reported as unused.");
        Assert.IsTrue(unusedNames.Contains("IndexModel"),
            "Convention exclusion off — IndexModel should be reported as unused.");
        Assert.IsTrue(unusedNames.Contains("MyDbContextModelSnapshot"),
            "Convention exclusion off — MyDbContextModelSnapshot should be reported as unused.");
        Assert.IsTrue(unusedNames.Contains("LoggingMiddleware"),
            "Convention exclusion off — LoggingMiddleware should be reported as unused.");
        Assert.IsTrue(unusedNames.Contains("HealthResponseWriter"),
            "Convention exclusion off — HealthResponseWriter should be reported as unused.");
        Assert.IsTrue(unusedNames.Contains("SampleMcpToolCatalog"),
            "Convention exclusion off — SampleMcpToolCatalog should be reported as unused.");
        Assert.IsTrue(unusedNames.Contains("WebHostSerialCollection"),
            "Convention exclusion off — WebHostSerialCollection should be reported as unused.");
    }

    [TestMethod]
    public async Task FindUnusedSymbols_ControlType_StillReportedUnderConventionFilter()
    {
        // The convention filter must NOT swallow truly-unused types that don't match any
        // convention shape. TrulyUnusedConcreteType is a plain internal class that nothing
        // references anywhere in the sample solution.
        var workspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);

        var unusedSymbols = await UnusedCodeAnalyzer.FindUnusedSymbolsAsync(
            workspaceId,
            new UnusedSymbolsAnalysisOptions { ProjectFilter = "SampleLib", IncludePublic = false, Limit = 500 },
            CancellationToken.None);

        var unusedNames = unusedSymbols.Select(s => s.SymbolName).ToHashSet(StringComparer.Ordinal);

        Assert.IsTrue(unusedNames.Contains("TrulyUnusedConcreteType"),
            "Plain internal type with no references must still be reported even with the convention filter on.");
    }
}
