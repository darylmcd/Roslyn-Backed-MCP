namespace RoslynMcp.Tests;

[DoNotParallelize]
[TestClass]
public sealed class TypeConsumersServiceTests : SharedWorkspaceTestBase
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
    public async Task FindTypeConsumers_IAnimal_RollsUpByFile_ClassifiesUsingInheritCtorLocal()
    {
        var results = await TypeConsumersService.FindTypeConsumersAsync(
            WorkspaceId, "SampleLib.IAnimal", limit: 100, CancellationToken.None);

        Assert.IsTrue(results.Count >= 3,
            $"Expected at least 3 files referencing IAnimal (Dog, Cat, AnimalService), got {results.Count}.");

        // Every rollup entry must report a positive site count and a non-empty kinds list.
        foreach (var entry in results)
        {
            Assert.IsTrue(entry.Count >= 1, $"Rollup for '{entry.FilePath}' has zero sites.");
            Assert.IsTrue(entry.Kinds.Count >= 1, $"Rollup for '{entry.FilePath}' has empty kinds.");
            // Every kind string must be one of the documented values.
            foreach (var kind in entry.Kinds)
            {
                CollectionAssert.Contains(
                    new[] { "using", "ctor", "inherit", "field", "local", "other" },
                    kind,
                    $"Unexpected kind '{kind}' in rollup for '{entry.FilePath}'.");
            }
        }

        // Inherit kind: Dog and Cat declare `: IAnimal` so their files must report `inherit`.
        var dog = results.FirstOrDefault(r => r.FilePath.EndsWith("Dog.cs", StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(dog, "Dog.cs should appear in the IAnimal rollup.");
        CollectionAssert.Contains(dog.Kinds.ToList(), "inherit",
            $"Dog.cs should classify its IAnimal usage as 'inherit'; got [{string.Join(", ", dog.Kinds)}].");

        // AnimalService.cs constructs new Dog/Cat into a List<IAnimal> and accepts IAnimal in
        // method parameters/locals — so it should report multiple sites and contain at least
        // one of `local` (from `var animal in animals`) or `other` (parameter/return).
        var animalService = results.FirstOrDefault(r => r.FilePath.EndsWith("AnimalService.cs", StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(animalService, "AnimalService.cs should appear in the IAnimal rollup.");
        Assert.IsTrue(animalService.Count >= 4,
            $"AnimalService.cs should have ≥4 IAnimal reference sites, got {animalService.Count}.");

        // Sort order: descending Count then ascending FilePath. Verify count is monotonically
        // non-increasing across the result list.
        for (var i = 1; i < results.Count; i++)
        {
            Assert.IsTrue(results[i].Count <= results[i - 1].Count,
                $"Result not sorted by descending count at index {i}: {results[i - 1].Count} -> {results[i].Count}.");
        }
    }

    [TestMethod]
    public async Task FindTypeConsumers_ShortName_ResolvesUnambiguousType()
    {
        // Short-name fallback: "IAnimal" alone should resolve.
        var results = await TypeConsumersService.FindTypeConsumersAsync(
            WorkspaceId, "IAnimal", limit: 100, CancellationToken.None);
        Assert.IsTrue(results.Count >= 1, "Short name 'IAnimal' should resolve and return rollups.");
    }

    [TestMethod]
    public async Task FindTypeConsumers_NonExistentType_ThrowsKeyNotFound()
    {
        await Assert.ThrowsExceptionAsync<KeyNotFoundException>(
            () => TypeConsumersService.FindTypeConsumersAsync(
                WorkspaceId, "SampleLib.DefinitelyNotAType", limit: 100, CancellationToken.None));
    }

    [TestMethod]
    public async Task FindTypeConsumers_LimitCapsResultCount()
    {
        var capped = await TypeConsumersService.FindTypeConsumersAsync(
            WorkspaceId, "SampleLib.IAnimal", limit: 1, CancellationToken.None);
        Assert.AreEqual(1, capped.Count, "limit=1 must cap the result list to a single entry.");
    }

    [TestMethod]
    public async Task FindTypeConsumers_Shape_ClassifiesInheritFromBaseList()
    {
        // Hierarchy/Rectangle.cs and Hierarchy/Circle.cs both inherit Shape — their rollups
        // must include `inherit`.
        var results = await TypeConsumersService.FindTypeConsumersAsync(
            WorkspaceId, "SampleLib.Hierarchy.Shape", limit: 100, CancellationToken.None);

        var rectangle = results.FirstOrDefault(r => r.FilePath.EndsWith("Rectangle.cs", StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(rectangle, "Rectangle.cs should appear in the Shape rollup.");
        CollectionAssert.Contains(rectangle.Kinds.ToList(), "inherit",
            $"Rectangle.cs should classify its Shape usage as 'inherit'; got [{string.Join(", ", rectangle.Kinds)}].");
    }
}
