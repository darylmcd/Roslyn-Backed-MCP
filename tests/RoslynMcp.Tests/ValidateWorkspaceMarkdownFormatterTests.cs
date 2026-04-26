using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Formatters;

namespace RoslynMcp.Tests;

/// <summary>
/// response-format-json-markdown-parameter (P4 UX): asserts the markdown formatter
/// produces a compact summary that preserves the verdict (overallStatus) across the
/// four canonical response shapes: clean, compile-error, test-failure, mixed
/// (analyzer error + test failure). The formatter is a pure function over
/// <see cref="WorkspaceValidationDto"/>; tests exercise it directly without spinning
/// up a workspace.
/// </summary>
[TestClass]
public sealed class ValidateWorkspaceMarkdownFormatterTests
{
    [TestMethod]
    public void Format_CleanResponse_RendersHeaderAndZeroCounts()
    {
        var dto = MakeDto(overallStatus: "clean");

        var md = ValidateWorkspaceMarkdownFormatter.Format(dto);

        StringAssert.Contains(md, "# validate_workspace — clean");
        StringAssert.Contains(md, "| Overall status | clean |");
        StringAssert.Contains(md, "| Compile errors | 0 |");
        StringAssert.Contains(md, "| Analyzer/compile error diagnostics | 0 |");
        Assert.IsFalse(md.Contains("## Error diagnostics"), "Clean response must not emit a diagnostics section.");
        Assert.IsFalse(md.Contains("## Discovered tests"), "Clean response with no tests must not emit a tests section.");
        Assert.IsFalse(md.Contains("## Test failures"), "Clean response must not emit a failures section.");
    }

    [TestMethod]
    public void Format_CompileErrorResponse_EmitsDiagnosticTableAndPreservesVerdict()
    {
        var diag = new DiagnosticDto(
            Id: "CS0103",
            Message: "The name 'foo' does not exist in the current context",
            Severity: "Error",
            Category: "Compiler",
            FilePath: @"C:\src\Sample\Foo.cs",
            StartLine: 42,
            StartColumn: 5,
            EndLine: 42,
            EndColumn: 8);

        var dto = MakeDto(
            overallStatus: "compile-error",
            compile: new CompileCheckDto(
                Success: false,
                ErrorCount: 1,
                WarningCount: 0,
                TotalDiagnostics: 1,
                ReturnedDiagnostics: 1,
                Offset: 0,
                Limit: 100,
                HasMore: false,
                Diagnostics: new[] { diag },
                ElapsedMs: 12),
            errorDiagnostics: new[] { diag });

        var md = ValidateWorkspaceMarkdownFormatter.Format(dto);

        StringAssert.Contains(md, "# validate_workspace — compile-error");
        StringAssert.Contains(md, "| Overall status | compile-error |");
        StringAssert.Contains(md, "## Error diagnostics");
        StringAssert.Contains(md, "CS0103");
        StringAssert.Contains(md, "Foo.cs");
        StringAssert.Contains(md, "42");
    }

    [TestMethod]
    public void Format_TestFailureResponse_EmitsFailuresAndRerunFilter()
    {
        var test = new RelatedTestCaseDto(
            DisplayName: "Sample.Tests.WidgetTests.AddsCorrectly",
            FullyQualifiedName: "Sample.Tests.WidgetTests.AddsCorrectly",
            ProjectName: "Sample.Tests",
            FilePath: @"C:\src\Sample.Tests\WidgetTests.cs",
            Line: 14,
            TriggeredByFiles: new[] { @"C:\src\Sample\Widget.cs" });

        var run = new TestRunResultDto(
            Execution: MakeExecution(),
            Total: 1,
            Passed: 0,
            Failed: 1,
            Skipped: 0,
            Failures: new[]
            {
                new TestFailureDto(
                    DisplayName: "Sample.Tests.WidgetTests.AddsCorrectly",
                    FullyQualifiedName: "Sample.Tests.WidgetTests.AddsCorrectly",
                    Message: "Expected 4 but was 3",
                    StackTrace: "  at Sample.Tests.WidgetTests.AddsCorrectly() line 17"),
            });

        var dto = MakeDto(
            overallStatus: "test-failure",
            discoveredTests: new[] { test },
            dotnetTestFilter: "FullyQualifiedName=Sample.Tests.WidgetTests.AddsCorrectly",
            testRunResult: run);

        var md = ValidateWorkspaceMarkdownFormatter.Format(dto);

        StringAssert.Contains(md, "# validate_workspace — test-failure");
        StringAssert.Contains(md, "## Discovered tests");
        StringAssert.Contains(md, "Sample.Tests.WidgetTests.AddsCorrectly");
        StringAssert.Contains(md, "## Test failures");
        StringAssert.Contains(md, "Expected 4 but was 3");
        StringAssert.Contains(md, "**Re-run filter:**");
        StringAssert.Contains(md, "FullyQualifiedName=Sample.Tests.WidgetTests.AddsCorrectly");
        StringAssert.Contains(md, "| Tests failed | 1 |");
    }

