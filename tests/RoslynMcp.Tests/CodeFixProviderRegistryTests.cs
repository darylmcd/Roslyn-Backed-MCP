using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
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
    public void Registry_SamePathReplacementReference_UsesNewLoaderAndReusesEachReference()
    {
        var path = typeof(CodeFixProviderRegistryTests).Assembly.Location;
        var firstReference = new AnalyzerFileReference(path, new TestAnalyzerAssemblyLoader());
        var replacementReference = new AnalyzerFileReference(path, new TestAnalyzerAssemblyLoader());
        var firstProvider = new TestCodeFixProvider();
        var replacementProvider = new TestCodeFixProvider();
        var loads = new List<AnalyzerFileReference>();
        var registry = new CodeFixProviderRegistry(
            NullLogger<CodeFixProviderRegistry>.Instance,
            () => new FeatureProviderLoadResult<CodeFixProvider>([], []),
            reference =>
            {
                loads.Add(reference);
                return new FeatureProviderLoadResult<CodeFixProvider>(
                    [ReferenceEquals(reference, firstReference) ? firstProvider : replacementProvider], []);
            });

        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var firstSolution = workspace.CurrentSolution
            .AddProject(projectId, "Fixture", "Fixture", LanguageNames.CSharp)
            .AddAnalyzerReference(projectId, firstReference);
        var replacementSolution = firstSolution
            .RemoveAnalyzerReference(projectId, firstReference)
            .AddAnalyzerReference(projectId, replacementReference);

        Assert.AreSame(firstProvider, registry.GetProvidersForDetailed("TEST0001", firstSolution).Providers.Single());
        Assert.AreSame(firstProvider, registry.GetProvidersForDetailed("TEST0001", firstSolution).Providers.Single());
        Assert.AreSame(replacementProvider, registry.GetProvidersForDetailed("TEST0001", replacementSolution).Providers.Single());
        Assert.AreSame(replacementProvider, registry.GetProvidersForDetailed("TEST0001", replacementSolution).Providers.Single());
        Assert.AreSequenceEqual(new[] { firstReference, replacementReference }, loads);
    }

    [TestMethod]
    public void Registry_SamePathReferences_ContinuesAfterFailedLoaderAndDeduplicatesRepeatedIdentity()
    {
        var path = typeof(CodeFixProviderRegistryTests).Assembly.Location;
        var failedReference = new AnalyzerFileReference(path, new TestAnalyzerAssemblyLoader());
        var healthyReference = new AnalyzerFileReference(path, new TestAnalyzerAssemblyLoader());
        var healthyProvider = new TestCodeFixProvider();
        var loads = new List<AnalyzerFileReference>();
        var registry = new CodeFixProviderRegistry(
            NullLogger<CodeFixProviderRegistry>.Instance,
            () => new FeatureProviderLoadResult<CodeFixProvider>([], []),
            reference =>
            {
                loads.Add(reference);
                return ReferenceEquals(reference, failedReference)
                    ? new FeatureProviderLoadResult<CodeFixProvider>([],
                        [new FeatureProviderLoadFailure(FeatureProviderLoadFailureKind.AssemblyLoad, null)])
                    : new FeatureProviderLoadResult<CodeFixProvider>([healthyProvider], []);
            });
        using var workspace = new AdhocWorkspace();
        var firstProjectId = ProjectId.CreateNewId();
        var secondProjectId = ProjectId.CreateNewId();
        var thirdProjectId = ProjectId.CreateNewId();
        var solution = workspace.CurrentSolution
            .AddProject(firstProjectId, "First", "First", LanguageNames.CSharp)
            .AddAnalyzerReference(firstProjectId, failedReference)
            .AddProject(secondProjectId, "Second", "Second", LanguageNames.CSharp)
            .AddAnalyzerReference(secondProjectId, failedReference)
            .AddProject(thirdProjectId, "Third", "Third", LanguageNames.CSharp)
            .AddAnalyzerReference(thirdProjectId, healthyReference);

        var result = registry.GetProvidersForDetailed("TEST0001", solution);

        Assert.AreSame(healthyProvider, result.Providers.Single());
        Assert.IsFalse(result.IsComplete);
        Assert.AreEqual(1, result.FailedProviderCount);
        Assert.AreSequenceEqual(new[] { failedReference, healthyReference }, loads);
    }

    [TestMethod]
    public void Registry_RetiredAnalyzerReferenceAndCachedProvider_AreCollectible()
    {
        var contextReference = new WeakReference[1];
        var registry = new CodeFixProviderRegistry(
            NullLogger<CodeFixProviderRegistry>.Instance,
            () => new FeatureProviderLoadResult<CodeFixProvider>([], []),
            _ =>
            {
                var context = new AssemblyLoadContext("retired-analyzer", isCollectible: true);
                context.LoadFromAssemblyPath(typeof(CodeFixProviderRegistryTests).Assembly.Location);
                contextReference[0] = new WeakReference(context);
                var provider = new ContextHoldingCodeFixProvider(context);
                context.Unload();
                return new FeatureProviderLoadResult<CodeFixProvider>([provider], []);
            });
        var references = QueryAndRetireReference(registry);

        for (var attempt = 0; attempt < 10 &&
             (references.Reference.IsAlive || references.Provider.IsAlive || contextReference[0].IsAlive); attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.IsFalse(references.Reference.IsAlive, "The registry must not retain a retired analyzer reference.");
        Assert.IsFalse(references.Provider.IsAlive, "The registry must not retain providers from a retired reference.");
        Assert.IsFalse(contextReference[0].IsAlive, "A retired provider must not pin its load context.");
        GC.KeepAlive(registry);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Reference, WeakReference Provider) QueryAndRetireReference(
        CodeFixProviderRegistry registry)
    {
        var reference = new AnalyzerFileReference(
            typeof(CodeFixProviderRegistryTests).Assembly.Location, new TestAnalyzerAssemblyLoader());
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var solution = workspace.CurrentSolution
            .AddProject(projectId, "Fixture", "Fixture", LanguageNames.CSharp)
            .AddAnalyzerReference(projectId, reference);
        var provider = registry.GetProvidersForDetailed("TEST0001", solution).Providers.Single();
        return (new WeakReference(reference), new WeakReference(provider));
    }

    [TestMethod]
    public void Registry_DistinctReferencesWithCaseVariantPathsEachUseOwnLoader()
    {
        var fixtureDirectory = Directory.CreateTempSubdirectory("roslyn-provider-case-");
        var assemblyPath = typeof(CodeFixProviderRegistryTests).Assembly.Location;
        var path = Path.Combine(fixtureDirectory.FullName, Path.GetFileName(assemblyPath));
        var caseVariant = Path.Combine(
            fixtureDirectory.FullName,
            Path.GetFileName(path).ToUpperInvariant());
        Assert.AreNotEqual(path, caseVariant, "The fixture needs a path with different casing.");
        File.Copy(assemblyPath, path);
        // Windows resolves the spelling against the existing file. On a
        // case-sensitive filesystem the second spelling needs its own copy for
        // AnalyzerFileReference's eager dependency-location validation.
        if (!File.Exists(caseVariant))
            File.Copy(path, caseVariant);
        try
        {
            var firstReference = new AnalyzerFileReference(path, new TestAnalyzerAssemblyLoader());
            var secondReference = new AnalyzerFileReference(caseVariant, new TestAnalyzerAssemblyLoader());
            var loadCount = 0;
            var registry = new CodeFixProviderRegistry(
                NullLogger<CodeFixProviderRegistry>.Instance,
                () => new FeatureProviderLoadResult<CodeFixProvider>([], []),
                _ =>
                {
                    loadCount++;
                    return new FeatureProviderLoadResult<CodeFixProvider>([], []);
                });
            using var workspace = new AdhocWorkspace();
            var firstProjectId = ProjectId.CreateNewId();
            var secondProjectId = ProjectId.CreateNewId();
            var solution = workspace.CurrentSolution
                .AddProject(firstProjectId, "First", "First", LanguageNames.CSharp)
                .AddAnalyzerReference(firstProjectId, firstReference)
                .AddProject(secondProjectId, "Second", "Second", LanguageNames.CSharp)
                .AddAnalyzerReference(secondProjectId, secondReference);

            _ = registry.GetProvidersForDetailed("TEST0001", solution);

            Assert.AreEqual(2, loadCount);
        }
        finally
        {
            fixtureDirectory.Delete(recursive: true);
        }
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

    private sealed class ContextHoldingCodeFixProvider(AssemblyLoadContext context) : CodeFixProvider
    {
        private readonly AssemblyLoadContext _context = context;

        public override ImmutableArray<string> FixableDiagnosticIds => ["TEST0001"];

        public override FixAllProvider? GetFixAllProvider() => null;

        public override Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            GC.KeepAlive(_context);
            return Task.CompletedTask;
        }
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
