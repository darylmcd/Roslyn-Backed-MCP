using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class OriginalNamespaceScopeTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExternNamespaceScope_IsNotWidenedByGlobalNamespaceMerge(bool replaceOriginalAlias)
    {
        using var workspace = new AdhocWorkspace();
        var owner = ProjectId.CreateNewId();
        var originalTarget = ProjectId.CreateNewId();
        var addedTarget = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(owner);
        const string source = """
            extern alias selected;
            using Kept = selected::Shared;
            namespace User { public class C { public Kept.Foo Existing => null; } }
            """;
        var solution = workspace.CurrentSolution;
        foreach (var entry in new[] { (owner, "Owner"), (originalTarget, "OriginalTarget"), (addedTarget, "AddedTarget") })
            solution = solution.AddProject(ProjectInfo.Create(entry.Item1, VersionStamp.Create(), entry.Item2, entry.Item2,
                LanguageNames.CSharp, compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
                metadataReferences: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]));
        solution = solution.AddDocument(documentId, "Original.cs", SourceText.From(source))
            .AddDocument(DocumentId.CreateNewId(originalTarget), "Foo.cs", SourceText.From("namespace Shared { public class Foo { } }"))
            .AddDocument(DocumentId.CreateNewId(addedTarget), "Foo.cs", SourceText.From("namespace Shared { public class Foo { } }"));
        var originalReference = new ProjectReference(originalTarget, ImmutableArray.Create("selected"));
        solution = solution.AddProjectReference(owner, originalReference);
        var original = await solution.GetProject(owner)!.GetCompilationAsync();
        Assert.IsNotNull(original);
        Assert.IsFalse(original.GetDiagnostics().Any(item => item.Severity == DiagnosticSeverity.Error), string.Join("; ", original.GetDiagnostics()));
        var bindings = await OriginalNameBindings.CaptureAsync(solution, CancellationToken.None);
        var changed = replaceOriginalAlias
            ? bindings.Solution.RemoveProjectReference(owner, originalReference)
                .AddProjectReference(owner, new ProjectReference(addedTarget, ImmutableArray.Create("selected")))
            : bindings.Solution.AddProjectReference(owner, new ProjectReference(addedTarget));
        if (replaceOriginalAlias)
        {
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => bindings.RestoreAsync(changed, CancellationToken.None));
            return;
        }
        var restored = await bindings.RestoreAsync(changed, CancellationToken.None);
        var compilation = await restored.GetProject(owner)!.GetCompilationAsync();
        Assert.IsNotNull(compilation);
        Assert.IsFalse(compilation.GetDiagnostics().Any(item => item.Severity == DiagnosticSeverity.Error), string.Join("; ", compilation.GetDiagnostics()));
        var property = compilation.GetTypeByMetadataName("User.C")!.GetMembers("Existing").OfType<IPropertySymbol>().Single();
        Assert.AreEqual("OriginalTarget", property.Type.ContainingAssembly.Name);
        Assert.AreEqual(source, (await restored.GetDocument(documentId)!.GetTextAsync()).ToString());
    }
}
