using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class DryRunPreviewSideEffectAuditTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task RenamePreview_StoresTokenWithoutMutatingWorkspaceVersionSolutionOrDisk()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var catFile = workspace.GetPath("SampleLib", "Cat.cs");
        var projectFile = workspace.GetPath("SampleLib", "SampleLib.csproj");
        var before = await PreviewAuditSnapshot.CaptureAsync(workspace.WorkspaceId, [catFile, projectFile], CancellationToken.None);

        var preview = await RefactoringService.PreviewRenameAsync(
            workspace.WorkspaceId,
            SymbolLocator.BySource(catFile, line: 3, column: 14),
            "PreviewAuditCat",
            CancellationToken.None);

        Assert.IsFalse(string.IsNullOrWhiteSpace(preview.PreviewToken), "Preview must create a redeemable token.");
        var tokenEntry = PreviewStore.Retrieve(preview.PreviewToken);
        Assert.IsNotNull(tokenEntry, "Preview token should be stored; token creation is the expected preview-side cache effect.");
        Assert.AreEqual(workspace.WorkspaceId, tokenEntry.Value.WorkspaceId);
        Assert.AreEqual(before.WorkspaceVersion, tokenEntry.Value.WorkspaceVersion);

        await before.AssertNoWorkspaceOrDiskMutationAsync(CancellationToken.None);
    }

    [TestMethod]
    public async Task MoveTypeAndProjectMutationPreviews_DoNotMutateWorkspaceVersionSolutionOrDisk()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var catFile = workspace.GetPath("SampleLib", "Cat.cs");
        var projectFile = workspace.GetPath("SampleLib", "SampleLib.csproj");
        var targetFile = workspace.GetPath("SampleLib", "PreviewAuditKitten.cs");

        await File.AppendAllTextAsync(
            catFile,
            """

            public class PreviewAuditKitten : IAnimal
            {
                public string Name => "PreviewAuditKitten";
                public string Speak() => "Mew";
            }
            """,
            CancellationToken.None);

        var workspaceId = await workspace.LoadAsync(CancellationToken.None);
        var beforeMove = await PreviewAuditSnapshot.CaptureAsync(workspaceId, [catFile, projectFile, targetFile], CancellationToken.None);

        var movePreview = await TypeMoveService.PreviewMoveTypeToFileAsync(
            workspaceId,
            catFile,
            "PreviewAuditKitten",
            targetFile,
            CancellationToken.None);

        Assert.IsFalse(string.IsNullOrWhiteSpace(movePreview.PreviewToken), "Move-type preview must create a token.");
        var moveToken = PreviewStore.Retrieve(movePreview.PreviewToken);
        Assert.IsNotNull(moveToken, "Move-type preview token should remain available for a later apply.");
        Assert.AreEqual(beforeMove.WorkspaceVersion, moveToken.Value.WorkspaceVersion);

        await beforeMove.AssertNoWorkspaceOrDiskMutationAsync(CancellationToken.None);

        var beforeProjectMutation = await PreviewAuditSnapshot.CaptureAsync(workspaceId, [catFile, projectFile, targetFile], CancellationToken.None);

        var projectPreview = await ProjectMutationService.PreviewAddPackageReferenceAsync(
            workspaceId,
            new AddPackageReferenceDto("SampleLib", "Humanizer.Core", "2.14.1"),
            CancellationToken.None);

        Assert.IsFalse(string.IsNullOrWhiteSpace(projectPreview.PreviewToken), "Project mutation preview must create a token.");
        Assert.IsTrue(projectPreview.Changes.Any(change =>
            string.Equals(change.FilePath, projectFile, StringComparison.OrdinalIgnoreCase)),
            "Project mutation preview should report the project-file diff without writing it.");

        await beforeProjectMutation.AssertNoWorkspaceOrDiskMutationAsync(CancellationToken.None);
    }

    private sealed class PreviewAuditSnapshot
    {
        private readonly string _workspaceId;
        private readonly Solution _solution;
        private readonly Dictionary<string, byte[]?> _fileBytes;

        private PreviewAuditSnapshot(string workspaceId, int workspaceVersion, Solution solution, Dictionary<string, byte[]?> fileBytes)
        {
            _workspaceId = workspaceId;
            WorkspaceVersion = workspaceVersion;
            _solution = solution;
            _fileBytes = fileBytes;
        }

        public int WorkspaceVersion { get; }

        public static async Task<PreviewAuditSnapshot> CaptureAsync(
            string workspaceId,
            IEnumerable<string> paths,
            CancellationToken cancellationToken)
        {
            var fileBytes = new Dictionary<string, byte[]?>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in paths)
            {
                fileBytes[path] = File.Exists(path)
                    ? await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false)
                    : null;
            }

            return new PreviewAuditSnapshot(
                workspaceId,
                WorkspaceManager.GetCurrentVersion(workspaceId),
                WorkspaceManager.GetCurrentSolution(workspaceId),
                fileBytes);
        }

        public async Task AssertNoWorkspaceOrDiskMutationAsync(CancellationToken cancellationToken)
        {
            Assert.AreEqual(
                WorkspaceVersion,
                WorkspaceManager.GetCurrentVersion(_workspaceId),
                "Preview-only operation must not bump workspace version.");

            var currentSolution = WorkspaceManager.GetCurrentSolution(_workspaceId);
            var projectChanges = currentSolution.GetChanges(_solution).GetProjectChanges().ToList();
            Assert.AreEqual(
                0,
                projectChanges.Count,
                $"Preview-only operation must not mutate the current workspace solution. Changed projects: {string.Join(", ", projectChanges.Select(change => change.ProjectId.Id))}");

            foreach (var (path, expectedBytes) in _fileBytes)
            {
                if (expectedBytes is null)
                {
                    Assert.IsFalse(File.Exists(path), $"Preview-only operation created file on disk: {path}");
                    continue;
                }

                Assert.IsTrue(File.Exists(path), $"Preview-only operation removed file on disk: {path}");
                var actualBytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
                CollectionAssert.AreEqual(expectedBytes, actualBytes, $"Preview-only operation changed bytes on disk: {path}");
            }
        }
    }
}
