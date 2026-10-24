using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class WorkspaceForkDocumentEncodingTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    [DataRow(false, "utf16-bom")]
    [DataRow(true, "utf16-bom")]
    [DataRow(false, "utf8-bom")]
    [DataRow(true, "utf8-bom")]
    [DataRow(false, "utf8-no-bom")]
    [DataRow(true, "utf8-no-bom")]
    [DataRow(false, "default")]
    [DataRow(true, "default")]
    public async Task ApplyAsync_PreservesReviewedDocumentBytesAndSourceAuthority(bool added, string encodingName)
    {
        await using var source = await CreateIsolatedWorkspaceAsync();
        var original = WorkspaceManager.GetCurrentSolution(source.WorkspaceId);
        var status = await WorkspaceManager.GetStatusAsync(source.WorkspaceId);
        var version = WorkspaceManager.GetCurrentVersion(source.WorkspaceId);
        var inventory = original.Projects.SelectMany(project => project.Documents)
            .Select(document => (document.Id, document.FilePath)).ToArray();
        var sourceBytes = new Dictionary<string, byte[]>();
        foreach (var (_, path) in inventory)
        {
            Assert.IsNotNull(path);
            sourceBytes.Add(path, await File.ReadAllBytesAsync(path));
        }

        var dog = original.Projects.SelectMany(project => project.Documents)
            .Single(document => document.Name == "Dog.cs");
        var targetPath = added ? source.GetPath("SampleLib", "EncodingProbe.cs") : dog.FilePath!;
        var body = added
            ? "namespace SampleLib; public class EncodingProbe { public string Text => \"caf\u00e9 \u03bb \u65e5\"; }\n"
            : (await dog.GetTextAsync()).ToString().Replace("Woof", "caf\u00e9 \u03bb \u65e5", StringComparison.Ordinal);
        Encoding? encoding = encodingName switch
        {
            "utf16-bom" => new UnicodeEncoding(false, true),
            "utf8-bom" => new UTF8Encoding(true),
            "utf8-no-bom" => new UTF8Encoding(false),
            "default" => null,
            _ => throw new AssertFailedException("Unknown test encoding.")
        };
        var text = SourceText.From(body, encoding);
        var modified = added
            ? original.AddDocument(DocumentId.CreateNewId(dog.Project.Id), "EncodingProbe.cs", text, filePath: targetPath)
            : original.WithDocumentText(dog.Id, text);
        var store = new PreviewStore();
        var token = store.Store(source.WorkspaceId, original, modified, version,
            "fork encoding preview", diffTruncated: false, PreviewKind.Unspecified);
        var entry = store.Retrieve(token);
        Assert.IsNotNull(entry);
        var validation = new WorkspaceValidationService(CompileCheckService, DiagnosticService,
            TestDiscoveryService, TestRunnerService, WorkspaceManager, ChangeTracker);
        var service = new WorkspaceForkApplyService(WorkspaceManager, WorkspaceExecutionGate, store,
            validation, TestRunnerService, new DotnetCommandRunner(), Helpers.ForkStorageFixtureBoundary.ValidateAsync, NullLogger<WorkspaceForkApplyService>.Instance);

        var result = await service.ApplyAsync(source.WorkspaceId, token, "keep", false, null, null, CancellationToken.None);
        try
        {
            Assert.IsTrue(result.Success, "The real fork restore, load and validation must succeed.");
            Assert.IsTrue(result.Retained);
            Assert.IsNotNull(result.ForkWorkspaceId);
            CollectionAssert.AreEqual(Array.Empty<string>(), result.CleanupWarnings.ToArray());

            var current = WorkspaceManager.GetCurrentSolution(source.WorkspaceId);
            Assert.AreSame(original, current, "Fork replay must preserve the source solution identity.");
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(source.WorkspaceId));
            Assert.AreEqual(status.SnapshotToken, (await WorkspaceManager.GetStatusAsync(source.WorkspaceId)).SnapshotToken);
            CollectionAssert.AreEqual(inventory, current.Projects.SelectMany(project => project.Documents)
                .Select(document => (document.Id, document.FilePath)).ToArray());
            foreach (var (path, bytes) in sourceBytes)
                CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(path), "Source bytes changed: " + path);
            if (added)
                Assert.IsFalse(File.Exists(targetPath), "Added preview documents must remain absent from the source.");

            var retainedEntry = store.Retrieve(token);
            Assert.IsNotNull(retainedEntry, "Fork replay must not consume the preview.");
            Assert.AreEqual(entry, retainedEntry, "Fork replay must preserve preview metadata.");
            Assert.AreEqual(source.WorkspaceId, retainedEntry.Value.WorkspaceId);
            Assert.AreEqual(version, retainedEntry.Value.WorkspaceVersion);
            Assert.AreSame(original, retainedEntry.Value.OriginalSolution);
            Assert.AreSame(modified, retainedEntry.Value.ModifiedSolution);

            var forkFile = Path.Combine(result.ForkPath, Path.GetRelativePath(source.RootPath, targetPath));
            CollectionAssert.AreEqual(new[] { forkFile }, result.AppliedFiles.ToArray());
            var expectedEncoding = encoding ?? new UTF8Encoding(false);
            var expectedBytes = expectedEncoding.GetPreamble().Concat(expectedEncoding.GetBytes(body)).ToArray();
            CollectionAssert.AreEqual(expectedBytes, await File.ReadAllBytesAsync(forkFile),
                "Fork bytes must preserve the reviewed encoding, preamble and non-ASCII body.");
        }
        finally
        {
            if (result.ForkWorkspaceId is not null)
                WorkspaceManager.Close(result.ForkWorkspaceId);
            DeleteDirectoryIfExists(result.ForkPath);
        }
    }
}
