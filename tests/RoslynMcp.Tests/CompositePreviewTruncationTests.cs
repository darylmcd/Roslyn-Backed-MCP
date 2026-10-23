using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class CompositePreviewTruncationTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ClassSplit_BoundedPreviewNeverPublishesIncompleteMutationAuthority(bool truncated)
    {
        await WithRootAsync(async root =>
        {
            var path = Path.Combine(root, "Source.cs");
            var output = Path.Combine(root, "Moved.cs");
            var literal = new string('x', truncated ? DiffGenerator.DefaultMaxDiffChars * 2 : 10);
            var source = "public class Source { public string Move => \"" + literal + "\"; public int Keep => 1; }";
            await File.WriteAllTextAsync(path, source);
            var workspace = new AdhocWorkspace();
            Exception? workspacePrimary = null;
            try
            {
                var project = workspace.AddProject("Owner", LanguageNames.CSharp);
                var id = DocumentId.CreateNewId(project.Id);
                var solution = project.Solution.AddDocument(id, "Source.cs", SourceText.From(source), filePath: path);
                var manager = new FailClosedWorkspaceManagerStub
                {
                    GetCurrentSolutionHandler = _ => solution,
                    GetCurrentVersionHandler = _ => 1
                };
                var cache = Path.Combine(root, "cache");
                var backend = new PersistentCompositeStorage(cache, TimeSpan.FromMinutes(5));
                var store = new CountingStore(new CompositePreviewStore(20, TimeSpan.FromMinutes(5), backend));
                var service = new ClassSplitOrchestrator(manager, store);
                if (truncated)
                {
                    // The generated member alone exceeds the unchanged production per-file cap.
                    Assert.IsTrue(PreviewDiffSafety.ContainsTruncation([
                        new(path, DiffGenerator.GenerateUnifiedDiff("", source, path))]));
                    await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => service.PreviewSplitClassAsync(
                        "owned", path, "Source", ["Move"], "Moved.cs", CancellationToken.None));
                    Assert.AreEqual(0, store.Publications);
                    Assert.IsEmpty(Directory.EnumerateFileSystemEntries(cache), "Refusal must not persist redeemable authority.");
                }
                else
                {
                    var preview = await service.PreviewSplitClassAsync("owned", path, "Source", ["Move"], "Moved.cs", CancellationToken.None);
                    Assert.IsFalse(PreviewDiffSafety.ContainsTruncation(preview.Changes));
                    Assert.AreEqual(1, store.Publications);
                    // A separate store can redeem the complete persisted preview without a format change.
                    var reader = new CompositePreviewStore(20, TimeSpan.FromMinutes(5),
                        new PersistentCompositeStorage(cache, TimeSpan.FromMinutes(5)));
                    var entry = reader.Retrieve(preview.PreviewToken);
                    Assert.IsNotNull(entry);
                    Assert.AreEqual(2, entry.Value.Mutations.Count);
                }
                Assert.AreEqual(source, await File.ReadAllTextAsync(path));
                Assert.IsFalse(File.Exists(output));
            }
            catch (Exception failure) { workspacePrimary = failure; throw; }
            finally { OwnedResourceCleanup.Dispose("Composite preview workspace cleanup failed.", workspacePrimary, workspace); }
        });
    }

    [TestMethod]
    [DataRow("aggregate", true)]
    [DataRow("file-lf", true)]
    [DataRow("file-crlf", true)]
    [DataRow("source-text", false)]
    public void SharedSafety_RecognizesOnlyActualRendererMarkers(string kind, bool refused)
    {
        IReadOnlyList<FileChangeDto> changes = kind switch
        {
            "aggregate" => [new(SolutionDiffHelper.TruncatedSentinelFilePath, "omitted")],
            "file-lf" => [new("source.cs", "header\n@@ truncated @@\n")],
            "file-crlf" => [new("source.cs", "header\r\n@@ truncated @@\r\n")],
            _ => [new("source.cs", "+@@ truncated @@\n-@@ truncated @@\n")]
        };
        Assert.AreEqual(refused, PreviewDiffSafety.ContainsTruncation(changes));
        if (refused) Assert.ThrowsExactly<PublicInvalidOperationException>(() => PreviewDiffSafety.RequireComplete(changes));
        else PreviewDiffSafety.RequireComplete(changes);
    }

    [TestMethod]
    public async Task ExtractAndWire_AggregateBoundRefusesBeforePublication()
    {
        await WithRootAsync(async root =>
        {
            var workspace = new AdhocWorkspace();
            Exception? workspacePrimary = null;
            try
            {
                var project = workspace.AddProject("Owner", LanguageNames.CSharp);
                var original = project.Solution;
                var ids = new List<DocumentId>();
                var paths = new List<string>();
                for (var index = 0; index < 40; index++)
                {
                    var path = Path.Combine(root, $"Source{index}.cs");
                    var text = $"public class Source{index} {{ }}";
                    await File.WriteAllTextAsync(path, text);
                    var id = DocumentId.CreateNewId(project.Id);
                    original = original.AddDocument(id, Path.GetFileName(path), SourceText.From(text), filePath: path);
                    ids.Add(id);
                    paths.Add(path);
                }
                var modified = original;
                var individualLength = 0;
                foreach (var (id, index) in ids.Select((id, index) => (id, index)))
                {
                    var members = string.Join("\n", Enumerable.Range(0, 80).Select(n => $"public int Value{n} => {n};"));
                    var changed = $"public class Source{index} {{\n{members}\n}}";
                    var before = (await original.GetDocument(id)!.GetTextAsync()).ToString();
                    var diff = DiffGenerator.GenerateUnifiedDiff(before, changed, paths[index]);
                    Assert.IsFalse(PreviewDiffSafety.ContainsTruncation([new(paths[index], diff)]));
                    individualLength += diff.Length;
                    modified = modified.WithDocumentText(id, SourceText.From(changed));
                }
                Assert.IsTrue(individualLength > SolutionDiffHelper.DefaultMaxTotalChars,
                    "Every file is individually complete; only the real aggregate renderer must clip.");
                var addedPath = Path.Combine(root, "IContract.cs");
                modified = modified.AddDocument(DocumentId.CreateNewId(project.Id), "IContract.cs",
                    SourceText.From("public interface IContract { }"), filePath: addedPath);
                var authorized = new HashSet<string>(paths.Append(addedPath), FileSystemPath.Comparer);
                var manager = new FailClosedWorkspaceManagerStub
                {
                    GetCurrentSolutionHandler = _ => original,
                    GetCurrentVersionHandler = _ => 1,
                    AuthorizePathsHandler = (_, requested, ct) =>
                    {
                        ct.ThrowIfCancellationRequested();
                        Assert.IsTrue(requested.All(authorized.Contains));
                        return Task.CompletedTask;
                    }
                };
                var previewStore = new PreviewStore();
                var cache = Path.Combine(root, "aggregate-cache");
                var store = new CountingStore(new CompositePreviewStore(20, TimeSpan.FromMinutes(5),
                    new PersistentCompositeStorage(cache, TimeSpan.FromMinutes(5))));
                var intermediate = previewStore.Store("owned", original, modified, 1, "fixture extraction", [],
                    PreviewKind.CrossProjectExtractInterface);
                var extraction = new FixedExtraction(new RefactoringPreviewDto(intermediate, "fixture extraction", [], null));
                var service = new ExtractAndWireOrchestrator(manager, store, previewStore, extraction, new NoRegistrations());
                var actualChanges = await SolutionDiffHelper.ComputeChangesAsync(original, modified,
                    SolutionDiffReadContext.ForWorkspace(manager, "owned"), CancellationToken.None);
                Assert.IsTrue(actualChanges.Any(change => change.FilePath == SolutionDiffHelper.TruncatedSentinelFilePath));
                await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => service.PreviewExtractAndWireInterfaceAsync(
                    "owned", paths[0], "Source0", "IContract", "Owner", false, CancellationToken.None));
                Assert.AreEqual(0, store.Publications);
                Assert.IsEmpty(Directory.EnumerateFileSystemEntries(cache));
                Assert.IsNull(previewStore.Retrieve(intermediate));
                Assert.IsFalse(File.Exists(addedPath));
                for (var index = 0; index < paths.Count; index++)
                    Assert.AreEqual($"public class Source{index} {{ }}", await File.ReadAllTextAsync(paths[index]));
            }
            catch (Exception failure) { workspacePrimary = failure; throw; }
            finally { OwnedResourceCleanup.Dispose("Composite preview workspace cleanup failed.", workspacePrimary, workspace); }
        });
    }

    private static async Task WithRootAsync(Func<string, Task> body)
    {
        var root = Path.Combine(TestTempRoot.Current, "composite-review-" + Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            await body(root);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Composite preview fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    private sealed class FixedExtraction(RefactoringPreviewDto preview) : ICrossProjectRefactoringService
    {
        public Task<RefactoringPreviewDto> PreviewExtractInterfaceAsync(string workspaceId, string filePath,
            string typeName, string? interfaceName, string? targetProjectName, CancellationToken ct) => Task.FromResult(preview);
        public Task<RefactoringPreviewDto> PreviewMoveTypeToProjectAsync(string workspaceId, string sourceFilePath,
            string typeName, string targetProjectName, string? targetNamespace, CancellationToken ct, bool preserveNamespace = false) => throw new NotSupportedException();
        public Task<RefactoringPreviewDto> PreviewDependencyInversionAsync(string workspaceId, string filePath,
            string typeName, string? interfaceName, string interfaceProjectName, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class NoRegistrations : IDiRegistrationService
    {
        public Task<IReadOnlyList<DiRegistrationDto>> GetDiRegistrationsAsync(string workspaceId, string? projectFilter,
            CancellationToken ct) => throw new AssertFailedException("Registration updates are disabled in this fixture.");
        public Task<DiRegistrationScanResult> GetDiRegistrationsWithOverridesAsync(string workspaceId,
            string? projectFilter, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class CountingStore(ICompositePreviewStore inner) : ICompositePreviewStore
    {
        internal int Publications { get; private set; }
        public string Store(string workspaceId, int workspaceVersion, string description, IReadOnlyList<CompositeFileMutation> mutations)
        {
            Publications++;
            return inner.Store(workspaceId, workspaceVersion, description, mutations);
        }
        public (string WorkspaceId, int WorkspaceVersion, string Description, IReadOnlyList<CompositeFileMutation> Mutations)? Retrieve(string token) => inner.Retrieve(token);
        public void Invalidate(string token) => inner.Invalidate(token);
        public string? PeekWorkspaceId(string token) => inner.PeekWorkspaceId(token);
    }
}
