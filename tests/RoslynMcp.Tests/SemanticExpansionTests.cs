using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

[TestClass]
public class SemanticExpansionTests : TestBase
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
    public async Task Find_Overrides_Finds_Rectangle_Describe()
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var shapeFile = solution.Projects.SelectMany(project => project.Documents).First(document => document.Name == "Shape.cs");

        var overrides = await SymbolService.FindOverridesAsync(
            WorkspaceId,
            SymbolLocator.BySource(shapeFile.FilePath!, 7, 27),
            CancellationToken.None);

        Assert.IsTrue(overrides.Any(location => location.FilePath.EndsWith("Rectangle.cs", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task Find_Base_Members_Finds_Shape_Describe_For_Rectangle()
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var rectangleFile = solution.Projects.SelectMany(project => project.Documents).First(document => document.Name == "Rectangle.cs");

        var baseMembers = await SymbolService.FindBaseMembersAsync(
            WorkspaceId,
            SymbolLocator.BySource(rectangleFile.FilePath!, 16, 28),
            CancellationToken.None);

        Assert.IsTrue(baseMembers.Any(location => location.FilePath.EndsWith("Shape.cs", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task Member_Hierarchy_Includes_Base_And_Overrides()
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var shapeFile = solution.Projects.SelectMany(project => project.Documents).First(document => document.Name == "Shape.cs");

        var hierarchy = await SymbolService.GetMemberHierarchyAsync(
            WorkspaceId,
            SymbolLocator.BySource(shapeFile.FilePath!, 7, 27),
            CancellationToken.None);

        Assert.IsNotNull(hierarchy);
        Assert.AreEqual("Describe", hierarchy.Symbol.Name);
        Assert.IsTrue(hierarchy.Overrides.Any(overrideSymbol => overrideSymbol.Name == "Describe"));
    }

    [TestMethod]
    public void Project_Graph_Reports_Project_References()
    {
        var graph = WorkspaceManager.GetProjectGraph(WorkspaceId);
        var sampleApp = graph.Projects.First(project => project.ProjectName == "SampleApp");
        var sampleTests = graph.Projects.First(project => project.ProjectName == "SampleLib.Tests");

        CollectionAssert.Contains(sampleApp.ProjectReferences.ToList(), "SampleLib");
        CollectionAssert.Contains(sampleTests.ProjectReferences.ToList(), "SampleLib");
    }

    [TestMethod]
    public async Task Source_Generated_Documents_Are_Listed_For_Project()
    {
        var generatedWorkspace = await WorkspaceManager.LoadAsync(GeneratedDocumentSolutionPath, CancellationToken.None);
        var buildResult = await ValidationService.BuildWorkspaceAsync(generatedWorkspace.WorkspaceId, CancellationToken.None);
        Assert.IsTrue(buildResult.Execution.Succeeded, buildResult.Execution.StdErr);
        var generatedDocuments = await WorkspaceManager.GetSourceGeneratedDocumentsAsync(
            generatedWorkspace.WorkspaceId,
            "ConsumerLib",
            CancellationToken.None);

        Assert.IsTrue(generatedDocuments.Count > 0, "Expected generated documents for ConsumerLib.");
        Assert.IsTrue(generatedDocuments.Any(document => document.HintName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task Symbol_Signature_Help_Returns_Parameters_And_Return_Type()
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var animalServiceFile = solution.Projects.SelectMany(project => project.Documents).First(document => document.Name == "AnimalService.cs");

        var signature = await SymbolService.GetSignatureHelpAsync(
            WorkspaceId,
            SymbolLocator.BySource(animalServiceFile.FilePath!, 16, 17),
            CancellationToken.None);

        Assert.IsNotNull(signature);
        Assert.AreEqual("void", signature.ReturnType);
        Assert.IsTrue(signature.Parameters.Any(parameter => parameter.Contains("IEnumerable<IAnimal>", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task Symbol_Relationships_Combine_References_And_Implementations()
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var animalFile = solution.Projects.SelectMany(project => project.Documents).First(document => document.Name == "IAnimal.cs");

        var relationships = await SymbolService.GetSymbolRelationshipsAsync(
            WorkspaceId,
            SymbolLocator.BySource(animalFile.FilePath!, 6, 12),
            CancellationToken.None);

        Assert.IsNotNull(relationships);
        Assert.IsTrue(relationships.References.Count > 0);
        Assert.IsTrue(relationships.Implementations.Count > 0);
    }
}
