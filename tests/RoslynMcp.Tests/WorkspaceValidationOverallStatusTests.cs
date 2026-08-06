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

    // ---- validate-workspace-diagnostic-harvest-reconcile: MergeErrorDiagnostics ----

    private static CompileCheckDto CleanCompile(params DiagnosticDto[] diagnostics) => new(
        Success: true,
        ErrorCount: 0,
        WarningCount: 0,
        TotalDiagnostics: diagnostics.Length,
        ReturnedDiagnostics: diagnostics.Length,
        Offset: 0,
        Limit: 200,
        HasMore: false,
        Diagnostics: diagnostics,
        ElapsedMs: 0,
        Cancelled: false,
        CompletedProjects: 1,
        TotalProjects: 1);

    private static CompileCheckDto FailingCompile(int errorCount, params DiagnosticDto[] diagnostics) => new(
        Success: false,
        ErrorCount: errorCount,
        WarningCount: 0,
        TotalDiagnostics: diagnostics.Length,
        ReturnedDiagnostics: diagnostics.Length,
        Offset: 0,
        Limit: 200,
        HasMore: false,
        Diagnostics: diagnostics,
        ElapsedMs: 0,
        Cancelled: false,
        CompletedProjects: 1,
        TotalProjects: 1);

    // DiagnosticsResultDto is positional with WorkspaceDiagnostics in slot 1 — always use named args.
    private static DiagnosticsResultDto DiagResult(
        IReadOnlyList<DiagnosticDto>? compilerDiagnostics = null,
        IReadOnlyList<DiagnosticDto>? analyzerDiagnostics = null,
        IReadOnlyList<DiagnosticDto>? workspaceDiagnostics = null) => new(
        WorkspaceDiagnostics: workspaceDiagnostics ?? Array.Empty<DiagnosticDto>(),
        CompilerDiagnostics: compilerDiagnostics ?? Array.Empty<DiagnosticDto>(),
        AnalyzerDiagnostics: analyzerDiagnostics ?? Array.Empty<DiagnosticDto>(),
        TotalErrors: 0,
        TotalWarnings: 0,
        TotalInfo: 0);

    /// <summary>
    /// validate-workspace-diagnostic-harvest-reconcile (acceptance bullet 1): the project_diagnostics
    /// harvest surfaces a Category=="Compiler" error the authoritative compile pass never saw
    /// (compile.ErrorCount==0). That uncorroborated row must not enter the merged error list at all —
    /// pre-fix it inflated ErrorCount to 1 and drove overallStatus to "analyzer-error" on a green build.
    /// </summary>
    [TestMethod]
    public void MergeErrorDiagnostics_UncorroboratedCompilerRow_CleanCompile_IsDropped()
    {
        var merged = WorkspaceValidationService.MergeErrorDiagnostics(
            CleanCompile(),
            DiagResult(compilerDiagnostics: [new DiagnosticDto("CS0103", "x", "Error", "Compiler", "Y.cs", 2, 1, 2, 5)]));

        Assert.AreEqual(
            0,
            merged.Length,
            "a Compiler-category row seen only by the second harvest, with compile.ErrorCount==0, is not corroborated and must not enter the merged error set.");
    }

    /// <summary>
    /// The corroborated path is unchanged: when compile.ErrorCount &gt; 0 the second harvest's
    /// Compiler-category rows still merge, and DistinctBy dedup across the two harvests still collapses
    /// identical (Id, FilePath, StartLine, StartColumn) tuples exactly once.
    /// </summary>
    [TestMethod]
    public void MergeErrorDiagnostics_CorroboratedCompilerRows_StillMerge_AndDedup()
    {
        var shared = new DiagnosticDto("CS0246", "missing type", "Error", "Compiler", "A.cs", 3, 5, 3, 9);
        var secondHarvestOnly = new DiagnosticDto("CS1002", "; expected", "Error", "Compiler", "B.cs", 7, 1, 7, 2);

        var merged = WorkspaceValidationService.MergeErrorDiagnostics(
            FailingCompile(errorCount: 2, shared),
            DiagResult(compilerDiagnostics: [shared, secondHarvestOnly]));

        Assert.AreEqual(2, merged.Length, "corroborated Compiler rows merge; the duplicate is deduped exactly once.");
        CollectionAssert.AreEquivalent(
            new[] { "CS0246", "CS1002" },
            merged.Select(d => d.Id).ToArray());
    }

    /// <summary>
    /// Regression guard on the reason stage 2 exists at all: analyzer (CA*/IDE*) errors are structurally
    /// invisible to compile_check, so AnalyzerDiagnostics merges unconditionally — the corroboration gate
    /// applies ONLY to the CompilerDiagnostics arm.
    /// </summary>
    [TestMethod]
    public void MergeErrorDiagnostics_AnalyzerErrors_MergeEvenWhenCompileIsClean()
    {
        var merged = WorkspaceValidationService.MergeErrorDiagnostics(
            CleanCompile(),
            DiagResult(
                compilerDiagnostics: [new DiagnosticDto("CS0103", "x", "Error", "Compiler", "Y.cs", 2, 1, 2, 5)],
                analyzerDiagnostics: [new DiagnosticDto("CA1000", "design", "Error", "Design", "Z.cs", 1, 1, 1, 10)]));

        Assert.AreEqual(1, merged.Length, "the analyzer error survives; the uncorroborated compiler row does not.");
        Assert.AreEqual("CA1000", merged[0].Id);
    }

    /// <summary>
    /// WorkspaceDiagnostics stays deliberately excluded from the merge (pre-existing behavior) — pinned
    /// so the extraction into MergeErrorDiagnostics cannot silently widen the harvest.
    /// </summary>
    [TestMethod]
    public void MergeErrorDiagnostics_WorkspaceDiagnostics_AreNeverMerged()
    {
        var merged = WorkspaceValidationService.MergeErrorDiagnostics(
            FailingCompile(errorCount: 1),
            DiagResult(workspaceDiagnostics: [new DiagnosticDto("WS0001", "load", "Error", "Workspace", "P.csproj", 1, 1, 1, 1)]));

        Assert.AreEqual(0, merged.Length, "WorkspaceDiagnostics were never part of the merged error set.");
    }

    /// <summary>
    /// End-to-end reproduction of the reported green-build shape: clean compile_check (0 errors,
    /// 0 warnings), one phantom Compiler-category row from the project_diagnostics harvest. Chaining
    /// MergeErrorDiagnostics into ComputeOverallStatus must now yield overallStatus "clean" with zero
    /// errors — pre-fix this returned "analyzer-error" / errorCount 1 (and, before PR #1140,
    /// "compile-error" / 1).
    /// </summary>
    [TestMethod]
    public void MergeErrorDiagnostics_ThenComputeOverallStatus_GreenBuildWithPhantomCompilerRow_YieldsClean()
    {
        var compileClean = CleanCompile();
        var diagResult = DiagResult(
            compilerDiagnostics: [new DiagnosticDto("CS0103", "x", "Error", "Compiler", "Y.cs", 2, 1, 2, 5)]);

        var merged = WorkspaceValidationService.MergeErrorDiagnostics(compileClean, diagResult);
        var status = WorkspaceValidationService.ComputeOverallStatus(compileClean, merged, null, runTests: false);

        Assert.AreEqual(0, merged.Length, "errorCount (allErrors.Length) must be 0 on a green build.");
        Assert.AreEqual("clean", status, "a green build must report overallStatus 'clean', not a phantom 'analyzer-error'.");
    }
}
