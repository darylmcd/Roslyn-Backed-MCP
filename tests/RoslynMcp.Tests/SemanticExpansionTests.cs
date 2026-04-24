using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

[DoNotParallelize]
[TestClass]
public class SemanticExpansionTests : SharedWorkspaceTestBase
{
    private static string WorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        WorkspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);
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

        var overrides = await ReferenceService.FindOverridesAsync(
            WorkspaceId,
            SymbolLocator.BySource(shapeFile.FilePath!, 7, 27),
            CancellationToken.None);

        Assert.IsTrue(overrides.Any(overrideSymbol => overrideSymbol.FilePath?.EndsWith("Rectangle.cs", StringComparison.OrdinalIgnoreCase) == true));
    }

    // find-base-members-vs-member-hierarchy-metadata-drift: regression — metadata-boundary
    // bases (IEquatable<T>.Equals from corlib) must surface across all three tools with the
    // same count. Pre-fix: find_base_members/find_overrides returned count=0 because the
    // LocationDto materializer filtered out symbols with no IsInSource location.
    [TestMethod]
    public async Task Metadata_Boundary_Base_Surfaces_Across_Find_Base_Members_And_Member_Hierarchy()
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var pointFile = solution.Projects
            .SelectMany(project => project.Documents)
            .First(document => document.Name == "EquatablePoint.cs");

        // EquatablePoint.Equals(EquatablePoint) — line 23, col 17 (the `Equals` token).
        var locator = SymbolLocator.BySource(pointFile.FilePath!, 23, 17);

        var baseMembers = await ReferenceService.FindBaseMembersAsync(WorkspaceId, locator, CancellationToken.None);
        var hierarchy = await SymbolRelationshipService.GetMemberHierarchyAsync(WorkspaceId, locator, CancellationToken.None);

        Assert.IsNotNull(hierarchy);
        Assert.IsTrue(hierarchy.BaseMembers.Count > 0,
            "member_hierarchy should resolve IEquatable<T>.Equals as a base member.");
        Assert.AreEqual(
            hierarchy.BaseMembers.Count,
            baseMembers.Count,
            "find_base_members must match member_hierarchy.BaseMembers count — metadata-boundary alignment.");
        Assert.IsTrue(baseMembers.Any(symbol => symbol.FullyQualifiedName.Contains("IEquatable", StringComparison.Ordinal)),
            "find_base_members should surface the metadata-only IEquatable<T>.Equals symbol.");
        Assert.IsTrue(baseMembers.Any(symbol => symbol.FilePath is null),
            "At least one returned base member should carry FilePath=null (metadata-only).");
    }

    // find-base-members-vs-member-hierarchy-metadata-drift: regression — find_overrides and
    // member_hierarchy.Overrides must agree for a metadata-boundary symbol. SymbolFinder
    // cannot walk into corlib to find IEquatable<T> implementations, so both return 0 — the
    // important invariant is that they AGREE (no silent drift between the two surfaces).
    [TestMethod]
    public async Task Metadata_Boundary_Find_Overrides_Matches_Member_Hierarchy_Overrides()
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var pointFile = solution.Projects
            .SelectMany(project => project.Documents)
            .First(document => document.Name == "EquatablePoint.cs");

        var locator = SymbolLocator.BySource(pointFile.FilePath!, 23, 17);

        var overrides = await ReferenceService.FindOverridesAsync(WorkspaceId, locator, CancellationToken.None);
        var hierarchy = await SymbolRelationshipService.GetMemberHierarchyAsync(WorkspaceId, locator, CancellationToken.None);
        Assert.IsNotNull(hierarchy);
        Assert.AreEqual(
            hierarchy.Overrides.Count,
            overrides.Count,
            "find_overrides must match member_hierarchy.Overrides count for metadata-boundary symbols.");
    }

    [TestMethod]
    public async Task Find_Base_Members_Finds_Shape_Describe_For_Rectangle()
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var rectangleFile = solution.Projects.SelectMany(project => project.Documents).First(document => document.Name == "Rectangle.cs");

        var baseMembers = await ReferenceService.FindBaseMembersAsync(
            WorkspaceId,
            SymbolLocator.BySource(rectangleFile.FilePath!, 16, 28),
            CancellationToken.None);

        Assert.IsTrue(baseMembers.Any(baseMember => baseMember.FilePath?.EndsWith("Shape.cs", StringComparison.OrdinalIgnoreCase) == true));
    }

    [TestMethod]
    public async Task Member_Hierarchy_Includes_Base_And_Overrides()
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var shapeFile = solution.Projects.SelectMany(project => project.Documents).First(document => document.Name == "Shape.cs");

        var hierarchy = await SymbolRelationshipService.GetMemberHierarchyAsync(
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
        var buildResult = await BuildService.BuildWorkspaceAsync(generatedWorkspace.WorkspaceId, CancellationToken.None);
        Assert.IsTrue(buildResult.Execution.Succeeded, buildResult.Execution.StdErr);
        var generatedDocuments = await WorkspaceManager.GetSourceGeneratedDocumentsAsync(
            generatedWorkspace.WorkspaceId,
            "ConsumerLib",
            CancellationToken.None);

        Assert.IsTrue(generatedDocuments.Count > 0, "Expected generated documents for ConsumerLib.");
        Assert.IsTrue(generatedDocuments.Any(document => document.HintName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)));
        var duplicateHintNames = generatedDocuments
            .GroupBy(document => $"{document.ProjectName}\u001F{document.HintName}", StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        Assert.AreEqual(0, duplicateHintNames.Count, "Expected no duplicate generated-document hint names per project.");
    }

    [TestMethod]
    public async Task Symbol_Signature_Help_Returns_Parameters_And_Return_Type()
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var animalServiceFile = solution.Projects.SelectMany(project => project.Documents).First(document => document.Name == "AnimalService.cs");

        var signature = await SymbolRelationshipService.GetSignatureHelpAsync(
            WorkspaceId,
            SymbolLocator.BySource(animalServiceFile.FilePath!, 16, 17),
            preferDeclaringMember: true,
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

        var relationships = await SymbolRelationshipService.GetSymbolRelationshipsAsync(
            WorkspaceId,
            SymbolLocator.BySource(animalFile.FilePath!, 6, 12),
            preferDeclaringMember: true,
            CancellationToken.None);

        Assert.IsNotNull(relationships);
        Assert.IsTrue(relationships.References.Count > 0);
        Assert.IsTrue(relationships.Implementations.Count > 0);
    }
}
