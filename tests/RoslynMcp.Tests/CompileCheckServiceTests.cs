using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

[DoNotParallelize]
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
        Assert.IsNotNull(result.RestoreHint);
        StringAssert.Contains(result.RestoreHint, "file filter fallback");
    }
}
