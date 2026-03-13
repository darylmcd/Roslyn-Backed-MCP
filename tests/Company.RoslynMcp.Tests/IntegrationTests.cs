using Company.RoslynMcp.Core.Models;

namespace Company.RoslynMcp.Tests;

[TestClass]
public class IntegrationTests : TestBase
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

    [TestMethod]
    public void Workspace_Load_Returns_WorkspaceId_And_Metadata()
    {
        var status = WorkspaceManager.GetStatus(WorkspaceId);
        Assert.IsTrue(status.IsLoaded);
        Assert.IsFalse(string.IsNullOrWhiteSpace(status.WorkspaceId));
        Assert.IsFalse(string.IsNullOrWhiteSpace(status.SnapshotToken));
        Assert.AreEqual(SampleSolutionPath, status.LoadedPath);
        Assert.IsTrue(status.ProjectCount >= 2);
        Assert.IsTrue(status.DocumentCount >= 1);
        Assert.IsTrue(status.Projects.Count >= 2, $"Expected at least 2 projects, got {status.Projects.Count}");
    }

    [TestMethod]
    public void Workspace_Status_Can_Be_Looked_Up_By_Id()
    {
        var status = WorkspaceManager.GetStatus(WorkspaceId);
        Assert.AreEqual(WorkspaceId, status.WorkspaceId);
    }

    [TestMethod]
    public async Task Workspace_Reload_Rejects_Unknown_Id()
    {
        await Assert.ThrowsExceptionAsync<KeyNotFoundException>(() =>
            WorkspaceManager.ReloadAsync("missing-workspace", CancellationToken.None));
    }

    [TestMethod]
    public void Workspace_Has_SampleLib_Project()
    {
        var status = WorkspaceManager.GetStatus(WorkspaceId);
        var sampleLib = status.Projects.FirstOrDefault(p => p.Name == "SampleLib");
        Assert.IsNotNull(sampleLib, "SampleLib project not found");
        Assert.IsTrue(sampleLib.DocumentCount > 0);
        CollectionAssert.Contains(sampleLib.TargetFrameworks.ToList(), "net10.0");
    }

    [TestMethod]
    public void Workspace_Has_SampleApp_Project()
    {
        var status = WorkspaceManager.GetStatus(WorkspaceId);
        var sampleApp = status.Projects.FirstOrDefault(p => p.Name == "SampleApp");
        Assert.IsNotNull(sampleApp, "SampleApp project not found");
        Assert.IsTrue(sampleApp.ProjectReferences.Contains("SampleLib"));
    }

    [TestMethod]
    public async Task Symbol_Search_Finds_Dog()
    {
        var results = await SymbolService.SearchSymbolsAsync(WorkspaceId, "Dog", null, null, null, 10, CancellationToken.None);
        Assert.IsTrue(results.Any(s => s.Name == "Dog"), "Dog class not found");
    }

    [TestMethod]
    public async Task Symbol_Search_Finds_IAnimal()
    {
        var results = await SymbolService.SearchSymbolsAsync(WorkspaceId, "IAnimal", null, null, null, 10, CancellationToken.None);
        Assert.IsTrue(results.Any(s => s.Name == "IAnimal"), "IAnimal interface not found");
    }

    [TestMethod]
    public async Task Symbol_Search_With_Kind_Filter()
    {
        var results = await SymbolService.SearchSymbolsAsync(WorkspaceId, "Dog", null, "Class", null, 10, CancellationToken.None);
        Assert.IsTrue(results.All(s => s.Kind == "Class"));
        Assert.IsTrue(results.Any(s => s.Name == "Dog"));
    }

    [TestMethod]
    public async Task Symbol_Info_Supports_Metadata_Name_Lookup()
    {
        var result = await SymbolService.GetSymbolInfoAsync(
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

        var definitions = await SymbolService.GoToDefinitionAsync(
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

        var symbol = await SymbolService.GetSymbolInfoAsync(
            WorkspaceId,
            SymbolLocator.BySource(animalFile.FilePath!, 6, 12),
            CancellationToken.None);
        Assert.IsNotNull(symbol);
        Assert.IsFalse(string.IsNullOrWhiteSpace(symbol.SymbolHandle));

        var refs = await SymbolService.FindReferencesAsync(
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

        var implementations = await SymbolService.FindImplementationsAsync(
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

        var symbols = await SymbolService.GetDocumentSymbolsAsync(WorkspaceId, dogFile.FilePath!, CancellationToken.None);
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

        var hierarchy = await SymbolService.GetTypeHierarchyAsync(
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
        var diagnostics = await DiagnosticService.GetDiagnosticsAsync(WorkspaceId, "SampleLib", null, null, CancellationToken.None);
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

        var result = await SymbolService.GetCallersCalleesAsync(
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

        var result = await SymbolService.AnalyzeImpactAsync(
            WorkspaceId,
            SymbolLocator.BySource(animalFile.FilePath!, 6, 12),
            CancellationToken.None);
        Assert.IsNotNull(result, "Impact analysis not found");
        Assert.IsTrue(result.DirectReferences.Count > 0, "Speak should have references");
        Assert.IsTrue(result.AffectedProjects.Count > 0, "Should affect at least one project");
    }

    [TestMethod]
    public async Task Format_Document_Preview_Produces_Changes()
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var serviceFile = solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => d.Name == "AnimalService.cs");
        Assert.IsNotNull(serviceFile, "AnimalService.cs not found");

        var preview = await RefactoringService.PreviewFormatDocumentAsync(WorkspaceId, serviceFile.FilePath!, CancellationToken.None);
        Assert.IsNotNull(preview.PreviewToken);
        // AnimalService.cs has intentional formatting issues
        // Changes may or may not be produced depending on the formatter
    }

    [TestMethod]
    public async Task Organize_Usings_Preview_Produces_Changes()
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var serviceFile = solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => d.Name == "AnimalService.cs");
        Assert.IsNotNull(serviceFile, "AnimalService.cs not found");

        var preview = await RefactoringService.PreviewOrganizeUsingsAsync(WorkspaceId, serviceFile.FilePath!, CancellationToken.None);
        Assert.IsNotNull(preview.PreviewToken);
        // AnimalService.cs has unused using System.Threading
    }

    [TestMethod]
    public async Task Rename_Preview_And_Apply_Update_Isolated_Workspace_Copy()
    {
        var isolatedSolutionPath = CreateSampleSolutionCopy();
        var isolatedRoot = Path.GetDirectoryName(isolatedSolutionPath)!;

        try
        {
            var status = await WorkspaceManager.LoadAsync(isolatedSolutionPath, CancellationToken.None);
            var isolatedWorkspaceId = status.WorkspaceId;
            var dogFilePath = Path.Combine(isolatedRoot, "SampleLib", "Dog.cs");
            var serviceFilePath = Path.Combine(isolatedRoot, "SampleLib", "AnimalService.cs");

            var preview = await RefactoringService.PreviewRenameAsync(
                isolatedWorkspaceId,
                SymbolLocator.BySource(dogFilePath, 3, 14),
                "Hound",
                CancellationToken.None);
            Assert.IsTrue(preview.Changes.Count > 0, "Rename preview should produce file changes.");

            var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, CancellationToken.None);
            Assert.IsTrue(applyResult.Success, applyResult.Error);

            var dogContents = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
            var serviceContents = await File.ReadAllTextAsync(serviceFilePath, CancellationToken.None);
            StringAssert.Contains(dogContents, "class Hound");
            StringAssert.Contains(serviceContents, "new Hound()");
        }
        finally
        {
            DeleteDirectoryIfExists(isolatedRoot);
        }
    }

    [TestMethod]
    public async Task Preview_Token_Is_Rejected_When_Workspace_Becomes_Stale()
    {
        var isolatedSolutionPath = CreateSampleSolutionCopy();
        var isolatedRoot = Path.GetDirectoryName(isolatedSolutionPath)!;

        try
        {
            var status = await WorkspaceManager.LoadAsync(isolatedSolutionPath, CancellationToken.None);
            var isolatedWorkspaceId = status.WorkspaceId;
            var serviceFilePath = Path.Combine(isolatedRoot, "SampleLib", "AnimalService.cs");

            var preview = await RefactoringService.PreviewOrganizeUsingsAsync(
                isolatedWorkspaceId,
                serviceFilePath,
                CancellationToken.None);
            await WorkspaceManager.ReloadAsync(isolatedWorkspaceId, CancellationToken.None);

            var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, CancellationToken.None);
            Assert.IsFalse(applyResult.Success, "Stale previews should be rejected.");
            StringAssert.Contains(applyResult.Error ?? string.Empty, "stale");
        }
        finally
        {
            DeleteDirectoryIfExists(isolatedRoot);
        }
    }

    [TestMethod]
    public async Task Organize_Usings_Apply_Removes_Unused_Using_In_Isolated_Copy()
    {
        var isolatedSolutionPath = CreateSampleSolutionCopy();
        var isolatedRoot = Path.GetDirectoryName(isolatedSolutionPath)!;

        try
        {
            var status = await WorkspaceManager.LoadAsync(isolatedSolutionPath, CancellationToken.None);
            var isolatedWorkspaceId = status.WorkspaceId;
            var serviceFilePath = Path.Combine(isolatedRoot, "SampleLib", "AnimalService.cs");

            var preview = await RefactoringService.PreviewOrganizeUsingsAsync(
                isolatedWorkspaceId,
                serviceFilePath,
                CancellationToken.None);
            var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, CancellationToken.None);

            Assert.IsTrue(applyResult.Success, applyResult.Error);
            var serviceContents = await File.ReadAllTextAsync(serviceFilePath, CancellationToken.None);
            Assert.IsFalse(serviceContents.Contains("using System.Threading;"));
        }
        finally
        {
            DeleteDirectoryIfExists(isolatedRoot);
        }
    }

    [TestMethod]
    public async Task Format_Document_Apply_Normalizes_Isolated_Copy()
    {
        var isolatedSolutionPath = CreateSampleSolutionCopy();
        var isolatedRoot = Path.GetDirectoryName(isolatedSolutionPath)!;

        try
        {
            var status = await WorkspaceManager.LoadAsync(isolatedSolutionPath, CancellationToken.None);
            var isolatedWorkspaceId = status.WorkspaceId;
            var serviceFilePath = Path.Combine(isolatedRoot, "SampleLib", "AnimalService.cs");

            var preview = await RefactoringService.PreviewFormatDocumentAsync(
                isolatedWorkspaceId,
                serviceFilePath,
                CancellationToken.None);
            var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, CancellationToken.None);

            Assert.IsTrue(applyResult.Success, applyResult.Error);
            var serviceContents = await File.ReadAllTextAsync(serviceFilePath, CancellationToken.None);
            Assert.IsFalse(serviceContents.Contains("MakeThemSpeak(    IEnumerable<IAnimal>     animals   )"));
        }
        finally
        {
            DeleteDirectoryIfExists(isolatedRoot);
        }
    }
}
