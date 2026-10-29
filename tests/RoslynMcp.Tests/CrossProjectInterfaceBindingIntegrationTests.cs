using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Services;

using static RoslynMcp.Tests.TestInfrastructure.InterfaceExtractionTestContext;

namespace RoslynMcp.Tests;

/// <summary>
/// Every generated interface reference (source base list, DI consumer parameter, same-project field)
/// must bind to the generated symbol, proven by compiling the pending solution and resolving symbols,
/// never by matching text. Fixtures use only primitive signatures: the signature-dependency closure
/// is a separate row.
/// </summary>
[TestClass]
public sealed class CrossProjectInterfaceBindingIntegrationTests : IsolatedWorkspaceTestBase
{
    private const string PlainSource =
        "namespace SampleLib { public class EchoService { public int Echo(int value) => value + 1; } }";

    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    public static IEnumerable<object[]> Routes() =>
        [["cross-project"], ["dependency-inversion"], ["extract-and-wire"]];

    [TestMethod]
    [DynamicData(nameof(Routes))]
    public async Task Source_Base_List_Binds_When_Source_Namespace_Differs_From_Interface_Namespace(string route)
    {
        await using var workspace = await LoadAsync(PlainSource);
        var pending = await PreviewAsync(workspace, route);

        await AssertCompilesAsync(pending);
        await AssertImplementsContractsInterfaceAsync(pending);
    }

    [TestMethod]
    [DynamicData(nameof(Routes))]
    public async Task Source_Base_List_Uses_Global_Qualification_When_An_Alias_Shadows_The_Namespace(string route)
    {
        // The alias named `Contracts` shadows the real `Contracts` namespace inside `namespace SampleLib`.
        const string source = """
            namespace SampleLib
            {
                using Contracts = System.Text;

                public class EchoService { public int Echo(int value) => value + 1; }
            }
            """;
        await using var workspace = await LoadAsync(source);
        var pending = await PreviewAsync(workspace, route);

        await AssertCompilesAsync(pending);
        await AssertImplementsContractsInterfaceAsync(pending);
        var text = (await pending.Projects.Single(p => p.Name == "SampleLib").Documents.Single(d => d.Name == "EchoService.cs").GetTextAsync()).ToString();
        StringAssert.Contains(text, "global::Contracts.IEchoService");
    }

    [TestMethod]
    [DynamicData(nameof(Routes))]
    public async Task Source_Base_List_Does_Not_Bind_A_Same_Named_Interface_Already_In_Scope(string route)
    {
        // `IEchoService` is also declared in the source namespace: the bare name compiles, but to the wrong symbol.
        const string source = """
            namespace SampleLib
            {
                public interface IEchoService { }

                public class EchoService { public int Echo(int value) => value + 1; }
            }
            """;
        await using var workspace = await LoadAsync(source);
        var pending = await PreviewAsync(workspace, route);

        await AssertCompilesAsync(pending);
        await AssertImplementsContractsInterfaceAsync(pending);
    }

    [TestMethod]
    public async Task Dependency_Inversion_Consumer_Without_A_Reference_Gets_One_And_Binds()
    {
        await using var workspace = await LoadAsync(PlainSource, consumerReferencesContracts: false);
        var pending = await PreviewAsync(workspace, "dependency-inversion");

        await AssertCompilesAsync(pending);
        var app = pending.Projects.Single(p => p.Name == "SampleApp");
        var contracts = pending.Projects.Single(p => p.Name == "Contracts");
        Assert.AreEqual(1, app.ProjectReferences.Count(r => r.ProjectId == contracts.Id));
        var consumer = (await app.Documents.Single(d => d.Name == "EchoConsumer.cs").GetTextAsync()).ToString();
        StringAssert.Contains(consumer, "Contracts.IEchoService service");
    }

    [TestMethod]
    public async Task Dependency_Inversion_Consumer_That_Already_References_The_Interface_Project_Gets_No_Second_Reference()
    {
        await using var workspace = await LoadAsync(PlainSource, consumerReferencesContracts: true);
        var pending = await PreviewAsync(workspace, "dependency-inversion");

        await AssertCompilesAsync(pending);
        var app = pending.Projects.Single(p => p.Name == "SampleApp");
        var contracts = pending.Projects.Single(p => p.Name == "Contracts");
        Assert.AreEqual(1, app.ProjectReferences.Count(r => r.ProjectId == contracts.Id));
        Assert.AreEqual(2, app.ProjectReferences.Count());
    }

