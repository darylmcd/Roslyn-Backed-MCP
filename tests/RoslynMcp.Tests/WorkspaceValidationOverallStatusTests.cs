using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// validate-workspace-overallstatus-false-positive: compile_check can return Success=false
/// with ErrorCount=0 when no projects are evaluated. Overall status must not be compile-error
/// in that "no work" case.
/// </summary>
[TestClass]
public sealed class WorkspaceValidationOverallStatusTests
{
    [TestMethod]
    public void ComputeOverallStatus_CompileNotSuccessfulButZeroErrorCount_AndNoCompilerErrorsInMerge_YieldsClean()
    {
        var compile = new CompileCheckDto(
            Success: false,
            ErrorCount: 0,
            WarningCount: 0,
            TotalDiagnostics: 0,
            ReturnedDiagnostics: 0,
            Offset: 0,
            Limit: 200,
            HasMore: false,
            Diagnostics: Array.Empty<DiagnosticDto>(),
            ElapsedMs: 0,
            RestoreHint: "filter matched 0 projects",
            Cancelled: false,
            CompletedProjects: 0,
            TotalProjects: 0);

        var status = WorkspaceValidationService.ComputeOverallStatus(
            compile,
            Array.Empty<DiagnosticDto>(),
            null,
            runTests: false);

        Assert.AreEqual("clean", status, "Item #6 / zero-project compile_check is not a compiler-failure gating case.");
    }

    [TestMethod]
    public void ComputeOverallStatus_PositiveErrorCount_YieldsCompileError()
    {
        var compile = new CompileCheckDto(
            Success: false,
            ErrorCount: 1,
            WarningCount: 0,
            TotalDiagnostics: 1,
            ReturnedDiagnostics: 1,
            Offset: 0,
            Limit: 200,
            HasMore: false,
            Diagnostics: [new DiagnosticDto("CS0001", "x", "Error", "Compiler", "e.cs", 1, 1, 1, 1)],
            ElapsedMs: 0,
            Cancelled: false,
            CompletedProjects: 1,
            TotalProjects: 1);

        var status = WorkspaceValidationService.ComputeOverallStatus(
            compile,
            [new DiagnosticDto("CS0001", "x", "Error", "Compiler", "e.cs", 1, 1, 1, 1)],
            null,
            runTests: false);

        Assert.AreEqual("compile-error", status);
    }

    [TestMethod]
    public void ComputeOverallStatus_TotalProjectsNonZero_ButNoProjectsCompleted_ZeroErrorCount_YieldsClean()
    {
        var compile = new CompileCheckDto(
            Success: false,
            ErrorCount: 0,
            WarningCount: 0,
            TotalDiagnostics: 0,
            ReturnedDiagnostics: 0,
            Offset: 0,
            Limit: 200,
            HasMore: false,
            Diagnostics: Array.Empty<DiagnosticDto>(),
            ElapsedMs: 0,
            RestoreHint: null,
            Cancelled: false,
            CompletedProjects: 0,
            TotalProjects: 3);

        var status = WorkspaceValidationService.ComputeOverallStatus(
            compile,
            Array.Empty<DiagnosticDto>(),
            null,
            runTests: false);

        Assert.AreEqual("clean", status, "incomplete / vacuous compile_check must not be compile-error with zero errors");
    }

