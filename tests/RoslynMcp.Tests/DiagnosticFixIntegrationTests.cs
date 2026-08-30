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
            Assert.Contains("get_code_actions", details.GuidanceMessage);
            Assert.Contains("CS8019", details.GuidanceMessage);
        }
        else
        {
            // Future-proofing: if Roslyn ships a CS8019 CodeFixProvider in a later release,
            // the populated branch must at least name a using/import-related fix.
            Assert.IsNull(details.GuidanceMessage,
                "When SupportedFixes is populated, GuidanceMessage must be null.");
            Assert.Contains(
                fix =>
                    fix.Title.Contains("using", StringComparison.OrdinalIgnoreCase) ||
                    fix.Title.Contains("import", StringComparison.OrdinalIgnoreCase),
                details.SupportedFixes,
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
            Assert.Contains("get_code_actions", details.GuidanceMessage);
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
        Assert.IsEmpty(preview.Changes,
            "When no provider is registered, Changes must be empty.");
        Assert.IsNotNull(preview.GuidanceMessage,
            "When no provider is registered, GuidanceMessage must be set — mirrors fix_all_preview.");
        Assert.Contains("No code fix provider is loaded", preview.GuidanceMessage!);
        Assert.Contains("CS8019", preview.GuidanceMessage!);
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

            Assert.IsNotEmpty(preview.Changes, "Code fix preview should produce changes.");

            var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);

            Assert.IsTrue(applyResult.Success, applyResult.Error);
            var contents = await File.ReadAllTextAsync(serviceFilePath, CancellationToken.None);
            Assert.DoesNotContain("using System.Threading;", contents);
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
    public async Task SupportedFixEnumeration_ReportsProviderOutcome(
        string scenario,
        int expectedFixCount,
        bool expectedComplete,
        int expectedFailureCount)
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var document = solution.Projects
            .SelectMany(project => project.Documents)
            .First(candidate => candidate.Name == "DiagnosticsProbe.cs");
        var compilation = await document.Project.GetCompilationAsync(CancellationToken.None);
        Assert.IsNotNull(compilation);
        var diagnostic = compilation.GetDiagnostics(CancellationToken.None)
            .First(candidate =>
                candidate.Id == "CS0414" &&
                candidate.Location.SourceTree?.FilePath == document.FilePath);
        var filePath = document.FilePath!;
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
        var service = new SupportedFixEnumerationService(
            new StaticCodeFixProviderRegistry(providers),
            logger,
            reporter);

        if (scenario == "cancelled")
        {
            var actual = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
                service.EnumerateAsync(
                    diagnostic,
                    solution,
                    filePath,
                    CancellationToken.None));
            Assert.AreSame(cancellation, actual);

            Assert.IsEmpty(reporter.Reports);
            return;
        }

        var result = await service.EnumerateAsync(
            diagnostic,
            solution,
            filePath,
            CancellationToken.None);
        var guidance = SupportedFixEnumerationService.GetGuidance("CS0414", result);

        Assert.HasCount(expectedFixCount, result.Fixes);
        Assert.AreEqual(expectedComplete, result.IsComplete);
        Assert.AreEqual(expectedFailureCount, result.FailedProviderCount);
        if (expectedComplete)
        {
            Assert.IsNull(guidance);
            Assert.IsEmpty(reporter.Reports);
        }
        else
        {
            Assert.IsNotNull(guidance);
            Assert.Contains("incomplete", guidance);
            Assert.IsFalse(
                guidance.Contains("No code fix provider is currently loaded", StringComparison.Ordinal),
                "A provider crash must not be projected as authoritative provider absence.");
            Assert.HasCount(expectedFailureCount, reporter.Reports);
            Assert.IsTrue(reporter.Reports.All(report =>
                report.Category == UnexpectedExceptionCategory.AnalysisScan));
            Assert.HasCount(expectedFailureCount, logger.Entries);
            foreach (var entry in logger.Entries)
            {
                Assert.IsNull(entry.Exception);
                Assert.Contains("InvalidOperationException", entry.Message);
                Assert.Contains("category=AnalysisScan", entry.Message);
                Assert.Contains("correlationId=diagnostic-correlation", entry.Message);
                Assert.DoesNotContain(SensitiveProviderFailure, entry.Message, StringComparison.Ordinal);
                Assert.DoesNotContain(filePath, entry.Message, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [TestMethod]
    [DataRow("compiler", "CS0414", "DiagnosticsProbe.cs", 9, 24)]
    [DataRow("analyzer", "CA1822", "AnimalService.cs", 7, 26)]
    [DataRow("absent", "RMCP9999", "AnimalService.cs", 7, 26)]
    [DataRow("cancelled", "CS0414", "DiagnosticsProbe.cs", 9, 24)]
    public async Task DiagnosticDocumentLookup_ReportsCompilerAnalyzerAbsentAndCancellation(
        string scenario,
        string diagnosticId,
        string documentName,
        int line,
        int column)
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var document = solution.Projects
            .SelectMany(project => project.Documents)
            .Single(candidate => candidate.Name == documentName);
        using var compilationCache = new CompilationCache(WorkspaceManager);
        var lookup = new DiagnosticDocumentLookup(compilationCache);

        if (scenario == "cancelled")
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
                lookup.FindAsync(
                    WorkspaceId,
                    solution,
                    new DiagnosticLookupTarget(
                        diagnosticId,
                        document.FilePath!,
                        line,
                        column),
                    cachedDiagnostics: null,
                    cancellation.Token));
            return;
        }

        var result = await lookup.FindAsync(
            WorkspaceId,
            solution,
            new DiagnosticLookupTarget(
                diagnosticId,
                document.FilePath!,
                line,
                column),
            cachedDiagnostics: null,
            CancellationToken.None);

        if (scenario == "absent")
        {
            Assert.IsNull(result.Diagnostic);
            Assert.IsNotNull(
                result.FullScanDiagnostics,
                "An unresolved single-document lookup must preserve the full-scan fallback contract.");
            return;
        }

        Assert.IsNotNull(result.Diagnostic, $"The {scenario} diagnostic must resolve.");
        Assert.AreEqual(diagnosticId, result.Diagnostic.Id);
        Assert.IsNull(
            result.FullScanDiagnostics,
            $"The {scenario} diagnostic must resolve before the full-solution fallback.");
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
            providers.Count > 0 ? providers[0] : null;
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
