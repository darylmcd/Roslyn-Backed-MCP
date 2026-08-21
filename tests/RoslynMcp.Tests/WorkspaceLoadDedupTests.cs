namespace RoslynMcp.Tests;

/// <summary>
/// Covers the <c>workspace-session-deduplication</c> fix (P3): repeat calls to
/// <see cref="RoslynMcp.Roslyn.Services.WorkspaceManager.LoadAsync"/> with the same path
/// must return the existing session instead of spinning up a second <c>WorkspaceId</c>.
/// Prevents the "one host, two distinct IDs for the same solution" pattern from the
/// 2026-04-08 roslyn-backed-mcp audit.
///
/// Each test uses an isolated <c>CreateSampleSolutionCopy</c> so we can close the
/// workspace on cleanup without invalidating the shared <c>SharedWorkspaceTestBase</c>
/// cache that other test classes depend on.
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class WorkspaceLoadDedupTests : SharedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task LoadAsync_SamePathTwice_ReturnsSameWorkspaceId()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;
        try
        {
            var first = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
            var second = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);

            Assert.AreEqual(first.WorkspaceId, second.WorkspaceId,
                "workspace_load must be idempotent by path — second call should return the existing WorkspaceId.");
            Assert.AreEqual(first.LoadedPath, second.LoadedPath);

            WorkspaceManager.Close(first.WorkspaceId);
        }
        finally
        {
            DeleteDirectoryIfExists(copiedRoot);
        }
    }

    [TestMethod]
    public async Task LoadAsync_SamePathTenTimes_OnlyOneWorkspaceIsTracked()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;
        try
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < 10; i++)
            {
                var status = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
                ids.Add(status.WorkspaceId);
            }

            Assert.AreEqual(1, ids.Count,
                "All 10 repeat loads of the same path should collapse to one WorkspaceId — no extra slot consumption.");

            var matchingCount = WorkspaceManager.ListWorkspaces()
                .Count(w => string.Equals(w.LoadedPath, copiedSolutionPath,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
            Assert.AreEqual(1, matchingCount,
                "workspace_list should show exactly one entry for the isolated copy path.");

            foreach (var id in ids) WorkspaceManager.Close(id);
        }
        finally
        {
            DeleteDirectoryIfExists(copiedRoot);
        }
    }

    [TestMethod]
    public async Task LoadAsync_CaseInsensitivePath_ReturnsSameWorkspaceId()
    {
        // Windows paths are case-insensitive. An upper-cased variant of the sample solution
        // path must still be recognized as the same workspace.
        // Linux/macOS paths are case-sensitive, so the upper-cased path doesn't exist.
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Case-insensitive path dedup only applies on Windows.");
            return;
        }

        var copiedSolutionPath = CreateSampleSolutionCopy();
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;
        try
        {
            var first = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
            var upperCased = copiedSolutionPath.ToUpperInvariant();
            var second = await WorkspaceManager.LoadAsync(upperCased, CancellationToken.None);

            Assert.AreEqual(first.WorkspaceId, second.WorkspaceId,
                "Path dedup must be case-insensitive on Windows.");

            WorkspaceManager.Close(first.WorkspaceId);
        }
        finally
        {
            DeleteDirectoryIfExists(copiedRoot);
        }
    }

    [TestMethod]
    public async Task LoadAsync_CaseDistinctSolutionPaths_OnUnixRemainDistinct()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Case-distinct path identity applies to Unix filesystems.");
            return;
        }

        var copiedSolutionPath = CreateSampleSolutionCopy();
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;
        var originalName = Path.GetFileName(copiedSolutionPath);
        var alternateName = originalName.ToUpperInvariant();
        if (alternateName == originalName)
        {
            alternateName = originalName.ToLowerInvariant();
        }
        Assert.AreNotEqual(originalName, alternateName);
        Assert.IsTrue(string.Equals(originalName, alternateName, StringComparison.OrdinalIgnoreCase));
        var alternatePath = Path.Combine(copiedRoot, alternateName);
        File.Copy(copiedSolutionPath, alternatePath);

        try
        {
            var first = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
            var second = await WorkspaceManager.LoadAsync(alternatePath, CancellationToken.None);

            Assert.AreNotEqual(first.WorkspaceId, second.WorkspaceId,
                "Unix path identity must not collapse solution files that differ only by case.");
            WorkspaceManager.Close(first.WorkspaceId);
            WorkspaceManager.Close(second.WorkspaceId);
        }
        finally
        {
            DeleteDirectoryIfExists(copiedRoot);
        }
    }

    [TestMethod]
    public async Task FindWorkspaceIdsContainingFile_UsesLoadedDocumentMembership()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;
        try
        {
            var status = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
            var documentPath = Directory.EnumerateFiles(copiedRoot, "*.cs", SearchOption.AllDirectories)
                .First();

            var owners = WorkspaceManager.FindWorkspaceIdsContainingFile(documentPath);

            CollectionAssert.AreEqual(new[] { status.WorkspaceId }, owners.ToArray());
            WorkspaceManager.Close(status.WorkspaceId);
        }
        finally
        {
            DeleteDirectoryIfExists(copiedRoot);
        }
    }

    [TestMethod]
    public async Task FindWorkspaceIdsContainingFile_RefreshesAfterAppliedDocumentAddition()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;
        try
        {
            var status = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
            var solution = WorkspaceManager.GetCurrentSolution(status.WorkspaceId);
            var project = solution.Projects.First();
            var documentId = Microsoft.CodeAnalysis.DocumentId.CreateNewId(project.Id);
            var addedPath = Path.Combine(Path.GetDirectoryName(project.FilePath!)!, "OwnershipProbe.cs");
            var withDocument = solution.AddDocument(
                documentId,
                "OwnershipProbe.cs",
                Microsoft.CodeAnalysis.Text.SourceText.From("internal sealed class OwnershipProbe { }"),
                filePath: addedPath);

            Assert.IsTrue(WorkspaceManager.TryApplyChanges(status.WorkspaceId, withDocument));
            CollectionAssert.AreEqual(
                new[] { status.WorkspaceId },
                WorkspaceManager.FindWorkspaceIdsContainingFile(addedPath).ToArray());

            WorkspaceManager.Close(status.WorkspaceId);
        }
        finally
        {
            DeleteDirectoryIfExists(copiedRoot);
        }
    }
}