    /// <summary>
    /// validate-workspace-compiler-category-status-mismatch: a Category=="Compiler" diagnostic
    /// that appears ONLY in the merged list (i.e. surfaced by the project_diagnostics harvest,
    /// with its own version-keyed compilation cache) while the authoritative compile_check pass
    /// reports ErrorCount==0 must NOT yield "compile-error" — that combination produced the
    /// observed "overallStatus: compile-error / Compile errors: 0" contradiction. It is
    /// downgraded to "analyzer-error" (surfaced, not silently dropped to "clean").
    /// This test previously pinned the pre-fix "compile-error" verdict.
    /// </summary>
    [TestMethod]
    public void ComputeOverallStatus_CompilerErrorOnlyInMergedErrors_YieldsAnalyzerError()
    {
        var compileClean = new CompileCheckDto(
            Success: true,
            ErrorCount: 0,
            WarningCount: 0,
            TotalDiagnostics: 0,
            ReturnedDiagnostics: 0,
            Offset: 0,
            Limit: 200,
            HasMore: false,
            Diagnostics: Array.Empty<DiagnosticDto>(),
            ElapsedMs: 0,
            Cancelled: false,
            CompletedProjects: 1,
            TotalProjects: 1);

        var merged = new DiagnosticDto(
            "CS0103", "x", "Error", "Compiler", "Y.cs", 2, 1, 2, 5);

        var status = WorkspaceValidationService.ComputeOverallStatus(
            compileClean,
            [merged],
            null,
            runTests: false);

        Assert.AreEqual(
            "analyzer-error",
            status,
            "an uncorroborated Compiler-category row from the second harvest must not override a clean compile.ErrorCount==0 — it degrades to analyzer-error, and must not vanish into 'clean'.");
    }

    [TestMethod]
    public void ComputeOverallStatus_NonCompilerErrorInMerge_YieldsAnalyzerError()
    {
        var compileClean = new CompileCheckDto(
            Success: true,
            ErrorCount: 0,
            WarningCount: 0,
            TotalDiagnostics: 0,
            ReturnedDiagnostics: 0,
            Offset: 0,
            Limit: 200,
            HasMore: false,
            Diagnostics: Array.Empty<DiagnosticDto>(),
            ElapsedMs: 0,
            Cancelled: false,
            CompletedProjects: 1,
            TotalProjects: 1);

        var merged = new DiagnosticDto(
            "CA1000", "x", "Error", "Design", "Z.cs", 1, 1, 1, 10);

        var status = WorkspaceValidationService.ComputeOverallStatus(
            compileClean,
            [merged],
            null,
            runTests: false);

        Assert.AreEqual("analyzer-error", status);
    }

    /// <summary>
    /// validate-workspace-runtests-total-zero: when runTests=true and the discovered filter
    /// produced a non-empty expression, but dotnet test reported Total=0, the symptom is almost
    /// always a filter-resolution failure (working-directory or IChangeTracker timing). Pre-fix
    /// this branch hit the final fall-through and returned "clean" — making the bundle silently
    /// claim success on a non-pass. The new "test-zero-run" verdict surfaces the symptom.
    /// </summary>
    [TestMethod]
    public void ComputeOverallStatus_RunTestsTrue_TotalZero_NonEmptyFilter_YieldsTestZeroRun()
    {
        var compileClean = new CompileCheckDto(
            Success: true,
            ErrorCount: 0,
            WarningCount: 0,
            TotalDiagnostics: 0,
            ReturnedDiagnostics: 0,
            Offset: 0,
            Limit: 200,
            HasMore: false,
            Diagnostics: Array.Empty<DiagnosticDto>(),
            ElapsedMs: 0,
            Cancelled: false,
            CompletedProjects: 1,
            TotalProjects: 1);

        // Simulates the gh #764 repro: dotnet test exited 0 (Succeeded=true), but matched no
        // tests for the auto-derived filter, so Total/Passed/Failed/Skipped all settle at 0.
        var testRunZero = new TestRunResultDto(
            new CommandExecutionDto(
                Command: "dotnet",
                Arguments: ["test", "--filter", "FullyQualifiedName~Foo"],
                WorkingDirectory: "/repo",
                TargetPath: string.Empty,
                ExitCode: 0,
                Succeeded: true,
                DurationMs: 0,
                StdOut: string.Empty,
                StdErr: string.Empty),
            Total: 0,
            Passed: 0,
            Failed: 0,
            Skipped: 0,
            Failures: []);

        var status = WorkspaceValidationService.ComputeOverallStatus(
            compileClean,
            Array.Empty<DiagnosticDto>(),
            testRunZero,
            runTests: true);

        Assert.AreEqual(
            "test-zero-run",
            status,
            "runTests=true with Total=0 must surface 'test-zero-run' — not 'clean' — so callers don't silently treat a filter-resolution failure as a pass.");
    }

