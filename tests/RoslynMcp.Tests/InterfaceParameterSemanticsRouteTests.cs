using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;
using static RoslynMcp.Tests.TestInfrastructure.InterfaceExtractionTestContext;

namespace RoslynMcp.Tests;

/// <summary>
/// Parameter semantics of an extracted interface on every route (standalone, cross-project,
/// dependency-inversion, extract-and-wire): each scenario is previewed, applied, reloaded and compiled, so the
/// emitted interface must carry the parameter modifiers, defaults and contract attributes the source type
/// declares. Signature types live in a third project (<c>Shared</c>) so the receiving project can name them;
/// that dependency ownership is a separate row.
/// </summary>
[TestClass]
public sealed class InterfaceParameterSemanticsRouteTests : InterfaceRouteTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    private const string SharedSource = """
        namespace SharedLib;

        public enum Hue { Red = 1, Green = 2 }
        public static class Limits { public const int Cap = 9; }
        public class ParamBase { public const int Max = 3; }
        """;

    private const string ConsumerSource = """
        namespace Contracts;

        public class ParamConsumer
        {
            public ParamConsumer(ParamService service) { }
        }
        """;

    private static string ServiceSource(string members) => $$"""
        using System;
        using System.Runtime.CompilerServices;
        using System.Runtime.InteropServices;
        using SharedLib;

        namespace Contracts;

        public class ParamService : ParamBase
        {
            private int _field;
            private string _name = "";
            public int Count;
            public const int OwnMax = 4;
        {{members}}
        }
        """;

    private const string ModifierMembers = """
            public void Modify(ref int @event, out int r, in int i) { r = 0; }
            public void Read(ref readonly int i) { }
            public void Scoped(scoped Span<int> s, scoped ref int i) { }
            public void Collect(params ReadOnlySpan<int> rest) { }
            public void Spread(params int[] rest) { }
        """;

    private const string DefaultMembers = """
            public void NumericDefaults(double a = double.PositiveInfinity, double b = double.NegativeInfinity, double c = double.NaN) { }
            public void Literals(float f = 1.5f, decimal m = 1m, char c = '\n', string s = "a\"b\\c\n", Hue h = Hue.Green) { }
            public void MetadataDefaults([Optional, DateTimeConstant(123)] DateTime d) { }
        """;

    private const string AttributeDefaultMembers = """
            public void AttributeDefaults(
                [Optional, DefaultParameterValue(5)] int a,
                [Optional, DefaultParameterValue(OwnMax)] int b,
                [Optional, DecimalConstant(0, 0, 0, 0, 7)] decimal c) { }
        """;

    private const string CallerMembers = """
            public void Caller([CallerMemberName] string c = "", int m = Max) { }
            public int this[int a, [CallerMemberName] string c = "", int m = Max] => a;
        """;

    private const string NameofMembers = """
            public void Names(string a = nameof(_name), string b = nameof(Max), string c = nameof(Count), string d = nameof(Names)) { }
            public void Referencing(string a, [CallerArgumentExpression(nameof(a))] string b = "") { }
        """;

    private const string SourceOwnedConstantMembers = """
            public enum Mode { A = 1, B = 2 }
            public void Own(int z = OwnMax, int e = (int)Mode.B) { }
        """;

    private const string ForeignAttributeMembers = """
            public void ForeignAttribute([Optional, DefaultParameterValue(Limits.Cap)] int a) { }
        """;

    private const string DroppedAttributeMembers = """
            public void Dropped([MarshalAs(UnmanagedType.I4)] int a) { }
        """;

    private const string UnnameableConstantMembers = """
            private enum Priv { A = 1 }
            internal const int Internal = 5;
            public void Hidden(int x = (int)Priv.A, int y = Internal + 1, int z = OwnMax) { }
        """;

    private const string NegativeEnumMembers = """
            internal const Hue Unknown = (Hue)(-1);
            public void Negative(Hue u = Unknown) { }
        """;

    private const string ForeignConstantMembers = """
            public void Foreign(int c = Limits.Cap) { }
        """;

    private const string ArglistMembers = """
            public void Write(int v, __arglist) { }
            public void Other() { }
        """;

    [TestMethod]
    [DataRow("standalone")]
    [DataRow("cross-project")]
    [DataRow("dependency-inversion")]
    [DataRow("extract-and-wire")]
    public async Task Parameter_Modifiers_Survive_Preview_Apply_And_Reload(string route) =>
        await AssertRouteAsync(route, ModifierMembers, (contract, text) =>
        {
            var modify = Method(contract, "Modify").Parameters;
            CollectionAssert.AreEqual(new[] { RefKind.Ref, RefKind.Out, RefKind.In }, modify.Select(p => p.RefKind).ToArray(), text);
            Assert.AreEqual("event", modify[0].Name, text);
            Assert.AreEqual(RefKind.RefReadOnlyParameter, Method(contract, "Read").Parameters[0].RefKind, text);
            var scoped = Method(contract, "Scoped").Parameters;
            Assert.AreEqual(ScopedKind.ScopedValue, scoped[0].ScopedKind, text);
            Assert.AreEqual(ScopedKind.ScopedRef, scoped[1].ScopedKind, text);
            Assert.IsTrue(Method(contract, "Collect").Parameters[0].IsParamsCollection, text);
            Assert.IsTrue(Method(contract, "Spread").Parameters[0].IsParamsArray, text);
        });

    [TestMethod]
    [DataRow("standalone")]
    [DataRow("cross-project")]
    [DataRow("dependency-inversion")]
    [DataRow("extract-and-wire")]
    public async Task Default_Values_Survive_Preview_Apply_And_Reload(string route) =>
        await AssertRouteAsync(route, DefaultMembers, (contract, text) =>
        {
            var numeric = Method(contract, "NumericDefaults").Parameters;
            Assert.IsTrue(double.IsPositiveInfinity((double)numeric[0].ExplicitDefaultValue!), text);
            Assert.IsTrue(double.IsNegativeInfinity((double)numeric[1].ExplicitDefaultValue!), text);
            Assert.IsTrue(double.IsNaN((double)numeric[2].ExplicitDefaultValue!), text);
            var literals = Method(contract, "Literals").Parameters;
            Assert.AreEqual(1.5f, literals[0].ExplicitDefaultValue, text);
            Assert.AreEqual(1m, literals[1].ExplicitDefaultValue, text);
            Assert.AreEqual('\n', literals[2].ExplicitDefaultValue, text);
            Assert.AreEqual("a\"b\\c\n", literals[3].ExplicitDefaultValue, text);
            Assert.AreEqual(2, literals[4].ExplicitDefaultValue, text);
            var metadata = Method(contract, "MetadataDefaults").Parameters[0];
            Assert.IsTrue(metadata.HasExplicitDefaultValue, text);
            Assert.IsTrue(metadata.GetAttributes().Any(a => a.AttributeClass?.Name == "DateTimeConstantAttribute"), text);
        });

    [TestMethod]
    [DataRow("standalone")]
    [DataRow("cross-project")]
    [DataRow("dependency-inversion")]
    [DataRow("extract-and-wire")]
    public async Task Attribute_Encoded_Defaults_Keep_Their_Attribute_And_Value(string route) =>
        await AssertRouteAsync(route, AttributeDefaultMembers, (contract, text) =>
        {
            var parameters = Method(contract, "AttributeDefaults").Parameters;
            Assert.IsTrue(parameters.All(p => p.IsOptional), text);
            CollectionAssert.AreEqual(new object[] { 5, 4, 7m }, parameters.Select(p => p.ExplicitDefaultValue).ToArray(), text);
        });

    [TestMethod]
    [DataRow("standalone")]
    [DataRow("cross-project")]
    [DataRow("dependency-inversion")]
    [DataRow("extract-and-wire")]
    public async Task Contract_Attribute_Next_To_A_Rewritten_Default_Survives(string route) =>
        await AssertRouteAsync(route, CallerMembers, (contract, text) =>
        {
            var caller = Method(contract, "Caller").Parameters;
            Assert.IsTrue(caller[0].GetAttributes().Any(a => a.AttributeClass?.Name == "CallerMemberNameAttribute"), text);
            Assert.AreEqual(3, caller[1].ExplicitDefaultValue, text);
            var indexer = contract.GetMembers().OfType<IPropertySymbol>().Single(p => p.IsIndexer).Parameters;
            Assert.IsTrue(indexer[1].GetAttributes().Any(a => a.AttributeClass?.Name == "CallerMemberNameAttribute"), text);
            Assert.AreEqual(3, indexer[2].ExplicitDefaultValue, text);
        });

    [TestMethod]
    [DataRow("standalone")]
    [DataRow("cross-project")]
    [DataRow("dependency-inversion")]
    [DataRow("extract-and-wire")]
    public async Task Nameof_Defaults_Do_Not_Name_Members_The_Interface_Lacks(string route) =>
        await AssertRouteAsync(route, NameofMembers, (contract, text) =>
        {
            var names = Method(contract, "Names").Parameters;
            CollectionAssert.AreEqual(new object[] { "_name", "Max", "Count", "Names" }, names.Select(p => p.ExplicitDefaultValue).ToArray(), text);
        });

    [TestMethod]
    [DataRow("standalone")]
    [DataRow("cross-project")]
    [DataRow("dependency-inversion")]
    [DataRow("extract-and-wire")]
    public async Task Constant_Defaults_Naming_Members_The_Interface_Cannot_Name_Are_Written_As_Values(string route) =>
        await AssertRouteAsync(route, UnnameableConstantMembers, (contract, text) =>
        {
            var hidden = Method(contract, "Hidden").Parameters;
            CollectionAssert.AreEqual(new object[] { 1, 6, 4 }, hidden.Select(p => p.ExplicitDefaultValue).ToArray(), text);
        });

    [TestMethod]
    [DataRow("standalone")]
    [DataRow("cross-project")]
    [DataRow("dependency-inversion")]
    [DataRow("extract-and-wire")]
    public async Task Negative_Enum_Default_Behind_A_Hidden_Constant_Is_Written_Parenthesized(string route) =>
        await AssertRouteAsync(route, NegativeEnumMembers, (contract, text) =>
            Assert.AreEqual(-1, Method(contract, "Negative").Parameters[0].ExplicitDefaultValue, text));

    [TestMethod]
    [DataRow("standalone")]
    [DataRow("cross-project")]
    [DataRow("dependency-inversion")]
    [DataRow("extract-and-wire")]
    public async Task Default_Naming_A_Constant_Of_An_Otherwise_Unreferenced_Type_Keeps_Its_Value(string route) =>
        await AssertRouteAsync(route, ForeignConstantMembers, (contract, text) =>
            Assert.AreEqual(9, Method(contract, "Foreign").Parameters[0].ExplicitDefaultValue, text));

    [TestMethod]
    [DataRow("standalone")]
    [DataRow("cross-project")]
    [DataRow("dependency-inversion")]
    [DataRow("extract-and-wire")]
    public async Task Contract_Attribute_Argument_Naming_A_Constant_Of_An_Otherwise_Unreferenced_Type_Imports_Its_Namespace(string route) =>
        await AssertRouteAsync(route, ForeignAttributeMembers, (contract, text) =>
            Assert.AreEqual(9, Method(contract, "ForeignAttribute").Parameters[0].ExplicitDefaultValue, text));

    [TestMethod]
    [DataRow("standalone")]
    [DataRow("cross-project")]
    [DataRow("dependency-inversion")]
    [DataRow("extract-and-wire")]
    public async Task Dropped_Attribute_Argument_Does_Not_Import_Its_Namespace(string route) =>
        await AssertRouteAsync(route, DroppedAttributeMembers, (contract, text) =>
        {
            Assert.AreEqual(0, Method(contract, "Dropped").Parameters[0].GetAttributes().Length, text);
            Assert.IsFalse(text.Contains("InteropServices", StringComparison.Ordinal), text);
        });

    [TestMethod]
    [DataRow("standalone")]
    [DataRow("cross-project")]
    [DataRow("dependency-inversion")]
    [DataRow("extract-and-wire")]
    public async Task Nameof_Of_A_Parameter_In_A_Contract_Attribute_Is_Left_As_Written(string route) =>
        await AssertRouteAsync(route, NameofMembers, (contract, text) =>
        {
            var attribute = Method(contract, "Referencing").Parameters[1].GetAttributes().Single(a => a.AttributeClass?.Name == "CallerArgumentExpressionAttribute");
            Assert.AreEqual("a", attribute.ConstructorArguments[0].Value, text);
            StringAssert.Contains(text, "CallerArgumentExpression(nameof(a))");
        });

    [TestMethod]
    [DataRow("standalone")]
    [DataRow("cross-project")]
    [DataRow("dependency-inversion")]
    [DataRow("extract-and-wire")]
    public async Task Source_Assembly_Constants_Stay_Symbolic_Only_Where_The_Interface_Shares_The_Source_Assembly(string route) =>
        await AssertRouteAsync(route, SourceOwnedConstantMembers, (contract, text) =>
        {
            CollectionAssert.AreEqual(new object[] { 4, 2 }, Method(contract, "Own").Parameters.Select(p => p.ExplicitDefaultValue).ToArray(), text);
            // A different receiving assembly cannot name ParamService, so the value is written instead.
            Assert.AreEqual(route == "standalone", text.Contains("ParamService.OwnMax", StringComparison.Ordinal), text);
            Assert.AreEqual(route == "standalone", text.Contains("ParamService.Mode.B", StringComparison.Ordinal), text);
        });

    [TestMethod]
    [DataRow("standalone")]
    [DataRow("cross-project")]
    [DataRow("dependency-inversion")]
    [DataRow("extract-and-wire")]
    public async Task Arglist_Method_Is_Extracted_Into_A_Compiling_Interface(string route) =>
        await AssertRouteAsync(route, ArglistMembers, (contract, text) =>
        {
            Assert.IsTrue(Method(contract, "Write").IsVararg, text);
            Assert.AreEqual(1, Method(contract, "Write").Parameters.Length, text);
        });

    [TestMethod]
    public void Compiler_Accepts_Arglist_On_An_Interface_Member_And_Its_Implementation()
    {
        // The projector emits `__arglist` as written (no refusal): this pins that premise to the compiler this
        // repo builds against, so a language change that rejects it fails here instead of in a user's extraction.
        var tree = CSharpSyntaxTree.ParseText(
            "interface I { void Write(int v, __arglist); } class C : I { public void Write(int v, __arglist) { } }");
        var compilation = CSharpCompilation.Create(
            "ArglistProbe", [tree], [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()).ToArray();
        Assert.AreEqual(0, errors.Length, string.Join(Environment.NewLine, errors));
        Assert.IsTrue(compilation.GetTypeByMetadataName("I")!.GetMembers("Write").OfType<IMethodSymbol>().Single().IsVararg);
    }

    private static IMethodSymbol Method(INamedTypeSymbol contract, string name) => contract.GetMembers(name).OfType<IMethodSymbol>().Single();

    private static Task<string> PrepareFixtureAsync(IsolatedWorkspaceScope workspace, string members) =>
        PrepareInterfaceRouteFixtureAsync(workspace, "ParamService.cs", ServiceSource(members), "ParamConsumer.cs", ConsumerSource, SharedSource);

    private static Task<RoslynMcp.Core.Models.RefactoringPreviewDto> PreviewRouteAsync(
        string route, string workspaceId, string sourcePath, CompositePreviewStore stores) =>
        PreviewInterfaceRouteAsync(route, workspaceId, sourcePath, "ParamService", stores);

    private static async Task AssertRouteAsync(string route, string members, Action<INamedTypeSymbol, string> verify)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var sourcePath = await PrepareFixtureAsync(workspace, members);
        var before = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var stores = new CompositePreviewStore();
        var receiver = route == "standalone" ? "SampleLib" : "Contracts";

        var preview = await PreviewRouteAsync(route, workspace.WorkspaceId, sourcePath, stores);

        var pending = route == "extract-and-wire"
            ? SerializedCompositePreview(before, stores.Retrieve(preview.PreviewToken)!.Value.Mutations)
            : PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;
        pending = await ReparseSerializedDocumentsAsync(pending);
        await VerifyAsync(pending, receiver, "preview", verify);

        var applied = route == "extract-and-wire"
            ? await new CompositeApplyOrchestrator(WorkspaceManager, stores, undoService: UndoService).ApplyCompositeAsync(preview.PreviewToken, CompositeAdmission.AdmitAll, CancellationToken.None)
            : await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "interface_parameter_semantics_apply", CancellationToken.None);
        Assert.IsTrue(applied.Success, applied.Error);
        await workspace.ReloadAsync();

        await VerifyAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId), receiver, "reload", verify);
    }

    private static async Task VerifyAsync(Solution solution, string receiver, string stage, Action<INamedTypeSymbol, string> verify)
    {
        var project = solution.Projects.Single(p => p.Name == receiver);
        var interfaceText = (await project.Documents.Single(d => d.Name == "IParamService.cs").GetTextAsync()).ToString();
        try
        {
            await AssertCompilesAsync(solution);
        }
        catch (AssertFailedException failure)
        {
            throw new AssertFailedException(stage + ": " + failure.Message + Environment.NewLine + interfaceText, failure);
        }

        var contract = (await project.GetCompilationAsync())!.GetTypeByMetadataName("Contracts.IParamService")!;
        Assert.IsNotNull(contract, stage + Environment.NewLine + interfaceText);
        verify(contract, stage + Environment.NewLine + interfaceText);
    }
}
