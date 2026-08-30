using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

[DoNotParallelize]
[TestClass]
public sealed class DiagnosticSourceGeneratorParityTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task DiagnosticSurfaces_MatchBuildForGeneratedAndUnimplementedPartialMethods()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var probePath = workspace.GetPath("SampleLib", "GeneratedRegexDiagnosticProbe.cs");
        await File.WriteAllTextAsync(
            probePath,
            """
            using System.Text.RegularExpressions;

            namespace SampleLib;

            public static partial class GeneratedRegexDiagnosticProbe
            {
                [GeneratedRegex("^[a-z]+$")]
                public static partial Regex ValidPattern();
            }
            """,
            CancellationToken.None);

        await RestoreWorkspaceAsync(workspace, CancellationToken.None);
        await workspace.LoadAsync(CancellationToken.None);

        var build = await BuildService.BuildWorkspaceAsync(
            workspace.WorkspaceId,
            CancellationToken.None);
        Assert.IsTrue(
            build.Execution.Succeeded,
            $"The process-level build is the parity authority. StdOut={build.Execution.StdOut} StdErr={build.Execution.StdErr}");

        var cleanCompile = await CompileCheckService.CheckAsync(
            workspace.WorkspaceId,
            new CompileCheckOptions(ProjectFilter: "SampleLib", SeverityFilter: "Error"),
            CancellationToken.None);
        var cleanDiagnostics = await DiagnosticService.GetDiagnosticsAsync(
            workspace.WorkspaceId,
            projectFilter: "SampleLib",
            fileFilter: null,
            severityFilter: "Error",
            diagnosticIdFilter: null,
            CancellationToken.None);

        Assert.IsFalse(
            cleanCompile.Diagnostics.Any(diagnostic => diagnostic.Id == "CS8795"),
            "compile_check must execute the GeneratedRegex source generator instead of reporting a false missing implementation.");
        Assert.IsFalse(
            cleanDiagnostics.CompilerDiagnostics.Any(diagnostic => diagnostic.Id == "CS8795"),
            "project_diagnostics must execute the GeneratedRegex source generator instead of reporting a false missing implementation.");

        await File.AppendAllTextAsync(
            probePath,
            """


            public static partial class MissingPartialImplementationProbe
            {
                public static partial int MissingImplementation();
            }
            """,
            CancellationToken.None);
        await workspace.ReloadAsync(CancellationToken.None);

        var brokenCompile = await CompileCheckService.CheckAsync(
            workspace.WorkspaceId,
            new CompileCheckOptions(ProjectFilter: "SampleLib", SeverityFilter: "Error"),
            CancellationToken.None);
        var brokenDiagnostics = await DiagnosticService.GetDiagnosticsAsync(
            workspace.WorkspaceId,
            projectFilter: "SampleLib",
            fileFilter: null,
            severityFilter: "Error",
            diagnosticIdFilter: null,
            CancellationToken.None);

        Assert.AreEqual(
            1,
            brokenCompile.Diagnostics.Count(diagnostic => diagnostic.Id == "CS8795"),
            "compile_check must retain the genuine missing-partial diagnostic.");
        Assert.AreEqual(
            1,
            brokenDiagnostics.CompilerDiagnostics.Count(diagnostic => diagnostic.Id == "CS8795"),
            "project_diagnostics must retain the genuine missing-partial diagnostic.");
    }
}
