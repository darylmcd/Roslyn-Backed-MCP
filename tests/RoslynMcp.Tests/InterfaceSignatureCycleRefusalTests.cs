using System.Xml.Linq;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;
using static RoslynMcp.Tests.TestInfrastructure.InterfaceExtractionTestContext;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class InterfaceSignatureCycleRefusalTests : InterfaceRouteTestBase
{
    private const string Consumer = "namespace Consumer; public sealed class User { public User(SampleLib.RefService service) { } }";
    private const string SourceOwned = "namespace SampleLib; public sealed class Owned { } public sealed class RefService { public Owned Get() => new(); }";
    private const string SharedOwned = "namespace Shared; public sealed class Shape { }";
    private const string SharedSource = "namespace SampleLib; public sealed class RefService { public Shared.Shape Get() => new(); }";

    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    [DataRow("cross-project", false)]
    [DataRow("dependency-inversion", false)]
    [DataRow("extract-and-wire", false)]
    [DataRow("cross-project", true)]
    [DataRow("dependency-inversion", true)]
    [DataRow("extract-and-wire", true)]
    public async Task Cyclic_Signature_Owner_Refuses_Before_Token_Or_Workspace_Mutation(string route, bool transitive)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var sourcePath = await PrepareInterfaceRouteFixtureAsync(workspace, "RefService.cs",
            transitive ? SharedSource : SourceOwned, "User.cs", Consumer,
            transitive ? SharedOwned : null);
        if (transitive)
        {
            var contractsPath = workspace.GetPath("Contracts", "Contracts.csproj");
            var contractsXml = XDocument.Load(contractsPath);
            contractsXml.Descendants("ProjectReference").Single().Remove();
            contractsXml.Save(contractsPath);
            var sharedPath = workspace.GetPath("Shared", "Shared.csproj");
            var sharedXml = XDocument.Load(sharedPath);
            sharedXml.Root!.Add(new XElement("ItemGroup", new XElement("ProjectReference",
                new XAttribute("Include", "../Contracts/Contracts.csproj"))));
            sharedXml.Save(sharedPath);
            await workspace.ReloadAsync();
            await AssertCompilesAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        }

        var sourceBefore = await File.ReadAllTextAsync(sourcePath);
        var contractsPathBefore = await File.ReadAllTextAsync(workspace.GetPath("Contracts", "Contracts.csproj"));
        var sourceProjectBefore = await File.ReadAllTextAsync(workspace.GetPath("SampleLib", "SampleLib.csproj"));
        var versionBefore = WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId);
        var exception = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(async () =>
            await PreviewInterfaceRouteAsync(route, workspace.WorkspaceId, sourcePath, "RefService", new CompositePreviewStore()));
        StringAssert.Contains(exception.Message, transitive ? "Shape" : "Owned");
        StringAssert.Contains(exception.Message, transitive ? "Shared" : "SampleLib");
        StringAssert.Contains(exception.Message, "Contracts");
        Assert.AreEqual(versionBefore, WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId));
        Assert.AreEqual(sourceBefore, await File.ReadAllTextAsync(sourcePath));
        Assert.AreEqual(contractsPathBefore, await File.ReadAllTextAsync(workspace.GetPath("Contracts", "Contracts.csproj")));
        Assert.AreEqual(sourceProjectBefore, await File.ReadAllTextAsync(workspace.GetPath("SampleLib", "SampleLib.csproj")));
        Assert.IsFalse(File.Exists(workspace.GetPath("Contracts", "IRefService.cs")));
    }

    [TestMethod]
    [DataRow("cross-project")]
    [DataRow("dependency-inversion")]
    [DataRow("extract-and-wire")]
    public async Task Receiver_Owned_Signature_Remains_Valid(string route)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var sourcePath = await PrepareInterfaceRouteFixtureAsync(workspace, "RefService.cs",
            SourceOwned, "User.cs", Consumer);
        await File.WriteAllTextAsync(workspace.GetPath("Contracts", "Owned.cs"),
            "namespace Contracts; public sealed class Owned { }");
        var sourceProjectPath = workspace.GetPath("SampleLib", "SampleLib.csproj");
        var sourceProjectXml = XDocument.Load(sourceProjectPath);
        sourceProjectXml.Root!.Add(new XElement("ItemGroup", new XElement("ProjectReference",
            new XAttribute("Include", "../Contracts/Contracts.csproj"))));
        sourceProjectXml.Save(sourceProjectPath);
        await File.WriteAllTextAsync(sourcePath,
            "namespace SampleLib; public sealed class RefService { public Contracts.Owned Get() => new(); }");
        await workspace.ReloadAsync();
        await AssertCompilesAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));

        var stores = new CompositePreviewStore();
        var preview = await PreviewInterfaceRouteAsync(route, workspace.WorkspaceId, sourcePath, "RefService", stores);
        if (route == "extract-and-wire")
        {
            var apply = await new CompositeApplyOrchestrator(WorkspaceManager, stores, undoService: UndoService)
                .ApplyCompositeAsync(preview.PreviewToken, CompositeAdmission.AdmitAll, CancellationToken.None);
            Assert.IsTrue(apply.Success, apply.Error);
        }
        else
        {
            var pending = PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;
            await AssertCompilesAsync(pending);
            var apply = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
            Assert.IsTrue(apply.Success, apply.Error);
        }

        await workspace.ReloadAsync();
        await AssertCompilesAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
    }
}
