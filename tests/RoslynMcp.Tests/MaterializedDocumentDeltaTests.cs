using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class MaterializedDocumentDeltaTests
{
    [TestMethod]
    public async Task BindingOwner_MaterializesItsAnnotatedOutputBeforePhysicalDiff()
    {
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("Owner", LanguageNames.CSharp);
        var id = DocumentId.CreateNewId(project.Id);
        var path = Path.Combine(TestTempRoot.Current, "outside-policy-" + Guid.NewGuid().ToString("N"), "Source.cs");
        var original = project.Solution.AddMetadataReference(project.Id, MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
            .AddDocument(id, "Source.cs", SourceText.From("public class Original { public string Name => string.Empty; }"), filePath: path);
        var bindings = await OriginalNameBindings.CaptureAsync(original, CancellationToken.None);
        Assert.IsTrue(bindings.Solution.GetChanges(original).GetProjectChanges().SelectMany(change => change.GetChangedDocuments()).Contains(id));
        var restored = await bindings.RestoreAsync(bindings.Solution, CancellationToken.None);
        Assert.IsTrue(restored.GetDocument(id)!.TryGetText(out var text), "The semantic owner must materialize its annotated output.");
        Assert.AreEqual("public class Original { public string Name => string.Empty; }", text.ToString());
        var observedPaths = new List<string>();
        var manager = new FailClosedWorkspaceManagerStub
        {
            AuthorizePathsHandler = (_, paths, _) =>
            {
                observedPaths.AddRange(paths);
                if (paths.Count != 0) throw new PublicArgumentException("Outside owned fixture policy.", nameof(paths));
                return Task.CompletedTask;
            }
        };
        var changes = await SolutionDiffHelper.ComputeChangesAsync(original, restored,
            SolutionDiffReadContext.ForWorkspace(manager, "owned"), CancellationToken.None);
        Assert.IsEmpty(changes);
        Assert.IsEmpty(observedPaths);
        Assert.IsFalse(File.Exists(path));
    }

    [TestMethod]
    [DataRow("annotation", false)]
    [DataRow("text", true)]
    [DataRow("relocation", true)]
    [DataRow("name", true)]
    [DataRow("folders", true)]
    [DataRow("source-kind", true)]
    [DataRow("document-kind", true)]
    [DataRow("encoding", true)]
    [DataRow("checksum", true)]
    [DataRow("linked-conflict", true)]
    public async Task PhysicalInventory_ExemptsOnlyMaterializedExactNoOp(string scenario, bool denied)
    {
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("Owner", LanguageNames.CSharp);
        var id = DocumentId.CreateNewId(project.Id);
        var path = Path.Combine(TestTempRoot.Current, "outside-policy-" + Guid.NewGuid().ToString("N"), "Source.cs");
        var source = SourceText.From("class Source { }", Encoding.UTF8, SourceHashAlgorithm.Sha1);
        var original = project.Solution.AddDocument(id, "Source.cs", source, filePath: path);
        var root = await original.GetDocument(id)!.GetSyntaxRootAsync();
        var modified = original.WithDocumentSyntaxRoot(id, root!.WithAdditionalAnnotations(new SyntaxAnnotation()));
        modified = scenario switch
        {
            "text" => modified.WithDocumentText(id, SourceText.From("class Changed { }", Encoding.UTF8, SourceHashAlgorithm.Sha1)),
            "relocation" => modified.WithDocumentFilePath(id, path + ".moved"),
            "name" => modified.WithDocumentName(id, "Renamed.cs"),
            "folders" => modified.WithDocumentFolders(id, ["NewFolder"]),
            "source-kind" => modified.WithDocumentSourceCodeKind(id, SourceCodeKind.Script),
            "document-kind" => modified.RemoveDocument(id).AddAdditionalDocument(id, "Source.cs", source, filePath: path),
            "encoding" => modified.WithDocumentText(id, SourceText.From(source.ToString(), Encoding.Unicode, SourceHashAlgorithm.Sha1)),
            "checksum" => modified.WithDocumentText(id, SourceText.From(source.ToString(), Encoding.UTF8, SourceHashAlgorithm.Sha256)),
            _ => modified
        };
        if (scenario == "linked-conflict")
        {
            var peer = DocumentId.CreateNewId(project.Id);
            original = original.AddDocument(peer, "Peer.cs", source, filePath: path);
            modified = modified.AddDocument(peer, "Peer.cs", SourceText.From("class Conflicting { }", Encoding.UTF8), filePath: path);
        }
        // The owner materializes its in-memory syntax. The shared inventory must never do so.
        foreach (var document in original.Projects.Concat(modified.Projects).SelectMany(p => p.Documents))
            await document.GetTextAsync();
        if (scenario == "annotation")
        {
            Assert.IsTrue(original.GetDocument(id)!.TryGetText(out var originalText));
            Assert.IsTrue(modified.GetDocument(id)!.TryGetText(out var modifiedText));
            Assert.AreEqual(originalText.Encoding, modifiedText.Encoding);
            Assert.AreEqual(originalText.ChecksumAlgorithm, modifiedText.ChecksumAlgorithm);
            Assert.IsTrue(originalText.ContentEquals(modifiedText));
        }
        var observedPaths = new List<string>();
        var manager = new FailClosedWorkspaceManagerStub
        {
            AuthorizePathsHandler = (_, paths, _) =>
            {
                observedPaths.AddRange(paths);
                if (paths.Count != 0) throw new PublicArgumentException("Outside owned fixture policy.", nameof(paths));
                return Task.CompletedTask;
            }
        };
        var context = SolutionDiffReadContext.ForWorkspace(manager, "owned");
        if (denied)
        {
            await Assert.ThrowsExactlyAsync<PublicArgumentException>(() => context.PrepareAsync(original, modified, CancellationToken.None));
            Assert.IsTrue(observedPaths.Contains(path));
        }
        else
        {
            var changes = await SolutionDiffHelper.ComputeChangesAsync(original, modified, context, CancellationToken.None);
            Assert.IsEmpty(changes);
            Assert.IsEmpty(observedPaths);
            Assert.IsEmpty(SolutionFileMutationPlanner.ChangedPaths(original, modified));
        }
        Assert.IsFalse(File.Exists(path));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UnknownOrLinkedLazyText_IsAuthorizedBeforeAnyLoaderInvocation(bool linked)
    {
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("Owner", LanguageNames.CSharp);
        var path = Path.Combine(TestTempRoot.Current, "never-load-" + Guid.NewGuid().ToString("N"), "Source.cs");
        var id = DocumentId.CreateNewId(project.Id);
        var loader = new RefusingLoader();
        var original = linked
            ? project.Solution.AddDocument(id, "Source.cs", SourceText.From("class Source { }"), filePath: path)
            : project.Solution.AddDocument(DocumentInfo.Create(id, "Source.cs", loader: loader, filePath: path));
        if (linked)
            original = original.AddDocument(DocumentInfo.Create(DocumentId.CreateNewId(project.Id), "Peer.cs", loader: loader, filePath: path));
        var modified = original.WithDocumentText(id, SourceText.From("class Source { }"));
        await modified.GetDocument(id)!.GetTextAsync();
        if (linked) await original.GetDocument(id)!.GetTextAsync();
        var manager = new FailClosedWorkspaceManagerStub
        {
            AuthorizePathsHandler = (_, paths, _) =>
            {
                CollectionAssert.Contains(paths.ToArray(), path);
                throw new PublicArgumentException("Outside owned fixture policy.", nameof(paths));
            }
        };
        await Assert.ThrowsExactlyAsync<PublicArgumentException>(() => SolutionDiffReadContext.ForWorkspace(manager, "owned")
            .PrepareAsync(original, modified, CancellationToken.None));
        Assert.AreEqual(0, loader.Attempts);
    }

    private sealed class RefusingLoader : TextLoader
    {
        internal int Attempts { get; private set; }
        public override Task<TextAndVersion> LoadTextAndVersionAsync(LoadTextOptions options, CancellationToken cancellationToken)
        {
            Attempts++;
            throw new AssertFailedException("Inventory must not invoke a lazy physical loader.");
        }
    }
}
