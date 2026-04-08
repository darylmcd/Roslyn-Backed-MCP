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
            new ScaffoldTestDto("SampleLib.Tests", "Dog", "Speak"),
            CancellationToken.None);

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, CancellationToken.None);

        Assert.IsTrue(applyResult.Success, applyResult.Error);
        Assert.IsTrue(File.Exists(targetFilePath));
        var contents = await File.ReadAllTextAsync(targetFilePath, CancellationToken.None);
        StringAssert.Contains(contents, "[TestMethod]");
        StringAssert.Contains(contents, "Speak_Needs_Test");
    }
}
