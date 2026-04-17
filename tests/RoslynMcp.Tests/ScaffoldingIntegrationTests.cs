using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ScaffoldingIntegrationTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        InitializeServices();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        DisposeServices();
    }

    [TestMethod]
    public async Task Scaffold_Type_Preview_And_Apply_Creates_New_Type_File()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var targetFilePath = workspace.GetPath("SampleLib", "Generated", "Bird.cs");

        var preview = await ScaffoldingService.PreviewScaffoldTypeAsync(
            workspace.WorkspaceId,
            new ScaffoldTypeDto("SampleLib", "Bird", "class", "SampleLib.Generated"),
            CancellationToken.None);

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, CancellationToken.None);

        Assert.IsTrue(applyResult.Success, applyResult.Error);
        Assert.IsTrue(File.Exists(targetFilePath));
        var contents = await File.ReadAllTextAsync(targetFilePath, CancellationToken.None);
        StringAssert.Contains(contents, "namespace SampleLib.Generated");
        StringAssert.Contains(contents, "class Bird");
    }

    [TestMethod]
    public async Task Scaffold_Type_Default_Class_IsInternalSealed()
    {
        // BUG fix (scaffold-defaults-improvements / a): default class scaffold should be
        // `internal sealed class` instead of `public class` per modern .NET convention.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var targetFilePath = workspace.GetPath("SampleLib", "Generated", "Bird.cs");

        var preview = await ScaffoldingService.PreviewScaffoldTypeAsync(
            workspace.WorkspaceId,
            new ScaffoldTypeDto("SampleLib", "Bird", "class", "SampleLib.Generated"),
            CancellationToken.None);
        await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, CancellationToken.None);

        var contents = await File.ReadAllTextAsync(targetFilePath, CancellationToken.None);
        StringAssert.Contains(contents, "internal sealed class Bird");
        Assert.IsFalse(contents.Contains("public class Bird"),
            "Default class scaffold should not emit 'public class'.");
    }

    [TestMethod]
    public async Task Scaffold_Type_Interface_StaysPublic()
    {
        // Interfaces cannot be `sealed` and are typically intended for cross-assembly use.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var targetFilePath = workspace.GetPath("SampleLib", "Contracts", "IThing.cs");

        var preview = await ScaffoldingService.PreviewScaffoldTypeAsync(
            workspace.WorkspaceId,
            new ScaffoldTypeDto("SampleLib", "IThing", "interface", "SampleLib.Contracts"),
            CancellationToken.None);
        await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, CancellationToken.None);

        var contents = await File.ReadAllTextAsync(targetFilePath, CancellationToken.None);
        StringAssert.Contains(contents, "public interface IThing");
    }

    [TestMethod]
    public async Task Scaffold_Test_CollectionInterfaceCtorArg_UsesArrayEmpty()
    {
        // BUG fix (scaffold-defaults-improvements / c): test stub constructor args for empty
        // collection interfaces should emit `Array.Empty<T>()` so the generated test runs
        // green. Previously every parameter was emitted as `default(T)`, throwing NRE on the
        // first call when the parameter was a non-null collection interface.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);

        // Inject a target type with a constructor that takes IEnumerable<int>.
        var fixturePath = workspace.GetPath("SampleLib", "BulkProcessor.cs");
        await File.WriteAllTextAsync(fixturePath, """
namespace SampleLib;

using System.Collections.Generic;

public class BulkProcessor
{
    private readonly IEnumerable<int> _items;

    public BulkProcessor(IEnumerable<int> items)
    {
        _items = items;
    }

    public int Sum()
    {
        var total = 0;
        foreach (var item in _items)
        {
            total += item;
        }
        return total;
    }
}
""", CancellationToken.None);

        await workspace.ReloadAsync(CancellationToken.None);

        var preview = await ScaffoldingService.PreviewScaffoldTestAsync(
            workspace.WorkspaceId,
            new ScaffoldTestDto("SampleLib.Tests", "BulkProcessor", "Sum"),
            CancellationToken.None);
        await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, CancellationToken.None);

        var contents = await File.ReadAllTextAsync(
            workspace.GetPath("SampleLib.Tests", "BulkProcessorGeneratedTests.cs"),
            CancellationToken.None);

        StringAssert.Contains(contents, "System.Array.Empty<int>()",
            "Constructor arg of type IEnumerable<int> should be emitted as System.Array.Empty<int>().");
        Assert.IsFalse(contents.Contains("default(IEnumerable<int>)"),
            "Should NOT emit default(IEnumerable<int>) — that throws NRE on first iteration.");
    }

    [TestMethod]
    public async Task Scaffold_Type_NamespaceNotMatchingProject_GetsFolderStructure()
    {
        // BUG fix (scaffold-defaults-improvements / b): when the namespace does not start
        // with the project name, the file should still land in folders matching the
        // namespace path — not at the project root.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var expectedPath = workspace.GetPath("SampleLib", "Acme", "Domain", "Widget.cs");

        var preview = await ScaffoldingService.PreviewScaffoldTypeAsync(
            workspace.WorkspaceId,
            new ScaffoldTypeDto("SampleLib", "Widget", "class", "Acme.Domain"),
            CancellationToken.None);
        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, CancellationToken.None);

        Assert.IsTrue(applyResult.Success, applyResult.Error);
        Assert.IsTrue(File.Exists(expectedPath),
            $"Scaffolded type with namespace 'Acme.Domain' should land in 'Acme/Domain/Widget.cs', " +
            $"not at the project root. Expected: {expectedPath}");
    }

    [TestMethod]
    public async Task Scaffold_Test_Preview_And_Apply_Creates_New_Test_File()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var targetFilePath = workspace.GetPath("SampleLib.Tests", "DogGeneratedTests.cs");

        var preview = await ScaffoldingService.PreviewScaffoldTestAsync(
            workspace.WorkspaceId,
            // Opt out of sibling inference so this existing regression asserts the bare
            // scaffold output; the sibling-inference behaviour is covered by its own test.
            new ScaffoldTestDto("SampleLib.Tests", "Dog", "Speak", ReferenceTestFile: string.Empty),
            CancellationToken.None);

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, CancellationToken.None);

        Assert.IsTrue(applyResult.Success, applyResult.Error);
        Assert.IsTrue(File.Exists(targetFilePath));
        var contents = await File.ReadAllTextAsync(targetFilePath, CancellationToken.None);
        StringAssert.Contains(contents, "[TestMethod]");
        StringAssert.Contains(contents, "Speak_Needs_Test");
    }

    [TestMethod]
    public async Task Scaffold_Test_With_ReferenceTestFile_Replicates_IClassFixture_Pattern()
    {
        // Regression for scaffold-test-sibling-pattern-inference: when a referenceTestFile
        // exposes an ASP.NET Core integration-test shape (class attribute + constructor-injected
        // IClassFixture<TestFactory>), the scaffolded output should mirror the class-level
        // attribute, base-list / constructor-fixture shape, and required usings so the
        // convention carries over without a manual rewrite.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);

        // Seed a sibling test file that exercises the full pattern we want replicated.
        var siblingPath = workspace.GetPath("SampleLib.Tests", "HealthEndpointTests.cs");
        await File.WriteAllTextAsync(siblingPath, """
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Acme.Integration.Support;

namespace SampleLib.Tests;

[TestCategory("Integration")]
[TestClass]
public class HealthEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public HealthEndpointTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [TestMethod]
    public void HealthEndpoint_Returns_Ok() { }
}
""", CancellationToken.None);

        await workspace.ReloadAsync(CancellationToken.None);

        var preview = await ScaffoldingService.PreviewScaffoldTestAsync(
            workspace.WorkspaceId,
            new ScaffoldTestDto(
                "SampleLib.Tests",
                "AnimalService",
                ReferenceTestFile: siblingPath),
            CancellationToken.None);
        var applyResult = await RefactoringService.ApplyRefactoringAsync(
            preview.PreviewToken, CancellationToken.None);

        Assert.IsTrue(applyResult.Success, applyResult.Error);
        var scaffoldPath = workspace.GetPath("SampleLib.Tests", "AnimalServiceGeneratedTests.cs");
        Assert.IsTrue(File.Exists(scaffoldPath), $"Expected scaffolded file at {scaffoldPath}.");

        var contents = await File.ReadAllTextAsync(scaffoldPath, CancellationToken.None);

        // Class-level [TestCategory] attribute from the sibling should land on the scaffold.
        // [TestClass] is emitted unconditionally by the MSTest framework builder — the
        // sibling's own [TestClass] is filtered so we don't double-emit it.
        StringAssert.Contains(contents, "[TestCategory(\"Integration\")]",
            "Sibling class-level [TestCategory] should be replicated on the scaffold.");
        var testClassOccurrences = System.Text.RegularExpressions.Regex
            .Matches(contents, @"\[TestClass\]").Count;
        Assert.AreEqual(1, testClassOccurrences,
            "[TestClass] must appear exactly once — the sibling's attribute is filtered so the framework builder emits the canonical one.");

        // Base list / fixture interface: IClassFixture<CustomWebApplicationFactory> carries over.
        StringAssert.Contains(contents, ": IClassFixture<CustomWebApplicationFactory>",
            "IClassFixture<T> base from the sibling should be replicated on the scaffold.");

        // Constructor-injected fixture: the sibling's `CustomWebApplicationFactory factory` parameter
        // lands as a constructor parameter, is stored in a `_factory` field, and is assigned in the body.
        StringAssert.Contains(contents, "private readonly CustomWebApplicationFactory _factory;",
            "Constructor-injected fixture should become a readonly field.");
        StringAssert.Contains(contents,
            "public AnimalServiceGeneratedTests(CustomWebApplicationFactory factory)",
            "Constructor signature should mirror the sibling's constructor parameters.");
        StringAssert.Contains(contents, "_factory = factory;",
            "Constructor body should assign the injected fixture to the generated field.");

        // Required `using` from the sibling carries over so the scaffold compiles.
        StringAssert.Contains(contents, "using Acme.Integration.Support;",
            "Sibling-carried using directive should be replicated so the scaffold resolves its types.");
    }
}
