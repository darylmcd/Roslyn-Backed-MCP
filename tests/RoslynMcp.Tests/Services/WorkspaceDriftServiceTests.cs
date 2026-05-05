namespace RoslynMcp.Tests.Services;

/// <summary>
/// Integration tests for <see cref="Roslyn.Services.WorkspaceDriftService"/>. Uses
/// <see cref="IsolatedWorkspaceTestBase"/> so each test has a private on-disk solution
/// copy that can be mutated (overwrite, delete) without polluting the shared SampleSolution
/// fixture used by other test classes.
/// </summary>
[TestClass]
public sealed class WorkspaceDriftServiceTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    /// <summary>
    /// Clean workspace: no on-disk changes since load → drift check returns
    /// <c>{ stale: false, files_drifted: [], recommended: "noop" }</c>. This is the
    /// hot-path agents will hit most often when the workspace and disk are in sync.
    /// </summary>
    [TestMethod]
    public async Task CheckDriftAsync_CleanWorkspace_ReturnsNotStale()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);

        var result = await WorkspaceDriftService.CheckDriftAsync(
            workspace.WorkspaceId,
            CancellationToken.None);

        Assert.IsFalse(result.Stale, "Fresh workspace with no on-disk edits should not be stale.");
        Assert.AreEqual(0, result.FilesDrifted.Count, "files_drifted should be empty when not stale.");
        Assert.AreEqual("noop", result.Recommended, "Recommendation should be 'noop' when not stale.");
    }

    /// <summary>
    /// Out-of-band edit after load → drift detected, the touched file appears in
    /// <c>files_drifted</c>, recommendation is <c>"reload"</c>. This is the central
    /// motivator for the tool: agents that <c>Edit</c>/<c>Write</c> a tracked .cs file
    /// directly need to know the workspace's in-memory copy is now stale.
    /// </summary>
    [TestMethod]
    public async Task CheckDriftAsync_FileEditedAfterLoad_ReturnsStaleWithPath()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);

        // Pick any tracked .cs file in the loaded solution and bump its mtime past the
        // workspace's LoadedAtUtc. The workspace was loaded synchronously above; a small
        // delay guarantees the post-load mtime is strictly greater on every filesystem
        // (NTFS resolves to ~100ns but the WriteAllText / GetLastWriteTimeUtc round-trip
        // is observed at >50ms in practice).
        var solution = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var targetPath = solution.Projects
            .SelectMany(p => p.Documents)
            .Select(d => d.FilePath)
            .First(p => !string.IsNullOrEmpty(p) && p!.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))!;

        await Task.Delay(50, CancellationToken.None);
        var original = File.ReadAllText(targetPath);
        File.WriteAllText(targetPath, original + "\n// drift-test\n");

        var result = await WorkspaceDriftService.CheckDriftAsync(
            workspace.WorkspaceId,
            CancellationToken.None);

        Assert.IsTrue(result.Stale, "Workspace should be stale after an out-of-band edit to a tracked file.");
        Assert.AreEqual("reload", result.Recommended, "Recommendation should be 'reload' when drifted.");
        CollectionAssert.Contains(
            result.FilesDrifted.ToArray(),
            targetPath,
            $"files_drifted should include the edited path. Got: [{string.Join(", ", result.FilesDrifted)}]");
    }

    /// <summary>
    /// Tracked file deleted from disk after load → still treated as drift so the caller
    /// reloads and the deletion surfaces through the normal Roslyn diagnostic path
    /// rather than as a stale read against an in-memory <c>SourceText</c>.
    /// </summary>
    [TestMethod]
    public async Task CheckDriftAsync_FileDeletedAfterLoad_ReturnsStaleWithPath()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);

        var solution = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var targetPath = solution.Projects
            .SelectMany(p => p.Documents)
            .Select(d => d.FilePath)
            .First(p => !string.IsNullOrEmpty(p) && p!.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))!;

        File.Delete(targetPath);

        var result = await WorkspaceDriftService.CheckDriftAsync(
            workspace.WorkspaceId,
            CancellationToken.None);

        Assert.IsTrue(result.Stale, "Workspace should be stale after a tracked file is deleted.");
        Assert.AreEqual("reload", result.Recommended, "Recommendation should be 'reload' when a tracked file vanishes.");
        CollectionAssert.Contains(
            result.FilesDrifted.ToArray(),
            targetPath,
            $"files_drifted should include the deleted path. Got: [{string.Join(", ", result.FilesDrifted)}]");
    }

    /// <summary>
    /// Output determinism: the <c>files_drifted</c> list must be sorted ordinally so
    /// repeat calls on the same drifted state produce byte-identical JSON. Agents and
    /// snapshot tests key on this — an unordered diff would break their caching.
    /// </summary>
    [TestMethod]
    public async Task CheckDriftAsync_MultipleDrifts_ReturnsSortedList()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);

        var solution = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var paths = solution.Projects
            .SelectMany(p => p.Documents)
            .Select(d => d.FilePath)
            .Where(p => !string.IsNullOrEmpty(p) && p!.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Cast<string>()
            .Take(2)
            .ToArray();

        // Need at least 2 drifted files for the sort assertion to be meaningful.
        if (paths.Length < 2)
        {
            Assert.Inconclusive("SampleSolution does not expose enough .cs files to validate sort order.");
        }

        await Task.Delay(50, CancellationToken.None);
        foreach (var path in paths)
        {
            File.WriteAllText(path, File.ReadAllText(path) + "\n// drift-test\n");
        }

        var result = await WorkspaceDriftService.CheckDriftAsync(
            workspace.WorkspaceId,
            CancellationToken.None);

        var expected = result.FilesDrifted.OrderBy(p => p, StringComparer.Ordinal).ToArray();
        CollectionAssert.AreEqual(
            expected,
            result.FilesDrifted.ToArray(),
            "files_drifted must be sorted ordinally for deterministic output.");
    }
}
