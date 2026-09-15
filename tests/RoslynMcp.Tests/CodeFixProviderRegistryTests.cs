using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Coverage for code-fix-providers-missing-ca: <see cref="CodeFixProviderRegistry"/> must
/// surface providers for both compiler diagnostics (CS*) and analyzer diagnostics (CA*/IDE*)
/// rather than the previous CS8019-only hardcoded path.
/// </summary>
[TestClass]
public sealed class CodeFixProviderRegistryTests
{
    [TestMethod]
    public void Registry_LoadsAtLeastOneStaticProvider()
    {
        var registry = new CodeFixProviderRegistry(NullLogger<CodeFixProviderRegistry>.Instance);

        // Sanity: probe a handful of well-known diagnostic ids — at least one should resolve.
        // Different Roslyn versions ship different IDE/CS diagnostic ids; we just verify the
        // loader actually pulled providers, not which exact ids.
        string[] knownIds = ["CS8019", "IDE0005", "IDE0044", "CS0168", "CS0219", "CS1591"];
        var anyResolved = knownIds.Any(id => registry.GetProvidersFor(id).Count > 0);

        Assert.IsTrue(anyResolved,
            "Registry must expose at least one provider across well-known diagnostic ids " +
            $"({string.Join(", ", knownIds)}). The static loader likely failed to load " +
            "Microsoft.CodeAnalysis.CSharp.Features.");
    }

    [TestMethod]
    public void Registry_AnalyzerReferences_UseFullPathAndShareCachedLoad()
    {
        var assemblyPath = typeof(CodeFixProviderRegistryTests).Assembly.Location;
        var loader = new TestAnalyzerAssemblyLoader();
        var reference = new AnalyzerFileReference(assemblyPath, loader);
        Assert.AreNotEqual(reference.FullPath, reference.Display,
            "The fixture must distinguish the display label from the assembly path.");
        using var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;
        foreach (var name in new[] { "First", "Second" })
        {
            var projectId = ProjectId.CreateNewId();
            solution = solution.AddProject(projectId, name, name, LanguageNames.CSharp)
                .AddAnalyzerReference(projectId, reference);
        }
        var registry = new CodeFixProviderRegistry(NullLogger<CodeFixProviderRegistry>.Instance);

        var first = registry.GetProvidersForDetailed("TEST0001", solution);
        var second = registry.GetProvidersForDetailed("TEST0001", solution);

        Assert.IsEmpty(first.Providers);
        Assert.IsTrue(first.IsComplete);
        Assert.AreEqual(0, first.FailedProviderCount);
        Assert.IsEmpty(second.Providers);
        Assert.AreSequenceEqual(new[] { assemblyPath }, loader.LoadedPaths);
    }

    [TestMethod]
    public void Registry_UnknownDiagnostic_ReturnsEmpty()
    {
        var registry = new CodeFixProviderRegistry(NullLogger<CodeFixProviderRegistry>.Instance);
        var providers = registry.GetProvidersFor("ZZ9999");
        Assert.AreEqual(0, providers.Count);
    }

    [TestMethod]
    public void FirstProviderFor_ReturnsNullForUnknownDiagnostic()
    {
        var registry = new CodeFixProviderRegistry(NullLogger<CodeFixProviderRegistry>.Instance);
        Assert.IsNull(registry.FirstProviderFor("ZZ9999"));
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void Registry_DetailedProjectionPreservesCompletenessAndFailsClosedOnFalseAbsence(
        bool loadFailed,
        bool includeHealthyProvider)
    {
        var providers = includeHealthyProvider
            ? ImmutableArray.Create<CodeFixProvider>(new TestCodeFixProvider())
            : ImmutableArray<CodeFixProvider>.Empty;
        var failures = loadFailed
            ? ImmutableArray.Create(new FeatureProviderLoadFailure(
                FeatureProviderLoadFailureKind.ConstructorFailure,
                nameof(TestCodeFixProvider)))
            : ImmutableArray<FeatureProviderLoadFailure>.Empty;
        var registry = new CodeFixProviderRegistry(
            NullLogger<CodeFixProviderRegistry>.Instance,
            () => new FeatureProviderLoadResult<CodeFixProvider>(providers, failures),
            _ => new FeatureProviderLoadResult<CodeFixProvider>([], []));

        var detailed = registry.GetProvidersForDetailed("TEST0001");

        Assert.AreEqual(includeHealthyProvider ? 1 : 0, detailed.Providers.Count);
        Assert.AreEqual(!loadFailed, detailed.IsComplete);
        Assert.AreEqual(loadFailed ? 1 : 0, detailed.FailedProviderCount);
        Assert.AreEqual(includeHealthyProvider ? 1 : 0, detailed.LoadedProviderCount);
        if (loadFailed && !includeHealthyProvider)
        {
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                registry.GetProvidersFor("TEST0001"));
        }
        else
        {
            Assert.AreEqual(includeHealthyProvider ? 1 : 0, registry.GetProvidersFor("TEST0001").Count);
        }
    }

    /// <summary>
    /// Pins the static CSharp.Features provider set for representative CA rules.
    /// This does not inspect project analyzer assemblies or establish why an external
    /// provider is unavailable; callers can query IDE actions with get_code_actions.
    /// </summary>
    [TestMethod]
    public void Registry_CaSeriesRules_ReturnEmptySupportedFixes_DocumentedLimitation()
    {
        var registry = new CodeFixProviderRegistry(NullLogger<CodeFixProviderRegistry>.Instance);

        string[] caRuleIds = ["CA1826", "CA1848", "CA1822", "CA2201", "CA1416"];
        foreach (var caId in caRuleIds)
        {
            var providers = registry.GetProvidersFor(caId);
            Assert.AreEqual(0, providers.Count,
                $"The static CSharp.Features provider set should not expose '{caId}'.");
        }
    }

    private sealed class TestCodeFixProvider : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds => ["TEST0001"];

        public override FixAllProvider? GetFixAllProvider() => null;

        public override Task RegisterCodeFixesAsync(CodeFixContext context) => Task.CompletedTask;
    }

    private sealed class TestAnalyzerAssemblyLoader : IAnalyzerAssemblyLoader
    {
        public List<string> LoadedPaths { get; } = [];

        public void AddDependencyLocation(string fullPath) => Assert.IsTrue(File.Exists(fullPath));

        public System.Reflection.Assembly LoadFromPath(string fullPath)
        {
            LoadedPaths.Add(fullPath);
            // Return a real assembly with no providers. The callback proves the registry
            // honors the reference's loader instead of loading the test assembly directly.
            return typeof(CodeFixProviderRegistry).Assembly;
        }
    }
}
