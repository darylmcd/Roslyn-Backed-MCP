namespace RoslynMcp.Tests;

[DoNotParallelize]
[TestClass]
public class DiagnosticFixIntegrationTests : SharedWorkspaceTestBase
{
    private static string WorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        WorkspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        DisposeServices();
    }

    [TestMethod]
    public async Task Diagnostic_Details_For_CS8019_Honors_Docs_Match_Reality()
    {
        // diagnostic-details-supported-fixes-empty: SupportedFixes is sourced from
        // CodeFixProviderRegistry. CS8019 ("unused using") is NOT carried in the static
        // CSharp.Features CodeFixProvider pack — Roslyn's IDE handles unused-using removal
        // via a refactoring/feature path that does not register a CodeFixProvider with
        // FixableDiagnosticIds.Contains("CS8019"). Hence the registry returns 0 providers
        // and the contract is: SupportedFixes == [] AND GuidanceMessage points at
        // get_code_actions. Note that code_fix_preview still removes the using via the
        // RefactoringService fallback path (covered by
        // Code_Fix_Preview_And_Apply_Removes_Unused_Using_In_Isolated_Copy below).
        _ = typeof(Microsoft.CodeAnalysis.CSharp.Formatting.CSharpFormattingOptions);

        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var animalServiceFile = solution.Projects.SelectMany(project => project.Documents).First(document => document.Name == "AnimalService.cs");

        var details = await DiagnosticService.GetDiagnosticDetailsAsync(
            WorkspaceId,
            diagnosticId: "CS8019",
            filePath: animalServiceFile.FilePath!,
            line: 1,
            column: 1,
            CancellationToken.None);

        Assert.IsNotNull(details);
        Assert.AreEqual("CS8019", details.Diagnostic.Id);

        if (details.SupportedFixes.Count == 0)
        {
            Assert.IsNotNull(details.GuidanceMessage,
                "When CS8019 has no registered CodeFixProvider, GuidanceMessage must point at get_code_actions.");
            StringAssert.Contains(details.GuidanceMessage, "get_code_actions");
            StringAssert.Contains(details.GuidanceMessage, "CS8019");
        }
        else
        {
            // Future-proofing: if Roslyn ships a CS8019 CodeFixProvider in a later release,
            // the populated branch must at least name a using/import-related fix.
            Assert.IsNull(details.GuidanceMessage,
                "When SupportedFixes is populated, GuidanceMessage must be null.");
            Assert.IsTrue(
                details.SupportedFixes.Any(fix =>
                    fix.Title.Contains("using", StringComparison.OrdinalIgnoreCase) ||
                    fix.Title.Contains("import", StringComparison.OrdinalIgnoreCase)),
                $"CS8019 fixes should mention using/import. Got: {string.Join(", ", details.SupportedFixes.Select(f => f.Title))}");
        }
    }

    [TestMethod]
    public async Task Diagnostic_Details_For_CS0414_Returns_Provider_Backed_Fix_Or_Guidance()
    {
        // diagnostic-details-supported-fixes-empty: positive coverage of the registry
        // populating SupportedFixes from a real diagnostic. CS0414 ("field assigned but
        // never used") is emitted by the SampleLib DiagnosticsProbe fixture and is fixable
        // by the static CSharp.Features RemoveUnusedMembersCodeFixProvider. Asserts the
        // contract honestly: either fixes populate (and guidance is null) OR the registry
        // doesn't ship a provider (and guidance is populated). This prevents the bug we're
        // fixing — empty fixes WITHOUT guidance, while the description promises curated
        // fixes — from regressing.
        _ = typeof(Microsoft.CodeAnalysis.CSharp.Formatting.CSharpFormattingOptions);

        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var probeFile = solution.Projects.SelectMany(p => p.Documents).First(d => d.Name == "DiagnosticsProbe.cs");

        var compilation = await probeFile.Project.GetCompilationAsync(CancellationToken.None);
        Assert.IsNotNull(compilation);
        var cs0414 = compilation.GetDiagnostics(CancellationToken.None)
            .FirstOrDefault(d => d.Id == "CS0414" && d.Location.IsInSource &&
                                 string.Equals(d.Location.SourceTree?.FilePath, probeFile.FilePath, StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(cs0414, "SampleLib DiagnosticsProbe fixture should emit CS0414.");

        var lineSpan = cs0414.Location.GetLineSpan();
        var details = await DiagnosticService.GetDiagnosticDetailsAsync(
            WorkspaceId,
            diagnosticId: "CS0414",
            filePath: probeFile.FilePath!,
            line: lineSpan.StartLinePosition.Line + 1,
            column: lineSpan.StartLinePosition.Character + 1,
            CancellationToken.None);

        Assert.IsNotNull(details);
        Assert.AreEqual("CS0414", details.Diagnostic.Id);

        // The honest-contract envelope: SupportedFixes and GuidanceMessage are mutually
        // exclusive in their non-empty/non-null forms. The previous bug was advertising
        // "curated fix options" while always returning [] AND null guidance.
        if (details.SupportedFixes.Count > 0)
        {
            Assert.IsNull(details.GuidanceMessage,
                "When SupportedFixes is populated, GuidanceMessage must be null.");
            foreach (var fix in details.SupportedFixes)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(fix.FixId), "FixId must not be empty.");
                Assert.IsFalse(string.IsNullOrWhiteSpace(fix.Title), "Title must not be empty.");
            }
        }
        else
        {
            Assert.IsNotNull(details.GuidanceMessage,
                "When SupportedFixes is empty, GuidanceMessage must direct callers to get_code_actions.");
            StringAssert.Contains(details.GuidanceMessage, "get_code_actions");
        }
    }

    /// <summary>
    /// code-fix-preview-vs-fix-all-preview-shape-inconsistency: when no <c>CodeFixProvider</c>
    /// is registered for the requested diagnostic id, <c>code_fix_preview</c> must return a
    /// structured envelope (empty <c>PreviewToken</c> + empty <c>Changes</c> +
    /// <c>GuidanceMessage</c>) rather than throwing <c>InvalidOperationException</c>. Mirrors
    /// the shape <see cref="RoslynMcp.Roslyn.Services.FixAllService.PreviewFixAllAsync"/>
    /// already emits in the same condition (see
    /// <c>FixAllServiceGuidanceTests.PreviewFixAll_NoProviderRegistered_ReturnsGuidance</c>).
    ///
    /// <para>
    /// To exercise this branch we use CS8019 + a non-default fixId. The static
    /// <c>CodeFixProviderRegistry</c> does not ship a CS8019 provider (covered by the CS8019
    /// diagnostic_details test above), so the registry path returns null. With fixId set to
    /// something other than "remove_unused_using", the legacy CS8019 fallback condition fails
    /// and control falls through to the new structured-return branch.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task CodeFixPreview_NoProviderRegistered_ReturnsGuidance()
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var animalServiceFile = solution.Projects
            .SelectMany(project => project.Documents)
            .First(document => document.Name == "AnimalService.cs");

        var preview = await RefactoringService.PreviewCodeFixAsync(
            WorkspaceId,
            diagnosticId: "CS8019",
            filePath: animalServiceFile.FilePath!,
            line: 1,
            column: 1,
            fixId: "non_default_fix_id_to_skip_fallback",
            CancellationToken.None);

        Assert.AreEqual(string.Empty, preview.PreviewToken,
            "When no provider is registered, PreviewToken must be empty so callers do not try to apply.");
        Assert.AreEqual(0, preview.Changes.Count,
            "When no provider is registered, Changes must be empty.");
        Assert.IsNotNull(preview.GuidanceMessage,
            "When no provider is registered, GuidanceMessage must be set — mirrors fix_all_preview.");
        StringAssert.Contains(preview.GuidanceMessage!, "No code fix provider is loaded");
        StringAssert.Contains(preview.GuidanceMessage!, "CS8019");
    }

    [TestMethod]
    public async Task Code_Fix_Preview_And_Apply_Removes_Unused_Using_In_Isolated_Copy()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;
        var serviceFilePath = Path.Combine(copiedRoot, "SampleLib", "AnimalService.cs");

        try
        {
            var copiedWorkspace = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
            var preview = await RefactoringService.PreviewCodeFixAsync(
                copiedWorkspace.WorkspaceId,
                diagnosticId: "CS8019",
                filePath: serviceFilePath,
                line: 1,
                column: 1,
                fixId: "remove_unused_using",
                CancellationToken.None);

            Assert.IsTrue(preview.Changes.Count > 0, "Code fix preview should produce changes.");

            var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);

            Assert.IsTrue(applyResult.Success, applyResult.Error);
            var contents = await File.ReadAllTextAsync(serviceFilePath, CancellationToken.None);
            Assert.IsFalse(contents.Contains("using System.Threading;"));
        }
        finally
        {
            DeleteDirectoryIfExists(copiedRoot);
        }
    }
}
