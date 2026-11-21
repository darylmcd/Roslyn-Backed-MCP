using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression (relocation-selected-enum-delegate-reported-not-found, same mechanism): the
/// cross-project routes looked the selected type up among <c>TypeDeclarationSyntax</c> only, so an
/// enum or delegate selection was reported as "not found in the source document".
/// </summary>
[TestClass]
public sealed class CrossProjectUnsupportedKindRefusalTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    private const string FixtureDirectory = "UnsupportedKindFixture";

    public static IEnumerable<object[]> Cases()
    {
        foreach (var (route, tool, supported) in new[]
                 {
                     ("move", "move_type_to_project_preview", "class, struct, record, and interface declarations"),
                     ("cross-project", "extract_interface_cross_project_preview", "class and struct declarations"),
                     ("dependency-inversion", "dependency_inversion_preview", "class and struct declarations"),
                     ("extract-and-wire", "extract_and_wire_interface_preview", "class and struct declarations"),
                 })
        {
            yield return [route, tool, supported, "public enum Selected { A }", "Enum"];
            yield return [route, tool, supported, "public delegate void Selected();", "Delegate"];
        }
    }

    [TestMethod]
    [DynamicData(nameof(Cases))]
    public async Task Route_Names_The_Unsupported_Kind_For_An_Enum_Or_Delegate_Selection(
        string route, string tool, string supported, string declaration, string kind)
    {
        var (workspace, sourcePath) = await PrepareAsync("namespace Old\n{\n    " + declaration + "\n\n    public class Keep { }\n}\n");
        await using var _ = workspace;

        var ex = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            PreviewAsync(workspace, route, sourcePath, "Selected"));

        Assert.AreEqual(
            $"type-kind {kind} not supported for this refactor. {tool} currently supports {supported} only.",
            ex.Message);
    }

    [TestMethod]
    [DataRow("move")]
    [DataRow("cross-project")]
    [DataRow("dependency-inversion")]
    [DataRow("extract-and-wire")]
    public async Task Route_Still_Reports_Not_Found_For_A_Missing_Type(string route)
    {
        var (workspace, sourcePath) = await PrepareAsync("namespace Old\n{\n    public enum Other { A }\n\n    public class Keep { }\n}\n");
        await using var _ = workspace;

        var ex = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            PreviewAsync(workspace, route, sourcePath, "Selected"));

        Assert.AreEqual("Type 'Selected' was not found in the source document.", ex.Message);
    }

    private static async Task<(IsolatedWorkspaceScope Workspace, string SourcePath)> PrepareAsync(string source)
    {
        var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        var directory = Path.Combine(workspace.GetPath("SampleLib"), FixtureDirectory);
        Directory.CreateDirectory(directory);
        var sourcePath = Path.Combine(directory, "Source.cs");
        await File.WriteAllTextAsync(sourcePath, source, CancellationToken.None);
        await workspace.LoadAsync(CancellationToken.None);
        return (workspace, sourcePath);
    }

    private static Task PreviewAsync(IsolatedWorkspaceScope workspace, string route, string sourcePath, string typeName) =>
        route switch
        {
            "move" => CrossProjectRefactoringService.PreviewMoveTypeToProjectAsync(
                workspace.WorkspaceId, sourcePath, typeName, "Contracts", null, CancellationToken.None),
            "cross-project" => CrossProjectRefactoringService.PreviewExtractInterfaceAsync(
                workspace.WorkspaceId, sourcePath, typeName, "ISelected", "Contracts", CancellationToken.None),
            "dependency-inversion" => CrossProjectRefactoringService.PreviewDependencyInversionAsync(
                workspace.WorkspaceId, sourcePath, typeName, "ISelected", "Contracts", CancellationToken.None),
            "extract-and-wire" => new ExtractAndWireOrchestrator(WorkspaceManager, new CompositePreviewStore(),
                CrossProjectRefactoringService, DiRegistrationService).PreviewExtractAndWireInterfaceAsync(
                    workspace.WorkspaceId, sourcePath, typeName, "ISelected", "Contracts", false, CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(route)),
        };
}
