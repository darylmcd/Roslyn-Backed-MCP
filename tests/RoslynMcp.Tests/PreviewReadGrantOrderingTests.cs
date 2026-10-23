using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class PreviewReadGrantOrderingTests
{
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task PhysicalPreview_AuthorizesBeforeAliasInspection_AndStillRejectsAuthorizedAlias(bool rawBytes, bool grant)
    {
        var lexical = Path.Combine(TestTempRoot.Current, nameof(PreviewReadGrantOrderingTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(lexical);
        var root = PhysicalPathResolver.Resolve(lexical);
        Exception? primary = null;
        try
        {
            var target = Path.Combine(root, "target");
            Directory.CreateDirectory(target);
            var physical = Path.Combine(target, "Source.cs");
            await File.WriteAllTextAsync(physical, "class Before { }");
            var alias = Path.Combine(root, "alias");
            if (!TestFixtureFileSystem.TryCreateDirectoryLink(alias, target))
                Assert.Inconclusive("Directory links are unavailable in this environment.");
            var path = Path.Combine(alias, "Source.cs");
            var denial = new PublicArgumentException("Exact current preview grant denied.", "paths");
            var calls = 0;
            var manager = new FailClosedWorkspaceManagerStub
            {
                AuthorizePathsHandler = (workspaceId, paths, ct) =>
                {
                    Assert.AreEqual("preview-order", workspaceId);
                    CollectionAssert.AreEqual(new[] { path }, paths.ToArray());
                    calls++;
                    if (!grant) throw denial;
                    return Task.CompletedTask;
                }
            };
            var context = SolutionDiffReadContext.ForWorkspace(manager, "preview-order");
            using var workspace = new AdhocWorkspace();
            var projectId = ProjectId.CreateNewId();
            var documentId = DocumentId.CreateNewId(projectId);
            var original = workspace.CurrentSolution.AddProject(projectId, "Order", "Order", LanguageNames.CSharp)
                .AddDocument(DocumentInfo.Create(documentId, "Source.cs", loader: new FileTextLoader(path, null), filePath: path));
            var modified = original.WithDocumentText(documentId, SourceText.From("class After { }"));
            async Task InvokeAsync()
            {
                if (rawBytes) await context.ReadBytesAsync(path, CancellationToken.None);
                else await context.PrepareAsync(original, modified, CancellationToken.None);
            }
            if (grant) await Assert.ThrowsExactlyAsync<InvalidOperationException>(InvokeAsync);
            else Assert.AreSame(denial, await Assert.ThrowsExactlyAsync<PublicArgumentException>(InvokeAsync));
            Assert.AreEqual(1, calls, "A live grant must precede every filesystem alias inspection.");
            Assert.AreEqual("class Before { }", await File.ReadAllTextAsync(physical));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Preview grant fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }
}
