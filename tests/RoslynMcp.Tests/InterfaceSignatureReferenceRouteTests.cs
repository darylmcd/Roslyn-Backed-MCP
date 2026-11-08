using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;
using static RoslynMcp.Tests.TestInfrastructure.InterfaceExtractionTestContext;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class InterfaceSignatureReferenceRouteTests : InterfaceRouteTestBase
{
    private const string SharedSource = """
        namespace Shared;
        public sealed class Shape { }
        public sealed class Box<T> { }
        public interface IMarker { }
        public delegate void ChangedHandler(Shape shape);
        """;

    private const string Source = """
        using Shared;
        namespace SampleLib;
        public sealed class RefService
        {
            public Box<Shape> Get<T>(T value) where T : IMarker => new();
            public Shape Current => new();
            public event ChangedHandler? Changed { add { } remove { } }
        }
        """;

    private const string Consumer = """
        namespace Consumer;
        public sealed class RefConsumer
        {
            public RefConsumer(SampleLib.RefService service) { _ = service; }
        }
        """;

    private const string AliasedSource = """
        extern alias ext;
        namespace SampleLib;
        public sealed class RefService
        {
            public ext::Shared.Box<ext::Shared.Shape> Get() => new();
        }
        """;

    private const string IndependentSource = """
        using Shared;
        namespace SampleLib;
        public sealed class RefService
        {
            public Box<GenericArg.Shape> Get<T>(T value) where T : Constraint.IMarker => new();
            public PropertyOwner.Shape Current => new();
            public event EventOwner.ChangedHandler? Changed { add { } remove { } }
        }
        """;

    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    [DataRow("cross-project")]
    [DataRow("dependency-inversion")]
    [DataRow("extract-and-wire")]
    public async Task Independent_Signature_Owner_Gets_Reference_And_Compiles(string route)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var sourcePath = await PrepareInterfaceRouteFixtureAsync(workspace, "RefService.cs", Source,
            "RefConsumer.cs", Consumer, SharedSource);
        foreach (var (name, source) in new[]
        {
            ("GenericArg", "namespace GenericArg; public sealed class Shape { }"),
            ("Constraint", "namespace Constraint; public interface IMarker { }"),
            ("PropertyOwner", "namespace PropertyOwner; public sealed class Shape { }"),
            ("EventOwner", "namespace EventOwner; public delegate void ChangedHandler();"),
        })
        {
            AddProjectToCopiedSolution(workspace.RootPath, name, "net10.0");
            await File.WriteAllTextAsync(workspace.GetPath(name, name + ".cs"), source);
            AddProjectReference(workspace.GetPath("SampleLib", "SampleLib.csproj"), name);
        }
        await File.WriteAllTextAsync(sourcePath, IndependentSource);
        RemoveContractsSharedReference(workspace);
        await workspace.ReloadAsync();
        await AssertCompilesAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));

        var before = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var stores = new CompositePreviewStore();
        var preview = await PreviewInterfaceRouteAsync(route, workspace.WorkspaceId, sourcePath, "RefService", stores);
        if (route == "extract-and-wire")
        {
            var mutations = stores.Retrieve(preview.PreviewToken, null)!.Value.Mutations;
            var contractsProject = workspace.GetPath("Contracts", "Contracts.csproj");
            var projectMutation = mutations.Single(m => m.FilePath == contractsProject);
            foreach (var owner in SignatureOwners)
                StringAssert.Contains(projectMutation.UpdatedContent, owner + ".csproj");
            var apply = await new CompositeApplyOrchestrator(WorkspaceManager, stores, undoService: UndoService)
                .ApplyCompositeAsync(preview.PreviewToken, CompositeAdmission.AdmitAll, CancellationToken.None);
            Assert.IsTrue(apply.Success, apply.Error);
            await workspace.ReloadAsync();
            await AssertCompilesAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        }
        else
        {
            var pending = PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;
            AssertOwnerReferences(pending, SignatureOwners);
            await AssertCompilesAsync(pending);
            var apply = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
            Assert.IsTrue(apply.Success, apply.Error);
            await workspace.ReloadAsync();
            await AssertCompilesAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        }
    }

    [TestMethod]
    [DataRow("cross-project", false)]
    [DataRow("dependency-inversion", false)]
    [DataRow("extract-and-wire", false)]
    [DataRow("cross-project", true)]
    [DataRow("dependency-inversion", true)]
    [DataRow("extract-and-wire", true)]
    public async Task Alias_Only_Signature_Reference_Keeps_Alias_Through_Preview_And_Apply(string route, bool targetAlreadyReferencesOwner)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var sourcePath = await PrepareInterfaceRouteFixtureAsync(workspace, "RefService.cs", Source,
            "RefConsumer.cs", Consumer, SharedSource);
        if (!targetAlreadyReferencesOwner) RemoveContractsSharedReference(workspace);
        var sourceProjectFile = workspace.GetPath("SampleLib", "SampleLib.csproj");
        var sourceProjectXml = XDocument.Load(sourceProjectFile);
        sourceProjectXml.Descendants("ProjectReference").Single(element =>
            ((string?)element.Attribute("Include"))?.Contains("Shared.csproj", StringComparison.Ordinal) == true)
            .Add(new XElement("Aliases", "ext"));
        sourceProjectXml.Save(sourceProjectFile);
        await File.WriteAllTextAsync(sourcePath, AliasedSource);
        await workspace.ReloadAsync();
        await AssertCompilesAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));

        var stores = new CompositePreviewStore();
        var preview = await PreviewInterfaceRouteAsync(route, workspace.WorkspaceId, sourcePath, "RefService", stores);
        if (route == "extract-and-wire")
        {
            var mutations = stores.Retrieve(preview.PreviewToken, null)!.Value.Mutations;
            var projectText = mutations.Single(m => m.FilePath == workspace.GetPath("Contracts", "Contracts.csproj")).UpdatedContent;
            StringAssert.Contains(projectText, targetAlreadyReferencesOwner ? "<Aliases>ext,global</Aliases>" : "<Aliases>ext</Aliases>");
            var apply = await new CompositeApplyOrchestrator(WorkspaceManager, stores, undoService: UndoService)
                .ApplyCompositeAsync(preview.PreviewToken, CompositeAdmission.AdmitAll, CancellationToken.None);
            Assert.IsTrue(apply.Success, apply.Error);
        }
        else
        {
            var pending = PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;
            var contracts = pending.Projects.Single(p => p.Name == "Contracts");
            var shared = pending.Projects.Single(p => p.Name == "Shared");
            var expectedAliases = targetAlreadyReferencesOwner ? new[] { "global", "ext" } : ["ext"];
            CollectionAssert.AreEquivalent(expectedAliases, contracts.ProjectReferences.Single(r => r.ProjectId == shared.Id).Aliases.ToArray());
            var text = (await contracts.Documents.Single(d => d.Name == "IRefService.cs").GetTextAsync()).ToString();
            StringAssert.Contains(text, "extern alias ext;");
            await AssertCompilesAsync(pending);
            var apply = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
            Assert.IsTrue(apply.Success, apply.Error);
        }

        await workspace.ReloadAsync();
        await AssertCompilesAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        var appliedXml = XDocument.Load(workspace.GetPath("Contracts", "Contracts.csproj"));
        var sharedReference = appliedXml.Descendants("ProjectReference").Single(element =>
            ((string?)element.Attribute("Include"))?.Contains("Shared.csproj", StringComparison.Ordinal) == true);
        Assert.AreEqual(targetAlreadyReferencesOwner ? "ext,global" : "ext", (string?)sharedReference.Element("Aliases"));
    }

    [TestMethod]
    public async Task Equal_Simple_Assembly_Names_Do_Not_Confuse_Signature_Owner()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var sourcePath = await PrepareInterfaceRouteFixtureAsync(workspace, "RefService.cs", Source,
            "RefConsumer.cs", Consumer, SharedSource);
        AddProjectToCopiedSolution(workspace.RootPath, "Imposter", "net10.0");
        await File.WriteAllTextAsync(workspace.GetPath("Imposter", "Imposter.cs"),
            "namespace Imposter; public sealed class Shape { }");
        SetAssemblyName(workspace.GetPath("Shared", "Shared.csproj"), "SameAssembly");
        SetAssemblyName(workspace.GetPath("Imposter", "Imposter.csproj"), "SameAssembly");
        RemoveContractsSharedReference(workspace);
        await workspace.ReloadAsync();
        await AssertCompilesAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));

        var preview = await PreviewInterfaceRouteAsync("cross-project", workspace.WorkspaceId, sourcePath,
            "RefService", new CompositePreviewStore());
        var pending = PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;
        AssertOwnerReferences(pending, ["Shared"]);
        var contracts = pending.Projects.Single(project => project.Name == "Contracts");
        var imposter = pending.Projects.Single(project => project.Name == "Imposter");
        Assert.IsFalse(contracts.ProjectReferences.Any(reference => reference.ProjectId == imposter.Id));
        await AssertCompilesAsync(pending);
        var apply = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(apply.Success, apply.Error);
        await workspace.ReloadAsync();
        await AssertCompilesAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
    }

    private static readonly string[] SignatureOwners = ["Shared", "GenericArg", "Constraint", "PropertyOwner", "EventOwner"];

    private static void AssertOwnerReferences(Solution pending, IEnumerable<string> ownerNames)
    {
        var contracts = pending.Projects.Single(p => p.Name == "Contracts");
        foreach (var ownerName in ownerNames)
        {
            var owner = pending.Projects.Single(p => p.Name == ownerName);
            Assert.AreEqual(1, contracts.ProjectReferences.Count(r => r.ProjectId == owner.Id), ownerName);
        }
    }

    private static void AddProjectReference(string projectPath, string ownerName)
    {
        var xml = XDocument.Load(projectPath);
        xml.Root!.Add(new XElement("ItemGroup", new XElement("ProjectReference",
            new XAttribute("Include", $"../{ownerName}/{ownerName}.csproj"))));
        xml.Save(projectPath);
    }

    private static void SetAssemblyName(string projectPath, string assemblyName)
    {
        var xml = XDocument.Load(projectPath);
        xml.Root!.Add(new XElement("PropertyGroup", new XElement("AssemblyName", assemblyName)));
        xml.Save(projectPath);
    }

    private static void RemoveContractsSharedReference(IsolatedWorkspaceScope workspace)
    {
        var projectPath = workspace.GetPath("Contracts", "Contracts.csproj");
        var xml = XDocument.Load(projectPath);
        xml.Descendants("ProjectReference").Single(element =>
            ((string?)element.Attribute("Include"))?.Contains("Shared.csproj", StringComparison.Ordinal) == true).Remove();
        xml.Save(projectPath);
    }
}
