using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

/// <summary>
/// Tests for the <c>scaffold_first_test_file_preview</c> service surface added to cover
/// the brand-new-fixture flow. Where <c>scaffold_test_preview</c> emits one test method
/// targeted at a specific <c>TargetMethodName</c>, this surface emits a full
/// <c>&lt;Service&gt;Tests.cs</c> fixture with one smoke test per public method on the
/// service plus a class-level setup hook — eliminating the ~30 lines of boilerplate that
/// every fresh service fixture today writes by hand (initiative
/// <c>first-test-for-new-service-scaffold</c>).
///
/// These tests scaffold against <c>SampleLib.Dog</c> (no sibling <c>DogTests.cs</c> ships
/// in the sample solution) so the brand-new-fixture path is exercised without deleting
/// fixture files — keeping the tests robust against the workspace-reload timing issue
/// documented under the <c>ci-test-parallelization-audit</c> backlog row.
/// </summary>
[TestClass]
public sealed class ScaffoldingFirstTestFileTests : IsolatedWorkspaceTestBase
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
    public async Task FirstTestFile_Emits_Fixture_With_OneSmokeTest_Per_PublicMethod()
    {
        // SampleLib.Dog has the public instance methods Speak() and Fetch(string). The
        // first-test-file scaffolder must emit one [TestMethod] per public method.
        // Property accessors (e.g. Dog.Name's getter) must NOT be treated as methods —
        // they are filtered via IMethodSymbol.AssociatedSymbol.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var destinationPath = workspace.GetPath("SampleLib.Tests", "DogTests.cs");
        Assert.IsFalse(File.Exists(destinationPath),
            $"Test precondition violated: '{destinationPath}' must NOT exist for this scenario.");

        var preview = await ScaffoldingService.PreviewScaffoldFirstTestFileAsync(
            workspace.WorkspaceId,
            new ScaffoldFirstTestFileDto("SampleLib.Dog", "SampleLib.Tests"),
            CancellationToken.None);
        var applyResult = await RefactoringService.ApplyRefactoringAsync(
            preview.PreviewToken, CancellationToken.None);

        Assert.IsTrue(applyResult.Success, applyResult.Error);
        Assert.IsTrue(File.Exists(destinationPath),
            $"Scaffolded fixture should land at '{destinationPath}'.");

        var contents = await File.ReadAllTextAsync(destinationPath, CancellationToken.None);

        // Fixture name + class-level shape.
        StringAssert.Contains(contents, "public sealed class DogTests",
            "Fixture should be named '<Service>Tests' (NOT '<Service>GeneratedTests').");
        StringAssert.Contains(contents, "[TestClass]");
        StringAssert.Contains(contents, "[ClassInitialize]");
        StringAssert.Contains(contents, "public static void ClassInit(TestContext _)",
            "MSTest fixture should expose ClassInitialize for shared setup.");
        StringAssert.Contains(contents, "_subject = new Dog()",
            "ClassInit should instantiate the service into a static _subject field.");

        // One smoke-test per public instance method. Property accessors must NOT appear
        // as their own tests — Dog.Name has a `get` accessor that is an IMethodSymbol but
        // its AssociatedSymbol is the Name property; we must filter those out.
        StringAssert.Contains(contents, "public void Speak_Smoke_Needs_Real_Test()");
        StringAssert.Contains(contents, "public void Fetch_Smoke_Needs_Real_Test()");
        Assert.IsFalse(contents.Contains("get_Name"),
            "Property accessors must not leak into the scaffolded smoke tests.");
    }

    [TestMethod]
    public async Task FirstTestFile_FailsWhen_DestinationFile_AlreadyExists()
    {
        // Brand-new-fixture surface MUST refuse to overwrite. Callers should fall back
        // to scaffold_test_preview (which adds method-focused tests to an existing
        // fixture) when a sibling already exists. AnimalServiceTests.cs ships in the
        // SampleLib.Tests project so we use that as the blocked case.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var destinationPath = workspace.GetPath("SampleLib.Tests", "AnimalServiceTests.cs");
        Assert.IsTrue(File.Exists(destinationPath),
            $"Test precondition violated: '{destinationPath}' must exist for this scenario.");

        var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            ScaffoldingService.PreviewScaffoldFirstTestFileAsync(
                workspace.WorkspaceId,
                new ScaffoldFirstTestFileDto("SampleLib.AnimalService", "SampleLib.Tests"),
                CancellationToken.None));

        StringAssert.Contains(ex.Message, "already exists",
            "Error should explain that the destination file is occupied.");
        StringAssert.Contains(ex.Message, "scaffold_test_preview",
            "Error should redirect callers to the additive surface.");
    }

    [TestMethod]
    public async Task FirstTestFile_Infers_TestProject_When_Omitted()
    {
        // When testProjectName is omitted, the scaffolder should pick the project that
        // (a) project-references Dog's containing project AND (b) has a name ending in
        // '.Tests'. SampleLib.Tests is the only candidate in the sample solution that
        // meets both criteria.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var destinationPath = workspace.GetPath("SampleLib.Tests", "DogTests.cs");
        Assert.IsFalse(File.Exists(destinationPath),
            $"Test precondition violated: '{destinationPath}' must NOT exist for this scenario.");

        // Note: testProjectName is null — exercise the inference path.
        var preview = await ScaffoldingService.PreviewScaffoldFirstTestFileAsync(
            workspace.WorkspaceId,
            new ScaffoldFirstTestFileDto(
                ServiceMetadataName: "SampleLib.Dog",
                TestProjectName: null),
            CancellationToken.None);
        var applyResult = await RefactoringService.ApplyRefactoringAsync(
            preview.PreviewToken, CancellationToken.None);

        Assert.IsTrue(applyResult.Success, applyResult.Error);
        Assert.IsTrue(File.Exists(destinationPath),
            "Inference should have picked SampleLib.Tests as the destination project.");
        var contents = await File.ReadAllTextAsync(destinationPath, CancellationToken.None);
        StringAssert.Contains(contents, "namespace SampleLib.Tests;",
            "Scaffolded namespace should be the inferred test project's name.");
    }

    [TestMethod]
    public async Task FirstTestFile_FailsWith_ClearError_When_ServiceMetadataName_NotFound()
    {
        // Misspelled or ambiguous service names are a routine failure mode (especially
        // when the caller passes a bare type name instead of the FQN). The error must
        // call out the FQN expectation so the caller knows to retry with the full
        // namespace.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);

        var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            ScaffoldingService.PreviewScaffoldFirstTestFileAsync(
                workspace.WorkspaceId,
                new ScaffoldFirstTestFileDto("SampleLib.NoSuchService", "SampleLib.Tests"),
                CancellationToken.None));

        StringAssert.Contains(ex.Message, "not found",
            "Error should say the service was not found.");
        StringAssert.Contains(ex.Message, "fully-qualified",
            "Error should remind callers that the input is a metadata name (FQN).");
    }
}
