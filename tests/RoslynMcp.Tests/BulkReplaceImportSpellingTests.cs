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

    [TestMethod]
    [DataRow("namespace Bulk.NewNs; public class Consumer { public void M(OldType x) { } }", false)]
    [DataRow("namespace Bulk { namespace NewNs { public class Consumer { public void M(OldType x) { } } } }", false)]
    [DataRow("namespace Bulk.NewNs.Child { public class Consumer { public void M(OldType x) { } } }", false)]
    [DataRow("namespace Bulk.NewNs { public class Consumer { public void M(OldType x) { } } } namespace Other { public class Consumer { public void M(OldType x) { } } }", true)]
    [DataRow("namespace Other { using Bulk.NewNs; public class Consumer { public void M(OldType x) { } } }", false)]
    public async Task AddsImportOnlyWhenARewrittenOccurrenceNeedsIt(string source, bool needsImport)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        await File.WriteAllTextAsync(workspace.GetPath("SampleLib", "ScopeOld.cs"),
            "namespace Bulk.OldNs { public class OldType { } }");
        await File.WriteAllTextAsync(workspace.GetPath("SampleLib", "ScopeNew.cs"),
            "namespace Bulk.NewNs { public class NewType { } }");
        var path = workspace.GetPath("SampleLib", "ScopeConsumer.cs");
        await File.WriteAllTextAsync(path, "using Bulk.OldNs; " + source);
        var id = await workspace.LoadAsync(CancellationToken.None);
        var preview = await BulkRefactoringService.PreviewBulkReplaceTypeAsync(
            id, "OldType", "NewType", "parameters", CancellationToken.None);
        var modified = PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;
        var document = modified.Projects.SelectMany(p => p.Documents)
            .Single(d => string.Equals(d.FilePath, path, StringComparison.OrdinalIgnoreCase));
        var text = (await document.GetTextAsync()).ToString();
        Assert.AreEqual(needsImport ? 1 : 0, CountImports(text, "Bulk.NewNs"), text);
        StringAssert.Contains(text, "NewType x");
        var compilation = await document.Project.GetCompilationAsync();
        Assert.IsNotNull(compilation);
        var errors = compilation.GetDiagnostics().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToArray();
        Assert.AreEqual(0, errors.Length, string.Join(Environment.NewLine, errors.Select(d => d.ToString())));
    }

    [TestMethod]
    [DataRow("Other", "Bulk.NewNs", true)]
    [DataRow("Bulk", "NewNs", false)]
    public async Task LocalImportUsesBoundNamespaceRatherThanWrittenSpelling(string enclosingNamespace, string importName, bool needsImport)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        await File.WriteAllTextAsync(workspace.GetPath("SampleLib", "BindingTypes.cs"),
            "namespace Bulk.OldNs { public class OldType { } } namespace Bulk.NewNs { public class NewType { } } namespace Other.Bulk.NewNs { public class Distractor { } }");
        var path = workspace.GetPath("SampleLib", "BindingConsumer.cs");
        await File.WriteAllTextAsync(path,
            $"using Bulk.OldNs; namespace {enclosingNamespace} {{ using {importName}; public class Consumer {{ public void M(OldType x) {{ }} }} }}");
        var id = await workspace.LoadAsync(CancellationToken.None);
        var original = WorkspaceManager.GetCurrentSolution(id).Projects.SelectMany(p => p.Documents)
            .Single(d => string.Equals(d.FilePath, path, StringComparison.OrdinalIgnoreCase));
        var originalCompilation = await original.Project.GetCompilationAsync();
        Assert.IsNotNull(originalCompilation);
        Assert.AreEqual(0, originalCompilation.GetDiagnostics().Count(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var preview = await BulkRefactoringService.PreviewBulkReplaceTypeAsync(
            id, "OldType", "NewType", "parameters", CancellationToken.None);
        var modified = PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;
        var document = modified.Projects.SelectMany(p => p.Documents)
            .Single(d => string.Equals(d.FilePath, path, StringComparison.OrdinalIgnoreCase));
        var text = (await document.GetTextAsync()).ToString();
        StringAssert.Contains(text, "NewType x");
        var emitted = document.WithSyntaxRoot(CSharpSyntaxTree.ParseText(text).GetRoot());
        var compilation = await emitted.Project.GetCompilationAsync();
        Assert.IsNotNull(compilation);
        var errors = compilation.GetDiagnostics().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToArray();
        Assert.AreEqual(0, errors.Length, string.Join(Environment.NewLine, errors.Select(d => d.ToString())));
        Assert.AreEqual(needsImport ? 1 : 0, CountImports(text, "Bulk.NewNs"), text);
    }
}
