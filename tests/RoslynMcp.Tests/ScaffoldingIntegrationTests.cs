using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Diagnostics;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.TestInfrastructure;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ScaffoldingIntegrationTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        InitializeServices();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        DisposeServices();
    }

    [TestMethod]
    [DataRow("referenceTestFile")]
    [DataRow("sibling fixture")]
    public void ScaffoldingReadFailurePolicy_SingleAndBatchWarningsAreSecretSafe(string affectedInput)
    {
        const string sentinel = "SECRET-SENTINEL-C:/private/scaffolding.cs";
        var sink = new CapturingServerObservabilitySink();
        var reporter = new ServerObservabilityReporter(sink);

        string warning;
        using (RequestCorrelationContext.Begin())
        {
            warning = ScaffoldingReadFailurePolicy.CreateWarning(
                reporter,
                new IOException(sentinel, new UnauthorizedAccessException(sentinel)),
                affectedInput);
        }

        StringAssert.Contains(warning, affectedInput);
        StringAssert.Contains(warning, "scaffolded without pattern inference");
        StringAssert.Contains(warning, "correlationId=");
        Assert.IsFalse(warning.Contains(sentinel, StringComparison.Ordinal));
        Assert.IsFalse(warning.Contains(nameof(IOException), StringComparison.Ordinal));
        Assert.IsFalse(warning.Contains("C:/private", StringComparison.Ordinal));

        Assert.HasCount(1, sink.Events);
        Assert.AreEqual("Scaffolding", sink.Events.Single().Category);
        Assert.IsFalse(JsonSerializer.Serialize(sink.Events.Single())
            .Contains(sentinel, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Scaffold_Type_Preview_And_Apply_Creates_New_Type_File()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var targetFilePath = workspace.GetPath("SampleLib", "Generated", "Bird.cs");

        var preview = await ScaffoldingService.PreviewScaffoldTypeAsync(
            workspace.WorkspaceId,
            new ScaffoldTypeDto("SampleLib", "Bird", "class", "SampleLib.Generated"),
            CancellationToken.None);

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);

        Assert.IsTrue(applyResult.Success, applyResult.Error);
        Assert.IsTrue(File.Exists(targetFilePath));
        var contents = await File.ReadAllTextAsync(targetFilePath, CancellationToken.None);
        StringAssert.Contains(contents, "namespace SampleLib.Generated");
        StringAssert.Contains(contents, "class Bird");
    }

    [TestMethod]
    public async Task Scaffold_Type_Default_Class_IsInternalSealed()
    {
        // BUG fix (scaffold-defaults-improvements / a): default class scaffold should be
        // `internal sealed class` instead of `public class` per modern .NET convention.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var targetFilePath = workspace.GetPath("SampleLib", "Generated", "Bird.cs");

        var preview = await ScaffoldingService.PreviewScaffoldTypeAsync(
            workspace.WorkspaceId,
            new ScaffoldTypeDto("SampleLib", "Bird", "class", "SampleLib.Generated"),
            CancellationToken.None);
        await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);

        var contents = await File.ReadAllTextAsync(targetFilePath, CancellationToken.None);
        StringAssert.Contains(contents, "internal sealed class Bird");
        Assert.IsFalse(contents.Contains("public class Bird"),
            "Default class scaffold should not emit 'public class'.");
    }

    [TestMethod]
    public async Task Scaffold_Type_Interface_StaysPublic()
    {
        // Interfaces cannot be `sealed` and are typically intended for cross-assembly use.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var targetFilePath = workspace.GetPath("SampleLib", "Contracts", "IThing.cs");

        var preview = await ScaffoldingService.PreviewScaffoldTypeAsync(
            workspace.WorkspaceId,
            new ScaffoldTypeDto("SampleLib", "IThing", "interface", "SampleLib.Contracts"),
            CancellationToken.None);
        await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);

        var contents = await File.ReadAllTextAsync(targetFilePath, CancellationToken.None);
        StringAssert.Contains(contents, "public interface IThing");
    }

    [TestMethod]
    public async Task Scaffold_Test_CollectionInterfaceCtorArg_UsesArrayEmpty()
    {
        // BUG fix (scaffold-defaults-improvements / c): test stub constructor args for empty
        // collection interfaces should emit `Array.Empty<T>()` so the generated test runs
        // green. Previously every parameter was emitted as `default(T)`, throwing NRE on the
        // first call when the parameter was a non-null collection interface.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);

        // Inject a target type with a constructor that takes IEnumerable<int>.
        var fixturePath = workspace.GetPath("SampleLib", "BulkProcessor.cs");
        await File.WriteAllTextAsync(fixturePath, """
namespace SampleLib;

using System.Collections.Generic;

public class BulkProcessor
{
    private readonly IEnumerable<int> _items;

    public BulkProcessor(IEnumerable<int> items)
    {
        _items = items;
    }

    public int Sum()
    {
        var total = 0;
        foreach (var item in _items)
        {
            total += item;
        }
        return total;
    }
}
""", CancellationToken.None);

        await workspace.ReloadAsync(CancellationToken.None);

        var preview = await ScaffoldingService.PreviewScaffoldTestAsync(
            workspace.WorkspaceId,
            new ScaffoldTestDto("SampleLib.Tests", "BulkProcessor", "Sum"),
            CancellationToken.None);
        await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);

        var contents = await File.ReadAllTextAsync(
            workspace.GetPath("SampleLib.Tests", "BulkProcessorGeneratedTests.cs"),
            CancellationToken.None);

        StringAssert.Contains(contents, "System.Array.Empty<int>()",
            "Constructor arg of type IEnumerable<int> should be emitted as System.Array.Empty<int>().");
        Assert.IsFalse(contents.Contains("default(IEnumerable<int>)"),
            "Should NOT emit default(IEnumerable<int>) — that throws NRE on first iteration.");
    }

    [TestMethod]
    public async Task Scaffold_Test_DIService_WithInterfaceAndConcreteCtorArgs_EmitsPerArgPlaceholders()
    {
        // BUG fix (scaffold-test-preview-ctor-arg-stubs): when the target's primary ctor
        // requires arguments (the canonical DI-registered-service shape), scaffold_test_preview
        // previously picked the wrong ctor or emitted `new T()`, producing a non-compiling
        // scaffold. The fix probes the widest accessible ctor when no parameterless ctor
        // exists and synthesizes per-param placeholders:
        //   - interface / abstract class → `default(T)!` with a TODO when NSubstitute is not
        //     referenced by the test project; `NSubstitute.Substitute.For<T>()` when it is.
        //   - concrete class with parameterless ctor → `new T()`.
        //   - concrete class without parameterless ctor → TODO placeholder.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);

        var fixturePath = workspace.GetPath("SampleLib", "NamespaceRelocationServiceFixture.cs");
        await File.WriteAllTextAsync(fixturePath, """
namespace SampleLib;

public interface ILogger
{
    void Log(string message);
}

public sealed class Clock
{
    public System.DateTime Now => System.DateTime.UtcNow;
}

public sealed class NamespaceRelocationServiceFixture
{
    private readonly ILogger _logger;
    private readonly Clock _clock;
    private readonly string _name;

    public NamespaceRelocationServiceFixture(ILogger logger, Clock clock, string name)
    {
        _logger = logger;
        _clock = clock;
        _name = name;
    }

    public string Describe() => _name;
}
""", CancellationToken.None);

        await workspace.ReloadAsync(CancellationToken.None);

        var preview = await ScaffoldingService.PreviewScaffoldTestAsync(
            workspace.WorkspaceId,
            new ScaffoldTestDto(
                "SampleLib.Tests",
                "NamespaceRelocationServiceFixture",
                "Describe",
                ReferenceTestFile: string.Empty),
            CancellationToken.None);
        await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "scaffold_test_apply", CancellationToken.None);

        var contents = await File.ReadAllTextAsync(
            workspace.GetPath("SampleLib.Tests", "NamespaceRelocationServiceFixtureGeneratedTests.cs"),
            CancellationToken.None);

        // Should NOT emit the bare `new NamespaceRelocationServiceFixture()` — that was the
        // pre-fix output and does not compile.
        Assert.IsFalse(
            contents.Contains("new NamespaceRelocationServiceFixture()"),
            "Scaffold must not emit a parameterless `new T()` when the target ctor requires args — got:\n" + contents);

        // Interface arg: SampleLib.Tests does NOT reference NSubstitute in this test harness,
        // so the TODO placeholder branch applies.
        StringAssert.Contains(contents, "default(ILogger)!",
            "Interface ctor arg should be emitted as `default(T)!` with a TODO when NSubstitute is not referenced.");
        StringAssert.Contains(contents, "TODO",
            "Interface ctor arg placeholder should carry a TODO comment so callers notice.");
        StringAssert.Contains(contents, "/* logger */",
            "Ctor arg should retain the parameter-name breadcrumb.");

        // Concrete class with parameterless ctor: scaffolded as `new Clock()`.
        StringAssert.Contains(contents, "new Clock()",
            "Concrete ctor arg with an accessible parameterless ctor should be emitted as `new T()`.");
        StringAssert.Contains(contents, "/* clock */",
            "Concrete ctor arg should retain the parameter-name breadcrumb.");

        // String arg: existing behaviour — `string.Empty`.
        StringAssert.Contains(contents, "string.Empty",
            "String ctor arg should be emitted as `string.Empty`.");
    }

    [TestMethod]
    public void BuildArgExpression_InterfaceArg_WithNSubstitute_EmitsSubstituteFor()
    {
        // Covers the NSubstitute-available branch of BuildArgExpression — the SampleLib.Tests
        // integration fixture does NOT reference NSubstitute, so this unit-level assertion is
        // what pins the `NSubstitute.Substitute.For<T>()` emission.
        var source = """
namespace Acme;

public interface IFoo { }
public sealed class Bar { }
""";
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source);
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
            "Test",
            [tree],
            [Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
        var foo = compilation.GetTypeByMetadataName("Acme.IFoo")!;
        var bar = compilation.GetTypeByMetadataName("Acme.Bar")!;

        var withSub = RoslynMcp.Roslyn.Services.TestScaffoldRenderer.BuildArgExpression(foo, nsubstituteAvailable: true);
        var withoutSub = RoslynMcp.Roslyn.Services.TestScaffoldRenderer.BuildArgExpression(foo, nsubstituteAvailable: false);
        var concrete = RoslynMcp.Roslyn.Services.TestScaffoldRenderer.BuildArgExpression(bar, nsubstituteAvailable: false);

        StringAssert.Contains(withSub, "NSubstitute.Substitute.For<IFoo>()",
            "Interface arg with NSubstitute available should emit Substitute.For<T>().");
        StringAssert.Contains(withoutSub, "default(IFoo)!",
            "Interface arg without NSubstitute should emit default(T)! with TODO.");
        StringAssert.Contains(withoutSub, "TODO",
            "Interface arg placeholder should include TODO.");
        StringAssert.Contains(concrete, "new Bar()",
            "Concrete class with parameterless ctor should emit new T().");
    }

    [TestMethod]
    public void BuildArgExpression_CollectionArgs_EmitEmptyArray()
    {
        // Pins the generic-collection branch of BuildArgExpression (extracted into
        // TryBuildCollectionArgExpression): IEnumerable<T>/ICollection<T>/IReadOnlyCollection<T>/
        // IList<T>/IReadOnlyList<T> ctor params must emit `System.Array.Empty<T>()`, not
        // `default(T)` (which throws NRE on first use for a non-null collection interface).
        var source = """
namespace Acme;

using System.Collections.Generic;

public sealed class Consumer
{
    public Consumer(
        IEnumerable<int> a,
        ICollection<string> b,
        IReadOnlyCollection<int> c,
        IList<string> d,
        IReadOnlyList<int> e) { }
}
""";
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source);
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
            "Test",
            [tree],
            [
                Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(System.Collections.Generic.IEnumerable<>).Assembly.Location),
            ]);
        var consumer = compilation.GetTypeByMetadataName("Acme.Consumer")!;
        var p = consumer.InstanceConstructors.Single(c => c.Parameters.Length == 5).Parameters;

        Assert.AreEqual("System.Array.Empty<int>()",
            RoslynMcp.Roslyn.Services.TestScaffoldRenderer.BuildArgExpression(p[0].Type),
            "IEnumerable<int> should emit System.Array.Empty<int>().");
        Assert.AreEqual("System.Array.Empty<string>()",
            RoslynMcp.Roslyn.Services.TestScaffoldRenderer.BuildArgExpression(p[1].Type),
            "ICollection<string> should emit System.Array.Empty<string>().");
        Assert.AreEqual("System.Array.Empty<int>()",
            RoslynMcp.Roslyn.Services.TestScaffoldRenderer.BuildArgExpression(p[2].Type),
            "IReadOnlyCollection<int> should emit System.Array.Empty<int>().");
        Assert.AreEqual("System.Array.Empty<string>()",
            RoslynMcp.Roslyn.Services.TestScaffoldRenderer.BuildArgExpression(p[3].Type),
            "IList<string> should emit System.Array.Empty<string>().");
        Assert.AreEqual("System.Array.Empty<int>()",
            RoslynMcp.Roslyn.Services.TestScaffoldRenderer.BuildArgExpression(p[4].Type),
            "IReadOnlyList<int> should emit System.Array.Empty<int>().");
    }

    [TestMethod]
    public void BuildArgExpression_DictionaryArgs_EmitNewDictionary()
    {
        // Pins the generic-dictionary branch of BuildArgExpression (extracted into
        // TryBuildDictionaryArgExpression): IDictionary<K,V>/IReadOnlyDictionary<K,V> ctor params
        // must emit `new System.Collections.Generic.Dictionary<K, V>()`, not `default(T)`.
        var source = """
namespace Acme;

using System.Collections.Generic;

public sealed class Consumer
{
    public Consumer(
        IDictionary<string, int> a,
        IReadOnlyDictionary<string, int> b) { }
}
""";
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source);
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
            "Test",
            [tree],
            [
                Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(System.Collections.Generic.IDictionary<,>).Assembly.Location),
            ]);
        var consumer = compilation.GetTypeByMetadataName("Acme.Consumer")!;
        var p = consumer.InstanceConstructors.Single(c => c.Parameters.Length == 2).Parameters;

        Assert.AreEqual("new System.Collections.Generic.Dictionary<string, int>()",
            RoslynMcp.Roslyn.Services.TestScaffoldRenderer.BuildArgExpression(p[0].Type),
            "IDictionary<string, int> should emit new Dictionary<string, int>().");
        Assert.AreEqual("new System.Collections.Generic.Dictionary<string, int>()",
            RoslynMcp.Roslyn.Services.TestScaffoldRenderer.BuildArgExpression(p[1].Type),
            "IReadOnlyDictionary<string, int> should emit new Dictionary<string, int>().");
    }

    [TestMethod]
    public async Task Scaffold_Type_NamespaceNotMatchingProject_GetsFolderStructure()
    {
        // BUG fix (scaffold-defaults-improvements / b): when the namespace does not start
        // with the project name, the file should still land in folders matching the
        // namespace path — not at the project root.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var expectedPath = workspace.GetPath("SampleLib", "Acme", "Domain", "Widget.cs");

        var preview = await ScaffoldingService.PreviewScaffoldTypeAsync(
            workspace.WorkspaceId,
            new ScaffoldTypeDto("SampleLib", "Widget", "class", "Acme.Domain"),
            CancellationToken.None);
        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);

        Assert.IsTrue(applyResult.Success, applyResult.Error);
        Assert.IsTrue(File.Exists(expectedPath),
            $"Scaffolded type with namespace 'Acme.Domain' should land in 'Acme/Domain/Widget.cs', " +
            $"not at the project root. Expected: {expectedPath}");
    }

    [TestMethod]
    public async Task Scaffold_Test_Preview_And_Apply_Creates_New_Test_File()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var targetFilePath = workspace.GetPath("SampleLib.Tests", "DogGeneratedTests.cs");

        var preview = await ScaffoldingService.PreviewScaffoldTestAsync(
            workspace.WorkspaceId,
            // Opt out of sibling inference so this existing regression asserts the bare
            // scaffold output; the sibling-inference behaviour is covered by its own test.
            new ScaffoldTestDto("SampleLib.Tests", "Dog", "Speak", ReferenceTestFile: string.Empty),
            CancellationToken.None);

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);

        Assert.IsTrue(applyResult.Success, applyResult.Error);
        Assert.IsTrue(File.Exists(targetFilePath));
        var contents = await File.ReadAllTextAsync(targetFilePath, CancellationToken.None);
        StringAssert.Contains(contents, "[TestMethod]");
        StringAssert.Contains(contents, "Speak_Needs_Test");
    }

    [TestMethod]
    public async Task Scaffold_Test_Preview_UseSamplingFalse_DoesNotCallSuggestionProvider()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var targetFilePath = workspace.GetPath("SampleLib.Tests", "DogGeneratedTests.cs");
        var provider = new RecordingTestNameSuggestionProvider("Speak_WhenDogIsReady_ReturnsBark");

        var preview = await ScaffoldingService.PreviewScaffoldTestAsync(
            workspace.WorkspaceId,
            new ScaffoldTestDto(
                "SampleLib.Tests",
                "Dog",
                "Speak",
                ReferenceTestFile: string.Empty,
                UseSampling: false),
            CancellationToken.None,
            provider);

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);

        Assert.IsTrue(applyResult.Success, applyResult.Error);
        Assert.AreEqual(0, provider.CallCount, "Sampling provider must not be called unless useSampling is true.");
        var contents = await File.ReadAllTextAsync(targetFilePath, CancellationToken.None);
        StringAssert.Contains(contents, "Speak_Needs_Test");
        Assert.IsFalse(contents.Contains("Speak_WhenDogIsReady_ReturnsBark"),
            "Default deterministic scaffold should not use sampled names.");
    }

    [TestMethod]
    public async Task Scaffold_Test_Preview_UseSamplingTrue_UsesSuggestedMethodName()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var siblingPath = workspace.GetPath("SampleLib.Tests", "DogExistingTests.cs");
        await File.WriteAllTextAsync(siblingPath, """
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SampleLib.Tests;

[TestClass]
public sealed class DogExistingTests
{
    [TestMethod]
    public void Speak_WhenQuiet_ReturnsEmpty()
    {
    }
}
""", CancellationToken.None);
        var targetFilePath = workspace.GetPath("SampleLib.Tests", "DogGeneratedTests.cs");
        var provider = new RecordingTestNameSuggestionProvider("Speak_WhenDogIsReady_ReturnsBark");

        var preview = await ScaffoldingService.PreviewScaffoldTestAsync(
            workspace.WorkspaceId,
            new ScaffoldTestDto(
                "SampleLib.Tests",
                "Dog",
                "Speak",
                ReferenceTestFile: string.Empty,
                UseSampling: true),
            CancellationToken.None,
            provider);

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);

        Assert.IsTrue(applyResult.Success, applyResult.Error);
        Assert.AreEqual(1, provider.CallCount, "Sampling provider should be called once for an opted-in method-focused scaffold.");
        Assert.IsNotNull(provider.LastContext);
        Assert.AreEqual("Dog", provider.LastContext.TargetTypeName);
        Assert.AreEqual("Speak", provider.LastContext.TargetMethodName);
        StringAssert.Contains(provider.LastContext.TargetMethodSignature!, "Speak(");
        CollectionAssert.Contains(provider.LastContext.SiblingTestMethodNames.ToList(), "Speak_WhenQuiet_ReturnsEmpty");

        var contents = await File.ReadAllTextAsync(targetFilePath, CancellationToken.None);
        StringAssert.Contains(contents, "Speak_WhenDogIsReady_ReturnsBark");
        Assert.IsFalse(contents.Contains("Speak_Needs_Test"),
            "Opted-in sampled scaffold should replace the deterministic placeholder with the suggested method name.");
    }

    [TestMethod]
    public async Task Scaffold_Test_Preview_InvalidSampledName_UsesSecretSafeBoundedWarning()
    {
        const string sentinel = "sampled-name-secret-sentinel";
        const string privatePath = "C:/private/tenant/solution.slnx";
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var invalidSuggestion = $"{sentinel}:{privatePath}/{new string('x', 8_192)}";

        var preview = await ScaffoldingService.PreviewScaffoldTestAsync(
            workspace.WorkspaceId,
            new ScaffoldTestDto(
                "SampleLib.Tests",
                "Dog",
                "Speak",
                ReferenceTestFile: string.Empty,
                UseSampling: true),
            CancellationToken.None,
            new RecordingTestNameSuggestionProvider(invalidSuggestion));

        Assert.IsNotNull(preview.Warnings);
        var warnings = string.Join(Environment.NewLine, preview.Warnings);
        StringAssert.Contains(warnings, "sampled test method name was invalid or exceeded the supported length");
        Assert.IsFalse(warnings.Contains(sentinel, StringComparison.Ordinal));
        Assert.IsFalse(warnings.Contains(privatePath, StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(warnings.Length < 512,
            "An invalid sampled value must not amplify the client-visible warning payload.");
    }

    [TestMethod]
    public async Task Scaffold_Test_Preview_HugeValidSampledIdentifier_UsesBoundedFallback()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var oversizedValidIdentifier = new string('A', 1_000_000);

        var preview = await ScaffoldingService.PreviewScaffoldTestAsync(
            workspace.WorkspaceId,
            new ScaffoldTestDto(
                "SampleLib.Tests",
                "Dog",
                "Speak",
                ReferenceTestFile: string.Empty,
                UseSampling: true),
            CancellationToken.None,
            new RecordingTestNameSuggestionProvider(oversizedValidIdentifier));

        Assert.IsNotNull(preview.Warnings);
        var warnings = string.Join(Environment.NewLine, preview.Warnings);
        StringAssert.Contains(warnings, "sampled test method name was invalid or exceeded the supported length");
        Assert.IsTrue(warnings.Length < 512,
            "A syntactically valid but oversized identifier must not amplify preview output.");
        var previewJson = JsonSerializer.Serialize(preview);
        Assert.IsFalse(previewJson.Contains(oversizedValidIdentifier, StringComparison.Ordinal),
            "The rejected sampled identifier must not be embedded in the preview contract.");
    }

    [TestMethod]
    public async Task Scaffold_Test_Preview_SamplingProviderCancellation_Propagates()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var cancellation = new OperationCanceledException("sampling request cancelled");

        var thrown = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            ScaffoldingService.PreviewScaffoldTestAsync(
                workspace.WorkspaceId,
                new ScaffoldTestDto(
                    "SampleLib.Tests",
                    "Dog",
                    "Speak",
                    ReferenceTestFile: string.Empty,
                    UseSampling: true),
                CancellationToken.None,
                new ThrowingTestNameSuggestionProvider(cancellation)));

        Assert.AreSame(cancellation, thrown,
            "Response handling must not convert provider cancellation into a deterministic-name fallback.");
    }

    [TestMethod]
    public async Task Scaffold_Test_Private_Target_Method_Adds_Warning_And_Reflection_Call()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var targetTypePath = workspace.GetPath("SampleLib", "HiddenBehavior.cs");
        await File.WriteAllTextAsync(targetTypePath, """
namespace SampleLib;

public class HiddenBehavior
{
    private string BuildSecret()
    {
        return "secret";
    }
}
""", CancellationToken.None);

        await workspace.ReloadAsync(CancellationToken.None);

        var preview = await ScaffoldingService.PreviewScaffoldTestAsync(
            workspace.WorkspaceId,
            new ScaffoldTestDto("SampleLib.Tests", "HiddenBehavior", "BuildSecret", ReferenceTestFile: string.Empty),
            CancellationToken.None);

        Assert.IsNotNull(preview.Warnings);
        Assert.AreEqual(1, preview.Warnings.Count);
        StringAssert.Contains(
            preview.Warnings[0],
            "Target method 'BuildSecret' is private — the scaffold uses reflection to invoke it;");

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applyResult.Success, applyResult.Error);

        var contents = await File.ReadAllTextAsync(
            workspace.GetPath("SampleLib.Tests", "HiddenBehaviorGeneratedTests.cs"),
            CancellationToken.None);

        StringAssert.Contains(contents, "typeof(HiddenBehavior).GetMethod(");
        StringAssert.Contains(contents, "\"BuildSecret\"");
    }

    [TestMethod]
    public async Task Scaffold_Test_Internal_Target_Without_InternalsVisibleTo_Emits_Warning_And_Placeholder()
    {
        // scaffold-test-internal-target-accessibility regression: internal target types in an
        // assembly that does NOT grant InternalsVisibleTo to the test project would previously
        // generate a `new TargetType()` call that compile-fails with CS0122. The fix detects
        // the cross-assembly internal-not-visible case during target resolution and emits a
        // warning + Inconclusive placeholder scaffold instead.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var targetTypePath = workspace.GetPath("SampleLib", "InternalGate.cs");
        await File.WriteAllTextAsync(targetTypePath, """
namespace SampleLib;

internal sealed class InternalGate
{
    public void Open() { }
}
""", CancellationToken.None);

        await workspace.ReloadAsync(CancellationToken.None);

        var preview = await ScaffoldingService.PreviewScaffoldTestAsync(
            workspace.WorkspaceId,
            new ScaffoldTestDto("SampleLib.Tests", "InternalGate", "Open", ReferenceTestFile: string.Empty),
            CancellationToken.None);

        Assert.IsNotNull(preview.Warnings, "Inaccessible internal target should produce a warning.");
        Assert.AreEqual(1, preview.Warnings.Count);
        StringAssert.Contains(preview.Warnings[0], "Target type 'SampleLib.InternalGate' is not accessible from 'SampleLib.Tests'");
        StringAssert.Contains(preview.Warnings[0], "InternalsVisibleTo");

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applyResult.Success, applyResult.Error);

        var contents = await File.ReadAllTextAsync(
            workspace.GetPath("SampleLib.Tests", "InternalGateGeneratedTests.cs"),
            CancellationToken.None);

        Assert.IsFalse(
            contents.Contains("new InternalGate("),
            "Inaccessible internal target should NOT emit a direct `new TargetType(...)` call — that would fail CS0122.");
        Assert.IsFalse(
            contents.Contains("subject.Open"),
            "Inaccessible internal target should NOT emit a direct `subject.Method()` call.");
        StringAssert.Contains(contents, "Assert.Inconclusive(");
        StringAssert.Contains(contents, "is not visible to this test assembly");
    }

    [TestMethod]
    public async Task Scaffold_Test_Internal_Target_With_InternalsVisibleTo_Generates_Direct_Call()
    {
        // scaffold-test-internal-target-accessibility companion case: when InternalsVisibleTo
        // grants the test assembly access to the target's containing assembly, scaffolding
        // should produce the ordinary direct-call shape (no warning, no placeholder).
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);

        var ivtPath = workspace.GetPath("SampleLib", "AssemblyInfo.IVT.cs");
        await File.WriteAllTextAsync(ivtPath, """
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("SampleLib.Tests")]
""", CancellationToken.None);

        var targetTypePath = workspace.GetPath("SampleLib", "InternalGateVisible.cs");
        await File.WriteAllTextAsync(targetTypePath, """
namespace SampleLib;

internal sealed class InternalGateVisible
{
    public void Open() { }
}
""", CancellationToken.None);

        await workspace.ReloadAsync(CancellationToken.None);

        var preview = await ScaffoldingService.PreviewScaffoldTestAsync(
            workspace.WorkspaceId,
            new ScaffoldTestDto("SampleLib.Tests", "InternalGateVisible", "Open", ReferenceTestFile: string.Empty),
            CancellationToken.None);

        Assert.IsNull(
            preview.Warnings,
            "When InternalsVisibleTo grants access, no inaccessibility warning should be emitted.");

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applyResult.Success, applyResult.Error);

        var contents = await File.ReadAllTextAsync(
            workspace.GetPath("SampleLib.Tests", "InternalGateVisibleGeneratedTests.cs"),
            CancellationToken.None);

        StringAssert.Contains(contents, "new InternalGateVisible(");
        StringAssert.Contains(contents, "subject.Open");
        Assert.IsFalse(
            contents.Contains("Assert.Inconclusive(") && contents.Contains("is not visible to this test assembly"),
            "When access is granted, scaffold should NOT contain the inaccessible-target placeholder body.");
    }

    [TestMethod]
    public async Task Scaffold_Test_Internal_Method_On_Public_Type_Emits_Warning_And_Placeholder()
    {
        // scaffold-test-internal-target-accessibility (method-level): the containing type is
        // public, but the target method is internal and the test assembly lacks
        // InternalsVisibleTo. Direct call would fail CS0122. Private methods stay on the
        // existing reflection-scaffold path; only the internal-not-visible case redirects.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var targetTypePath = workspace.GetPath("SampleLib", "PublicGate.cs");
        await File.WriteAllTextAsync(targetTypePath, """
namespace SampleLib;

public class PublicGate
{
    internal void OpenInternal() { }
}
""", CancellationToken.None);

        await workspace.ReloadAsync(CancellationToken.None);

        var preview = await ScaffoldingService.PreviewScaffoldTestAsync(
            workspace.WorkspaceId,
            new ScaffoldTestDto("SampleLib.Tests", "PublicGate", "OpenInternal", ReferenceTestFile: string.Empty),
            CancellationToken.None);

        Assert.IsNotNull(preview.Warnings, "Internal method with no InternalsVisibleTo should produce a warning.");
        Assert.AreEqual(1, preview.Warnings.Count);
        StringAssert.Contains(preview.Warnings[0], "Target method 'SampleLib.PublicGate.OpenInternal' is not accessible");

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applyResult.Success, applyResult.Error);

        var contents = await File.ReadAllTextAsync(
            workspace.GetPath("SampleLib.Tests", "PublicGateGeneratedTests.cs"),
            CancellationToken.None);

        Assert.IsFalse(
            contents.Contains("subject.OpenInternal"),
            "Inaccessible internal method should NOT emit a direct `subject.Method()` call.");
        StringAssert.Contains(contents, "Assert.Inconclusive(");
    }

    [TestMethod]
    public async Task Scaffold_Test_With_ReferenceTestFile_Replicates_FixturePattern_AndTrimsAttributesAndUsings()
    {
        // Regression for scaffold-test-sibling-pattern-inference + post-fix narrowing per
        // scaffold-test-preview-sibling-inference-overbroad: when a referenceTestFile exposes
        // a constructor-injected fixture shape, the scaffold should mirror the structural
        // pieces (base list + ctor params) but NOT carry convention-tagging attributes
        // ([TestCategory], [Trait], [Category], [Collection]) and SHOULD trim usings to those
        // semantically required by the captured surface.
        //
        // The sibling here uses real types from SampleLib (Shape from SampleLib.Hierarchy as
        // the constructor-injected dependency) so the test project's compilation can verify
        // the using-trim retains required namespaces via semantic resolution.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);

        // Seed a sibling test file that exercises the full pattern we want replicated, plus
        // a [TestCategory] tag that should be filtered (per the overbroad-inference fix).
        var siblingPath = workspace.GetPath("SampleLib.Tests", "ShapeRendererTests.cs");
        await File.WriteAllTextAsync(siblingPath, """
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SampleLib.Hierarchy;

namespace SampleLib.Tests;

[TestCategory("Integration")]
[TestClass]
public class ShapeRendererTests
{
    private readonly Shape _shape;

    public ShapeRendererTests(Shape shape)
    {
        _shape = shape;
    }

    [TestMethod]
    public void Render_Returns_Ok() { }
}
""", CancellationToken.None);

        await workspace.ReloadAsync(CancellationToken.None);

        var preview = await ScaffoldingService.PreviewScaffoldTestAsync(
            workspace.WorkspaceId,
            new ScaffoldTestDto(
                "SampleLib.Tests",
                "AnimalService",
                ReferenceTestFile: siblingPath),
            CancellationToken.None);
        var applyResult = await RefactoringService.ApplyRefactoringAsync(
            preview.PreviewToken, "test_apply", CancellationToken.None);

        Assert.IsTrue(applyResult.Success, applyResult.Error);
        var scaffoldPath = workspace.GetPath("SampleLib.Tests", "AnimalServiceGeneratedTests.cs");
        Assert.IsTrue(File.Exists(scaffoldPath), $"Expected scaffolded file at {scaffoldPath}.");

        var contents = await File.ReadAllTextAsync(scaffoldPath, CancellationToken.None);

        // Per scaffold-test-preview-sibling-inference-overbroad: [TestCategory] is a
        // convention-tagging attribute that should NOT carry across — sibling-inference is
        // narrowed to file-scoped namespace + base-class-name shape only.
        Assert.IsFalse(contents.Contains("[TestCategory(\"Integration\")]"),
            "Sibling [TestCategory] must NOT be replicated on the scaffold — convention-tagging " +
            "attributes leak the sibling's bucket classification onto an unrelated target.");

        // [TestClass] is emitted unconditionally by the MSTest framework builder — the
        // sibling's own [TestClass] is filtered so we don't double-emit it.
        var testClassOccurrences = System.Text.RegularExpressions.Regex
            .Matches(contents, @"\[TestClass\]").Count;
        Assert.AreEqual(1, testClassOccurrences,
            "[TestClass] must appear exactly once — the sibling's attribute is filtered so the framework builder emits the canonical one.");

        // Constructor-injected fixture: the sibling's `Shape shape` parameter lands as a
        // constructor parameter, is stored in a `_shape` field, and is assigned in the body.
        // This is the load-bearing replication shape — DI fixture support is exactly why we
        // capture the constructor parameters at all.
        StringAssert.Contains(contents, "private readonly Shape _shape;",
            "Constructor-injected fixture should become a readonly field.");
        StringAssert.Contains(contents,
            "public AnimalServiceGeneratedTests(Shape shape)",
            "Constructor signature should mirror the sibling's constructor parameters.");
        StringAssert.Contains(contents, "_shape = shape;",
            "Constructor body should assign the injected fixture to the generated field.");

        // Required `using` from the sibling carries over because semantic resolution proves
        // it's needed by the captured ctor-param type (Shape). Per the overbroad-inference
        // fix, only usings the trim verifies are required carry across — the rest are dropped.
        StringAssert.Contains(contents, "using SampleLib.Hierarchy;",
            "Sibling-carried using directive should be replicated when the trim verifies it's " +
            "required by the captured ctor-param surface.");
    }

    [TestMethod]
    public async Task Scaffold_Test_With_PlaywrightSibling_DoesNotLeak_AttributesOrUsings()
    {
        // Regression for scaffold-test-preview-sibling-inference-overbroad. When the MRU sibling
        // is a Playwright fixture (`[TestCategory("Playwright")]` + `[Trait("Category","Playwright")]`
        // + 10+ Playwright usings), the auto-detected sibling-inference path should NOT leak any
        // of those bucket-classification attributes or unused usings into a non-Playwright scaffold.
        // Sibling-inference is narrowed to file-scoped namespace + base-class-name shape only;
        // attribute carry is filtered to drop convention tags; usings are trimmed via semantic
        // resolution to those required by the captured surface.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);

        // Seed a Playwright-style sibling fixture in the test project. This becomes the MRU
        // sibling auto-detected by scaffold_test_preview when no explicit reference is supplied.
        var playwrightSiblingPath = workspace.GetPath("SampleLib.Tests", "PlaywrightFlowTests.cs");
        await File.WriteAllTextAsync(playwrightSiblingPath, """
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Playwright;
using Microsoft.Playwright.MSTest;
using Microsoft.Playwright.NUnit;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Net.Http;
using System.Diagnostics;
using System.IO;

namespace SampleLib.Tests;

[TestCategory("Playwright")]
[TestCategory("UI")]
[TestClass]
public class PlaywrightFlowTests
{
    [TestMethod]
    public async Task Login_Flow_Works() { await Task.CompletedTask; }
}
""", CancellationToken.None);

        await workspace.ReloadAsync(CancellationToken.None);

        // No explicit reference — the MRU auto-detection should pick the Playwright sibling.
        // The bug under fix would copy [TestCategory("Playwright")], [TestCategory("UI")], and
        // every using above into the AnimalService scaffold.
        var preview = await ScaffoldingService.PreviewScaffoldTestAsync(
            workspace.WorkspaceId,
            // ReferenceTestFile is null (default) — exercise the MRU auto-detection path.
            new ScaffoldTestDto("SampleLib.Tests", "AnimalService"),
            CancellationToken.None);
        var applyResult = await RefactoringService.ApplyRefactoringAsync(
            preview.PreviewToken, "test_apply", CancellationToken.None);

        Assert.IsTrue(applyResult.Success, applyResult.Error);
        var scaffoldPath = workspace.GetPath("SampleLib.Tests", "AnimalServiceGeneratedTests.cs");
        Assert.IsTrue(File.Exists(scaffoldPath), $"Expected scaffolded file at {scaffoldPath}.");
        var contents = await File.ReadAllTextAsync(scaffoldPath, CancellationToken.None);

        // No [TestCategory] tags should leak from the Playwright sibling — they're explicitly
        // blocklisted as convention-tagging attributes.
        Assert.IsFalse(contents.Contains("[TestCategory(\"Playwright\")]"),
            "Playwright sibling's [TestCategory(\"Playwright\")] must not leak into a non-Playwright scaffold.");
        Assert.IsFalse(contents.Contains("[TestCategory(\"UI\")]"),
            "Playwright sibling's [TestCategory(\"UI\")] must not leak into a non-Playwright scaffold.");

        // No Playwright usings should carry — semantic resolution proves none of them are
        // required by the captured surface (which is empty here: no base list, no ctor params).
        Assert.IsFalse(contents.Contains("using Microsoft.Playwright"),
            "No Microsoft.Playwright.* using should leak from the sibling — these are unused imports.");

        // Other unused System.* imports from the sibling should also be dropped (the captured
        // surface is empty, so the trim retains nothing). The framework using and the explicit
        // target-type using are added by the scaffolder itself, not from sibling carry.
        Assert.IsFalse(contents.Contains("using System.Diagnostics;"),
            "Sibling's unused System.Diagnostics import must not leak — trim removes unreferenced usings.");
        Assert.IsFalse(contents.Contains("using System.Net.Http;"),
            "Sibling's unused System.Net.Http import must not leak — trim removes unreferenced usings.");

        // The scaffold must still emit the framework using (always injected) and the target-type
        // using (added explicitly from the resolved target type's namespace).
        StringAssert.Contains(contents, "using Microsoft.VisualStudio.TestTools.UnitTesting;",
            "Framework using must be emitted by the per-framework builder regardless of sibling shape.");
        StringAssert.Contains(contents, "using SampleLib;",
            "Target-type's containing-namespace using must be emitted by the scaffolder.");

        // Sanity: the scaffold should still produce a valid class for AnimalService.
        StringAssert.Contains(contents, "public class AnimalServiceGeneratedTests",
            "Scaffold class declaration should be emitted normally despite sibling carry being trimmed.");
    }

    [TestMethod]
    public async Task Scaffold_Test_Batch_Preview_And_Apply_Creates_Multiple_Test_Files()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var dogPath = workspace.GetPath("SampleLib.Tests", "DogGeneratedTests.cs");
        var animalServicePath = workspace.GetPath("SampleLib.Tests", "AnimalServiceGeneratedTests.cs");

        var preview = await ScaffoldingService.PreviewScaffoldTestBatchAsync(
            workspace.WorkspaceId,
            new ScaffoldTestBatchDto(
                "SampleLib.Tests",
                [
                    new ScaffoldTestBatchTargetDto("Dog", "Speak"),
                    new ScaffoldTestBatchTargetDto("AnimalService", "GetAllAnimals")
                ],
                "auto"),
            CancellationToken.None);

        Assert.AreEqual(2, preview.Changes.Count,
            "Batch scaffold preview should aggregate one file-creation diff per target.");

        var applyResult = await RefactoringService.ApplyRefactoringAsync(
            preview.PreviewToken, "test_apply", CancellationToken.None);

        Assert.IsTrue(applyResult.Success, applyResult.Error);
        Assert.IsTrue(File.Exists(dogPath), $"Expected scaffolded file at {dogPath}.");
        Assert.IsTrue(File.Exists(animalServicePath), $"Expected scaffolded file at {animalServicePath}.");

        var dogContents = await File.ReadAllTextAsync(dogPath, CancellationToken.None);
        var animalServiceContents = await File.ReadAllTextAsync(animalServicePath, CancellationToken.None);

        StringAssert.Contains(dogContents, "Speak_Needs_Test");
        StringAssert.Contains(animalServiceContents, "GetAllAnimals_Needs_Test");
    }

    [TestMethod]
    public async Task Scaffold_Test_Batch_NullableCtorArgs_EmitCompileSafeExpressions()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var fixturePath = workspace.GetPath("SampleLib", "Dog.cs");
        await File.AppendAllTextAsync(fixturePath, """

public interface INullableSink
{
    void Record(string value);
}

public sealed class NullableOption
{
    public string Value { get; } = string.Empty;
}

public sealed class RequiredOption
{
    public RequiredOption(string value)
    {
        Value = value;
    }

    public string Value { get; }
}

public sealed class NullableConstructorTarget
{
    private readonly NullableOption? _option;
    private readonly RequiredOption? _required;
    private readonly INullableSink? _sink;

    public NullableConstructorTarget(
        NullableOption? option,
        RequiredOption? required,
        INullableSink? sink)
    {
        _option = option;
        _required = required;
        _sink = sink;
    }

    public string Describe()
    {
        _sink?.Record(_option?.Value ?? _required?.Value ?? string.Empty);
        return _option?.Value ?? _required?.Value ?? string.Empty;
    }
}
""", CancellationToken.None);

        await workspace.ReloadAsync(CancellationToken.None);

        var preview = await ScaffoldingService.PreviewScaffoldTestBatchAsync(
            workspace.WorkspaceId,
            new ScaffoldTestBatchDto(
                "SampleLib.Tests",
                [new ScaffoldTestBatchTargetDto("NullableConstructorTarget", "Describe")],
                "auto"),
            CancellationToken.None);

        var applyResult = await RefactoringService.ApplyRefactoringAsync(
            preview.PreviewToken,
            "scaffold_test_batch_apply",
            CancellationToken.None);

        Assert.IsTrue(applyResult.Success, applyResult.Error);
        await workspace.ReloadAsync(CancellationToken.None);

        var generatedPath = workspace.GetPath("SampleLib.Tests", "NullableConstructorTargetGeneratedTests.cs");
        var contents = await File.ReadAllTextAsync(generatedPath, CancellationToken.None);

        StringAssert.Contains(contents, "new NullableOption()",
            "Nullable concrete ctor arg with an accessible parameterless ctor should unwrap the annotation before object creation.");
        Assert.IsFalse(contents.Contains("new NullableOption?()"),
            "Scaffold must not emit invalid nullable object creation syntax.");
        StringAssert.Contains(contents, "default(RequiredOption?)!",
            "Nullable concrete ctor arg without a parameterless ctor should stay compile-safe and keep the nullable annotation.");
        StringAssert.Contains(contents, "default(INullableSink?)!",
            "Nullable interface ctor arg should use the existing compile-safe TODO placeholder shape.");
    }

    [TestMethod]
    public async Task Scaffold_Test_DottedTargetTypeName_TopLevel_Yields_SingleIdentifier_ClassName()
    {
        // Regression for scaffold-test-preview-dotted-identifier: callers who hit the
        // ambiguity-resolution error are told to "use the fully qualified type name" and
        // then re-invoke with `Namespace.Type`. Previously that dotted input was stamped
        // verbatim into the class-name template, emitting
        // `public class SampleLib.Hierarchy.CircleGeneratedTests` — a CS syntax error. The
        // fix uses `TypeSymbol.Name` (unqualified) so only the simple identifier lands in
        // the class and file names, while the `using SampleLib.Hierarchy;` brings the type
        // into scope for the constructor call.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var expectedFilePath = workspace.GetPath("SampleLib.Tests", "CircleGeneratedTests.cs");

        var preview = await ScaffoldingService.PreviewScaffoldTestAsync(
            workspace.WorkspaceId,
            new ScaffoldTestDto(
                "SampleLib.Tests",
                "SampleLib.Hierarchy.Circle",
                ReferenceTestFile: string.Empty),
            CancellationToken.None);

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);

        Assert.IsTrue(applyResult.Success, applyResult.Error);
        Assert.IsTrue(File.Exists(expectedFilePath),
            $"Expected scaffolded file at '{expectedFilePath}' — dotted FQN input must be stripped to the simple name for the filename.");

        var contents = await File.ReadAllTextAsync(expectedFilePath, CancellationToken.None);

        // Class identifier must be a single identifier — a dotted identifier is a CS syntax error.
        StringAssert.Contains(contents, "public class CircleGeneratedTests");
        Assert.IsFalse(contents.Contains("SampleLib.Hierarchy.CircleGeneratedTests"),
            "Scaffold must NOT emit a dotted class identifier — this is a CS syntax error.");

        // The `using` for the target namespace brings Circle into scope for `new Circle(...)`.
        StringAssert.Contains(contents, "using SampleLib.Hierarchy;");
        StringAssert.Contains(contents, "new Circle(");
    }

    [TestMethod]
    public async Task Scaffold_Test_DottedTargetTypeName_NestedType_Yields_SingleIdentifier_ClassName()
    {
        // Companion to the top-level regression above: a nested type's "FQN" looks like
        // `Namespace.Outer.Inner`, and the scaffold must still emit a single-identifier
        // class name. We inject a fixture with a nested type so the test is self-contained
        // and does not depend on sample-solution evolution.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var fixturePath = workspace.GetPath("SampleLib", "NestedFixture.cs");
        await File.WriteAllTextAsync(fixturePath, """
namespace SampleLib;

public class NestedHost
{
    public class NestedInner
    {
        public string Speak() => "inner";
    }
}
""", CancellationToken.None);

        await workspace.ReloadAsync(CancellationToken.None);

        var expectedFilePath = workspace.GetPath("SampleLib.Tests", "NestedInnerGeneratedTests.cs");

        var preview = await ScaffoldingService.PreviewScaffoldTestAsync(
            workspace.WorkspaceId,
            new ScaffoldTestDto(
                "SampleLib.Tests",
                "SampleLib.NestedHost.NestedInner",
                "Speak",
                ReferenceTestFile: string.Empty),
            CancellationToken.None);

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);

        Assert.IsTrue(applyResult.Success, applyResult.Error);
        Assert.IsTrue(File.Exists(expectedFilePath),
            $"Expected scaffolded file at '{expectedFilePath}' — nested-type FQN must be stripped to the simple name.");

        var contents = await File.ReadAllTextAsync(expectedFilePath, CancellationToken.None);

        StringAssert.Contains(contents, "public class NestedInnerGeneratedTests");
        Assert.IsFalse(contents.Contains("NestedHost.NestedInnerGeneratedTests"),
            "Scaffold must NOT emit a dotted class identifier for nested types either.");
        StringAssert.Contains(contents, "Speak_Needs_Test");
    }

    [TestMethod]
    public async Task Scaffold_Test_Preview_StaticClass_Omits_NewT_InArrange()
    {
        // scaffold-test-preview-static-target-body: a `static class` target should scaffold
        // a utility-shaped body — no `new StaticUtil()` and no `var subject = ...` Arrange line.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var fixturePath = workspace.GetPath("SampleLib", "StaticUtil.cs");
        await File.WriteAllTextAsync(fixturePath, """
namespace SampleLib;

public static class StaticUtil
{
    public static int Add(int a, int b) => a + b;
}
""", CancellationToken.None);

        await workspace.ReloadAsync(CancellationToken.None);

        var expectedFilePath = workspace.GetPath("SampleLib.Tests", "StaticUtilGeneratedTests.cs");

        var preview = await ScaffoldingService.PreviewScaffoldTestAsync(
            workspace.WorkspaceId,
            new ScaffoldTestDto(
                "SampleLib.Tests",
                "StaticUtil",
                "Add",
                ReferenceTestFile: string.Empty),
            CancellationToken.None);

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);

        Assert.IsTrue(applyResult.Success, applyResult.Error);
        var contents = await File.ReadAllTextAsync(expectedFilePath, CancellationToken.None);

        Assert.IsFalse(contents.Contains("new StaticUtil("),
            "Static-class scaffold must NOT emit `new StaticUtil(...)` in Arrange.");
        Assert.IsFalse(contents.Contains("var subject ="),
            "Static-class scaffold must NOT emit a `subject` instance.");
    }

    [TestMethod]
    public async Task Scaffold_Test_Preview_ConstantsOnlyClass_Omits_NewT_InArrange()
    {
        // scaffold-test-preview-static-target-body: a non-static class whose only public surface
        // is `const` / static fields (classic "constants" holder e.g. `TenantConstants`) should
        // scaffold without `new TenantConstants()` — previously the scaffolder only inspected
        // methods, so constants-only types fell through to the instance template.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var fixturePath = workspace.GetPath("SampleLib", "TenantConstants.cs");
        await File.WriteAllTextAsync(fixturePath, """
namespace SampleLib;

public class TenantConstants
{
    public const string DefaultTenant = "default";
    public const int MaxConnections = 42;
}
""", CancellationToken.None);

        await workspace.ReloadAsync(CancellationToken.None);

        var expectedFilePath = workspace.GetPath("SampleLib.Tests", "TenantConstantsGeneratedTests.cs");

        var preview = await ScaffoldingService.PreviewScaffoldTestAsync(
            workspace.WorkspaceId,
            new ScaffoldTestDto(
                "SampleLib.Tests",
                "TenantConstants",
                TargetMethodName: null,
                ReferenceTestFile: string.Empty),
            CancellationToken.None);

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);

        Assert.IsTrue(applyResult.Success, applyResult.Error);
        var contents = await File.ReadAllTextAsync(expectedFilePath, CancellationToken.None);

        Assert.IsFalse(contents.Contains("new TenantConstants("),
            "Constants-only class scaffold must NOT emit `new TenantConstants(...)` in Arrange.");
        Assert.IsFalse(contents.Contains("var subject ="),
            "Constants-only class scaffold must NOT emit a `subject` instance.");
    }

    [TestMethod]
    public async Task Scaffold_Test_Preview_StaticMembersOnlyClass_Omits_NewT_InArrange()
    {
        // scaffold-test-preview-static-target-body: a non-static class whose public surface is
        // entirely static members (methods + properties — e.g. `SnapshotContentHasher`) should
        // scaffold as a utility, not as an instance.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var fixturePath = workspace.GetPath("SampleLib", "SnapshotContentHasher.cs");
        await File.WriteAllTextAsync(fixturePath, """
namespace SampleLib;

public class SnapshotContentHasher
{
    public static string Version { get; } = "v1";

    public static string Hash(string content) => content.GetHashCode().ToString();
}
""", CancellationToken.None);

        await workspace.ReloadAsync(CancellationToken.None);

        var expectedFilePath = workspace.GetPath("SampleLib.Tests", "SnapshotContentHasherGeneratedTests.cs");

        var preview = await ScaffoldingService.PreviewScaffoldTestAsync(
            workspace.WorkspaceId,
            new ScaffoldTestDto(
                "SampleLib.Tests",
                "SnapshotContentHasher",
                "Hash",
                ReferenceTestFile: string.Empty),
            CancellationToken.None);

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);

        Assert.IsTrue(applyResult.Success, applyResult.Error);
        var contents = await File.ReadAllTextAsync(expectedFilePath, CancellationToken.None);

        Assert.IsFalse(contents.Contains("new SnapshotContentHasher("),
            "Static-members-only class scaffold must NOT emit `new SnapshotContentHasher(...)` in Arrange.");
        Assert.IsFalse(contents.Contains("var subject ="),
            "Static-members-only class scaffold must NOT emit a `subject` instance.");
    }

    [TestMethod]
    public async Task Scaffold_Test_EmitsUsings_ForCtorParamNamespaces()
    {
        // scaffold-test-preview-missing-usings: scaffold_test_preview previously only emitted
        // a using directive for the target type's own namespace. Constructor parameter types
        // from other namespaces were silently omitted, producing CS0246 errors in the generated
        // file. This test verifies all three ctor-parameter namespaces of
        // SampleLib.MultiNamespaceService appear as using directives.
        //
        // MultiNamespaceService(TextWriter, StringBuilder, IList<string>)
        //   → using System.IO;               (TextWriter)
        //   → using System.Text;             (StringBuilder)
        //   → using System.Collections.Generic; (IList<T>)
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);

        var preview = await ScaffoldingService.PreviewScaffoldTestAsync(
            workspace.WorkspaceId,
            new ScaffoldTestDto("SampleLib.Tests", "MultiNamespaceService", "Flush"),
            CancellationToken.None);
        await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);

        var generatedPath = workspace.GetPath("SampleLib.Tests", "MultiNamespaceServiceGeneratedTests.cs");
        var contents = await File.ReadAllTextAsync(generatedPath, CancellationToken.None);

        // Each constructor-parameter namespace must appear as a using directive so the
        // generated file compiles without CS0246 errors.
        StringAssert.Contains(contents, "using System.IO;",
            "System.IO must be emitted for the TextWriter ctor parameter.");
        StringAssert.Contains(contents, "using System.Text;",
            "System.Text must be emitted for the StringBuilder ctor parameter.");
        StringAssert.Contains(contents, "using System.Collections.Generic;",
            "System.Collections.Generic must be emitted for the IList<string> ctor parameter.");
    }

    private sealed class RecordingTestNameSuggestionProvider(string methodName) : ITestNameSuggestionProvider
    {
        public int CallCount { get; private set; }

        public ScaffoldTestNameSuggestionContext? LastContext { get; private set; }

        public Task<TestNameSuggestionResult> SuggestTestNameAsync(
            ScaffoldTestNameSuggestionContext context,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            CallCount++;
            LastContext = context;
            return Task.FromResult(new TestNameSuggestionResult(methodName));
        }
    }

    private sealed class ThrowingTestNameSuggestionProvider(OperationCanceledException exception)
        : ITestNameSuggestionProvider
    {
        public Task<TestNameSuggestionResult> SuggestTestNameAsync(
            ScaffoldTestNameSuggestionContext context,
            CancellationToken ct) =>
            Task.FromException<TestNameSuggestionResult>(exception);
    }
}
