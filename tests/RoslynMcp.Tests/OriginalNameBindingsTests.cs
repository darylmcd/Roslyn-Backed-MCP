using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class OriginalNameBindingsTests
{
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task NewReference_PreservesOtherPublicTypesAndAttributes(bool addReference, bool nullableEnabled)
    {
        using var workspace = new AdhocWorkspace();
        var sourceId = ProjectId.CreateNewId();
        var targetId = ProjectId.CreateNewId();
        var sourceDocumentId = DocumentId.CreateNewId(sourceId);
        const string source = """
            namespace Origin
            {
                public class Foo { }
                public class MarkerAttribute : System.Attribute { }
            }
            namespace Conflict { public class Present { } }
            namespace User
            {
                using Origin;
                using Conflict;
                [Marker] public class C { public Foo Existing => null!; public System.Collections.Generic.List<Foo?> Constructed => null!; public Foo? Default() { var result = default(Foo); return result; } }
            }
            """;
        var solution = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(sourceId, VersionStamp.Create(), "Source", "Source", LanguageNames.CSharp,
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: nullableEnabled ? NullableContextOptions.Enable : NullableContextOptions.Disable),
                metadataReferences: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]))
            .AddProject(ProjectInfo.Create(targetId, VersionStamp.Create(), "Target", "Target", LanguageNames.CSharp,
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: nullableEnabled ? NullableContextOptions.Enable : NullableContextOptions.Disable),
                metadataReferences: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]))
            .AddDocument(sourceDocumentId, "Original.cs", SourceText.From(source))
            .AddDocument(DocumentId.CreateNewId(targetId), "Added.cs", SourceText.From("""
                namespace Conflict
                {
                    public interface IGenerated { }
                    public class Foo { }
                    public class MarkerAttribute : System.Attribute { }
                }
                """));
        var original = await solution.GetProject(sourceId)!.GetCompilationAsync();
        Assert.IsNotNull(original);
        Assert.IsFalse(original.GetDiagnostics().Any(item => item.Severity == DiagnosticSeverity.Error));
        var bindings = await OriginalNameBindings.CaptureAsync(solution, CancellationToken.None);
        var modified = addReference
            ? bindings.Solution.AddProjectReference(sourceId, new ProjectReference(targetId))
            : bindings.Solution;
        if (addReference)
        {
            var broken = await modified.GetProject(sourceId)!.GetCompilationAsync();
            Assert.IsTrue(broken!.GetDiagnostics().Any(item => item.Id == "CS0104"),
                "The new reference must expose an actual ambiguous original name before preservation.");
        }
        var restored = await bindings.RestoreAsync(modified, CancellationToken.None);
        var compilation = await restored.GetProject(sourceId)!.GetCompilationAsync();
        Assert.IsNotNull(compilation);
        Assert.IsFalse(compilation.GetDiagnostics().Any(item => item.Severity == DiagnosticSeverity.Error),
            string.Join("; ", compilation.GetDiagnostics()));
        var type = compilation.GetTypeByMetadataName("User.C")!;
        var property = type.GetMembers("Existing").OfType<IPropertySymbol>().Single();
        Assert.AreEqual("Origin.Foo", property.Type.ToDisplayString());
        Assert.AreEqual("Source", property.Type.ContainingAssembly.Name);
        Assert.AreEqual(nullableEnabled ? NullableAnnotation.NotAnnotated : NullableAnnotation.None, property.NullableAnnotation);
        var constructed = (INamedTypeSymbol)type.GetMembers("Constructed").OfType<IPropertySymbol>().Single().Type;
        Assert.AreEqual(NullableAnnotation.Annotated, constructed.TypeArguments.Single().NullableAnnotation);
        Assert.AreEqual("Foo", constructed.TypeArguments.Single().Name);
        Assert.AreEqual("Origin", constructed.TypeArguments.Single().ContainingNamespace.ToDisplayString());
        Assert.AreEqual(NullableAnnotation.Annotated, type.GetMembers("Default").OfType<IMethodSymbol>().Single().ReturnNullableAnnotation);
        Assert.AreEqual("Source", constructed.TypeArguments.Single().ContainingAssembly.Name);
        Assert.AreEqual("Origin.MarkerAttribute", type.GetAttributes().Single().AttributeClass!.ToDisplayString());
        Assert.AreEqual("Source", type.GetAttributes().Single().AttributeClass!.ContainingAssembly.Name);
        if (!addReference)
            Assert.AreEqual(source, (await restored.GetDocument(sourceDocumentId)!.GetTextAsync()).ToString());
        Assert.AreEqual(source, (await solution.GetDocument(sourceDocumentId)!.GetTextAsync()).ToString());
    }
}
