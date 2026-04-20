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
                .Count(w => string.Equals(w.LoadedPath, copiedSolutionPath, StringComparison.OrdinalIgnoreCase));
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
}
