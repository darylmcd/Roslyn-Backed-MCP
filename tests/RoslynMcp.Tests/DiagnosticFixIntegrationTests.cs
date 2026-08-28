using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[DoNotParallelize]
[TestClass]
public class DiagnosticFixIntegrationTests : SharedWorkspaceTestBase
{
    private const string SensitiveProviderFailure = "sensitive diagnostic provider C:\\secret\\code.cs";

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

    [TestMethod]
    [DataRow("healthy", 1, true, 0)]
    [DataRow("throwing", 0, false, 1)]
    [DataRow("mixed", 1, false, 1)]
    [DataRow("cancelled", 0, false, 0)]
    public async Task DiagnosticDetails_ReportsProviderEnumerationOutcome(
        string scenario,
        int expectedFixCount,
        bool expectedComplete,
        int expectedFailureCount)
    {
        var (filePath, line, column) = await FindDiagnosticLocationAsync("CS0414");
        var cancellation = new OperationCanceledException("provider cancellation");
        var providers = scenario switch
        {
            "healthy" => new CodeFixProvider[] { new HealthyCodeFixProvider() },
            "throwing" => [new ThrowingCodeFixProvider(new InvalidOperationException(SensitiveProviderFailure))],
            "mixed" =>
            [
                new HealthyCodeFixProvider(),
                new ThrowingCodeFixProvider(new InvalidOperationException(SensitiveProviderFailure)),
            ],
            "cancelled" => [new ThrowingCodeFixProvider(cancellation)],
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown provider scenario."),
        };
        var logger = new CaptureLogger<DiagnosticService>();
        var reporter = new RecordingUnexpectedExceptionReporter("diagnostic-correlation");
        var service = new DiagnosticService(
            WorkspaceManager,
            new CompilationCache(WorkspaceManager),
            new StaticCodeFixProviderRegistry(providers),
            logger,
            reporter);

        if (scenario == "cancelled")
        {
            try
            {
                await service.GetDiagnosticDetailsAsync(
                    WorkspaceId,
                    "CS0414",
                    filePath,
                    line,
                    column,
                    CancellationToken.None);
                Assert.Fail("Provider cancellation must propagate.");
            }
            catch (OperationCanceledException actual)
            {
                Assert.AreSame(cancellation, actual);
            }

            Assert.AreEqual(0, reporter.Reports.Count);
            return;
        }

        var details = await service.GetDiagnosticDetailsAsync(
            WorkspaceId,
            "CS0414",
            filePath,
            line,
            column,
            CancellationToken.None);

        Assert.IsNotNull(details);
        Assert.AreEqual(expectedFixCount, details.SupportedFixes.Count);
        Assert.AreEqual(expectedComplete, details.FixEnumerationComplete);
        Assert.AreEqual(expectedFailureCount, details.FailedProviderCount);
        if (expectedComplete)
        {
            Assert.IsNull(details.GuidanceMessage);
            Assert.AreEqual(0, reporter.Reports.Count);
        }
        else
        {
            Assert.IsNotNull(details.GuidanceMessage);
            StringAssert.Contains(details.GuidanceMessage, "incomplete");
            Assert.IsFalse(
                details.GuidanceMessage.Contains("No code fix provider is currently loaded", StringComparison.Ordinal),
                "A provider crash must not be projected as authoritative provider absence.");
            Assert.AreEqual(expectedFailureCount, reporter.Reports.Count);
            Assert.IsTrue(reporter.Reports.All(report =>
                report.Category == UnexpectedExceptionCategory.AnalysisScan));
            Assert.AreEqual(expectedFailureCount, logger.Entries.Count);
            foreach (var entry in logger.Entries)
            {
                Assert.IsNull(entry.Exception);
                StringAssert.Contains(entry.Message, "InvalidOperationException");
                StringAssert.Contains(entry.Message, "category=AnalysisScan");
                StringAssert.Contains(entry.Message, "correlationId=diagnostic-correlation");
                Assert.IsFalse(entry.Message.Contains(SensitiveProviderFailure, StringComparison.Ordinal));
                Assert.IsFalse(entry.Message.Contains(filePath, StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    private static async Task<(string FilePath, int Line, int Column)> FindDiagnosticLocationAsync(
        string diagnosticId)
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var document = solution.Projects
            .SelectMany(project => project.Documents)
            .First(candidate => candidate.Name == "DiagnosticsProbe.cs");
        var syntaxTree = await document.GetSyntaxTreeAsync(CancellationToken.None);
        Assert.IsNotNull(syntaxTree);
        var compilation = await document.Project.GetCompilationAsync(CancellationToken.None);
        Assert.IsNotNull(compilation);
        var diagnostic = compilation.GetDiagnostics(CancellationToken.None)
            .First(candidate =>
                candidate.Id == diagnosticId &&
                candidate.Location.SourceTree == syntaxTree);
        var start = diagnostic.Location.GetLineSpan().StartLinePosition;
        return (document.FilePath!, start.Line + 1, start.Character + 1);
    }

    private sealed class StaticCodeFixProviderRegistry(IReadOnlyList<CodeFixProvider> providers)
        : ICodeFixProviderRegistry
    {
        public IReadOnlyList<CodeFixProvider> GetProvidersFor(
            string diagnosticId,
            Solution? solution = null) =>
            providers;

        public CodeFixProvider? FirstProviderFor(
            string diagnosticId,
            Solution? solution = null) =>
            providers.FirstOrDefault();
    }

    private sealed class HealthyCodeFixProvider : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds => ["CS0414"];

        public override FixAllProvider? GetFixAllProvider() => null;

        public override Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    "Test provider fix",
                    _ => Task.FromResult(context.Document),
                    equivalenceKey: "test-provider-fix"),
                context.Diagnostics[0]);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingCodeFixProvider(Exception exception) : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds => ["CS0414"];

        public override FixAllProvider? GetFixAllProvider() => null;

        public override Task RegisterCodeFixesAsync(CodeFixContext context) =>
            Task.FromException(exception);
    }

    private sealed class RecordingUnexpectedExceptionReporter(string correlationId)
        : IUnexpectedExceptionReporter
    {
        public List<(Exception Exception, UnexpectedExceptionCategory Category)> Reports { get; } = [];

        public UnexpectedExceptionDetails ReportUnexpected(
            Exception exception,
            UnexpectedExceptionCategory category)
        {
            Reports.Add((exception, category));
            return PublicExceptionDetailPolicy.ProjectUnexpected(exception, correlationId);
        }
    }
}
