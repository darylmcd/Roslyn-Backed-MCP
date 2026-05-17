using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

[DoNotParallelize]
[TestClass]
public class IntegrationTests_SymbolNavigation : SharedWorkspaceTestBase
{
    private static string WorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        WorkspaceId = await GetOrLoadWorkspaceIdAsync(SampleSolutionPath, CancellationToken.None);
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        DisposeServices();
    }

    [TestMethod]
    public async Task Symbol_Search_Finds_Dog()
    {
        var results = await SymbolSearchService.SearchSymbolsAsync(WorkspaceId, "Dog", null, null, null, 10, CancellationToken.None);
        Assert.IsTrue(results.Any(s => s.Name == "Dog"), "Dog class not found");
    }

    [TestMethod]
    public async Task Symbol_Search_Finds_IAnimal()
    {
        var results = await SymbolSearchService.SearchSymbolsAsync(WorkspaceId, "IAnimal", null, null, null, 10, CancellationToken.None);
        Assert.IsTrue(results.Any(s => s.Name == "IAnimal"), "IAnimal interface not found");
    }

    // symbol-search-partial-match-gap: queries that include a namespace segment should still
    // find the type. Pre-fix, `FindSourceDeclarationsWithPatternAsync` matched only simple
    // names so "SampleLib.Dog" returned []; the FQN-substring pass now surfaces the type.
    [TestMethod]
    public async Task Symbol_Search_Finds_Type_By_Namespace_Qualified_Partial_Name()
    {
        var results = await SymbolSearchService.SearchSymbolsAsync(
            WorkspaceId, "SampleLib.Dog", null, null, null, 10, CancellationToken.None);
        Assert.IsTrue(results.Any(s => s.Name == "Dog"),
            $"Expected to find `Dog` via namespace-qualified query 'SampleLib.Dog'; got: {string.Join(", ", results.Select(r => r.Name))}");
    }

    [TestMethod]
    public async Task Symbol_Search_Finds_Member_By_FullyQualified_Name()
    {
        var results = await SymbolSearchService.SearchSymbolsAsync(
            WorkspaceId, "SampleLib.AnimalService.CountAnimals", null, null, null, 10, CancellationToken.None);

        Assert.IsTrue(results.Count > 0,
            $"Expected at least one hit for 'SampleLib.AnimalService.CountAnimals'; got zero.");
    }

    [TestMethod]
    public async Task Symbol_Search_With_Kind_Filter()
    {
        var results = await SymbolSearchService.SearchSymbolsAsync(WorkspaceId, "Dog", null, "Class", null, 10, CancellationToken.None);
        Assert.IsTrue(results.All(s => s.Kind == "Class"));
        Assert.IsTrue(results.Any(s => s.Name == "Dog"));
    }

    [TestMethod]
    public async Task Symbol_Info_Supports_Metadata_Name_Lookup()
    {
        var result = await SymbolSearchService.GetSymbolInfoAsync(
            WorkspaceId,
            SymbolLocator.ByMetadataName("SampleLib.Dog"),
            CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual("Dog", result.Name);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.SymbolHandle));
    }

    [TestMethod]
    public async Task Go_To_Definition_Finds_IAnimal()
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var animalFile = solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => d.Name == "IAnimal.cs");
        Assert.IsNotNull(animalFile, "IAnimal.cs not found");

        var definitions = await SymbolNavigationService.GoToDefinitionAsync(
            WorkspaceId,
            SymbolLocator.BySource(animalFile.FilePath!, 5, 12),
            CancellationToken.None);
        Assert.IsTrue(definitions.Count > 0, "No definitions found");
    }

    [TestMethod]
    public async Task Find_References_Of_Speak_By_Symbol_Handle()
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var animalFile = solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => d.Name == "IAnimal.cs");
        Assert.IsNotNull(animalFile, "IAnimal.cs not found");

        var symbol = await SymbolSearchService.GetSymbolInfoAsync(
            WorkspaceId,
            SymbolLocator.BySource(animalFile.FilePath!, 6, 12),
            CancellationToken.None);
        Assert.IsNotNull(symbol);
        Assert.IsFalse(string.IsNullOrWhiteSpace(symbol.SymbolHandle));

        var refs = await ReferenceService.FindReferencesAsync(
            WorkspaceId,
            SymbolLocator.ByHandle(symbol.SymbolHandle!),
            CancellationToken.None);
        Assert.IsTrue(refs.Count >= 1, $"Expected at least 1 reference to Speak(), found {refs.Count}");
    }

    [TestMethod]
    public async Task Find_Implementations_Of_IAnimal()
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var animalFile = solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => d.Name == "IAnimal.cs");
        Assert.IsNotNull(animalFile, "IAnimal.cs not found");

        var implementations = await ReferenceService.FindImplementationsAsync(
            WorkspaceId,
            SymbolLocator.BySource(animalFile.FilePath!, 3, 18),
            CancellationToken.None);
        Assert.IsTrue(implementations.Count >= 2, $"Expected at least 2 implementations (Dog, Cat), found {implementations.Count}");
    }

    [TestMethod]
    public async Task Document_Symbols_Returns_Hierarchy()
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var dogFile = solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => d.Name == "Dog.cs");
        Assert.IsNotNull(dogFile, "Dog.cs not found");

        var symbols = await SymbolSearchService.GetDocumentSymbolsAsync(WorkspaceId, dogFile.FilePath!, CancellationToken.None);
        Assert.IsTrue(symbols.Count > 0, "No symbols found");

        var dogClass = symbols.FirstOrDefault(s => s.Name == "Dog");
        Assert.IsNotNull(dogClass, "Dog class not found in symbols");
        Assert.IsNotNull(dogClass.Children, "Dog should have child members");
        Assert.IsTrue(dogClass.Children.Any(c => c.Name == "Speak"), "Speak method not found in Dog");
    }

    [TestMethod]
    public async Task Type_Hierarchy_Shows_Shape_Hierarchy()
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var shapeFile = solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => d.Name == "Shape.cs");
        Assert.IsNotNull(shapeFile, "Shape.cs not found");

        var hierarchy = await SymbolRelationshipService.GetTypeHierarchyAsync(
            WorkspaceId,
            SymbolLocator.BySource(shapeFile.FilePath!, 3, 23),
            CancellationToken.None);
        Assert.IsNotNull(hierarchy, "Hierarchy not found");
        Assert.AreEqual("Shape", hierarchy.TypeName);
        Assert.IsNotNull(hierarchy.DerivedTypes, "Should have derived types");
        Assert.IsTrue(hierarchy.DerivedTypes.Count >= 2, $"Expected Circle and Rectangle, got {hierarchy.DerivedTypes.Count}");
    }

    [TestMethod]
    public async Task Project_Diagnostics_Returns_Separated_Buckets()
    {
        var diagnostics = await DiagnosticService.GetDiagnosticsAsync(WorkspaceId, "SampleLib", null, null, null, CancellationToken.None);
        // Should have at least some diagnostics (unused field, unused using, etc.)
        Assert.IsTrue(diagnostics.CompilerDiagnostics.Count > 0 || diagnostics.WorkspaceDiagnostics.Count > 0,
            "Expected some diagnostics from SampleLib");
        Assert.IsNotNull(diagnostics.AnalyzerDiagnostics);
    }

    [TestMethod]
    public async Task Callers_Callees_For_MakeThemSpeak()
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var serviceFile = solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => d.Name == "AnimalService.cs");
        Assert.IsNotNull(serviceFile, "AnimalService.cs not found");

        var result = await SymbolRelationshipService.GetCallersCalleesAsync(
            WorkspaceId,
            SymbolLocator.BySource(serviceFile.FilePath!, 16, 17),
            CancellationToken.None);
        Assert.IsNotNull(result, "Callers/callees not found");
        Assert.IsTrue(result.Callees.Count > 0, "MakeThemSpeak should have callees (Speak, Console.WriteLine)");
    }

    [TestMethod]
    public async Task Impact_Analysis_For_Speak()
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var animalFile = solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => d.Name == "IAnimal.cs");
        Assert.IsNotNull(animalFile, "IAnimal.cs not found");

        var result = await MutationAnalysisService.AnalyzeImpactAsync(
            WorkspaceId,
            SymbolLocator.BySource(animalFile.FilePath!, 6, 12),
            new ImpactAnalysisPaging(),
            CancellationToken.None);
        Assert.IsNotNull(result, "Impact analysis not found");
        Assert.IsTrue(result.DirectReferences.Count > 0, "Speak should have references");
        Assert.IsTrue(result.AffectedProjects.Count > 0, "Should affect at least one project");
        Assert.IsTrue(result.TotalDirectReferences >= result.DirectReferences.Count);
    }
}
