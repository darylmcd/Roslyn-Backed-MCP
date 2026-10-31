using System.Runtime.Loader;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class SplitServiceReceivingNamespaceIntegrationTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    [DataRow("7.3", false)]
    [DataRow("9.0", false)]
    [DataRow("10.0", false)]
    [DataRow("14.0", false)]
    [DataRow("7.3", true)]
    [DataRow("9.0", true)]
    public async Task Partition_Uses_Native_Receiving_Policy_Through_Preview_Apply_And_Reload(string version, bool global)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        var projectPath = workspace.GetPath("Contracts", "Contracts.csproj");
        var projectXml = XDocument.Load(projectPath);
        projectXml.Root!.Add(new XElement("PropertyGroup",
            new XElement("LangVersion", version), new XElement("Nullable", "disable"),
            new XElement("ImplicitUsings", "disable"), new XElement("TreatWarningsAsErrors", "true")));
        projectXml.Save(projectPath);
        var projectBytes = await File.ReadAllBytesAsync(projectPath);
        var sourcePath = workspace.GetPath("Contracts", "Calculator.cs");
        var source = global
            ? "public class Calculator { public int Add(int value) => value + 1; }"
            : "namespace Contracts { public class Calculator { public int Add(int value) => value + 1; } }";
        await File.WriteAllTextAsync(sourcePath, source);
        var sourceBytes = await File.ReadAllBytesAsync(sourcePath);
        var unrelatedPath = workspace.GetPath("Contracts", "Unchanged.cs");
        await File.WriteAllTextAsync(unrelatedPath,
            "namespace Contracts { public class Unchanged { public int Value() => 7; } }");
        var unrelatedBytes = await File.ReadAllBytesAsync(unrelatedPath);
        await workspace.LoadAsync();
        var before = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var receiving = before.Projects.Single(p => p.Name == "Contracts");
        Assert.IsTrue(LanguageVersionFacts.TryParse(version, out var expected));
        Assert.AreEqual(expected, ((CSharpParseOptions)receiving.ParseOptions!).LanguageVersion);
        await AssertContractsCompilesAsync(before);
        var originalVersion = WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId);

        var store = new CompositePreviewStore();
        var service = new SymbolRefactorService(WorkspaceManager, RefactoringService, EditService,
            new RestructureService(WorkspaceManager, PreviewStore), store, DiRegistrationService);
        using var gate = new WorkspaceExecutionGate(new ExecutionGateOptions(), WorkspaceManager);
        var previewJson = await SymbolRefactorTools.PreviewSplitServiceWithDi(gate, service,
            workspace.WorkspaceId, sourcePath, "Calculator",
            [new SplitServicePartition("CalculatorPart", ["Add"])], null, CancellationToken.None);
        using var preview = JsonDocument.Parse(previewJson);
        var token = preview.RootElement.GetProperty("previewToken").GetString()!;
        var entry = store.Retrieve(token);
        Assert.IsNotNull(entry);
        Assert.AreEqual(workspace.WorkspaceId, entry.Value.WorkspaceId);
        Assert.AreEqual(originalVersion, entry.Value.WorkspaceVersion);
        var partitionPath = workspace.GetPath("Contracts", "CalculatorPart.cs");
        var partitionText = entry.Value.Mutations.Single(m => m.FilePath == partitionPath).UpdatedContent!;
        var parsed = CSharpSyntaxTree.ParseText(partitionText, (CSharpParseOptions)receiving.ParseOptions!, partitionPath);
        var root = (CompilationUnitSyntax)await parsed.GetRootAsync();
        var parseErrors = parsed.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.AreEqual(0, parseErrors.Length, string.Join(Environment.NewLine, parseErrors.Select(d => d.ToString())));
        var pending = before;
        foreach (var mutation in entry.Value.Mutations)
        {
            var existing = pending.Projects.SelectMany(p => p.Documents).SingleOrDefault(d => d.FilePath == mutation.FilePath);
            pending = existing is null
                ? pending.AddDocument(DocumentId.CreateNewId(receiving.Id), Path.GetFileName(mutation.FilePath),
                    Microsoft.CodeAnalysis.Text.SourceText.From(mutation.UpdatedContent!), filePath: mutation.FilePath)
                : pending.WithDocumentText(existing.Id, Microsoft.CodeAnalysis.Text.SourceText.From(mutation.UpdatedContent!));
        }
        await AssertContractsCompilesAsync(pending);
        Assert.AreEqual(global, root.Members.Single() is ClassDeclarationSyntax);
        if (!global) Assert.AreEqual(version is "7.3" or "9.0", root.Members.Single() is NamespaceDeclarationSyntax);
        await AssertRuntimeAsync(pending, global);
        CollectionAssert.AreEqual(sourceBytes, await File.ReadAllBytesAsync(sourcePath));
        CollectionAssert.AreEqual(projectBytes, await File.ReadAllBytesAsync(projectPath));
        CollectionAssert.AreEqual(unrelatedBytes, await File.ReadAllBytesAsync(unrelatedPath));
        Assert.IsFalse(File.Exists(partitionPath));
        Assert.AreEqual(originalVersion, WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId));
        Assert.AreSame(before, WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));

        var orchestrator = new CompositeApplyOrchestrator(WorkspaceManager, store, undoService: UndoService);
        var applyJson = await OrchestrationTools.ApplyComposite(gate, orchestrator, store, token, CancellationToken.None);
        using var result = JsonDocument.Parse(applyJson);
        Assert.IsTrue(result.RootElement.GetProperty("success").GetBoolean(), applyJson);
        await workspace.ReloadAsync();
        var applied = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        Assert.AreEqual(expected, ((CSharpParseOptions)applied.Projects.Single(p => p.Name == "Contracts").ParseOptions!).LanguageVersion);
        await AssertContractsCompilesAsync(applied);
        await AssertRuntimeAsync(applied, global);
        Assert.AreEqual(partitionText, await File.ReadAllTextAsync(partitionPath));
        CollectionAssert.AreEqual(projectBytes, await File.ReadAllBytesAsync(projectPath));
        CollectionAssert.AreEqual(unrelatedBytes, await File.ReadAllBytesAsync(unrelatedPath));
        Assert.IsFalse((await new CompositeApplyOrchestrator(WorkspaceManager, store)
            .ApplyCompositeAsync(token, CancellationToken.None)).Success);
    }

    private static async Task AssertContractsCompilesAsync(Solution solution)
    {
        var compilation = (await solution.Projects.Single(p => p.Name == "Contracts").GetCompilationAsync())!;
        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.AreEqual(0, errors.Length, string.Join(Environment.NewLine, errors.Select(d => d.ToString())));
        Assert.IsNotNull(compilation.GetTypeByMetadataName("Contracts.Unchanged"));
    }

    private static async Task AssertRuntimeAsync(Solution solution, bool global)
    {
        var compilation = (await solution.Projects.Single(p => p.Name == "Contracts").GetCompilationAsync())!;
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.IsTrue(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        stream.Position = 0;
        var loadContext = new AssemblyLoadContext("SplitService_" + Guid.NewGuid().ToString("N"), true);
        try
        {
            var assembly = loadContext.LoadFromStream(stream);
            var prefix = global ? "" : "Contracts.";
            var part = Activator.CreateInstance(assembly.GetType(prefix + "CalculatorPart", true)!)!;
            var facade = Activator.CreateInstance(assembly.GetType(prefix + "Calculator", true)!, part)!;
            Assert.AreEqual(42, facade.GetType().GetMethod("Add")!.Invoke(facade, [41]));
            var unrelated = Activator.CreateInstance(assembly.GetType("Contracts.Unchanged", true)!)!;
            Assert.AreEqual(7, unrelated.GetType().GetMethod("Value")!.Invoke(unrelated, null));
        }
        finally
        {
            loadContext.Unload();
        }
    }
}
