using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

[DoNotParallelize]
[TestClass]
public sealed class ConsumerAnalysisTests : SharedWorkspaceTestBase
{
    private static string WorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        WorkspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);
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
    public async Task FindConsumers_NonExistentSymbol_ThrowsKeyNotFoundException()
    {
        // After error-response-observability fix: unresolvable handles/metadata names throw
        // KeyNotFoundException so the tool layer's ToolErrorHandler emits a structured
        // NotFound envelope (instead of the old null/empty result that callers could not
        // distinguish from a legitimate "valid symbol, zero consumers" outcome).
        var locator = SymbolLocator.ByMetadataName("SampleLib.NonExistentType");
        await Assert.ThrowsExceptionAsync<KeyNotFoundException>(
            () => ConsumerAnalysisService.FindConsumersAsync(WorkspaceId, locator, CancellationToken.None));
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

    [TestMethod]
    public async Task FindConsumers_StaticClass_ClassifiesAsStaticMemberAccess()
    {
        // find-consumers-static-class-classification: consumers that call static methods on a
        // public static class must be classified as "StaticMemberAccess", not "Other".
        // SampleApp/Program.cs calls AnimalFormatter.Format(...) and AnimalFormatter.FormatAll(...)
        // using explicit static-class syntax — this is the canonical static-consumer pattern.
        var locator = SymbolLocator.ByMetadataName("SampleLib.AnimalFormatter");
        var result = await ConsumerAnalysisService.FindConsumersAsync(WorkspaceId, locator, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.IsTrue(result.Consumers.Count >= 1,
            $"Expected at least 1 consumer of AnimalFormatter, got {result.Consumers.Count}");

        // All dependency kinds reported for AnimalFormatter consumers must NOT include "Other".
        // The presence of "StaticMemberAccess" is the positive assertion.
        var allKinds = result.Consumers.SelectMany(c => c.DependencyKinds).Distinct().ToList();
        CollectionAssert.Contains(allKinds, "StaticMemberAccess",
            $"Expected 'StaticMemberAccess' kind for static-class consumers. Actual kinds: [{string.Join(", ", allKinds)}]");
        CollectionAssert.DoesNotContain(allKinds, "Other",
            $"'Other' kind must not appear for static-class consumers. Actual kinds: [{string.Join(", ", allKinds)}]");
    }
}
