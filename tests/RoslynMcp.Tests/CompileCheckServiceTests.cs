using System.Xml.Linq;
using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class CompileCheckServiceTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task CheckAsync_FileFilterOwnedByOneProject_CompilesOnlyOwningProject()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        var unrelatedAppPath = workspace.GetPath("SampleApp", "Program.cs");
        await File.AppendAllTextAsync(
            unrelatedAppPath,
            $"{Environment.NewLine}this is not valid csharp{Environment.NewLine}",
            CancellationToken.None);

        await workspace.LoadAsync(CancellationToken.None);
        var dogPath = workspace.GetPath("SampleLib", "Dog.cs");

        var result = await CompileCheckService.CheckAsync(
            workspace.WorkspaceId,
            new CompileCheckOptions(SeverityFilter: "Error", FileFilter: dogPath),
            CancellationToken.None);

        Assert.AreEqual(1, result.TotalProjects,
            "A file filter that resolves to one project should compile only that owning project.");
        Assert.AreEqual(1, result.CompletedProjects);
        Assert.AreEqual(0, result.ErrorCount,
            "The broken unrelated project must not participate in the scoped compile check.");
        Assert.AreEqual("files", result.RequestedScope);
        Assert.AreEqual(result.RequestedScope, result.ActualScope,
            "A file filter honoured by its single owning project must not report a widened scope.");
        Assert.IsNull(result.RestoreHint,
            "A clean scoped compile check with no CS0234 flood, zero-projects fallback, or file-filter widening must not carry a restoreHint.");
    }

    [TestMethod]
    public async Task CheckAsync_FileFilterResolvingToNoDocument_FailsLoudInsteadOfWidening()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        // Guarantee the solution carries real compile errors, so a widened full-solution compile
        // would have plenty of diagnostics to (incorrectly) filter away by the bogus path.
        var brokenPath = workspace.GetPath("SampleApp", "Program.cs");
        await File.AppendAllTextAsync(
            brokenPath,
            $"{Environment.NewLine}this is not valid csharp{Environment.NewLine}",
            CancellationToken.None);

        await workspace.LoadAsync(CancellationToken.None);
        var missingPath = workspace.GetPath("SampleLib", "NoSuchFile.cs");

        var result = await CompileCheckService.CheckAsync(
            workspace.WorkspaceId,
            new CompileCheckOptions(SeverityFilter: "Error", FileFilter: missingPath),
            CancellationToken.None);

        Assert.IsFalse(result.Success,
            "A file scope resolving to zero workspace documents must not report a vacuous green pass.");
        Assert.AreEqual(0, result.TotalProjects,
            "The zero-resolution arm must not widen to the full project list.");
        Assert.AreEqual(0, result.CompletedProjects);
        StringAssert.Contains(result.RestoreHint, "did not resolve to any loaded workspace document");
        Assert.AreEqual("files", result.RequestedScope);
        Assert.AreEqual(result.RequestedScope, result.ActualScope,
            "Nothing was compiled, so no widening to solution scope may be claimed.");
    }

    [TestMethod]
    public async Task CheckAsync_FileFiltersAcrossProjects_FallsBackToFullScopeWithHint()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);

        var dogPath = workspace.GetPath("SampleLib", "Dog.cs");
        var programPath = workspace.GetPath("SampleApp", "Program.cs");

        var result = await CompileCheckService.CheckAsync(
            workspace.WorkspaceId,
            new CompileCheckOptions(
                SeverityFilter: "Error",
                FileFilters: [dogPath, programPath]),
            CancellationToken.None);

        Assert.IsTrue(result.TotalProjects > 1,
            "File filters spanning multiple projects should fall back to the full project scope.");
        Assert.IsFalse(string.IsNullOrEmpty(result.RestoreHint),
            "Fallback restoreHint must be non-empty when file filters span multiple projects.");
        StringAssert.Contains(result.RestoreHint, "file filter fallback");
        Assert.AreEqual("files", result.RequestedScope);
        Assert.AreEqual("solution", result.ActualScope);
        Assert.AreNotEqual(result.RequestedScope, result.ActualScope,
            "A widened file scope must be detectable structurally, not only by parsing restoreHint.");
    }

    [TestMethod]
    public async Task CheckAsync_WhitespaceOnlyProjectFilterWithMultiProjectFiles_NoLongerMisreportsSolutionScope()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);

        var dogPath = workspace.GetPath("SampleLib", "Dog.cs");
        var programPath = workspace.GetPath("SampleApp", "Program.cs");

        var result = await CompileCheckService.CheckAsync(
            workspace.WorkspaceId,
            new CompileCheckOptions(
                SeverityFilter: "Error",
                ProjectFilter: " ",
                FileFilters: [dogPath, programPath]),
            CancellationToken.None);

        Assert.IsTrue(result.TotalProjects > 1,
            "A whitespace-only projectFilter must be treated as no filter, not as a literal (nonexistent) project name.");
        Assert.AreEqual("solution", result.ActualScope);
        Assert.AreEqual("files", result.RequestedScope,
            "Whitespace-only projectFilter must not be classified as a project-scoped request.");
        StringAssert.Contains(result.RestoreHint, "file filter fallback");
    }

    [TestMethod]
    public async Task CheckAsync_WhitespaceOnlyProjectFilterWithZeroProjects_ReportsWorkspaceReloadHintNotFilterMismatch()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        // Strip every <Project> entry so the copied solution loads with zero projects. This is
        // the only way to reach BuildHint's zeroProjectsHint branch with a whitespace-only
        // projectFilter: ProjectFilterHelper.FilterProjects treats whitespace as "no filter" and
        // returns solution.Projects, so a normal (non-empty) sample solution would still resolve
        // to >0 projects regardless of the filter's whitespace-ness.
        var solutionFilePath = workspace.GetPath("SampleSolution.slnx");
        var solutionDocument = XDocument.Load(solutionFilePath, LoadOptions.PreserveWhitespace);
        solutionDocument.Root?.Elements("Project").Remove();
        solutionDocument.Save(solutionFilePath, SaveOptions.DisableFormatting);

        await workspace.LoadAsync(CancellationToken.None);

        var result = await CompileCheckService.CheckAsync(
            workspace.WorkspaceId,
            new CompileCheckOptions(SeverityFilter: "Error", ProjectFilter: "   "),
            CancellationToken.None);

        Assert.AreEqual(0, result.TotalProjects,
            "The zero-project solution must resolve to zero projects regardless of the whitespace-only filter.");
        Assert.IsFalse(result.Success,
            "Zero evaluated projects must not report a vacuous success.");
        StringAssert.Contains(result.RestoreHint, "call workspace_reload explicitly",
            "A whitespace-only projectFilter is not a real filter — the zero-projects hint must fire the generic " +
            "workspace-reload wording (string.IsNullOrWhiteSpace discriminator), matching every other whitespace " +
            "guard in this file since PR #1191.");
        Assert.IsFalse(
            result.RestoreHint!.Contains("did not match any project", StringComparison.Ordinal),
            "A whitespace-only projectFilter must never be treated as a literal (nonexistent) project name, so " +
            $"the filter-mismatch wording must not fire. Got: {result.RestoreHint}");
    }
}
