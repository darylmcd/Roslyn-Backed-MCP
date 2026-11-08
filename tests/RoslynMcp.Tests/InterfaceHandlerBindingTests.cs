using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;
using static RoslynMcp.Tests.TestInfrastructure.InterfaceExtractionTestContext;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class InterfaceHandlerBindingTests : InterfaceRouteTestBase
{
    private const string Shared = """
        using System.Runtime.CompilerServices;
        namespace Shared;
        [InterpolatedStringHandler]
        public ref struct Handler
        {
            public Handler(int literalLength, int formattedCount, object receiver) { }
            public void AppendLiteral(string text) { }
            public void AppendFormatted<T>(T value) { }
        }
        """;

    private const string Source = """
        using System.Runtime.CompilerServices;
        using Shared;
        namespace SampleLib;
        public class RefService
        {
            public void Log([InterpolatedStringHandlerArgument("")] Handler handler) { }
        }
        """;

    private const string Consumer = "namespace Consumer; public sealed class User { public User(SampleLib.RefService service) { } }";

    private const string AlternativeShared = """
        using System.Runtime.CompilerServices;
        namespace Shared;
        public interface IReceiverMarker { }
        [InterpolatedStringHandler]
        public ref struct Handler
        {
            public Handler(int literalLength, int formattedCount, object receiver) { }
            public Handler(int literalLength, int formattedCount, IReceiverMarker receiver) { }
            public void AppendLiteral(string text) { }
            public void AppendFormatted<T>(T value) { }
        }
        """;

    private const string AlternativeSource = """
        using System.Runtime.CompilerServices;
        using Shared;
        namespace SampleLib;
        public class RefService : IReceiverMarker
        {
            public void Log([InterpolatedStringHandlerArgument("")] Handler handler) { }
        }
        """;

    private const string AdvancedShared = """
        using System.Runtime.CompilerServices;
        namespace Shared;
        [InterpolatedStringHandler]
        public ref struct Handler
        {
            public Handler(int literalLength, int formattedCount, object receiver, object neighbor) { }
            public void AppendLiteral(string text) { }
            public void AppendFormatted<T>(T value) { }
        }
        """;

    private const string AdvancedSource = """
        using System.Runtime.CompilerServices;
        using Shared;
        namespace SampleLib;
        public class RefService
        {
            public void Log(object neighbor, [InterpolatedStringHandlerArgument("", "neighbor")] Handler handler) { }
            public void Log(string neighbor, [InterpolatedStringHandlerArgument("", "neighbor")] Handler handler) { }
            public void Generic<@class>(object neighbor, [InterpolatedStringHandlerArgument("", "neighbor")] Handler handler) where @class : class { }
            public void Original<Original>(object neighbor, [InterpolatedStringHandlerArgument("", "neighbor")] Handler handler) where Original : class { }
            public void Projected<Projected>(object neighbor, [InterpolatedStringHandlerArgument("", "neighbor")] Handler handler) where Projected : class { }
        }
        """;

    private const string DynamicSource = """
        using System.Runtime.CompilerServices;
        using Shared;
        namespace SampleLib;
        public class RefService
        {
            public void Log(dynamic neighbor, [InterpolatedStringHandlerArgument("", "neighbor")] Handler handler) { }
        }
        """;

    private const string GetterIndexerSource = """
        using System.Runtime.CompilerServices;
        using Shared;
        namespace SampleLib;
        public class RefService
        {
            public int this[[InterpolatedStringHandlerArgument("")] Handler key] { get => 0; }
        }
        """;

    private const string SetterIndexerSource = """
        using System.Runtime.CompilerServices;
        using Shared;
        namespace SampleLib;
        public class RefService
        {
            public int this[[InterpolatedStringHandlerArgument("")] Handler key] { set { } }
        }
        """;

    private const string NestedConsumer = "namespace Consumer; public sealed class User { public User(SampleLib.Outer<System.Text.StringBuilder>.RefService<int> service) { } }";

    private static string NestedSource(string layout)
    {
        const string members = """
            public class RefService<U> where U : struct
            {
                public void Generic<@class>([InterpolatedStringHandlerArgument("")] Handler handler) where @class : class { }
            }
            """;
        const string header = "using System.Runtime.CompilerServices; using Shared;\n";
        var outer = "public class Outer<T> where T : class, new() { " + members + " }";
        return layout switch
        {
            "plain" => header + "namespace SampleLib;\n" + outer,
            "block" => header + "namespace SampleLib { " + outer + " }",
            "line" => header + "#line 100 \"nested.cs\"\nnamespace SampleLib;\n" + outer,
            "primary" => header + "namespace SampleLib;\npublic class Outer<T>(int value) where T : class, new() { " + members + " }",
            "collision" => header + "namespace SampleLib;\npublic class Outer<__receiver> where __receiver : class, new() { " +
                members.Replace("RefService<U> where U", "RefService<__assigned> where __assigned", StringComparison.Ordinal) + " }",
            _ => throw new ArgumentOutOfRangeException(nameof(layout)),
        };
    }

    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    [DataRow("standalone")]
    [DataRow("cross-project")]
    [DataRow("dependency-inversion")]
    [DataRow("extract-and-wire")]
    public async Task Compatible_Handler_Attribute_Survives_Every_Route(string route)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var sourcePath = await PrepareInterfaceRouteFixtureAsync(workspace, "RefService.cs", Source, "User.cs", Consumer, Shared);
        var stores = new CompositePreviewStore();
        var preview = await PreviewInterfaceRouteAsync(route, workspace.WorkspaceId, sourcePath, "RefService", stores);
        string interfaceText;
        if (route == "extract-and-wire")
        {
            interfaceText = stores.Retrieve(preview.PreviewToken, null)!.Value.Mutations
                .Single(m => m.FilePath == workspace.GetPath("Contracts", "IRefService.cs")).UpdatedContent
                ?? throw new AssertFailedException("The interface mutation has no content.");
        }
        else
        {
            var pending = PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;
            var project = pending.Projects.Single(p => p.Name == (route == "standalone" ? "SampleLib" : "Contracts"));
            interfaceText = (await project.Documents.Single(d => d.Name == "IRefService.cs").GetTextAsync()).ToString();
            await AssertCompilesAsync(pending);
        }
        StringAssert.Contains(interfaceText, "InterpolatedStringHandlerArgument");
    }

    [TestMethod]
    [DataRow("standalone")]
    [DataRow("cross-project")]
    [DataRow("dependency-inversion")]
    [DataRow("extract-and-wire")]
    public async Task Changed_Handler_Constructor_Refuses_Before_Preview(string route)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var sourcePath = await PrepareInterfaceRouteFixtureAsync(workspace, "RefService.cs", AlternativeSource, "User.cs", Consumer, AlternativeShared);
        var stores = new CompositePreviewStore();
        var error = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(
            () => PreviewInterfaceRouteAsync(route, workspace.WorkspaceId, sourcePath, "RefService", stores));
        StringAssert.Contains(error.Message, "handler constructor");
        Assert.IsFalse(File.Exists(workspace.GetPath(route == "standalone" ? "SampleLib" : "Contracts", "IRefService.cs")));
    }

    [TestMethod]
    [DataRow("standalone")]
    [DataRow("cross-project")]
    public async Task Generic_Methods_Escaped_Names_And_Overloads_Bind_Identically(string route)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var sourcePath = await PrepareInterfaceRouteFixtureAsync(workspace, "RefService.cs", AdvancedSource, "User.cs", Consumer, AdvancedShared);
        var preview = await PreviewInterfaceRouteAsync(route, workspace.WorkspaceId, sourcePath, "RefService", new CompositePreviewStore());
        var pending = PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;
        await AssertCompilesAsync(pending);
        var project = pending.Projects.Single(p => p.Name == (route == "standalone" ? "SampleLib" : "Contracts"));
        var text = (await project.Documents.Single(d => d.Name == "IRefService.cs").GetTextAsync()).ToString();
        Assert.AreEqual(5, text.Split("InterpolatedStringHandlerArgument", StringSplitOptions.None).Length - 1, text);
    }

    [TestMethod]
    public async Task Dynamic_Forwarded_Neighbour_Refuses_With_Compiler_Id()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var sourcePath = await PrepareInterfaceRouteFixtureAsync(workspace, "RefService.cs", DynamicSource, "User.cs", Consumer, AdvancedShared);
        var error = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(
            () => PreviewInterfaceRouteAsync("cross-project", workspace.WorkspaceId, sourcePath, "RefService", new CompositePreviewStore()));
        StringAssert.Contains(error.Message, "CS8953");
    }

    [TestMethod]
    [DataRow("standalone", false)]
    [DataRow("cross-project", false)]
    [DataRow("dependency-inversion", false)]
    [DataRow("extract-and-wire", false)]
    [DataRow("standalone", true)]
    [DataRow("cross-project", true)]
    [DataRow("dependency-inversion", true)]
    [DataRow("extract-and-wire", true)]
    public async Task Indexer_Handler_Attribute_Preserves_Getter_And_Setter_Binding(string route, bool setterOnly)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var sourcePath = await PrepareInterfaceRouteFixtureAsync(workspace, "RefService.cs",
            setterOnly ? SetterIndexerSource : GetterIndexerSource, "User.cs", Consumer, Shared);
        var stores = new CompositePreviewStore();
        var preview = await PreviewInterfaceRouteAsync(route, workspace.WorkspaceId, sourcePath, "RefService", stores);
        string interfaceText;
        if (route == "extract-and-wire")
        {
            interfaceText = stores.Retrieve(preview.PreviewToken, null)!.Value.Mutations
                .Single(m => m.FilePath == workspace.GetPath("Contracts", "IRefService.cs")).UpdatedContent
                ?? throw new AssertFailedException("The interface mutation has no content.");
        }
        else
        {
            var pending = PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;
            var project = pending.Projects.Single(p => p.Name == (route == "standalone" ? "SampleLib" : "Contracts"));
            interfaceText = (await project.Documents.Single(d => d.Name == "IRefService.cs").GetTextAsync()).ToString();
            await AssertCompilesAsync(pending);
        }
        StringAssert.Contains(interfaceText, "InterpolatedStringHandlerArgument");
    }

    [TestMethod]
    [DataRow("plain")]
    [DataRow("block")]
    [DataRow("line")]
    [DataRow("primary")]
    [DataRow("collision")]
    public async Task Unused_Constrained_Containing_Parameters_Are_In_Scope_For_Generic_Probe(string layout)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var sourcePath = await PrepareInterfaceRouteFixtureAsync(workspace, "RefService.cs", NestedSource(layout),
            "User.cs", NestedConsumer, Shared);
        var preview = await PreviewInterfaceRouteAsync("cross-project", workspace.WorkspaceId, sourcePath, "RefService", new CompositePreviewStore());
        var pending = PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;
        await AssertCompilesAsync(pending);
        var text = (await pending.Projects.Single(p => p.Name == "Contracts").Documents.Single(d => d.Name == "IRefService.cs").GetTextAsync()).ToString();
        StringAssert.Contains(text, "InterpolatedStringHandlerArgument");
    }

    [TestMethod]
    public void Handler_Refusal_Lists_All_Compiler_Ids()
    {
        var diagnostics = Enumerable.Range(1, 8).Reverse()
            .Select(number => Diagnostic.Create(new DiagnosticDescriptor("TEST00" + number, "title", "message", "test",
                DiagnosticSeverity.Error, isEnabledByDefault: true), Location.None))
            .ToArray();
        Assert.AreEqual("TEST001, TEST002, TEST003, TEST004, TEST005, TEST006, TEST007, TEST008",
            InterfaceHandlerBindingValidator.FormatIds(diagnostics.Concat(diagnostics)));
    }
}