    /// <summary>
    /// validate-workspace-overallstatus-analyzer-error-with-empty-errordiagnostics (gh #751):
    /// when <c>summary=true</c> the service drops the per-diagnostic <see cref="WorkspaceValidationDto.ErrorDiagnostics"/>
    /// list but the verdict was already computed from the full error set, so the caller saw
    /// <c>overallStatus=analyzer-error</c> with an empty list and no count — leaving them
    /// unable to tell <em>how many</em> errors drove the verdict. The new <see cref="WorkspaceValidationDto.ErrorCount"/>
    /// field always carries the count (mirrors <see cref="WorkspaceValidationDto.WarningCount"/>)
    /// so summary callers can see the magnitude of the analyzer-error verdict without re-running
    /// with <c>summary=false</c>.
    ///
    /// This test pins the DTO-shape contract directly: a record constructed with
    /// <c>OverallStatus="analyzer-error"</c>, <c>ErrorCount=1</c>, and an empty
    /// <c>ErrorDiagnostics</c> list (exactly the summary-mode wire shape) MUST round-trip those
    /// three fields independently.
    /// </summary>
    [TestMethod]
    public void WorkspaceValidationDto_AnalyzerError_SummaryMode_ErrorCountNonZero_ErrorDiagnosticsEmpty()
    {
        var compileClean = new CompileCheckDto(
            Success: true,
            ErrorCount: 0,
            WarningCount: 0,
            TotalDiagnostics: 0,
            ReturnedDiagnostics: 0,
            Offset: 0,
            Limit: 200,
            HasMore: false,
            Diagnostics: Array.Empty<DiagnosticDto>(),
            ElapsedMs: 0,
            Cancelled: false,
            CompletedProjects: 1,
            TotalProjects: 1);

        // Construct the DTO directly with the summary-mode wire shape: ErrorCount > 0 alongside
        // an empty ErrorDiagnostics list. Pre-fix this combination was unrepresentable because
        // the only count came from ErrorDiagnostics.Count (always 0 in summary mode).
        var dto = new WorkspaceValidationDto(
            OverallStatus: "analyzer-error",
            ChangedFilePaths: Array.Empty<string>(),
            UnknownFilePaths: Array.Empty<string>(),
            CompileResult: compileClean,
            ErrorDiagnostics: Array.Empty<DiagnosticDto>(),
            ErrorCount: 1,
            WarningCount: 0,
            DiscoveredTests: Array.Empty<RelatedTestCaseDto>(),
            DotnetTestFilter: null,
            TestRunResult: null,
            Warnings: Array.Empty<string>());

        Assert.AreEqual("analyzer-error", dto.OverallStatus, "verdict must surface even when the list is suppressed");
        Assert.AreEqual(1, dto.ErrorCount, "ErrorCount must carry the count that drove the verdict");
        Assert.AreEqual(0, dto.ErrorDiagnostics.Count, "ErrorDiagnostics is empty in the summary-mode wire shape");
    }

    /// <summary>
    /// Negative-control for the test-zero-run guard: runTests=false (the default) must keep
    /// the legacy "clean" verdict even when no test_run was attempted. The guard only fires
    /// when the caller asked for tests AND a test_run actually executed AND returned Total=0.
    /// </summary>
    [TestMethod]
    public void ComputeOverallStatus_RunTestsFalse_NullTestResult_StillYieldsClean()
    {
        var compileClean = new CompileCheckDto(
            Success: true,
            ErrorCount: 0,
            WarningCount: 0,
            TotalDiagnostics: 0,
            ReturnedDiagnostics: 0,
            Offset: 0,
            Limit: 200,
            HasMore: false,
            Diagnostics: Array.Empty<DiagnosticDto>(),
            ElapsedMs: 0,
            Cancelled: false,
            CompletedProjects: 1,
            TotalProjects: 1);

        var status = WorkspaceValidationService.ComputeOverallStatus(
            compileClean,
            Array.Empty<DiagnosticDto>(),
            testRunResult: null,
            runTests: false);

        Assert.AreEqual("clean", status, "runTests=false must remain a 'clean' verdict on a clean compile — the new test-zero-run guard must not regress that path.");
    }
}
