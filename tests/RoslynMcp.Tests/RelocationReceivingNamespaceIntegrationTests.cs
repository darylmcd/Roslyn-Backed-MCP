using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Roslyn.Services;

using static RoslynMcp.Tests.TestInfrastructure.InterfaceExtractionTestContext;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class RelocationReceivingNamespaceIntegrationTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    [DataRow("7.3")]
    [DataRow("9.0")]
    [DataRow("10.0")]
    [DataRow("14.0")]
    public async Task New_File_Uses_Source_Project_Namespace_Policy_Through_Preview_Apply_And_Reload(string version)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "Receiving", "net10.0");
        var projectPath = workspace.GetPath("Receiving", "Receiving.csproj");
        var project = XDocument.Load(projectPath);
        project.Root!.Add(new XElement("PropertyGroup", new XElement("LangVersion", version),
            new XElement("ImplicitUsings", "disable"), new XElement("Nullable", "disable")));
        project.Save(projectPath);
        var projectBytes = await File.ReadAllBytesAsync(projectPath);
        var sourcePath = workspace.GetPath("Receiving", "Source.cs");
        const string source = "// file-header\r\nnamespace Origin\r\n{\r\n    /// <summary>Moved type.</summary>\r\n    public class Service { public int Echo(int value) => value + 1; } // tail\r\n    public class Kept { }\r\n}\r\n// file-end\r\n";
        await File.WriteAllTextAsync(sourcePath, source);
        var sourceBytes = await File.ReadAllBytesAsync(sourcePath);
        var consumerPath = workspace.GetPath("Receiving", "Consumer.cs");
        const string consumer = "using Origin;\r\nnamespace Client { public class Consumer { public int Call() => new Service().Echo(7); } }\r\n";
        await File.WriteAllTextAsync(consumerPath, consumer);
        var consumerBytes = await File.ReadAllBytesAsync(consumerPath);
        await workspace.LoadAsync();
        var before = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var receiving = before.Projects.Single(p => p.Name == "Receiving");
        Assert.IsTrue(LanguageVersionFacts.TryParse(version, out var expected));
        Assert.AreEqual(expected, ((CSharpParseOptions)receiving.ParseOptions!).LanguageVersion);
        await AssertCompilesAsync(before);
        var originalVersion = WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId);
        var destinationPath = workspace.GetPath("Receiving", "Moved.cs");
        var service = new NamespaceRelocationService(WorkspaceManager, PreviewStore,
            NullLogger<NamespaceRelocationService>.Instance);
        var preview = await service.PreviewChangeTypeNamespaceAsync(workspace.WorkspaceId,
            "Service", "Origin", "Destination", destinationPath, CancellationToken.None);
        var pending = await ReparseSerializedDocumentsAsync(PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution);
        await AssertCompilesAsync(pending);
        await AssertOutputAndRuntimeAsync(pending, version, destinationPath);
        CollectionAssert.AreEqual(projectBytes, await File.ReadAllBytesAsync(projectPath));
        CollectionAssert.AreEqual(sourceBytes, await File.ReadAllBytesAsync(sourcePath));
        CollectionAssert.AreEqual(consumerBytes, await File.ReadAllBytesAsync(consumerPath));
        Assert.IsFalse(File.Exists(destinationPath));
        Assert.AreSame(before, WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        Assert.AreEqual(originalVersion, WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId));

        var result = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken,
            "relocation_namespace_policy", CancellationToken.None);
        Assert.IsTrue(result.Success, result.Error);
        await workspace.ReloadAsync();
        var applied = await ReparseSerializedDocumentsAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        Assert.AreEqual(expected, ((CSharpParseOptions)applied.Projects.Single(p => p.Name == "Receiving").ParseOptions!).LanguageVersion);
        await AssertCompilesAsync(applied);
        await AssertOutputAndRuntimeAsync(applied, version, destinationPath);
        Assert.AreEqual((await applied.Projects.Single(p => p.Name == "Receiving").Documents.Single(d => d.Name == "Moved.cs")
            .GetTextAsync()).ToString(), await File.ReadAllTextAsync(destinationPath));
        CollectionAssert.AreEqual(projectBytes, await File.ReadAllBytesAsync(projectPath));
    }

    private static async Task AssertOutputAndRuntimeAsync(Solution solution, string version, string destinationPath)
    {
        var receiving = solution.Projects.Single(p => p.Name == "Receiving");
        var moved = receiving.Documents.Single(d => d.FilePath == destinationPath);
        var root = (CompilationUnitSyntax)(await moved.GetSyntaxRootAsync())!;
        Assert.AreEqual(version is "7.3" or "9.0", root.Members.Single() is NamespaceDeclarationSyntax);
        StringAssert.Contains((await moved.GetTextAsync()).ToString(), "// file-header");
        StringAssert.Contains((await moved.GetTextAsync()).ToString(), "/// <summary>Moved type.</summary>");
        StringAssert.Contains((await moved.GetTextAsync()).ToString(), "// file-end");
        Assert.AreEqual("Destination.Service", (await moved.GetSemanticModelAsync())!
            .GetDeclaredSymbol(root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single())!.ToDisplayString());
        var compilation = (await receiving.GetCompilationAsync())!;
        Assert.IsNotNull(compilation.GetTypeByMetadataName("Origin.Kept"));
        var consumer = receiving.Documents.Single(d => d.Name == "Consumer.cs");
        var consumerRoot = (await consumer.GetSyntaxRootAsync())!;
        var creation = consumerRoot.DescendantNodes().OfType<ObjectCreationExpressionSyntax>().Single();
        Assert.AreEqual("Destination.Service", (await consumer.GetSemanticModelAsync())!
            .GetTypeInfo(creation).Type!.ToDisplayString());
        using var bytes = new MemoryStream();
        var emitted = compilation.Emit(bytes);
        Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        bytes.Position = 0;
        var context = new System.Runtime.Loader.AssemblyLoadContext("Relocation_" + Guid.NewGuid().ToString("N"), true);
        try
        {
            var assembly = context.LoadFromStream(bytes);
            var instance = Activator.CreateInstance(assembly.GetType("Client.Consumer")!)!;
            Assert.AreEqual(8, assembly.GetType("Client.Consumer")!.GetMethod("Call")!.Invoke(instance, null));
        }
        finally
        {
            context.Unload();
        }
    }
}
