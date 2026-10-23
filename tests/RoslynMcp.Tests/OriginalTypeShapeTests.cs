using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class OriginalTypeShapeTests
{
    [TestMethod]
    [DataRow("generic-nullability")]
    [DataRow("tuple-name")]
    [DataRow("nullable-wrapper")]
    [DataRow("nullable-added")]
    [DataRow("outer-array")]
    [DataRow("native-integer")]
    public async Task SurvivingOriginalType_DoesNotAcceptLostSemanticShape(string mutation)
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId);
        var solution = workspace.CurrentSolution.AddProject(ProjectInfo.Create(projectId, VersionStamp.Create(), "Original", "Original", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable),
            metadataReferences: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]))
            .AddDocument(documentId, "Original.cs", SourceText.From("""
                public class Foo { }
                public class C
                {
                    public System.Collections.Generic.List<Foo?> Generic => null!;
                    public (Foo? First, Foo Second) Tuple => default;
                    public Foo? Nullable => null;
                    public Foo NonNullable => null!;
                    public Foo[]? Array => null;
                    public nint Native => default;
                }
                """));
        var before = await solution.GetProject(projectId)!.GetCompilationAsync();
        Assert.IsNotNull(before);
        Assert.IsFalse(before.GetDiagnostics().Any(item => item.Severity == DiagnosticSeverity.Error));
        var captured = await OriginalNameBindings.CaptureAsync(solution, CancellationToken.None);
        var root = await captured.Solution.GetDocument(documentId)!.GetSyntaxRootAsync();
        Assert.IsNotNull(root);
        var propertyName = mutation switch
        {
            "generic-nullability" => "Generic",
            "tuple-name" => "Tuple",
            "nullable-wrapper" => "Nullable",
            "nullable-added" => "NonNullable",
            "outer-array" => "Array",
            "native-integer" => "Native",
            _ => throw new AssertFailedException("Unknown mutation.")
        };
        var property = root.DescendantNodes().OfType<PropertyDeclarationSyntax>().Single(item => item.Identifier.ValueText == propertyName);
        TypeSyntax changedType;
        if (mutation == "generic-nullability")
        {
            var argument = property.Type.DescendantNodes().OfType<NullableTypeSyntax>().Single();
            changedType = property.Type.ReplaceNode(argument, argument.ElementType);
        }
        else if (mutation == "tuple-name")
        {
            var tuple = (TupleTypeSyntax)property.Type;
            var first = tuple.Elements[0];
            changedType = tuple.WithElements(tuple.Elements.Replace(first,
                first.WithIdentifier(SyntaxFactory.Identifier("Changed").WithTriviaFrom(first.Identifier))));
        }
        else if (mutation is "nullable-wrapper" or "outer-array")
        {
            var nullable = (NullableTypeSyntax)property.Type;
            changedType = nullable.CopyAnnotationsTo(nullable.ElementType.WithTriviaFrom(nullable));
        }
        else if (mutation == "nullable-added")
        {
            changedType = property.Type.CopyAnnotationsTo(SyntaxFactory.NullableType(
                SyntaxFactory.ParseTypeName(property.Type.ToString())).WithTriviaFrom(property.Type));
        }
        else
        {
            changedType = property.Type.CopyAnnotationsTo(SyntaxFactory.ParseTypeName("global::System.IntPtr"));
        }
        var changed = captured.Solution.WithDocumentSyntaxRoot(documentId, root.ReplaceNode(property.Type, changedType));
        var restored = await captured.RestoreAsync(changed, CancellationToken.None);
        var compilation = await restored.GetProject(projectId)!.GetCompilationAsync();
        Assert.IsNotNull(compilation);
        Assert.IsFalse(compilation.GetDiagnostics().Any(item => item.Severity == DiagnosticSeverity.Error), string.Join("; ", compilation.GetDiagnostics()));
        var type = compilation.GetTypeByMetadataName("C")!;
        var generic = (INamedTypeSymbol)type.GetMembers("Generic").OfType<IPropertySymbol>().Single().Type;
        Assert.AreEqual(NullableAnnotation.Annotated, generic.TypeArguments.Single().NullableAnnotation);
        var tupleType = (INamedTypeSymbol)type.GetMembers("Tuple").OfType<IPropertySymbol>().Single().Type;
        CollectionAssert.AreEqual(new[] { "First", "Second" }, tupleType.TupleElements.Select(item => item.Name).ToArray());
        Assert.AreEqual(NullableAnnotation.Annotated, tupleType.TupleElements[0].Type.NullableAnnotation);
        Assert.AreEqual(NullableAnnotation.NotAnnotated, tupleType.TupleElements[1].Type.NullableAnnotation);
        Assert.AreEqual(NullableAnnotation.Annotated, type.GetMembers("Nullable").OfType<IPropertySymbol>().Single().NullableAnnotation,
            "Original annotation: " + before.GetTypeByMetadataName("C")!.GetMembers("Nullable").OfType<IPropertySymbol>().Single().NullableAnnotation
            + "; restored source: " + (await restored.GetDocument(documentId)!.GetTextAsync()).ToString());
        Assert.AreEqual(NullableAnnotation.NotAnnotated, type.GetMembers("NonNullable").OfType<IPropertySymbol>().Single().NullableAnnotation);
        var arrayProperty = type.GetMembers("Array").OfType<IPropertySymbol>().Single();
        Assert.AreEqual(NullableAnnotation.Annotated, arrayProperty.NullableAnnotation);
        Assert.AreEqual(NullableAnnotation.NotAnnotated, ((IArrayTypeSymbol)arrayProperty.Type).ElementType.NullableAnnotation);
        Assert.IsTrue(type.GetMembers("Native").OfType<IPropertySymbol>().Single().Type.IsNativeIntegerType);
    }
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DisplacedNameOf_PreservesCommentsOrRefusesDirectiveMovement(bool directives)
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId);
        var argument = directives ? "\n#if true\nIAdded\n#endif\n" : "/* before alias */ IAdded /* after alias */";
        var source = "namespace Origin { public class Existing { } } namespace User { using IAdded = Origin.Existing; public class C { public const string Name = nameof(" + argument + "); } }";
        var solution = workspace.CurrentSolution.AddProject(ProjectInfo.Create(projectId, VersionStamp.Create(), "Original", "Original", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            metadataReferences: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]))
            .AddDocument(documentId, "Original.cs", SourceText.From(source));
        var originalCompilation = await solution.GetProject(projectId)!.GetCompilationAsync();
        Assert.IsNotNull(originalCompilation);
        Assert.IsFalse(originalCompilation.GetDiagnostics().Any(item => item.Severity == DiagnosticSeverity.Error), string.Join("; ", originalCompilation.GetDiagnostics()));
        var captured = await OriginalNameBindings.CaptureAsync(solution, CancellationToken.None);
        var changed = captured.Solution.AddDocument(DocumentId.CreateNewId(projectId), "Added.cs", SourceText.From("namespace User { public interface IAdded { } }"));
        if (directives)
        {
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => captured.RestoreAsync(changed, CancellationToken.None));
            return;
        }
        var restored = await captured.RestoreAsync(changed, CancellationToken.None);
        var compilation = await restored.GetProject(projectId)!.GetCompilationAsync();
        Assert.IsNotNull(compilation);
        Assert.IsFalse(compilation.GetDiagnostics().Any(item => item.Severity == DiagnosticSeverity.Error), string.Join("; ", compilation.GetDiagnostics()));
        Assert.AreEqual("IAdded", compilation.GetTypeByMetadataName("User.C")!.GetMembers("Name").OfType<IFieldSymbol>().Single().ConstantValue);
        var text = (await restored.GetDocument(documentId)!.GetTextAsync()).ToString();
        StringAssert.Contains(text, "/* before alias */");
        StringAssert.Contains(text, "/* after alias */");
    }
    [TestMethod]
    public async Task NullableValueAlias_CanExpandWithoutChangingItsNativeType()
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId);
        var solution = workspace.CurrentSolution.AddProject(ProjectInfo.Create(projectId, VersionStamp.Create(), "Original", "Original", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            metadataReferences: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]))
            .AddDocument(documentId, "Original.cs", SourceText.From("namespace User { using Alias = int?; public class C { public Alias Value => null; } }"));
        var originalCompilation = await solution.GetProject(projectId)!.GetCompilationAsync();
        Assert.IsNotNull(originalCompilation);
        Assert.IsFalse(originalCompilation.GetDiagnostics().Any(item => item.Severity == DiagnosticSeverity.Error), string.Join("; ", originalCompilation.GetDiagnostics()));
        var captured = await OriginalNameBindings.CaptureAsync(solution, CancellationToken.None);
        var changed = captured.Solution.AddDocument(DocumentId.CreateNewId(projectId), "Added.cs", SourceText.From("namespace User { public interface Alias { } }"));
        var restored = await captured.RestoreAsync(changed, CancellationToken.None);
        var compilation = await restored.GetProject(projectId)!.GetCompilationAsync();
        Assert.IsNotNull(compilation);
        Assert.IsFalse(compilation.GetDiagnostics().Any(item => item.Severity == DiagnosticSeverity.Error), string.Join("; ", compilation.GetDiagnostics()));
        var propertyType = (INamedTypeSymbol)compilation.GetTypeByMetadataName("User.C")!.GetMembers("Value").OfType<IPropertySymbol>().Single().Type;
        Assert.AreEqual(SpecialType.System_Nullable_T, propertyType.OriginalDefinition.SpecialType);
        Assert.AreEqual(SpecialType.System_Int32, propertyType.TypeArguments.Single().SpecialType);
    }
}
