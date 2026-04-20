using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

[DoNotParallelize]
[TestClass]
public sealed class SymbolMapperTests : SharedWorkspaceTestBase
{
    private static string WorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        WorkspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);
    }

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task ToDto_Interface_MapsKindAndFqName()
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var symbol = await SymbolResolver.ResolveOrThrowAsync(
            solution,
            SymbolLocator.ByMetadataName("SampleLib.IAnimal"),
            CancellationToken.None).ConfigureAwait(false);
        var dto = SymbolMapper.ToDto(symbol, solution);
        Assert.AreEqual("Interface", dto.Kind);
        StringAssert.Contains(dto.FullyQualifiedName ?? "", "IAnimal");
        Assert.AreEqual("SampleLib", dto.Namespace);
    }

    [TestMethod]
    public async Task ToDto_Class_MapsKindAndHierarchy()
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var symbol = await SymbolResolver.ResolveOrThrowAsync(
            solution,
            SymbolLocator.ByMetadataName("SampleLib.AnimalService"),
            CancellationToken.None).ConfigureAwait(false);
        var dto = SymbolMapper.ToDto(symbol, solution);
        Assert.AreEqual("Class", dto.Kind);
        StringAssert.Contains(dto.FullyQualifiedName ?? "", "AnimalService");
    }

    [TestMethod]
    public async Task ToDto_Method_MapsReturnTypeAndParameters()
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var type = (INamedTypeSymbol)await SymbolResolver.ResolveOrThrowAsync(
            solution,
            SymbolLocator.ByMetadataName("SampleLib.AnimalService"),
            CancellationToken.None).ConfigureAwait(false);
        var method = type.GetMembers("CountAnimals").OfType<IMethodSymbol>().First(m => m.Parameters.Length == 1);
        var dto = SymbolMapper.ToDto(method, solution);
        Assert.AreEqual("Method", dto.Kind);
        Assert.IsNotNull(dto.ReturnType);
        Assert.IsNotNull(dto.Parameters);
        Assert.AreEqual(1, dto.Parameters!.Count);
        StringAssert.Contains(dto.Parameters[0], "IAnimal");
    }

    [TestMethod]
    public async Task ToDto_Property_MapsAccessors()
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var type = (INamedTypeSymbol)await SymbolResolver.ResolveOrThrowAsync(
            solution,
            SymbolLocator.ByMetadataName("SampleLib.Cat"),
            CancellationToken.None).ConfigureAwait(false);
        var property = type.GetMembers("Name").OfType<IPropertySymbol>().First();
        var dto = SymbolMapper.ToDto(property, solution);
        Assert.AreEqual("Property", dto.Kind);
        Assert.IsTrue(dto.HasGetter);
    }

    [TestMethod]
    public async Task ClassifyReferenceLocation_CallSite_IsRead()
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var type = (INamedTypeSymbol)await SymbolResolver.ResolveOrThrowAsync(
            solution,
            SymbolLocator.ByMetadataName("SampleLib.AnimalService"),
            CancellationToken.None).ConfigureAwait(false);
        var method = type.GetMembers("CountAnimals").OfType<IMethodSymbol>().First(m => m.Parameters.Length == 1);
        var refs = await SymbolFinder.FindReferencesAsync(method, solution, CancellationToken.None).ConfigureAwait(false);
        var loc = refs.SelectMany(r => r.Locations).First(l => !l.IsImplicit);
        var classification = SymbolMapper.ClassifyReferenceLocation(loc);
        Assert.AreEqual("Read", classification);
    }

    [TestMethod]
    public async Task ClassifyReferenceLocation_Nameof_IsNameOf()
    {
        var tree = CSharpSyntaxTree.ParseText(
            """
            class C {
              static int F() => 1;
              static string M() => nameof(F);
            }
            """,
            path: "nameof.cs");
        var references =
            new List<MetadataReference> { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) };
        using var workspace = new AdhocWorkspace();
        var project = workspace.CurrentSolution
            .AddProject("nameofasm", "nameofasm.dll", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReferences(references)
            .AddDocument("nameof.cs", await tree.GetRootAsync(CancellationToken.None).ConfigureAwait(false)).Project;
        var solution = project.Solution;
        var doc = solution.Projects.Single().Documents.Single();
        var model = await doc.GetSemanticModelAsync(CancellationToken.None).ConfigureAwait(false);
        var root = await doc.GetSyntaxRootAsync(CancellationToken.None).ConfigureAwait(false);
        Assert.IsNotNull(root);
        var methodF = root.DescendantNodes().OfType<MethodDeclarationSyntax>().First(m => m.Identifier.Text == "F");
        var symF = model.GetDeclaredSymbol(methodF) ?? throw new AssertFailedException("symbol");
        var refs = await SymbolFinder.FindReferencesAsync(symF, solution, CancellationToken.None).ConfigureAwait(false);
        var nameofRef = refs.SelectMany(r => r.Locations).Last(l => !l.IsImplicit);
        Assert.AreEqual("NameOf", SymbolMapper.ClassifyReferenceLocation(nameofRef));
    }

    [TestMethod]
    public void ToDiagnosticDto_MapsIdAndSeverity()
    {
        var text = SourceText.From("class X { }");
        var tree = SyntaxFactory.ParseSyntaxTree(text);
        var loc = Location.Create(tree, TextSpan.FromBounds(0, 1));
        var diagnostic = Diagnostic.Create(
            new DiagnosticDescriptor(
                "TEST999",
                "t",
                "message",
                "cat",
                DiagnosticSeverity.Warning,
                isEnabledByDefault: true),
            loc);
        var dto = SymbolMapper.ToDiagnosticDto(diagnostic);
        Assert.AreEqual("TEST999", dto.Id);
        Assert.AreEqual("message", dto.Message);
        StringAssert.Contains(dto.Severity ?? "", "Warning");
    }
}
