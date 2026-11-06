using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;
using static RoslynMcp.Tests.TestInfrastructure.InterfaceExtractionTestContext;

namespace RoslynMcp.Tests;

/// <summary>
/// A member whose emitted signature contains a pointer or function-pointer type only compiles in a receiving
/// project that enables <c>AllowUnsafeBlocks</c> (CS0227). Every route that lands an interface in a project
/// (cross-project, dependency-inversion, extract-and-wire) must refuse before publishing a token when that
/// project disables unsafe code, and must emit per-member <c>unsafe</c> when it enables it. The standalone route
/// writes into the source project, which already compiles the unsafe source, so it only has a success row.
/// </summary>
[TestClass]
public sealed class InterfaceUnsafeReceiverRouteTests : InterfaceRouteTestBase
{
    private const string RefusalMessage = "requires unsafe signatures; receiving project does not enable AllowUnsafeBlocks";

    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    private const string UnsafeSource = """
        namespace Contracts;

        public unsafe class UnsafeService
        {
            public int* Pointer(int* p, delegate*<int, void> callback) => p;
            public int* Prop { get; set; }
            public int* this[int* index] => index;
            public int Plain(int x) => x;
        }
        """;

    private const string PlainSource = """
        namespace Contracts;

        public class UnsafeService
        {
            public int Plain(int x) => x;
        }
        """;

    private const string ConsumerSource = """
        namespace Contracts;

        public class UnsafeConsumer
        {
            public UnsafeConsumer(UnsafeService service) { }
        }
        """;

    public static IEnumerable<object[]> RefusalRoutes() =>
        new[] { "cross-project", "dependency-inversion", "extract-and-wire" }.Select(r => new object[] { r });

    public static IEnumerable<object[]> AllRoutes() =>
        new[] { "standalone", "cross-project", "dependency-inversion", "extract-and-wire" }.Select(r => new object[] { r });

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Unsafe(bool receiverAllows) =>
        new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["SampleLib"] = new Dictionary<string, string> { ["AllowUnsafeBlocks"] = "true" },
            ["Contracts"] = new Dictionary<string, string> { ["AllowUnsafeBlocks"] = receiverAllows ? "true" : "false" },
        };

    private static Task<string> PrepareAsync(IsolatedWorkspaceScope workspace, string source, bool receiverAllows) =>
        PrepareInterfaceRouteFixtureAsync(workspace, "UnsafeService.cs", source, "UnsafeConsumer.cs", ConsumerSource,
            projectProperties: Unsafe(receiverAllows));

    private static Task<RoslynMcp.Core.Models.RefactoringPreviewDto> PreviewAsync(
        string route, string workspaceId, string sourcePath, CompositePreviewStore stores) =>
        PreviewInterfaceRouteAsync(route, workspaceId, sourcePath, "UnsafeService", stores);

    [TestMethod]
    [DynamicData(nameof(RefusalRoutes))]
    public async Task Unsafe_Signatures_Are_Refused_Before_Publication_When_Receiver_Disables_Unsafe(string route)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var sourcePath = await PrepareAsync(workspace, UnsafeSource, receiverAllows: false);
        var files = Snapshot(workspace.RootPath);
        var before = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var version = WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId);
        var tracking = (TrackingPreviewStore)PreviewStore;
        var ordinaryCount = tracking.GetRedeemableTokens(workspace.WorkspaceId).Count;
        var stores = new CompositePreviewStore();

        var error = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(
            () => PreviewAsync(route, workspace.WorkspaceId, sourcePath, stores));

        StringAssert.Contains(error.Message, RefusalMessage);
        Assert.IsFalse(error.Message.Contains(sourcePath, StringComparison.Ordinal), error.Message);
        Assert.IsFalse(error.Message.Contains(workspace.RootPath, StringComparison.Ordinal), error.Message);
        Assert.AreEqual(ordinaryCount, tracking.GetRedeemableTokens(workspace.WorkspaceId).Count);
        Assert.AreEqual(0, StoreCount(stores));
        AssertSnapshot(files);
        Assert.IsFalse(File.Exists(workspace.GetPath("Contracts", "IUnsafeService.cs")));
        Assert.AreSame(before, WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId));
    }

    [TestMethod]
    [DynamicData(nameof(AllRoutes))]
    public async Task Unsafe_Signatures_Compile_With_Per_Member_Unsafe_When_Receiver_Enables_Unsafe(string route)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var sourcePath = await PrepareAsync(workspace, UnsafeSource, receiverAllows: true);
        var before = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var stores = new CompositePreviewStore();
        var receiver = route == "standalone" ? "SampleLib" : "Contracts";

        var preview = await PreviewAsync(route, workspace.WorkspaceId, sourcePath, stores);

        var pending = route == "extract-and-wire"
            ? SerializedCompositePreview(before, stores.Retrieve(preview.PreviewToken, null)!.Value.Mutations)
            : PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;
        pending = await ReparseSerializedDocumentsAsync(pending);
        await AssertCompilesAsync(pending);
        var document = pending.Projects.Single(p => p.Name == receiver).Documents.Single(d => d.Name == "IUnsafeService.cs");
        var root = (await document.GetSyntaxRootAsync())!;
        var members = root.DescendantNodes().OfType<InterfaceDeclarationSyntax>().Single().Members;
        Assert.AreEqual(4, members.Count);
        foreach (var member in members)
        {
            var name = member switch
            {
                MethodDeclarationSyntax m => m.Identifier.ValueText,
                PropertyDeclarationSyntax p => p.Identifier.ValueText,
                IndexerDeclarationSyntax => "this[]",
                _ => throw new AssertFailedException("Unexpected member " + member.Kind()),
            };
            Assert.AreEqual(name != "Plain", member.Modifiers.Any(SyntaxKind.UnsafeKeyword), name + " in:\n" + root);
        }
    }

    [TestMethod]
    [DynamicData(nameof(RefusalRoutes))]
    public async Task Signatures_Without_Pointers_Are_Not_Refused_In_An_Unsafe_Disabled_Receiver(string route)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var sourcePath = await PrepareAsync(workspace, PlainSource, receiverAllows: false);
        var before = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var stores = new CompositePreviewStore();

        var preview = await PreviewAsync(route, workspace.WorkspaceId, sourcePath, stores);

        var pending = route == "extract-and-wire"
            ? SerializedCompositePreview(before, stores.Retrieve(preview.PreviewToken, null)!.Value.Mutations)
            : PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;
        pending = await ReparseSerializedDocumentsAsync(pending);
        await AssertCompilesAsync(pending);
        var text = (await pending.Projects.Single(p => p.Name == "Contracts").Documents.Single(d => d.Name == "IUnsafeService.cs").GetTextAsync()).ToString();
        Assert.IsFalse(text.Contains("unsafe", StringComparison.Ordinal), text);
    }
}
