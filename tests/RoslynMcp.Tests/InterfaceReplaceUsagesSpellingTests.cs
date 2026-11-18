using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynMcp.Core.Services;
using RoslynMcp.Tests.Helpers;
using static RoslynMcp.Tests.TestInfrastructure.InterfaceExtractionTestContext;

namespace RoslynMcp.Tests;

/// <summary>
/// <c>replaceUsages</c> rewrites every spelling of the concrete type in a declaration position (qualified, aliased,
/// return type, local, property, generic argument, array, nullable), leaves positions that are not usages (construction,
/// static access, <c>typeof</c>, casts, <c>is</c>) alone, and refuses a rewrite that would not compile. Every rewrite is
/// proved by compiling the pending solution and resolving symbols, never by matching text.
/// </summary>
[TestClass]
public sealed class InterfaceReplaceUsagesSpellingTests : IsolatedWorkspaceTestBase
{
    private const string EchoSource = """
        namespace SampleLib;

        public class EchoService
        {
            public static int Count => 3;

            public int Echo(int value) => value + 1;
        }
        """;

    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task Qualified_And_Global_Qualified_Spellings_Are_Rewritten()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var pending = await PreviewAsync(workspace, EchoSource, """
            namespace Consumers;

            public class Holder
            {
                private SampleLib.EchoService _qualified = default!;
                public global::SampleLib.EchoService Global = default!;

                public void Use(SampleLib.EchoService parameter) { }
            }
            """);

