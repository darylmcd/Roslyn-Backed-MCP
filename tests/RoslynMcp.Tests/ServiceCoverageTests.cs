using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

[TestClass]
public class ServiceCoverageTests : TestBase
{
    private static string WorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        var status = await WorkspaceManager.LoadAsync(SampleSolutionPath, CancellationToken.None);
        WorkspaceId = status.WorkspaceId;
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        DisposeServices();
    }

    private static string FindDocumentPath(string name)
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        return solution.Projects
            .SelectMany(p => p.Documents)
            .First(d => d.Name == name).FilePath!;
    }

    // ── SymbolSearchService ──────────────────────────────────────────

    [TestMethod]
    public async Task SearchSymbols_Finds_Dog_Class()
    {
        var results = await SymbolSearchService.SearchSymbolsAsync(
            WorkspaceId, "Dog", null, null, null, 10, CancellationToken.None);

        Assert.IsTrue(results.Any(s => s.Name == "Dog"),
            "Expected at least one result with Name == 'Dog'");
    }

    [TestMethod]
    public async Task GetSymbolInfo_Returns_Details_For_AnimalService()
    {
        var result = await SymbolSearchService.GetSymbolInfoAsync(
            WorkspaceId,
            SymbolLocator.ByMetadataName("SampleLib.AnimalService"),
            CancellationToken.None);

        Assert.IsNotNull(result, "SymbolInfo should not be null for AnimalService");
        Assert.AreEqual("AnimalService", result.Name);
    }

    [TestMethod]
    public async Task GetDocumentSymbols_Returns_Members_For_Dog()
    {
        var dogPath = FindDocumentPath("Dog.cs");

        var symbols = await SymbolSearchService.GetDocumentSymbolsAsync(
            WorkspaceId, dogPath, CancellationToken.None);

        Assert.IsTrue(symbols.Count > 0,
            "Expected at least one symbol in Dog.cs");
    }

    // ── SymbolNavigationService ──────────────────────────────────────

    [TestMethod]
    public async Task GoToDefinition_Finds_IAnimal()
    {
        var definitions = await SymbolNavigationService.GoToDefinitionAsync(
            WorkspaceId,
            SymbolLocator.ByMetadataName("SampleLib.IAnimal"),
            CancellationToken.None);

        Assert.IsTrue(definitions.Count >= 1,
            $"Expected at least 1 definition location for IAnimal, got {definitions.Count}");
    }

    [TestMethod]
    public async Task GoToTypeDefinition_Navigates_From_Variable()
    {
        var animalServicePath = FindDocumentPath("AnimalService.cs");

        // Line 18: "foreach (var animal in animals)" — position on 'animal' should navigate to IAnimal
        var results = await SymbolNavigationService.GoToTypeDefinitionAsync(
            WorkspaceId,
            SymbolLocator.BySource(animalServicePath, 18, 22),
            CancellationToken.None);

        Assert.IsTrue(results.Count > 0,
            "Expected at least one type definition result for the 'animal' variable");
    }

    [TestMethod]
    public async Task GetEnclosingSymbol_Returns_Method()
    {
        var animalServicePath = FindDocumentPath("AnimalService.cs");

        // Line 18 is inside MakeThemSpeak method body
        var result = await SymbolNavigationService.GetEnclosingSymbolAsync(
            WorkspaceId, animalServicePath, 18, 10, CancellationToken.None);

        Assert.IsNotNull(result, "Enclosing symbol should not be null");
        Assert.IsTrue(result.Name.Contains("MakeThemSpeak"),
            $"Expected enclosing symbol to contain 'MakeThemSpeak', got '{result.Name}'");
    }

    // ── SymbolRelationshipService ────────────────────────────────────

    [TestMethod]
    public async Task GetTypeHierarchy_Shows_Shape_Hierarchy()
    {
        var hierarchy = await SymbolRelationshipService.GetTypeHierarchyAsync(
            WorkspaceId,
            SymbolLocator.ByMetadataName("SampleLib.Hierarchy.Shape"),
            CancellationToken.None);

        Assert.IsNotNull(hierarchy, "Type hierarchy should not be null for Shape");
        Assert.IsNotNull(hierarchy.DerivedTypes, "Shape should have derived types");

        var derivedNames = hierarchy.DerivedTypes.Select(d => d.TypeName).ToList();
        Assert.IsTrue(derivedNames.Any(n => n.Contains("Circle")),
            "Circle should be a derived type of Shape");
        Assert.IsTrue(derivedNames.Any(n => n.Contains("Rectangle")),
            "Rectangle should be a derived type of Shape");
    }

    [TestMethod]
    public async Task GetSignatureHelp_Returns_Method_Signature()
    {
        var animalServicePath = FindDocumentPath("AnimalService.cs");

        // Line 16 column 17 is MakeThemSpeak declaration
        var result = await SymbolRelationshipService.GetSignatureHelpAsync(
            WorkspaceId,
            SymbolLocator.BySource(animalServicePath, 16, 17),
            CancellationToken.None);

        Assert.IsNotNull(result, "Signature help should not be null for MakeThemSpeak");
    }

    [TestMethod]
    public async Task GetCallersCallees_Finds_Speak_Callers()
    {
        var animalServicePath = FindDocumentPath("AnimalService.cs");

        // Line 16 column 17 is MakeThemSpeak declaration
        var result = await SymbolRelationshipService.GetCallersCalleesAsync(
            WorkspaceId,
            SymbolLocator.BySource(animalServicePath, 16, 17),
            CancellationToken.None);

        Assert.IsNotNull(result, "CallerCallee result should not be null");
        Assert.IsTrue(result.Callers.Count > 0 || result.Callees.Count > 0,
            "MakeThemSpeak should have callers or callees");
    }

    // ── CompletionService ────────────────────────────────────────────

    [TestMethod]
    public async Task GetCompletions_Returns_Items_At_Valid_Position()
    {
        var animalServicePath = FindDocumentPath("AnimalService.cs");

        // Line 20 column 30 is inside method body where completions are available
        var result = await CompletionService.GetCompletionsAsync(
            WorkspaceId, animalServicePath, 20, 30, CancellationToken.None);

        Assert.IsTrue(result.Items.Count > 0,
            "Expected completion items at a valid position in AnimalService.cs");
    }

    // ── TestDiscoveryService ─────────────────────────────────────────

    [TestMethod]
    public async Task DiscoverTests_Finds_Sample_Tests()
    {
        var result = await TestDiscoveryService.DiscoverTestsAsync(
            WorkspaceId, CancellationToken.None);

        Assert.IsTrue(result.TestProjects.Count > 0,
            "Expected at least one test project to be discovered in the sample solution");
    }

    // ── TestRunnerService ────────────────────────────────────────────

    [TestMethod]
    public async Task RunTests_Executes_SampleLib_Tests()
    {
        var result = await TestRunnerService.RunTestsAsync(
            WorkspaceId, "SampleLib.Tests", null, CancellationToken.None);

        Assert.IsNotNull(result,
            "Test run result should not be null for SampleLib.Tests");
    }
}
