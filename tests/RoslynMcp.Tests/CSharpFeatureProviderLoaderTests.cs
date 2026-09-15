using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class CSharpFeatureProviderLoaderTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void LoadExportedProviders_IgnoresUnrelatedMissingDependenciesButReportsBrokenExports(bool exportBrokenType)
    {
        var dependencyName = "MissingProviderDependency" + Guid.NewGuid().ToString("N");
        var dependency = CompileFixture(dependencyName, "public class MissingBase { }", []);
        var source = $$"""
            namespace Fixture;
            public sealed class TestExportAttribute : System.Attribute { }
            public class Container
            {
                [TestExport] public sealed class HealthyProvider { }
            }
            {{(exportBrokenType ? "[TestExport]" : "")}}
            public sealed class BrokenType : MissingBase { }
            """;
        var bytes = CompileFixture("ProviderFixture" + Guid.NewGuid().ToString("N"), source,
            [MetadataReference.CreateFromImage(dependency)]);
        var directory = Path.Combine(TestTempRoot.Current, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var assemblyPath = Path.Combine(directory, "ProviderFixture.dll");
        File.WriteAllBytes(assemblyPath, bytes);
        var context = new AssemblyLoadContext("provider-export-probe", isCollectible: true);
        try
        {
            var assembly = context.LoadFromAssemblyPath(assemblyPath);
            Assert.ThrowsExactly<ReflectionTypeLoadException>(() => assembly.GetTypes(),
                "The fixture must contain a real unresolved dependency.");
            var logger = new CaptureLogger<CodeFixProviderRegistry>();
            var result = CSharpFeatureProviderLoader.LoadFromAssemblyFactory<object>(
                () => assembly, logger, exportAttributeFullName: "Fixture.TestExportAttribute");

            Assert.HasCount(1, result.Providers);
            Assert.AreEqual("Fixture.Container+HealthyProvider", result.Providers[0].GetType().FullName);
            Assert.AreEqual(!exportBrokenType, result.IsComplete);
            Assert.AreEqual(exportBrokenType ? 1 : 0, result.FailedProviderCount);
            Assert.IsTrue(result.Failures.All(failure => failure.Kind == FeatureProviderLoadFailureKind.TypeLoad));
            Assert.IsTrue(logger.Entries.All(entry => entry.Exception is null));
        }
        finally
        {
            context.Unload();
            // The assembly-owned temp root is reclaimed after collectible contexts release files.
        }
    }

    private static byte[] CompileFixture(string name, string source, MetadataReference[] extraReferences)
    {
        var compilation = CSharpCompilation.Create(name,
            [CSharpSyntaxTree.ParseText(source)],
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) }.Concat(extraReferences),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return stream.ToArray();
    }

    [TestMethod]
    [DataRow("healthy", 1, 0, 0, 0, 0, true)]
    [DataRow("assembly-load", 0, 1, 0, 0, 0, false)]
    [DataRow("missing-constructor", 0, 0, 0, 1, 0, true)]
    [DataRow("throwing-constructor", 0, 0, 0, 0, 1, false)]
    [DataRow("type-load", 1, 0, 1, 0, 0, false)]
    public void LoadFromTypeSource_ClassifiesEveryOutcome(
        string scenario,
        int expectedLoaded,
        int expectedAssemblyFailures,
        int expectedTypeFailures,
        int expectedSkipped,
        int expectedConstructorFailures,
        bool expectedComplete)
    {
        var logger = new CaptureLogger<CodeFixProviderRegistry>();
        var result = scenario == "assembly-load"
            ? CSharpFeatureProviderLoader.LoadFromAssemblyFactory<TestProvider>(
                () => throw new FileLoadException("sensitive assembly detail"),
                logger)
            : CSharpFeatureProviderLoader.LoadFromTypeSource<TestProvider>(
                BuildTypeSource(scenario),
                logger);

        Assert.AreEqual(expectedLoaded, result.Providers.Length);
        Assert.AreEqual(
            expectedAssemblyFailures + expectedTypeFailures + expectedConstructorFailures,
            result.FailedProviderCount);
        Assert.AreEqual(expectedSkipped, result.SkippedProviderCount);
        Assert.AreEqual(expectedComplete, result.IsComplete);
        Assert.IsTrue(logger.Entries.Any(entry =>
            entry.Level == LogLevel.Information &&
            entry.Message.Contains($"loaded={expectedLoaded}", StringComparison.Ordinal) &&
            entry.Message.Contains($"assemblyLoadFailures={expectedAssemblyFailures}", StringComparison.Ordinal) &&
            entry.Message.Contains($"typeLoadFailures={expectedTypeFailures}", StringComparison.Ordinal) &&
            entry.Message.Contains($"skippedNoConstructor={expectedSkipped}", StringComparison.Ordinal) &&
            entry.Message.Contains($"constructorFailures={expectedConstructorFailures}", StringComparison.Ordinal)));
        Assert.IsTrue(logger.Entries.All(entry => entry.Exception is null),
            "Provider-loader logs must not carry raw exception objects into operator sinks.");
    }

    private static Func<Type[]> BuildTypeSource(string scenario) => scenario switch
    {
        "healthy" => () => [typeof(HealthyProvider)],
        "missing-constructor" => () => [typeof(NoParameterlessConstructorProvider)],
        "throwing-constructor" => () => [typeof(ThrowingConstructorProvider)],
        "type-load" => () => throw new ReflectionTypeLoadException(
            [typeof(HealthyProvider), null!],
            [new TypeLoadException("sensitive loader detail")]),
        _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown test scenario."),
    };

    private abstract class TestProvider;

    private sealed class HealthyProvider : TestProvider;

    private sealed class NoParameterlessConstructorProvider(string value) : TestProvider
    {
        public string Value { get; } = value;
    }

    private sealed class ThrowingConstructorProvider : TestProvider
    {
        public ThrowingConstructorProvider() =>
            throw new InvalidOperationException("sensitive constructor detail");
    }
}
