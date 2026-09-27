using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression guard for <c>callers-callees-field-initializer-callers-dropped</c>:
/// <see cref="SymbolServiceHelpers.GetContainingSymbolFromRoot"/> walked to the enclosing
/// <c>FieldDeclarationSyntax</c> and asked it for a declared symbol, which is always null (the
/// field symbol hangs off the <c>VariableDeclaratorSyntax</c>). References inside field
/// initializers therefore had no containing member, so <c>callers_callees</c> and
/// <c>impact_analysis</c> dropped them and <c>find_references</c> reported a null
/// <c>containingMember</c>.
/// </summary>
[TestClass]
public sealed class SymbolServiceHelpersContainingSymbolTests
{
    private const string Source = """
        using System;

        public static class Registry
        {
            public static int Tool(string name) => name.Length;
        }

        public class Consumer
        {
            private static readonly int _first = Registry.Tool("a"), _second = Registry.Tool("b");
            public static event Action Changed = () => Registry.Tool("c");

            public int Method() => Registry.Tool("d");

            public int WithLocalFunction()
            {
                return Local();
                int Local() => Registry.Tool("e");
            }
        }
        """;

    [TestMethod]
    public async Task FieldInitializer_ResolvesToTheDeclaringField()
    {
        var document = CreateDocument();

        var symbol = await ContainingSymbolAtAsync(document, "Registry.Tool(\"a\")");

        Assert.IsNotNull(symbol, "A reference inside a field initializer must have a containing symbol.");
        Assert.AreEqual(SymbolKind.Field, symbol.Kind);
        Assert.AreEqual("_first", symbol.Name);
    }

    [TestMethod]
    public async Task MultiDeclaratorField_ResolvesToTheDeclaratorContainingTheReference()
    {
        var document = CreateDocument();

        var symbol = await ContainingSymbolAtAsync(document, "Registry.Tool(\"b\")");

        Assert.IsNotNull(symbol);
        Assert.AreEqual(SymbolKind.Field, symbol.Kind);
        Assert.AreEqual("_second", symbol.Name);
    }

    [TestMethod]
    public async Task EventFieldInitializer_ResolvesToTheDeclaringEvent()
    {
        var document = CreateDocument();

        var symbol = await ContainingSymbolAtAsync(document, "Registry.Tool(\"c\")");

        Assert.IsNotNull(symbol);
        Assert.AreEqual(SymbolKind.Event, symbol.Kind);
        Assert.AreEqual("Changed", symbol.Name);
    }

    [TestMethod]
    public async Task FieldDeclaredType_FallsBackToTheFirstDeclarator()
    {
        var document = CreateDocument();

        // The `int` keyword sits outside every declarator span.
        var symbol = await ContainingSymbolAtAsync(document, "int _first");

        Assert.IsNotNull(symbol);
        Assert.AreEqual("_first", symbol.Name);
    }

    [TestMethod]
    public async Task MethodAndLocalFunctionBodies_StillResolveToTheirDeclarations()
    {
        var document = CreateDocument();

        var method = await ContainingSymbolAtAsync(document, "Registry.Tool(\"d\")");
        var local = await ContainingSymbolAtAsync(document, "Registry.Tool(\"e\")");

        Assert.AreEqual("Method", method?.Name);
        Assert.AreEqual("Local", local?.Name);
    }

    [TestMethod]
    public async Task EveryReferenceToTool_HasAContainingSymbol()
    {
        // Mirrors SymbolRelationshipService.CollectCallersAsync / ReferenceLocationMaterializer:
        // every reference location must map to a containing symbol so no caller is dropped.
        var document = CreateDocument();
        var compilation = await document.Project.GetCompilationAsync();
        var tool = compilation!.GetTypeByMetadataName("Registry")!.GetMembers("Tool").Single();

        var references = await SymbolFinder.FindReferencesAsync(tool, document.Project.Solution);
        var locations = references.SelectMany(r => r.Locations).ToList();
        Assert.HasCount(5, locations);

        var containing = new List<string>();
        foreach (var location in locations)
        {
            var symbol = await SymbolServiceHelpers.GetContainingSymbolAsync(location.Document, location.Location, CancellationToken.None);
            Assert.IsNotNull(symbol, $"Reference at {location.Location.SourceSpan} has no containing symbol.");
            containing.Add(symbol.Name);
        }

        CollectionAssert.AreEquivalent(new[] { "_first", "_second", "Changed", "Method", "Local" }, containing);
    }

    private static async Task<ISymbol?> ContainingSymbolAtAsync(Document document, string snippet)
    {
        var text = await document.GetTextAsync();
        var start = text.ToString().IndexOf(snippet, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, start, $"Snippet '{snippet}' not found in the test source.");

        var root = await document.GetSyntaxRootAsync();
        var model = await document.GetSemanticModelAsync();
        var location = Location.Create(root!.SyntaxTree, new TextSpan(start, 1));
        return SymbolServiceHelpers.GetContainingSymbolFromRoot(root, model!, location, CancellationToken.None);
    }

    private static Document CreateDocument()
    {
        var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var project = workspace.AddProject(ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            name: "ContainingSymbolTestProject",
            assemblyName: "ContainingSymbolTestProject",
            language: LanguageNames.CSharp,
            metadataReferences: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]));

        return workspace.AddDocument(project.Id, "Consumer.cs", SourceText.From(Source));
    }
}
