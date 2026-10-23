using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class PreviewStoreAssertionTests
{
    [TestMethod]
    public void RefusalAssertion_UsesContractAndRejectsRedeemableTokens()
    {
        using var workspace = new AdhocWorkspace();
        var store = new TrackingPreviewStore(new PreviewStore());
        FileOperationIntegrationTests.AssertNoStoredPreviews(store, "refused");
        var sibling = store.Store("sibling", workspace.CurrentSolution, workspace.CurrentSolution, 1, "sibling", [], PreviewKind.FileCreate);
        FileOperationIntegrationTests.AssertNoStoredPreviews(store, "refused");
        var token = store.Store("refused", workspace.CurrentSolution, workspace.CurrentSolution, 1, "unexpected", [], PreviewKind.FileCreate);
        Assert.ThrowsExactly<AssertFailedException>(() => FileOperationIntegrationTests.AssertNoStoredPreviews(store, "refused"));
        Assert.IsNotNull(store.Retrieve(token), "The assertion must not consume a token.");
        Assert.AreEqual(PreviewKind.FileCreate, store.PeekKind(token));
        store.Invalidate(token);
        FileOperationIntegrationTests.AssertNoStoredPreviews(store, "refused");
        Assert.IsNotNull(store.Retrieve(sibling), "Workspace-scoped assertions must preserve sibling tokens.");
        store.InvalidateOnVersionBump("sibling", 3);
        FileOperationIntegrationTests.AssertNoStoredPreviews(store, "sibling");
    }

    [TestMethod]
    [DataRow(false, PreviewKind.Unspecified)]
    [DataRow(true, PreviewKind.Unspecified)]
    [DataRow(false, PreviewKind.FileCreate)]
    [DataRow(true, PreviewKind.FileCreate)]
    public void Observer_PreservesStoreSnapshotsAndProvenance(bool truncated, PreviewKind kind)
    {
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("Observer", LanguageNames.CSharp);
        var document = workspace.AddDocument(project.Id, "Original.cs", Microsoft.CodeAnalysis.Text.SourceText.From("class Original {}"));
        var original = document.Project.Solution;
        var modified = original.WithDocumentText(document.Id,
            Microsoft.CodeAnalysis.Text.SourceText.From("class Changed {}"));
        var store = new TrackingPreviewStore(new PreviewStore());
        IReadOnlyList<FileChangeDto> changes = truncated
            ? [new FileChangeDto("Original.cs", "@@ truncated @@")]
            : [new FileChangeDto("Original.cs", "-class Original {}\n+class Changed {}")];
        var token = store.Store("observed", original, modified, 1, "edit", changes, kind);
        CollectionAssert.AreEqual(new[] { token }, store.GetRedeemableTokens("observed").ToArray());
        var snapshot = store.Retrieve(token);
        Assert.IsNotNull(snapshot);
        Assert.AreSame(original, snapshot.Value.OriginalSolution);
        Assert.AreSame(modified, snapshot.Value.ModifiedSolution);
        Assert.AreEqual(truncated, snapshot.Value.DiffTruncated);
        Assert.AreEqual(kind, store.PeekKind(token));
        store.InvalidateAll("other");
        Assert.IsNotNull(store.Retrieve(token));
        store.InvalidateAll("observed");
        FileOperationIntegrationTests.AssertNoStoredPreviews(store, "observed");
    }
}
