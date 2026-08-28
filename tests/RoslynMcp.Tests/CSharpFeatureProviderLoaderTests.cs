using System.Reflection;
using Microsoft.Extensions.Logging;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class CSharpFeatureProviderLoaderTests
{
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
