using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ConstructorDependencyIdentityTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExactSourceDeclaration_MatchesCompilerViewsWithoutMatchingOtherAssembly(bool consumerFirst)
    {
        using var workspace = new AdhocWorkspace();
        var sourceId = ProjectId.CreateNewId();
        var consumerId = ProjectId.CreateNewId();
        var otherId = ProjectId.CreateNewId();
        var declarationId = DocumentId.CreateNewId(sourceId);
        var solution = workspace.CurrentSolution;
        var order = consumerFirst ? new[] { (consumerId, "Consumer"), (sourceId, "Source"), (otherId, "Other") }
            : new[] { (sourceId, "Source"), (consumerId, "Consumer"), (otherId, "Other") };
        foreach (var (id, name) in order)
            solution = solution.AddProject(ProjectInfo.Create(id, VersionStamp.Create(), name, name, LanguageNames.CSharp,
                parseOptions: new CSharpParseOptions(LanguageVersion.Latest),
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
                metadataReferences: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]));
        solution = solution.AddDocument(declarationId, "Target.cs", SourceText.From("namespace Model { public class Target { } }"))
            .AddDocument(DocumentId.CreateNewId(sourceId), "Primary.cs", SourceText.From("namespace Model { public class SourceOne(Target value) { } }"))
            .AddDocument(DocumentId.CreateNewId(sourceId), "Record.cs", SourceText.From("namespace Model { public record SourceRecord(Target Value); }"))
            .AddDocument(DocumentId.CreateNewId(sourceId), "Explicit.cs", SourceText.From("""
                namespace Model
                {
                    public class SourceTwo
                    {
                        public SourceTwo(Target value)
                        {
                            System.Func<Target, int> ignored = (Target lambda) => 1;
                            int Local(Target local) => 1;
                        }
                    }
                }
                """))
            .AddDocument(DocumentId.CreateNewId(otherId), "Target.cs", SourceText.From("namespace Model { public class Target { } }"))
            .AddDocument(DocumentId.CreateNewId(consumerId), "Consumer.cs", SourceText.From("""
                extern alias other;
                using Model;
                public class ConsumerOwner { public ConsumerOwner(Target selected, other::Model.Target unrelated) { } }
                """))
            .AddProjectReference(consumerId, new ProjectReference(sourceId))
            .AddProjectReference(consumerId, new ProjectReference(otherId, ImmutableArray.Create("other")));
        var originalDocument = solution.GetDocument(declarationId)!;
        var root = await originalDocument.GetSyntaxRootAsync();
        Assert.IsNotNull(root);
        var declaration = root.DescendantNodes().OfType<TypeDeclarationSyntax>().Single();
        var annotation = new SyntaxAnnotation();
        solution = solution.WithDocumentSyntaxRoot(declarationId, root.ReplaceNode(declaration, declaration.WithAdditionalAnnotations(annotation)));
        var identity = await ConstructorDependencyIdentity.CreateAsync(solution.GetDocument(declarationId)!, annotation, CancellationToken.None);
        Assert.AreEqual("Source", identity.Type.ContainingAssembly.Name);
        var matched = new List<string>();
        var excludedNested = new List<string>();
        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            Assert.IsNotNull(compilation);
            Assert.IsFalse(compilation.GetDiagnostics().Any(item => item.Severity == DiagnosticSeverity.Error), string.Join("; ", compilation.GetDiagnostics()));
            foreach (var document in project.Documents)
            {
                var documentRoot = await document.GetSyntaxRootAsync();
                var model = await document.GetSemanticModelAsync();
                Assert.IsNotNull(documentRoot);
                Assert.IsNotNull(model);
                foreach (var parameter in documentRoot.DescendantNodes().OfType<ParameterSyntax>())
                {
                    if (!ConstructorDependencyIdentity.IsConstructorParameter(parameter))
                    {
                        excludedNested.Add(parameter.Identifier.ValueText);
                        continue;
                    }
                    if (await identity.MatchesAsync(model.GetTypeInfo(parameter.Type!).Type, CancellationToken.None))
                        matched.Add(document.Name + ":" + parameter.Identifier.ValueText);
                    else
                        Assert.AreEqual("unrelated", parameter.Identifier.ValueText);
                }
            }
        }
        CollectionAssert.AreEquivalent(new[] { "Primary.cs:value", "Record.cs:Value", "Explicit.cs:value", "Consumer.cs:selected" }, matched);
        CollectionAssert.AreEquivalent(new[] { "lambda", "local" }, excludedNested);
    }

    [TestMethod]
    public async Task UnresolvedUnrelatedConstructorDependency_IsNotMatched()
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId);
        var solution = workspace.CurrentSolution.AddProject(ProjectInfo.Create(projectId, VersionStamp.Create(),
            "Source", "Source", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            metadataReferences: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]))
            .AddDocument(documentId, "Target.cs", SourceText.From("""
                public class Target { }
                public class Owner { public Owner(Target selected, UnresolvedDependency unrelated) { } }
                """));
        var root = await solution.GetDocument(documentId)!.GetSyntaxRootAsync();
        Assert.IsNotNull(root);
        var declaration = root.DescendantNodes().OfType<TypeDeclarationSyntax>().Single(node => node.Identifier.ValueText == "Target");
        var annotation = new SyntaxAnnotation();
        solution = solution.WithDocumentSyntaxRoot(documentId, root.ReplaceNode(declaration, declaration.WithAdditionalAnnotations(annotation)));
        var document = solution.GetDocument(documentId)!;
        var identity = await ConstructorDependencyIdentity.CreateAsync(document, annotation, CancellationToken.None);
        root = await document.GetSyntaxRootAsync();
        var model = await document.GetSemanticModelAsync();
        Assert.IsNotNull(root);
        Assert.IsNotNull(model);
        Assert.IsTrue(model.Compilation.GetDiagnostics().Any(diagnostic => diagnostic.Id == "CS0246"));
        var parameters = root.DescendantNodes().OfType<ParameterSyntax>().ToArray();
        Assert.IsTrue(await identity.MatchesAsync(model.GetTypeInfo(parameters[0].Type!).Type, CancellationToken.None));
        var unresolved = model.GetTypeInfo(parameters[1].Type!).Type;
        Assert.IsInstanceOfType<IErrorTypeSymbol>(unresolved);
        Assert.IsFalse(await identity.MatchesAsync(unresolved, CancellationToken.None));
        // Roslyn can also expose an unbound error without any containing assembly.
        var unbound = model.Compilation.CreateErrorTypeSymbol(null, "UnboundDependency", 0);
        Assert.IsNull(unbound.ContainingAssembly);
        Assert.IsFalse(await identity.MatchesAsync(unbound, CancellationToken.None));
    }
}
