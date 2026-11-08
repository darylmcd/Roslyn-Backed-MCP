using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;
using static RoslynMcp.Tests.TestInfrastructure.InterfaceExtractionTestContext;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class InterfacePreviewCompilationGateTests : InterfaceRouteTestBase
{
    private const string PlainSource = "namespace SampleLib; public class RefService { public string Get() => string.Empty; }";
    private const string PlainConsumer = "namespace Consumer; public class RefConsumer { public RefConsumer(SampleLib.RefService service) { } }";

    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    [DataRow("cross-project")]
    [DataRow("dependency-inversion")]
    [DataRow("extract-and-wire")]
    public async Task Source_Only_Metadata_Signature_Refuses_Before_Preview(string route)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var sourcePath = await PrepareInterfaceRouteFixtureAsync(workspace, "RefService.cs", PlainSource,
            "RefConsumer.cs", PlainConsumer);
        var externalDirectory = workspace.GetPath("External");
        Directory.CreateDirectory(externalDirectory);
        var assemblyPath = Path.Combine(externalDirectory, "External.dll");
        var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var compilation = CSharpCompilation.Create("External",
            [CSharpSyntaxTree.ParseText("namespace External; public static class Constants { public const int Value = 1; }")],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "System.Runtime.dll"))],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        await using (var image = File.Create(assemblyPath))
            Assert.IsTrue(compilation.Emit(image).Success);
        var projectPath = workspace.GetPath("SampleLib", "SampleLib.csproj");
        var xml = XDocument.Load(projectPath);
        xml.Root!.Add(new XElement("ItemGroup", new XElement("Reference", new XAttribute("Include", "External"),
            new XElement("HintPath", "../External/External.dll"))));
        xml.Save(projectPath);
        await File.WriteAllTextAsync(sourcePath,
            "namespace SampleLib; public class RefService { public void Use([System.Runtime.InteropServices.DefaultParameterValue(External.Constants.Value)] int value) { } }");
        await workspace.ReloadAsync();
        await AssertCompilesAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));

        var error = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            PreviewInterfaceRouteAsync(route, workspace.WorkspaceId, sourcePath, "RefService", new CompositePreviewStore()));
        StringAssert.Contains(error.Message, "Contracts");
        StringAssert.Contains(error.Message, "CS0246");
        Assert.IsFalse(File.Exists(workspace.GetPath("Contracts", "IRefService.cs")));
    }

    [TestMethod]
    public async Task Dependency_Inversion_Refuses_New_Consumer_Method_Error()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var sourcePath = await PrepareInterfaceRouteFixtureAsync(workspace, "RefService.cs",
            "namespace SampleLib; public class Base { public void Existing() { } } public class RefService : Base { public void Extracted() { } }",
            "RefConsumer.cs", "namespace Consumer; public class RefConsumer { public RefConsumer(SampleLib.RefService service) { service.Existing(); } }");
        var error = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            PreviewInterfaceRouteAsync("dependency-inversion", workspace.WorkspaceId, sourcePath, "RefService", new CompositePreviewStore()));
        StringAssert.Contains(error.Message, "CS1061");
        StringAssert.Contains(error.Message, "Consumer");
    }

    [TestMethod]
    public async Task Extract_And_Wire_Refuses_Invalid_Final_Registration()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var sourcePath = await PrepareInterfaceRouteFixtureAsync(workspace, "RefService.cs", PlainSource,
            "RefConsumer.cs", "using SampleLib; namespace Consumer; public class RefConsumer { public RefConsumer(RefService service) { } } public class Registry { public void AddSingleton<T>() { } public void Configure() { AddSingleton<RefService>(); } }");
        var error = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            PreviewInterfaceRouteAsync("extract-and-wire", workspace.WorkspaceId, sourcePath, "RefService", new CompositePreviewStore()));
        StringAssert.Contains(error.Message, "CS0305");
    }

    [TestMethod]
    public async Task Extract_And_Wire_Composes_Registration_In_Source_File()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var source = "using Contracts; namespace SampleLib; public class RefService { public string Get() => string.Empty; } public class Registry { public void AddSingleton<TService, TImplementation>() { } public void Configure() { AddSingleton<RefService>(); } public void AddSingleton<T>() { } }";
        var sourcePath = await PrepareInterfaceRouteFixtureAsync(workspace, "RefService.cs", PlainSource,
            "RefConsumer.cs", PlainConsumer);
        var sourceProjectPath = workspace.GetPath("SampleLib", "SampleLib.csproj");
        var sourceProjectXml = XDocument.Load(sourceProjectPath);
        sourceProjectXml.Root!.Add(new XElement("ItemGroup", new XElement("ProjectReference",
            new XAttribute("Include", "../Contracts/Contracts.csproj"))));
        sourceProjectXml.Save(sourceProjectPath);
        await File.WriteAllTextAsync(workspace.GetPath("Contracts", "Anchor.cs"),
            "namespace Contracts; public sealed class Anchor { }");
        await File.WriteAllTextAsync(sourcePath, source);
        await workspace.ReloadAsync();
        await AssertCompilesAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        var stores = new CompositePreviewStore();
        var preview = await PreviewInterfaceRouteAsync("extract-and-wire", workspace.WorkspaceId, sourcePath, "RefService", stores);
        Assert.AreEqual(source, await File.ReadAllTextAsync(sourcePath));
        var mutation = stores.Retrieve(preview.PreviewToken, null)!.Value.Mutations.Single(item => item.FilePath == sourcePath);
        StringAssert.Contains(mutation.UpdatedContent!, "class RefService : IRefService");
        StringAssert.Contains(mutation.UpdatedContent!, "AddSingleton<global::Contracts.IRefService, RefService>()");
        var sourceDiff = preview.Changes.Single(change => change.FilePath == sourcePath).UnifiedDiff;
        StringAssert.Contains(sourceDiff, "class RefService : IRefService");
        StringAssert.Contains(sourceDiff, "AddSingleton<global::Contracts.IRefService, RefService>()");
        var apply = await new CompositeApplyOrchestrator(WorkspaceManager, stores, undoService: UndoService)
            .ApplyCompositeAsync(preview.PreviewToken, CompositeAdmission.AdmitAll, CancellationToken.None);
        Assert.IsTrue(apply.Success, apply.Error);
        await workspace.ReloadAsync();
        await AssertCompilesAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        var sourceProject = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId).Projects.Single(project => project.Name == "SampleLib");
        var sourceCompilation = (await sourceProject.GetCompilationAsync())!;
        var serviceType = sourceCompilation.GetTypeByMetadataName("SampleLib.RefService")!;
        var interfaceType = sourceCompilation.GetTypeByMetadataName("Contracts.IRefService")!;
        Assert.IsTrue(serviceType.AllInterfaces.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate, interfaceType)));
    }

    [TestMethod]
    public async Task Extract_And_Wire_Reconciles_Linked_Source_Document()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        const string source = "namespace SampleLib; public class RefService { public string Get() => string.Empty; } public class Registry { public void AddSingleton<T>() { } public void AddSingleton<TService, TImplementation>() { } public void Configure() { AddSingleton<RefService>(); } }";
        var sourcePath = await PrepareInterfaceRouteFixtureAsync(workspace, "RefService.cs", source,
            "RefConsumer.cs", PlainConsumer);
        AddLinkedSourceProject(workspace, "Linked", linkedReferencesContracts: true);
        await workspace.ReloadAsync();
        await AssertCompilesAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        var stores = new CompositePreviewStore();
        var preview = await PreviewInterfaceRouteAsync("extract-and-wire", workspace.WorkspaceId, sourcePath, "RefService", stores);
        var mutation = stores.Retrieve(preview.PreviewToken, null)!.Value.Mutations.Single(item => item.FilePath == sourcePath);
        StringAssert.Contains(mutation.UpdatedContent!, "class RefService : Contracts.IRefService");
        StringAssert.Contains(mutation.UpdatedContent!, "AddSingleton<global::Contracts.IRefService, RefService>()");
        Assert.AreEqual(source, await File.ReadAllTextAsync(sourcePath));
        var apply = await new CompositeApplyOrchestrator(WorkspaceManager, stores, undoService: UndoService)
            .ApplyCompositeAsync(preview.PreviewToken, CompositeAdmission.AdmitAll, CancellationToken.None);
        Assert.IsTrue(apply.Success, apply.Error);
        await workspace.ReloadAsync();
        await AssertCompilesAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
    }

    [TestMethod]
    public async Task Extract_And_Wire_Without_Di_Updates_Every_Linked_Owner()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var sourcePath = await PrepareInterfaceRouteFixtureAsync(workspace, "RefService.cs", PlainSource,
            "RefConsumer.cs", PlainConsumer);
        var linkedProjectPath = AddLinkedSourceProject(workspace, "Linked");
        await workspace.ReloadAsync();
        await AssertCompilesAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        var stores = new CompositePreviewStore();
        var orchestrator = new ExtractAndWireOrchestrator(WorkspaceManager, stores,
            CrossProjectRefactoringService, DiRegistrationService);

        var preview = await orchestrator.PreviewExtractAndWireInterfaceAsync(workspace.WorkspaceId, sourcePath, "RefService",
            "IRefService", "Contracts", false, CancellationToken.None);
        var mutations = stores.Retrieve(preview.PreviewToken, null)!.Value.Mutations;
        Assert.AreEqual(1, mutations.Count(mutation => mutation.FilePath == sourcePath));
        Assert.IsTrue(mutations.Any(mutation => mutation.FilePath == workspace.GetPath("SampleLib", "SampleLib.csproj")));
        Assert.IsTrue(mutations.Any(mutation => mutation.FilePath == linkedProjectPath));
        Assert.AreEqual(PlainSource, await File.ReadAllTextAsync(sourcePath));
        var apply = await new CompositeApplyOrchestrator(WorkspaceManager, stores, undoService: UndoService)
            .ApplyCompositeAsync(preview.PreviewToken, CompositeAdmission.AdmitAll, CancellationToken.None);
        Assert.IsTrue(apply.Success, apply.Error);
        await workspace.ReloadAsync();
        var applied = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        await AssertCompilesAsync(applied);
        var contractsId = applied.Projects.Single(project => project.Name == "Contracts").Id;
        foreach (var owner in new[] { "SampleLib", "Linked" })
            Assert.IsTrue(applied.Projects.Single(project => project.Name == owner)
                .ProjectReferences.Any(reference => reference.ProjectId == contractsId), owner);
    }

    [TestMethod]
    public async Task Extract_And_Wire_Refuses_Cyclic_Linked_Owner()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var sourcePath = await PrepareInterfaceRouteFixtureAsync(workspace, "RefService.cs", PlainSource,
            "RefConsumer.cs", PlainConsumer);
        AddLinkedSourceProject(workspace, "ZLinked", contractsReferencesLinked: true);
        await workspace.ReloadAsync();
        await AssertCompilesAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        Assert.AreEqual("SampleLib", RoslynMcp.Roslyn.Helpers.SymbolResolver
            .FindDocument(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId), sourcePath)!.Project.Name);
        var stores = new CompositePreviewStore();
        var orchestrator = new ExtractAndWireOrchestrator(WorkspaceManager, stores,
            CrossProjectRefactoringService, DiRegistrationService);

        var error = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            orchestrator.PreviewExtractAndWireInterfaceAsync(workspace.WorkspaceId, sourcePath, "RefService",
                "IRefService", "Contracts", false, CancellationToken.None));
        StringAssert.Contains(error.Message, "ZLinked");
        StringAssert.Contains(error.Message, "circular dependency");
        Assert.AreEqual(PlainSource, await File.ReadAllTextAsync(sourcePath));
    }

    [TestMethod]
    [DataRow("cross-project")]
    [DataRow("dependency-inversion")]
    public async Task Direct_Routes_Apply_Linked_Owner_References(string route)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var sourcePath = await PrepareInterfaceRouteFixtureAsync(workspace, "RefService.cs", PlainSource,
            "RefConsumer.cs", PlainConsumer);
        AddLinkedSourceProject(workspace, "Linked");
        await workspace.ReloadAsync();
        await AssertCompilesAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));

        var preview = await PreviewInterfaceRouteAsync(route, workspace.WorkspaceId, sourcePath, "RefService", new CompositePreviewStore());
        Assert.AreEqual(PlainSource, await File.ReadAllTextAsync(sourcePath));
        var pending = PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;
        await AssertCompilesAsync(pending);
        Assert.AreEqual(1, preview.Changes.Count(change => change.FilePath == sourcePath));
        var contractsId = pending.Projects.Single(project => project.Name == "Contracts").Id;
        foreach (var owner in new[] { "SampleLib", "Linked" })
            Assert.IsTrue(pending.Projects.Single(project => project.Name == owner)
                .ProjectReferences.Any(reference => reference.ProjectId == contractsId), owner);
        var apply = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "linked_apply", CancellationToken.None);
        Assert.IsTrue(apply.Success, apply.Error);
        await workspace.ReloadAsync();
        await AssertCompilesAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
    }

    [TestMethod]
    [DataRow("cross-project")]
    [DataRow("dependency-inversion")]
    public async Task Direct_Routes_Refuse_Cyclic_Linked_Owner(string route)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var sourcePath = await PrepareInterfaceRouteFixtureAsync(workspace, "RefService.cs", PlainSource,
            "RefConsumer.cs", PlainConsumer);
        AddLinkedSourceProject(workspace, "ZLinked", contractsReferencesLinked: true);
        await workspace.ReloadAsync();
        await AssertCompilesAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        Assert.AreEqual("SampleLib", RoslynMcp.Roslyn.Helpers.SymbolResolver
            .FindDocument(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId), sourcePath)!.Project.Name);

        var error = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            PreviewInterfaceRouteAsync(route, workspace.WorkspaceId, sourcePath, "RefService", new CompositePreviewStore()));
        StringAssert.Contains(error.Message, "ZLinked");
        StringAssert.Contains(error.Message, "circular dependency");
        Assert.AreEqual(PlainSource, await File.ReadAllTextAsync(sourcePath));
    }

    [TestMethod]
    public async Task Extract_And_Wire_Refuses_Unmapped_Registration_File()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var sourcePath = await PrepareInterfaceRouteFixtureAsync(workspace, "RefService.cs", PlainSource,
            "RefConsumer.cs", PlainConsumer);
        var unmappedPath = workspace.GetPath("Consumer", "Unmapped.cs");
        const string registration = "public class Registry { public void AddSingleton<T>() { } public void Configure() { AddSingleton<RefService>(); } }";
        await File.WriteAllTextAsync(unmappedPath, registration);
        var stores = new CompositePreviewStore();
        var orchestrator = new ExtractAndWireOrchestrator(WorkspaceManager, stores,
            CrossProjectRefactoringService, new UnmappedRegistrationService(unmappedPath));

        var error = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            orchestrator.PreviewExtractAndWireInterfaceAsync(workspace.WorkspaceId, sourcePath, "RefService",
                "IRefService", "Contracts", true, CancellationToken.None));
        StringAssert.Contains(error.Message, "not mapped");
        Assert.AreEqual(registration, await File.ReadAllTextAsync(unmappedPath));
    }

    [TestMethod]
    public async Task Preexisting_Unrelated_Compiler_Error_Does_Not_Block_Preview()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var sourcePath = await PrepareInterfaceRouteFixtureAsync(workspace, "RefService.cs", PlainSource,
            "RefConsumer.cs", PlainConsumer);
        await File.WriteAllTextAsync(workspace.GetPath("SampleLib", "Broken.cs"),
            "namespace SampleLib; public sealed class Broken { MissingType Value = null!; }");
        var projectPath = workspace.GetPath("SampleLib", "SampleLib.csproj");
        var xml = XDocument.Load(projectPath);
        xml.Root!.Add(new XElement("ItemGroup", new XElement("Compile", new XAttribute("Include", "Broken.cs"))));
        xml.Save(projectPath);
        await workspace.ReloadAsync();
        var baseline = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        Assert.IsTrue((await baseline.Projects.Single(project => project.Name == "SampleLib").GetCompilationAsync())!
            .GetDiagnostics().Any(diagnostic => diagnostic.Id == "CS0246"));

        var preview = await PreviewInterfaceRouteAsync("cross-project", workspace.WorkspaceId,
            sourcePath, "RefService", new CompositePreviewStore());
        var pending = PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;
        Assert.IsNotNull(pending.Projects.Single(project => project.Name == "Contracts")
            .Documents.Single(document => document.Name == "IRefService.cs"));
    }

    private static string AddLinkedSourceProject(IsolatedWorkspaceScope workspace, string projectName,
        bool linkedReferencesContracts = false, bool contractsReferencesLinked = false)
    {
        AddProjectToCopiedSolution(workspace.RootPath, projectName, "net10.0");
        var linkedProjectPath = workspace.GetPath(projectName, projectName + ".csproj");
        var xml = XDocument.Load(linkedProjectPath);
        var items = new List<XElement>
        {
            new("Compile", new XAttribute("Include", "../SampleLib/RefService.cs"),
                new XAttribute("Link", "LinkedRefService.cs")),
        };
        if (linkedReferencesContracts)
            items.Add(new XElement("ProjectReference", new XAttribute("Include", "../Contracts/Contracts.csproj")));
        xml.Root!.Add(new XElement("ItemGroup", items));
        xml.Save(linkedProjectPath);
        if (contractsReferencesLinked)
        {
            var contractsProjectPath = workspace.GetPath("Contracts", "Contracts.csproj");
            var contractsXml = XDocument.Load(contractsProjectPath);
            contractsXml.Root!.Add(new XElement("ItemGroup", new XElement("ProjectReference",
                new XAttribute("Include", "../" + projectName + "/" + projectName + ".csproj"))));
            contractsXml.Save(contractsProjectPath);
        }
        return linkedProjectPath;
    }

    private sealed class UnmappedRegistrationService(string path) : IDiRegistrationService
    {
        private readonly DiRegistrationDto[] _registrations =
            [new("RefService", "RefService", "Singleton", path, 1, "AddSingleton")];

        public Task<IReadOnlyList<DiRegistrationDto>> GetDiRegistrationsAsync(
            string workspaceId, string? projectFilter, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DiRegistrationDto>>(_registrations);

        public Task<DiRegistrationScanResult> GetDiRegistrationsWithOverridesAsync(
            string workspaceId, string? projectFilter, CancellationToken ct) =>
            Task.FromResult(new DiRegistrationScanResult(_registrations, []));
    }
}
