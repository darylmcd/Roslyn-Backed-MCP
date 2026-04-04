namespace RoslynMcp.Tests;

[TestClass]
public class DiagnosticFixIntegrationTests : TestBase
{
    private static string WorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        var status = await WorkspaceManager.LoadAsync(SampleSolutionPath, CancellationToken.None);
        WorkspaceId = status.WorkspaceId;
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        DisposeServices();
    }

    [TestMethod]
    public async Task Diagnostic_Details_Returns_Curated_Code_Fix_For_Unused_Using()
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var animalServiceFile = solution.Projects.SelectMany(project => project.Documents).First(document => document.Name == "AnimalService.cs");

        var details = await DiagnosticService.GetDiagnosticDetailsAsync(
            WorkspaceId,
            diagnosticId: "CS8019",
            filePath: animalServiceFile.FilePath!,
            line: 1,
            column: 1,
            CancellationToken.None);

        Assert.IsNotNull(details);
        Assert.AreEqual("CS8019", details.Diagnostic.Id);
        Assert.IsTrue(details.SupportedFixes.Any(fix => fix.FixId == "remove_unused_using"));
    }

    [TestMethod]
    public async Task Diagnostic_Details_For_CS0414_Includes_Curated_RemoveUnusedField_Fix()
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var probeFile = solution.Projects.SelectMany(project => project.Documents).First(document => document.Name == "DiagnosticsProbe.cs");

        var details = await DiagnosticService.GetDiagnosticDetailsAsync(
            WorkspaceId,
            diagnosticId: "CS0414",
            filePath: probeFile.FilePath!,
            line: 9,
            column: 24,
            CancellationToken.None);

        Assert.IsNotNull(details);
        Assert.AreEqual(1, details.SupportedFixes.Count);
        Assert.AreEqual("remove_unused_field", details.SupportedFixes[0].FixId);
    }

    [TestMethod]
    public async Task Code_Fix_Preview_And_Apply_Removes_Unused_Using_In_Isolated_Copy()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;
        var serviceFilePath = Path.Combine(copiedRoot, "SampleLib", "AnimalService.cs");

        try
        {
            var copiedWorkspace = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
            var preview = await RefactoringService.PreviewCodeFixAsync(
                copiedWorkspace.WorkspaceId,
                diagnosticId: "CS8019",
                filePath: serviceFilePath,
                line: 1,
                column: 1,
                fixId: "remove_unused_using",
                CancellationToken.None);

            Assert.IsTrue(preview.Changes.Count > 0, "Code fix preview should produce changes.");

            var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, CancellationToken.None);

            Assert.IsTrue(applyResult.Success, applyResult.Error);
            var contents = await File.ReadAllTextAsync(serviceFilePath, CancellationToken.None);
            Assert.IsFalse(contents.Contains("using System.Threading;"));
        }
        finally
        {
            DeleteDirectoryIfExists(copiedRoot);
        }
    }
}
