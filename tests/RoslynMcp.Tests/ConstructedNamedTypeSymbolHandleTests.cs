using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
    public async Task MethodOwnerAndOverBudgetTypes_KeepLegacyBehavior()
    {
        using var workspace = new AdhocWorkspace();
        var (solution, compilation) = await CreateFixtureAsync(workspace);
        var method = compilation.GetTypeByMetadataName("Fixture.Uses`1")!.GetMembers("Method").OfType<IMethodSymbol>().Single();
        var methodArgument = method.Parameters[0].Type;
        var handle = SymbolMapper.ToDto(methodArgument).SymbolHandle;
        Assert.IsNotNull(handle);
        Assert.IsNull(SymbolHandleSerializer.ParseHandlePayload(handle).ConstructedType);
        Assert.IsNull(await SymbolHandleSerializer.ResolveHandleAsync(solution, handle, CancellationToken.None));

        var generic = compilation.GetTypeByMetadataName("Fixture.Base`1")!;
        ITypeSymbol deep = compilation.GetSpecialType(SpecialType.System_Int32);
        for (var level = 0; level < 8; level++)
        {
            deep = generic.Construct(deep);
        }
        var deepHandle = SymbolMapper.ToDto(deep).SymbolHandle;
        Assert.IsNotNull(deepHandle);
        Assert.IsNull(SymbolHandleSerializer.ParseHandlePayload(deepHandle).ConstructedType);
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
            [CSharpSyntaxTree.ParseText("namespace Shared; public class Argument { public class Nested { } }")],
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
