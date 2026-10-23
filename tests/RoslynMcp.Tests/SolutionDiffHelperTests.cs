using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public class SolutionDiffHelperTests
{
    private sealed class ObservedPhysicalTextLoader(string path) : TextLoader
    {
        private readonly FileTextLoader _inner = new(path, null);
        private int _readAttempts;
        internal int ReadAttempts => Volatile.Read(ref _readAttempts);

        public override Task<TextAndVersion> LoadTextAndVersionAsync(LoadTextOptions options, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _readAttempts);
            return _inner.LoadTextAndVersionAsync(options, cancellationToken);
        }
    }

    private static (Solution solution, ProjectId projectId) CreateEmptySolution()
    {
        var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var solution = workspace.CurrentSolution
            .AddProject(projectId, "TestProject", "TestProject", LanguageNames.CSharp);
        return (solution, projectId);
    }

    // These callers construct every document from SourceText above; materialize that
    // owned in-memory input explicitly. Loader-safety tests never use this helper.
    private static async Task<IReadOnlyList<FileChangeDto>> ComputeOwnedSnapshotChangesAsync(Solution original, Solution modified)
    {
        foreach (var document in original.Projects.Concat(modified.Projects).SelectMany(project => project.Documents))
            await document.GetTextAsync();
        return await SolutionDiffHelper.ComputeChangesAsync(original, modified, SolutionDiffReadContext.SnapshotOnly, CancellationToken.None);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task SnapshotDiff_UnchangedLinkedPeer_MustAlreadyBeMaterialized(bool rootedKey, bool lazyPeer)
    {
        var directory = Path.Combine(TestTempRoot.Current, "linked-diff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "Peer.cs");
        try
        {
            await File.WriteAllTextAsync(path, "must not load physical text");
            using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var (solution, projectId) = CreateEmptySolution();
            var first = DocumentId.CreateNewId(projectId);
            var peer = DocumentId.CreateNewId(projectId);
            const string source = "class Before { }";
            var loader = new ObservedPhysicalTextLoader(path);
            var original = solution.AddDocument(first, path, SourceText.From(source), filePath: rootedKey ? path : null);
            original = lazyPeer
                ? original.AddDocument(DocumentInfo.Create(peer, path, loader: loader, filePath: rootedKey ? path : null))
                : original.AddDocument(peer, path, SourceText.From(source), filePath: rootedKey ? path : null);
            await original.GetDocument(first)!.GetTextAsync();
            if (!lazyPeer) await original.GetDocument(peer)!.GetTextAsync();
            var modified = original.WithDocumentText(first, SourceText.From(source));
            await modified.GetDocument(first)!.GetTextAsync();
            Assert.AreEqual(0, loader.ReadAttempts, "Fixture setup must leave the unchanged peer lazy.");
            if (lazyPeer)
                await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => SolutionDiffHelper.ComputeChangesAsync(original, modified,
                    SolutionDiffReadContext.SnapshotOnly, CancellationToken.None));
            else
                Assert.HasCount(0, await SolutionDiffHelper.ComputeChangesAsync(original, modified, SolutionDiffReadContext.SnapshotOnly, CancellationToken.None));
            Assert.AreEqual(0, loader.ReadAttempts, "A refusal after invoking the physical loader is too late.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task PhysicalDiff_AuthorizesCompleteInventoryBeforeLoadingAnyDocument()
    {
        var root = Path.Combine(TestTempRoot.Current, "diff-policy-" + Guid.NewGuid().ToString("N"));
        var allowed = Path.Combine(root, "Allowed.cs");
        var outside = Path.Combine(root, "Outside", "Peer.cs");
        var (solution, projectId) = CreateEmptySolution();
        var first = DocumentId.CreateNewId(projectId);
        var second = DocumentId.CreateNewId(projectId);
        var original = solution.AddDocument(DocumentInfo.Create(first, "Allowed.cs", loader: new FileTextLoader(allowed, null), filePath: allowed))
            .AddDocument(DocumentInfo.Create(second, "Peer.cs", loader: new FileTextLoader(outside, null), filePath: outside));
        var modified = original.WithDocumentText(first, SourceText.From("class Changed { }"))
            .WithDocumentText(second, SourceText.From("class ChangedPeer { }"));
        var authorizations = 0;
        var manager = new FailClosedWorkspaceManagerStub
        {
            AuthorizePathsHandler = (workspaceId, paths, ct) =>
            {
                Assert.AreEqual("diff-workspace", workspaceId);
                CollectionAssert.AreEquivalent(new[] { allowed, outside }, paths.ToArray());
                authorizations++;
                throw new PublicArgumentException("Peer is outside the current request roots.", nameof(paths));
            }
        };
        await Assert.ThrowsExactlyAsync<PublicArgumentException>(() => SolutionDiffHelper.ComputeChangesAsync(original, modified,
            SolutionDiffReadContext.ForWorkspace(manager, "diff-workspace"), CancellationToken.None));
        Assert.AreEqual(1, authorizations);
    }

    [TestMethod]
    public async Task PhysicalDiff_ReauthorizesImmediatelyBeforeRawByteRead()
    {
        var path = Path.Combine(TestTempRoot.Current, "diff-read-" + Guid.NewGuid().ToString("N"), "Source.cs");
        var (solution, projectId) = CreateEmptySolution();
        var id = DocumentId.CreateNewId(projectId);
        var original = solution.AddDocument(id, "Source.cs", SourceText.From("class Before { }"), filePath: path);
        var modified = original.WithDocumentText(id, SourceText.From("class After { }"));
        var authorizations = 0;
        var manager = new FailClosedWorkspaceManagerStub
        {
            AuthorizePathsHandler = (workspaceId, paths, ct) =>
            {
                CollectionAssert.AreEqual(new[] { path }, paths.ToArray());
                if (++authorizations == 2) throw new PublicArgumentException("The current request roots narrowed.", nameof(paths));
                return Task.CompletedTask;
            }
        };
        await Assert.ThrowsExactlyAsync<PublicArgumentException>(() => SolutionDiffHelper.ComputeChangesAsync(original, modified,
            SolutionDiffReadContext.ForWorkspace(manager, "diff-workspace"), CancellationToken.None));
        Assert.AreEqual(2, authorizations);
    }

    [TestMethod]
    public async Task SnapshotDiff_RefusesLazyPhysicalLoaderWithoutReadingIt()
    {
        var path = Path.Combine(TestTempRoot.Current, "missing-diff-" + Guid.NewGuid().ToString("N"), "Source.cs");
        var (solution, projectId) = CreateEmptySolution();
        var id = DocumentId.CreateNewId(projectId);
        var original = solution.AddDocument(DocumentInfo.Create(id, "Source.cs", loader: new FileTextLoader(path, null), filePath: path));
        var modified = original.WithDocumentText(id, SourceText.From("class After { }"));
        await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => SolutionDiffHelper.ComputeChangesAsync(original, modified,
            SolutionDiffReadContext.SnapshotOnly, CancellationToken.None));
    }

    [TestMethod]
    public async Task SnapshotDiff_DoesNotReadPhysicalFileBehindDocumentName()
    {
        var directory = Path.Combine(TestTempRoot.Current, "snapshot-diff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "Snapshot.cs");
        try
        {
            await File.WriteAllTextAsync(path, "physical content must never be read");
            using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var (solution, projectId) = CreateEmptySolution();
            var id = DocumentId.CreateNewId(projectId);
            var original = solution.AddDocument(id, path, SourceText.From("class Before { }"));
            var modified = original.WithDocumentText(id, SourceText.From("class After { }"));
            var changes = await ComputeOwnedSnapshotChangesAsync(original, modified);
            Assert.HasCount(1, changes);
            StringAssert.Contains(changes[0].UnifiedDiff, "class After");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task ChangedDocument_ReturnsOneChangeWithCorrectFilePath()
    {
        var (solution, projectId) = CreateEmptySolution();
        var docId = DocumentId.CreateNewId(projectId);
        var oldSolution = solution
            .AddDocument(docId, "Test.cs", SourceText.From("class A { }"));
        var newSolution = oldSolution
            .WithDocumentText(docId, SourceText.From("class B { }"));

        var changes = await ComputeOwnedSnapshotChangesAsync(oldSolution, newSolution);

        Assert.AreEqual(1, changes.Count);
        StringAssert.EndsWith(changes[0].FilePath, "Test.cs");
        Assert.IsFalse(string.IsNullOrWhiteSpace(changes[0].UnifiedDiff));
    }

    [TestMethod]
    public async Task AddedDocument_ReturnsChange()
    {
        var (solution, projectId) = CreateEmptySolution();
        var oldSolution = solution;
        var docId = DocumentId.CreateNewId(projectId);
        var newSolution = solution
            .AddDocument(docId, "New.cs", SourceText.From("class New { }"));

        var changes = await ComputeOwnedSnapshotChangesAsync(oldSolution, newSolution);

        Assert.AreEqual(1, changes.Count);
        StringAssert.EndsWith(changes[0].FilePath, "New.cs");
        Assert.IsFalse(string.IsNullOrWhiteSpace(changes[0].UnifiedDiff));
    }

    [TestMethod]
    public async Task RemovedDocument_ReturnsChange()
    {
        var (solution, projectId) = CreateEmptySolution();
        var docId = DocumentId.CreateNewId(projectId);
        var oldSolution = solution
            .AddDocument(docId, "Remove.cs", SourceText.From("class Remove { }"));
        var newSolution = oldSolution.RemoveDocument(docId);

        var changes = await ComputeOwnedSnapshotChangesAsync(oldSolution, newSolution);

        Assert.AreEqual(1, changes.Count);
        StringAssert.EndsWith(changes[0].FilePath, "Remove.cs");
        Assert.IsFalse(string.IsNullOrWhiteSpace(changes[0].UnifiedDiff));
    }

    [TestMethod]
    public async Task NoChanges_ReturnsEmptyList()
    {
        var (solution, _) = CreateEmptySolution();

        var changes = await ComputeOwnedSnapshotChangesAsync(solution, solution);

        Assert.AreEqual(0, changes.Count);
    }

    [TestMethod]
    public async Task IdenticalText_ReturnsEmptyList()
    {
        // Use the same document ID but replace with an identical SourceText object.
        // Roslyn reports this as a "changed" document, but the text is identical,
        // so SolutionDiffHelper should skip it.
        var (solution, projectId) = CreateEmptySolution();
        var docId = DocumentId.CreateNewId(projectId);
        const string content = "class Same { }";

        var oldSolution = solution
            .AddDocument(docId, "Same.cs", SourceText.From(content));
        var newSolution = oldSolution
            .WithDocumentText(docId, SourceText.From(content));

        var changes = await ComputeOwnedSnapshotChangesAsync(oldSolution, newSolution);

        Assert.AreEqual(0, changes.Count);
    }

    [TestMethod]
    public async Task Truncation_LastEntryHasTruncatedFilePath()
    {
        var (solution, projectId) = CreateEmptySolution();
        var oldSolution = solution;
        var newSolution = solution;

        // Generate a large block of text so each document diff is substantial.
        // DefaultMaxTotalChars is 64 * 1024 = 65536 chars.
        // Create enough documents with large content to exceed the limit.
        var largeContent = new string('x', 4096);
        for (int i = 0; i < 30; i++)
        {
            var docId = DocumentId.CreateNewId(projectId);
            newSolution = newSolution
                .AddDocument(docId, $"Big{i}.cs", SourceText.From($"// file {i}\n{largeContent}"));
        }

        var changes = await ComputeOwnedSnapshotChangesAsync(oldSolution, newSolution);

        Assert.IsTrue(changes.Count >= 2, "Expected at least 2 entries (some diffs + truncation marker).");
        var last = changes[changes.Count - 1];
        Assert.AreEqual("<truncated>", last.FilePath);
    }
}
