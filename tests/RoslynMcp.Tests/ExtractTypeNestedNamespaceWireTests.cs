using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// extract-type-nested-namespace-identity wire evidence (release-policy Rule 1, ADR 0041), both
/// protocol eras. The static fixture is the primary evidence: the base returned a SUCCESS with the type
/// in the wrong namespace, the change returns a success of the same shape with the full namespace. The
/// instance fixture is refusal to success (the base falsely refused with CS0246); policy line 44 names
/// only success to error as major. Raw frames are printed as OBSERVED-WIRE for the PR body.
/// </summary>
[TestClass]
public sealed class ExtractTypeNestedNamespaceWireTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    [DataRow("2025-11-25", "static")]
    [DataRow("2026-07-28", "static")]
    [DataRow("2025-11-25", "instance")]
    [DataRow("2026-07-28", "instance")]
    public async Task ExtractTypePreview_NestedNamespace_SuccessShapeAndFullNamespaceOnTheWire(string version, string shape)
    {
        var isStatic = shape == "static";
        var typeName = isStatic ? "WireStaticSrc" : "WireInstanceSrc";
        var member = isStatic ? "private static int Unused() => 1;" : "private int Helper() => 1; public int V => Helper();";
        await using var workspace = CreateIsolatedWorkspaceCopy();
        await File.WriteAllTextAsync(
            workspace.GetPath("SampleLib", typeName + ".cs"),
            "namespace SampleLib\n{\n    namespace Inner\n    {\n        public class " + typeName + "\n        {\n            "
                + member + "\n        }\n    }\n}\n",
            CancellationToken.None);

        await using var harness = await ProductionParityMcpHarness.CreateAsync(
            "extract-type-nested-namespace", version,
            services => services.AddSingleton(new SecurityOptions { SanctionedRoots = [workspace.RootPath] }));
        var loaded = await harness.Client.CallToolAsync("workspace_load",
            new Dictionary<string, object?> { ["path"] = workspace.SolutionPath, ["autoRestore"] = false },
            cancellationToken: CancellationToken.None);
        Assert.IsFalse(loaded.IsError == true, string.Concat(loaded.Content.OfType<TextContentBlock>().Select(c => c.Text)));
        var id = JsonNode.Parse(loaded.Content.OfType<TextContentBlock>().Single().Text)!["workspaceId"]!.GetValue<string>();

        var prior = harness.RawServerMessages.Count;
        await harness.Client.CallToolAsync("extract_type_preview",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = id,
                ["filePath"] = workspace.GetPath("SampleLib", typeName + ".cs"),
                ["typeName"] = typeName,
                ["memberNames"] = new[] { isStatic ? "Unused" : "Helper" },
                ["newTypeName"] = "ExtractedType",
            },
            cancellationToken: CancellationToken.None);
        var frame = harness.RawServerMessages.Skip(prior).Select(s => JsonNode.Parse(s)!.AsObject())
            .Single(node => node.ContainsKey("id") && (node.ContainsKey("result") || node.ContainsKey("error")));
        Console.WriteLine("OBSERVED-WIRE:" + version + ":" + shape + ":" + frame.ToJsonString());

        Assert.IsNull(frame["error"], frame.ToJsonString());
        var result = frame["result"]!.AsObject();
        Assert.AreNotEqual(true, result["isError"]?.GetValue<bool>(), frame.ToJsonString());
        if (version == "2026-07-28")
            Assert.AreEqual("complete", result["resultType"]!.GetValue<string>());
        else
            Assert.IsFalse(result.ContainsKey("resultType"));
        var payload = JsonNode.Parse(result["content"]!.AsArray().Single()!["text"]!.GetValue<string>())!.AsObject();
        Assert.IsFalse(payload.ContainsKey("error"), frame.ToJsonString());
        Assert.IsNotNull(payload["previewToken"], frame.ToJsonString());
        Assert.IsNotNull(payload["description"], frame.ToJsonString());
        var addedPath = workspace.GetPath("SampleLib", "ExtractedType.cs");
        var addedChanges = payload["changes"]!.AsArray().Where(change =>
            string.Equals(Path.GetFullPath(change!["filePath"]!.GetValue<string>()),
                Path.GetFullPath(addedPath), StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert.HasCount(1, addedChanges, frame.ToJsonString());
        var generatedText = TypeExtractionTests.ReconstructPreviewText(
            string.Empty, addedChanges.Single()!["unifiedDiff"]!.GetValue<string>());
        var tree = CSharpSyntaxTree.ParseText(generatedText);
        Assert.IsFalse(tree.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error), generatedText);
        var compilation = CSharpCompilation.Create("NestedNamespaceWire", [tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.IsEmpty(errors, string.Join("\n", errors.Select(d => d.ToString())));
        var declaration = tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>().Single();
        var generatedType = compilation.GetSemanticModel(tree).GetDeclaredSymbol(declaration)!;
        Assert.AreEqual("SampleLib.Inner.ExtractedType", generatedType.ToDisplayString());
        Assert.IsTrue(SymbolEqualityComparer.Default.Equals(
            generatedType, compilation.GetTypeByMetadataName("SampleLib.Inner.ExtractedType")));
        Assert.IsNull(compilation.GetTypeByMetadataName("SampleLib.ExtractedType"));
        Assert.IsNull(compilation.GetTypeByMetadataName("Inner.ExtractedType"));
        Assert.IsNull(compilation.GetTypeByMetadataName("ExtractedType"));
        var moved = generatedType.GetMembers(isStatic ? "Unused" : "Helper").OfType<IMethodSymbol>().Single();
        Assert.AreEqual(isStatic, moved.IsStatic);
        Assert.AreEqual(SpecialType.System_Int32, moved.ReturnType.SpecialType);
    }
}
