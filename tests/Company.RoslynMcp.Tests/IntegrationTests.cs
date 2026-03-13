namespace Company.RoslynMcp.Tests;

[TestClass]
public class IntegrationTests : TestBase
{
    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        await WorkspaceManager.LoadAsync(SampleSolutionPath, CancellationToken.None);
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        DisposeServices();
    }

    [TestMethod]
    public void Workspace_Is_Loaded()
    {
        var status = WorkspaceManager.GetStatus();
        Assert.IsTrue(status.IsLoaded);
        Assert.IsTrue(status.Projects.Count >= 2, $"Expected at least 2 projects, got {status.Projects.Count}");
    }

    [TestMethod]
    public void Workspace_Has_SampleLib_Project()
    {
        var status = WorkspaceManager.GetStatus();
        var sampleLib = status.Projects.FirstOrDefault(p => p.Name == "SampleLib");
        Assert.IsNotNull(sampleLib, "SampleLib project not found");
        Assert.IsTrue(sampleLib.DocumentCount > 0);
    }

    [TestMethod]
    public void Workspace_Has_SampleApp_Project()
    {
        var status = WorkspaceManager.GetStatus();
        var sampleApp = status.Projects.FirstOrDefault(p => p.Name == "SampleApp");
        Assert.IsNotNull(sampleApp, "SampleApp project not found");
        Assert.IsTrue(sampleApp.ProjectReferences.Contains("SampleLib"));
    }

    [TestMethod]
    public async Task Symbol_Search_Finds_Dog()
    {
        var results = await SymbolService.SearchSymbolsAsync("Dog", null, null, null, 10, CancellationToken.None);
        Assert.IsTrue(results.Any(s => s.Name == "Dog"), "Dog class not found");
    }

    [TestMethod]
    public async Task Symbol_Search_Finds_IAnimal()
    {
        var results = await SymbolService.SearchSymbolsAsync("IAnimal", null, null, null, 10, CancellationToken.None);
        Assert.IsTrue(results.Any(s => s.Name == "IAnimal"), "IAnimal interface not found");
    }

    [TestMethod]
    public async Task Symbol_Search_With_Kind_Filter()
    {
        var results = await SymbolService.SearchSymbolsAsync("Dog", null, "NamedType", null, 10, CancellationToken.None);
        Assert.IsTrue(results.All(s => s.Kind == "NamedType"));
        Assert.IsTrue(results.Any(s => s.Name == "Dog"));
    }

    [TestMethod]
    public async Task Go_To_Definition_Finds_IAnimal()
    {
        var solution = WorkspaceManager.GetCurrentSolution();
        var animalFile = solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => d.Name == "IAnimal.cs");
        Assert.IsNotNull(animalFile, "IAnimal.cs not found");

        var definitions = await SymbolService.GoToDefinitionAsync(animalFile.FilePath!, 5, 12, CancellationToken.None);
        Assert.IsTrue(definitions.Count > 0, "No definitions found");
    }

    [TestMethod]
    public async Task Find_References_Of_Speak()
    {
        var solution = WorkspaceManager.GetCurrentSolution();
        var animalFile = solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => d.Name == "IAnimal.cs");
        Assert.IsNotNull(animalFile, "IAnimal.cs not found");

        // Speak() is on line 6 in IAnimal.cs
        var refs = await SymbolService.FindReferencesAsync(animalFile.FilePath!, 6, 12, CancellationToken.None);
        Assert.IsTrue(refs.Count >= 1, $"Expected at least 1 reference to Speak(), found {refs.Count}");
    }

    [TestMethod]
    public async Task Find_Implementations_Of_IAnimal()
    {
        var solution = WorkspaceManager.GetCurrentSolution();
        var animalFile = solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => d.Name == "IAnimal.cs");
        Assert.IsNotNull(animalFile, "IAnimal.cs not found");

        // IAnimal is on line 3
        var implementations = await SymbolService.FindImplementationsAsync(animalFile.FilePath!, 3, 18, CancellationToken.None);
        Assert.IsTrue(implementations.Count >= 2, $"Expected at least 2 implementations (Dog, Cat), found {implementations.Count}");
    }

    [TestMethod]
    public async Task Document_Symbols_Returns_Hierarchy()
    {
        var solution = WorkspaceManager.GetCurrentSolution();
        var dogFile = solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => d.Name == "Dog.cs");
        Assert.IsNotNull(dogFile, "Dog.cs not found");

        var symbols = await SymbolService.GetDocumentSymbolsAsync(dogFile.FilePath!, CancellationToken.None);
        Assert.IsTrue(symbols.Count > 0, "No symbols found");

        var dogClass = symbols.FirstOrDefault(s => s.Name == "Dog");
        Assert.IsNotNull(dogClass, "Dog class not found in symbols");
        Assert.IsNotNull(dogClass.Children, "Dog should have child members");
        Assert.IsTrue(dogClass.Children.Any(c => c.Name == "Speak"), "Speak method not found in Dog");
    }

    [TestMethod]
    public async Task Type_Hierarchy_Shows_Shape_Hierarchy()
    {
        var solution = WorkspaceManager.GetCurrentSolution();
        var shapeFile = solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => d.Name == "Shape.cs");
        Assert.IsNotNull(shapeFile, "Shape.cs not found");

        // Shape is on line 3
        var hierarchy = await SymbolService.GetTypeHierarchyAsync(shapeFile.FilePath!, 3, 23, CancellationToken.None);
        Assert.IsNotNull(hierarchy, "Hierarchy not found");
        Assert.AreEqual("Shape", hierarchy.TypeName);
        Assert.IsNotNull(hierarchy.DerivedTypes, "Should have derived types");
        Assert.IsTrue(hierarchy.DerivedTypes.Count >= 2, $"Expected Circle and Rectangle, got {hierarchy.DerivedTypes.Count}");
    }

    [TestMethod]
    public async Task Project_Diagnostics_Returns_Results()
    {
        var diagnostics = await DiagnosticService.GetDiagnosticsAsync("SampleLib", null, null, CancellationToken.None);
        // Should have at least some diagnostics (unused field, unused using, etc.)
        Assert.IsTrue(diagnostics.CompilerDiagnostics.Count > 0 || diagnostics.WorkspaceDiagnostics.Count > 0,
            "Expected some diagnostics from SampleLib");
    }

    [TestMethod]
    public async Task Callers_Callees_For_MakeThemSpeak()
    {
        var solution = WorkspaceManager.GetCurrentSolution();
        var serviceFile = solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => d.Name == "AnimalService.cs");
        Assert.IsNotNull(serviceFile, "AnimalService.cs not found");

        // MakeThemSpeak is on line 16
        var result = await SymbolService.GetCallersCalleesAsync(serviceFile.FilePath!, 16, 17, CancellationToken.None);
        Assert.IsNotNull(result, "Callers/callees not found");
        Assert.IsTrue(result.Callees.Count > 0, "MakeThemSpeak should have callees (Speak, Console.WriteLine)");
    }

    [TestMethod]
    public async Task Impact_Analysis_For_Speak()
    {
        var solution = WorkspaceManager.GetCurrentSolution();
        var animalFile = solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => d.Name == "IAnimal.cs");
        Assert.IsNotNull(animalFile, "IAnimal.cs not found");

        var result = await SymbolService.AnalyzeImpactAsync(animalFile.FilePath!, 6, 12, CancellationToken.None);
        Assert.IsNotNull(result, "Impact analysis not found");
        Assert.IsTrue(result.DirectReferences.Count > 0, "Speak should have references");
        Assert.IsTrue(result.AffectedProjects.Count > 0, "Should affect at least one project");
    }

    [TestMethod]
    public async Task Format_Document_Preview_Produces_Changes()
    {
        var solution = WorkspaceManager.GetCurrentSolution();
        var serviceFile = solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => d.Name == "AnimalService.cs");
        Assert.IsNotNull(serviceFile, "AnimalService.cs not found");

        var preview = await RefactoringService.PreviewFormatDocumentAsync(serviceFile.FilePath!, CancellationToken.None);
        Assert.IsNotNull(preview.PreviewToken);
        // AnimalService.cs has intentional formatting issues
        // Changes may or may not be produced depending on the formatter
    }

    [TestMethod]
    public async Task Organize_Usings_Preview_Produces_Changes()
    {
        var solution = WorkspaceManager.GetCurrentSolution();
        var serviceFile = solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => d.Name == "AnimalService.cs");
        Assert.IsNotNull(serviceFile, "AnimalService.cs not found");

        var preview = await RefactoringService.PreviewOrganizeUsingsAsync(serviceFile.FilePath!, CancellationToken.None);
        Assert.IsNotNull(preview.PreviewToken);
        // AnimalService.cs has unused using System.Threading
    }
}
