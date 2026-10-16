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
        var sibling = store.Store("sibling", workspace.CurrentSolution, 1, "sibling", false, PreviewKind.FileCreate);
        FileOperationIntegrationTests.AssertNoStoredPreviews(store, "refused");
        var token = store.Store("refused", workspace.CurrentSolution, 1, "unexpected", false, PreviewKind.FileCreate);
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
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    public void Observer_PreservesStoreSnapshotsAndProvenance(int overload)
    {
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("Observer", LanguageNames.CSharp);
        var document = workspace.AddDocument(project.Id, "Original.cs", Microsoft.CodeAnalysis.Text.SourceText.From("class Original {}"));
        var modified = document.Project.Solution.WithDocumentText(document.Id,
            Microsoft.CodeAnalysis.Text.SourceText.From("class Changed {}"));
        var store = new TrackingPreviewStore(new PreviewStore());
        var token = overload switch
        {
            0 => store.Store("observed", modified, 1, "edit"),
            1 => store.Store("observed", modified, 1, "edit", true),
            2 => store.Store("observed", modified, 1, "edit", true, PreviewKind.FileCreate),
            3 => store.Store("observed", modified, 1, "edit", Array.Empty<FileChangeDto>()),
            4 => store.Store("observed", modified, 1, "edit", Array.Empty<FileChangeDto>(), PreviewKind.FileCreate),
            _ => throw new AssertFailedException("Unknown overload")
        };
        CollectionAssert.AreEqual(new[] { token }, store.GetRedeemableTokens("observed").ToArray());
        var snapshot = store.Retrieve(token);
        Assert.IsNotNull(snapshot);
        Assert.AreSame(modified, snapshot.Value.ModifiedSolution);
        Assert.AreEqual(overload is 1 or 2, snapshot.Value.DiffTruncated);
        Assert.AreEqual(overload is 2 or 4 ? PreviewKind.FileCreate : PreviewKind.Unspecified, store.PeekKind(token));
        store.InvalidateAll("other");
        Assert.IsNotNull(store.Retrieve(token));
        store.InvalidateAll("observed");
        FileOperationIntegrationTests.AssertNoStoredPreviews(store, "observed");
    }
}
