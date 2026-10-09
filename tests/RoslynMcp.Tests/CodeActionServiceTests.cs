using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>Verifies same-line caret clamping and provider selection bounds.</summary>
[TestClass]
public sealed class CodeActionServiceTests : SharedWorkspaceTestBase
{
    private const string SensitiveFailureMessage = "sensitive provider path C:\\secret\\source.cs";

    private static string SampleWorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        SampleWorkspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);
    }

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    private static string FindDocumentPath(string name)
    {
        var solution = WorkspaceManager.GetCurrentSolution(SampleWorkspaceId);
        return solution.Projects
            .SelectMany(p => p.Documents)
            .First(d => d.Name == name).FilePath!;
    }

    [TestMethod]
    [DataRow(0, 1, true, "startLine")]
    [DataRow(0, 1, false, "startLine")]
    [DataRow(1, 0, true, "startColumn")]
    [DataRow(1, 0, false, "startColumn")]
    [DataRow(-1, -1, true, "startLine")]
    [DataRow(-1, -1, false, "startLine")]
    public async Task GetCodeActions_NegativeStart_PrecedesWorkspaceAndDocumentLookup(
        int line, int column, bool missingWorkspace, string parameter)
    {
        var missingPath = Path.Combine(Path.GetDirectoryName(SampleSolutionPath)!, "Missing.cs");
        var exception = await Assert.ThrowsExactlyAsync<PublicArgumentException>(() =>
            CodeActionService.GetCodeActionsAsync(
                missingWorkspace ? "missing-workspace" : SampleWorkspaceId, missingPath,
                line, column, null, null, CancellationToken.None));
        Assert.AreEqual(parameter, exception.ParamName);
        StringAssert.Contains(exception.PublicMessage, parameter + " must be >= 1");
    }

    [TestMethod]
    [DataRow(1, 1)]
    [DataRow(1, 50)]
    [DataRow(38, 13)]
    [DataRow(43, 100)]
    [DataRow(1, int.MaxValue)]
    public async Task GetCodeActions_CaretOnly_ProvidersReceiveSameLineBoundedSpan(int line, int column)
    {
        var filePath = FindDocumentPath("RefactoringProbe.cs");
        var document = SymbolResolver.FindDocument(WorkspaceManager.GetCurrentSolution(SampleWorkspaceId), filePath)!;
        var text = await document.GetTextAsync();
        var sourceLine = text.Lines[line - 1];
        var provider = new CapturingRefactoringProvider();
        var service = CreateService(new CaptureLogger<CodeActionService>(),
            new RecordingUnexpectedExceptionReporter("unused"), [], [provider]);
        await service.GetCodeActionsAsync(SampleWorkspaceId, filePath, line, column, null, null, CancellationToken.None);
        Assert.AreEqual(TextSpan.FromBounds(
            sourceLine.Start + Math.Min(column - 1, sourceLine.Span.Length), sourceLine.End), provider.Span);
    }

    [TestMethod]
    public async Task GetCodeActions_ExplicitSelection_PreservesCheckedSpan()
    {
        var filePath = FindDocumentPath("RefactoringProbe.cs");
        var provider = new CapturingRefactoringProvider();
        var service = CreateService(new CaptureLogger<CodeActionService>(),
            new RecordingUnexpectedExceptionReporter("unused"), [], [provider]);
        await service.GetCodeActionsAsync(SampleWorkspaceId, filePath, 1, 2, 1, 4, CancellationToken.None);
        Assert.AreEqual(new TextSpan(1, 2), provider.Span);
    }

    private sealed class CapturingRefactoringProvider : CodeRefactoringProvider
    {
        public TextSpan? Span { get; private set; }

        public override Task ComputeRefactoringsAsync(CodeRefactoringContext context)
        {
            Span = context.Span;
            return Task.CompletedTask;
        }
    }

    [TestMethod]
    public async Task ProviderFailures_EmitCorrelatedRedactedDiagnostics()
    {
        var (filePath, line, column) = await FindDiagnosticLocationAsync("CS0414");
        var logger = new CaptureLogger<CodeActionService>();
        var reporter = new RecordingUnexpectedExceptionReporter("provider-correlation");
        var service = CreateService(
            logger,
            reporter,
            [new ThrowingCodeFixProvider(new InvalidOperationException(SensitiveFailureMessage))],
            [new ThrowingRefactoringProvider(new InvalidOperationException(SensitiveFailureMessage))]);

        var result = await service.GetCodeActionsAsync(
            SampleWorkspaceId,
            filePath,
            line,
            column,
            line,
            column + 1,
            CancellationToken.None);

        Assert.AreEqual(0, result.Count);
        Assert.AreEqual(2, reporter.Reports.Count);
        Assert.IsTrue(reporter.Reports.All(report =>
            report.Category == UnexpectedExceptionCategory.AnalysisScan));
        Assert.AreEqual(2, logger.Entries.Count);
        foreach (var entry in logger.Entries)
        {
            Assert.AreEqual(LogLevel.Debug, entry.Level);
            Assert.IsNull(entry.Exception, "Provider diagnostics must not pass raw exceptions to ILogger.");
            StringAssert.Contains(entry.Message, "InvalidOperationException");
            StringAssert.Contains(entry.Message, "category=AnalysisScan");
            StringAssert.Contains(entry.Message, "correlationId=provider-correlation");
            Assert.IsFalse(entry.Message.Contains(SensitiveFailureMessage, StringComparison.Ordinal));
            Assert.IsFalse(entry.Message.Contains(filePath, StringComparison.OrdinalIgnoreCase));
        }
    }

    [TestMethod]
    public async Task ProviderCancellation_PropagatesTheOriginalException()
    {
        var (filePath, line, column) = await FindDiagnosticLocationAsync("CS0414");
        var expected = new OperationCanceledException("provider cancellation");
        var service = CreateService(
            new CaptureLogger<CodeActionService>(),
            new RecordingUnexpectedExceptionReporter("unused"),
            [new ThrowingCodeFixProvider(expected)],
            []);

        try
        {
            await service.GetCodeActionsAsync(
                SampleWorkspaceId,
                filePath,
                line,
                column,
                line,
                column + 1,
                CancellationToken.None);
            Assert.Fail("Provider cancellation must propagate.");
        }
        catch (OperationCanceledException actual)
        {
            Assert.AreSame(expected, actual);
        }
    }

    private static CodeActionService CreateService(
        CaptureLogger<CodeActionService> logger,
        IUnexpectedExceptionReporter reporter,
        ImmutableArray<CodeFixProvider> codeFixProviders,
        ImmutableArray<CodeRefactoringProvider> refactoringProviders) =>
        new(
            WorkspaceManager,
            PreviewStore,
            logger,
            () => new FeatureProviderLoadResult<CodeFixProvider>(codeFixProviders, []),
            () => new FeatureProviderLoadResult<CodeRefactoringProvider>(refactoringProviders, []),
            reporter);

    private static async Task<(string FilePath, int Line, int Column)> FindDiagnosticLocationAsync(
        string diagnosticId)
    {
        var solution = WorkspaceManager.GetCurrentSolution(SampleWorkspaceId);
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

    private sealed class ThrowingCodeFixProvider(Exception exception) : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds => ["CS0414"];

        public override FixAllProvider? GetFixAllProvider() => null;

        public override Task RegisterCodeFixesAsync(CodeFixContext context) =>
            Task.FromException(exception);
    }

    private sealed class ThrowingRefactoringProvider(Exception exception) : CodeRefactoringProvider
    {
        public override Task ComputeRefactoringsAsync(CodeRefactoringContext context) =>
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
