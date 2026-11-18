using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;
using static RoslynMcp.Tests.TestInfrastructure.InterfaceExtractionTestContext;

namespace RoslynMcp.Tests;

/// <summary>
/// A type that already implements an interface of the requested name is a no-op only when that interface covers every
/// requested member. Otherwise the requested interface is generated, the original interface stays implemented, and every
/// reference the new same-namespace interface would capture (the original's, and an unrelated same-named type's) keeps
/// binding to what it bound to before, across projects. A capture that cannot be repaired is refused, never published.
/// </summary>
[TestClass]
public sealed class InterfaceSameNameSiblingTests : InterfaceRouteTestBase
{
    private const string Sibling = """
        namespace SampleLib.Contracts;

        public interface IItemService
        {
            int Compute();
        }
        """;

    private const string Item = """
        using SampleLib.Contracts;

        namespace SampleLib;

        public class ItemService : IItemService
        {
            public int Compute() => 1;

            public int Extra() => 2;
        }
        """;

    private static readonly (string Path, string Text)[] SiblingFixture =
    [
        ("SampleLib/Contracts/IItemService.cs", Sibling),
        ("SampleLib/ItemService.cs", Item),
        ("SampleLib/Holder.cs", """
            using SampleLib.Contracts;

            namespace SampleLib;

            public class Holder
            {
                public IItemService Item = default!;
            }
            """),
        ("SampleLib/Consumers/UsesBoth.cs", """
            using SampleLib;
            using SampleLib.Contracts;

            namespace Consumers;

            public class UsesBoth
            {
                public IItemService Item = default!;
            }
            """),
        ("SampleLib/Other/IItemService.cs", """
            namespace Other;

            public interface IItemService
            {
                void Run();
            }
            """),
        ("SampleLib/UsesOther.cs", """
            using Other;

            namespace SampleLib;

            public class UsesOther
            {
                public IItemService Item = default!;
            }
            """),
    ];

    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task A_Sibling_That_Does_Not_Cover_The_Members_Is_Kept_And_The_Requested_Interface_Is_Generated()
    {
        await using var workspace = await LoadAsync(SiblingFixture);

        var preview = await ExtractAsync(workspace, "ItemService", "IItemService", null, replaceUsages: false);

        Assert.IsNull(preview.Warnings, "an incomplete contract is not a zero-change success");
        Assert.IsTrue(preview.Changes.Any(c => Path.GetFullPath(c.FilePath) == workspace.GetPath("SampleLib", "IItemService.cs")),
            "the requested interface is added next to the type");
        var pending = PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;
        await AssertCompilesAsync(pending);
        await AssertBothInterfacesAsync(pending);

        var applied = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "extract_interface_apply", CancellationToken.None);
        Assert.IsTrue(applied.Success, applied.Error);
        Assert.IsTrue(File.Exists(workspace.GetPath("SampleLib", "IItemService.cs")));
        var live = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        await AssertCompilesAsync(live);
        await AssertBothInterfacesAsync(live);
    }

    [TestMethod]
    public async Task A_Reference_To_An_Unrelated_Same_Named_Type_Keeps_Its_Binding()
    {
        await using var workspace = await LoadAsync(SiblingFixture);

        var preview = await ExtractAsync(workspace, "ItemService", "IItemService", null, replaceUsages: false);

        var compilation = await CompileAsync(PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution, "SampleLib");
        Assert.IsNotNull(compilation.GetTypeByMetadataName("SampleLib.IItemService"), "the requested interface is generated");
        Assert.AreEqual("Other.IItemService", FieldType(compilation, "SampleLib.UsesOther", "Item"),
            "the new SampleLib.IItemService must not capture a reference that bound to Other.IItemService");
    }

    [TestMethod]
    public async Task A_Same_Namespace_Interface_That_Does_Not_Cover_The_Members_Is_A_Target_Conflict()
    {
        await using var workspace = await LoadAsync(
            ("SampleLib/IItemService.cs", "namespace SampleLib;\n\npublic interface IItemService\n{\n    int Compute();\n}\n"),
            ("SampleLib/ItemService.cs", "namespace SampleLib;\n\npublic class ItemService : IItemService\n{\n    public int Compute() => 1;\n\n    public int Extra() => 2;\n}\n"));
        var files = Snapshot(workspace.RootPath);

        var refusal = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            ExtractAsync(workspace, "ItemService", "IItemService", null, replaceUsages: false));

        StringAssert.Contains(refusal.Message, "already exists in project");
        StringAssert.Contains(refusal.Message, "(namespace 'SampleLib')");
        AssertSnapshot(files);
    }

    [TestMethod]
    public async Task A_Sibling_That_Covers_The_Members_Is_A_NoOp_Including_Coverage_By_A_Subset_And_By_A_Base_Interface()
    {
        await using var subset = await LoadAsync(SiblingFixture);
        var subsetPreview = await ExtractAsync(subset, "ItemService", "IItemService", ["Compute"], replaceUsages: false);
        AssertNoOp(subsetPreview);

        await using var inherited = await LoadAsync(
            ("SampleLib/Contracts/IBase.cs", "namespace SampleLib.Contracts;\n\npublic interface IBase\n{\n    int Compute();\n}\n"),
            ("SampleLib/Contracts/IItemService.cs", "namespace SampleLib.Contracts;\n\npublic interface IItemService : IBase\n{\n}\n"),
            ("SampleLib/ItemService.cs", Item.Replace("    public int Extra() => 2;\n", string.Empty, StringComparison.Ordinal)));
        AssertNoOp(await ExtractAsync(inherited, "ItemService", "IItemService", null, replaceUsages: false));
    }

    [TestMethod]
    public async Task A_Sibling_Covering_An_Indexer_And_An_Event_Is_A_NoOp()
    {
        await using var workspace = await LoadAsync(
            ("SampleLib/Contracts/IItemService.cs", """
                namespace SampleLib.Contracts;

                public interface IItemService
                {
                    int this[int index] { get; }
                    event System.EventHandler Changed;
                }
                """),
            ("SampleLib/ItemService.cs", """
                using SampleLib.Contracts;

                namespace SampleLib;

                public class ItemService : IItemService
                {
                    public int this[int index] => index;

                    public event System.EventHandler? Changed;

                    public void Raise() => Changed?.Invoke(this, System.EventArgs.Empty);
                }
                """));

        // `Raise` is requested too, so memberNames pins the contract to the two members the sibling declares.
        AssertNoOp(await ExtractAsync(workspace, "ItemService", "IItemService", ["this[]", "Changed"], replaceUsages: false));
    }

    [TestMethod]
    public async Task A_Member_Implemented_Only_Explicitly_Is_Not_Covered_By_The_Sibling()
    {
        await using var workspace = await LoadAsync(
            ("SampleLib/Contracts/IItemService.cs", Sibling),
            ("SampleLib/ItemService.cs", """
                using SampleLib.Contracts;

                namespace SampleLib;

                public class ItemService : IItemService
                {
                    public int Compute() => 1;

                    int IItemService.Compute() => 2;
                }
                """));

        var preview = await ExtractAsync(workspace, "ItemService", "IItemService", null, replaceUsages: false);

        Assert.IsTrue(preview.Changes.Count > 0, "the public Compute does not implement the sibling's Compute, so the contract is not covered");
        var pending = PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;
        await AssertCompilesAsync(pending);
    }

    [TestMethod]
    public async Task A_Member_Declared_In_A_Base_Class_Is_Not_Part_Of_The_Requested_Contract()
    {
        await using var workspace = await LoadAsync(
            ("SampleLib/Contracts/IItemService.cs", Sibling),
            ("SampleLib/ItemService.cs", """
                using SampleLib.Contracts;

                namespace SampleLib;

                public class ItemBase
                {
                    public int Compute() => 1;
                }

                public class ItemService : ItemBase, IItemService
                {
                    public int Extra() => 2;
                }
                """));

        var preview = await ExtractAsync(workspace, "ItemService", "IItemService", null, replaceUsages: false);

        // Only declared members are projected (existing behaviour); the sibling is not covered by `Extra`, so it is generated.
        var pending = PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;
        await AssertCompilesAsync(pending);
        var generated = (await CompileAsync(pending, "SampleLib")).GetTypeByMetadataName("SampleLib.IItemService")!;
        CollectionAssert.AreEqual(new[] { "Extra" }, generated.GetMembers().Select(m => m.Name).Order(StringComparer.Ordinal).ToArray());
    }

    [TestMethod]
    public async Task A_Generic_Sibling_Is_Neither_A_Cover_Nor_Rebound()
    {
        await using var workspace = await LoadAsync(
            ("SampleLib/Contracts/IRepo.cs", "namespace SampleLib.Contracts;\n\npublic interface IRepo<T>\n{\n    T Get();\n}\n"),
            ("SampleLib/Repo.cs", "using SampleLib.Contracts;\n\nnamespace SampleLib;\n\npublic class Repo : IRepo<int>\n{\n    public int Get() => 1;\n}\n"),
            ("SampleLib/RepoUser.cs", "using SampleLib.Contracts;\n\nnamespace SampleLib;\n\npublic class RepoUser\n{\n    public IRepo<int> Repo = default!;\n}\n"));

        var preview = await ExtractAsync(workspace, "Repo", "IRepo", null, replaceUsages: false);

        Assert.IsFalse(preview.Changes.Any(c => c.FilePath.EndsWith("RepoUser.cs", StringComparison.Ordinal)), "IRepo<int> is a different type and is never rewritten");
        var pending = PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;
        await AssertCompilesAsync(pending);
        var repo = (await CompileAsync(pending, "SampleLib")).GetTypeByMetadataName("SampleLib.Repo")!;
        CollectionAssert.AreEqual(new[] { "SampleLib.Contracts.IRepo<int>", "SampleLib.IRepo" },
            repo.AllInterfaces.Select(i => i.ToDisplayString()).Order(StringComparer.Ordinal).ToArray());
    }

    [TestMethod]
    public async Task Sibling_References_Beside_Concrete_Usages_Are_Both_Rewritten_In_One_Document()
    {
        await using var workspace = await LoadAsync(SiblingFixture.Append(("SampleLib/Both.cs", """
            using SampleLib.Contracts;

            namespace SampleLib;

            public class Both
            {
                public IItemService Sibling = default!;
                public ItemService Concrete = default!;
            }
            """)).ToArray());

        var preview = await ExtractAsync(workspace, "ItemService", "IItemService", null, replaceUsages: true);

        var pending = PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;
        await AssertCompilesAsync(pending);
        var compilation = await CompileAsync(pending, "SampleLib");
        Assert.AreEqual("SampleLib.Contracts.IItemService", FieldType(compilation, "SampleLib.Both", "Sibling"));
        Assert.AreEqual("SampleLib.IItemService", FieldType(compilation, "SampleLib.Both", "Concrete"));
    }

    [TestMethod]
    public async Task A_Failure_With_A_Same_Named_Type_And_ReplaceUsages_Names_Both_Causes()
    {
        await using var workspace = await LoadAsync(SiblingFixture.Append(("SampleLib/Both.cs", """
            using SampleLib.Contracts;

            namespace SampleLib;

            public class Both
            {
                public ItemService Concrete = default!;

                public int Use() => Concrete.Compute();
            }
            """)).ToArray());

        // `Compute` is outside memberNames, so retyping `Concrete` makes `Use` uncompilable.
        var refusal = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            ExtractAsync(workspace, "ItemService", "IItemService", ["Extra"], replaceUsages: true));

        StringAssert.Contains(refusal.Message, "The generated interface introduces compiler errors");
        StringAssert.Contains(refusal.Message, "choose a different interface name");
        StringAssert.Contains(refusal.Message, "include the members those usages call in memberNames");
        StringAssert.Contains(refusal.Message, InterfacePreviewCompilationGate.SiblingReplaceUsagesRemedy);
    }

    [TestMethod]
    public void The_Remedy_Names_Only_The_Causes_That_Can_Apply()
    {
        Assert.AreEqual(InterfacePreviewCompilationGate.ReplaceUsagesRemedy, InterfacePreviewCompilationGate.RemedyFor(sameNamedType: false, replaceUsages: true));
        Assert.AreEqual(InterfacePreviewCompilationGate.SiblingRemedy, InterfacePreviewCompilationGate.RemedyFor(sameNamedType: true, replaceUsages: false));
        Assert.AreEqual(InterfacePreviewCompilationGate.SiblingReplaceUsagesRemedy, InterfacePreviewCompilationGate.RemedyFor(sameNamedType: true, replaceUsages: true));
        Assert.IsFalse(InterfacePreviewCompilationGate.SiblingRemedy.Contains("replaceUsages", StringComparison.Ordinal),
            "without replaceUsages the remedy must not blame it");
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(5)]
    [DataRow(18)] // ends exactly at the window edge: not a straddle
    [DataRow(19)] // 30-char window: every start from 19 to 29 puts the 12-char name across the edge
    [DataRow(24)]
    [DataRow(29)]
    public void A_Name_Straddling_Two_Scan_Windows_Is_Found_And_An_Absent_One_Is_Not(int leading)
    {
        const int chunk = 30; // larger than twice the 12-char name, so the window is exactly this size
        var text = Microsoft.CodeAnalysis.Text.SourceText.From(new string('x', leading) + "IItemService" + new string('y', 9));

        Assert.IsTrue(SiblingInterfaceRepin.Mentions(text, "IItemService", chunk));
        Assert.IsFalse(SiblingInterfaceRepin.Mentions(text, "IOtherService", chunk));
        Assert.IsFalse(SiblingInterfaceRepin.Mentions(Microsoft.CodeAnalysis.Text.SourceText.From(string.Empty), "IItemService", chunk));
    }

    [TestMethod]
    public async Task A_Projected_Member_That_Names_The_Unrelated_Same_Named_Type_Does_Not_Bind_To_The_New_Interface()
    {
        await using var workspace = await LoadAsync(SiblingFixture.Append(("SampleLib/ItemService.cs", """
            using SampleLib.Contracts;

            namespace SampleLib;

            public class ItemService : IItemService
            {
                public int Compute() => 1;

                public void Take(Other.IItemService other) { }
            }
            """)).ToArray());

        var preview = await ExtractAsync(workspace, "ItemService", "IItemService", null, replaceUsages: false);

        var pending = PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;
        await AssertCompilesAsync(pending);
        var generated = (await CompileAsync(pending, "SampleLib")).GetTypeByMetadataName("SampleLib.IItemService")!;
        var take = generated.GetMembers("Take").OfType<IMethodSymbol>().Single();
        Assert.AreEqual("Other.IItemService", take.Parameters.Single().Type.ToDisplayString(),
            "the generated interface must not capture the unrelated type it mentions");
    }

    [TestMethod]
    public async Task A_Consumer_File_Linked_Into_Two_Projects_Is_Repinned_Identically_And_Applies()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        await WriteAsync(workspace, SiblingFixture);
        AddLinkedProject(workspace, "Linked", "../SampleLib/Holder.cs", "../SampleLib/Contracts/IItemService.cs");
        await workspace.LoadAsync(CancellationToken.None);

        var preview = await ExtractAsync(workspace, "ItemService", "IItemService", null, replaceUsages: false);

        var pending = PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;
        await AssertCompilesAsync(pending);
        var copies = pending.Projects.SelectMany(p => p.Documents).Where(d => d.Name is "Holder.cs" or "LinkedHolder.cs").ToArray();
        Assert.AreEqual(2, copies.Length, "the consumer is compiled by SampleLib and by Linked");
        Assert.AreEqual(1, (await Task.WhenAll(copies.Select(async d => (await d.GetTextAsync()).ToString()))).Distinct().Count(),
            "both copies of the one physical file carry the same edit");
        foreach (var project in new[] { "SampleLib", "Linked" })
            Assert.AreEqual("SampleLib.Contracts.IItemService", FieldType(await CompileAsync(pending, project), "SampleLib.Holder", "Item"), project);

        var applied = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "extract_interface_apply", CancellationToken.None);
        Assert.IsTrue(applied.Success, applied.Error);
        StringAssert.Contains(await File.ReadAllTextAsync(workspace.GetPath("SampleLib", "Holder.cs")), "SampleLib.Contracts.IItemService");
        await AssertCompilesAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
    }

    [TestMethod]
    public async Task Cross_Project_Route_Repins_Linked_Source_And_Consumer_Copies_Without_Diverging()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var sourcePath = await PrepareInterfaceRouteFixtureAsync(workspace, "ItemService.cs",
            "using Contracts;\nusing SampleLib.Legacy;\n\nnamespace SampleLib;\n\npublic class ItemService : IItemService\n{\n    public int Compute() => 1;\n\n    public int Extra() => 2;\n}\n",
            "Uses.cs", "namespace Consumer;\n\npublic class Uses\n{\n}\n",
            extraSources: new Dictionary<string, string>
            {
                ["SampleLib/Legacy/IItemService.cs"] = "namespace SampleLib.Legacy;\n\npublic interface IItemService\n{\n    int Compute();\n}\n",
                ["SampleLib/UsesBoth.cs"] = "using Contracts;\nusing SampleLib.Legacy;\n\nnamespace SampleLib;\n\npublic class UsesBoth\n{\n    public IItemService Item = default!;\n}\n",
                ["Contracts/Marker.cs"] = "namespace Contracts;\n\npublic sealed class Marker\n{\n}\n",
            },
            libReferencesContracts: true);
        AddLinkedProject(workspace, "Linked", "../SampleLib/ItemService.cs", "../SampleLib/UsesBoth.cs", "../SampleLib/Legacy/IItemService.cs");
        var linkedProject = workspace.GetPath("Linked", "Linked.csproj");
        var xml = System.Xml.Linq.XDocument.Load(linkedProject);
        xml.Root!.Add(new System.Xml.Linq.XElement("ItemGroup", new System.Xml.Linq.XElement("ProjectReference",
            new System.Xml.Linq.XAttribute("Include", "../Contracts/Contracts.csproj"))));
        xml.Save(linkedProject);
        await workspace.ReloadAsync();
        await AssertCompilesAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));

        var preview = await PreviewInterfaceRouteAsync("cross-project", workspace.WorkspaceId, sourcePath, "ItemService", new CompositePreviewStore());

        var pending = PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;
        await AssertCompilesAsync(pending);
        foreach (var project in new[] { "SampleLib", "Linked" })
        {
            var compilation = await CompileAsync(pending, project);
            Assert.AreEqual("SampleLib.Legacy.IItemService", FieldType(compilation, "SampleLib.UsesBoth", "Item"), project);
            CollectionAssert.AreEqual(new[] { "Contracts.IItemService", "SampleLib.Legacy.IItemService" },
                compilation.GetTypeByMetadataName("SampleLib.ItemService")!.AllInterfaces.Select(i => i.ToDisplayString()).Order(StringComparer.Ordinal).ToArray(), project);
        }
    }

    [TestMethod]
    public async Task Linked_Copies_That_Bind_The_Name_To_Different_Types_Refuse_The_Preview()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        await WriteAsync(workspace, SiblingFixture);
        AddLinkedProject(workspace, "Linked", "../SampleLib/Holder.cs");
        // In Linked, the file's bare name means an unrelated global-using type, not the sibling.
        await File.WriteAllTextAsync(workspace.GetPath("Linked", "Local.cs"),
            "global using Other;\nnamespace Other { public interface IItemService { } }\nnamespace SampleLib.Contracts { public sealed class Marker { } }\n");
        await workspace.LoadAsync(CancellationToken.None);
        var files = Snapshot(workspace.RootPath);

        var refusal = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            ExtractAsync(workspace, "ItemService", "IItemService", null, replaceUsages: false));

        StringAssert.Contains(refusal.Message, "to different types in different projects");
        StringAssert.Contains(refusal.Message, "Holder.cs");
        AssertSnapshot(files);
    }

    [TestMethod]
    public async Task Linked_Copies_With_Different_Text_Refuse_The_Preview()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        await WriteAsync(workspace, SiblingFixture);
        AddLinkedProject(workspace, "Linked", "../SampleLib/Holder.cs", "../SampleLib/Contracts/IItemService.cs");
        await workspace.LoadAsync(CancellationToken.None);
        // The workspace holds the two copies of the one file with different text (an unreloaded edit in one project).
        var solution = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var variant = solution.Projects.Single(p => p.Name == "Linked").Documents.Single(d => d.Name == "LinkedHolder.cs");
        solution = solution.WithDocumentText(variant.Id, Microsoft.CodeAnalysis.Text.SourceText.From(
            (await variant.GetTextAsync()).ToString() + "// stale copy\n"));
        var manager = new FixedSolutionWorkspace(WorkspaceManager, solution);
        var store = new TrackingPreviewStore(new PreviewStore());
        using var cache = new CompilationCache(manager);
        var service = new InterfaceExtractionService(manager, store, cache, NullLogger<InterfaceExtractionService>.Instance);
        var files = Snapshot(workspace.RootPath);

        var refusal = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            service.PreviewExtractInterfaceAsync(workspace.WorkspaceId, workspace.GetPath("SampleLib", "ItemService.cs"),
                "ItemService", "IItemService", null, replaceUsages: false, CancellationToken.None));

        StringAssert.Contains(refusal.Message, "different content in different projects");
        Assert.AreEqual(0, store.GetRedeemableTokens(workspace.WorkspaceId).Count);
        AssertSnapshot(files);
    }

    [TestMethod]
    public async Task Standalone_Route_Edits_Every_Linked_Copy_Of_The_Source_Type_And_Applies()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var sourcePath = await PrepareInterfaceRouteFixtureAsync(workspace, "ItemService.cs",
            "using Contracts;\n\nnamespace SampleLib;\n\npublic class ItemService : IItemService\n{\n    public int Compute() => 1;\n\n    public int Extra() => 2;\n}\n",
            "Uses.cs", "namespace Consumer;\n\npublic class Uses\n{\n}\n",
            extraSources: new Dictionary<string, string>
            {
                ["Contracts/IItemService.cs"] = "namespace Contracts;\n\npublic interface IItemService\n{\n    int Compute();\n}\n",
            },
            libReferencesContracts: true);
        AddLinkedProject(workspace, "Linked", "../SampleLib/ItemService.cs");
        var linkedProject = workspace.GetPath("Linked", "Linked.csproj");
        var xml = System.Xml.Linq.XDocument.Load(linkedProject);
        xml.Root!.Add(new System.Xml.Linq.XElement("ItemGroup", new System.Xml.Linq.XElement("ProjectReference",
            new System.Xml.Linq.XAttribute("Include", "../Contracts/Contracts.csproj"))));
        xml.Save(linkedProject);
        await workspace.ReloadAsync();
        await AssertCompilesAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));

        var preview = await ExtractAsync(workspace, "ItemService", "IItemService", null, replaceUsages: false, sourcePath);

        var pending = PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;
        var copies = pending.Projects.SelectMany(p => p.Documents).Where(d => d.Name is "ItemService.cs" or "LinkedItemService.cs").ToArray();
        Assert.AreEqual(2, copies.Length, "the source type is compiled by SampleLib and by Linked");
        var texts = await Task.WhenAll(copies.Select(async d => (await d.GetTextAsync()).ToString()));
        Assert.AreEqual(1, texts.Distinct().Count(), "pending must match what apply writes to the one physical file");
        StringAssert.Contains(texts[0], ", IItemService");
        await AssertCompilesAsync(pending);

        var applied = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "extract_interface_apply", CancellationToken.None);
        Assert.IsTrue(applied.Success, applied.Error);
        var live = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        await AssertCompilesAsync(live);
        var linkedType = (await CompileAsync(live, "Linked")).GetTypeByMetadataName("SampleLib.ItemService")!;
        CollectionAssert.Contains(linkedType.AllInterfaces.Select(i => i.ToDisplayString()).ToArray(), "SampleLib.IItemService");
    }

    [TestMethod]
    public async Task A_Sibling_Failure_Without_ReplaceUsages_Does_Not_Blame_ReplaceUsages()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        await WriteAsync(workspace, SiblingFixture.Take(2).Append(("SampleLib/ExtUser.cs", """
            extern alias ext;
            using ext::Shared;

            namespace SampleLib;

            public class ExtUser
            {
                public IItemService Item = default!;
            }
            """)));
        // `Shared` is reachable only through the `ext` alias, so a repaired reference cannot spell it from anywhere.
        AddProjectToCopiedSolution(workspace.RootPath, "Shared", "net10.0");
        await File.WriteAllTextAsync(workspace.GetPath("Shared", "IItemService.cs"), "namespace Shared;\n\npublic interface IItemService\n{\n}\n");
        var projectPath = workspace.GetPath("SampleLib", "SampleLib.csproj");
        var projectXml = System.Xml.Linq.XDocument.Load(projectPath);
        projectXml.Root!.Add(new System.Xml.Linq.XElement("ItemGroup", new System.Xml.Linq.XElement("ProjectReference",
            new System.Xml.Linq.XAttribute("Include", "../Shared/Shared.csproj"), new System.Xml.Linq.XElement("Aliases", "ext"))));
        projectXml.Save(projectPath);
        await workspace.LoadAsync(CancellationToken.None);
        await AssertCompilesAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));

        var refusal = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            ExtractAsync(workspace, "ItemService", "IItemService", null, replaceUsages: false));

        StringAssert.Contains(refusal.Message, "The generated interface introduces compiler errors");
        StringAssert.Contains(refusal.Message, InterfacePreviewCompilationGate.SiblingRemedy);
        Assert.IsFalse(refusal.Message.Contains("replaceUsages", StringComparison.Ordinal), refusal.Message);
    }

    private static async Task WriteAsync(IsolatedWorkspaceScope workspace, IEnumerable<(string Path, string Text)> files)
    {
        foreach (var (relative, text) in files)
        {
            var path = workspace.GetPath(relative.Split('/'));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, text);
        }
    }

    /// <summary>Adds a project that compiles the named SampleLib files as linked copies of the same physical files.</summary>
    private static void AddLinkedProject(IsolatedWorkspaceScope workspace, string projectName, params string[] links)
    {
        AddProjectToCopiedSolution(workspace.RootPath, projectName, "net10.0");
        var path = workspace.GetPath(projectName, projectName + ".csproj");
        var xml = System.Xml.Linq.XDocument.Load(path);
        xml.Root!.Add(new System.Xml.Linq.XElement("ItemGroup", links.Select(link => new System.Xml.Linq.XElement("Compile",
            new System.Xml.Linq.XAttribute("Include", link),
            new System.Xml.Linq.XAttribute("Link", "Linked" + System.IO.Path.GetFileName(link))))));
        xml.Save(path);
    }

    [TestMethod]
    public async Task A_Downstream_Project_That_Would_Gain_An_Ambiguity_Is_Repinned()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var sourcePath = await PrepareInterfaceRouteFixtureAsync(workspace, "ItemService.cs",
            "using SampleLib.Legacy;\n\nnamespace SampleLib;\n\npublic class ItemService : IItemService\n{\n    public int Compute() => 1;\n\n    public int Extra() => 2;\n}\n",
            "Uses.cs",
            "using SampleLib;\nusing SampleLib.Legacy;\n\nnamespace Consumer;\n\npublic class Uses\n{\n    public IItemService Item = default!;\n}\n",
            extraSources: new Dictionary<string, string>
            {
                ["SampleLib/Legacy/IItemService.cs"] = "namespace SampleLib.Legacy;\n\npublic interface IItemService\n{\n    int Compute();\n}\n",
            });

        var preview = await ExtractAsync(workspace, "ItemService", "IItemService", null, replaceUsages: false, sourcePath);

        var pending = PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;
        await AssertCompilesAsync(pending);
        var consumer = await CompileAsync(pending, "Consumer");
        Assert.AreEqual("SampleLib.Legacy.IItemService", FieldType(consumer, "Consumer.Uses", "Item"));
        Assert.IsTrue(preview.Changes.Any(c => c.FilePath.EndsWith("Uses.cs", StringComparison.Ordinal)), "the downstream document is part of the preview");
    }

    [TestMethod]
    [DataRow("cross-project")]
    [DataRow("dependency-inversion")]
    public async Task Cross_Project_Routes_Keep_The_Sibling_Bound_And_Implemented(string route)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var sourcePath = await PrepareInterfaceRouteFixtureAsync(workspace, "ItemService.cs",
            "using Contracts;\nusing SampleLib.Legacy;\n\nnamespace SampleLib;\n\npublic class ItemService : IItemService\n{\n    public int Compute() => 1;\n\n    public int Extra() => 2;\n}\n",
            "Uses.cs", "namespace Consumer;\n\npublic class Uses\n{\n}\n",
            extraSources: new Dictionary<string, string>
            {
                ["SampleLib/Legacy/IItemService.cs"] = "namespace SampleLib.Legacy;\n\npublic interface IItemService\n{\n    int Compute();\n}\n",
                ["SampleLib/UsesBoth.cs"] = "using Contracts;\nusing SampleLib.Legacy;\n\nnamespace SampleLib;\n\npublic class UsesBoth\n{\n    public IItemService Item = default!;\n}\n",
                ["Contracts/Marker.cs"] = "namespace Contracts;\n\npublic sealed class Marker\n{\n}\n",
            },
            libReferencesContracts: true);

        var preview = await PreviewInterfaceRouteAsync(route, workspace.WorkspaceId, sourcePath, "ItemService", new CompositePreviewStore());

        var pending = PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;
        await AssertCompilesAsync(pending);
        var compilation = await CompileAsync(pending, "SampleLib");
        Assert.AreEqual("SampleLib.Legacy.IItemService", FieldType(compilation, "SampleLib.UsesBoth", "Item"));
        CollectionAssert.AreEqual(new[] { "Contracts.IItemService", "SampleLib.Legacy.IItemService" },
            compilation.GetTypeByMetadataName("SampleLib.ItemService")!.AllInterfaces.Select(i => i.ToDisplayString()).Order(StringComparer.Ordinal).ToArray());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task A_Rebound_Reference_In_A_Source_Generated_Document_Is_Refused_Even_When_It_Still_Compiles(bool generatorDependsOnTheNewType)
    {
        await using var workspace = await LoadAsync(SiblingFixture.Take(2).ToArray());
        var generatedSource = "using SampleLib.Contracts;\n\nnamespace SampleLib;\n\npublic class GeneratedUser\n{\n    public IItemService Held = default!;\n}\n";
        var outputs = new Dictionary<string, string>
        {
            [generatorDependsOnTheNewType ? "SampleLib|SampleLib.IItemService" : "SampleLib"] = generatedSource,
        };
        if (generatorDependsOnTheNewType)
            outputs["SampleLib"] = "namespace SampleLib;\n\npublic class Anchor\n{\n}\n";
        var solution = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var project = solution.Projects.Single(p => p.Name == "SampleLib").AddAnalyzerReference(GeneratorReference(workspace.RootPath, outputs));
        if (!generatorDependsOnTheNewType) await AssertGeneratedReferenceAsync(project, "SampleLib.GeneratedUser");
        var manager = new FixedSolutionWorkspace(WorkspaceManager, project.Solution);
        var store = new TrackingPreviewStore(new PreviewStore());
        using var cache = new CompilationCache(manager);
        var service = new InterfaceExtractionService(manager, store, cache, NullLogger<InterfaceExtractionService>.Instance);
        var files = Snapshot(workspace.RootPath);

        var refusal = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            service.PreviewExtractInterfaceAsync(workspace.WorkspaceId, workspace.GetPath("SampleLib", "ItemService.cs"),
                "ItemService", "IItemService", null, replaceUsages: false, CancellationToken.None));

        StringAssert.Contains(refusal.Message, "would rebind");
        StringAssert.Contains(refusal.Message, "source-generated document");
        StringAssert.Contains(refusal.Message, "project 'SampleLib'");
        Assert.AreEqual(0, store.GetRedeemableTokens(workspace.WorkspaceId).Count);
        AssertSnapshot(files);
    }

    private static void AssertNoOp(RefactoringPreviewDto preview)
    {
        Assert.AreEqual(0, preview.Changes.Count, string.Join("; ", preview.Changes.Select(c => c.FilePath)));
        Assert.IsTrue(preview.Warnings?.Any(w => w.Contains("already implements", StringComparison.Ordinal)) == true,
            "the no-op explains itself: " + string.Join(" | ", preview.Warnings ?? []));
    }

    private static async Task AssertBothInterfacesAsync(Solution solution)
    {
        var compilation = await CompileAsync(solution, "SampleLib");
        var item = compilation.GetTypeByMetadataName("SampleLib.ItemService")!;
        CollectionAssert.AreEqual(new[] { "SampleLib.Contracts.IItemService", "SampleLib.IItemService" },
            item.AllInterfaces.Select(i => i.ToDisplayString()).Order(StringComparer.Ordinal).ToArray());
        CollectionAssert.AreEqual(new[] { "Compute", "Extra" },
            compilation.GetTypeByMetadataName("SampleLib.IItemService")!.GetMembers().Select(m => m.Name).Order(StringComparer.Ordinal).ToArray());
        CollectionAssert.AreEqual(new[] { "Compute" },
            compilation.GetTypeByMetadataName("SampleLib.Contracts.IItemService")!.GetMembers().Select(m => m.Name).ToArray());
        Assert.AreEqual("SampleLib.Contracts.IItemService", FieldType(compilation, "SampleLib.Holder", "Item"));
        Assert.AreEqual("SampleLib.Contracts.IItemService", FieldType(compilation, "Consumers.UsesBoth", "Item"));
    }

    private static string FieldType(Compilation compilation, string typeName, string field) =>
        compilation.GetTypeByMetadataName(typeName)!.GetMembers(field).OfType<IFieldSymbol>().Single().Type.ToDisplayString();

    private static async Task<Compilation> CompileAsync(Solution solution, string project) =>
        (await solution.Projects.Single(p => p.Name == project).GetCompilationAsync())!;

    private static Task<RefactoringPreviewDto> ExtractAsync(
        IsolatedWorkspaceScope workspace, string typeName, string interfaceName, IReadOnlyList<string>? memberNames, bool replaceUsages, string? sourcePath = null) =>
        InterfaceExtractionService.PreviewExtractInterfaceAsync(workspace.WorkspaceId,
            sourcePath ?? workspace.GetPath("SampleLib", typeName + ".cs"), typeName, interfaceName, memberNames, replaceUsages, CancellationToken.None);

    private async Task<IsolatedWorkspaceScope> LoadAsync(params (string Path, string Text)[] files)
    {
        var workspace = CreateIsolatedWorkspaceCopy();
        foreach (var (relative, text) in files)
        {
            var path = workspace.GetPath(relative.Split('/'));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, text);
        }

        await workspace.LoadAsync(CancellationToken.None);
        return workspace;
    }
}
