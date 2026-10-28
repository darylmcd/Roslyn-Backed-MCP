using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;
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
        public class MatrixBase
        {
            public class BaseNested { }
            public const int Max = 3;
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

        public unsafe class MatrixService : MatrixBase
        {
            private int _field;
            private readonly Dictionary<string, List<int>> _map = new();

            public ref int RefReturn() => ref _field;
            public ref readonly int RefReadonlyReturn() => ref _field;
            public ref int RefProperty => ref _field;
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

            public static void StaticIsExcluded() { }
            protected void ProtectedIsExcluded() { }
        }
        """;

    private static readonly string[] _expectedMembers =
    [
        "Collect", "Convert", "Defaults", "ExpressionBodied", "GetSet", "Generic", "InitOnly", "Log", "Manual", "Maybe",
        "Metadata", "Modify", "Pointer", "PointerProperty", "PrivateGet", "PrivateSet", "RefProperty", "RefReadonlyReturn",
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
