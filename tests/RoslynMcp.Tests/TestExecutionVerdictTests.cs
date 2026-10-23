using System.Reflection;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class TestExecutionVerdictTests
{
    [TestMethod]
    [DataRow("exit", "test-failure")]
    [DataRow("envelope", "test-failure")]
    [DataRow("inconsistent-exit", "test-failure")]
    [DataRow("early-kill", "test-failure")]
    [DataRow("negative-total", "test-failure")]
    [DataRow("negative-failed", "test-failure")]
    [DataRow("timeout", "timeout")]
    [DataRow("zero", "test-zero-run")]
    [DataRow("zero-failed-command", "test-zero-run")]
    [DataRow("failed-test", "test-failure")]
    [DataRow("pass", "clean")]
    public void WorkspaceVerdict_RequiresSuccessfulExecutionAndPreservesPrecedence(string shape, string expected)
    {
        var result = Result(shape);
        Assert.AreEqual(expected, WorkspaceValidationService.ComputeOverallStatus(Compile(), [], result, true));
        Assert.AreEqual("clean", WorkspaceValidationService.ComputeOverallStatus(Compile(), [], result, false));
        Assert.AreEqual("compile-error", WorkspaceValidationService.ComputeOverallStatus(Compile() with { ErrorCount = 1 }, [], result, true));
    }

    [TestMethod]
    [DataRow("exit", false)]
    [DataRow("envelope", false)]
    [DataRow("inconsistent-exit", false)]
    [DataRow("early-kill", false)]
    [DataRow("negative-total", false)]
    [DataRow("negative-failed", false)]
    [DataRow("timeout", false)]
    [DataRow("zero", false)]
    [DataRow("zero-failed-command", false)]
    [DataRow("failed-test", false)]
    [DataRow("pass", true)]
    public void ForkVerdict_RequiresSuccessfulExplicitTestExecution(string shape, bool expected)
    {
        var validation = new WorkspaceValidationDto("clean", [], [], Compile(), [], 0, 0, [], null, null, []);
        var method = typeof(WorkspaceForkApplyService).GetMethod("IsValidationSuccess", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.AreEqual(expected, (bool)method.Invoke(null, [validation, Result(shape)])!);
        Assert.IsFalse((bool)method.Invoke(null, [validation with { OverallStatus = "compile-incomplete" }, Result(shape)])!);
        Assert.IsTrue((bool)method.Invoke(null, [validation, null])!);
    }

    private static CompileCheckDto Compile() => new(true, 0, 0, 0, 0, 0, 200, false, [], 0,
        CompletedProjects: 1, TotalProjects: 1);

    private static TestRunResultDto Result(string shape)
    {
        var total = shape.StartsWith("zero", StringComparison.Ordinal) ? 0 : shape == "negative-total" ? -1 : 1;
        var failed = shape == "failed-test" ? 1 : shape == "negative-failed" ? -1 : 0;
        var execution = new CommandExecutionDto("dotnet", ["test"], "", "", shape is "exit" or "inconsistent-exit" or "zero-failed-command" ? 1 : 0,
            shape is not "exit" and not "zero-failed-command", 1, "", "", shape == "early-kill" ? "FileLock" : null);
        var envelope = shape is "envelope" or "timeout"
            ? new TestRunFailureEnvelopeDto(shape == "timeout" ? "Timeout" : "BuildFailure", false, "Synthetic execution failure", null, null) : null;
        return new(execution, total, total - failed, failed, 0, [], envelope);
    }
}
