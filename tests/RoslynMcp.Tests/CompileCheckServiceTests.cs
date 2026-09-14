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
        Assert.AreEqual("ready", result.Readiness);
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
    public async Task CheckAsync_FileFiltersAcrossProjects_CompilesOnlyOwners()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        await File.WriteAllTextAsync(workspace.GetPath("SampleLib.Tests", "UnrelatedError.cs"),
            "this is not valid csharp", CancellationToken.None);
        await workspace.LoadAsync(CancellationToken.None);

        var dogPath = workspace.GetPath("SampleLib", "Dog.cs");
        var programPath = workspace.GetPath("SampleApp", "Program.cs");

        var result = await CompileCheckService.CheckAsync(
            workspace.WorkspaceId,
            new CompileCheckOptions(
                SeverityFilter: "Error",
                FileFilters: [dogPath, programPath]),
            CancellationToken.None);

        Assert.AreEqual(2, result.TotalProjects);
        Assert.AreEqual(2, result.CompletedProjects);
        Assert.IsNull(result.RestoreHint);
        Assert.IsTrue(result.Success);
        Assert.IsTrue(result.Diagnostics.All(d => d.FilePath == dogPath || d.FilePath == programPath));
        Assert.AreEqual("files", result.RequestedScope);
        Assert.AreEqual("files", result.ActualScope);
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

        Assert.AreEqual(2, result.TotalProjects,
            "A whitespace-only projectFilter must be treated as no filter, not as a literal (nonexistent) project name.");
        Assert.AreEqual("files", result.ActualScope);
        Assert.AreEqual("files", result.RequestedScope,
            "Whitespace-only projectFilter must not be classified as a project-scoped request.");
        Assert.IsNull(result.RestoreHint);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CheckAsync_RestoreRequired_DoesNotEvaluateDiagnostics(bool missingAnalyzer)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var propsPath = workspace.GetPath("Directory.Packages.props");
        var props = XDocument.Load(propsPath);
        var package = props.Descendants("PackageVersion").Single(element =>
            (string?)element.Attribute("Include") == "Microsoft.NET.Test.Sdk");
        package.SetAttributeValue("Version", "0.0.0");
        props.Save(propsPath);
        if (missingAnalyzer) AddMissingAnalyzer(workspace);
        await workspace.LoadAsync(CancellationToken.None);
        var status = await WorkspaceManager.GetStatusAsync(workspace.WorkspaceId);
        Assert.IsTrue(status.RestoreRequired);
        if (missingAnalyzer) Assert.IsTrue(status.BuildRequired);

        var result = await CompileCheckService.CheckAsync(workspace.WorkspaceId,
            new CompileCheckOptions(Offset: 3, Limit: 7), CancellationToken.None);

        Assert.IsFalse(result.Success);
        Assert.AreEqual("restore-required", result.Readiness);
        Assert.AreEqual(0, result.CompletedProjects);
        Assert.AreEqual(0, result.ErrorCount);
        Assert.AreEqual(0, result.WarningCount);
        Assert.AreEqual(0, result.TotalDiagnostics);
        Assert.AreEqual(0, result.ReturnedDiagnostics);
        Assert.IsEmpty(result.Diagnostics);
        Assert.IsFalse(result.HasMore);
        Assert.AreEqual(3, result.Offset);
        Assert.AreEqual(7, result.Limit);
        StringAssert.Contains(result.RestoreHint, "workspace_reload");
        StringAssert.Contains(result.RestoreHint, "autoRestore=true");
    }

    [TestMethod]
    public async Task CheckAsync_BuildRequired_StillReportsCompilerErrors()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddMissingAnalyzer(workspace);
        await File.AppendAllTextAsync(workspace.GetPath("SampleLib", "Dog.cs"),
            "\nthis is not valid csharp\n", CancellationToken.None);
        await workspace.LoadAsync(CancellationToken.None);
        var status = await WorkspaceManager.GetStatusAsync(workspace.WorkspaceId);
        Assert.IsTrue(status.BuildRequired);
        Assert.IsFalse(status.RestoreRequired);

        var result = await CompileCheckService.CheckAsync(workspace.WorkspaceId,
            new CompileCheckOptions(ProjectFilter: "SampleLib"), CancellationToken.None);

        Assert.AreEqual("analyzer-limited", result.Readiness);
        Assert.IsFalse(result.Success);
        Assert.IsGreaterThan(0, result.ErrorCount);
        Assert.IsTrue(result.Diagnostics.Any(d => d.Id.StartsWith("CS", StringComparison.Ordinal)));
        Assert.AreEqual(1, result.CompletedProjects);
        StringAssert.Contains(result.RestoreHint, "build_project");
        StringAssert.Contains(WorkspaceStatusSummaryDto.From(status).RestoreHint, "build_project");
    }

    private static void AddMissingAnalyzer(IsolatedWorkspaceScope workspace)
    {
        var projectPath = workspace.GetPath("SampleLib", "SampleLib.csproj");
        var project = XDocument.Load(projectPath);
        Assert.IsNotNull(project.Root);
        project.Root.Add(new XElement("ItemGroup",
            new XElement("Analyzer", new XAttribute("Include", "MissingAnalyzer.dll"))));
        project.Save(projectPath);
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
