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
    // ---- CompileCheckDto builders ----
    // CompileCheckDto is a 16-slot positional record, but only four slots drive the code under
    // test: Success, ErrorCount, Cancelled, and CompletedProjects-vs-TotalProjects. Restating the
    // full literal per test buried those four in boilerplate, so each named helper below fixes one
    // compile shape and every test names the shape it is exercising.

    private static CompileCheckDto Compile(
        bool success,
        int errorCount,
        int completedProjects,
        int totalProjects,
        bool cancelled = false,
        string? restoreHint = null,
        params DiagnosticDto[] diagnostics) => new(
        Success: success,
        ErrorCount: errorCount,
        WarningCount: 0,
        TotalDiagnostics: diagnostics.Length,
        ReturnedDiagnostics: diagnostics.Length,
        Offset: 0,
        Limit: 200,
        HasMore: false,
        Diagnostics: diagnostics,
        ElapsedMs: 0,
        RestoreHint: restoreHint,
        Cancelled: cancelled,
        CompletedProjects: completedProjects,
        TotalProjects: totalProjects);

    /// <summary>A complete, genuinely-green compile pass: every project compiled, zero errors.</summary>
    private static CompileCheckDto CleanCompile(params DiagnosticDto[] diagnostics) =>
        Compile(success: true, errorCount: 0, completedProjects: 1, totalProjects: 1, diagnostics: diagnostics);

    /// <summary>A complete compile pass that found real compiler errors.</summary>
    private static CompileCheckDto FailingCompile(int errorCount, params DiagnosticDto[] diagnostics) =>
        Compile(success: false, errorCount: errorCount, completedProjects: 1, totalProjects: 1, diagnostics: diagnostics);

    /// <summary>
    /// A vacuous "no work" pass — the project filter matched nothing, or no project completed, with
    /// zero errors. Success=false here is a scope signal, not a compiler failure.
    /// </summary>
    private static CompileCheckDto VacuousCompile(
        int completedProjects,
        int totalProjects,
        string? restoreHint = null) =>
        Compile(
            success: false,
            errorCount: 0,
            completedProjects: completedProjects,
            totalProjects: totalProjects,
            restoreHint: restoreHint);

    /// <summary>
    /// A compile pass cancelled mid-sweep: CompileCheckService swallows the
    /// OperationCanceledException and returns Cancelled=true with whatever PARTIAL ErrorCount it
    /// had accumulated, so ErrorCount==0 here means "did not look", not "found nothing".
    /// </summary>
    private static CompileCheckDto CancelledCompile(
        int completedProjects,
        int totalProjects,
        int errorCount = 0,
        params DiagnosticDto[] diagnostics) =>
        Compile(
            success: false,
            errorCount: errorCount,
            completedProjects: completedProjects,
            totalProjects: totalProjects,
            cancelled: true,
            diagnostics: diagnostics);

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

    // ---- ComputeOverallStatus ----

    [TestMethod]
    public void ComputeOverallStatus_CompileNotSuccessfulButZeroErrorCount_AndNoCompilerErrorsInMerge_YieldsClean()
    {
        var compile = VacuousCompile(
            completedProjects: 0,
            totalProjects: 0,
            restoreHint: "filter matched 0 projects");

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
        var compile = FailingCompile(
            errorCount: 1,
            new DiagnosticDto("CS0001", "x", "Error", "Compiler", "e.cs", 1, 1, 1, 1));

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
        var compile = VacuousCompile(completedProjects: 0, totalProjects: 3);

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
    /// downgraded to "analyzer-error" rather than dropped.
    ///
    /// This pins ComputeOverallStatus in isolation. On the LIVE path this shape no longer reaches
    /// the verdict at all: validate-workspace-diagnostic-harvest-reconcile made
    /// MergeErrorDiagnostics filter the uncorroborated row out of `errors` upstream (see
    /// MergeErrorDiagnostics_ThenComputeOverallStatus_GreenBuildWithPhantomCompilerRow_YieldsClean
    /// for the composed behavior). The branch pinned here is defense-in-depth for any caller that
    /// assembles `errors` by another route.
    /// </summary>
    [TestMethod]
    public void ComputeOverallStatus_CompilerErrorOnlyInMergedErrors_YieldsAnalyzerError()
    {
        var compileClean = CleanCompile();

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
            "an uncorroborated Compiler-category row from the second harvest must not override a clean compile.ErrorCount==0 — if it reaches the verdict at all it degrades to analyzer-error, and must not vanish into 'clean'.");
    }

    [TestMethod]
    public void ComputeOverallStatus_NonCompilerErrorInMerge_YieldsAnalyzerError()
    {
        var compileClean = CleanCompile();

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
        var compileClean = CleanCompile();

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
        var compileClean = CleanCompile();

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
        var compileClean = CleanCompile();

        var status = WorkspaceValidationService.ComputeOverallStatus(
            compileClean,
            Array.Empty<DiagnosticDto>(),
            testRunResult: null,
            runTests: false);

        Assert.AreEqual("clean", status, "runTests=false must remain a 'clean' verdict on a clean compile — the new test-zero-run guard must not regress that path.");
    }

    // ---- validate-workspace-diagnostic-harvest-reconcile: MergeErrorDiagnostics ----

    /// <summary>
    /// validate-workspace-diagnostic-harvest-reconcile (acceptance bullet 1): the project_diagnostics
    /// harvest surfaces a Category=="Compiler" error the authoritative compile pass never saw
    /// (compile.ErrorCount==0, pass COMPLETE). That uncorroborated row must not enter the merged error
    /// list at all — pre-fix it inflated ErrorCount to 1 and drove overallStatus to "analyzer-error"
    /// on a green build.
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
            "a Compiler-category row seen only by the second harvest, with a COMPLETE compile pass reporting ErrorCount==0, is not corroborated and must not enter the merged error set.");
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
    /// End-to-end reproduction of the reported green-build shape: clean AND COMPLETE compile_check
    /// (0 errors, 0 warnings, every project compiled), one phantom Compiler-category row from the
    /// project_diagnostics harvest. Chaining MergeErrorDiagnostics into ComputeOverallStatus must now
    /// yield overallStatus "clean" with zero errors — pre-fix this returned "analyzer-error" /
    /// errorCount 1 (and, before PR #1140, "compile-error" / 1).
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

    // ---- Authority gate: a compile pass that did not finish cannot suppress the second harvest ----

    /// <summary>
    /// The false-green counterpart of the phantom-row case, and the reason the gate keys on AUTHORITY
    /// rather than on ErrorCount alone. CompileCheckService.CheckAsync swallows
    /// OperationCanceledException and returns Cancelled=true with a PARTIAL ErrorCount, and stage 2
    /// gets its own fresh timeout budget — so a timed-out stage 1 (ErrorCount==0 because it never
    /// looked) routinely pairs with a stage 2 that completed and DID see the real CS* errors in the
    /// un-compiled projects. Suppressing those rows would report "clean" on a build that does not
    /// compile: a false green on the pre-ship gate, strictly worse than the phantom-row false positive.
    /// </summary>
    [TestMethod]
    public void MergeErrorDiagnostics_CancelledCompile_CompilerRowsStillMerge()
    {
        var merged = WorkspaceValidationService.MergeErrorDiagnostics(
            CancelledCompile(completedProjects: 1, totalProjects: 4),
            DiagResult(compilerDiagnostics: [new DiagnosticDto("CS0103", "x", "Error", "Compiler", "Y.cs", 2, 1, 2, 5)]));

        Assert.AreEqual(
            1,
            merged.Length,
            "a cancelled compile pass forfeits authority — its ErrorCount==0 means 'did not look', so the second harvest's compiler rows must survive.");
        Assert.AreEqual("CS0103", merged[0].Id);
    }

    /// <summary>
    /// Same forfeiture keyed on incompleteness rather than the Cancelled flag. Today Cancelled is the
    /// only way CompletedProjects can lag TotalProjects, but incompleteness is the invariant that
    /// actually makes ErrorCount non-authoritative, so it is pinned independently.
    /// </summary>
    [TestMethod]
    public void MergeErrorDiagnostics_PartialProjectCoverage_CompilerRowsStillMerge()
    {
        var partial = Compile(
            success: false,
            errorCount: 0,
            completedProjects: 2,
            totalProjects: 5,
            cancelled: false);

        var merged = WorkspaceValidationService.MergeErrorDiagnostics(
            partial,
            DiagResult(compilerDiagnostics: [new DiagnosticDto("CS0246", "missing type", "Error", "Compiler", "A.cs", 3, 5, 3, 9)]));

        Assert.AreEqual(
            1,
            merged.Length,
            "CompletedProjects < TotalProjects means the compile pass never examined every project — its ErrorCount cannot suppress the only harvest that did.");
    }

    /// <summary>
    /// The regression the authority gate exists to prevent, composed end-to-end: a timed-out compile
    /// pass plus a completed second harvest carrying real CS* errors must NOT report "clean". Pre-fix
    /// (the ErrorCount-only gate) this dropped the rows and returned "clean" with errorCount 0 — a new
    /// false green on the pre-ship gate.
    /// </summary>
    [TestMethod]
    public void MergeErrorDiagnostics_ThenComputeOverallStatus_TimedOutCompileWithRealErrors_IsNotClean()
    {
        var timedOut = CancelledCompile(completedProjects: 1, totalProjects: 4);
        var diagResult = DiagResult(
            compilerDiagnostics: [new DiagnosticDto("CS0246", "missing type", "Error", "Compiler", "A.cs", 3, 5, 3, 9)]);

        var merged = WorkspaceValidationService.MergeErrorDiagnostics(timedOut, diagResult);
        var status = WorkspaceValidationService.ComputeOverallStatus(timedOut, merged, null, runTests: false);

        Assert.AreEqual(1, merged.Length, "the real compiler error from the completed harvest must be counted.");
        Assert.AreNotEqual(
            "clean",
            status,
            "a timed-out compile pass with real CS* errors visible only to the second harvest must never report 'clean'.");
        Assert.AreEqual("analyzer-error", status, "compile.ErrorCount is 0 (partial), so the surfaced row degrades to analyzer-error rather than compile-error.");
    }

    /// <summary>
    /// A cancelled compile pass that DID accumulate errors before the timeout keeps the stronger
    /// compile-error verdict — the authority gate widens what is merged, it does not weaken the
    /// verdict for an already-corroborated failure.
    /// </summary>
    [TestMethod]
    public void MergeErrorDiagnostics_ThenComputeOverallStatus_CancelledCompileWithPartialErrorCount_YieldsCompileError()
    {
        var cs0246 = new DiagnosticDto("CS0246", "missing type", "Error", "Compiler", "A.cs", 3, 5, 3, 9);
        var cancelled = CancelledCompile(completedProjects: 2, totalProjects: 4, errorCount: 1, cs0246);
        var diagResult = DiagResult(
            compilerDiagnostics: [cs0246, new DiagnosticDto("CS1002", "; expected", "Error", "Compiler", "B.cs", 7, 1, 7, 2)]);

        var merged = WorkspaceValidationService.MergeErrorDiagnostics(cancelled, diagResult);
        var status = WorkspaceValidationService.ComputeOverallStatus(cancelled, merged, null, runTests: false);

        Assert.AreEqual(2, merged.Length, "both harvests contribute; the shared row dedups exactly once.");
        Assert.AreEqual("compile-error", status);
    }

    // ---- validate-workspace-compiler-gate-scope-to-category: gate scoped to Category=="Compiler" ----

    /// <summary>
    /// Acceptance-bullet-2 repro: a complete, genuinely-green compile pass (Cancelled=false,
    /// CompletedProjects==TotalProjects, ErrorCount==0) plus a synthetic WORKSPACE001
    /// (Category=="Workspace") row riding in the same CompilerDiagnostics arm. The corroboration gate
    /// must apply ONLY to Category=="Compiler" rows — WORKSPACE001 is a genuine "failed to load this
    /// project's compilation" signal and must survive, driving the verdict away from "clean".
    /// </summary>
    [TestMethod]
    public void MergeErrorDiagnostics_CleanCompile_WorkspaceCategoryRow_Survives_AndIsNotClean()
    {
        var compileClean = CleanCompile();
        var diagResult = DiagResult(
            compilerDiagnostics: [new DiagnosticDto("WORKSPACE001", "Could not get compilation for project 'P'", "Error", "Workspace", "P.csproj", null, null, null, null)]);

        var merged = WorkspaceValidationService.MergeErrorDiagnostics(compileClean, diagResult);
        var status = WorkspaceValidationService.ComputeOverallStatus(compileClean, merged, null, runTests: false);

        Assert.AreEqual(1, merged.Length, "a Workspace-category row is not subject to the compiler-arm corroboration gate and must survive a clean, complete compile pass.");
        Assert.AreEqual("WORKSPACE001", merged[0].Id);
        Assert.AreNotEqual("clean", status, "a real 'failed to load compilation' signal must not be reported as a clean build.");
        Assert.AreEqual("analyzer-error", status, "compile.ErrorCount is 0, so the surviving row degrades to analyzer-error rather than compile-error.");
    }

    /// <summary>
    /// Proves the partition is per-row, not per-arm: with the SAME clean, complete compile pass, a
    /// WORKSPACE001 (Category=="Workspace") row and an uncorroborated phantom (Category=="Compiler")
    /// row both ride in CompilerDiagnostics. Only the Workspace-category row survives — the
    /// Compiler-category row is still dropped by the corroboration gate.
    /// </summary>
    [TestMethod]
    public void MergeErrorDiagnostics_MixedCategoryRows_OnlyNonCompilerRowSurvives()
    {
        var compileClean = CleanCompile();
        var diagResult = DiagResult(
            compilerDiagnostics: [
                new DiagnosticDto("WORKSPACE001", "Could not get compilation for project 'P'", "Error", "Workspace", "P.csproj", null, null, null, null),
                new DiagnosticDto("CS0103", "x", "Error", "Compiler", "Y.cs", 2, 1, 2, 5)
            ]);

        var merged = WorkspaceValidationService.MergeErrorDiagnostics(compileClean, diagResult);

        Assert.AreEqual(1, merged.Length, "the partition is per-row: the Workspace-category row survives while the Compiler-category row is still gated.");
        Assert.AreEqual("WORKSPACE001", merged[0].Id);
    }

    /// <summary>
    /// Regression guard: an incomplete/cancelled compile pass plus a WORKSPACE001 row still merges —
    /// this combination already worked pre-fix (the whole arm was unconditionally merged whenever the
    /// compile pass forfeited authority) and must keep working once the gate is scoped per-row.
    /// </summary>
    [TestMethod]
    public void MergeErrorDiagnostics_CancelledCompile_WorkspaceCategoryRow_StillMerges()
    {
        var cancelled = CancelledCompile(completedProjects: 1, totalProjects: 4);
        var diagResult = DiagResult(
            compilerDiagnostics: [new DiagnosticDto("WORKSPACE001", "Could not get compilation for project 'P'", "Error", "Workspace", "P.csproj", null, null, null, null)]);

        var merged = WorkspaceValidationService.MergeErrorDiagnostics(cancelled, diagResult);

        Assert.AreEqual(1, merged.Length, "a Workspace-category row must still merge when the compile pass is incomplete, mirroring the pre-existing Compiler-category authority-gate behavior.");
        Assert.AreEqual("WORKSPACE001", merged[0].Id);
    }
}