        await AssertCompilesAsync(pending);
        var holder = await HolderAsync(pending);
        AssertGenerated(Field(holder, "_qualified").Type, "_qualified");
        AssertGenerated(Field(holder, "Global").Type, "Global");
        AssertGenerated(Method(holder, "Use").Parameters.Single().Type, "Use parameter");
    }

    [TestMethod]
    public async Task Aliased_Spelling_Is_Rewritten()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var pending = await PreviewAsync(workspace, EchoSource, """
            using E = SampleLib.EchoService;

            namespace Consumers;

            public class Holder
            {
                private E _aliased = default!;

                public void Use(E parameter) { }
            }
            """);

        await AssertCompilesAsync(pending);
        var holder = await HolderAsync(pending);
        AssertGenerated(Field(holder, "_aliased").Type, "_aliased");
        AssertGenerated(Method(holder, "Use").Parameters.Single().Type, "Use parameter");
    }

    [TestMethod]
    public async Task Return_Property_And_Local_Spellings_Are_Rewritten_And_Construction_Stays_Concrete()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var pending = await PreviewAsync(workspace, EchoSource, """
            using SampleLib;

            namespace Consumers;

            public class Holder
            {
                public EchoService Create() => new EchoService();

                public EchoService Property { get; set; } = new EchoService();

                public int Run()
                {
                    EchoService local = new EchoService();
                    return local.Echo(1);
                }
            }
            """);

        await AssertCompilesAsync(pending);
        var holder = await HolderAsync(pending);
        AssertGenerated(Method(holder, "Create").ReturnType, "Create return");
        AssertGenerated(holder.GetMembers("Property").OfType<IPropertySymbol>().Single().Type, "Property");
        var (tree, model) = await ModelAsync(pending);
        var run = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.Text == "Run");
        var declaration = run.DescendantNodes().OfType<VariableDeclarationSyntax>().Single();
        AssertGenerated(model.GetTypeInfo(declaration.Type).Type!, "local");
        var creation = run.DescendantNodes().OfType<ObjectCreationExpressionSyntax>().Single();
        Assert.AreEqual("EchoService", model.GetTypeInfo(creation).Type!.Name, "construction must stay concrete");
    }

    [TestMethod]
    public async Task Generic_Argument_Array_And_Nullable_Spellings_Are_Rewritten()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var pending = await PreviewAsync(workspace, EchoSource, """
            #nullable enable
            using System.Collections.Generic;
            using SampleLib;

            namespace Consumers;

            public class Holder
            {
                public List<EchoService> Items = new List<EchoService>();
                public EchoService[] Array = new EchoService[0];
                public EchoService? Maybe;
            }
            """);

        await AssertCompilesAsync(pending);
        var holder = await HolderAsync(pending);
        var items = (INamedTypeSymbol)Field(holder, "Items").Type;
        AssertGenerated(items.TypeArguments.Single(), "Items argument");
        AssertGenerated(((IArrayTypeSymbol)Field(holder, "Array").Type).ElementType, "Array element");
        AssertGenerated(Field(holder, "Maybe").Type, "Maybe");
    }

    [TestMethod]
    public async Task Positions_That_Are_Not_Usage_Replacement_Stay_Concrete()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        const string holderSource = """
            using SampleLib;

            namespace Consumers;

            public class Holder
            {
                public int Run(object o)
                {
                    var count = EchoService.Count;
                    var type = typeof(EchoService);
                    var matches = o is EchoService;
                    var cast = (EchoService)o;
                    return count + (matches ? 1 : 0) + (type is null ? 0 : 1) + cast.Echo(1);
                }
            }
            """;
        var pending = await PreviewAsync(workspace, EchoSource, holderSource);

        await AssertCompilesAsync(pending);
        var holder = pending.Projects.Single(p => p.Name == "SampleLib").Documents.Single(d => d.Name == "Holder.cs");
        Assert.AreEqual(holderSource, (await holder.GetTextAsync()).ToString());
    }

    [TestMethod]
    public async Task A_Member_Outside_The_Interface_Refuses_With_The_Gate_Text()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        const string echo = """
            namespace SampleLib;

            public class EchoService
            {
                public int Echo(int value) => value + 1;

                public int Other() => 2;
            }
            """;

        var refusal = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => PreviewAsync(workspace, echo, """
            using SampleLib;

            namespace Consumers;

            public class Holder
            {
                public int Run(EchoService service) => service.Other();
            }
            """, ["Echo"]));

        StringAssert.Contains(refusal.Message, "The generated interface introduces compiler errors");
        StringAssert.Contains(refusal.Message, "CS1061");
        StringAssert.Contains(refusal.Message, "SampleLib");
        Assert.IsFalse(File.Exists(workspace.GetPath("SampleLib", "IEchoService.cs")));
    }

    [TestMethod]
    public async Task A_Fluent_Self_Return_Is_Retyped_In_The_Class_And_The_Interface_Together()
    {
        // The projected interface member returns the concrete class. The generated interface file is part of the
        // sweep, so both declarations change together and the class still implements the member.
        await using var workspace = CreateIsolatedWorkspaceCopy();
        const string echo = """
            namespace SampleLib;

            public class EchoService
            {
                public EchoService WithName(string name) => this;

                public int Echo(int value) => value + 1;
            }
            """;

        var pending = await PreviewAsync(workspace, echo, """
            using SampleLib;

            namespace Consumers;

            public class Holder
            {
                public int Run(EchoService service) => service.WithName("x").Echo(1);
            }
            """);

        await AssertCompilesAsync(pending);
        var compilation = (await pending.Projects.Single(p => p.Name == "SampleLib").GetCompilationAsync())!;
        var concrete = compilation.GetTypeByMetadataName("SampleLib.EchoService")!;
        var contract = compilation.GetTypeByMetadataName("SampleLib.IEchoService")!;
        AssertGenerated(concrete.GetMembers("WithName").OfType<IMethodSymbol>().Single().ReturnType, "class WithName return");
        AssertGenerated(contract.GetMembers("WithName").OfType<IMethodSymbol>().Single().ReturnType, "interface WithName return");
        Assert.IsTrue(concrete.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, contract)));
        AssertGenerated((await HolderAsync(pending)).GetMembers("Run").OfType<IMethodSymbol>().Single().Parameters.Single().Type, "Run parameter");
    }

    private async Task<Solution> PreviewAsync(
        IsolatedWorkspaceScope workspace, string echoSource, string holderSource, IReadOnlyList<string>? memberNames = null)
    {
        await File.WriteAllTextAsync(workspace.GetPath("SampleLib", "EchoService.cs"), echoSource);
        await File.WriteAllTextAsync(workspace.GetPath("SampleLib", "Holder.cs"), holderSource);
        await workspace.LoadAsync(CancellationToken.None);
        var preview = await InterfaceExtractionService.PreviewExtractInterfaceAsync(workspace.WorkspaceId,
            workspace.GetPath("SampleLib", "EchoService.cs"), "EchoService", "IEchoService", memberNames, replaceUsages: true, CancellationToken.None);
        return PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;
    }

    private static async Task<INamedTypeSymbol> HolderAsync(Solution pending)
    {
        var compilation = (await pending.Projects.Single(p => p.Name == "SampleLib").GetCompilationAsync())!;
        return compilation.GetTypeByMetadataName("Consumers.Holder")!;
    }

    private static async Task<(SyntaxTree Tree, SemanticModel Model)> ModelAsync(Solution pending)
    {
        var compilation = (await pending.Projects.Single(p => p.Name == "SampleLib").GetCompilationAsync())!;
        var tree = compilation.SyntaxTrees.Single(t => Path.GetFileName(t.FilePath) == "Holder.cs");
        return (tree, compilation.GetSemanticModel(tree));
    }

    private static IFieldSymbol Field(INamedTypeSymbol holder, string name) => holder.GetMembers(name).OfType<IFieldSymbol>().Single();

    private static IMethodSymbol Method(INamedTypeSymbol holder, string name) => holder.GetMembers(name).OfType<IMethodSymbol>().Single();

    private static void AssertGenerated(ITypeSymbol type, string label)
    {
        Assert.AreEqual("IEchoService", type.Name, label);
        Assert.AreEqual("SampleLib", type.ContainingNamespace.ToDisplayString(), label);
    }
}
