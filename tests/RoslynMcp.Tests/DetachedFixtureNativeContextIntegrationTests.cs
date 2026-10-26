using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class DetachedFixtureNativeContextIntegrationTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    [DataRow("standalone")]
    [DataRow("cross-project")]
    [DataRow("dependency-inversion")]
    [DataRow("extract-and-wire")]
    public async Task Extraction_Preserves_Native_Nullable_Context_Through_Preview_Apply_Reload(string route)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        var sourcePath = workspace.GetPath("SampleLib", "EchoService.cs");
        const string source = "namespace Contracts; public class EchoService { public string? Echo(string? value) => value; }";
        var sourceBytes = System.Text.Encoding.UTF8.GetBytes(source);
        await File.WriteAllBytesAsync(sourcePath, sourceBytes);
        await workspace.LoadAsync();
        await AssertNativeContextAsync(workspace.WorkspaceId);
        var before = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        await AssertCompilesAsync(before);
        await AssertNullableGraphAsync(before, "EchoService");

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
        await AssertNullableGraphAsync(pending, "EchoService");
        await AssertNullableGraphAsync(pending, "IEchoService");
        CollectionAssert.AreEqual(sourceBytes, await File.ReadAllBytesAsync(sourcePath), "Preview must preserve source bytes.");
        Assert.IsFalse(Directory.EnumerateFiles(workspace.RootPath, "IEchoService.cs", SearchOption.AllDirectories).Any(),
            "Preview must not publish the interface file.");

        var result = route == "extract-and-wire"
            ? await new CompositeApplyOrchestrator(WorkspaceManager, compositeStore, undoService: UndoService)
                .ApplyCompositeAsync(preview.PreviewToken, CancellationToken.None)
            : await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "fixture_context_test", CancellationToken.None);
        Assert.IsTrue(result.Success, result.Error);
        await workspace.ReloadAsync();
        await AssertNativeContextAsync(workspace.WorkspaceId);
        var applied = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        await AssertCompilesAsync(await ReparseSerializedDocumentsAsync(applied));
        await AssertNullableGraphAsync(applied, "EchoService");
        await AssertNullableGraphAsync(applied, "IEchoService");
        foreach (var document in applied.Projects.SelectMany(p => p.Documents).Where(d => d.Name is "EchoService.cs" or "IEchoService.cs"))
            Assert.AreEqual((await document.GetTextAsync()).ToString(), await File.ReadAllTextAsync(document.FilePath!),
                "Reloaded compilation must describe the persisted source and interface.");
    }

    private static Solution SerializedCompositePreview(Solution solution, IReadOnlyList<CompositeFileMutation> mutations)
    {
        foreach (var mutation in mutations)
        {
            if (!mutation.FilePath.EndsWith(".cs", StringComparison.Ordinal)) continue;
            var existing = solution.Projects.SelectMany(p => p.Documents).SingleOrDefault(d => d.FilePath == mutation.FilePath);
            solution = existing is not null
                ? solution.WithDocumentText(existing.Id, SourceText.From(mutation.UpdatedContent!))
                : solution.AddDocument(DocumentId.CreateNewId(solution.Projects.Single(p => p.Name == "Contracts").Id),
                    Path.GetFileName(mutation.FilePath), SourceText.From(mutation.UpdatedContent!), filePath: mutation.FilePath);
        }
        return solution.AddProjectReference(solution.Projects.Single(p => p.Name == "SampleLib").Id,
            new ProjectReference(solution.Projects.Single(p => p.Name == "Contracts").Id));
    }

    private static async Task<Solution> ReparseSerializedDocumentsAsync(Solution solution)
    {
        foreach (var document in solution.Projects.SelectMany(p => p.Documents).ToArray())
        {
            var text = (await document.GetTextAsync()).ToString();
            var tree = CSharpSyntaxTree.ParseText(text, (CSharpParseOptions?)document.Project.ParseOptions, document.FilePath ?? document.Name);
            solution = solution.WithDocumentSyntaxRoot(document.Id, await tree.GetRootAsync());
        }
        return solution;
    }

    private static async Task AssertNativeContextAsync(string workspaceId)
    {
        var errors = WorkspaceManager.GetStatus(workspaceId).WorkspaceDiagnostics
            .Where(d => string.Equals(d.Severity, "Error", StringComparison.OrdinalIgnoreCase)).ToArray();
        var facts = new List<string> { "Native errors: " + string.Join(Environment.NewLine, errors.Take(8).Select(e => e.Message)) };
        foreach (var project in WorkspaceManager.GetCurrentSolution(workspaceId).Projects)
        {
            var compilation = (await project.GetCompilationAsync())!;
            facts.Add($"{project.Name}: Nullable={compilation.Options.NullableContextOptions}; diagnostics=" +
                string.Join(",", compilation.GetDiagnostics().Select(d => d.Id).Distinct().Order(StringComparer.Ordinal).Take(20)));
        }
        var context = string.Join(Environment.NewLine, facts);
        Console.WriteLine(context);
        Assert.AreEqual(0, errors.Length, context);
        foreach (var project in WorkspaceManager.GetCurrentSolution(workspaceId).Projects)
            Assert.AreEqual(NullableContextOptions.Enable, (await project.GetCompilationAsync())!.Options.NullableContextOptions, project.Name);
    }

    private static async Task AssertNullableGraphAsync(Solution solution, string typeName)
    {
        var types = new List<INamedTypeSymbol>();
        foreach (var project in solution.Projects)
        {
            var compilation = (await project.GetCompilationAsync())!;
            var type = compilation.GetTypeByMetadataName("Contracts." + typeName);
            if (type is not null && SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly)) types.Add(type);
        }
        Assert.AreEqual(1, types.Count, typeName);
        var method = types.Single().GetMembers("Echo").OfType<IMethodSymbol>().Single();
        Assert.AreEqual(SpecialType.System_String, method.ReturnType.SpecialType);
        Assert.AreEqual(NullableAnnotation.Annotated, method.ReturnNullableAnnotation);
        Assert.AreEqual(SpecialType.System_String, method.Parameters.Single().Type.SpecialType);
        Assert.AreEqual(NullableAnnotation.Annotated, method.Parameters.Single().NullableAnnotation);
    }

    private static async Task AssertCompilesAsync(Solution solution)
    {
        foreach (var project in solution.Projects)
        {
            var errors = (await project.GetCompilationAsync())!.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
            Assert.AreEqual(0, errors.Length, project.Name + ": " + string.Join(Environment.NewLine, errors.Select(e => e.ToString())));
        }
    }
}
