using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

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

    private const string NestedConsumerSource = """
        public class EchoConsumer
        {
            public EchoConsumer(SampleLib.Outer.EchoService real, SampleLib.Decoy.EchoService decoy)
            {
            }
        }
        """;

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
        await AssertImplementsContractsInterfaceAsync(pending, "Contracts");
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
        await AssertImplementsContractsInterfaceAsync(pending, "Contracts");
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
        await AssertImplementsContractsInterfaceAsync(pending, "Contracts");
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

    [TestMethod]
    [DynamicData(nameof(Routes))]
    public async Task Dotted_Default_Namespace_Is_Qualified_Segment_By_Segment(string route)
    {
        await using var workspace = await LoadAsync(PlainSource, rootNamespace: "Acme.Contracts");
        var pending = await PreviewAsync(workspace, route);

        await AssertCompilesAsync(pending);
        await AssertImplementsContractsInterfaceAsync(pending, "Acme.Contracts");
        StringAssert.Contains(await SourceTextAsync(pending), ": Acme.Contracts.IEchoService");
    }

    [TestMethod]
    [DynamicData(nameof(Routes))]
    public async Task Dotted_Default_Namespace_Uses_Global_Qualification_When_An_Alias_Shadows_Its_First_Segment(string route)
    {
        const string source = """
            namespace SampleLib
            {
                using Acme = System.Text;

                public class EchoService { public int Echo(int value) => value + 1; }
            }
            """;
        await using var workspace = await LoadAsync(source, rootNamespace: "Acme.Contracts");
        var pending = await PreviewAsync(workspace, route);

        await AssertCompilesAsync(pending);
        await AssertImplementsContractsInterfaceAsync(pending, "Acme.Contracts");
        StringAssert.Contains(await SourceTextAsync(pending), "global::Acme.Contracts.IEchoService");
    }

    [TestMethod]
    [DataRow("cross-project", "Acme.Contracts")]
    [DataRow("dependency-inversion", "Acme.Contracts")]
    [DataRow("extract-and-wire", "Acme.Contracts")]
    [DataRow("cross-project", "")]
    [DataRow("dependency-inversion", "")]
    [DataRow("extract-and-wire", "")]
    public async Task Interfaces_Subfolder_Namespace_Is_Bound_Through_Every_Segment(string route, string rootNamespace)
    {
        await using var workspace = await LoadAsync(PlainSource, rootNamespace: rootNamespace, interfacesFolder: true);
        var receivingProject = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId).Projects
            .Single(project => project.Name == "Contracts");
        Assert.AreEqual(rootNamespace, receivingProject.DefaultNamespace);
        var pending = await PreviewAsync(workspace, route);

        var expectedNamespace = InterfaceFolderNamespace(rootNamespace);
        await AssertCompilesAsync(pending);
        await AssertImplementsContractsInterfaceAsync(pending, expectedNamespace);
        StringAssert.Contains(await SourceTextAsync(pending), $": {expectedNamespace}.IEchoService");
    }

    [TestMethod]
    [DynamicData(nameof(Routes))]
    public async Task Escaped_Keyword_Namespace_Segment_Binds_Through_Every_Route(string route)
    {
        // A project whose RootNamespace is written with an escaped keyword segment. (An unescaped keyword in
        // RootNamespace is a different defect in namespace emission, not in reference binding.)
        await using var workspace = await LoadAsync(PlainSource, rootNamespace: "Acme.@namespace");
        var pending = await PreviewAsync(workspace, route);

        await AssertCompilesAsync(pending);
        await AssertImplementsContractsInterfaceAsync(pending, "Acme.namespace");
        StringAssert.Contains(await SourceTextAsync(pending), ": Acme.@namespace.IEchoService");
    }

    [TestMethod]
    public async Task Same_Project_Global_Namespace_Interface_Is_Global_Qualified_When_Shadowed()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var sourcePath = workspace.GetPath("SampleLib", "EchoService.cs");
        await File.WriteAllTextAsync(sourcePath, "public class EchoService { public int Echo(int value) => value + 1; }\n");
        await File.WriteAllTextAsync(workspace.GetPath("SampleLib", "EchoHolder.cs"),
            "namespace Consumers;\n\npublic interface IEchoService { }\n\npublic class EchoHolder\n{\n    private EchoService _service = default!;\n}\n");
        await workspace.LoadAsync(CancellationToken.None);

        var preview = await InterfaceExtractionService.PreviewExtractInterfaceAsync(
            workspace.WorkspaceId, sourcePath, "EchoService", "IEchoService", null, replaceUsages: true, CancellationToken.None);
        var pending = PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;

        await AssertCompilesAsync(pending);
        var compilation = (await pending.Projects.Single(p => p.Name == "SampleLib").GetCompilationAsync())!;
        var field = compilation.GetTypeByMetadataName("Consumers.EchoHolder")!.GetMembers().OfType<IFieldSymbol>().Single();
        Assert.IsTrue(field.Type.ContainingNamespace.IsGlobalNamespace, "must bind the global IEchoService, not Consumers.IEchoService");
        var holder = (await pending.Projects.Single(p => p.Name == "SampleLib").Documents.Single(d => d.Name == "EchoHolder.cs").GetTextAsync()).ToString();
        StringAssert.Contains(holder, "global::IEchoService _service");
    }

    [TestMethod]
    public async Task Same_Project_A_Nested_Interface_Of_The_Same_Name_Is_Not_Mistaken_For_The_Generated_One()
    {
        // `Outer.IEchoService` shares name, namespace and assembly with the generated `SampleLib.IEchoService`
        // and is what the bare name binds to inside `Outer`.
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var sourcePath = workspace.GetPath("SampleLib", "EchoService.cs");
        await File.WriteAllTextAsync(sourcePath, """
            namespace SampleLib;

            public class Outer
            {
                public interface IEchoService { }

                public class EchoService { public int Echo(int value) => value + 1; }
            }
            """);
        await workspace.LoadAsync(CancellationToken.None);

        var preview = await InterfaceExtractionService.PreviewExtractInterfaceAsync(
            workspace.WorkspaceId, sourcePath, "EchoService", "IEchoService", null, replaceUsages: false, CancellationToken.None);
        var pending = PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;

        await AssertCompilesAsync(pending);
        var compilation = (await pending.Projects.Single(p => p.Name == "SampleLib").GetCompilationAsync())!;
        var implemented = compilation.GetTypeByMetadataName("SampleLib.Outer+EchoService")!.Interfaces.Single();
        Assert.IsNull(implemented.ContainingType, "must implement the generated top-level interface, not Outer.IEchoService");
    }

    [TestMethod]
    public async Task Dependency_Inversion_A_Nested_Type_Of_The_Same_Name_Is_Not_Rewritten()
    {
        // `Decoy.EchoService` shares name, namespace and assembly with the extracted `Outer.EchoService`.
        const string source = """
            namespace SampleLib
            {
                public class Outer { public class EchoService { public int Echo(int value) => value + 1; } }

                public class Decoy { public class EchoService { public int Echo(int value) => value + 2; } }
            }
            """;
        await using var workspace = await LoadAsync(source, consumerSource: NestedConsumerSource + "\n");
        var pending = await PreviewAsync(workspace, "dependency-inversion");

        await AssertCompilesAsync(pending);
        var consumer = (await pending.Projects.Single(p => p.Name == "SampleApp").Documents.Single(d => d.Name == "EchoConsumer.cs").GetTextAsync()).ToString();
        StringAssert.Contains(consumer, "Contracts.IEchoService real");
        StringAssert.Contains(consumer, "SampleLib.Decoy.EchoService decoy");
    }

    [TestMethod]
    public async Task Dependency_Inversion_Nested_Consumer_Bindings_Survive_Mcp_Apply_And_Reload()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var sourcePath = workspace.GetPath("SampleLib", "EchoService.cs");
        var consumerPath = workspace.GetPath("SampleApp", "EchoConsumer.cs");
        await PrepareNestedDependencyInversionAsync(workspace);

        await using var harness = await ProductionParityMcpHarness.CreateAsync("nested-dependency-inversion", "2026-07-28",
            services => services.AddSingleton(new SecurityOptions { SanctionedRoots = [workspace.RootPath] }));
        var manager = harness.Server.Services!.GetRequiredService<IWorkspaceManager>();
        var loaded = await CallAsync("workspace_load", new() { ["path"] = workspace.SolutionPath });
        Assert.IsTrue(loaded["isLoaded"]!.GetValue<bool>(), loaded.ToJsonString());
        var workspaceId = loaded["workspaceId"]!.GetValue<string>();

        var preview = await CallAsync("dependency_inversion_preview", new()
        {
            ["workspaceId"] = workspaceId,
            ["filePath"] = sourcePath,
            ["typeName"] = "EchoService",
            ["interfaceProjectName"] = "Contracts",
        });
        var consumerChange = preview["changes"]!.AsArray().SingleOrDefault(
            change => change!["filePath"]!.GetValue<string>() == consumerPath);
        Assert.IsNotNull(consumerChange, preview.ToJsonString());
        StringAssert.Contains(consumerChange["unifiedDiff"]!.GetValue<string>(), "Contracts.IEchoService real");

        var applied = await CallAsync("apply_with_verify", new() { ["previewToken"] = preview["previewToken"]!.GetValue<string>() });
        Assert.AreEqual("applied", applied["status"]!.GetValue<string>(), applied.ToJsonString());
        Assert.AreEqual(0, applied["preErrorCount"]!.GetValue<int>(), applied.ToJsonString());
        Assert.AreEqual(0, applied["postErrorCount"]!.GetValue<int>(), applied.ToJsonString());

        var appliedVersion = manager.GetCurrentVersion(workspaceId);
        var reloaded = await CallAsync("workspace_reload", new() { ["workspaceId"] = workspaceId, ["verbose"] = false });
        Assert.IsTrue(reloaded["isLoaded"]!.GetValue<bool>(), reloaded.ToJsonString());
        Assert.AreEqual(workspaceId, reloaded["workspaceId"]!.GetValue<string>());
        Assert.IsTrue(reloaded["workspaceVersion"]!.GetValue<int>() > appliedVersion, reloaded.ToJsonString());

        var solution = manager.GetCurrentSolution(workspaceId);
        await AssertCompilesAsync(solution);
        var app = solution.Projects.Single(p => p.Name == "SampleApp");
        var compilation = (await app.GetCompilationAsync())!;
        var generated = compilation.GetTypeByMetadataName("Contracts.IEchoService");
        var concrete = compilation.GetTypeByMetadataName("SampleLib.Outer+EchoService");
        var decoy = compilation.GetTypeByMetadataName("SampleLib.Decoy+EchoService");
        var consumer = compilation.GetTypeByMetadataName("EchoConsumer");
        Assert.IsNotNull(generated);
        Assert.AreEqual(TypeKind.Interface, generated.TypeKind);
        Assert.AreEqual("Contracts", generated.ContainingAssembly.Name);
        Assert.IsNotNull(concrete);
        Assert.IsNotNull(decoy);
        Assert.IsTrue(concrete.Interfaces.Any(type => SymbolEqualityComparer.Default.Equals(generated, type)),
            $"{concrete} must implement {generated}.");
        Assert.IsFalse(decoy.Interfaces.Any(type => SymbolEqualityComparer.Default.Equals(generated, type)),
            $"{decoy} must retain its original interface list.");
        Assert.IsNotNull(consumer);
        var parameters = consumer.InstanceConstructors.Single().Parameters;
        var realParameter = parameters.Single(p => p.Name == "real");
        var decoyParameter = parameters.Single(p => p.Name == "decoy");
        Assert.IsTrue(SymbolEqualityComparer.Default.Equals(generated, realParameter.Type),
            $"Expected real to bind to {generated}, but found {realParameter.Type}.");
        Assert.IsTrue(SymbolEqualityComparer.Default.Equals(decoy, decoyParameter.Type),
            $"Expected decoy to bind to {decoy}, but found {decoyParameter.Type}.");

        async Task<JsonObject> CallAsync(string name, Dictionary<string, object?> arguments)
        {
            var result = await harness.Client.CallToolAsync(name, arguments, cancellationToken: CancellationToken.None);
            var payload = result.Content.OfType<TextContentBlock>().Single().Text;
            Assert.IsFalse(result.IsError == true, $"{name}: {payload}");
            return JsonNode.Parse(payload)!.AsObject();
        }
    }

    private static async Task PrepareNestedDependencyInversionAsync(IsolatedWorkspaceScope workspace)
    {
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        // Both nested types have the same leaf name, namespace and assembly. The source path selects Outer.
        await File.WriteAllTextAsync(workspace.GetPath("SampleLib", "EchoService.cs"), """
            namespace SampleLib;

            public class Outer { public class EchoService { public int Echo(int value) => value + 1; } }
            """);
        await File.WriteAllTextAsync(workspace.GetPath("SampleLib", "Decoy.cs"), """
            namespace SampleLib;

            public class Decoy { public class EchoService { public int Echo(int value) => value + 2; } }
            """);
        await File.WriteAllTextAsync(workspace.GetPath("SampleApp", "EchoConsumer.cs"), NestedConsumerSource);
    }

    private static string InterfaceFolderNamespace(string rootNamespace) =>
        rootNamespace.Length == 0 ? "Interfaces" : rootNamespace + ".Interfaces";

    private async Task<IsolatedWorkspaceScope> LoadAsync(
        string source, bool? consumerReferencesContracts = null, string? rootNamespace = null, bool interfacesFolder = false,
        string? consumerSource = null)
    {
        var workspace = CreateIsolatedWorkspaceCopy();
        try
        {
            AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
            if (rootNamespace is not null)
            {
                // A realistic project default namespace: the receiving namespace is derived from it.
                var contractsProject = workspace.GetPath("Contracts", "Contracts.csproj");
                await File.WriteAllTextAsync(contractsProject, (await File.ReadAllTextAsync(contractsProject)).Replace(
                    "<ImplicitUsings>enable</ImplicitUsings>",
                    $"<ImplicitUsings>enable</ImplicitUsings>\n    <RootNamespace>{rootNamespace}</RootNamespace>"));
            }

            if (interfacesFolder)
            {
                // The conventional folder the cross-project route places (and namespaces) the interface in.
                Directory.CreateDirectory(workspace.GetPath("Contracts", "Interfaces"));
                await File.WriteAllTextAsync(workspace.GetPath("Contracts", "Interfaces", "Marker.cs"),
                    $"namespace {InterfaceFolderNamespace(rootNamespace ?? "Contracts")};\n\npublic interface IMarker {{ }}\n");
            }

            await File.WriteAllTextAsync(workspace.GetPath("SampleLib", "EchoService.cs"), source);
            await File.WriteAllTextAsync(workspace.GetPath("SampleApp", "EchoConsumer.cs"),
                consumerSource ?? "using SampleLib;\n\npublic class EchoConsumer\n{\n    public EchoConsumer(EchoService service)\n    {\n    }\n}\n");
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
            "extract-and-wire" => await new ExtractAndWireOrchestrator(WorkspaceManager, compositeStore,
                CrossProjectRefactoringService, DiRegistrationService).PreviewExtractAndWireInterfaceAsync(
                    workspace.WorkspaceId, sourcePath, "EchoService", "IEchoService", "Contracts", false, CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(route)),
        };
        var pending = route == "extract-and-wire"
            ? SerializedCompositePreview(before, compositeStore.Retrieve(preview.PreviewToken, null)!.Value.Mutations)
            : PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;
        return await ReparseSerializedDocumentsAsync(pending);
    }

    /// <summary>The source class implements the generated interface: in the Contracts assembly, in exactly these namespace segments, top-level.</summary>
    private static async Task AssertImplementsContractsInterfaceAsync(Solution pending, params string[] namespaceSegments)
    {
        var compilation = (await pending.Projects.Single(p => p.Name == "SampleLib").GetCompilationAsync())!;
        var service = compilation.GetTypeByMetadataName("SampleLib.EchoService")!;
        var generated = service.Interfaces.Single(i => i.Name == "IEchoService");
        Assert.AreEqual("Contracts", generated.ContainingAssembly.Name);
        Assert.IsNull(generated.ContainingType);
        var actual = new List<string>();
        for (var ns = generated.ContainingNamespace; !ns.IsGlobalNamespace; ns = ns.ContainingNamespace) actual.Insert(0, ns.Name);
        CollectionAssert.AreEqual(namespaceSegments.SelectMany(s => s.Split('.')).ToArray(), actual);
    }

    private static async Task<string> SourceTextAsync(Solution pending) =>
        (await pending.Projects.Single(p => p.Name == "SampleLib").Documents.Single(d => d.Name == "EchoService.cs").GetTextAsync()).ToString();
}
