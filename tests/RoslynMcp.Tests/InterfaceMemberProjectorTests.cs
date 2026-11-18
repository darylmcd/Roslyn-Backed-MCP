using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using static RoslynMcp.Tests.TestInfrastructure.InterfaceExtractionTestContext;

namespace RoslynMcp.Tests;

/// <summary>
/// Construct matrix for the shared interface-member projector: one source type carries every C# construct the
/// emitters are known to mishandle, both extraction services emit its interface, and the source type must
/// implement the emitted interface with zero compiler errors.
/// </summary>
[TestClass]
public sealed class InterfaceMemberProjectorTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    private const string SharedSource = """
        using System;

        namespace MatrixShared;

        public enum Color { Red = 1, Green = 2 }
        public class Outer<T> { public class Inner<U> { } public class Middle<U> { public class Deep<V> { } } }
        public delegate void Changed(object? sender, int value);
        public static class Ids { public const string Msg = "m"; }
        [AttributeUsage(AttributeTargets.All)]
        public sealed class MarkerAttribute : Attribute { public MarkerAttribute(string value) { } }
        public class MatrixBase
        {
            public class BaseNested { }
            public const int Max = 3;
            public virtual void Over<T>(T value) where T : class, IComparable<T> { }
        }
        """;

    private const string MatrixSourceText = """
        using System;
        using System.Collections.Generic;
        using System.Diagnostics.CodeAnalysis;
        using System.Runtime.CompilerServices;
        using System.Runtime.InteropServices;
        using MatrixShared;

        namespace Contracts;

        public unsafe partial class MatrixService : MatrixBase
        {
            private int _field;
            private readonly Dictionary<string, List<int>> _map = new();

            public ref int RefReturn() => ref _field;
            public ref readonly int RefReadonlyReturn() => ref _field;
            public ref int RefProperty => ref _field;
            // Pins (green before the attribute-scope change): generic ref returns, partial methods,
            // multi-declarator events and a qualified non-public constant default.
            public ref T RefGeneric<T>(T[] values) => ref values[0];
            public ref readonly T RefReadonlyGeneric<T>(T[] values) => ref values[0];
            public partial void Part(int a);
            public partial void Part(int a) { }
            public event Action? MultiA, MultiB;
            public void QualifiedHidden(int h = MatrixService.Hidden) { }
            public ref readonly int this[int index] => ref _field;
            public string this[string key, Color color] { get => key; set { } }

            public TResult Convert<TResult, TSource>(TSource value)
                where TResult : class, new()
                where TSource : struct, IComparable<TSource> => new TResult();
            public void Collect<T>(List<T> items) where T : notnull { }

            public event Changed? Raised;
            public event EventHandler<EventArgs>? Manual { add { } remove { } }
            public event Action? @delegate;

            public int GetSet { get; set; }
            public int InitOnly { get; init; }
            public int PrivateSet { get; private set; }
            public int PrivateGet { private get; set; }
            public int ExpressionBodied => 1;

            public void @class(int @event, string @string) { }
            public int @namespace { get; set; }
            public void Generic<@class>(@class value) where @class : class { }

            public int* Pointer(int* pointer, delegate*<int, void> callback) => pointer;
            public int* PointerProperty => null;

            public void Modify(ref int a, out int b, in int c, params int[] rest) { b = 0; }
            public void Defaults(int a = 5, string s = "x\"y", double d = double.NaN, Color c = Color.Green, int m = Max, DateTime when = default, BaseNested? nested = null) { }
            public void Metadata([Optional, DateTimeConstant(123)] DateTime d) { }
            public bool TryGet(string key, [NotNullWhen(true)] out string? value) { value = null; return false; }
            public void Log(string message, [CallerMemberName] string caller = "") { }
            public void Scoped(scoped Span<int> span, scoped ref int number) { }

            public Outer<string>.Inner<object> Substituted(Outer<string>.Middle<object>.Deep<int> deep) => throw new NotImplementedException();
            public BaseNested UseBase(BaseNested nested) => nested;
            public Dictionary<string, List<int>>.ValueCollection Values => _map.Values;
            public string? Maybe(List<string?> items, string[]? array) => null;

            public override void Over<T>(T value) { }
            private const int Hidden = 7;
            internal const Color Tint = Color.Green;
            public void HiddenDefault(int h = Hidden, Color tint = Tint) { }

            public static void StaticIsExcluded() { }
            protected void ProtectedIsExcluded() { }
        }
        """;

    private static readonly string[] _expectedMembers =
    [
        "Collect", "Convert", "Defaults", "HiddenDefault", "Over", "ExpressionBodied", "GetSet", "Generic", "InitOnly", "Log", "Manual", "Maybe",
        "Metadata", "Modify", "MultiA", "MultiB", "Part", "QualifiedHidden", "RefGeneric", "RefReadonlyGeneric", "Pointer", "PointerProperty", "PrivateGet", "PrivateSet", "RefProperty", "RefReadonlyReturn",
        "RefReturn", "Raised", "Scoped", "Substituted", "TryGet", "UseBase", "Values", "class", "delegate", "namespace",
        "this[]", "this[]",
    ];

    [TestMethod]
    [DataRow("standalone")]
    [DataRow("cross-project")]
    public async Task Every_Construct_Is_Projected_Into_An_Interface_The_Source_Type_Implements(string route)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        PrepareFixture(workspace);
        var sourcePath = workspace.GetPath("SampleLib", "MatrixService.cs");
        await File.WriteAllTextAsync(sourcePath, MatrixSourceText);
        await workspace.LoadAsync();
        var before = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        await AssertCompilesAsync(before);

        var preview = route == "standalone"
            ? await InterfaceExtractionService.PreviewExtractInterfaceAsync(
                workspace.WorkspaceId, sourcePath, "MatrixService", "IMatrixService", null, false, CancellationToken.None)
            : await CrossProjectRefactoringService.PreviewExtractInterfaceAsync(
                workspace.WorkspaceId, sourcePath, "MatrixService", "IMatrixService", "Contracts", CancellationToken.None);

        var pending = await ReparseSerializedDocumentsAsync(PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution);
        var interfaceProject = pending.Projects.Single(p => p.Name == (route == "standalone" ? "SampleLib" : "Contracts"));
        var interfaceDocument = interfaceProject.Documents.Single(d => d.Name == "IMatrixService.cs");
        var interfaceText = (await interfaceDocument.GetTextAsync()).ToString();

        // The source type's new base list makes any signature drift a CS0535/CS0738/CS0425 error here.
        foreach (var project in pending.Projects)
        {
            var errors = (await project.GetCompilationAsync())!.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
            Assert.AreEqual(0, errors.Length, project.Name + ": " + string.Join(Environment.NewLine, errors.Select(e => e.ToString())) + Environment.NewLine + interfaceText);
        }

        var contract = (await interfaceProject.GetCompilationAsync())!.GetTypeByMetadataName(
            "Contracts.IMatrixService")!;
        var emitted = contract.GetMembers().Where(m => m is IPropertySymbol or IEventSymbol or IMethodSymbol { MethodKind: MethodKind.Ordinary })
            .Select(m => m is IPropertySymbol { IsIndexer: true } ? "this[]" : m.Name).Order(StringComparer.Ordinal).ToArray();
        CollectionAssert.AreEqual(_expectedMembers.Order(StringComparer.Ordinal).ToArray(), emitted, interfaceText);

        // Fidelity beyond "it compiles": the contract keeps what callers observe.
        IMethodSymbol Method(string name) => contract.GetMembers(name).OfType<IMethodSymbol>().Single();
        IPropertySymbol Property(string name) => contract.GetMembers(name).OfType<IPropertySymbol>().Single();
        Assert.AreEqual(RefKind.Ref, Method("RefReturn").RefKind, interfaceText);
        Assert.AreEqual(RefKind.RefReadOnly, Method("RefReadonlyReturn").RefKind, interfaceText);
        Assert.AreEqual(RefKind.Ref, Property("RefProperty").RefKind, interfaceText);
        Assert.IsNull(Property("PrivateSet").SetMethod, interfaceText);
        Assert.IsNull(Property("PrivateGet").GetMethod, interfaceText);
        Assert.IsTrue(Property("InitOnly").SetMethod!.IsInitOnly, interfaceText);
        Assert.AreEqual(2, contract.GetMembers().OfType<IPropertySymbol>().Count(p => p.IsIndexer), interfaceText);
        Assert.IsTrue(Method("Over").TypeParameters[0].HasReferenceTypeConstraint, interfaceText);
        Assert.AreEqual(7, Method("HiddenDefault").Parameters[0].ExplicitDefaultValue, interfaceText);
        Assert.AreEqual(7, Method("QualifiedHidden").Parameters[0].ExplicitDefaultValue, interfaceText);
        StringAssert.Contains(interfaceText, "ref T RefGeneric<T>(T[] values);");
        StringAssert.Contains(interfaceText, "ref readonly T RefReadonlyGeneric<T>(T[] values);");
        StringAssert.Contains(interfaceText, "void Part(int a);");
        StringAssert.Contains(interfaceText, "event Action? MultiA;");
        StringAssert.Contains(interfaceText, "event Action? MultiB;");
        Assert.AreEqual(RefKind.Ref, Method("RefGeneric").RefKind, interfaceText);
        Assert.AreEqual(RefKind.RefReadOnly, Method("RefReadonlyGeneric").RefKind, interfaceText);
        Assert.AreEqual(1, Method("Generic").TypeParameters.Length, interfaceText);
        Assert.IsTrue(Method("Generic").TypeParameters[0].HasReferenceTypeConstraint, interfaceText);
        Assert.AreEqual(2, Method("Convert").TypeParameters.Count(t => t.HasReferenceTypeConstraint || t.HasValueTypeConstraint), interfaceText);
        var modify = Method("Modify").Parameters;
        CollectionAssert.AreEqual(new[] { RefKind.Ref, RefKind.Out, RefKind.In, RefKind.None }, modify.Select(p => p.RefKind).ToArray(), interfaceText);
        Assert.IsTrue(modify[3].IsParams, interfaceText);
        var defaults = Method("Defaults").Parameters;
        Assert.IsTrue(defaults.All(p => p.HasExplicitDefaultValue), interfaceText);
        Assert.AreEqual("x\"y", defaults[1].ExplicitDefaultValue, interfaceText);
        Assert.IsTrue(double.IsNaN((double)defaults[2].ExplicitDefaultValue!), interfaceText);
        Assert.AreEqual(3, defaults[4].ExplicitDefaultValue, interfaceText);
        Assert.IsTrue(Method("TryGet").Parameters[1].GetAttributes().Any(a => a.AttributeClass?.Name == "NotNullWhenAttribute"), interfaceText);
        Assert.IsTrue(Method("Log").Parameters[1].GetAttributes().Any(a => a.AttributeClass?.Name == "CallerMemberNameAttribute"), interfaceText);
        Assert.AreEqual(ScopedKind.ScopedValue, Method("Scoped").Parameters[0].ScopedKind, interfaceText);
        Assert.AreEqual(ScopedKind.ScopedRef, Method("Scoped").Parameters[1].ScopedKind, interfaceText);
        Assert.AreEqual(NullableAnnotation.Annotated, Method("Maybe").ReturnType.NullableAnnotation, interfaceText);
        Assert.AreEqual(NullableAnnotation.Annotated, ((INamedTypeSymbol)Method("Maybe").Parameters[0].Type).TypeArguments[0].NullableAnnotation, interfaceText);
        StringAssert.EndsWith(Method("Substituted").Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), "Outer<string>.Middle<object>.Deep<int>", interfaceText);
    }

    private const string RefReturnSourceText = """
        using System.Diagnostics.CodeAnalysis;

        namespace Contracts;

        public struct RefFieldStruct
        {
            private int _f;

            [UnscopedRef] public ref int V => ref _f;
            public ref int P { [UnscopedRef] get => ref _f; }
            [UnscopedRef] public ref int M() => ref _f;
            [UnscopedRef] public ref readonly int RoMethod() => ref _f;
            [UnscopedRef] public ref readonly int RoProperty => ref _f;
            [UnscopedRef] public ref readonly int this[int index] => ref _f;

            public int Value => _f;
            public int Plain() => _f;
        }

        public ref struct RefStructSource
        {
            private int _f;

            [UnscopedRef] public ref int V => ref _f;
            public ref int P { [UnscopedRef] get => ref _f; }
            [UnscopedRef] public ref int M() => ref _f;
            [UnscopedRef] public ref readonly int RoMethod() => ref _f;
            [UnscopedRef] public ref readonly int RoProperty => ref _f;
            [UnscopedRef] public ref readonly int this[int index] => ref _f;

            public int Value => _f;
            public int Plain() => _f;
        }
        """;

    [TestMethod]
    [DataRow("standalone", "RefFieldStruct")]
    [DataRow("cross-project", "RefFieldStruct")]
    [DataRow("standalone", "RefStructSource")]
    [DataRow("cross-project", "RefStructSource")]
    public async Task Ref_Returning_Struct_And_RefStruct_Members_Keep_Contract_And_UnscopedRef(string route, string typeName)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        PrepareFixture(workspace);
        var sourcePath = workspace.GetPath("SampleLib", "MatrixService.cs");
        await File.WriteAllTextAsync(sourcePath, RefReturnSourceText);
        await workspace.LoadAsync();
        await AssertCompilesAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));

        var interfaceName = "I" + typeName;
        var preview = route == "standalone"
            ? await InterfaceExtractionService.PreviewExtractInterfaceAsync(
                workspace.WorkspaceId, sourcePath, typeName, interfaceName, null, false, CancellationToken.None)
            : await CrossProjectRefactoringService.PreviewExtractInterfaceAsync(
                workspace.WorkspaceId, sourcePath, typeName, interfaceName, "Contracts", CancellationToken.None);

        var pending = await ReparseSerializedDocumentsAsync(PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution);
        var interfaceProject = pending.Projects.Single(p => p.Name == (route == "standalone" ? "SampleLib" : "Contracts"));
        var interfaceText = (await interfaceProject.Documents.Single(d => d.Name == interfaceName + ".cs").GetTextAsync()).ToString();

        // CS9102 (an implementation's [UnscopedRef] missing from the interface member) and CS8170/CS0535 surface here.
        await AssertCompilesAsync(pending);

        var contract = (await interfaceProject.GetCompilationAsync())!.GetTypeByMetadataName("Contracts." + interfaceName)!;
        IMethodSymbol Method(string name) => contract.GetMembers(name).OfType<IMethodSymbol>().Single();
        IPropertySymbol Property(string name) => contract.GetMembers(name).OfType<IPropertySymbol>().Single();
        IPropertySymbol Indexer() => contract.GetMembers().OfType<IPropertySymbol>().Single(p => p.IsIndexer);
        static bool HasUnscopedRef(ISymbol? symbol) =>
            symbol is not null && symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "System.Diagnostics.CodeAnalysis.UnscopedRefAttribute");

        Assert.AreEqual(RefKind.Ref, Property("V").RefKind, interfaceText);
        Assert.IsTrue(HasUnscopedRef(Property("V")), interfaceText);
        Assert.AreEqual(RefKind.Ref, Property("P").RefKind, interfaceText);
        Assert.IsFalse(HasUnscopedRef(Property("P")), "Accessor-level [UnscopedRef] must stay on the accessor.\n" + interfaceText);
        Assert.IsTrue(HasUnscopedRef(Property("P").GetMethod), interfaceText);
        Assert.AreEqual(RefKind.Ref, Method("M").RefKind, interfaceText);
        Assert.IsTrue(HasUnscopedRef(Method("M")), interfaceText);
        Assert.AreEqual(RefKind.RefReadOnly, Method("RoMethod").RefKind, interfaceText);
        Assert.IsTrue(HasUnscopedRef(Method("RoMethod")), interfaceText);
        Assert.AreEqual(RefKind.RefReadOnly, Property("RoProperty").RefKind, interfaceText);
        Assert.IsTrue(HasUnscopedRef(Property("RoProperty")), interfaceText);
        Assert.AreEqual(RefKind.RefReadOnly, Indexer().RefKind, interfaceText);
        Assert.IsTrue(HasUnscopedRef(Indexer()), interfaceText);

        // Value-return controls: no ref kind and no invented [UnscopedRef].
        Assert.AreEqual(RefKind.None, Property("Value").RefKind, interfaceText);
        Assert.IsFalse(HasUnscopedRef(Property("Value")), interfaceText);
        Assert.AreEqual(RefKind.None, Method("Plain").RefKind, interfaceText);
        Assert.IsFalse(HasUnscopedRef(Method("Plain")), interfaceText);
    }

    [TestMethod]
    public async Task Positional_Record_Properties_Without_Declaring_Member_Syntax_Are_Projected_Through_The_Generator()
    {
        // `Name` / `Age` are declared by ParameterSyntax, not by a property declaration, so the
        // projector must fall back to SyntaxGenerator.Declaration and still emit an init contract.
        await using var workspace = CreateIsolatedWorkspaceCopy();
        PrepareFixture(workspace);
        var sourcePath = workspace.GetPath("SampleLib", "MatrixService.cs");
        await File.WriteAllTextAsync(sourcePath, MatrixSourceText + "\npublic record PositionalRecord(string Name, List<int>? Ages) { public string Greet(string who) => who; }\n");
        await workspace.LoadAsync();

        var preview = await InterfaceExtractionService.PreviewExtractInterfaceAsync(
            workspace.WorkspaceId, sourcePath, "PositionalRecord", "IPositionalRecord", null, false, CancellationToken.None);

        var pending = await ReparseSerializedDocumentsAsync(PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution);
        var project = pending.Projects.Single(p => p.Name == "SampleLib");
        var text = (await project.Documents.Single(d => d.Name == "IPositionalRecord.cs").GetTextAsync()).ToString();
        var errors = (await project.GetCompilationAsync())!.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.AreEqual(0, errors.Length, string.Join(Environment.NewLine, errors.Select(e => e.ToString())) + Environment.NewLine + text);
        var contract = (await project.GetCompilationAsync())!.GetTypeByMetadataName("Contracts.IPositionalRecord")!;
        Assert.IsTrue(contract.GetMembers("Name").OfType<IPropertySymbol>().Single().SetMethod!.IsInitOnly, text);
        Assert.AreEqual(NullableAnnotation.Annotated, contract.GetMembers("Ages").OfType<IPropertySymbol>().Single().NullableAnnotation, text);
        Assert.AreEqual(1, contract.GetMembers("Greet").Length, text);
    }

    private const string NestedOwnedSource = """
        using System.Collections.Generic;

        namespace Contracts;

        public class NestedOwner
        {
            public class Inner { }
            public Inner Make() => new();
            public void Take(List<Inner> items) { }
        }
        """;

    [TestMethod]
    [DataRow("standalone")]
    [DataRow("cross-project")]
    public async Task Nested_Type_Owned_By_The_Source_Type_Is_Qualified_Standalone_And_Refused_Across_Projects(string route)
    {
        // Pin (green before the attribute-scope change): the nested type is spelled through its owner, and a
        // receiving project that cannot reference the owner is refused instead of receiving uncompilable text.
        await using var workspace = CreateIsolatedWorkspaceCopy();
        PrepareFixture(workspace);
        var sourcePath = workspace.GetPath("SampleLib", "MatrixService.cs");
        await File.WriteAllTextAsync(sourcePath, NestedOwnedSource);
        await workspace.LoadAsync();

        if (route == "cross-project")
        {
            var refusal = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                CrossProjectRefactoringService.PreviewExtractInterfaceAsync(
                    workspace.WorkspaceId, sourcePath, "NestedOwner", "INestedOwner", "Contracts", CancellationToken.None));
            StringAssert.Contains(refusal.Message, "NestedOwner.Inner");
            StringAssert.Contains(refusal.Message, "circular dependency");
            return;
        }

        var preview = await InterfaceExtractionService.PreviewExtractInterfaceAsync(
            workspace.WorkspaceId, sourcePath, "NestedOwner", "INestedOwner", null, false, CancellationToken.None);
        var pending = await ReparseSerializedDocumentsAsync(PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution);
        var text = (await pending.Projects.Single(p => p.Name == "SampleLib").Documents.Single(d => d.Name == "INestedOwner.cs").GetTextAsync()).ToString();
        StringAssert.Contains(text, "NestedOwner.Inner Make();");
        StringAssert.Contains(text, "void Take(List<NestedOwner.Inner> items);");
        await AssertCompilesAsync(pending);
    }

    private const string DroppedHostSource = """
        using MatrixShared;

        namespace Contracts;

        public class DroppedHost
        {
            [Marker(Ids.Msg)] public void Member([Marker(Ids.Msg)] int a) { }
        }
        """;

    [TestMethod]
    public async Task Dropped_Attribute_Arguments_Record_No_Referenced_Types_Aliases_Or_Unqualified_Types()
    {
        // Asserted on the projector itself so no route gate can mask a spurious record.
        await using var workspace = CreateIsolatedWorkspaceCopy();
        PrepareFixture(workspace);
        var sourcePath = workspace.GetPath("SampleLib", "MatrixService.cs");
        await File.WriteAllTextAsync(sourcePath, DroppedHostSource);
        await workspace.LoadAsync();
        var document = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId).Projects.Single(p => p.Name == "SampleLib").Documents
            .Single(d => d.Name == "MatrixService.cs");
        var type = (await document.GetSemanticModelAsync())!.Compilation.GetTypeByMetadataName("Contracts.DroppedHost")!;

        var projector = await InterfaceMemberProjector.CreateAsync(document, new InterfaceNullableProjection(), document.Project, CancellationToken.None);
        var projected = type.GetMembers().Where(InterfaceMemberProjector.IsProjectable).Select(projector.Project).ToArray();

        var declaration = projected.Single().ToFullString();
        Assert.IsFalse(declaration.Contains("Marker", StringComparison.Ordinal), declaration);
        Assert.IsFalse(projector.ReferencedTypes.Any(t => t.Name is "Ids" or "MarkerAttribute"), string.Join(",", projector.ReferencedTypes.Select(t => t.Name)));
        Assert.IsFalse(projector.ReferencedUnqualifiedTypes.Any(t => t.Name is "Ids" or "MarkerAttribute"),
            string.Join(",", projector.ReferencedUnqualifiedTypes.Select(t => t.Name)));
        Assert.AreEqual(0, projector.ReferencedUsingAliases.Count);
    }

    private static void PrepareFixture(IsolatedWorkspaceScope workspace)
    {
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        AddProjectToCopiedSolution(workspace.RootPath, "Shared", "net10.0");
        File.WriteAllText(workspace.GetPath("Shared", "MatrixShared.cs"), SharedSource);
        var solution = XDocument.Load(workspace.SolutionPath);
        var kept = new[] { "SampleLib/SampleLib.csproj", "Contracts/Contracts.csproj", "Shared/Shared.csproj" };
        foreach (var project in solution.Descendants("Project").Where(p => !kept.Contains(p.Attribute("Path")!.Value.Replace('\\', '/'))).ToArray())
            project.Remove();
        solution.Save(workspace.SolutionPath);
        foreach (var name in new[] { "SampleLib", "Contracts" })
        {
            var path = workspace.GetPath(name, name + ".csproj");
            var xml = XDocument.Load(path);
            xml.Root!.Add(new XElement("PropertyGroup", new XElement("AllowUnsafeBlocks", "true")),
                new XElement("ItemGroup", new XElement("ProjectReference", new XAttribute("Include", "../Shared/Shared.csproj"))));
            if (name == "SampleLib")
                xml.Root.Add(new XElement("PropertyGroup", new XElement("EnableDefaultCompileItems", "false")),
                    new XElement("ItemGroup", new XElement("Compile", new XAttribute("Include", "MatrixService.cs"))));
            xml.Save(path);
        }
    }
}
