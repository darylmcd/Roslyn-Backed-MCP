using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

/// <summary>
/// Verifies goto-type-definition-local-vars: navigating from a local variable whose declared
/// type is a constructed generic from metadata (e.g. <c>List&lt;IAnimal&gt;</c>) lands on the
/// in-source type argument (<c>IAnimal</c>) rather than failing with "No type definition found."
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class GoToTypeDefinitionTests : SharedWorkspaceTestBase
{
    private static string _workspaceId = string.Empty;
    private static string _animalServicePath = string.Empty;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        _workspaceId = await LoadSharedSampleWorkspaceAsync();
        var sampleSolutionRoot = Path.GetDirectoryName(SampleSolutionPath)!;
        _animalServicePath = Path.Combine(sampleSolutionRoot, "SampleLib", "AnimalService.cs");
    }

    [TestMethod]
    public async Task GoToTypeDefinition_LocalOfBclGenericWithSourceArgument_ReturnsArgumentLocation()
    {
        // AnimalService.cs line 18: `foreach (var animal in animals)` where animals is
        // IEnumerable<IAnimal>. Pre-fix: returns empty list because IEnumerable<T> has no
        // in-source location and the SpecialType.None guard short-circuited the result.
        // Post-fix: walks the type arguments and returns IAnimal's source location.
        var locator = SymbolLocator.BySource(_animalServicePath, line: 18, column: 22);
        var locations = await SymbolNavigationService.GoToTypeDefinitionAsync(
            _workspaceId, locator, CancellationToken.None);

        Assert.IsTrue(locations.Count > 0,
            "Expected at least one location for the local variable's type or its in-source type argument.");
        Assert.IsTrue(
            locations.Any(l => l.FilePath?.EndsWith("IAnimal.cs", StringComparison.OrdinalIgnoreCase) == true) ||
            locations.Any(l => l.FilePath?.EndsWith("AnimalService.cs", StringComparison.OrdinalIgnoreCase) == true),
            $"Expected navigation to IAnimal.cs (type argument) or AnimalService.cs. Got: " +
            string.Join(", ", locations.Select(l => l.FilePath)));
    }

    [TestMethod]
    public async Task GoToTypeDefinition_BclPrimitive_ThrowsWithDescriptiveMessage()
    {
        // AnimalService.cs line 21: `Console.WriteLine($"{animal.Name} says {sound}");`
        // Cursor on `sound` (line 20 col 13 declared as `var sound = animal.Speak();` returning string).
        var locator = SymbolLocator.BySource(_animalServicePath, line: 20, column: 17);

        var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            SymbolNavigationService.GoToTypeDefinitionAsync(
                _workspaceId, locator, CancellationToken.None));

        StringAssert.Contains(ex.Message, "Cannot navigate to type definition");
    }
}
