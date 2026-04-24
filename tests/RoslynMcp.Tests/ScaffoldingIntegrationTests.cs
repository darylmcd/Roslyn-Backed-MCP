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

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);

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
        await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);

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
        await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);

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
        await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);

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
        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);

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

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);

        Assert.IsTrue(applyResult.Success, applyResult.Error);
        Assert.IsTrue(File.Exists(targetFilePath));
        var contents = await File.ReadAllTextAsync(targetFilePath, CancellationToken.None);
        StringAssert.Contains(contents, "[TestMethod]");
        StringAssert.Contains(contents, "Speak_Needs_Test");
    }

    [TestMethod]
    public async Task Scaffold_Test_Private_Target_Method_Adds_Warning_And_Reflection_Call()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var targetTypePath = workspace.GetPath("SampleLib", "HiddenBehavior.cs");
        await File.WriteAllTextAsync(targetTypePath, """
namespace SampleLib;

public class HiddenBehavior
{
    private string BuildSecret()
    {
        return "secret";
    }
}
""", CancellationToken.None);

        await workspace.ReloadAsync(CancellationToken.None);

        var preview = await ScaffoldingService.PreviewScaffoldTestAsync(
            workspace.WorkspaceId,
            new ScaffoldTestDto("SampleLib.Tests", "HiddenBehavior", "BuildSecret", ReferenceTestFile: string.Empty),
            CancellationToken.None);

        Assert.IsNotNull(preview.Warnings);
        Assert.AreEqual(1, preview.Warnings.Count);
        StringAssert.Contains(
            preview.Warnings[0],
            "Target method 'BuildSecret' is private — the scaffold uses reflection to invoke it;");

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applyResult.Success, applyResult.Error);

        var contents = await File.ReadAllTextAsync(
            workspace.GetPath("SampleLib.Tests", "HiddenBehaviorGeneratedTests.cs"),
            CancellationToken.None);

        StringAssert.Contains(contents, "typeof(HiddenBehavior).GetMethod(");
        StringAssert.Contains(contents, "\"BuildSecret\"");
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
            preview.PreviewToken, "test_apply", CancellationToken.None);

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

    [TestMethod]
    public async Task Scaffold_Test_Batch_Preview_And_Apply_Creates_Multiple_Test_Files()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var dogPath = workspace.GetPath("SampleLib.Tests", "DogGeneratedTests.cs");
        var animalServicePath = workspace.GetPath("SampleLib.Tests", "AnimalServiceGeneratedTests.cs");

        var preview = await ScaffoldingService.PreviewScaffoldTestBatchAsync(
            workspace.WorkspaceId,
            new ScaffoldTestBatchDto(
                "SampleLib.Tests",
                [
                    new ScaffoldTestBatchTargetDto("Dog", "Speak"),
                    new ScaffoldTestBatchTargetDto("AnimalService", "GetAllAnimals")
                ],
                "auto"),
            CancellationToken.None);

        Assert.AreEqual(2, preview.Changes.Count,
            "Batch scaffold preview should aggregate one file-creation diff per target.");

        var applyResult = await RefactoringService.ApplyRefactoringAsync(
            preview.PreviewToken, "test_apply", CancellationToken.None);

        Assert.IsTrue(applyResult.Success, applyResult.Error);
        Assert.IsTrue(File.Exists(dogPath), $"Expected scaffolded file at {dogPath}.");
        Assert.IsTrue(File.Exists(animalServicePath), $"Expected scaffolded file at {animalServicePath}.");

        var dogContents = await File.ReadAllTextAsync(dogPath, CancellationToken.None);
        var animalServiceContents = await File.ReadAllTextAsync(animalServicePath, CancellationToken.None);

        StringAssert.Contains(dogContents, "Speak_Needs_Test");
        StringAssert.Contains(animalServiceContents, "GetAllAnimals_Needs_Test");
    }

    [TestMethod]
    public async Task Scaffold_Test_DottedTargetTypeName_TopLevel_Yields_SingleIdentifier_ClassName()
    {
        // Regression for scaffold-test-preview-dotted-identifier: callers who hit the
        // ambiguity-resolution error are told to "use the fully qualified type name" and
        // then re-invoke with `Namespace.Type`. Previously that dotted input was stamped
        // verbatim into the class-name template, emitting
        // `public class SampleLib.Hierarchy.CircleGeneratedTests` — a CS syntax error. The
        // fix uses `TypeSymbol.Name` (unqualified) so only the simple identifier lands in
        // the class and file names, while the `using SampleLib.Hierarchy;` brings the type
        // into scope for the constructor call.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var expectedFilePath = workspace.GetPath("SampleLib.Tests", "CircleGeneratedTests.cs");

        var preview = await ScaffoldingService.PreviewScaffoldTestAsync(
            workspace.WorkspaceId,
            new ScaffoldTestDto(
                "SampleLib.Tests",
                "SampleLib.Hierarchy.Circle",
                ReferenceTestFile: string.Empty),
            CancellationToken.None);

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);

        Assert.IsTrue(applyResult.Success, applyResult.Error);
        Assert.IsTrue(File.Exists(expectedFilePath),
            $"Expected scaffolded file at '{expectedFilePath}' — dotted FQN input must be stripped to the simple name for the filename.");

        var contents = await File.ReadAllTextAsync(expectedFilePath, CancellationToken.None);

        // Class identifier must be a single identifier — a dotted identifier is a CS syntax error.
        StringAssert.Contains(contents, "public class CircleGeneratedTests");
        Assert.IsFalse(contents.Contains("SampleLib.Hierarchy.CircleGeneratedTests"),
            "Scaffold must NOT emit a dotted class identifier — this is a CS syntax error.");

        // The `using` for the target namespace brings Circle into scope for `new Circle(...)`.
        StringAssert.Contains(contents, "using SampleLib.Hierarchy;");
        StringAssert.Contains(contents, "new Circle(");
    }

    [TestMethod]
    public async Task Scaffold_Test_DottedTargetTypeName_NestedType_Yields_SingleIdentifier_ClassName()
    {
        // Companion to the top-level regression above: a nested type's "FQN" looks like
        // `Namespace.Outer.Inner`, and the scaffold must still emit a single-identifier
        // class name. We inject a fixture with a nested type so the test is self-contained
        // and does not depend on sample-solution evolution.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var fixturePath = workspace.GetPath("SampleLib", "NestedFixture.cs");
        await File.WriteAllTextAsync(fixturePath, """
namespace SampleLib;

public class NestedHost
{
    public class NestedInner
    {
        public string Speak() => "inner";
    }
}
""", CancellationToken.None);

        await workspace.ReloadAsync(CancellationToken.None);

        var expectedFilePath = workspace.GetPath("SampleLib.Tests", "NestedInnerGeneratedTests.cs");

        var preview = await ScaffoldingService.PreviewScaffoldTestAsync(
            workspace.WorkspaceId,
            new ScaffoldTestDto(
                "SampleLib.Tests",
                "SampleLib.NestedHost.NestedInner",
                "Speak",
                ReferenceTestFile: string.Empty),
            CancellationToken.None);

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);

        Assert.IsTrue(applyResult.Success, applyResult.Error);
        Assert.IsTrue(File.Exists(expectedFilePath),
            $"Expected scaffolded file at '{expectedFilePath}' — nested-type FQN must be stripped to the simple name.");

        var contents = await File.ReadAllTextAsync(expectedFilePath, CancellationToken.None);

        StringAssert.Contains(contents, "public class NestedInnerGeneratedTests");
        Assert.IsFalse(contents.Contains("NestedHost.NestedInnerGeneratedTests"),
            "Scaffold must NOT emit a dotted class identifier for nested types either.");
        StringAssert.Contains(contents, "Speak_Needs_Test");
    }

    [TestMethod]
    public async Task Scaffold_Test_Preview_StaticClass_Omits_NewT_InArrange()
    {
        // scaffold-test-preview-static-target-body: a `static class` target should scaffold
        // a utility-shaped body — no `new StaticUtil()` and no `var subject = ...` Arrange line.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var fixturePath = workspace.GetPath("SampleLib", "StaticUtil.cs");
        await File.WriteAllTextAsync(fixturePath, """
namespace SampleLib;

public static class StaticUtil
{
    public static int Add(int a, int b) => a + b;
}
""", CancellationToken.None);

        await workspace.ReloadAsync(CancellationToken.None);

        var expectedFilePath = workspace.GetPath("SampleLib.Tests", "StaticUtilGeneratedTests.cs");

        var preview = await ScaffoldingService.PreviewScaffoldTestAsync(
            workspace.WorkspaceId,
            new ScaffoldTestDto(
                "SampleLib.Tests",
                "StaticUtil",
                "Add",
                ReferenceTestFile: string.Empty),
            CancellationToken.None);

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);

        Assert.IsTrue(applyResult.Success, applyResult.Error);
        var contents = await File.ReadAllTextAsync(expectedFilePath, CancellationToken.None);

        Assert.IsFalse(contents.Contains("new StaticUtil("),
            "Static-class scaffold must NOT emit `new StaticUtil(...)` in Arrange.");
        Assert.IsFalse(contents.Contains("var subject ="),
            "Static-class scaffold must NOT emit a `subject` instance.");
    }

    [TestMethod]
    public async Task Scaffold_Test_Preview_ConstantsOnlyClass_Omits_NewT_InArrange()
    {
        // scaffold-test-preview-static-target-body: a non-static class whose only public surface
        // is `const` / static fields (classic "constants" holder e.g. `TenantConstants`) should
        // scaffold without `new TenantConstants()` — previously the scaffolder only inspected
        // methods, so constants-only types fell through to the instance template.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var fixturePath = workspace.GetPath("SampleLib", "TenantConstants.cs");
        await File.WriteAllTextAsync(fixturePath, """
namespace SampleLib;

public class TenantConstants
{
    public const string DefaultTenant = "default";
    public const int MaxConnections = 42;
}
""", CancellationToken.None);

        await workspace.ReloadAsync(CancellationToken.None);

        var expectedFilePath = workspace.GetPath("SampleLib.Tests", "TenantConstantsGeneratedTests.cs");

        var preview = await ScaffoldingService.PreviewScaffoldTestAsync(
            workspace.WorkspaceId,
            new ScaffoldTestDto(
                "SampleLib.Tests",
                "TenantConstants",
                TargetMethodName: null,
                ReferenceTestFile: string.Empty),
            CancellationToken.None);

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);

        Assert.IsTrue(applyResult.Success, applyResult.Error);
        var contents = await File.ReadAllTextAsync(expectedFilePath, CancellationToken.None);

        Assert.IsFalse(contents.Contains("new TenantConstants("),
            "Constants-only class scaffold must NOT emit `new TenantConstants(...)` in Arrange.");
        Assert.IsFalse(contents.Contains("var subject ="),
            "Constants-only class scaffold must NOT emit a `subject` instance.");
    }

    [TestMethod]
    public async Task Scaffold_Test_Preview_StaticMembersOnlyClass_Omits_NewT_InArrange()
    {
        // scaffold-test-preview-static-target-body: a non-static class whose public surface is
        // entirely static members (methods + properties — e.g. `SnapshotContentHasher`) should
        // scaffold as a utility, not as an instance.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var fixturePath = workspace.GetPath("SampleLib", "SnapshotContentHasher.cs");
        await File.WriteAllTextAsync(fixturePath, """
namespace SampleLib;

public class SnapshotContentHasher
{
    public static string Version { get; } = "v1";

    public static string Hash(string content) => content.GetHashCode().ToString();
}
""", CancellationToken.None);

        await workspace.ReloadAsync(CancellationToken.None);

        var expectedFilePath = workspace.GetPath("SampleLib.Tests", "SnapshotContentHasherGeneratedTests.cs");

        var preview = await ScaffoldingService.PreviewScaffoldTestAsync(
            workspace.WorkspaceId,
            new ScaffoldTestDto(
                "SampleLib.Tests",
                "SnapshotContentHasher",
                "Hash",
                ReferenceTestFile: string.Empty),
            CancellationToken.None);

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);

        Assert.IsTrue(applyResult.Success, applyResult.Error);
        var contents = await File.ReadAllTextAsync(expectedFilePath, CancellationToken.None);

        Assert.IsFalse(contents.Contains("new SnapshotContentHasher("),
            "Static-members-only class scaffold must NOT emit `new SnapshotContentHasher(...)` in Arrange.");
        Assert.IsFalse(contents.Contains("var subject ="),
            "Static-members-only class scaffold must NOT emit a `subject` instance.");
    }
}
