using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression coverage for <c>compilation-cache-adoption-read-side</c>: the read-side analysis
/// services must obtain project compilations through the shared <see cref="ICompilationCache"/>
/// rather than calling <c>project.GetCompilationAsync</c> directly. Batch 1 covered
/// <see cref="CouplingAnalysisService"/>, <see cref="ExceptionFlowService"/>, and
/// <see cref="AnalyzerInfoService"/>; batch 2 adds <see cref="TypeConsumersService"/>,
/// <see cref="CodePatternAnalyzer"/>, and <see cref="SymbolSearchService"/>; group-a core adds
/// <see cref="TestReferenceMapService"/>, <see cref="ReferenceService"/>,
/// <see cref="ImpactSweepService"/>, <see cref="MutationAnalysisService"/>, and
/// <see cref="SymbolRelationshipService"/>; group-b core adds <see cref="BuildService"/>,
/// <see cref="FixAllService"/>, <see cref="BulkRefactoringService"/>, and
/// <see cref="CrossProjectRefactoringService"/>; the group-b tail adds
/// <see cref="InterfaceExtractionService"/>.
/// <para>
/// A reference-equality check alone cannot prove adoption — Roslyn memoizes a
/// <see cref="Compilation"/> on its owning <see cref="Project"/>, so two direct calls would also
/// return the same instance. The recording cache below makes the routing observable: if a service
/// bypassed the cache, <see cref="RecordingCompilationCache.GetCompilationCallCount"/> would stay
/// at zero. The second assertion confirms the cache hands back the reference-equal warm
/// compilation across successive read calls at an unchanged workspace version.
/// </para>
/// </summary>
[TestClass]
public sealed class CompilationCacheAdoptionTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    /// <summary>
    /// Regression cover for the shared-task poisoning hazard called out in the
    /// <c>compilation-cache-adoption-read-side</c> item doc: a cached entry is shared by every
    /// later caller at the same workspace version, so it must not inherit the cancellation
    /// lifetime of whichever caller happened to arrive first. Caller A supplies an already-
    /// canceled token and must observe <see cref="OperationCanceledException"/> from its own
    /// wrapper; caller B then reads the SAME (workspaceId, project) key at the unchanged version
    /// with an uncancelable token and must receive a real <see cref="Compilation"/>. Pre-fix the
    /// entry installed by caller A held a <c>Canceled</c> task and caller B rethrew A's
    /// cancellation until the next workspace version bump.
    /// </summary>
    [TestMethod]
    public async Task GetCompilationAsync_OneCallersCancellation_DoesNotPoisonSharedEntry()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        using var cache = new CompilationCache(WorkspaceManager);
        var project = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId).Projects.First();

        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(
            () => cache.GetCompilationAsync(workspace.WorkspaceId, project, canceled.Token),
            "A caller passing an already-canceled token must observe its own cancellation.");

        var compilation = await cache.GetCompilationAsync(workspace.WorkspaceId, project, CancellationToken.None);
        Assert.IsNotNull(compilation,
            "A later caller reading the same key at the unchanged workspace version must get a real " +
            "compilation — an OperationCanceledException here means the earlier caller's cancellation " +
            "poisoned the shared cache entry.");

        var again = await cache.GetCompilationAsync(workspace.WorkspaceId, project, CancellationToken.None);
        Assert.AreSame(compilation, again,
            "The warm-compilation sharing contract must survive the cancellation-decoupling fix.");
    }

    /// <summary>
    /// Analyzer-bound twin of
    /// <see cref="GetCompilationAsync_OneCallersCancellation_DoesNotPoisonSharedEntry"/> — the
    /// <c>_analyzerBound</c> map installs its own shared task and needs the identical treatment.
    /// The bound result may legitimately be <c>null</c> when the project declares no analyzers, so
    /// the assertion is that caller B completes at all rather than rethrowing caller A's
    /// cancellation.
    /// </summary>
    [TestMethod]
    public async Task GetCompilationWithAnalyzersAsync_OneCallersCancellation_DoesNotPoisonSharedEntry()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        using var cache = new CompilationCache(WorkspaceManager);
        var project = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId).Projects.First();

        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(
            () => cache.GetCompilationWithAnalyzersAsync(workspace.WorkspaceId, project, canceled.Token),
            "A caller passing an already-canceled token must observe its own cancellation.");

        // Must not throw: pre-fix this rethrew the first caller's OperationCanceledException.
        _ = await cache.GetCompilationWithAnalyzersAsync(workspace.WorkspaceId, project, CancellationToken.None);
    }

    [TestMethod]
    public async Task CouplingAnalysisService_ObtainsCompilations_ThroughSharedCache()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var cache = new RecordingCompilationCache(new CompilationCache(WorkspaceManager));
        var service = new CouplingAnalysisService(WorkspaceManager, cache, NullLogger<CouplingAnalysisService>.Instance);

        var afterFirstRun = await RunTwiceAndCaptureAsync(cache, () =>
            service.GetCouplingMetricsAsync(
                workspace.WorkspaceId,
                projectFilter: null,
                limit: 50,
                excludeTestProjects: false,
                includeInterfaces: false,
                CancellationToken.None));

        AssertRoutedThroughCacheAndShared(cache, afterFirstRun);
    }

    /// <summary>
    /// Regression cover for <c>analysis-services-uncached-full-solution-scans</c> (change 1):
    /// <see cref="CouplingAnalysisService.ComputeAfferentCouplingAsync"/> must obtain the semantic
    /// model for every referencing document through the shared <see cref="ICompilationCache"/>
    /// instead of the raw <c>doc.GetSemanticModelAsync</c> Document API. The candidate-enumeration
    /// + efferent pass already fetches exactly one compilation per project through the cache; the
    /// afferent reference scan fetches once more per referencing document, so a correctly-routed
    /// implementation drives the cache call count strictly above the project count. Pre-fix the
    /// afferent path bypassed the cache entirely and the count would equal the project count.
    /// </summary>
    [TestMethod]
    public async Task CouplingAnalysisService_AfferentScan_RoutesReferencingDocsThroughSharedCache()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var cache = new RecordingCompilationCache(new CompilationCache(WorkspaceManager));
        var service = new CouplingAnalysisService(WorkspaceManager, cache, NullLogger<CouplingAnalysisService>.Instance);

        var solution = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var projectCount = solution.Projects.Count();

        var metrics = await service.GetCouplingMetricsAsync(
            workspace.WorkspaceId,
            projectFilter: null,
            limit: 50,
            excludeTestProjects: false,
            includeInterfaces: false,
            CancellationToken.None);

        Assert.IsTrue(metrics.Count > 0, "Expected coupling metrics for the sample workspace.");
        Assert.IsTrue(cache.GetCompilationCallCount > projectCount,
            "Afferent coupling must route each referencing document's semantic model through " +
            $"ICompilationCache — expected the cache call count ({cache.GetCompilationCallCount}) to " +
            $"exceed the project count ({projectCount}); an equal count means the afferent scan still " +
            "uses the raw doc.GetSemanticModelAsync path.");
    }

    [TestMethod]
    public async Task ExceptionFlowService_ObtainsCompilations_ThroughSharedCache()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var cache = new RecordingCompilationCache(new CompilationCache(WorkspaceManager));
        var service = new ExceptionFlowService(WorkspaceManager, cache, NullLogger<ExceptionFlowService>.Instance);

        var afterFirstRun = await RunTwiceAndCaptureAsync(cache, () =>
            service.TraceExceptionFlowAsync(
                workspace.WorkspaceId,
                "System.Exception",
                scopeProjectFilter: null,
                maxResults: null,
                CancellationToken.None));

        AssertRoutedThroughCacheAndShared(cache, afterFirstRun);
    }

    [TestMethod]
    public async Task AnalyzerInfoService_ObtainsCompilations_ThroughSharedCache()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var cache = new RecordingCompilationCache(new CompilationCache(WorkspaceManager));
        var service = new AnalyzerInfoService(WorkspaceManager, cache, NullLogger<AnalyzerInfoService>.Instance);

        var afterFirstRun = await RunTwiceAndCaptureAsync(cache, () =>
            service.ListAnalyzersAsync(workspace.WorkspaceId, projectFilter: null, CancellationToken.None));

        AssertRoutedThroughCacheAndShared(cache, afterFirstRun);
    }

    [TestMethod]
    public async Task TypeConsumersService_ObtainsCompilations_ThroughSharedCache()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var cache = new RecordingCompilationCache(new CompilationCache(WorkspaceManager));
        var service = new TypeConsumersService(WorkspaceManager, cache, NullLogger<TypeConsumersService>.Instance);

        var afterFirstRun = await RunTwiceAndCaptureAsync(cache, () =>
            service.FindTypeConsumersAsync(
                workspace.WorkspaceId,
                "SampleLib.IAnimal",
                limit: 100,
                CancellationToken.None));

        AssertRoutedThroughCacheAndShared(cache, afterFirstRun);
    }

    [TestMethod]
    public async Task CodePatternAnalyzer_ObtainsCompilations_ThroughSharedCache()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var cache = new RecordingCompilationCache(new CompilationCache(WorkspaceManager));
        var service = new CodePatternAnalyzer(WorkspaceManager, cache, NullLogger<CodePatternAnalyzer>.Instance);

        // Exercises BOTH converted call paths in one method: FindReflectionUsagesAsync (the
        // instance-method site) and SemanticSearchAsync -> CollectSemanticSearchMatchesAsync
        // (the private static helper site).
        var afterFirstRun = await RunTwiceAndCaptureAsync(cache, async () =>
        {
            await service.FindReflectionUsagesAsync(workspace.WorkspaceId, projectFilter: null, CancellationToken.None);
            await service.SemanticSearchAsync(workspace.WorkspaceId, "Dog", projectFilter: null, limit: 50, CancellationToken.None);
        });

        AssertRoutedThroughCacheAndShared(cache, afterFirstRun);
    }

    [TestMethod]
    public async Task SymbolSearchService_ObtainsCompilations_ThroughSharedCache()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var cache = new RecordingCompilationCache(new CompilationCache(WorkspaceManager));
        var service = new SymbolSearchService(WorkspaceManager, cache, NullLogger<SymbolSearchService>.Instance);

        // maxResults is deliberately high so the primary pattern search leaves budget under the
        // cap and the FQN-substring fallback (the converted GetCompilationAsync site) runs.
        var afterFirstRun = await RunTwiceAndCaptureAsync(cache, () =>
            service.SearchSymbolsAsync(
                workspace.WorkspaceId,
                "IAnimal",
                projectFilter: null,
                kindFilter: null,
                namespaceFilter: null,
                maxResults: 100,
                CancellationToken.None));

        AssertRoutedThroughCacheAndShared(cache, afterFirstRun);
    }

    [TestMethod]
    public async Task TestReferenceMapService_ObtainsCompilations_ThroughSharedCache()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var cache = new RecordingCompilationCache(new CompilationCache(WorkspaceManager));
        var service = new TestReferenceMapService(WorkspaceManager, cache);

        // BuildAsync routes BOTH converted sites: CollectProductiveSymbolsAsync (productive-scope
        // projects) and RecordTestProjectReferencesAsync (the test projects). Either path alone
        // is enough to drive the call count above zero.
        var afterFirstRun = await RunTwiceAndCaptureAsync(cache, () =>
            service.BuildAsync(
                workspace.WorkspaceId,
                projectName: null,
                offset: 0,
                limit: 500,
                maxMockDriftWarnings: 50,
                CancellationToken.None));

        AssertRoutedThroughCacheAndShared(cache, afterFirstRun);
    }

    [TestMethod]
    public async Task ReferenceService_ObtainsCompilations_ThroughSharedCache()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var cache = new RecordingCompilationCache(new CompilationCache(WorkspaceManager));
        var service = new ReferenceService(WorkspaceManager, cache, NullLogger<ReferenceService>.Instance);

        // FindSiblingInterfaceImplementationsAsync -> FindInterfaceMemberImplementationsAsync is the
        // converted site. Anchor on the IAnimal.Speak interface member so the walk runs the
        // per-project compilation fetch through the cache (SampleLib.Dog implements IAnimal.Speak).
        var solution = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var animalFile = solution.Projects.SelectMany(p => p.Documents).First(d => d.Name == "IAnimal.cs");

        var afterFirstRun = await RunTwiceAndCaptureAsync(cache, () =>
            service.FindSiblingInterfaceImplementationsAsync(
                workspace.WorkspaceId,
                SymbolLocator.BySource(animalFile.FilePath!, 6, 13),
                CancellationToken.None));

        AssertRoutedThroughCacheAndShared(cache, afterFirstRun);
    }

    [TestMethod]
    public async Task ImpactSweepService_ObtainsCompilations_ThroughSharedCache()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var cache = new RecordingCompilationCache(new CompilationCache(WorkspaceManager));
        var service = new ImpactSweepService(
            WorkspaceManager,
            new ReferenceService(WorkspaceManager, cache, NullLogger<ReferenceService>.Instance),
            new DiagnosticService(WorkspaceManager, cache, new CodeFixProviderRegistry(NullLogger<CodeFixProviderRegistry>.Instance)),
            cache);

        // CollectPersistenceLayerFindingsAsync (its DTO-sibling scan and the hoisted
        // CollectMapperCandidateTypesAsync helper) are the two converted sites. They only fetch
        // compilations when the swept symbol is a property — anchor on SampleLib.Dog.Name (a
        // property) so the per-project compilation loop runs through the cache.
        var solution = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var dogFile = solution.Projects.SelectMany(p => p.Documents).First(d => d.Name == "Dog.cs");

        var afterFirstRun = await RunTwiceAndCaptureAsync(cache, () =>
            service.SweepAsync(
                workspace.WorkspaceId,
                SymbolLocator.BySource(dogFile.FilePath!, 5, 19),
                CancellationToken.None));

        AssertRoutedThroughCacheAndShared(cache, afterFirstRun);
    }

    /// <summary>
    /// Regression cover for <c>analysis-services-uncached-full-solution-scans</c> (change 2): the
    /// mapper-candidate scan in <see cref="ImpactSweepService.CollectPersistenceLayerFindingsAsync"/>
    /// must be HOISTED out of the per-DTO-sibling loop, so it fetches exactly one compilation per
    /// project (project-count scaling) instead of the pre-fix O(dtoSiblings × projects) — the former
    /// <c>FindMapperTypesAsync</c> re-ran the whole project loop once per sibling. Driving this
    /// through <see cref="ImpactSweepService.SweepAsync"/> would muddy the cache call count with the
    /// reference/diagnostic sub-queries' own fetches, so the invariant is asserted directly against
    /// the hoisted <see cref="ImpactSweepService.CollectMapperCandidateTypesAsync"/> /
    /// <see cref="ImpactSweepService.FilterMappersForDtoType"/> helpers on a synthetic two-project
    /// fixture with a counting cache.
    /// </summary>
    [TestMethod]
    public async Task ImpactSweepService_MapperCandidateScan_ScalesByProjectCount_NotBySiblingCount()
    {
        var (solution, projectCount, domain, dto, otherDto) = await BuildMapperHoistFixtureAsync();
        var cache = new CountingAdhocCompilationCache();

        var candidates = await ImpactSweepService.CollectMapperCandidateTypesAsync(
            "hoist-ws", cache, solution, domain, CancellationToken.None);

        Assert.AreEqual(projectCount, cache.CallCount,
            "The hoisted candidate scan must fetch exactly one compilation per project — cache call " +
            $"count ({cache.CallCount}) should equal the project count ({projectCount}).");

        // Per-sibling narrowing is pure in-memory: replaying the filter for several DTO siblings must
        // add ZERO further compilation fetches. Pre-hoist, FindMapperTypesAsync re-ran the project
        // loop once per sibling (O(dtoSiblings × projects)); the hoist makes it O(projects) — so the
        // cache call count must stay pinned at the project count regardless of how many siblings run.
        foreach (var sibling in new[] { dto, otherDto, dto })
        {
            _ = ImpactSweepService.FilterMappersForDtoType(candidates, domain, sibling);
        }

        Assert.AreEqual(projectCount, cache.CallCount,
            "Per-sibling narrowing must trigger no additional compilation fetches — the scan is " +
            $"hoisted out of the dtoSibling loop, so the call count ({cache.CallCount}) must remain " +
            $"the project count ({projectCount}) after filtering three siblings.");
    }

    /// <summary>
    /// Regression cover for <c>analysis-services-uncached-full-solution-scans</c> (change 2,
    /// correctness half): the hoist must preserve the exact same-method matching semantics of the
    /// former <c>mentionsBoth</c> predicate. <see cref="ImpactSweepService.CollectMapperCandidateTypesAsync"/>
    /// returns a sibling-independent SUPERSET (every mapper-suffixed type with a domain-mentioning
    /// method), and <see cref="ImpactSweepService.FilterMappersForDtoType"/> narrows it to only types
    /// where a SINGLE method mentions BOTH the domain type and the DTO — a type whose domain mention
    /// and DTO mention live in different methods must NOT match.
    /// </summary>
    [TestMethod]
    public async Task ImpactSweepService_MapperCandidateScan_FilterKeepsOnlySameMethodMatches()
    {
        var (solution, _, domain, dto, _) = await BuildMapperHoistFixtureAsync();
        var cache = new CountingAdhocCompilationCache();

        var candidates = await ImpactSweepService.CollectMapperCandidateTypesAsync(
            "hoist-ws", cache, solution, domain, CancellationToken.None);

        // The superset is sibling-independent: it keeps AlphaMapper (domain + DTO in ONE method),
        // BetaConverter (domain + DTO in SEPARATE methods), and GammaAdapter (domain-only) — every
        // mapper-suffixed type with at least one domain-mentioning method.
        CollectionAssert.AreEquivalent(
            new[] { "AlphaMapper", "BetaConverter", "GammaAdapter" },
            candidates.Select(c => c.Name).ToArray(),
            "Candidate scan must return the sibling-independent superset of domain-mentioning mappers.");

        var matches = ImpactSweepService.FilterMappersForDtoType(candidates, domain, dto);

        // Same-method semantics: only AlphaMapper has a single method mentioning BOTH the domain type
        // and the DTO. BetaConverter mentions each in separate methods; GammaAdapter never mentions
        // the DTO. Both must be excluded — the exact predicate the pre-hoist mentionsBoth check had.
        CollectionAssert.AreEqual(
            new[] { "AlphaMapper" },
            matches.Select(m => m.Name).ToArray(),
            "Only a mapper whose domain + DTO mention share ONE method may match after the hoist.");
    }

    [TestMethod]
    public async Task MutationAnalysisService_ObtainsCompilations_ThroughSharedCache()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var cache = new RecordingCompilationCache(new CompilationCache(WorkspaceManager));
        var service = new MutationAnalysisService(WorkspaceManager, cache);

        // FindTypeMutationsAsync -> ResolveContainingCompilationAsync is the converted site. It runs
        // only for a named-type target — anchor on SampleLib.Dog so the project loop that locates the
        // defining compilation fetches through the cache.
        var afterFirstRun = await RunTwiceAndCaptureAsync(cache, () =>
            service.FindTypeMutationsAsync(
                workspace.WorkspaceId,
                SymbolLocator.ByMetadataName("SampleLib.Dog"),
                CancellationToken.None));

        AssertRoutedThroughCacheAndShared(cache, afterFirstRun);
    }

    [TestMethod]
    public async Task SymbolRelationshipService_ObtainsCompilations_ThroughSharedCache()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var cache = new RecordingCompilationCache(new CompilationCache(WorkspaceManager));
        var service = new SymbolRelationshipService(
            WorkspaceManager,
            new ReferenceService(WorkspaceManager, cache, NullLogger<ReferenceService>.Instance),
            cache,
            NullLogger<SymbolRelationshipService>.Instance);

        // TryResolveByQualifiedSignatureAsync is the converted site. It only runs when the standard
        // metadata-name resolve returns null — a fully-qualified signature whose last dot lands inside
        // the parenthesized parameter list (`SampleLib.Dog.Fetch(System.String)`) defeats the last-dot
        // split, so the fallback's per-project compilation loop runs through the cache.
        var afterFirstRun = await RunTwiceAndCaptureAsync(cache, () =>
            service.GetSignatureHelpAsync(
                workspace.WorkspaceId,
                SymbolLocator.ByMetadataName("SampleLib.Dog.Fetch(System.String)"),
                preferDeclaringMember: false,
                CancellationToken.None));

        AssertRoutedThroughCacheAndShared(cache, afterFirstRun);
    }

    [TestMethod]
    public async Task BuildService_ObtainsCompilations_ThroughSharedCache()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var cache = new RecordingCompilationCache(new CompilationCache(WorkspaceManager));

        // EnrichDiagnosticSpansAsync -> CollectRoslynDiagnosticSpansAsync is the converted site. Its
        // candidate gate only admits a parsed build diagnostic that HAS a start position but NO end
        // position, so the command executor is stubbed with a canned MSBuild warning line anchored on
        // a real sample document. Shelling out to a real `dotnet build` would emit no diagnostics at
        // all against the clean sample, skip the helper entirely, and produce a false-green
        // zero-call assertion.
        var solution = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var dogFile = solution.Projects.SelectMany(p => p.Documents).First(d => d.Name == "Dog.cs");
        using var executor = new CannedBuildOutputExecutor(
            $"{dogFile.FilePath}(5,19): warning CS0219: The variable 'unused' is assigned but its value is never used");
        var service = new BuildService(
            WorkspaceManager,
            executor,
            cache,
            NullLogger<BuildService>.Instance);

        var afterFirstRun = await RunTwiceAndCaptureAsync(cache, () =>
            service.BuildWorkspaceAsync(workspace.WorkspaceId, CancellationToken.None));

        AssertRoutedThroughCacheAndShared(cache, afterFirstRun);
    }

    [TestMethod]
    public async Task FixAllService_ObtainsCompilations_ThroughSharedCache()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var cache = new RecordingCompilationCache(new CompilationCache(WorkspaceManager));
        var service = new FixAllService(
            WorkspaceManager,
            new PreviewStore(),
            cache,
            NullLogger<FixAllService>.Instance);

        // CollectDiagnosticsAsync is the converted site. PreviewFixAllAsync early-returns BEFORE it
        // unless a CodeFixProvider *and* a FixAllProvider both resolve for the id, so anchor on
        // IDE0161 (ConvertNamespaceCodeFixProvider) — the same id FixAllServiceGuidanceTests relies
        // on for reaching the active-provider path. Solution scope makes the collection walk every
        // project, so a correctly-routed implementation drives one cache fetch per project.
        var afterFirstRun = await RunTwiceAndCaptureAsync(cache, () =>
            service.PreviewFixAllAsync(
                workspace.WorkspaceId,
                diagnosticId: "IDE0161",
                scope: "solution",
                filePath: null,
                projectName: null,
                CancellationToken.None));

        AssertRoutedThroughCacheAndShared(cache, afterFirstRun);
    }

    [TestMethod]
    public async Task BulkRefactoringService_ObtainsCompilations_ThroughSharedCache()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var cache = new RecordingCompilationCache(new CompilationCache(WorkspaceManager));
        var service = new BulkRefactoringService(WorkspaceManager, new PreviewStore(), cache);

        // All THREE converted resolve-helpers must share the one injected cache instance, so the
        // reference-equality assertion below holds across the whole service rather than one path.
        var afterFirstRun = await RunTwiceAndCaptureAsync(cache, async () =>
        {
            // ResolveTypeByNameAsync — the fully-qualified-metadata sweep runs over every project
            // before the simple-name fallback resolves 'IAnimal'.
            await service.PreviewBulkReplaceTypeAsync(
                workspace.WorkspaceId, "IAnimal", "Shape", scope: null, CancellationToken.None);

            // ResolveMethodBySignatureAsync — a qualified-but-unresolvable signature drives the full
            // per-project compilation loop; the public method then throws because nothing matched.
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                service.PreviewReplaceInvocationAsync(
                    workspace.WorkspaceId,
                    oldMethod: "SampleLib.NoSuchHelper.Build(int, string)",
                    newMethod: "SampleLib.NoSuchHelper.Generate(string, int)",
                    scope: null,
                    CancellationToken.None));

            // ResolveMethodByBareNameAsync — an unqualified name defeats the last-dot split, so the
            // bare-name fallback runs its own per-project compilation loop.
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                service.PreviewReplaceInvocationAsync(
                    workspace.WorkspaceId,
                    oldMethod: "NoSuchBareBuild(int, string)",
                    newMethod: "NoSuchBareGenerate(string, int)",
                    scope: null,
                    CancellationToken.None));
        });

        AssertRoutedThroughCacheAndShared(cache, afterFirstRun);
    }

    [TestMethod]
    public async Task CrossProjectRefactoringService_ObtainsCompilations_ThroughSharedCache()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var cache = new RecordingCompilationCache(new CompilationCache(WorkspaceManager));
        var service = new CrossProjectRefactoringService(WorkspaceManager, new PreviewStore(), cache);

        // DetectExistingTypeConflictAsync is the converted site — reached from BOTH
        // PreviewExtractInterfaceAsync and (via CreateInterfaceExtractionSolutionAsync)
        // PreviewDependencyInversionAsync, always against the LIVE solution. It throws on a
        // collision, so the interface name is deliberately one the sample cannot already declare.
        // Explicitly NOT covered here: the SymbolResolver.ResolveByMetadataNameAsync call in
        // PreviewDependencyInversionAsync, which reads a FORKED solution and stays uncached.
        var sourceFilePath = workspace.GetPath("SampleLib", "AnimalService.cs");

        var afterFirstRun = await RunTwiceAndCaptureAsync(cache, () =>
            service.PreviewExtractInterfaceAsync(
                workspace.WorkspaceId,
                sourceFilePath,
                "AnimalService",
                interfaceName: "IAnimalServiceCompilationCacheProbe",
                targetProjectName: null,
                CancellationToken.None));

        AssertRoutedThroughCacheAndShared(cache, afterFirstRun);
    }

    [TestMethod]
    public async Task InterfaceExtractionService_ObtainsCompilations_ThroughSharedCache()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var cache = new RecordingCompilationCache(new CompilationCache(WorkspaceManager));
        var service = new InterfaceExtractionService(
            WorkspaceManager, new PreviewStore(), cache,
            NullLogger<InterfaceExtractionService>.Instance);

        // ValidateNoConflictsAsync is the converted site — it sweeps every project of the LIVE
        // pre-fork solution for a same-name type. It only runs inside the `!alreadyImplements`
        // guard, so the probe interface name must be one AnimalService cannot already implement,
        // otherwise the cache is never touched and the call-count assertion below fails.
        // Explicitly NOT covered here: ReplaceConcreteUsagesAsync, which compiles a FORKED
        // solution and therefore stays on the raw project.GetCompilationAsync path — hence
        // replaceUsages: false.
        var sourceFilePath = workspace.GetPath("SampleLib", "AnimalService.cs");

        var afterFirstRun = await RunTwiceAndCaptureAsync(cache, () =>
            service.PreviewExtractInterfaceAsync(
                workspace.WorkspaceId,
                sourceFilePath,
                "AnimalService",
                interfaceName: "IAnimalServiceInterfaceExtractionCacheProbe",
                memberNames: null,
                replaceUsages: false,
                CancellationToken.None));

        AssertRoutedThroughCacheAndShared(cache, afterFirstRun);
    }

    /// <summary>
    /// Runs the service once (proving it touched the cache), snapshots the compilations the cache
    /// handed out, then runs it a second time at the unchanged workspace version so the caller can
    /// assert the warm compilations were reused.
    /// </summary>
    private static async Task<IReadOnlyDictionary<ProjectId, Compilation>> RunTwiceAndCaptureAsync(
        RecordingCompilationCache cache, Func<Task> invokeService)
    {
        await invokeService();
        Assert.IsTrue(cache.GetCompilationCallCount > 0,
            "Service must obtain its compilation through ICompilationCache, not project.GetCompilationAsync.");
        var afterFirstRun = cache.SnapshotReturned();

        await invokeService();
        return afterFirstRun;
    }

    private static void AssertRoutedThroughCacheAndShared(
        RecordingCompilationCache cache, IReadOnlyDictionary<ProjectId, Compilation> afterFirstRun)
    {
        Assert.IsTrue(afterFirstRun.Count > 0, "Expected at least one project compilation to flow through the cache.");

        var afterSecondRun = cache.SnapshotReturned();
        foreach (var (projectId, firstCompilation) in afterFirstRun)
        {
            Assert.IsTrue(afterSecondRun.TryGetValue(projectId, out var secondCompilation),
                "The same project should be compiled through the cache on the repeat read.");
            Assert.AreSame(firstCompilation, secondCompilation,
                "At an unchanged workspace version the cache must hand back the reference-equal warm compilation.");
        }
    }

    /// <summary>
    /// Builds a synthetic two-project <see cref="AdhocWorkspace"/> for the mapper-candidate hoist
    /// tests. Project A holds a domain type (<c>Widget</c>), two DTO types (<c>Gadget</c>,
    /// <c>Trinket</c>), and three mapper-suffixed types exercising every branch: <c>AlphaMapper</c>
    /// (domain + DTO in ONE method → same-method match), <c>BetaConverter</c> (domain + DTO in
    /// SEPARATE methods → superset but not a match), and <c>GammaAdapter</c> (domain-only). Both the
    /// domain and DTO types appear as method PARAMETERS so the default <c>ToDisplayString()</c> — which
    /// omits return types — still renders them; the mapper class names deliberately avoid the domain
    /// and DTO substrings so the containing-type name in the display string cannot cause a false match.
    /// Project B is filler so the project-count-scaling assertion observes a count of two, not one.
    /// </summary>
    private static async Task<(Solution Solution, int ProjectCount, INamedTypeSymbol Domain, INamedTypeSymbol Dto, INamedTypeSymbol OtherDto)>
        BuildMapperHoistFixtureAsync()
    {
        const string fixtureSource = """
            namespace HoistFixture;

            public class Widget { }
            public class Gadget { }
            public class Trinket { }

            // Same-method: ONE method takes the domain type AND the DTO as parameters.
            public class AlphaMapper
            {
                public void Map(Widget widget, Gadget gadget) { }
            }

            // Split: the domain type and the DTO are each mentioned, but in SEPARATE methods.
            public class BetaConverter
            {
                public void TakeWidget(Widget widget) { }
                public void TakeGadget(Gadget gadget) { }
            }

            // Domain-only: mentions the domain type, never a DTO.
            public class GammaAdapter
            {
                public void Use(Widget widget) { }
            }
            """;

        var reference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
        var workspace = new AdhocWorkspace();

        var projectA = ProjectId.CreateNewId();
        workspace.AddProject(ProjectInfo.Create(
            projectA, VersionStamp.Create(), "ProjectA", "ProjectA", LanguageNames.CSharp,
            metadataReferences: new[] { reference }));
        workspace.AddDocument(DocumentInfo.Create(
            DocumentId.CreateNewId(projectA), "Fixture.cs",
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From(fixtureSource), VersionStamp.Create()))));

        var projectB = ProjectId.CreateNewId();
        workspace.AddProject(ProjectInfo.Create(
            projectB, VersionStamp.Create(), "ProjectB", "ProjectB", LanguageNames.CSharp,
            metadataReferences: new[] { reference }));
        workspace.AddDocument(DocumentInfo.Create(
            DocumentId.CreateNewId(projectB), "Filler.cs",
            loader: TextLoader.From(TextAndVersion.Create(
                SourceText.From("namespace Filler; public class Unrelated { }"), VersionStamp.Create()))));

        var solution = workspace.CurrentSolution;
        var compilation = await solution.GetProject(projectA)!.GetCompilationAsync(CancellationToken.None);
        var domain = compilation!.GetTypeByMetadataName("HoistFixture.Widget")!;
        var dto = compilation.GetTypeByMetadataName("HoistFixture.Gadget")!;
        var otherDto = compilation.GetTypeByMetadataName("HoistFixture.Trinket")!;
        return (solution, 2, domain, dto, otherDto);
    }

    /// <summary>
    /// Stub <see cref="IGatedCommandExecutor"/> that returns a fixed <c>dotnet build</c> stdout
    /// instead of spawning the real CLI. The canned line carries a start position and no end
    /// position, which is exactly the diagnostic shape <see cref="BuildService"/> routes into its
    /// Roslyn span-enrichment path — the converted compilation-cache site.
    /// </summary>
    private sealed class CannedBuildOutputExecutor(string stdOut) : IGatedCommandExecutor
    {
        public Task<CommandExecutionDto> ExecuteAsync(
            string workspaceId,
            string targetPath,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken ct) =>
            Task.FromResult(new CommandExecutionDto(
                Command: "dotnet",
                Arguments: arguments,
                WorkingDirectory: Path.GetDirectoryName(targetPath) ?? ".",
                TargetPath: targetPath,
                ExitCode: 0,
                Succeeded: true,
                DurationMs: 1,
                StdOut: stdOut,
                StdErr: string.Empty));

        public ProjectStatusDto ResolveProject(string workspaceId, string projectName) =>
            throw new NotSupportedException();

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// Minimal call-counting <see cref="ICompilationCache"/> for the hoist tests — increments a
    /// counter per <c>GetCompilationAsync</c> and returns the project's real compilation. The
    /// analyzer/invalidate members are unused by the two hoisted helpers and throw if reached.
    /// </summary>
    private sealed class CountingAdhocCompilationCache : ICompilationCache
    {
        private int _count;

        public int CallCount => Volatile.Read(ref _count);

        public async Task<Compilation?> GetCompilationAsync(string workspaceId, Project project, CancellationToken ct)
        {
            Interlocked.Increment(ref _count);
            return await project.GetCompilationAsync(ct).ConfigureAwait(false);
        }

        public Task<CompilationWithAnalyzers?> GetCompilationWithAnalyzersAsync(string workspaceId, Project project, CancellationToken ct) =>
            throw new NotSupportedException();

        public void Invalidate(string workspaceId) => throw new NotSupportedException();
    }

    /// <summary>
    /// <see cref="ICompilationCache"/> decorator that records how often <c>GetCompilationAsync</c>
    /// was invoked and which <see cref="Compilation"/> instance was returned per project, while
    /// delegating to a real <see cref="CompilationCache"/> so the version-keyed sharing contract
    /// is exercised end to end.
    /// </summary>
    private sealed class RecordingCompilationCache : ICompilationCache
    {
        private readonly ICompilationCache _inner;
        private readonly ConcurrentDictionary<ProjectId, Compilation> _returned = new();
        private int _getCompilationCallCount;

        public RecordingCompilationCache(ICompilationCache inner) => _inner = inner;

        public int GetCompilationCallCount => Volatile.Read(ref _getCompilationCallCount);

        public IReadOnlyDictionary<ProjectId, Compilation> SnapshotReturned() =>
            new Dictionary<ProjectId, Compilation>(_returned);

        public async Task<Compilation?> GetCompilationAsync(string workspaceId, Project project, CancellationToken ct)
        {
            Interlocked.Increment(ref _getCompilationCallCount);
            var compilation = await _inner.GetCompilationAsync(workspaceId, project, ct).ConfigureAwait(false);
            if (compilation is not null)
            {
                _returned[project.Id] = compilation;
            }

            return compilation;
        }

        public Task<CompilationWithAnalyzers?> GetCompilationWithAnalyzersAsync(string workspaceId, Project project, CancellationToken ct) =>
            _inner.GetCompilationWithAnalyzersAsync(workspaceId, project, ct);

        public void Invalidate(string workspaceId) => _inner.Invalidate(workspaceId);
    }
}