    [TestMethod]
    public void Format_MixedResponse_IncludesAllSections()
    {
        var diag = new DiagnosticDto("CA1822", "Member can be marked static", "Warning", "Performance",
            @"C:\src\Sample\Bar.cs", 7, 1, 7, 5);
        // Even though severity here is Warning, we put it in ErrorDiagnostics to exercise
        // the formatter's error-table path.
        var test = new RelatedTestCaseDto("BarTests.Works", "Sample.Tests.BarTests.Works",
            "Sample.Tests", null, null, Array.Empty<string>());
        var run = new TestRunResultDto(
            MakeExecution(),
            Total: 2, Passed: 1, Failed: 1, Skipped: 0,
            Failures: new[] { new TestFailureDto("BarTests.Works", "Sample.Tests.BarTests.Works", "boom", null) });

        var dto = MakeDto(
            overallStatus: "test-failure",
            errorDiagnostics: new[] { diag },
            warningCount: 5,
            discoveredTests: new[] { test },
            dotnetTestFilter: "FullyQualifiedName=Sample.Tests.BarTests.Works",
            testRunResult: run,
            warnings: new[] { "git not on PATH; falling back to full-workspace scope" });

        var md = ValidateWorkspaceMarkdownFormatter.Format(dto);

        StringAssert.Contains(md, "## Error diagnostics");
        StringAssert.Contains(md, "## Discovered tests");
        StringAssert.Contains(md, "## Test failures");
        StringAssert.Contains(md, "## Warnings");
        StringAssert.Contains(md, "git not on PATH");
        StringAssert.Contains(md, "| Warning diagnostics | 5 |");
    }

    [TestMethod]
    public void Format_DiagnosticMessageWithPipeAndNewline_EscapedSafely()
    {
        // Markdown table integrity: pipes split rows, newlines terminate them.
        var diag = new DiagnosticDto(
            Id: "CS9999",
            Message: "first line\nsecond | piped",
            Severity: "Error",
            Category: "Compiler",
            FilePath: @"C:\x.cs",
            StartLine: 1, StartColumn: 1, EndLine: 1, EndColumn: 2);
        var dto = MakeDto(overallStatus: "compile-error", errorDiagnostics: new[] { diag });

        var md = ValidateWorkspaceMarkdownFormatter.Format(dto);

        // The escaped row must appear on a single line and not contain a literal pipe inside the message cell.
        StringAssert.Contains(md, "first line second \\| piped");
        Assert.IsFalse(md.Contains("first line\nsecond"), "Newline inside a cell would split the table row.");
    }

    [TestMethod]
    public void Format_ManyDiagnostics_TruncatedToCap()
    {
        var diags = Enumerable.Range(0, ValidateWorkspaceMarkdownFormatter.MaxDiagnosticRows + 5)
            .Select(i => new DiagnosticDto($"CS{i:D4}", $"msg {i}", "Error", "Compiler",
                @"C:\f.cs", i + 1, 1, i + 1, 2))
            .ToArray();
        var dto = MakeDto(overallStatus: "compile-error", errorDiagnostics: diags);

        var md = ValidateWorkspaceMarkdownFormatter.Format(dto);

        StringAssert.Contains(md, "5 more truncated");
        // First diagnostic kept, last one (beyond cap) NOT printed as its own row.
        StringAssert.Contains(md, "CS0000");
        Assert.IsFalse(md.Contains($"CS{(ValidateWorkspaceMarkdownFormatter.MaxDiagnosticRows + 4):D4}"),
            "Diagnostic past the cap should not appear as its own row.");
    }

    private static CommandExecutionDto MakeExecution()
        => new(
            Command: "dotnet",
            Arguments: new[] { "test" },
            WorkingDirectory: ".",
            TargetPath: ".",
            ExitCode: 0,
            Succeeded: true,
            DurationMs: 100,
            StdOut: "",
            StdErr: "");

    private static WorkspaceValidationDto MakeDto(
        string overallStatus = "clean",
        CompileCheckDto? compile = null,
        IReadOnlyList<DiagnosticDto>? errorDiagnostics = null,
        int warningCount = 0,
        IReadOnlyList<RelatedTestCaseDto>? discoveredTests = null,
        string? dotnetTestFilter = null,
        TestRunResultDto? testRunResult = null,
        IReadOnlyList<string>? warnings = null)
    {
        compile ??= new CompileCheckDto(
            Success: true,
            ErrorCount: 0,
            WarningCount: 0,
            TotalDiagnostics: 0,
            ReturnedDiagnostics: 0,
            Offset: 0,
            Limit: 100,
            HasMore: false,
            Diagnostics: Array.Empty<DiagnosticDto>(),
            ElapsedMs: 1);

        return new WorkspaceValidationDto(
            OverallStatus: overallStatus,
            ChangedFilePaths: Array.Empty<string>(),
            UnknownFilePaths: Array.Empty<string>(),
            CompileResult: compile,
            ErrorDiagnostics: errorDiagnostics ?? Array.Empty<DiagnosticDto>(),
            WarningCount: warningCount,
            DiscoveredTests: discoveredTests ?? Array.Empty<RelatedTestCaseDto>(),
            DotnetTestFilter: dotnetTestFilter,
            TestRunResult: testRunResult,
            Warnings: warnings ?? Array.Empty<string>());
    }
}
