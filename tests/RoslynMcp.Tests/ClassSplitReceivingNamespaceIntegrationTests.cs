using System.Text.Json;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Services;
using static RoslynMcp.Tests.TestInfrastructure.InterfaceExtractionTestContext;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ClassSplitReceivingNamespaceIntegrationTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    [DataRow("7.3", false, false, null, null)]
    [DataRow("9.0", false, false, null, null)]
    [DataRow("10.0", true, false, null, null)]
    [DataRow("14.0", true, false, null, null)]
    [DataRow("9.0", false, true, null, null)]
    [DataRow("9.0", false, false, "Outer", null)]
    [DataRow("14.0", true, false, "Outer", null)]
    [DataRow("14.0", true, false, "@class", null)]
    [DataRow("9.0", false, false, "Outer.Chosen", "Outer.\n#if FEATURE\nChosen\n#else\nAlternate\n#endif\n")]
    public async Task Split_Uses_Receiving_Syntax_Through_Preview_Apply_And_Reload(
        string version, bool fileScoped, bool globalNamespace, string? outerNamespace, string? outerNamespaceSource)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        var projectPath = workspace.GetPath("Contracts", "Contracts.csproj");
        var xml = XDocument.Load(projectPath);
        xml.Root!.Add(new XElement("PropertyGroup", new XElement("LangVersion", version),
            new XElement("Nullable", "disable"), new XElement("ImplicitUsings", "disable")));
        if (outerNamespaceSource is not null)
            xml.Root!.Add(new XElement("PropertyGroup", new XElement("DefineConstants", "FEATURE")));
        xml.Save(projectPath);
        var projectBytes = await File.ReadAllBytesAsync(projectPath);
        var sourcePath = workspace.GetPath("Contracts", "Worker.cs");
        var newPath = workspace.GetPath("Contracts", "Worker.Move.cs");
        var declaration = "/// <summary>Worker documentation</summary>\r\n[System.Serializable]\r\npublic class Worker\r\n{\r\n    public int Stay() => 3;\r\n    public int Move() => 7; // member-tail\r\n} // type-tail\r\n";
        var namespaceName = outerNamespace is null ? "Contracts" : $"{outerNamespace}.Contracts";
        var metadataName = globalNamespace ? "Worker" : namespaceName.Replace("@", "", StringComparison.Ordinal) + ".Worker";
        var source = globalNamespace ? declaration : "namespace Contracts\r\n{\r\n" + declaration + "}\r\n";
        if (outerNamespace is not null)
            source = $"namespace {outerNamespaceSource ?? outerNamespace}\r\n{{\r\n{source}}}\r\n";
        source = "// license stays here\r\nusing System;\r\n" + source;
        await File.WriteAllTextAsync(sourcePath, source);
        var sourceBytes = await File.ReadAllBytesAsync(sourcePath);
        var unrelatedPath = workspace.GetPath("Contracts", "Unchanged.cs");
        await File.WriteAllTextAsync(unrelatedPath, "namespace Contracts { public class Unchanged { } }\r\n");
        var unrelatedBytes = await File.ReadAllBytesAsync(unrelatedPath);
        await workspace.LoadAsync();
        var before = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var project = before.Projects.Single(p => p.Name == "Contracts");
        Assert.IsTrue(LanguageVersionFacts.TryParse(version, out var expected));
        Assert.AreEqual(expected, ((CSharpParseOptions)project.ParseOptions!).LanguageVersion);
        await AssertCompilesAsync(before);
        await AssertRuntimeAsync(project, metadataName);
        var originalVersion = WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId);
        var store = new CompositePreviewStore();
        var split = new ClassSplitOrchestrator(WorkspaceManager, store);
        var previewJson = await OrchestrationTools.PreviewSplitClass(WorkspaceExecutionGate, split,
            workspace.WorkspaceId, sourcePath, "Worker", ["Move"], "Worker.Move.cs", CancellationToken.None);
        var preview = JsonSerializer.Deserialize<RefactoringPreviewDto>(previewJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.IsFalse(string.IsNullOrWhiteSpace(preview.PreviewToken), previewJson);
        Assert.AreEqual(2, preview.Changes.Count);
        StringAssert.Contains(preview.Changes.Single(change => change.FilePath == sourcePath).UnifiedDiff, "partial class Worker");
        var generatedDiff = preview.Changes.Single(change => change.FilePath == newPath).UnifiedDiff;
        StringAssert.Contains(generatedDiff, "Move()");
        Assert.AreEqual(originalVersion, WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId));
        var entry = store.Retrieve(preview.PreviewToken, null)!.Value;
        Assert.AreEqual(workspace.WorkspaceId, entry.WorkspaceId);
        Assert.AreEqual(originalVersion, entry.WorkspaceVersion);
        Assert.AreEqual(workspace.WorkspaceId, store.PeekWorkspaceId(preview.PreviewToken));
        var mutations = entry.Mutations;
        var generated = mutations.Single(m => m.FilePath == newPath).UpdatedContent!;
        var tree = CSharpSyntaxTree.ParseText(generated, (CSharpParseOptions)project.ParseOptions!, newPath);
        Assert.AreEqual(0, tree.GetDiagnostics().Count(d => d.Severity == DiagnosticSeverity.Error),
            string.Join(Environment.NewLine, tree.GetDiagnostics()));
        var pending = before.WithDocumentText(project.Documents.Single(d => d.FilePath == sourcePath).Id,
            SourceText.From(mutations.Single(m => m.FilePath == sourcePath).UpdatedContent!));
        pending = pending.AddDocument(DocumentId.CreateNewId(project.Id), "Worker.Move.cs",
            SourceText.From(generated), filePath: newPath);
        await AssertCompilesAsync(pending);
        var root = (CompilationUnitSyntax)await tree.GetRootAsync();
        if (globalNamespace)
            Assert.IsInstanceOfType<ClassDeclarationSyntax>(root.Members.Single());
        else
        {
            var generatedNamespace = root.Members.Single() as BaseNamespaceDeclarationSyntax;
            Assert.IsNotNull(generatedNamespace);
            Assert.AreEqual(fileScoped, generatedNamespace is FileScopedNamespaceDeclarationSyntax);
            Assert.AreEqual(namespaceName, generatedNamespace.Name.ToString());
            StringAssert.Contains(generatedDiff, $"+namespace {namespaceName}");
        }
        Assert.AreEqual(0, generated.Split("license stays here").Length - 1);
        Assert.AreEqual(0, generated.Split("Worker documentation").Length - 1);
        Assert.AreEqual(0, generated.Split("[System.Serializable]").Length - 1);
        StringAssert.Contains(generated, "// member-tail");
        if (!globalNamespace && !fileScoped)
            StringAssert.Contains(generated, "// type-tail\r\n}");
        await AssertRuntimeAsync(pending.Projects.Single(p => p.Name == "Contracts"), metadataName);
        CollectionAssert.AreEqual(sourceBytes, await File.ReadAllBytesAsync(sourcePath));
        CollectionAssert.AreEqual(projectBytes, await File.ReadAllBytesAsync(projectPath));
        CollectionAssert.AreEqual(unrelatedBytes, await File.ReadAllBytesAsync(unrelatedPath));
        Assert.IsFalse(File.Exists(newPath));
        var applyService = new CompositeApplyOrchestrator(WorkspaceManager, store);
        await Assert.ThrowsExactlyAsync<PreviewTokenStaleException>(() =>
            OrchestrationTools.ApplyComposite(WorkspaceExecutionGate, applyService, store,
                "unknown-preview-token", CancellationToken.None));
        CollectionAssert.AreEqual(sourceBytes, await File.ReadAllBytesAsync(sourcePath));
        Assert.IsFalse(File.Exists(newPath));
        var applyJson = await OrchestrationTools.ApplyComposite(WorkspaceExecutionGate, applyService, store,
            preview.PreviewToken, CancellationToken.None);
        var applied = JsonSerializer.Deserialize<ApplyResultDto>(applyJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.IsTrue(applied.Success, applied.Error);
        Assert.IsNull(store.PeekWorkspaceId(preview.PreviewToken));
        Assert.IsTrue(WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId) > originalVersion);
        await workspace.ReloadAsync();
        var reloaded = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        Assert.AreEqual(expected, ((CSharpParseOptions)reloaded.Projects.Single(p => p.Name == "Contracts").ParseOptions!).LanguageVersion);
        await AssertCompilesAsync(reloaded);
        await AssertRuntimeAsync(reloaded.Projects.Single(p => p.Name == "Contracts"), metadataName);
        Assert.AreEqual(generated, await File.ReadAllTextAsync(newPath));
        CollectionAssert.AreEqual(projectBytes, await File.ReadAllBytesAsync(projectPath));
        CollectionAssert.AreEqual(unrelatedBytes, await File.ReadAllBytesAsync(unrelatedPath));
        var original = await File.ReadAllTextAsync(sourcePath);
        StringAssert.Contains(original, "license stays here");
        StringAssert.Contains(original, "Worker documentation");
        StringAssert.Contains(original, "[System.Serializable]");
        StringAssert.Contains(original, "Stay()");
        Assert.IsFalse(original.Contains("Move()", StringComparison.Ordinal));
    }

    private static async Task AssertRuntimeAsync(Project project, string metadataName)
    {
        var compilation = (await project.GetCompilationAsync())!;
        var type = compilation.GetTypeByMetadataName(metadataName);
        Assert.IsNotNull(type);
        Assert.AreEqual(2, type.GetMembers().OfType<IMethodSymbol>().Count(m => m.Name is "Stay" or "Move"));
        using var bytes = new MemoryStream();
        var emit = compilation.Emit(bytes);
        Assert.IsTrue(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        bytes.Position = 0;
        var context = new System.Runtime.Loader.AssemblyLoadContext("ClassSplit_" + Guid.NewGuid().ToString("N"), true);
        try
        {
            var runtimeType = context.LoadFromStream(bytes).GetType(metadataName, throwOnError: true)!;
            var instance = Activator.CreateInstance(runtimeType)!;
            Assert.AreEqual(3, runtimeType.GetMethod("Stay")!.Invoke(instance, null));
            Assert.AreEqual(7, runtimeType.GetMethod("Move")!.Invoke(instance, null));
        }
        finally
        {
            context.Unload();
        }
    }
}
