using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Services;

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
            preview.PreviewToken, "test_apply", CancellationToken.None);

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
            preview.PreviewToken, "test_apply", CancellationToken.None);

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

    [TestMethod]
    public async Task FirstTestFile_InfersSuffix_When_MultipleTestProjects_Reference_Same_Library()
    {
        // scaffold-first-test-file-preview-single-target-heuristic: when multiple test projects
        // transitively reference the same library, the inference must apply a name-suffix
        // tiebreaker — pick the candidate whose Name is exactly `<Library>.Tests`. The shipping
        // SampleLib.Tests is the canonical match; we plant a sibling SampleLib.IntegrationTests
        // that also references SampleLib and assert the scaffolder still lands the new file in
        // SampleLib.Tests.
        var workspace = CreateIsolatedWorkspaceCopy();
        try
        {
            await WriteSiblingTestProjectAsync(workspace, "SampleLib.IntegrationTests", CancellationToken.None);
            await RestoreWorkspaceAsync(workspace, CancellationToken.None);
            await workspace.LoadAsync(CancellationToken.None);

            var winningPath = workspace.GetPath("SampleLib.Tests", "DogTests.cs");
            var losingPath = workspace.GetPath("SampleLib.IntegrationTests", "DogTests.cs");
            Assert.IsFalse(File.Exists(winningPath),
                $"Test precondition violated: '{winningPath}' must NOT exist for this scenario.");
            Assert.IsFalse(File.Exists(losingPath),
                $"Test precondition violated: '{losingPath}' must NOT exist for this scenario.");

            // Note: testProjectName is null — exercise the inference path with TWO candidates.
            // Without the suffix tiebreaker, this previously threw
            // "Multiple test projects reference 'SampleLib': SampleLib.Tests, SampleLib.IntegrationTests."
            var preview = await ScaffoldingService.PreviewScaffoldFirstTestFileAsync(
                workspace.WorkspaceId,
                new ScaffoldFirstTestFileDto(
                    ServiceMetadataName: "SampleLib.Dog",
                    TestProjectName: null),
                CancellationToken.None);
            var applyResult = await RefactoringService.ApplyRefactoringAsync(
                preview.PreviewToken, "test_apply", CancellationToken.None);

            Assert.IsTrue(applyResult.Success, applyResult.Error);
            Assert.IsTrue(File.Exists(winningPath),
                "Suffix tiebreaker should have selected SampleLib.Tests (matches '<Library>.Tests' convention).");
            Assert.IsFalse(File.Exists(losingPath),
                "Non-matching candidate SampleLib.IntegrationTests must not receive the scaffolded fixture.");

            var contents = await File.ReadAllTextAsync(winningPath, CancellationToken.None);
            StringAssert.Contains(contents, "namespace SampleLib.Tests;",
                "Scaffolded namespace should be the canonical-suffix test project's namespace.");
        }
        finally
        {
            await workspace.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task FirstTestFile_StillFails_When_Multiple_Suffix_Matches_OR_Zero_Suffix_Match()
    {
        // scaffold-first-test-file-preview-single-target-heuristic: the suffix tiebreaker is
        // intentionally conservative. The existing pre-filter (line ~408 of
        // ScaffoldingService.TestBatchAndFirstTestPreview.cs) only admits candidates whose Name
        // ends with `.Tests`. Within that pool, the tiebreaker prefers the candidate whose Name
        // is exactly `<SourceProject>.Tests`. When NO candidate matches the exact suffix (e.g.
        // sibling apps `WebApi.Tests` and `ConsoleHost.Tests` both reference the shared domain
        // library `SampleLib`), the service must still surface a clear error rather than
        // picking arbitrarily. Workspace topology: replace the shipping SampleLib.Tests so the
        // ONLY candidates are non-matching siblings (and `SampleLib.Tests` itself is absent
        // from the candidate pool, so the suffix-match set is empty).
        var workspace = CreateIsolatedWorkspaceCopy();
        try
        {
            ReplaceSolutionFile(workspace, new[]
            {
                "SampleApp/SampleApp.csproj",
                "SampleLib/SampleLib.csproj",
                "WebApi.Tests/WebApi.Tests.csproj",
                "ConsoleHost.Tests/ConsoleHost.Tests.csproj",
            });
            await WriteSiblingTestProjectAsync(workspace, "WebApi.Tests", CancellationToken.None);
            await WriteSiblingTestProjectAsync(workspace, "ConsoleHost.Tests", CancellationToken.None);
            await RestoreWorkspaceAsync(workspace, CancellationToken.None);
            await workspace.LoadAsync(CancellationToken.None);

            var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                ScaffoldingService.PreviewScaffoldFirstTestFileAsync(
                    workspace.WorkspaceId,
                    new ScaffoldFirstTestFileDto(
                        ServiceMetadataName: "SampleLib.Dog",
                        TestProjectName: null),
                    CancellationToken.None));

            StringAssert.Contains(ex.Message, "Multiple test projects reference",
                "Error must explain ambiguity is the root cause.");
            StringAssert.Contains(ex.Message, "WebApi.Tests",
                "Error must enumerate the ambiguous candidates so callers can choose explicitly.");
            StringAssert.Contains(ex.Message, "ConsoleHost.Tests",
                "Error must enumerate the ambiguous candidates so callers can choose explicitly.");
            StringAssert.Contains(ex.Message, "Pass testProjectName explicitly",
                "Error must point callers at the explicit-disambiguation escape hatch.");
            StringAssert.Contains(ex.Message, "SampleLib.Tests",
                "Error must surface the canonical suffix name that would have unlocked the tiebreaker.");
        }
        finally
        {
            await workspace.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task FirstTestFile_EmitsUsings_ForCtorParamNamespaces()
    {
        // scaffold-test-preview-missing-usings: scaffold_first_test_file_preview previously
        // only emitted a using directive for the service's own namespace. Constructor
        // parameter types from other namespaces were silently omitted, producing 7+ CS0246
        // errors in the generated file. This test verifies that all three constructor-
        // parameter namespaces of SampleLib.MultiNamespaceService are emitted.
        //
        // MultiNamespaceService(TextWriter writer, StringBuilder buffer, IList<string> items)
        //   → using System.IO;               (TextWriter)
        //   → using System.Text;             (StringBuilder)
        //   → using System.Collections.Generic; (IList<T>)
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var destinationPath = workspace.GetPath("SampleLib.Tests", "MultiNamespaceServiceTests.cs");
        Assert.IsFalse(File.Exists(destinationPath),
            $"Test precondition violated: '{destinationPath}' must NOT exist before scaffolding.");

        var preview = await ScaffoldingService.PreviewScaffoldFirstTestFileAsync(
            workspace.WorkspaceId,
            new ScaffoldFirstTestFileDto("SampleLib.MultiNamespaceService", "SampleLib.Tests"),
            CancellationToken.None);
        var applyResult = await RefactoringService.ApplyRefactoringAsync(
            preview.PreviewToken, "test_apply", CancellationToken.None);

        Assert.IsTrue(applyResult.Success, applyResult.Error);
        var contents = await File.ReadAllTextAsync(destinationPath, CancellationToken.None);

        // Each constructor-parameter namespace must appear as a using directive so the
        // generated file compiles without CS0246 errors.
        StringAssert.Contains(contents, "using System.IO;",
            "System.IO must be emitted for the TextWriter ctor parameter.");
        StringAssert.Contains(contents, "using System.Text;",
            "System.Text must be emitted for the StringBuilder ctor parameter.");
        StringAssert.Contains(contents, "using System.Collections.Generic;",
            "System.Collections.Generic must be emitted for the IList<string> ctor parameter.");
    }

    /// <summary>
    /// Writes a sibling test project under <paramref name="workspace"/>.RootPath that mirrors
    /// the shape of <c>SampleLib.Tests</c> (project-references <c>SampleLib</c>, uses the same
    /// central-package test-SDK dependencies) but is named <paramref name="projectName"/>.
    /// Caller must call <see cref="RestoreWorkspaceAsync"/> AND, if the new project should be
    /// part of the loaded solution, ensure the .slnx file references it (the helper handles
    /// folder creation + csproj writing; .slnx wiring is the caller's responsibility because
    /// some tests want the project on disk but excluded from the workspace).
    /// </summary>
    private static async Task WriteSiblingTestProjectAsync(
        IsolatedWorkspaceScope workspace,
        string projectName,
        CancellationToken ct)
    {
        var projectDir = workspace.GetPath(projectName);
        Directory.CreateDirectory(projectDir);

        var csprojPath = Path.Combine(projectDir, $"{projectName}.csproj");
        // Mirrors SampleLib.Tests.csproj — same SDK packages so the temp workspace's existing
        // Directory.Packages.props central versions cover the new project too, keeping the
        // follow-up `dotnet restore` to a single pass.
        const string csprojContent = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <IsTestProject>true</IsTestProject>
                <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
              </PropertyGroup>

              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" />
                <PackageReference Include="MSTest.TestAdapter" />
                <PackageReference Include="MSTest.TestFramework" />
              </ItemGroup>

              <ItemGroup>
                <ProjectReference Include="..\SampleLib\SampleLib.csproj" />
              </ItemGroup>
            </Project>
            """;
        await File.WriteAllTextAsync(csprojPath, csprojContent, ct).ConfigureAwait(false);

        // The .slnx is only touched if the project should join the solution. By default we add
        // the new project to the existing .slnx via simple text append (the format is a single
        // <Project Path="..."/> per line, terminated by </Solution>). Tests that want a wholesale
        // .slnx rewrite call ReplaceSolutionFile instead AND skip this slnx-merge step.
        var slnxContents = await File.ReadAllTextAsync(workspace.SolutionPath, ct).ConfigureAwait(false);
        var projectLine = $"  <Project Path=\"{projectName}/{projectName}.csproj\" />";
        if (!slnxContents.Contains(projectLine, StringComparison.Ordinal))
        {
            // Defensive: only insert when the .slnx still has the closing tag we expect. If a
            // prior helper has already rewritten the .slnx (e.g. via ReplaceSolutionFile), the
            // expected closing tag may be absent and the caller is responsible for wiring.
            var closingTag = "</Solution>";
            var closeIndex = slnxContents.LastIndexOf(closingTag, StringComparison.Ordinal);
            if (closeIndex >= 0)
            {
                var prefix = slnxContents[..closeIndex].TrimEnd('\r', '\n', ' ');
                var updated = $"{prefix}{Environment.NewLine}{projectLine}{Environment.NewLine}{closingTag}{Environment.NewLine}";
                await File.WriteAllTextAsync(workspace.SolutionPath, updated, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Rewrites <paramref name="workspace"/>.SolutionPath with a fresh .slnx whose &lt;Project&gt;
    /// entries are exactly <paramref name="relativeProjectPaths"/>. Used by tests that need to
    /// exclude shipping projects (e.g. removing <c>SampleLib.Tests</c> so the suffix-match set is
    /// empty). Caller must invoke <see cref="RestoreWorkspaceAsync"/> after wiring all relevant
    /// project files on disk.
    /// </summary>
    private static void ReplaceSolutionFile(IsolatedWorkspaceScope workspace, IEnumerable<string> relativeProjectPaths)
    {
        var lines = new List<string> { "<Solution>" };
        foreach (var path in relativeProjectPaths)
        {
            lines.Add($"  <Project Path=\"{path}\" />");
        }
        lines.Add("</Solution>");
        File.WriteAllText(workspace.SolutionPath, string.Join(Environment.NewLine, lines) + Environment.NewLine);
    }

    /// <summary>
    /// Runs <c>dotnet restore</c> on <paramref name="workspace"/>.SolutionPath so newly-written
    /// sibling test projects acquire their <c>obj/project.assets.json</c> before the workspace
    /// is loaded. The shipping sample-solution copy is restored ahead-of-time by
    /// <c>verify-release.ps1</c>; tests that mutate the project graph must restore the deltas.
    /// </summary>
    private static async Task RestoreWorkspaceAsync(IsolatedWorkspaceScope workspace, CancellationToken ct)
    {
        var execution = await DotnetCommandRunner.RunAsync(
            workingDirectory: workspace.RootPath,
            targetPath: workspace.SolutionPath,
            arguments: ["restore", workspace.SolutionPath, "--nologo"],
            ct).ConfigureAwait(false);

        Assert.IsTrue(
            execution.Succeeded,
            $"dotnet restore failed for test fixture. ExitCode={execution.ExitCode} StdOut={execution.StdOut} StdErr={execution.StdErr}");
    }
}
