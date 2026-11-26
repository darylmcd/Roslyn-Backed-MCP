using System.Text.Json;
using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class FindImplementationsAbstractTests : IsolatedWorkspaceTestBase
{
    private static IsolatedWorkspaceScope _scope = null!;
    private static string WorkspaceId => _scope.WorkspaceId;
    private static string FixturePath => _scope.GetPath("SampleLib", "AbstractFixture.cs");

    private const string Source = """
        namespace AbstractFixture;
        public abstract class Base<T>
        {
            public abstract T Run();
            public abstract T Value { get; }
            public abstract T this[int index] { get; }
            public abstract event System.Action Changed;
            public virtual void OrdinaryVirtual() { }
            public void Ordinary() { }
        }
        public abstract class Mid<T> : Base<T>
        {
            public abstract override T Run();
            public abstract override T Value { get; }
            public abstract override T this[int index] { get; }
            public abstract override event System.Action Changed;
        }
        public sealed class Leaf : Mid<int>
        {
            public override int Run() => 1;
            public override int Value => 1;
            public override int this[int index] => index;
            public override event System.Action Changed { add { } remove { } }
        }
        public abstract class ConcreteMid<T> : Base<T>
        {
            public override T Run() => default!;
            public override T Value => default!;
            public override T this[int index] => default!;
            public override event System.Action Changed { add { } remove { } }
        }
        public sealed class Inherited : ConcreteMid<int> { }
        public partial class Sibling : Base<int>
        {
            public override int Run() => 2;
            public override int Value => 2;
            public override int this[int index] => index;
            public override event System.Action Changed { add { } remove { } }
        }
        public class Unrelated
        {
            public int Run() => 3;
            public int Value => 3;
            public int this[int index] => index;
            public event System.Action Changed { add { } remove { } }
        }
        public abstract class Empty { }
        public interface IContract
        {
            int Run();
            int Value { get; }
            event System.Action Changed;
        }
        public class Implementation : IContract
        {
            public int Run() => 4;
            public int Value => 4;
            public event System.Action Changed { add { } remove { } }
        }
        public interface IContainer
        {
            public abstract class NestedBase { }
        }
        public sealed class NestedLeaf : IContainer.NestedBase { }
        public class Uses
        {
            public Base<int>? Constructed;
        }
        """;

    private const string PartialSource = """
        namespace AbstractFixture;
        public partial class Sibling { }
        """;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        _scope = await InitializeWithCleanupAsync(
            CreateIsolatedWorkspaceCopy(),
            async (scope, ct) =>
            {
                await File.WriteAllTextAsync(scope.GetPath("SampleLib", "AbstractFixture.cs"), Source, ct);
                await File.WriteAllTextAsync(scope.GetPath("SampleLib", "AbstractPartial.cs"), PartialSource, ct);
                await File.WriteAllTextAsync(scope.GetPath("SampleLib", "AbstractPartial.g.cs"), PartialSource, ct);
                await scope.LoadAsync(ct);
                var project = WorkspaceManager.GetCurrentSolution(scope.WorkspaceId).Projects.Single(p => p.Name == "SampleLib");
                var compilation = await project.GetCompilationAsync(ct);
                Assert.IsNotNull(compilation);
                var errors = compilation.GetDiagnostics(ct).Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
                Assert.AreEqual(0, errors.Length, string.Join(Environment.NewLine, errors.Select(d => d.ToString())));
            });
    }

    [ClassCleanup]
    public static void ClassCleanup() => _scope?.Dispose();

    [TestMethod]
    public async Task AbstractClass_AllLocators_ReturnConcreteTransitiveDeclarations()
    {
        await AssertAllPathsAsync(SymbolLocator.ByMetadataName("AbstractFixture.Base`1"), ClassDeclarations());
        await AssertAllPathsAsync(At("public abstract class Base<T>", "Base"), ClassDeclarations());
        // Constructed handle replay is tracked by symbol-handle-constructed-generic-roundtrip.
        var constructed = At("public Base<int>? Constructed;", "Base");
        var expected = ClassDeclarations();
        await AssertServicePathsAsync(constructed, expected, includeGeneratedPartials: false);
        await AssertToolPathAsync(constructed, expected, includeGeneratedPartials: false);
    }

    [TestMethod]
    public async Task AbstractClass_NestedInInterface_ReturnsConcreteDerivedClass()
    {
        await AssertAllPathsAsync(At("public abstract class NestedBase", "NestedBase"),
            [ExpectedInType("NestedLeaf", "public sealed class NestedLeaf", "NestedLeaf")]);
    }

    [TestMethod]
    public async Task AbstractClass_GeneratedOptIn_PreservesEveryDeclaration()
    {
        var expected = ClassDeclarations().Append(Expected("AbstractPartial.g.cs", PartialSource, "public partial class Sibling", "Sibling")).ToArray();
        await AssertAllPathsAsync(SymbolLocator.ByMetadataName("AbstractFixture.Base`1"), expected, includeGeneratedPartials: true);
    }

    [TestMethod]
    [DataRow("Run", "public abstract T Run();", "Run", "public override T Run()", "public override int Run()")]
    [DataRow("Value", "public abstract T Value", "Value", "public override T Value", "public override int Value")]
    [DataRow("this[]", "public abstract T this[int index]", "this", "public override T this[int index]", "public override int this[int index]")]
    [DataRow("Changed", "public abstract event System.Action Changed;", "Changed", "public override event System.Action Changed", "public override event System.Action Changed")]
    public async Task AbstractMember_AllPaths_ExcludeReabstractedAndInheritedDuplicates(
        string name, string declaration, string token, string genericOverride, string concreteOverride)
    {
        var expected = new[]
        {
            ExpectedInType("ConcreteMid<T>", genericOverride, token),
            ExpectedInType("Leaf", concreteOverride, token),
            ExpectedInType("Sibling", concreteOverride, token)
        };
        await AssertAllPathsAsync(At(declaration, token), expected);
        var relationships = await SymbolRelationshipService.GetSymbolRelationshipsAsync(
            WorkspaceId, At(declaration, token), preferDeclaringMember: false, CancellationToken.None);
        Assert.IsNotNull(relationships, name);
        AssertLocations(expected, relationships.Implementations);
        Assert.AreEqual(name, relationships.Symbol.Name);
        Assert.AreEqual(1, relationships.Definitions.Count);
        Assert.IsNull(relationships.Hint);
    }

    [TestMethod]
    public async Task InterfaceAndOrdinaryControls_KeepExistingBehavior()
    {
        await AssertAllPathsAsync(SymbolLocator.ByMetadataName("AbstractFixture.IContract"),
            [ExpectedInType("Implementation", "public class Implementation", "Implementation")]);
        foreach (var pair in new[] { ("int Run();", "Run"), ("int Value { get; }", "Value"), ("event System.Action Changed;", "Changed") })
        {
            await AssertAllPathsAsync(AtInType("IContract", pair.Item1, pair.Item2), [ExpectedInType("Implementation",
                pair.Item2 == "Run" ? "public int Run()" : pair.Item2 == "Value" ? "public int Value" : "public event System.Action Changed",
                pair.Item2)]);
        }
        foreach (var locator in new[]
        {
            SymbolLocator.ByMetadataName("AbstractFixture.Unrelated"),
            SymbolLocator.ByMetadataName("AbstractFixture.Empty"),
            At("public virtual void OrdinaryVirtual()", "OrdinaryVirtual"),
            At("public void Ordinary()", "Ordinary"),
            AtInType("Leaf", "public override int Run()", "Run")
        })
        {
            await AssertAllPathsAsync(locator, []);
        }
    }

    [TestMethod]
    public async Task AbstractClass_RelationshipConsumer_ReturnsConcreteDeclarations()
    {
        var result = await SymbolRelationshipService.GetSymbolRelationshipsAsync(
            WorkspaceId, SymbolLocator.ByMetadataName("AbstractFixture.Base`1"), preferDeclaringMember: false, CancellationToken.None);
        Assert.IsNotNull(result);
        AssertLocations(ClassDeclarations(), result.Implementations);
        Assert.AreEqual("Base", result.Symbol.Name);
        Assert.AreEqual(1, result.Definitions.Count);
        Assert.IsNull(result.Hint);
    }

    private static LocationDto[] ClassDeclarations() =>
    [
        ExpectedInType("Leaf", "public sealed class Leaf", "Leaf"),
        ExpectedInType("Inherited", "public sealed class Inherited", "Inherited"),
        ExpectedInType("Sibling", "public partial class Sibling", "Sibling"),
        Expected("AbstractPartial.cs", PartialSource, "public partial class Sibling", "Sibling")
    ];

    private static async Task AssertAllPathsAsync(SymbolLocator locator, LocationDto[] expected, bool includeGeneratedPartials = false)
    {
        var symbol = await AssertServicePathsAsync(locator, expected, includeGeneratedPartials);
        var handle = SymbolHandleSerializer.CreateHandle(symbol);
        AssertLocations(expected, await ReferenceService.FindImplementationsAsync(WorkspaceId, SymbolLocator.ByHandle(handle), CancellationToken.None, includeGeneratedPartials));
        await AssertToolPathAsync(locator, expected, includeGeneratedPartials);
    }

    private static async Task<ISymbol> AssertServicePathsAsync(SymbolLocator locator, LocationDto[] expected, bool includeGeneratedPartials)
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var symbol = await SymbolResolver.ResolveOrThrowAsync(solution, locator, CancellationToken.None);
        AssertLocations(expected, await ReferenceService.FindImplementationsAsync(WorkspaceId, locator, CancellationToken.None, includeGeneratedPartials));
        AssertLocations(expected, await ReferenceService.FindImplementationsAsync(WorkspaceId, symbol, CancellationToken.None, includeGeneratedPartials));
        return symbol;
    }

    private static async Task AssertToolPathAsync(SymbolLocator locator, LocationDto[] expected, bool includeGeneratedPartials)
    {
        var json = await SymbolTools.FindImplementations(WorkspaceExecutionGate, WorkspaceManager, ReferenceService,
            WorkspaceId, locator.FilePath, locator.Line, locator.Column, locator.SymbolHandle, locator.MetadataName,
            includeGeneratedPartials, CancellationToken.None);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        CollectionAssert.AreEquivalent(new[] { "count", "items", "includeGeneratedPartials" }, root.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.AreEqual(expected.Length, root.GetProperty("count").GetInt32());
        Assert.AreEqual(includeGeneratedPartials, root.GetProperty("includeGeneratedPartials").GetBoolean());
        var items = root.GetProperty("items").Deserialize<LocationDto[]>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.IsNotNull(items);
        AssertLocations(expected, items);
    }

    private static void AssertLocations(IEnumerable<LocationDto> expected, IReadOnlyList<LocationDto> actual)
    {
        static string Key(LocationDto location) =>
            $"{Path.GetFileName(location.FilePath)}:{location.StartLine}:{location.StartColumn}:{location.EndLine}:{location.EndColumn}";
        var ordered = expected.OrderBy(l => l.FilePath, StringComparer.Ordinal).ThenBy(l => l.StartLine).ThenBy(l => l.StartColumn).Select(Key).ToArray();
        CollectionAssert.AreEqual(ordered, actual.Select(Key).ToArray());
    }

    private static SymbolLocator At(string declaration, string token) => AtInType("", declaration, token);

    private static SymbolLocator AtInType(string type, string declaration, string token)
    {
        var location = ExpectedInType(type, declaration, token);
        return SymbolLocator.BySource(FixturePath, location.StartLine, location.StartColumn);
    }

    private static LocationDto ExpectedInType(string type, string declaration, string token)
    {
        var start = string.IsNullOrEmpty(type) ? 0 : Source.IndexOf(type, StringComparison.Ordinal);
        Assert.IsTrue(start >= 0, type);
        start = Source.LastIndexOf('\n', start) + 1;
        return Expected("AbstractFixture.cs", Source, declaration, token, start);
    }

    private static LocationDto Expected(string file, string source, string declaration, string token, int start = 0)
    {
        var declarationStart = source.IndexOf(declaration, start, StringComparison.Ordinal);
        Assert.IsTrue(declarationStart >= 0, declaration);
        var offset = source.IndexOf(token, declarationStart, StringComparison.Ordinal);
        var line = source[..offset].Count(c => c == '\n') + 1;
        var column = offset - source.LastIndexOf('\n', offset);
        return new LocationDto(_scope.GetPath("SampleLib", file), line, column, line, column + token.Length, null, null);
    }
}
