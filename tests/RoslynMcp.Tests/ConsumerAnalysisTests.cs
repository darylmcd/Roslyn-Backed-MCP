using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ConsumerAnalysisTests : TestBase
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
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task FindConsumers_IAnimal_ReturnsConsumingTypes()
    {
        var locator = SymbolLocator.ByMetadataName("SampleLib.IAnimal");
        var result = await ConsumerAnalysisService.FindConsumersAsync(WorkspaceId, locator, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.IsTrue(result.Consumers.Count >= 3,
            $"Expected at least 3 consumers of IAnimal (Dog, Cat, AnimalService), got {result.Consumers.Count}");

        var consumerNames = result.Consumers.Select(c => c.TypeName).ToList();
        CollectionAssert.Contains(consumerNames, "Dog");
        CollectionAssert.Contains(consumerNames, "Cat");
    }

    [TestMethod]
    public async Task FindConsumers_NonExistentSymbol_ReturnsNull()
    {
        var locator = SymbolLocator.ByMetadataName("SampleLib.NonExistentType");
        var result = await ConsumerAnalysisService.FindConsumersAsync(WorkspaceId, locator, CancellationToken.None);
        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task FindConsumers_Shape_ReturnsRectangleAsConsumer()
    {
        var locator = SymbolLocator.ByMetadataName("SampleLib.Hierarchy.Shape");
        var result = await ConsumerAnalysisService.FindConsumersAsync(WorkspaceId, locator, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.IsTrue(result.Consumers.Count >= 1,
            $"Expected at least 1 consumer of Shape, got {result.Consumers.Count}");

        var rectangle = result.Consumers.FirstOrDefault(c => c.TypeName == "Rectangle");
        Assert.IsNotNull(rectangle, "Rectangle should consume Shape");
        CollectionAssert.Contains(rectangle.DependencyKinds.ToList(), "BaseType");
    }
}
