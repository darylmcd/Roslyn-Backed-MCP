namespace RoslynMcp.Tests;

/// <summary>
/// Item #1 — regression guard for `severity-critical-fail-preview-diff-does-not-match-t`
/// (`dr-9-2-leaves-duplicate-type-in-source-file-and-creates`). The
/// NetworkDocumentation audit §9.2 repro: move_type_to_file_preview → apply showed a
/// deletion + creation in the preview, but on-disk left a duplicate type in the source
/// file AND created a rogue copy at project root. Root cause was an AddDocument call
/// that omitted <c>folders</c>, letting MSBuildWorkspace resolve the disk path to
/// <c>{projectDir}/{fileName}</c> while our explicit write used the requested deep path.
/// </summary>
[TestClass]
public sealed class MoveTypeDiskStateTests : TestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        InitializeServices();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        DisposeServices();
    }

    [TestMethod]
    public async Task MoveType_Apply_Creates_Only_The_File_In_The_Preview_Diff()
    {
        // Build a multi-type file in a subdirectory, move one type to a sibling file in the
        // same subdirectory, then assert the project directory contains exactly the expected
        // files — NOT an additional rogue project-root copy of the moved type.
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var sampleLibDir = Path.Combine(solutionDir, "SampleLib");
        var modelsDir = Path.Combine(sampleLibDir, "Models");
        Directory.CreateDirectory(modelsDir);

        var multiTypeFile = Path.Combine(modelsDir, "Item1Guard_MultiType.cs");
        await File.WriteAllTextAsync(multiTypeFile,
            """
            namespace SampleLib.Models;

            public class Item1Guard_KeepInSource
            {
                public int KeepValue { get; set; }
            }

            public class Item1Guard_Moved
            {
                public int MovedValue { get; set; }
            }
            """);

        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        var workspaceId = loadResult.WorkspaceId;

        try
        {
            var targetPath = Path.Combine(modelsDir, "Item1Guard_Moved.cs");

            var previewDto = await TypeMoveService.PreviewMoveTypeToFileAsync(
                workspaceId,
                multiTypeFile,
                "Item1Guard_Moved",
                targetFilePath: null, // defaults to sibling with typeName.cs
                CancellationToken.None);

            // Sanity: preview diff should list exactly one creation (target) and a modification
            // to the source file.
            Assert.IsTrue(previewDto.Changes.Count >= 2,
                $"Expected at least source-modification + target-creation in preview. Got {previewDto.Changes.Count}.");

            var applyResult = await RefactoringService.ApplyRefactoringAsync(previewDto.PreviewToken, CancellationToken.None);
            Assert.IsTrue(applyResult.Success, $"Apply failed: {applyResult.Error}");

            // Expected post-apply shape.
            Assert.IsTrue(File.Exists(targetPath), "Target file must exist at the preview's declared path.");
            var sourceAfter = await File.ReadAllTextAsync(multiTypeFile);
            Assert.IsFalse(
                sourceAfter.Contains("Item1Guard_Moved"),
                "Source file must no longer contain the moved type. Before Item #1 the deletion was not reflected on disk.");

            // ROGUE-FILE GUARD: the project directory must NOT contain a stray
            // {projectDir}/Item1Guard_Moved.cs copy. Before the Item #1 fix, MSBuildWorkspace's
            // TryApplyChanges-as-added path resolved the disk location to project root because
            // `folders` was not passed on AddDocument.
            var rogueCopyAtProjectRoot = Path.Combine(sampleLibDir, "Item1Guard_Moved.cs");
            Assert.IsFalse(
                File.Exists(rogueCopyAtProjectRoot),
                $"A rogue copy of the moved type appeared at the project root ({rogueCopyAtProjectRoot}). " +
                "This reproduces the NetworkDocumentation audit §9.2 defect and means the " +
                "folders argument is being dropped somewhere in the AddDocument call chain.");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
            TryDeleteDirectory(solutionDir);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }
}
