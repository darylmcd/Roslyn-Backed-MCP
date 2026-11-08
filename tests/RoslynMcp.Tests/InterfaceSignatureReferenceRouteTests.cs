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
    [DataRow("cross-project", "block")]
    [DataRow("dependency-inversion", "outer")]
    [DataRow("extract-and-wire", "inner")]
    [DataRow("cross-project", "using")]
    [DataRow("cross-project", "qualified-using")]
    [DataRow("cross-project", "unused-qualified-using")]
    [DataRow("cross-project", "contract-attribute")]
    [DataRow("cross-project", "static-using")]
    [DataRow("cross-project", "static-using-root")]
    [DataRow("cross-project", "contract-argument")]
    [DataRow("cross-project", "parameter-contract-argument")]
    public async Task Namespace_Scoped_Extern_Alias_Is_Preserved_Through_Interface_Routes(string route, string scope)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var sharedSource = scope switch
        {
            "contract-attribute" => SharedSource.Replace("namespace Shared;", "namespace Shared {", StringComparison.Ordinal)
                + "}\nnamespace System.Diagnostics.CodeAnalysis { [System.AttributeUsage(System.AttributeTargets.ReturnValue)] public sealed class NotNullAttribute : System.Attribute { } }",
            "static-using" or "static-using-root" => SharedSource + "\npublic static class Utility { public sealed class Nested { } }",
            "contract-argument" or "parameter-contract-argument" => SharedSource + "\npublic static class Patterns { public const string Name = \"value\"; }",
            _ => SharedSource,
        };
        var sourcePath = await PrepareInterfaceRouteFixtureAsync(workspace, "RefService.cs", Source,
            "RefConsumer.cs", Consumer, sharedSource);
        var sourceProjectFile = workspace.GetPath("SampleLib", "SampleLib.csproj");
        var sourceProjectXml = XDocument.Load(sourceProjectFile);
        sourceProjectXml.Descendants("ProjectReference").Single(element =>
            ((string?)element.Attribute("Include"))?.Contains("Shared.csproj", StringComparison.Ordinal) == true)
            .Add(new XElement("Aliases", "ext,unused"));
        sourceProjectXml.Save(sourceProjectFile);
        var source = scope switch
        {
            "block" => "namespace SampleLib { extern alias ext; extern alias unused; public sealed class RefService { public ext::Shared.Shape Get() => new(); } }",
            "outer" => "namespace SampleLib { extern alias ext; extern alias unused; namespace Nested { public sealed class RefService { public ext::Shared.Shape Get() => new(); } } }",
            "inner" => "namespace SampleLib { namespace Nested { extern alias ext; extern alias unused; public sealed class RefService { public ext::Shared.Shape Get() => new(); } } }",
            "using" => "namespace SampleLib { extern alias ext; extern alias unused; using Payload = ext::Shared.Shape; public sealed class RefService { public Payload Get() => new(); } }",
            "qualified-using" => "namespace SampleLib { extern alias ext; extern alias unused; using ext::Shared; using System.Text; public sealed class RefService { public Shape Get() => new(); } }",
            "unused-qualified-using" => "namespace SampleLib { extern alias ext; extern alias unused; using ext::Shared; public sealed class RefService { public ext::Shared.Shape Get() => new(); } }",
            "contract-attribute" => "namespace SampleLib { extern alias ext; extern alias unused; using Contract = ext::System.Diagnostics.CodeAnalysis.NotNullAttribute; public sealed class RefService { [return: Contract] public string Get() => string.Empty; } }",
            "static-using" => "namespace SampleLib { extern alias ext; extern alias unused; using static ext::Shared.Utility; public sealed class RefService { public Nested Get() => new(); } }",
            "static-using-root" => "extern alias ext; extern alias unused; using static ext::Shared.Utility; namespace SampleLib; public sealed class RefService { public Nested Get() => new(); }",
            "contract-argument" => "namespace SampleLib { extern alias ext; extern alias unused; using Patterns = ext::Shared.Patterns; public sealed class RefService { [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(Patterns.Name)] public string? Get(string? value) => value; } }",
            "parameter-contract-argument" => "namespace SampleLib { extern alias ext; extern alias unused; using Patterns = ext::Shared.Patterns; public sealed class RefService { public string Get([System.Diagnostics.CodeAnalysis.StringSyntax(Patterns.Name)] string value) => value; } }",
            _ => throw new ArgumentOutOfRangeException(nameof(scope)),
        };
        await File.WriteAllTextAsync(sourcePath, source);
        if (scope is "outer" or "inner")
            await File.WriteAllTextAsync(workspace.GetPath("Consumer", "RefConsumer.cs"),
                Consumer.Replace("SampleLib.RefService", "SampleLib.Nested.RefService", StringComparison.Ordinal));
        await workspace.ReloadAsync();
        await AssertCompilesAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));

        var stores = new CompositePreviewStore();
        var preview = await PreviewInterfaceRouteAsync(route, workspace.WorkspaceId, sourcePath, "RefService", stores);
        if (route == "extract-and-wire")
        {
            var mutations = stores.Retrieve(preview.PreviewToken, null)!.Value.Mutations;
            var generated = mutations.Single(m => m.FilePath == workspace.GetPath("Contracts", "IRefService.cs")).UpdatedContent;
            StringAssert.Contains(generated, "extern alias ext;");
            if (scope == "using") StringAssert.Contains(generated, "using Payload = ext::Shared.Shape;");
            if (scope == "qualified-using") StringAssert.Contains(generated, "using ext::Shared;");
            if (scope == "unused-qualified-using") Assert.IsFalse(generated.Contains("using ext::Shared;", StringComparison.Ordinal));
            if (scope == "contract-attribute") StringAssert.Contains(generated, "using Contract = ext::System.Diagnostics.CodeAnalysis.NotNullAttribute;");
            if (scope.EndsWith("contract-argument", StringComparison.Ordinal)) StringAssert.Contains(generated, "using Patterns = ext::Shared.Patterns;");
            Assert.IsFalse(generated.Contains("using System.Text;", StringComparison.Ordinal));
            if (scope.StartsWith("static-using", StringComparison.Ordinal)) Assert.IsFalse(generated.Contains("using Shared;", StringComparison.Ordinal));
            Assert.IsFalse(generated.Contains("extern alias unused;", StringComparison.Ordinal));
            var applied = await new CompositeApplyOrchestrator(WorkspaceManager, stores, undoService: UndoService)
                .ApplyCompositeAsync(preview.PreviewToken, CompositeAdmission.AdmitAll, CancellationToken.None);
            Assert.IsTrue(applied.Success, applied.Error);
        }
        else
        {
            var pending = PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;
            var generated = (await pending.Projects.Single(p => p.Name == "Contracts").Documents
                .Single(d => d.Name == "IRefService.cs").GetTextAsync()).ToString();
            StringAssert.Contains(generated, "extern alias ext;");
            if (scope == "using") StringAssert.Contains(generated, "using Payload = ext::Shared.Shape;");
            if (scope == "qualified-using") StringAssert.Contains(generated, "using ext::Shared;");
            if (scope == "unused-qualified-using") Assert.IsFalse(generated.Contains("using ext::Shared;", StringComparison.Ordinal));
            if (scope == "contract-attribute") StringAssert.Contains(generated, "using Contract = ext::System.Diagnostics.CodeAnalysis.NotNullAttribute;");
            if (scope.EndsWith("contract-argument", StringComparison.Ordinal)) StringAssert.Contains(generated, "using Patterns = ext::Shared.Patterns;");
            Assert.IsFalse(generated.Contains("using System.Text;", StringComparison.Ordinal));
            if (scope.StartsWith("static-using", StringComparison.Ordinal)) Assert.IsFalse(generated.Contains("using Shared;", StringComparison.Ordinal));
            Assert.IsFalse(generated.Contains("extern alias unused;", StringComparison.Ordinal));
            await AssertCompilesAsync(pending);
            var applied = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
            Assert.IsTrue(applied.Success, applied.Error);
        }

        await workspace.ReloadAsync();
        await AssertCompilesAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
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
