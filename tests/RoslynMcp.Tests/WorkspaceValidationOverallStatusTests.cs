using RoslynMcp.Core.Models;
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

    [TestMethod]
    public void ComputeOverallStatus_CompilerErrorOnlyInMergedErrors_YieldsCompileError()
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

        Assert.AreEqual("compile-error", status);
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
}
