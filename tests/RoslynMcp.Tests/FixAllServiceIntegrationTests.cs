using Microsoft.CodeAnalysis.CodeFixes;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[DoNotParallelize]
[TestClass]
public sealed class FixAllServiceIntegrationTests : SharedWorkspaceTestBase
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
    public async Task PreviewFixAll_UnknownDiagnostic_Returns_NoProvider_Guidance()
    {
        var result = await FixAllService.PreviewFixAllAsync(
            WorkspaceId,
            diagnosticId: "ZZZ_DOES_NOT_EXIST_999",
            scope: "solution",
            filePath: null,
            projectName: null,
            CancellationToken.None).ConfigureAwait(false);

        Assert.IsTrue(string.IsNullOrEmpty(result.PreviewToken));
        Assert.AreEqual(0, result.FixedCount);
        Assert.IsFalse(string.IsNullOrEmpty(result.GuidanceMessage), "Expected guidance when no fixer is available.");
        StringAssert.Contains(result.GuidanceMessage ?? "", "No code fix provider");
    }

    /// <summary>
    /// CS8019 is handled by <see cref="RefactoringService.PreviewCodeFixAsync"/> today; <see cref="FixAllService"/>
    /// only sees fixers loaded Features/analyzer assemblies and may not register a CS8019 FixAll provider.
    /// Pin the structured response so callers get guidance instead of an empty silent failure.
    /// </summary>
    [TestMethod]
    public async Task PreviewFixAll_CS8019_Returns_Guidance_When_No_FixAll_Provider_Loaded()
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var animalServiceFile = solution.Projects
            .SelectMany(project => project.Documents)
            .First(document => document.Name == "AnimalService.cs");

        var preview = await FixAllService.PreviewFixAllAsync(
            WorkspaceId,
            diagnosticId: "CS8019",
            scope: "document",
            filePath: animalServiceFile.FilePath!,
            projectName: null,
            CancellationToken.None).ConfigureAwait(false);

        Assert.IsTrue(string.IsNullOrEmpty(preview.PreviewToken));
        Assert.AreEqual(0, preview.FixedCount);
        StringAssert.Contains(preview.GuidanceMessage ?? "", "No code fix provider");
    }

    /// <summary>
    /// fixall-blank-projectname-silent-wrong-target: <see cref="ProjectFilterHelper"/>'s
    /// blank-means-no-filter semantics (PR #1191) made a whitespace/omitted projectName return
    /// every project instead of none, so the old FirstOrDefault-based resolution in
    /// FixAllService.ResolveTargets silently targeted solution.Projects.First() rather than
    /// erroring. Pin that scope 'project' now fails loud on a blank projectName instead of
    /// previewing fixes against the wrong project.
    /// </summary>
    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public async Task PreviewFixAll_ProjectScope_BlankProjectName_Throws_Instead_Of_Targeting_First_Project(
        string? projectName)
    {
        // CS8019 has no FixAll provider in this workspace. The validation must still run first,
        // otherwise provider discovery masks the malformed project-scoped request.
        var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await FixAllService.PreviewFixAllAsync(
                WorkspaceId,
                diagnosticId: "CS8019",
                scope: "project",
                filePath: null,
                projectName: projectName,
                CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);

        StringAssert.Contains(ex.Message, "projectName");
        Assert.AreEqual("projectName", ex.ParamName);
    }

    [TestMethod]
    [DataRow("document")]
    [DataRow("project")]
    [DataRow("solution")]
    public async Task TargetResolutionAndDiagnosticCollection_PreserveScopeBehavior(string scope)
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var diagnosticDocument = solution.Projects
            .SelectMany(project => project.Documents)
            .First(document => document.Name == "DiagnosticsProbe.cs");
        var filePath = scope == "document" ? diagnosticDocument.FilePath : null;
        var projectName = scope == "project" ? diagnosticDocument.Project.Name : null;
        var request = FixAllTargetResolver.ParseAndValidate(scope, filePath, projectName);
        var target = FixAllTargetResolver.Resolve(solution, request);
        var collector = new FixAllDiagnosticCollector(new CompilationCache(WorkspaceManager));

        var diagnostics = await collector.CollectAsync(
            new FixAllDiagnosticCollectionContext(
                WorkspaceId,
                solution,
                "CS0414",
                request.Scope,
                target.Document,
                target.Project,
                []),
            CancellationToken.None).ConfigureAwait(false);

        Assert.IsTrue(
            diagnostics.Values.SelectMany(items => items).Any(diagnostic => diagnostic.Id == "CS0414"),
            $"Expected compiler fallback to collect CS0414 at {scope} scope.");

        if (request.Scope == FixAllScope.Document)
        {
            Assert.IsTrue(diagnostics.Keys.All(document => document.Id == diagnosticDocument.Id));
        }
        else if (request.Scope == FixAllScope.Project)
        {
            Assert.IsTrue(diagnostics.Keys.All(document => document.Project.Id == diagnosticDocument.Project.Id));
        }
    }

    [TestMethod]
    [DataRow("document", null, null, "filePath")]
    [DataRow("project", null, null, "projectName")]
    [DataRow("invalid", null, null, "scope")]
    public void TargetResolution_RejectsMalformedInputBeforeWorkspaceLookup(
        string scope,
        string? filePath,
        string? projectName,
        string expectedParameter)
    {
        var exception = Assert.ThrowsExactly<ArgumentException>(() =>
            FixAllTargetResolver.ParseAndValidate(scope, filePath, projectName));

        Assert.AreEqual(expectedParameter, exception.ParamName);
    }
}