    [TestMethod]
    public async Task Same_Project_Extraction_Rewrites_Every_Field_And_Binds_Past_A_Competing_Interface_In_An_Escaped_Namespace()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var sourcePath = workspace.GetPath("SampleLib", "EchoService.cs");
        await File.WriteAllTextAsync(sourcePath,
            "namespace @namespace;\n\npublic class EchoService { public int Echo(int value) => value + 1; }\n");
        // Two fields in one document (every location must be rewritten), and a competing IEchoService in the
        // consumer's own namespace (the bare name would compile, to the wrong symbol).
        await File.WriteAllTextAsync(workspace.GetPath("SampleLib", "EchoHolder.cs"),
            "using @namespace;\n\nnamespace Consumers;\n\npublic interface IEchoService { }\n\n" +
            "public class EchoHolder\n{\n    private EchoService _first = default!;\n    private EchoService _second = default!;\n}\n");
        await workspace.LoadAsync(CancellationToken.None);

        var preview = await InterfaceExtractionService.PreviewExtractInterfaceAsync(
            workspace.WorkspaceId, sourcePath, "EchoService", "IEchoService", null, replaceUsages: true, CancellationToken.None);
        var pending = PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;

        await AssertCompilesAsync(pending);
        var compilation = (await pending.Projects.Single(p => p.Name == "SampleLib").GetCompilationAsync())!;
        var holder = compilation.GetTypeByMetadataName("Consumers.EchoHolder")!;
        foreach (var field in holder.GetMembers().OfType<IFieldSymbol>())
        {
            Assert.AreEqual("IEchoService", field.Type.Name, field.Name);
            Assert.AreEqual("namespace", field.Type.ContainingNamespace.Name, field.Name + " must bind the generated interface, not Consumers.IEchoService");
        }
    }

    private async Task<IsolatedWorkspaceScope> LoadAsync(string source, bool? consumerReferencesContracts = null)
    {
        var workspace = CreateIsolatedWorkspaceCopy();
        try
        {
            AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
            await File.WriteAllTextAsync(workspace.GetPath("SampleLib", "EchoService.cs"), source);
            await File.WriteAllTextAsync(workspace.GetPath("SampleApp", "EchoConsumer.cs"),
                "using SampleLib;\n\npublic class EchoConsumer\n{\n    public EchoConsumer(EchoService service)\n    {\n    }\n}\n");
            if (consumerReferencesContracts == true)
            {
                var appProject = workspace.GetPath("SampleApp", "SampleApp.csproj");
                await File.WriteAllTextAsync(appProject, (await File.ReadAllTextAsync(appProject)).Replace(
                    "</Project>", "  <ItemGroup>\n    <ProjectReference Include=\"..\\Contracts\\Contracts.csproj\" />\n  </ItemGroup>\n</Project>"));
            }

            await workspace.LoadAsync(CancellationToken.None);
            return workspace;
        }
        catch
        {
            await workspace.DisposeAsync();
            throw;
        }
    }

    private async Task<Solution> PreviewAsync(IsolatedWorkspaceScope workspace, string route)
    {
        var sourcePath = workspace.GetPath("SampleLib", "EchoService.cs");
        var before = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var compositeStore = new CompositePreviewStore();
        RefactoringPreviewDto preview = route switch
        {
            "cross-project" => await CrossProjectRefactoringService.PreviewExtractInterfaceAsync(
                workspace.WorkspaceId, sourcePath, "EchoService", "IEchoService", "Contracts", CancellationToken.None),
            "dependency-inversion" => await CrossProjectRefactoringService.PreviewDependencyInversionAsync(
                workspace.WorkspaceId, sourcePath, "EchoService", "IEchoService", "Contracts", CancellationToken.None),
            "extract-and-wire" => await new ExtractAndWireOrchestrator(WorkspaceManager, compositeStore, PreviewStore,
                CrossProjectRefactoringService, DiRegistrationService).PreviewExtractAndWireInterfaceAsync(
                    workspace.WorkspaceId, sourcePath, "EchoService", "IEchoService", "Contracts", false, CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(route)),
        };
        var pending = route == "extract-and-wire"
            ? SerializedCompositePreview(before, compositeStore.Retrieve(preview.PreviewToken)!.Value.Mutations)
            : PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;
        return await ReparseSerializedDocumentsAsync(pending);
    }

    private static async Task AssertImplementsContractsInterfaceAsync(Solution pending)
    {
        var compilation = (await pending.Projects.Single(p => p.Name == "SampleLib").GetCompilationAsync())!;
        var service = compilation.GetTypeByMetadataName("SampleLib.EchoService")!;
        var generated = service.Interfaces.Single(i => i.Name == "IEchoService");
        Assert.AreEqual("Contracts", generated.ContainingAssembly.Name);
        Assert.AreEqual("Contracts", generated.ContainingNamespace.ToDisplayString());
    }
}
