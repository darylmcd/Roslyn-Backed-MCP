using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// relocation-using-semantic-identity copy site (BulkRefactoringService.EnsureUsingDirective): the
/// existing-import check compared the syntax <c>Name.ToString()</c> with the symbol-derived
/// namespace, so an interior-trivia spelling of an import already present was not recognised and a
/// second import of the same namespace was added.
/// </summary>
[TestClass]
public sealed class BulkReplaceImportSpellingTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    private static int CountImports(string text, string namespaceKey) =>
        ((CompilationUnitSyntax)CSharpSyntaxTree.ParseText(text).GetRoot()).Usings
        .Count(directive => NamespaceNameIdentity.ImportsNamespace(directive, namespaceKey));

    [TestMethod]
    public async Task DoesNotDuplicateAnImportWrittenInAnEquivalentSpelling()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        await File.WriteAllTextAsync(workspace.GetPath("SampleLib", "BulkOldType.cs"),
            "namespace Bulk.OldNs\n{\n    public class BulkOldType { }\n}\n");
        await File.WriteAllTextAsync(workspace.GetPath("SampleLib", "BulkNewType.cs"),
            "namespace Bulk.NewNs.Sub\n{\n    public class BulkNewType { }\n}\n");
        var consumerPath = workspace.GetPath("SampleLib", "BulkConsumer.cs");
        await File.WriteAllTextAsync(consumerPath,
            "using Bulk.OldNs;\nusing Bulk . NewNs . Sub ;\n\nnamespace Consumers;\n\npublic sealed class BulkConsumer\n{\n    public void M(BulkOldType value) { }\n}\n");
        var workspaceId = await workspace.LoadAsync(CancellationToken.None);

        var preview = await BulkRefactoringService.PreviewBulkReplaceTypeAsync(
            workspaceId, "BulkOldType", "BulkNewType", "parameters", CancellationToken.None);

        var modified = PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;
        var text = (await modified.Projects.SelectMany(p => p.Documents)
            .Single(d => string.Equals(d.FilePath, consumerPath, StringComparison.OrdinalIgnoreCase))
            .GetTextAsync(CancellationToken.None)).ToString();
        StringAssert.Contains(text, "BulkNewType value");
        Assert.AreEqual(1, CountImports(text, "Bulk.NewNs.Sub"), text);
    }
}
