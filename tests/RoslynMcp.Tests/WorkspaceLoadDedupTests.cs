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
            var documentPath = SelectDeterministicProjectSourceFile(copiedRoot);

            var owners = WorkspaceManager.FindWorkspaceIdsContainingFile(documentPath);

            CollectionAssert.AreEqual(
                new[] { status.WorkspaceId },
                owners.ToArray(),
                $"Expected exactly the loaded workspace to own '{documentPath}'.");
            WorkspaceManager.Close(status.WorkspaceId);
        }
        finally
        {
            DeleteDirectoryIfExists(copiedRoot);
        }
    }

    /// <summary>
    /// Picks a project source file that is genuinely a document of the loaded workspace, in an
    /// order that does not depend on the filesystem.
    /// </summary>
    /// <remarks>
    /// This test previously took <c>EnumerateFiles(root, "*.cs", AllDirectories).First()</c>.
    /// Two problems compounded. <c>First()</c> over an unordered enumeration has no defined
    /// result, and enumeration order differs between NTFS and ext4 — so the pick varied by
    /// platform. And the fixture copy only excludes <c>bin</c>, never <c>obj</c> (it cannot:
    /// MSBuildWorkspace needs <c>obj/project.assets.json</c> to load), so the tree carries
    /// generated sources under both <c>obj/Debug</c> and <c>obj/Release</c>. Only the active
    /// configuration's copies are compilation documents, so landing on the other one returned
    /// zero owners and failed as "Different number of elements" — reliably on the Linux shard,
    /// never on Windows.
    /// </remarks>
    private static string SelectDeterministicProjectSourceFile(string copiedRoot)
    {
        var buildOutputSegments = new[]
        {
            $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
        };

        var documentPath = Directory
            .EnumerateFiles(copiedRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !buildOutputSegments.Any(
                segment => path.Contains(segment, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(path => path, StringComparer.Ordinal)
            .FirstOrDefault();

        Assert.IsNotNull(
            documentPath,
            $"The copied sample solution under '{copiedRoot}' contains no project source file.");

        return documentPath;
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

    [TestMethod]
    public async Task LoadAsync_LinkedRootPinsPhysicalDocumentsBeforeLinkSwap()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var physicalRoot = Path.GetDirectoryName(copiedSolutionPath)!;
        var testId = Guid.NewGuid().ToString("N");
        var logicalRoot = Path.Combine(TestTempRoot.Current, "rmcp-workspace-link-" + testId);
        var outsideRoot = Path.Combine(TestTempRoot.Current, "rmcp-workspace-outside-" + testId);
        string? workspaceId = null;
        try
        {
            if (!TestFixtureFileSystem.TryCreateDirectoryLink(logicalRoot, physicalRoot))
            {
                Assert.Inconclusive("Directory links are unavailable in this test environment.");
                return;
            }

            var linkedSolutionPath = Path.Combine(logicalRoot, Path.GetFileName(copiedSolutionPath));
            var status = await WorkspaceManager.LoadAsync(linkedSolutionPath, CancellationToken.None);
            workspaceId = status.WorkspaceId;

            Assert.AreEqual(
                Path.GetFullPath(copiedSolutionPath),
                status.LoadedPath,
                "workspace_load must open and retain the physical solution identity.");

            var solution = WorkspaceManager.GetCurrentSolution(workspaceId);
            var physicalDocument = solution.Projects
                .SelectMany(project => project.Documents)
                .First(document => document.FilePath is not null &&
                                   Path.GetFileName(document.FilePath) == "AnimalService.cs");
            var physicalDocumentPath = physicalDocument.FilePath!;
            var relativeDocumentPath = Path.GetRelativePath(physicalRoot, physicalDocumentPath);
            var linkedDocumentPath = Path.Combine(logicalRoot, relativeDocumentPath);

            var resolvedThroughLink = RoslynMcp.Roslyn.Helpers.SymbolResolver.FindDocument(
                solution,
                linkedDocumentPath);
            Assert.IsNotNull(resolvedThroughLink,
                "A caller may address a document through the linked workspace root before it changes.");
            Assert.AreEqual(
                Path.GetFullPath(physicalDocumentPath),
                resolvedThroughLink.FilePath,
                "The loaded Roslyn document must carry the physical path, not the linked alias.");

            Directory.Delete(logicalRoot);
            var outsideDocumentPath = Path.Combine(outsideRoot, relativeDocumentPath);
            Directory.CreateDirectory(Path.GetDirectoryName(outsideDocumentPath)!);
            const string outsideSentinel = "// outside target must remain unchanged";
            await File.WriteAllTextAsync(outsideDocumentPath, outsideSentinel);
            if (!TestFixtureFileSystem.TryCreateDirectoryLink(logicalRoot, outsideRoot))
            {
                Assert.Inconclusive("Directory link re-pointing is unavailable in this test environment.");
                return;
            }

            var originalText = await physicalDocument.GetTextAsync();
            var updatedText = Microsoft.CodeAnalysis.Text.SourceText.From(
                originalText.ToString() + Environment.NewLine + "// physical write target");
            var updatedSolution = solution.WithDocumentText(physicalDocument.Id, updatedText);

            Assert.IsTrue(WorkspaceManager.TryApplyChanges(workspaceId, updatedSolution));
            Assert.AreEqual(
                outsideSentinel,
                await File.ReadAllTextAsync(outsideDocumentPath),
                "A post-load link swap must not redirect MSBuildWorkspace.TryApplyChanges outside the loaded root.");
            StringAssert.Contains(
                await File.ReadAllTextAsync(physicalDocumentPath),
                "// physical write target",
                "The intended physical document must still receive the edit.");
        }
        finally
        {
            if (workspaceId is not null)
            {
                WorkspaceManager.Close(workspaceId);
            }

            if (Directory.Exists(logicalRoot))
            {
                Directory.Delete(logicalRoot);
            }

            DeleteDirectoryIfExists(outsideRoot);
            DeleteDirectoryIfExists(physicalRoot);
        }
    }
}
