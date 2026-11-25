using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

/// <summary>
/// Covers DI registration discovery and override chains using real framework references.
/// </summary>
[TestClass]
public sealed class DiLifetimeOverrideTests : IsolatedWorkspaceTestBase
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
    public async Task ShowLifetimeOverrides_Off_Default_Result_Matches_Legacy_Shape()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        await WriteServiceCollectionFixtureAsync(workspace, CancellationToken.None);
        await WriteRegistrationFileAsync(
            workspace,
            "RegistrationsAlpha.cs",
            "namespace SampleLib;\n\npublic static class RegistrationsAlpha\n{\n    public static void Configure(IServiceCollection services)\n    {\n        services.AddSingleton<IFoo, FooSingleton>();\n        services.AddScoped<IFoo, FooScoped>();\n    }\n}\n",
            CancellationToken.None);
        await workspace.LoadAsync(CancellationToken.None);

        var legacyResults = await DiRegistrationService.GetDiRegistrationsAsync(
            workspace.WorkspaceId, projectFilter: "SampleLib", CancellationToken.None);

        var iFooEntries = legacyResults.Where(r => r.ServiceType.EndsWith("IFoo", StringComparison.Ordinal)).ToList();
        Assert.AreEqual(2, iFooEntries.Count, "Legacy view must surface both Add* registrations.");
        Assert.IsTrue(
            iFooEntries.All(r => r.RegistrationMethod is "AddSingleton" or "AddScoped"),
            "Legacy view must NOT include TryAdd* methods (they are filtered out to keep the default shape stable).");
    }

    [TestMethod]
    public async Task Last_Wins_Add_Then_Add_Marks_Earlier_Singleton_As_Overridden_With_Scoped_Winner()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        await WriteServiceCollectionFixtureAsync(workspace, CancellationToken.None);

        // Two separate composition-root files — first registers IFoo as Singleton, second as Scoped.
        // Validation per plan: winning lifetime = Scoped, overridden = [Singleton], deadCount = 1.
        await WriteRegistrationFileAsync(
            workspace,
            "RegistrationsAlpha.cs",
            "namespace SampleLib;\n\npublic static class RegistrationsAlpha\n{\n    public static void Configure(IServiceCollection services)\n    {\n        services.AddSingleton<IFoo, FooSingleton>();\n    }\n}\n",
            CancellationToken.None);
        await WriteRegistrationFileAsync(
            workspace,
            "RegistrationsBeta.cs",
            "namespace SampleLib;\n\npublic static class RegistrationsBeta\n{\n    public static void Configure(IServiceCollection services)\n    {\n        services.AddScoped<IFoo, FooScoped>();\n    }\n}\n",
            CancellationToken.None);
        await workspace.LoadAsync(CancellationToken.None);

        var scan = await DiRegistrationService.GetDiRegistrationsWithOverridesAsync(
            workspace.WorkspaceId, projectFilter: "SampleLib", CancellationToken.None);

        var chain = scan.OverrideChains.SingleOrDefault(c => c.ServiceType.EndsWith("IFoo", StringComparison.Ordinal));
        Assert.IsNotNull(chain, "Expected an override chain for IFoo.");
        Assert.AreEqual("Scoped", chain.WinningLifetime,
            "Last Add* call wins per MS.DI descriptor resolution semantics.");
        Assert.AreEqual("FooScoped", ImplementationLeaf(chain.WinningImplementationType));
        Assert.IsTrue(chain.LifetimesDiffer, "Mixed Singleton + Scoped registrations must flag lifetime mismatch.");
        Assert.AreEqual(1, chain.DeadRegistrationCount, "Earlier Singleton registration is dead.");
        Assert.AreEqual(2, chain.Registrations.Count);

        var singletonEntry = chain.Registrations.Single(e => e.Lifetime == "Singleton");
        Assert.AreEqual("overridden", singletonEntry.EffectiveStatus,
            "Earlier Singleton must be marked overridden (last-wins semantics).");

        var scopedEntry = chain.Registrations.Single(e => e.Lifetime == "Scoped");
        Assert.AreEqual("winning", scopedEntry.EffectiveStatus,
            "Final Scoped registration is the winner.");
    }

    [TestMethod]
    public async Task TryAdd_First_Wins_Subsequent_TryAdd_Is_Shadowed()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        await WriteServiceCollectionFixtureAsync(workspace, CancellationToken.None);

        // First TryAdd takes effect; second TryAdd is a no-op because a descriptor exists.
        await WriteRegistrationFileAsync(
            workspace,
            "RegistrationsAlpha.cs",
            "namespace SampleLib;\n\npublic static class RegistrationsAlpha\n{\n    public static void Configure(IServiceCollection services)\n    {\n        services.TryAddSingleton<IBar, BarFirst>();\n        services.TryAddSingleton<IBar, BarSecond>();\n    }\n}\n",
            CancellationToken.None);
        await workspace.LoadAsync(CancellationToken.None);

        var scan = await DiRegistrationService.GetDiRegistrationsWithOverridesAsync(
            workspace.WorkspaceId, projectFilter: "SampleLib", CancellationToken.None);

        var chain = scan.OverrideChains.SingleOrDefault(c => c.ServiceType.EndsWith("IBar", StringComparison.Ordinal));
        Assert.IsNotNull(chain, "Expected an override chain for IBar.");
        Assert.AreEqual("BarFirst", ImplementationLeaf(chain.WinningImplementationType),
            "First TryAddSingleton wins; subsequent TryAdds are no-ops.");
        Assert.AreEqual("Singleton", chain.WinningLifetime);
        Assert.IsFalse(chain.LifetimesDiffer, "Both registrations are Singleton — no mismatch.");
        Assert.AreEqual(1, chain.DeadRegistrationCount, "Second TryAdd contributes nothing.");
        Assert.AreEqual(2, chain.Registrations.Count);

        var statuses = chain.Registrations.Select(e => e.EffectiveStatus).ToList();
        CollectionAssert.AreEqual(new[] { "winning", "shadowed" }, statuses,
            "Source-order: first TryAdd is the winner; second is shadowed.");
    }

    [TestMethod]
    public async Task Single_Registration_Service_Type_Is_Excluded_From_Override_Chains()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        await WriteServiceCollectionFixtureAsync(workspace, CancellationToken.None);
        await WriteRegistrationFileAsync(
            workspace,
            "RegistrationsAlpha.cs",
            "namespace SampleLib;\n\npublic static class RegistrationsAlpha\n{\n    public static void Configure(IServiceCollection services)\n    {\n        services.AddSingleton<IBaz, BazOnly>();\n    }\n}\n",
            CancellationToken.None);
        await workspace.LoadAsync(CancellationToken.None);

        var scan = await DiRegistrationService.GetDiRegistrationsWithOverridesAsync(
            workspace.WorkspaceId, projectFilter: "SampleLib", CancellationToken.None);

        Assert.IsFalse(
            scan.OverrideChains.Any(c => c.ServiceType.EndsWith("IBaz", StringComparison.Ordinal)),
            "A service registered exactly once is not an override and must be omitted from the chain output.");
    }

    /// <summary>
    /// get-di-registrations-multi-registration-overcounting Bug 1 (a): when a service type is
    /// consumed via <c>IEnumerable&lt;T&gt;</c> ctor injection, multi-registration is
    /// intentional — <c>GetServices&lt;T&gt;()</c> returns all entries. The override-chain
    /// emission must suppress the chain for that service type so the dead-registration count
    /// is not inflated.
    /// </summary>
    [TestMethod]
    public async Task IEnumerable_T_Consumer_Suppresses_Override_Chain_For_Service_Type()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        await WriteServiceCollectionFixtureAsync(workspace, CancellationToken.None);

        // Three Add* registrations of IAnalyzer — all intentional because
        // AnalyzerEnumerableConsumer consumes IEnumerable<IAnalyzer>.
        await WriteRegistrationFileAsync(
            workspace,
            "RegistrationsAlpha.cs",
            "namespace SampleLib;\n\npublic static class RegistrationsAlpha\n{\n    public static void Configure(IServiceCollection services)\n    {\n        services.AddSingleton<IAnalyzer, AnalyzerA>();\n        services.AddSingleton<IAnalyzer, AnalyzerB>();\n        services.AddSingleton<IAnalyzer, AnalyzerC>();\n    }\n}\n",
            CancellationToken.None);
        await workspace.LoadAsync(CancellationToken.None);

        var scan = await DiRegistrationService.GetDiRegistrationsWithOverridesAsync(
            workspace.WorkspaceId, projectFilter: "SampleLib", CancellationToken.None);

        Assert.IsFalse(
            scan.OverrideChains.Any(c => c.ServiceType.EndsWith("IAnalyzer", StringComparison.Ordinal)),
            "Service types consumed via IEnumerable<T> must be excluded from the override-chain output.");
    }

    /// <summary>
    /// get-di-registrations-multi-registration-overcounting Bug 1 (b): <c>IReadOnlyList&lt;T&gt;</c>
    /// and <c>T[]</c> consumers must also suppress the override chain. MS.DI resolves both
    /// shapes from the same descriptor list as <c>IEnumerable&lt;T&gt;</c>.
    /// </summary>
    [TestMethod]
    public async Task IReadOnlyList_And_Array_Consumers_Suppress_Override_Chains()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        await WriteServiceCollectionFixtureAsync(workspace, CancellationToken.None);

        await WriteRegistrationFileAsync(
            workspace,
            "RegistrationsAlpha.cs",
            "namespace SampleLib;\n\npublic static class RegistrationsAlpha\n{\n    public static void Configure(IServiceCollection services)\n    {\n        services.AddSingleton<IValidator, ValidatorA>();\n        services.AddSingleton<IValidator, ValidatorB>();\n        services.AddSingleton<IRule, RuleA>();\n        services.AddSingleton<IRule, RuleB>();\n    }\n}\n",
            CancellationToken.None);
        await workspace.LoadAsync(CancellationToken.None);

        var scan = await DiRegistrationService.GetDiRegistrationsWithOverridesAsync(
            workspace.WorkspaceId, projectFilter: "SampleLib", CancellationToken.None);

        Assert.IsFalse(
            scan.OverrideChains.Any(c => c.ServiceType.EndsWith("IValidator", StringComparison.Ordinal)),
            "Service types consumed via IReadOnlyList<T> must be excluded from the override-chain output.");
        Assert.IsFalse(
            scan.OverrideChains.Any(c => c.ServiceType.EndsWith("IRule", StringComparison.Ordinal)),
            "Service types consumed via T[] must be excluded from the override-chain output.");
    }

    /// <summary>
    /// get-di-registrations-multi-registration-overcounting Bug 1 (negative): a service type
    /// that has NO IEnumerable/IReadOnlyList/array consumer must still produce the override
    /// chain. This guards against the suppression being applied too aggressively.
    /// </summary>
    [TestMethod]
    public async Task Multi_Registration_Without_Enumerable_Consumer_Still_Reports_Override_Chain()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        await WriteServiceCollectionFixtureAsync(workspace, CancellationToken.None);

        // Two Add* registrations of IFoo, but NO IEnumerable<IFoo>/IReadOnlyList<IFoo>/IFoo[]
        // ctor or property exists in the fixture or in the registration file. The chain must
        // still surface so the consumer sees the second registration overriding the first.
        await WriteRegistrationFileAsync(
            workspace,
            "RegistrationsAlpha.cs",
            "namespace SampleLib;\n\npublic static class RegistrationsAlpha\n{\n    public static void Configure(IServiceCollection services)\n    {\n        services.AddSingleton<IFoo, FooSingleton>();\n        services.AddSingleton<IFoo, FooScoped>();\n    }\n}\n",
            CancellationToken.None);
        await workspace.LoadAsync(CancellationToken.None);

        var scan = await DiRegistrationService.GetDiRegistrationsWithOverridesAsync(
            workspace.WorkspaceId, projectFilter: "SampleLib", CancellationToken.None);

        var chain = scan.OverrideChains.SingleOrDefault(c => c.ServiceType.EndsWith("IFoo", StringComparison.Ordinal));
        Assert.IsNotNull(chain,
            "Multi-registration without an IEnumerable consumer must still report an override chain.");
        Assert.AreEqual(1, chain.DeadRegistrationCount,
            "Earlier registration is dead when no IEnumerable consumer exists.");
    }

    /// <summary>
    /// get-di-registrations-multi-registration-overcounting Bug 2 (a): factory lambda whose
    /// body forwards to <c>GetRequiredService&lt;T&gt;()</c> resolves T as the winning
    /// implementation type, not the opaque "factory" sentinel.
    /// </summary>
    [TestMethod]
    public async Task Factory_Lambda_With_GetRequiredService_Resolves_Implementation_Type()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        await WriteServiceCollectionFixtureAsync(workspace, CancellationToken.None);

        // First registration: concrete FileSnapshotReader. Second registration: factory lambda
        // forwarding ISnapshotReader to FileSnapshotReader via sp.GetRequiredService.
        await WriteRegistrationFileAsync(
            workspace,
            "RegistrationsAlpha.cs",
            "namespace SampleLib;\n\npublic static class RegistrationsAlpha\n{\n    public static void Configure(IServiceCollection services)\n    {\n        services.AddSingleton<FileSnapshotReader, FileSnapshotReader>();\n        services.AddSingleton<ISnapshotReader>(sp => sp.GetRequiredService<FileSnapshotReader>());\n    }\n}\n",
            CancellationToken.None);
        await workspace.LoadAsync(CancellationToken.None);

        var legacy = await DiRegistrationService.GetDiRegistrationsAsync(
            workspace.WorkspaceId, projectFilter: "SampleLib", CancellationToken.None);

        var iSnapshotEntry = legacy.SingleOrDefault(r => r.ServiceType.EndsWith("ISnapshotReader", StringComparison.Ordinal));
        Assert.IsNotNull(iSnapshotEntry, "Expected a registration entry for ISnapshotReader.");
        Assert.AreEqual("FileSnapshotReader", ImplementationLeaf(iSnapshotEntry.ImplementationType),
            "Lambda body GetRequiredService<FileSnapshotReader>() must surface FileSnapshotReader as the impl type.");
    }

    /// <summary>
    /// get-di-registrations-multi-registration-overcounting Bug 2 (b): factory lambda using
    /// <c>GetService&lt;T&gt;</c> (nullable variant) also resolves T.
    /// </summary>
    [TestMethod]
    public async Task Factory_Lambda_With_GetService_Resolves_Implementation_Type()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        await WriteServiceCollectionFixtureAsync(workspace, CancellationToken.None);

        await WriteRegistrationFileAsync(
            workspace,
            "RegistrationsAlpha.cs",
            "namespace SampleLib;\n\npublic static class RegistrationsAlpha\n{\n    public static void Configure(IServiceCollection services)\n    {\n        services.AddSingleton<FileSnapshotReader, FileSnapshotReader>();\n        services.AddSingleton<ISnapshotReader>(sp => sp.GetService<FileSnapshotReader>()!);\n    }\n}\n",
            CancellationToken.None);
        await workspace.LoadAsync(CancellationToken.None);

        var legacy = await DiRegistrationService.GetDiRegistrationsAsync(
            workspace.WorkspaceId, projectFilter: "SampleLib", CancellationToken.None);

        var iSnapshotEntry = legacy.SingleOrDefault(r => r.ServiceType.EndsWith("ISnapshotReader", StringComparison.Ordinal));
        Assert.IsNotNull(iSnapshotEntry, "Expected a registration entry for ISnapshotReader.");
        Assert.AreEqual("FileSnapshotReader", ImplementationLeaf(iSnapshotEntry.ImplementationType),
            "Lambda body GetService<FileSnapshotReader>() must surface FileSnapshotReader as the impl type.");
    }

    /// <summary>
    /// di-registrations-factory-lambda-impl-misattributed: a factory lambda that CONSTRUCTS the
    /// implementation reports the constructed type — a <c>GetRequiredService&lt;T&gt;()</c>
    /// call used as a constructor argument is a dependency, not the implementation.
    /// </summary>
    [TestMethod]
    public async Task Factory_Lambda_Constructing_Impl_From_Resolved_Dependency_Reports_Constructed_Type()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        await WriteServiceCollectionFixtureAsync(workspace, CancellationToken.None);

        await WriteRegistrationFileAsync(
            workspace,
            "RegistrationsAlpha.cs",
            "namespace SampleLib;\n\npublic static class RegistrationsAlpha\n{\n    public static void Configure(IServiceCollection services)\n    {\n        services.AddSingleton<CompositeImpl, CompositeImpl>();\n        services.AddSingleton<ISnapshotReader, FileSnapshotReader>();\n        services.AddSingleton<IComposite>(sp => new CompositeWrapper(sp.GetRequiredService<CompositeImpl>()));\n    }\n}\n",
            CancellationToken.None);
        await workspace.LoadAsync(CancellationToken.None);

        var legacy = await DiRegistrationService.GetDiRegistrationsAsync(
            workspace.WorkspaceId, projectFilter: "SampleLib", CancellationToken.None);

        var iCompositeEntry = legacy.SingleOrDefault(r => r.ServiceType.EndsWith("IComposite", StringComparison.Ordinal));
        Assert.IsNotNull(iCompositeEntry, "Expected a registration entry for IComposite.");
        Assert.AreEqual("CompositeWrapper", ImplementationLeaf(iCompositeEntry.ImplementationType),
            "The constructed CompositeWrapper is the implementation; the GetRequiredService<CompositeImpl>() ctor argument is only a dependency.");
    }

    /// <summary>
    /// di-registrations-factory-lambda-impl-misattributed: a dependency-free construction
    /// (<c>sp =&gt; new OpaqueImpl()</c>) reports the constructed type instead of the opaque
    /// <c>"factory"</c> sentinel.
    /// </summary>
    [TestMethod]
    public async Task Factory_Lambda_Constructing_Impl_Without_Dependencies_Reports_Constructed_Type()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        await WriteServiceCollectionFixtureAsync(workspace, CancellationToken.None);

        await WriteRegistrationFileAsync(
            workspace,
            "RegistrationsAlpha.cs",
            "namespace SampleLib;\n\npublic static class RegistrationsAlpha\n{\n    public static void Configure(IServiceCollection services)\n    {\n        services.AddSingleton<IOpaque>(sp => new OpaqueImpl());\n    }\n}\n",
            CancellationToken.None);
        await workspace.LoadAsync(CancellationToken.None);

        var legacy = await DiRegistrationService.GetDiRegistrationsAsync(
            workspace.WorkspaceId, projectFilter: "SampleLib", CancellationToken.None);

        var iOpaqueEntry = legacy.SingleOrDefault(r => r.ServiceType.EndsWith("IOpaque", StringComparison.Ordinal));
        Assert.IsNotNull(iOpaqueEntry, "Expected a registration entry for IOpaque.");
        Assert.AreEqual("OpaqueImpl", ImplementationLeaf(iOpaqueEntry.ImplementationType),
            "A lambda returning new OpaqueImpl() must surface OpaqueImpl as the impl type.");
    }

    /// <summary>
    /// di-registrations-factory-lambda-impl-misattributed: a block-body lambda resolves the
    /// type of its <c>return</c> expression; a service-locator call assigned to a local is a
    /// dependency and never the implementation.
    /// </summary>
    [TestMethod]
    public async Task Factory_Lambda_Block_Body_Reports_Returned_Construction_Type()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        await WriteServiceCollectionFixtureAsync(workspace, CancellationToken.None);

        await WriteRegistrationFileAsync(
            workspace,
            "RegistrationsAlpha.cs",
            "namespace SampleLib;\n\npublic static class RegistrationsAlpha\n{\n    public static void Configure(IServiceCollection services)\n    {\n        services.AddSingleton<IComposite>(sp =>\n        {\n            var inner = sp.GetRequiredService<CompositeImpl>();\n            return new CompositeWrapper(inner);\n        });\n    }\n}\n",
            CancellationToken.None);
        await workspace.LoadAsync(CancellationToken.None);

        var legacy = await DiRegistrationService.GetDiRegistrationsAsync(
            workspace.WorkspaceId, projectFilter: "SampleLib", CancellationToken.None);

        var iCompositeEntry = legacy.SingleOrDefault(r => r.ServiceType.EndsWith("IComposite", StringComparison.Ordinal));
        Assert.IsNotNull(iCompositeEntry, "Expected a registration entry for IComposite.");
        Assert.AreEqual("CompositeWrapper", ImplementationLeaf(iCompositeEntry.ImplementationType),
            "Block-body lambdas resolve the returned construction, not the locally-resolved dependency.");
    }

    /// <summary>
    /// A factory lambda whose returned expression is neither a construction nor a direct
    /// service-locator forward (helper call, ambiguous conditional) falls back to the
    /// <c>"factory"</c> sentinel instead of guessing from a nested resolution.
    /// </summary>
    [TestMethod]
    public async Task Factory_Lambda_Returning_Unrecognized_Shape_Falls_Back_To_Factory_String()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        await WriteServiceCollectionFixtureAsync(workspace, CancellationToken.None);

        await WriteRegistrationFileAsync(
            workspace,
            "RegistrationsAlpha.cs",
            "namespace SampleLib;\n\npublic static class RegistrationsAlpha\n{\n    public static void Configure(IServiceCollection services, bool wrap)\n    {\n        services.AddSingleton<IOpaque>(sp => OpaqueFactory.Create(sp.GetRequiredService<CompositeImpl>()));\n        services.AddSingleton<IComposite>(sp => wrap ? new CompositeWrapper(sp.GetRequiredService<CompositeImpl>()) : new CompositeImpl());\n    }\n}\n",
            CancellationToken.None);
        await workspace.LoadAsync(CancellationToken.None);

        var legacy = await DiRegistrationService.GetDiRegistrationsAsync(
            workspace.WorkspaceId, projectFilter: "SampleLib", CancellationToken.None);

        var iOpaqueEntry = legacy.SingleOrDefault(r => r.ServiceType.EndsWith("IOpaque", StringComparison.Ordinal));
        Assert.IsNotNull(iOpaqueEntry, "Expected a registration entry for IOpaque.");
        Assert.AreEqual("factory", iOpaqueEntry.ImplementationType,
            "A helper-call return must fall back to \"factory\", not the nested GetRequiredService<CompositeImpl>() dependency.");

        var iCompositeEntry = legacy.SingleOrDefault(r => r.ServiceType.EndsWith("IComposite", StringComparison.Ordinal));
        Assert.IsNotNull(iCompositeEntry, "Expected a registration entry for IComposite.");
        Assert.AreEqual("factory", iCompositeEntry.ImplementationType,
            "An ambiguous conditional return must fall back to \"factory\".");
    }

    [TestMethod]
    public async Task Summary_Mode_Returns_Compact_Counts_And_Paged_Override_Chains()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        await WriteServiceCollectionFixtureAsync(workspace, CancellationToken.None);
        await WriteRegistrationFileAsync(
            workspace,
            "RegistrationsAlpha.cs",
            "namespace SampleLib;\n\npublic static class RegistrationsAlpha\n{\n    public static void Configure(IServiceCollection services)\n    {\n        services.AddSingleton<IFoo, FooSingleton>();\n        services.AddScoped<IFoo, FooScoped>();\n        services.TryAddSingleton<IBar, BarFirst>();\n        services.TryAddSingleton<IBar, BarSecond>();\n    }\n}\n",
            CancellationToken.None);
        await workspace.LoadAsync(CancellationToken.None);

        var json = await AdvancedAnalysisTools.GetDiRegistrations(
            WorkspaceExecutionGate,
            DiRegistrationService,
            workspace.WorkspaceId,
            projectName: "SampleLib",
            showLifetimeOverrides: true,
            summary: true,
            offset: 0,
            limit: 1,
            ct: CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.AreEqual(2, root.GetProperty("count").GetInt32(),
            "count preserves the detailed legacy registration count and excludes TryAdd* entries.");
        Assert.AreEqual(2, root.GetProperty("overrideChainCount").GetInt32());
        Assert.AreEqual(1, root.GetProperty("limit").GetInt32());
        Assert.IsTrue(root.GetProperty("hasMore").GetBoolean(),
            "limit=1 should surface hasMore when more override-chain summaries exist.");
        Assert.IsTrue(root.GetProperty("isComplete").GetBoolean());
        Assert.AreEqual(0, root.GetProperty("failedDocumentCount").GetInt32());
        Assert.AreEqual("complete", root.GetProperty("totalCountMeaning").GetString());
        Assert.IsTrue(root.TryGetProperty("overrideChains", out var overrideChains));
        Assert.AreEqual(1, overrideChains.GetArrayLength());
        Assert.IsFalse(root.TryGetProperty("registrations", out _),
            "summary=true should not include the full registrations list.");
    }

    /// <summary>
    /// gh #771: the detailed default response previously serialized the entire registrations
    /// list with no cap, producing an 86 KB body on a 141-registration DI graph that exceeded
    /// the MCP inline transport cap. The fix paginates the detailed response via offset/limit
    /// (default limit=100) and emits totalCount/hasMore/offset/limit alongside the paged
    /// registrations slice. This test writes 101 distinct service-type registrations and
    /// verifies the default call returns a bounded first page with hasMore=true.
    /// </summary>
    [TestMethod]
    public async Task Default_Detailed_Response_Pages_Registrations_With_Hundred_Item_Default_Limit()
    {
        const int TotalRegistrations = 101;

        await using var workspace = CreateIsolatedWorkspaceCopy();
        await WriteServiceCollectionFixtureAsync(workspace, CancellationToken.None);
        await WriteManyRegistrationsAsync(workspace, TotalRegistrations, CancellationToken.None);
        await workspace.LoadAsync(CancellationToken.None);

        // showLifetimeOverrides=false, summary=false → exercises the detailed default path
        // at AdvancedAnalysisTools.cs:79 with default offset=0, limit=100.
        var jsonDefault = await AdvancedAnalysisTools.GetDiRegistrations(
            WorkspaceExecutionGate,
            DiRegistrationService,
            workspace.WorkspaceId,
            projectName: "SampleLib",
            showLifetimeOverrides: false,
            summary: false,
            ct: CancellationToken.None);

        using var documentDefault = JsonDocument.Parse(jsonDefault);
        var rootDefault = documentDefault.RootElement;
        Assert.AreEqual(100, rootDefault.GetProperty("count").GetInt32(),
            "Default limit=100 must bound the registrations slice.");
        Assert.AreEqual(TotalRegistrations, rootDefault.GetProperty("totalCount").GetInt32(),
            "totalCount must reflect the full registration count across the queried scope.");
        Assert.AreEqual(0, rootDefault.GetProperty("offset").GetInt32());
        Assert.AreEqual(100, rootDefault.GetProperty("limit").GetInt32());
        Assert.IsTrue(rootDefault.GetProperty("hasMore").GetBoolean(),
            "With 101 registrations and default limit=100, hasMore must be true.");
        Assert.IsTrue(rootDefault.GetProperty("isComplete").GetBoolean());
        Assert.AreEqual(0, rootDefault.GetProperty("failedDocumentCount").GetInt32());
        Assert.IsTrue(rootDefault.TryGetProperty("registrations", out var registrationsDefault));
        Assert.AreEqual(100, registrationsDefault.GetArrayLength(),
            "The paged registrations array must contain exactly limit entries.");

        // Also verify the showLifetimeOverrides=true detailed path applies the same pagination
        // to the registrations list (separate serialize site at AdvancedAnalysisTools.cs:92-98).
        // Note: the override-chains output remains unpaged in this mode; pagination only applies
        // to the registrations list. With 101 single-registration service types we expect zero
        // override chains (services need >= 2 registrations to qualify).
        var jsonOverrides = await AdvancedAnalysisTools.GetDiRegistrations(
            WorkspaceExecutionGate,
            DiRegistrationService,
            workspace.WorkspaceId,
            projectName: "SampleLib",
            showLifetimeOverrides: true,
            summary: false,
            ct: CancellationToken.None);

        using var documentOverrides = JsonDocument.Parse(jsonOverrides);
        var rootOverrides = documentOverrides.RootElement;
        Assert.AreEqual(100, rootOverrides.GetProperty("count").GetInt32(),
            "Default limit=100 must bound the registrations slice on the overrides path too.");
        Assert.AreEqual(TotalRegistrations, rootOverrides.GetProperty("totalCount").GetInt32());
        Assert.IsTrue(rootOverrides.GetProperty("hasMore").GetBoolean(),
            "hasMore must also be true on the overrides path with > limit registrations.");
        Assert.IsTrue(rootOverrides.GetProperty("isComplete").GetBoolean());
        Assert.AreEqual("complete", rootOverrides.GetProperty("totalCountMeaning").GetString());
        Assert.IsTrue(rootOverrides.TryGetProperty("registrations", out var registrationsOverrides));
        Assert.AreEqual(100, registrationsOverrides.GetArrayLength());
        Assert.IsTrue(rootOverrides.TryGetProperty("overrideChainCount", out _),
            "overrideChainCount must still be emitted on the overrides path.");
        Assert.IsTrue(rootOverrides.TryGetProperty("overrideChains", out _),
            "overrideChains must still be emitted on the overrides path.");
    }

    /// <summary>
    /// Materialises <paramref name="count"/> distinct service-type registrations into the
    /// workspace by emitting one file with <paramref name="count"/> interfaces, implementations,
    /// and AddSingleton&lt;,&gt; calls — sufficient for exercising paging on the detailed
    /// response shape without interacting with the shared service types used by the other tests.
    /// </summary>
    private static async Task WriteManyRegistrationsAsync(IsolatedWorkspaceScope workspace, int count, CancellationToken ct)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("namespace SampleLib;");
        sb.AppendLine();
        for (var i = 0; i < count; i++)
        {
            sb.Append("public interface IService").Append(i).AppendLine(" { }");
            sb.Append("public sealed class Service").Append(i).Append("Impl : IService").Append(i).AppendLine(" { }");
        }
        sb.AppendLine();
        sb.AppendLine("public static class ManyRegistrations");
        sb.AppendLine("{");
        sb.AppendLine("    public static void Configure(IServiceCollection services)");
        sb.AppendLine("    {");
        for (var i = 0; i < count; i++)
        {
            sb.Append("        services.AddSingleton<IService").Append(i)
              .Append(", Service").Append(i).AppendLine("Impl>();");
        }
        sb.AppendLine("    }");
        sb.AppendLine("}");

        await File.WriteAllTextAsync(
            workspace.GetPath("SampleLib", "ManyRegistrations.cs"),
            sb.ToString(),
            ct).ConfigureAwait(false);
    }

    private static async Task WriteServiceCollectionFixtureAsync(IsolatedWorkspaceScope workspace, CancellationToken ct)
    {
        AddDependencyInjectionReferences(workspace, "SampleLib");
        var fixture = """
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.DependencyInjection.Extensions;
namespace SampleLib;

public interface IFoo { }
public sealed class FooSingleton : IFoo { }
public sealed class FooScoped : IFoo { }

public interface IBar { }
public sealed class BarFirst : IBar { }
public sealed class BarSecond : IBar { }

public interface IBaz { }
public sealed class BazOnly : IBaz { }

// get-di-registrations-multi-registration-overcounting: types and consumers used by the
// IEnumerable<T> / IReadOnlyList<T> / T[] suppression tests.
public interface IAnalyzer { }
public sealed class AnalyzerA : IAnalyzer { }
public sealed class AnalyzerB : IAnalyzer { }
public sealed class AnalyzerC : IAnalyzer { }

public interface IValidator { }
public sealed class ValidatorA : IValidator { }
public sealed class ValidatorB : IValidator { }

public interface IRule { }
public sealed class RuleA : IRule { }
public sealed class RuleB : IRule { }

public sealed class AnalyzerEnumerableConsumer
{
    public AnalyzerEnumerableConsumer(System.Collections.Generic.IEnumerable<IAnalyzer> analyzers) { }
}

public sealed class ValidatorListConsumer
{
    public ValidatorListConsumer(System.Collections.Generic.IReadOnlyList<IValidator> validators) { }
}

public sealed class RuleArrayConsumer
{
    public RuleArrayConsumer(IRule[] rules) { }
}

// get-di-registrations-multi-registration-overcounting: lambda-resolution test types.
public interface ISnapshotReader { }
public sealed class FileSnapshotReader : ISnapshotReader { }

public interface IComposite { }
public sealed class CompositeImpl : IComposite { }
public sealed class CompositeWrapper : IComposite
{
    public CompositeWrapper(CompositeImpl inner) { }
}

public interface IOpaque { }
public sealed class OpaqueImpl : IOpaque { }

public static class OpaqueFactory
{
    public static IOpaque Create(CompositeImpl dependency) => new OpaqueImpl();
}
""";
        await File.WriteAllTextAsync(workspace.GetPath("SampleLib", "DiServices.cs"), fixture, ct).ConfigureAwait(false);
    }

    private static async Task WriteRegistrationFileAsync(IsolatedWorkspaceScope workspace, string fileName, string contents, CancellationToken ct)
    {
        await File.WriteAllTextAsync(workspace.GetPath("SampleLib", fileName), contents, ct).ConfigureAwait(false);
    }


    [TestMethod]
    public async Task FrameworkIdentity_Rejects_Lookalikes_Without_Changing_Public_Pages_Or_Chains()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddDependencyInjectionReferences(workspace, "SampleLib");
        await File.WriteAllTextAsync(workspace.GetPath("SampleLib", "Identity.cs"), FrameworkIdentitySource);
        await workspace.LoadAsync(CancellationToken.None);

        foreach (var overrides in new[] { false, true })
        {
            foreach (var summary in new[] { false, true })
            {
                var json = await AdvancedAnalysisTools.GetDiRegistrations(
                    WorkspaceExecutionGate, DiRegistrationService, workspace.WorkspaceId,
                    projectName: "SampleLib", showLifetimeOverrides: overrides, summary: summary, limit: 500);
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                Assert.AreEqual(4,
                    root.GetProperty(summary ? "count" : "totalCount").GetInt32(),
                    "Unrelated lookalike registrations must not inflate the public inventory.");
                Assert.IsTrue(root.GetProperty("isComplete").GetBoolean());
                Assert.AreEqual("complete", root.GetProperty("totalCountMeaning").GetString());
                if (overrides)
                {
                    Assert.AreEqual(1, root.GetProperty("overrideChainCount").GetInt32());
                    var chain = root.GetProperty("overrideChains")[0];
                    Assert.AreEqual("Singleton", chain.GetProperty("winningLifetime").GetString());
                }
            }
            var pages = new List<string>();
            for (var offset = 0; offset < 2; offset++)
            {
                using var page = JsonDocument.Parse(await AdvancedAnalysisTools.GetDiRegistrations(
                    WorkspaceExecutionGate, DiRegistrationService, workspace.WorkspaceId,
                    projectName: "SampleLib", showLifetimeOverrides: overrides, offset: offset, limit: 1));
                Assert.AreEqual(1, page.RootElement.GetProperty("count").GetInt32());
                Assert.IsTrue(page.RootElement.GetProperty("hasMore").GetBoolean());
                pages.Add(page.RootElement.GetProperty("registrations")[0].GetRawText());
            }
            Assert.AreNotEqual(pages[0], pages[1]);
        }
    }

    [TestMethod]
    public async Task FrameworkIdentity_Preserves_Framework_Custom_And_Static_Forms()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddDependencyInjectionReferences(workspace, "SampleLib");
        await File.WriteAllTextAsync(workspace.GetPath("SampleLib", "Forms.cs"), FrameworkFormsSource);
        await workspace.LoadAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(await AdvancedAnalysisTools.GetDiRegistrations(
            WorkspaceExecutionGate, DiRegistrationService, workspace.WorkspaceId,
            projectName: "SampleLib", showLifetimeOverrides: true, limit: 500));
        var rows = document.RootElement.GetProperty("registrations").EnumerateArray().ToArray();
        var expected = new[]
        {
            ("AddSingleton", "Singleton", "SampleLib.Impl", 10),
            ("AddScoped", "Scoped", "SampleLib.Impl", 6),
            ("AddTransient", "Transient", "SampleLib.Impl", 2),
            ("AddKeyedSingleton", "Singleton", "SampleLib.Impl", 1),
            ("AddKeyedScoped", "Scoped", "SampleLib.Impl", 1),
            ("AddKeyedTransient", "Transient", "SampleLib.Impl", 1),
            ("AddHostedService", "Singleton", "SampleLib.Worker", 1),
        };
        var selfRegistrationLines = FrameworkFormsSource.Split((char)10)
            .Select((text, index) => (text, index))
            .Where(line => line.text.Contains("AddSingleton<Impl>", StringComparison.Ordinal))
            .Select(line => line.index + 1)
            .ToHashSet();
        Assert.AreEqual(2, selfRegistrationLines.Count);
        Assert.AreEqual(expected.Sum(item => item.Item4), rows.Length);
        foreach (var (method, lifetime, implementation, count) in expected)
        {
            var matches = rows.Where(row => row.GetProperty("registrationMethod").GetString() == method).ToArray();
            Assert.AreEqual(count, matches.Length, method);
            foreach (var row in matches)
            {
                Assert.AreEqual(lifetime, row.GetProperty("lifetime").GetString(), method);
                Assert.AreEqual(implementation, row.GetProperty("implementationType").GetString(), method);
                Assert.AreEqual(method == "AddHostedService" ? "SampleLib.Worker" :
                    selfRegistrationLines.Contains(row.GetProperty("line").GetInt32()) ?
                        "SampleLib.Impl" : "SampleLib.IThing",
                    row.GetProperty("serviceType").GetString(), method);
            }
        }
        Assert.AreEqual(2, rows.Count(row => row.GetProperty("serviceType").GetString() == "SampleLib.Impl"));
        using var legacy = JsonDocument.Parse(await AdvancedAnalysisTools.GetDiRegistrations(
            WorkspaceExecutionGate, DiRegistrationService, workspace.WorkspaceId, projectName: "SampleLib", limit: 500));
        Assert.AreEqual(rows.Length, legacy.RootElement.GetProperty("totalCount").GetInt32());
        var chain = document.RootElement.GetProperty("overrideChains").EnumerateArray()
            .Single(item => item.GetProperty("serviceType").GetString() == "SampleLib.IThing");
        var tryAdds = chain.GetProperty("registrations").EnumerateArray()
            .Where(row => row.GetProperty("registrationMethod").GetString()!.StartsWith("TryAdd", StringComparison.Ordinal))
            .ToArray();
        Assert.AreEqual(4, tryAdds.Length);
        foreach (var (method, lifetime) in new[]
        {
            ("TryAddSingleton", "Singleton"),
            ("TryAddScoped", "Scoped"),
            ("TryAddTransient", "Transient"),
            ("TryAddEnumerable", "Enumerable"),
        })
        {
            var row = tryAdds.Single(item => item.GetProperty("registrationMethod").GetString() == method);
            Assert.AreEqual(lifetime, row.GetProperty("lifetime").GetString());
            Assert.AreEqual("SampleLib.Impl", row.GetProperty("implementationType").GetString());
        }
    }

    [TestMethod]
    public async Task FrameworkIdentity_Rejects_Source_Shadow_Of_Framework_Interface()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddDependencyInjectionReferences(workspace, "SampleLib");
        await File.WriteAllTextAsync(workspace.GetPath("SampleLib", "Shadow.cs"), """
            namespace Microsoft.Extensions.DependencyInjection
            {
                public interface IServiceCollection { }
                public static class ServiceCollectionExtensions
                {
                    public static void AddSingleton<T, U>(this IServiceCollection services) { }
                }
                public static class SourceShadow
                {
                    public static void Configure(IServiceCollection services) => services.AddSingleton<object, object>();
                }
            }
            """);
        await workspace.LoadAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(await AdvancedAnalysisTools.GetDiRegistrations(
            WorkspaceExecutionGate, DiRegistrationService, workspace.WorkspaceId, projectName: "SampleLib"));
        Assert.AreEqual(0, document.RootElement.GetProperty("totalCount").GetInt32(),
            "A source declaration with the framework full name must not substitute for metadata identity.");
    }

    private const string FrameworkIdentitySource = """
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Extensions.DependencyInjection.Extensions;
        namespace SampleLib
        {
            public interface IThing { }
            public sealed class Impl : IThing { }
            public static class Configure
            {
                public static void Register(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                {
                    services.AddSingleton<IThing, Impl>();
                    services.AddScoped<IThing, Impl>();
                    services.TryAddSingleton<IThing, Impl>();
                    services.TryAddTransient<IThing, Impl>();
                    Custom.AddTransient<IThing, Impl>(services);
                    services.AddKeyedSingleton<IThing, Impl>("key");
                    Fake.ServiceCollectionExtensions.AddSingleton<IThing, Impl>(new object());
                    new Fake.Derived().AddTransient<IThing, Impl>();
                    new Fake.ServiceCollectionLike().AddScoped<IThing, Impl>();
                }
            }
            public static class Custom
            {
                public static void AddTransient<T, U>(Microsoft.Extensions.DependencyInjection.IServiceCollection collection) { }
            }
        }
        namespace Fake
        {
            public interface IServiceCollection { }
            public sealed class Derived : IServiceCollection
            {
                public void AddTransient<T, U>() { }
            }
            public sealed class ServiceCollectionLike
            {
                public void AddScoped<T, U>() { }
            }
            public static class ServiceCollectionExtensions
            {
                public static void AddSingleton<T, U>(object collection) { }
            }
        }
        """;

    private const string FrameworkFormsSource = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Extensions.DependencyInjection.Extensions;
        using Microsoft.Extensions.Hosting;
        namespace SampleLib;
        public interface IThing { }
        public sealed class Impl : IThing { }
        public sealed class Worker : IHostedService
        {
            public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
            public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
        }
        public sealed class CustomCollection : List<ServiceDescriptor>, IServiceCollection
        {
            public void AddScoped<T, U>() { }
        }
        public static class Custom
        {
            public static void AddScoped<T, U>(this IServiceCollection collection, int marker) { }
            public static void AddSingleton<T, U>(IServiceCollection collection) { }
            public static void TryAddEnumerable<T, U>(IServiceCollection collection) { }
        }
        public static class Configure
        {
            public static void Register(IServiceCollection services, CustomCollection custom, ServiceCollection concrete)
            {
                services.AddSingleton<IThing, Impl>();
                ServiceCollectionServiceExtensions.AddSingleton<IThing, Impl>(services);
                services.AddScoped<IThing, Impl>();
                ServiceCollectionServiceExtensions.AddScoped<IThing, Impl>(services);
                services.AddTransient<IThing, Impl>();
                ServiceCollectionServiceExtensions.AddTransient<IThing, Impl>(services);
                services.AddSingleton<IThing>(new Impl());
                ServiceCollectionServiceExtensions.AddSingleton<IThing>(implementationInstance: new Impl(), services: services);
                services.AddSingleton<IThing>(sp => new Impl());
                ServiceCollectionServiceExtensions.AddSingleton<IThing>(implementationFactory: sp => new Impl(), services: services);
                services.AddSingleton<Impl>();
                ServiceCollectionServiceExtensions.AddSingleton<Impl>(services);
                services.AddScoped(typeof(IThing), typeof(Impl));
                ServiceCollectionServiceExtensions.AddScoped(implementationType: typeof(Impl), services: services, serviceType: typeof(IThing));
                services.AddKeyedSingleton<IThing, Impl>("key");
                services.AddKeyedScoped<IThing, Impl>("key");
                services.AddKeyedTransient<IThing, Impl>("key");
                services.AddHostedService<Worker>();
                services.TryAddSingleton<IThing, Impl>();
                services.TryAddScoped<IThing, Impl>();
                services.TryAddTransient<IThing, Impl>();
                services.TryAddEnumerable(ServiceDescriptor.Singleton<IThing, Impl>());
                Custom.TryAddEnumerable<IThing, Impl>(services);
                custom.AddScoped<IThing, Impl>();
                services.AddScoped<IThing, Impl>(42);
                Custom.AddSingleton<IThing, Impl>(services);
                concrete.AddSingleton<IThing, Impl>();
            }
        }
        """;

    private static string ImplementationLeaf(string fullyQualifiedTypeName)
    {
        var lastDot = fullyQualifiedTypeName.LastIndexOf('.');
        return lastDot >= 0 ? fullyQualifiedTypeName[(lastDot + 1)..] : fullyQualifiedTypeName;
    }
}
