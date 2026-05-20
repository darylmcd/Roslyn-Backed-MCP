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
    public async Task ToDto_PositionalRecordClass_MapsKindAsRecord()
    {
        // Regression cover for the historical bug where SymbolMapper.GetKind returned
        // "Class" for record classes (TypeKind.Class.ToString()), disagreeing with
        // document_symbols' syntax-side "Record" label. After the fix the symbol_info
        // surface returns "Record", matching the document_symbols surface.
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var symbol = await SymbolResolver.ResolveOrThrowAsync(
            solution,
            SymbolLocator.ByMetadataName("SampleLib.AnimalRecord"),
            CancellationToken.None).ConfigureAwait(false);
        var dto = SymbolMapper.ToDto(symbol, solution);
        Assert.AreEqual("Record", dto.Kind);
        StringAssert.Contains(dto.FullyQualifiedName ?? "", "AnimalRecord");
    }

    [TestMethod]
    public async Task ToDto_RecordStruct_MapsKindAsRecordStruct()
    {
        // Regression cover: record struct previously surfaced as "Struct" via
        // TypeKind.Struct.ToString(). After the fix it maps to "RecordStruct",
        // disambiguating it from a plain struct AND from a record class.
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var symbol = await SymbolResolver.ResolveOrThrowAsync(
            solution,
            SymbolLocator.ByMetadataName("SampleLib.AnimalCoord"),
            CancellationToken.None).ConfigureAwait(false);
        var dto = SymbolMapper.ToDto(symbol, solution);
        Assert.AreEqual("RecordStruct", dto.Kind);
        StringAssert.Contains(dto.FullyQualifiedName ?? "", "AnimalCoord");
    }

    [TestMethod]
    public async Task SymbolInfo_And_DocumentSymbols_Agree_On_RecordKinds()
    {
        // Cross-surface agreement: prior to the fix, symbol_info.kind on a record class
        // returned "Class" (semantic-side TypeKind.ToString()) while document_symbols.kind
        // returned "Record" (syntax-side RecordDeclarationSyntax check). After the fix both
        // surfaces must report the same label for the same source. The same agreement must
        // hold for record struct ("RecordStruct"). Reading from the shared sample workspace
        // exercises the same code path a real symbol_info / document_symbols MCP call uses.
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);

        // symbol_info side via SymbolMapper.ToDto.
        var animalRecordSymbol = await SymbolResolver.ResolveOrThrowAsync(
            solution,
            SymbolLocator.ByMetadataName("SampleLib.AnimalRecord"),
            CancellationToken.None).ConfigureAwait(false);
        var animalCoordSymbol = await SymbolResolver.ResolveOrThrowAsync(
            solution,
            SymbolLocator.ByMetadataName("SampleLib.AnimalCoord"),
            CancellationToken.None).ConfigureAwait(false);
        var animalRecordInfo = SymbolMapper.ToDto(animalRecordSymbol, solution);
        var animalCoordInfo = SymbolMapper.ToDto(animalCoordSymbol, solution);

        // document_symbols side via SymbolSearchService.GetDocumentSymbolsAsync.
        var sourcePath = animalRecordSymbol.Locations.First(l => l.IsInSource).GetLineSpan().Path;
        var docSymbols = await SymbolSearchService.GetDocumentSymbolsAsync(
            WorkspaceId, sourcePath, CancellationToken.None).ConfigureAwait(false);

        // AnimalRecords.cs uses file-scoped namespace, so CollectSymbols hoists the
        // types to the top level — no children-flattening required.
        var animalRecordDoc = docSymbols.Single(s => s.Name == "AnimalRecord");
        var animalCoordDoc = docSymbols.Single(s => s.Name == "AnimalCoord");

        Assert.AreEqual(animalRecordDoc.Kind, animalRecordInfo.Kind,
            $"symbol_info and document_symbols disagree on AnimalRecord: " +
            $"document_symbols='{animalRecordDoc.Kind}' symbol_info='{animalRecordInfo.Kind}'");
        Assert.AreEqual(animalCoordDoc.Kind, animalCoordInfo.Kind,
            $"symbol_info and document_symbols disagree on AnimalCoord: " +
            $"document_symbols='{animalCoordDoc.Kind}' symbol_info='{animalCoordInfo.Kind}'");
        Assert.AreEqual("Record", animalRecordInfo.Kind);
        Assert.AreEqual("RecordStruct", animalCoordInfo.Kind);
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
