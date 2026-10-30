using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;

using static RoslynMcp.Tests.TestInfrastructure.InterfaceExtractionTestContext;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class InterfaceBaseTypeInsertionIntegrationTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    [DataRow("class", false)]
    [DataRow("struct", false)]
    [DataRow("record", false)]
    [DataRow("record struct", false)]
    [DataRow("interface", false)]
    [DataRow("class", true)]
    [DataRow("struct", true)]
    [DataRow("interface", true)]
    [DataRow("record", true)]
    [DataRow("record struct", true)]
    public void Base_Writer_Preserves_Declaration_Kind_And_Terminator(string kind, bool semicolon)
    {
        var source = "public " + kind + " EchoService" + (semicolon ? " /* terminator */;" : " /* brace */ { }");
        var originalTree = CSharpSyntaxTree.ParseText(source);
        var originalCompilation = CSharpCompilation.Create("OriginalDeclarationKind", [originalTree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var originalErrors = originalCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.AreEqual(0, originalErrors.Length, string.Join(Environment.NewLine, originalErrors.Select(e => e.ToString())));
        var root = originalTree.GetRoot();
        var declaration = root.DescendantNodes().OfType<TypeDeclarationSyntax>().Single();
        foreach (var multiline in new[] { false, true })
        {
            var updated = InterfaceBaseTypeWriter.AddBaseType(declaration, SyntaxFactory.IdentifierName("IEchoService"), multiline);
            Assert.AreSame(updated, InterfaceBaseTypeWriter.AddBaseType(updated, SyntaxFactory.IdentifierName("IEchoService"), multiline));
            var text = updated.ToFullString();
            StringAssert.Contains(text, semicolon ? "/* terminator */;" : "/* brace */");
            var tree = CSharpSyntaxTree.ParseText(text + "\npublic interface IEchoService { }");
            var compilation = CSharpCompilation.Create("DeclarationKind", [tree],
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
            Assert.AreEqual(0, errors.Length, string.Join(Environment.NewLine, errors.Select(e => e.ToString())));
            Assert.AreEqual(declaration.OpenBraceToken.RawKind, updated.OpenBraceToken.RawKind);
            Assert.AreEqual(declaration.SemicolonToken.RawKind, updated.SemicolonToken.RawKind);
        }
    }

    public static IEnumerable<object[]> HeaderCases()
    {
        foreach (var route in new[] { "standalone", "cross-project", "dependency-inversion", "extract-and-wire" })
        {
            foreach (var existingBase in new[] { false, true })
                foreach (var layout in new[] { "tight", "block", "line", "primary-tight", "primary-block", "primary-line", "inline", "multiline", "brace-leading" })
                    yield return [route, existingBase, layout, false, "class"];
        }
        foreach (var route in new[] { "standalone", "cross-project", "dependency-inversion", "extract-and-wire" })
            foreach (var kind in new[] { "struct", "record", "record struct" })
                yield return [route, false, "block", false, kind];
        foreach (var route in new[] { "standalone", "cross-project" })
            foreach (var existingBase in new[] { false, true })
                yield return [route, existingBase, "block", true, "class"];
    }

    [TestMethod]
    [DynamicData(nameof(HeaderCases))]
    public async Task Extraction_Preserves_Header_Boundaries_Through_Serialized_Preview_And_Apply(
        string route, bool existingBase, string layout, bool nested, string kind)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        var sourcePath = workspace.GetPath("SampleLib", "EchoService.cs");
        var primary = layout.StartsWith("primary-", StringComparison.Ordinal);
        var trivia = layout switch
        {
            "block" or "primary-block" => "/* header-boundary */",
            "line" or "primary-line" => " // header-boundary\r\n",
            "tight" or "primary-tight" => "",
            "multiline" => "\r\n",
            _ => " ",
        };
        const string member = "public int Echo(int value) => value + 1;";
        var constraint = "where T : class, new()";
        var bodyTrivia = layout == "inline" ? " /* body-boundary */ " : "\r\n// body-boundary\r\n";
        if (layout == "brace-leading") bodyTrivia += "    /* brace-leading */ ";
        const string baseType = "System.Collections.Generic.List<T>";
        var source = "namespace Contracts; " + (nested ? "public class Outer<U> where U : class, new() { " : "") +
            "public " + kind + " EchoService<T>" + (primary ? "()" : "") +
            (existingBase ? " : " + baseType : "") + trivia + constraint + bodyTrivia +
            "{ " + member + " }" + (nested ? " }" : "");
        var unrelatedPath = workspace.GetPath("SampleLib", "Unchanged.cs");
        var unrelatedBytes = System.Text.Encoding.UTF8.GetBytes("// unchanged bytes\r\nnamespace Contracts; public class Unchanged { }\r\n");
        await File.WriteAllBytesAsync(unrelatedPath, unrelatedBytes);
        var sourceBytes = System.Text.Encoding.UTF8.GetBytes(source);
        await File.WriteAllBytesAsync(sourcePath, sourceBytes);
        await workspace.LoadAsync();
        await AssertNativeContextAsync(WorkspaceManager, workspace.WorkspaceId);
        var before = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        await AssertCompilesAsync(before);
        var references = (await before.Projects.Single(p => p.Name == "SampleLib").GetCompilationAsync())!.References;
        var concreteType = (nested ? "Outer<object>." : "") + "EchoService<object>";
        Assert.AreEqual(8, ExecuteEcho(source, null, concreteType, references));

        var compositeStore = new CompositePreviewStore();
        RefactoringPreviewDto preview = route switch
        {
            "standalone" => await InterfaceExtractionService.PreviewExtractInterfaceAsync(workspace.WorkspaceId,
                sourcePath, "EchoService", "IEchoService", null, false, CancellationToken.None),
            "cross-project" => await CrossProjectRefactoringService.PreviewExtractInterfaceAsync(workspace.WorkspaceId,
                sourcePath, "EchoService", "IEchoService", "Contracts", CancellationToken.None),
            "dependency-inversion" => await CrossProjectRefactoringService.PreviewDependencyInversionAsync(workspace.WorkspaceId,
                sourcePath, "EchoService", "IEchoService", "Contracts", CancellationToken.None),
            "extract-and-wire" => await new ExtractAndWireOrchestrator(WorkspaceManager, compositeStore, PreviewStore,
                CrossProjectRefactoringService, DiRegistrationService).PreviewExtractAndWireInterfaceAsync(workspace.WorkspaceId,
                    sourcePath, "EchoService", "IEchoService", "Contracts", true, CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(route)),
        };

        var pending = route == "extract-and-wire"
            ? SerializedCompositePreview(before, compositeStore.Retrieve(preview.PreviewToken)!.Value.Mutations)
            : PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;
        pending = await ReparseSerializedDocumentsAsync(pending);
        await AssertCompilesAsync(pending);
        var implementation = await SourceAsync(pending, "EchoService.cs");
        var contract = await SourceAsync(pending, "IEchoService.cs");
        AssertHeader(implementation);
        Assert.AreEqual(8, ExecuteEcho(implementation, contract, concreteType, references));
        CollectionAssert.AreEqual(sourceBytes, await File.ReadAllBytesAsync(sourcePath), "Preview must preserve source bytes.");
        Assert.IsFalse(Directory.EnumerateFiles(workspace.RootPath, "IEchoService.cs", SearchOption.AllDirectories).Any(),
            "Preview must not publish the interface file.");

        var result = route == "extract-and-wire"
            ? await new CompositeApplyOrchestrator(WorkspaceManager, compositeStore, undoService: UndoService)
                .ApplyCompositeAsync(preview.PreviewToken, CancellationToken.None)
            : await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "fixture_context_test", CancellationToken.None);
        Assert.IsTrue(result.Success, result.Error);
        await workspace.ReloadAsync();
        await AssertNativeContextAsync(WorkspaceManager, workspace.WorkspaceId);
        var applied = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        await AssertCompilesAsync(await ReparseSerializedDocumentsAsync(applied));
        var persisted = await File.ReadAllTextAsync(sourcePath);
        AssertHeader(persisted);
        Assert.AreEqual(8, ExecuteEcho(persisted, await SourceAsync(applied, "IEchoService.cs"), concreteType, references));
        CollectionAssert.AreEqual(unrelatedBytes, await File.ReadAllBytesAsync(unrelatedPath));
        foreach (var document in applied.Projects.SelectMany(p => p.Documents).Where(d => d.Name is "EchoService.cs" or "IEchoService.cs"))
            Assert.AreEqual((await document.GetTextAsync()).ToString(), await File.ReadAllTextAsync(document.FilePath!),
                "Reloaded compilation must describe the persisted source and interface.");
        void AssertHeader(string text)
        {
            if (trivia.Trim().Length > 0) StringAssert.Contains(text, trivia.TrimStart());
            StringAssert.Contains(text, bodyTrivia);
            StringAssert.Contains(text, member);
            StringAssert.Contains(text, constraint);
            if (existingBase)
                StringAssert.Contains(text, baseType + (route == "standalone" ? ", IEchoService" : "," + Environment.NewLine + "    IEchoService"));
        }

    }

    private static async Task<string> SourceAsync(Solution solution, string name) =>
        (await solution.Projects.SelectMany(p => p.Documents).Single(d => d.Name == name).GetTextAsync()).ToString();

    private static int ExecuteEcho(string implementation, string? contract, string concreteType, IEnumerable<MetadataReference> references)
    {
        var calls = "namespace Contracts; public static class Calls { public static int Run() { " +
            (contract is null ? "var" : "IEchoService") + " service = new " + concreteType + "(); return service.Echo(7); } }";
        var texts = new List<string> { implementation, calls };
        if (contract is not null) texts.Add(contract);
        var compilation = CSharpCompilation.Create("HeaderProbe_" + Guid.NewGuid().ToString("N"),
            texts.Select(text => CSharpSyntaxTree.ParseText(text)),
            references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        using var bytes = new MemoryStream();
        var result = compilation.Emit(bytes);
        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        var context = new System.Runtime.Loader.AssemblyLoadContext("HeaderProbe", isCollectible: true);
        try
        {
            bytes.Position = 0;
            var assembly = context.LoadFromStream(bytes);
            return (int)assembly.GetType("Contracts.Calls")!.GetMethod("Run")!.Invoke(null, null)!;
        }
        finally
        {
            context.Unload();
        }
    }
}
