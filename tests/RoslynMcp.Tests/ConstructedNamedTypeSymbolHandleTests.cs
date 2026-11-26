using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ConstructedNamedTypeSymbolHandleTests
{
    private const string Source = """
        #nullable enable
        extern alias Red;
        extern alias Blue;
        namespace Fixture;
        public class Plain { public void Member() { } }
        public abstract class Base<T> { }
        public sealed class Descendant : Base<int> { }
        public abstract class Mid<T> : Base<T> { public Base<T>? OwnerArgument; }
        public class Outer<T> { public class Inner { } public class InnerOf<U> { } }
        public class Uses<T> where T : class
        {
            public Base<int>? Source;
            public System.Collections.Generic.List<string?>? Metadata;
            public Outer<int>.Inner? Nested;
            public Outer<int>.InnerOf<string?>? NestedGeneric;
            public Base<T?>? AnnotatedOwnerArgument;
            public Base<Red::Shared.Argument>? RedArgument;
            public Base<Blue::Shared.Argument>? BlueArgument;
            public Base<(string? label, int, int c, int d, int e, int f, int g, string? tail)>? Tuple;
            public Base<string?[]?>? Vector;
            public Base<string?[,]?>? Matrix;
            public Base<nint>? NativeSigned;
            public Base<nuint>? NativeUnsigned;
            public Base<dynamic>? Dynamic;
            public void Method<TMethod>(System.Collections.Generic.List<TMethod> value) { }
            public void Mixed<TMethod>(System.Collections.Generic.Dictionary<T, TMethod> value) { }
            public void NestedMethod<TMethod>(Outer<TMethod>.Inner value) { }
        }
        public partial class MethodOwner
        {
            public void SameName<T>(Base<T> value) { }
            public void AliasOverload<T>(Red::Shared.Argument marker, Base<T> value) { }
            public void AliasOverload<T>(Blue::Shared.Argument marker, Base<T> value) { }
            public static int Rebind<T>(Red::Shared.Argument marker, Base<T> value) => 0;
            public ref T RefMethod<T>(ref Base<T> value, T[] items) => ref items[0];
            partial void Partial<T>(Base<T> value);
            partial void Partial<T>(Base<T> value) { }
            public void Locals<TOuter>()
            {
                void Local<T>(Base<T> value) { }
                void Parent<TParent>() { void Inner<T>(Base<T> value, TOuter outer, TParent parent) { } }
                { void Sibling<T>(Base<T> value) { } }
                { void Sibling<T>(Base<T> value) { } }
                System.Action action = () => { void LambdaLocal<T>(Base<T> value) { } };
            }
        }
        public interface IClosed<TInterface>
        {
            void Go<T>(TInterface marker, System.Collections.Generic.List<T> value);
        }
        public class ClosedInterfaceImpl : IClosed<int>
        {
            void IClosed<int>.Go<T>(int marker, System.Collections.Generic.List<T> value) { }
        }
        public class AliasImplementations : Red::Shared.IAlias, Blue::Shared.IAlias
        {
            void Red::Shared.IAlias.Go<T>(System.Collections.Generic.List<T> value) { }
            void Blue::Shared.IAlias.Go<T>(System.Collections.Generic.List<T> value) { }
        }
        """;

    [TestMethod]
    [DataRow("Source")]
    [DataRow("Metadata")]
    [DataRow("Nested")]
    [DataRow("NestedGeneric")]
    [DataRow("AnnotatedOwnerArgument")]
    [DataRow("OwnerArgument")]
    [DataRow("RedArgument")]
    [DataRow("BlueArgument")]
    [DataRow("Tuple")]
    [DataRow("Vector")]
    [DataRow("Matrix")]
    [DataRow("NativeSigned")]
    [DataRow("NativeUnsigned")]
    [DataRow("Dynamic")]
    public async Task ConstructedHandle_RoundTripsExactType(string field)
    {
        using var workspace = new AdhocWorkspace();
        var (solution, compilation) = await CreateFixtureAsync(workspace);
        var owner = field == "OwnerArgument" ? "Fixture.Mid`1" : "Fixture.Uses`1";
        var expected = (INamedTypeSymbol)((IFieldSymbol)compilation.GetTypeByMetadataName(owner)!.GetMembers(field).Single()).Type;
        var raw = SymbolHandleSerializer.CreateHandle(expected);
        var resolved = await SymbolHandleSerializer.ResolveHandleAsync(solution, raw, CancellationToken.None);
        var diagnostic = $"Resolution failed for {field}: {expected}.";
        if (resolved is null && expected.TypeArguments.FirstOrDefault() is INamedTypeSymbol { IsNativeIntegerType: true } argument)
        {
            var reconstructed = compilation.CreateNativeIntegerTypeSymbol(signed: argument.SpecialType == SpecialType.System_IntPtr);
            diagnostic += $" Source: {argument.MetadataName}, {argument.SpecialType}, {argument.ContainingAssembly.Identity}, {argument.NullableAnnotation}."
                + $" Pinned native factory: {reconstructed.MetadataName}, {reconstructed.SpecialType}, {reconstructed.IsNativeIntegerType},"
                + $" {reconstructed.ContainingAssembly.Identity}, {reconstructed.NullableAnnotation}.";
        }
        Assert.IsInstanceOfType<INamedTypeSymbol>(resolved, diagnostic);
        AssertExactType(expected, (INamedTypeSymbol)resolved);
    }

    [TestMethod]
    public async Task LegacyDefinitionsAndMembers_KeepTheirOriginalBytes()
    {
        using var workspace = new AdhocWorkspace();
        var (_, compilation) = await CreateFixtureAsync(workspace);
        var plain = compilation.GetTypeByMetadataName("Fixture.Plain")!;
        var generic = compilation.GetTypeByMetadataName("Fixture.Base`1")!;
        var nested = compilation.GetTypeByMetadataName("Fixture.Outer`1+Inner")!;
        var red = (IAssemblySymbol)compilation.GetAssemblyOrModuleSymbol(
            compilation.References.Single(reference => reference.Properties.Aliases.Contains("Red")))!;
        var metadataNested = red.GetTypeByMetadataName("Shared.Argument+Nested")!;
        var metadataMember = compilation.GetSpecialType(SpecialType.System_String).GetMembers("Substring")
            .OfType<IMethodSymbol>().Single(method => method.Parameters.Length == 1);
        ISymbol[] symbols = [plain, generic, generic.ConstructUnboundGenericType(), nested, metadataNested,
            plain.GetMembers("Member").Single(), metadataMember];
        foreach (var symbol in symbols)
        {
            var location = symbol.Locations.FirstOrDefault(candidate => candidate.IsInSource)?.GetLineSpan();
            var member = SymbolEqualityComparer.Default.Equals(symbol, metadataMember)
                ? new SymbolHandleSerializer.MemberIdentity(metadataMember.ContainingAssembly.Identity.ToString(),
                    "System.String", "M:System.String.Substring(System.Int32)~System.String")
                : null;
            var legacy = new LegacyPayload(symbol is INamedTypeSymbol ? symbol.ToDisplayString() : null,
                symbol.ToDisplayString(), location?.Path, location?.StartLinePosition.Line + 1,
                location?.StartLinePosition.Character + 1, member);
            Assert.AreEqual(Encode(legacy), SymbolHandleSerializer.CreateHandle(symbol), symbol.ToDisplayString());
        }
    }

    [TestMethod]
    [DataRow("Fixture.Uses`1", "Method")]
    [DataRow("Fixture.Uses`1", "Mixed")]
    [DataRow("Fixture.Uses`1", "NestedMethod")]
    [DataRow("Fixture.MethodOwner", "SameName")]
    [DataRow("Fixture.MethodOwner", "RefMethod")]
    public async Task MethodOwner_RoundTripsExactConstruction(string declaringType, string name)
    {
        using var workspace = new AdhocWorkspace();
        var (solution, compilation) = await CreateFixtureAsync(workspace);
        var method = compilation.GetTypeByMetadataName(declaringType)!.GetMembers(name).OfType<IMethodSymbol>().Single();
        var expected = method.Parameters[0].Type;
        var resolved = await SymbolHandleSerializer.ResolveHandleAsync(solution, SymbolHandleSerializer.CreateHandle(expected), CancellationToken.None);
        Assert.IsInstanceOfType<INamedTypeSymbol>(resolved);
        AssertExactType(expected, (INamedTypeSymbol)resolved);
    }

    [TestMethod]
    [DataRow("AliasOverload")]
    [DataRow("AliasInterface")]
    public async Task AliasedMethodOwners_RoundTripEachBoundSlot(string scenario)
    {
        using var workspace = new AdhocWorkspace();
        var (solution, compilation) = await CreateFixtureAsync(workspace);
        var type = compilation.GetTypeByMetadataName("Fixture." + (scenario == "AliasOverload" ? "MethodOwner" : "AliasImplementations"))!;
        var methods = type.GetMembers().OfType<IMethodSymbol>()
            .Where(method => scenario == "AliasOverload" ? method.Name == scenario : !method.ExplicitInterfaceImplementations.IsEmpty)
            .ToArray();
        Assert.HasCount(2, methods);
        var handles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var method in methods)
        {
            var expected = method.Parameters[^1].Type;
            var handle = SymbolHandleSerializer.CreateHandle(expected);
            var resolved = await SymbolHandleSerializer.ResolveHandleAsync(solution, handle, CancellationToken.None);
            Assert.IsInstanceOfType<INamedTypeSymbol>(resolved);
            AssertExactType(expected, (INamedTypeSymbol)resolved);
            Assert.IsTrue(handles.Add(handle), "Distinct aliased slots must mint different handles.");
        }
    }

    [TestMethod]
    [DataRow("Red::Shared.Argument marker", "Blue::Shared.Argument marker")]
    [DataRow("static int Rebind", "static long Rebind")]
    [DataRow("public static int Rebind", "public int Rebind")]
    public async Task StaleMethodOwner_ReturnsNoSymbolWhenBindingChanges(string before, string after)
    {
        using var workspace = new AdhocWorkspace();
        var (solution, compilation) = await CreateFixtureAsync(workspace);
        var method = compilation.GetTypeByMetadataName("Fixture.MethodOwner")!.GetMembers("Rebind").OfType<IMethodSymbol>().Single();
        var handle = SymbolHandleSerializer.CreateHandle(method.Parameters[^1].Type);
        Assert.IsNotNull(await SymbolHandleSerializer.ResolveHandleAsync(solution, handle, CancellationToken.None));
        var document = solution.Projects.Single().Documents.Single();
        var changed = solution.WithDocumentText(document.Id, SourceText.From(Source.Replace(before, after, StringComparison.Ordinal)));
        Assert.IsNull(await SymbolHandleSerializer.ResolveHandleAsync(changed, handle, CancellationToken.None));
    }

    [TestMethod]
    public async Task ExplicitInterfaceOwner_KeepsUnchangedImplementingSlot()
    {
        using var workspace = new AdhocWorkspace();
        var (solution, compilation) = await CreateFixtureAsync(workspace);
        var method = compilation.GetTypeByMetadataName("Fixture.ClosedInterfaceImpl")!.GetMembers()
            .OfType<IMethodSymbol>().Single(member => !member.ExplicitInterfaceImplementations.IsEmpty);
        var handle = SymbolHandleSerializer.CreateHandle(method.Parameters[^1].Type);
        Assert.IsNotNull(await SymbolHandleSerializer.ResolveHandleAsync(solution, handle, CancellationToken.None));
        var document = solution.Projects.Single().Documents.Single();
        var changed = solution.WithDocumentText(document.Id,
            SourceText.From(Source.Replace("Go<T>(TInterface marker", "Go<T>(int marker", StringComparison.Ordinal)));
        var resolved = await SymbolHandleSerializer.ResolveHandleAsync(changed, handle, CancellationToken.None);
        Assert.IsInstanceOfType<INamedTypeSymbol>(resolved);
        Assert.AreEqual(handle, SymbolHandleSerializer.CreateHandle(resolved));
    }

    [TestMethod]
    public async Task PartialMethodDefinitionAndImplementation_ReplayTheSameSlot()
    {
        using var workspace = new AdhocWorkspace();
        var (solution, compilation) = await CreateFixtureAsync(workspace);
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var declarations = (await tree.GetRootAsync()).DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(syntax => syntax.Identifier.ValueText == "Partial")
            .ToArray();
        Assert.HasCount(2, declarations);
        var definition = model.GetDeclaredSymbol(declarations.Single(syntax => syntax.Body is null))!;
        var implementation = model.GetDeclaredSymbol(declarations.Single(syntax => syntax.Body is not null))!;
        Assert.IsTrue(SymbolEqualityComparer.Default.Equals(definition, implementation.PartialDefinitionPart));
        var definitionArgument = definition.Parameters[0].Type;
        var implementationArgument = implementation.Parameters[0].Type;
        var definitionHandle = SymbolHandleSerializer.CreateHandle(definitionArgument);
        var implementationHandle = SymbolHandleSerializer.CreateHandle(implementationArgument);
        Assert.AreEqual(definitionHandle, implementationHandle);
        var resolved = await SymbolHandleSerializer.ResolveHandleAsync(solution, implementationHandle, CancellationToken.None);
        Assert.IsInstanceOfType<INamedTypeSymbol>(resolved);
        AssertExactType(definitionArgument, (INamedTypeSymbol)resolved);
        var replayed = (ITypeParameterSymbol)((INamedTypeSymbol)resolved).TypeArguments[0];
        Assert.IsTrue(SymbolEqualityComparer.Default.Equals(definition, replayed.DeclaringMethod));
        Assert.AreEqual(((ITypeParameterSymbol)((INamedTypeSymbol)implementationArgument).TypeArguments[0]).Ordinal, replayed.Ordinal);
    }

    [TestMethod]
    public async Task MetadataMethodOwner_RoundTripsExactConstruction()
    {
        using var workspace = new AdhocWorkspace();
        var (solution, compilation) = await CreateFixtureAsync(workspace);
        var red = (IAssemblySymbol)compilation.GetAssemblyOrModuleSymbol(
            compilation.References.Single(reference => reference.Properties.Aliases.Contains("Red")))!;
        var method = red.GetTypeByMetadataName("Shared.MetadataOwner")!.GetMembers("Method").OfType<IMethodSymbol>().Single();
        Assert.IsTrue(method.DeclaringSyntaxReferences.IsEmpty);
        var expected = method.Parameters[0].Type;
        var resolved = await SymbolHandleSerializer.ResolveHandleAsync(solution, SymbolHandleSerializer.CreateHandle(expected), CancellationToken.None);
        Assert.IsInstanceOfType<INamedTypeSymbol>(resolved);
        AssertExactType(expected, (INamedTypeSymbol)resolved);
    }

    [TestMethod]
    [DataRow("Local")]
    [DataRow("LambdaLocal")]
    [DataRow("Inner")]
    [DataRow("Sibling")]
    public async Task LocalFunctionTypeOwners_NewHandlesAreDisplayOnly(string name)
    {
        using var workspace = new AdhocWorkspace();
        var (solution, compilation) = await CreateFixtureAsync(workspace);
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var declarations = (await tree.GetRootAsync()).DescendantNodes().OfType<LocalFunctionStatementSyntax>()
            .Where(syntax => syntax.Identifier.ValueText == name)
            .ToArray();
        Assert.HasCount(name == "Sibling" ? 2 : 1, declarations);
        foreach (var declaration in declarations)
        {
            var method = model.GetDeclaredSymbol(declaration)!;
            var constructed = (INamedTypeSymbol)method.Parameters[0].Type;
            Assert.IsTrue(SymbolEqualityComparer.Default.Equals(constructed.TypeArguments[0], method.TypeParameters[0]));
            await AssertDisplayOnlyAsync(solution, constructed);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UnsupportedMethodOwners_NewHandlesAreDisplayOnly(bool overBudget)
    {
        using var workspace = new AdhocWorkspace();
        var (solution, compilation) = await CreateFixtureAsync(workspace);
        var uses = compilation.GetTypeByMetadataName("Fixture.Uses`1")!;
        var constructed = uses.Construct(compilation.GetSpecialType(SpecialType.System_String));
        var method = (IMethodSymbol)(overBudget
            ? uses.GetMembers("Method").Single()
            : constructed.GetMembers("Method").Single());
        ITypeSymbol value = method.Parameters[0].Type;
        if (overBudget)
        {
            var root = compilation.GetTypeByMetadataName("Fixture.Base`1")!;
            for (var depth = 0; depth < 32; depth++)
            {
                value = compilation.CreateArrayTypeSymbol(value);
            }
            value = root.Construct(value);
        }
        await AssertDisplayOnlyAsync(solution, value);
    }

    private static async Task AssertDisplayOnlyAsync(Solution solution, ITypeSymbol symbol)
    {
        var handle = SymbolHandleSerializer.CreateHandle(symbol);
        var resolved = await SymbolHandleSerializer.ResolveHandleAsync(solution, handle, CancellationToken.None);
        Assert.IsNull(resolved, "Unsupported method constructions must not select the open declaration.");
        Assert.AreEqual(Encode(new LegacyPayload(null, symbol.ToDisplayString(), null, null, null, null)), handle);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MethodOwnerFallback_ClassifiesWithoutRecursiveStackGrowth(bool methodOwned)
    {
        using var workspace = new AdhocWorkspace();
        var (_, compilation) = await CreateFixtureAsync(workspace);
        var containing = compilation.GetTypeByMetadataName("Fixture.Uses`1")!;
        var method = containing.GetMembers("Method").OfType<IMethodSymbol>().Single();
        ITypeSymbol symbol = methodOwned ? method.TypeParameters[0] : containing.TypeParameters[0];
        const string classifierName = "ContainsMethodOwnedParameter";
        var activeFrames = 0;
        var observedNodes = 0;
        for (var index = 0; index < 32; index++)
        {
            var element = symbol;
            var array = DispatchProxy.Create<IArrayTypeSymbol, ObservedArraySymbol>();
            ((ObservedArraySymbol)(object)array).ReadElement = () =>
            {
                observedNodes++;
                activeFrames = Math.Max(activeFrames, new StackTrace().GetFrames().Count(frame =>
                    frame.GetMethod() is { } caller && caller.DeclaringType == typeof(SymbolHandleSerializer) &&
                    caller.Name == classifierName));
                return element;
            };
            symbol = array;
        }

        var classifier = typeof(SymbolHandleSerializer).GetMethod(classifierName, BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.AreEqual(methodOwned, (bool)classifier.Invoke(null, [symbol])!);
        Assert.AreEqual(32, observedNodes);
        Assert.AreEqual(1, activeFrames, "The full-depth walk must use one classifier frame.");
    }

    public class ObservedArraySymbol : DispatchProxy
    {
        public Func<ITypeSymbol> ReadElement { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Assert.AreEqual("get_ElementType", targetMethod?.Name);
            return ReadElement();
        }
    }

    [TestMethod]
    public async Task OverBudgetTypeOwner_KeepLegacyBytes()
    {
        using var workspace = new AdhocWorkspace();
        var (_, compilation) = await CreateFixtureAsync(workspace);
        var generic = compilation.GetTypeByMetadataName("Fixture.Base`1")!;
        ITypeSymbol argument = generic.TypeParameters[0];
        for (var depth = 0; depth < 32; depth++)
        {
            argument = compilation.CreateArrayTypeSymbol(argument);
        }
        var deep = generic.Construct(argument);
        var source = deep.Locations.First(location => location.IsInSource).GetLineSpan();
        Assert.AreEqual(Encode(new LegacyPayload(deep.ToDisplayString(), deep.ToDisplayString(), source.Path,
                source.StartLinePosition.Line + 1, source.StartLinePosition.Character + 1, null)),
            SymbolHandleSerializer.CreateHandle(deep));
    }

    [TestMethod]
    public async Task MethodOwner_SurvivesSourceMovement()
    {
        using var workspace = new AdhocWorkspace();
        var (solution, compilation) = await CreateFixtureAsync(workspace);
        var method = compilation.GetTypeByMetadataName("Fixture.MethodOwner")!.GetMembers("Rebind").OfType<IMethodSymbol>().Single();
        var original = SymbolHandleSerializer.CreateHandle(method.Parameters[^1].Type);
        var document = solution.Projects.Single().Documents.Single();
        var shifted = solution.WithDocumentText(document.Id, SourceText.From(Environment.NewLine + Source));
        var resolved = await SymbolHandleSerializer.ResolveHandleAsync(shifted, original, CancellationToken.None);
        Assert.IsInstanceOfType<INamedTypeSymbol>(resolved);
        Assert.AreEqual(original, SymbolHandleSerializer.CreateHandle(resolved));
    }

    [TestMethod]
    [DataRow("Shared")]
    [DataRow("IndependentOwner")]
    [DataRow("IndependentSignatureType")]
    public async Task MethodOwnerEquivalentAndIndependentCompilations(string scenario)
    {
        const string source = """
            public class MethodOwner
            {
                public void Method<T>(Local marker, System.Collections.Generic.List<T> argument) { }
            }
            """;
        using var workspace = new AdhocWorkspace();
        var solution = AddProject(AddProject(workspace.CurrentSolution, "First", "Shared"), "Second", "Shared");
        var first = solution.Projects.First();
        var second = solution.Projects.Last();
        solution = solution.AddDocument(DocumentId.CreateNewId(first.Id), "First.cs", source, filePath: "Shared.cs")
            .AddDocument(DocumentId.CreateNewId(second.Id), "Second.cs", source,
                filePath: scenario == "IndependentOwner" ? "Independent.cs" : "Shared.cs")
            .AddDocument(DocumentId.CreateNewId(first.Id), "Local.cs", "public sealed class Local { }", filePath: "Local.cs")
            .AddDocument(DocumentId.CreateNewId(second.Id), "Local.cs", "public sealed class Local { }",
                filePath: scenario == "IndependentSignatureType" ? "OtherLocal.cs" : "Local.cs");
        var compilation = await solution.GetProject(first.Id)!.GetCompilationAsync();
        Assert.IsNotNull(compilation);
        var method = compilation.GetTypeByMetadataName("MethodOwner")!.GetMembers("Method").OfType<IMethodSymbol>().Single();
        var expected = method.Parameters[^1].Type;
        var handle = SymbolHandleSerializer.CreateHandle(expected);
        var resolved = await SymbolHandleSerializer.ResolveHandleAsync(solution, handle, CancellationToken.None);
        if (scenario == "Shared")
        {
            Assert.IsInstanceOfType<INamedTypeSymbol>(resolved);
            AssertExactType(expected, (INamedTypeSymbol)resolved);
        }
        else
        {
            Assert.IsNull(resolved);
        }
    }

    [TestMethod]
    public async Task MethodRecipe_RejectsMisplacedKindsAndCombinedPayloads()
    {
        using var workspace = new AdhocWorkspace();
        var (solution, compilation) = await CreateFixtureAsync(workspace);
        var method = compilation.GetTypeByMetadataName("Fixture.MethodOwner")!.GetMembers("SameName").OfType<IMethodSymbol>().Single();
        var handle = SymbolHandleSerializer.CreateHandle(method.Parameters[0].Type);
        var parsed = JsonNode.Parse(Convert.FromBase64String(handle))!.AsObject();
        var root = parsed["MethodConstructedType"]!.AsObject();
        var argument = MethodArgument(root);
        var placeholder = argument["Method"]!["Signature"]![0]!["Type"]!["Chain"]![0]!["Arguments"]![0]!;
        var oldShape = JsonSerializer.Deserialize<LegacyPayload>(Convert.FromBase64String(handle))!;
        Assert.IsNull(await SymbolHandleSerializer.ResolveHandleAsync(solution, Encode(oldShape), CancellationToken.None));

        var ownerInLegacyTree = (JsonObject)parsed.DeepClone();
        ownerInLegacyTree["ConstructedType"] = root.DeepClone();
        ownerInLegacyTree.Remove("MethodConstructedType");
        var both = (JsonObject)parsed.DeepClone();
        both["ConstructedType"] = root.DeepClone();
        var placeholderInOuterTree = (JsonObject)parsed.DeepClone();
        placeholderInOuterTree["MethodConstructedType"]!["Chain"]![0]!["Arguments"]![0] = placeholder.DeepClone();
        var ownerInSignature = (JsonObject)parsed.DeepClone();
        MethodArgument(ownerInSignature["MethodConstructedType"]!.AsObject())["Method"]!["Signature"]![0]!["Type"] = argument.DeepClone();
        foreach (var malformed in new[] { ownerInLegacyTree, both, placeholderInOuterTree, ownerInSignature })
        {
            await Assert.ThrowsExactlyAsync<PublicArgumentException>(() =>
                SymbolHandleSerializer.ResolveHandleAsync(solution, Encode(malformed), CancellationToken.None));
        }
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            SymbolHandleSerializer.ResolveHandleAsync(solution, handle, new CancellationToken(true)));

        static JsonObject MethodArgument(JsonObject recipe) => recipe["Chain"]![0]!["Arguments"]![0]!.AsObject();
    }

    [TestMethod]
    public async Task InvalidRecipeIsArgumentError_WhileStaleRecipeDoesNotFallBack()
    {
        using var workspace = new AdhocWorkspace();
        var (solution, compilation) = await CreateFixtureAsync(workspace);
        var symbol = (IFieldSymbol)compilation.GetTypeByMetadataName("Fixture.Uses`1")!.GetMembers("Source").Single();
        var handle = SymbolHandleSerializer.CreateHandle(symbol.Type);
        var payload = SymbolHandleSerializer.ParseHandlePayload(handle);
        var recipe = payload.ConstructedType;
        Assert.IsNotNull(recipe);

        var malformed = payload with { ConstructedType = recipe with { Chain = [] } };
        var contradictory = payload with
        {
            MemberIdentity = new("Assembly", "Fixture.Plain", "M:Fixture.Plain.Member"),
        };
        foreach (var invalid in new[] { malformed, contradictory })
        {
            await Assert.ThrowsExactlyAsync<PublicArgumentException>(() =>
                SymbolHandleSerializer.ResolveHandleAsync(solution, Encode(invalid), CancellationToken.None));
        }
        var stale = payload with
        {
            MetadataName = "Fixture.Plain",
            ConstructedType = recipe with { Assembly = "MissingAssembly, Version=1.0.0.0" },
        };
        Assert.IsNull(await SymbolHandleSerializer.ResolveHandleAsync(solution, Encode(stale), CancellationToken.None));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            SymbolHandleSerializer.ResolveHandleAsync(solution, handle, new CancellationToken(true)));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task SameAssemblyIdentityInIndependentProjects_IsAmbiguous(bool reverse) =>
        AssertProjectIdentityAsync(reverse, sameDeclarations: false, metadataRoot: false);

    [TestMethod]
    public Task MetadataRootWithDifferentArgumentDefinitions_IsAmbiguous() =>
        AssertProjectIdentityAsync(reverse: false, sameDeclarations: false, metadataRoot: true);

    [TestMethod]
    public Task SameDeclarationInSeparateTargetCompilations_Resolves() =>
        AssertProjectIdentityAsync(reverse: false, sameDeclarations: true, metadataRoot: false);

    private static async Task AssertProjectIdentityAsync(bool reverse, bool sameDeclarations, bool metadataRoot)
    {
        using var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;
        var names = reverse ? new[] { "Second", "First" } : new[] { "First", "Second" };
        foreach (var name in names)
        {
            solution = AddProject(solution, name, assemblyName: "Shared");
            var id = solution.Projects.Last().Id;
            var source = metadataRoot
                ? "namespace Collision; public class Local { } public class Usage { public System.Collections.Generic.List<Local> Value; }"
                : "namespace Collision; public class Generic<T> { } public class Usage { public Generic<int> Value; }";
            var filePath = sameDeclarations ? "Shared.cs" : name + ".cs";
            solution = solution.AddDocument(DocumentId.CreateNewId(id), filePath, source, filePath: filePath);
        }
        var second = solution.Projects.Single(project => project.Name == "Second");
        var compilation = await second.GetCompilationAsync();
        Assert.IsNotNull(compilation);
        var type = ((IFieldSymbol)compilation.GetTypeByMetadataName("Collision.Usage")!.GetMembers("Value").Single()).Type;
        var resolved = await SymbolHandleSerializer.ResolveHandleAsync(solution, SymbolHandleSerializer.CreateHandle(type), CancellationToken.None);
        if (sameDeclarations)
        {
            Assert.IsInstanceOfType<INamedTypeSymbol>(resolved);
            Assert.AreEqual("Shared.cs", resolved.DeclaringSyntaxReferences.Single().SyntaxTree.FilePath);
        }
        else
        {
            Assert.IsNull(resolved);
        }
    }

    [TestMethod]
    public async Task ProjectReference_ReconstructsTheSameSourceAssembly()
    {
        using var workspace = new AdhocWorkspace();
        var solution = AddProject(workspace.CurrentSolution, "Producer", "Producer");
        var producer = solution.ProjectIds.Single();
        solution = solution.AddDocument(DocumentId.CreateNewId(producer), "Producer.cs",
            "namespace Fixture; public class Generic<T> { }", filePath: "Producer.cs");
        solution = AddProject(solution, "Consumer", "Consumer");
        var consumer = solution.Projects.Single(project => project.Name == "Consumer").Id;
        solution = solution.AddProjectReference(consumer, new ProjectReference(producer))
            .AddDocument(DocumentId.CreateNewId(consumer), "Consumer.cs",
                "namespace Fixture; public class Usage { public Generic<int> Value; }", filePath: "Consumer.cs");
        var compilation = await solution.GetProject(consumer)!.GetCompilationAsync();
        Assert.IsNotNull(compilation);
        var type = ((IFieldSymbol)compilation.GetTypeByMetadataName("Fixture.Usage")!.GetMembers("Value").Single()).Type;
        var resolved = await SymbolHandleSerializer.ResolveHandleAsync(solution, SymbolHandleSerializer.CreateHandle(type), CancellationToken.None);
        Assert.IsInstanceOfType<INamedTypeSymbol>(resolved);
        Assert.AreEqual(producer, solution.GetProject(resolved.ContainingAssembly, CancellationToken.None)?.Id);
        Assert.AreEqual(SpecialType.System_Int32, ((INamedTypeSymbol)resolved).TypeArguments[0].SpecialType);
    }

    [TestMethod]
    public async Task MetadataArrayWithExplicitBounds_UsesTheLegacyPath()
    {
        using var workspace = new AdhocWorkspace();
        var solution = AddProject(workspace.CurrentSolution, "Consumer", "Consumer");
        var id = solution.ProjectIds.Single();
        solution = solution.AddMetadataReference(id, CreateBoundedArrayReference());
        var compilation = await solution.GetProject(id)!.GetCompilationAsync();
        Assert.IsNotNull(compilation);
        var member = (IFieldSymbol)compilation.GetTypeByMetadataName("Fixture.BoundedArray")!.GetMembers("Value").Single();
        var array = (IArrayTypeSymbol)((INamedTypeSymbol)member.Type).TypeArguments[0];
        CollectionAssert.AreEqual(new[] { 5 }, array.Sizes.ToArray());
        CollectionAssert.AreEqual(new[] { 1 }, array.LowerBounds.ToArray());
        var handle = SymbolHandleSerializer.CreateHandle(member.Type);
        Assert.IsNull(SymbolHandleSerializer.ParseHandlePayload(handle).ConstructedType);
    }

    private static Solution AddProject(Solution solution, string name, string assemblyName) =>
        solution.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), name, assemblyName,
            LanguageNames.CSharp, compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            metadataReferences: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]));

    private static PortableExecutableReference CreateBoundedArrayReference()
    {
        var metadata = new MetadataBuilder();
        var version = new Version(1, 0, 0, 0);
        metadata.AddModule(0, metadata.GetOrAddString("BoundedArray.dll"), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        metadata.AddAssembly(metadata.GetOrAddString("BoundedArray"), version, default, default, 0, AssemblyHashAlgorithm.None);
        var coreIdentity = typeof(object).Assembly.GetName();
        var core = metadata.AddAssemblyReference(metadata.GetOrAddString(coreIdentity.Name!), coreIdentity.Version!,
            default, metadata.GetOrAddBlob(coreIdentity.GetPublicKeyToken()!), 0, default);
        var systemObject = metadata.AddTypeReference(core, metadata.GetOrAddString("System"), metadata.GetOrAddString("Object"));
        var list = metadata.AddTypeReference(core, metadata.GetOrAddString("System.Collections.Generic"), metadata.GetOrAddString("List`1"));
        var firstField = MetadataTokens.FieldDefinitionHandle(1);
        var firstMethod = MetadataTokens.MethodDefinitionHandle(1);
        metadata.AddTypeDefinition(TypeAttributes.NotPublic, default, metadata.GetOrAddString("<Module>"),
            default, firstField, firstMethod);
        metadata.AddTypeDefinition(TypeAttributes.Public, metadata.GetOrAddString("Fixture"), metadata.GetOrAddString("BoundedArray"),
            systemObject, firstField, firstMethod);
        var signature = new BlobBuilder();
        new BlobEncoder(signature).FieldSignature().GenericInstantiation(list, 1, isValueType: false).AddArgument()
            .Array(element => element.Int32(), shape => shape.Shape(2, [5], [1]));
        metadata.AddFieldDefinition(FieldAttributes.Public | FieldAttributes.Static, metadata.GetOrAddString("Value"),
            metadata.GetOrAddBlob(signature));
        var image = new BlobBuilder();
        new ManagedPEBuilder(new PEHeaderBuilder(imageCharacteristics: Characteristics.Dll),
            new MetadataRootBuilder(metadata), new BlobBuilder()).Serialize(image);
        return MetadataReference.CreateFromImage(image.ToArray());
    }

    private static string Encode<T>(T payload) => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(payload));

    private sealed record LegacyPayload(string? MetadataName, string? DisplayName, string? FilePath,
        int? Line, int? Column,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SymbolHandleSerializer.MemberIdentity? MemberIdentity);

    private static async Task<(Solution Solution, Compilation Compilation)> CreateFixtureAsync(AdhocWorkspace workspace)
    {
        var core = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
        var projectId = ProjectId.CreateNewId();
        var solution = workspace.CurrentSolution.AddProject(ProjectInfo.Create(
            projectId, VersionStamp.Create(), "ConstructedHandleConsumer", "ConstructedHandleConsumer", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            metadataReferences:
            [
                core,
                MetadataReference.CreateFromFile(typeof(System.Runtime.CompilerServices.DynamicAttribute).Assembly.Location),
                CreateAliasedReference(core, "Red"),
                CreateAliasedReference(core, "Blue"),
            ]))
            .AddDocument(DocumentId.CreateNewId(projectId), "Fixture.cs", Source, filePath: "Fixture.cs");
        var compilation = await solution.GetProject(projectId)!.GetCompilationAsync();
        Assert.IsNotNull(compilation);
        var errors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.AreEqual(0, errors.Length, string.Join(Environment.NewLine, errors.Select(error => error.ToString())));
        return (solution, compilation);
    }

    private static PortableExecutableReference CreateAliasedReference(MetadataReference core, string name)
    {
        var compilation = CSharpCompilation.Create(name,
            [CSharpSyntaxTree.ParseText("namespace Shared; public class Argument { public class Nested { } } public interface IAlias { void Go<T>(System.Collections.Generic.List<T> value); } public class MetadataOwner { public void Method<T>(System.Collections.Generic.List<T> value) { } }")],
            [core], new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var image = new MemoryStream();
        var result = compilation.Emit(image);
        Assert.IsTrue(result.Success);
        return MetadataReference.CreateFromImage(image.ToArray(), MetadataReferenceProperties.Assembly.WithAliases([name]));
    }

    private static void AssertExactType(ITypeSymbol expected, ITypeSymbol actual)
    {
        Assert.IsTrue(SymbolEqualityComparer.IncludeNullability.Equals(expected, actual),
            $"Expected {expected} ({expected.NullableAnnotation}); got {actual} ({actual.NullableAnnotation}).");
        Assert.AreEqual(expected.NullableAnnotation, actual.NullableAnnotation);
        if (expected is IArrayTypeSymbol array)
        {
            var actualArray = (IArrayTypeSymbol)actual;
            Assert.AreEqual(array.ElementNullableAnnotation, actualArray.ElementNullableAnnotation);
            Assert.AreEqual(array.Rank, actualArray.Rank);
            Assert.AreEqual(array.IsSZArray, actualArray.IsSZArray);
            AssertExactType(array.ElementType, actualArray.ElementType);
        }
        if (expected is INamedTypeSymbol named)
        {
            var actualNamed = (INamedTypeSymbol)actual;
            Assert.AreEqual(named.ContainingAssembly.Identity, actualNamed.ContainingAssembly.Identity);
            Assert.AreEqual(named.IsNativeIntegerType, actualNamed.IsNativeIntegerType);
            CollectionAssert.AreEqual(named.TypeArgumentNullableAnnotations.ToArray(), actualNamed.TypeArgumentNullableAnnotations.ToArray());
            for (var index = 0; index < named.TypeArguments.Length; index++)
            {
                AssertExactType(named.TypeArguments[index], actualNamed.TypeArguments[index]);
            }
            if (named.ContainingType is not null)
            {
                AssertExactType(named.ContainingType, actualNamed.ContainingType!);
            }
            if (named.IsTupleType)
            {
                Assert.IsTrue(actualNamed.IsTupleType);
                CollectionAssert.AreEqual(named.TupleElements.Select(e => (e.Name, e.IsExplicitlyNamedTupleElement, e.NullableAnnotation)).ToArray(),
                    actualNamed.TupleElements.Select(e => (e.Name, e.IsExplicitlyNamedTupleElement, e.NullableAnnotation)).ToArray());
            }
        }
    }
}
