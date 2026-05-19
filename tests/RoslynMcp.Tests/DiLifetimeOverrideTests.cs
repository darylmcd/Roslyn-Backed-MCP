using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

/// <summary>
/// di-lifetime-mismatch-detection: covers the override-chain analysis added to
/// <c>get_di_registrations</c>. Tests use a self-contained shim of <c>IServiceCollection</c> +
/// <c>ServiceCollectionExtensions</c> written into the isolated workspace, so the analyzer
/// detects registrations purely by name-shape (containing-type name contains
/// "ServiceCollection") without requiring a real Microsoft.Extensions.DependencyInjection
/// package reference. This keeps the test independent of fixture-csproj package churn.
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
        await WriteServiceCollectionShimAsync(workspace, CancellationToken.None);
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
        await WriteServiceCollectionShimAsync(workspace, CancellationToken.None);

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
        await WriteServiceCollectionShimAsync(workspace, CancellationToken.None);

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
        await WriteServiceCollectionShimAsync(workspace, CancellationToken.None);
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
        await WriteServiceCollectionShimAsync(workspace, CancellationToken.None);

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
        await WriteServiceCollectionShimAsync(workspace, CancellationToken.None);

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
        await WriteServiceCollectionShimAsync(workspace, CancellationToken.None);

        // Two Add* registrations of IFoo, but NO IEnumerable<IFoo>/IReadOnlyList<IFoo>/IFoo[]
        // ctor or property exists in the shim or in the registration file. The chain must
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
        await WriteServiceCollectionShimAsync(workspace, CancellationToken.None);

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
        await WriteServiceCollectionShimAsync(workspace, CancellationToken.None);

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
    /// get-di-registrations-multi-registration-overcounting Bug 2 (c): lambda body using BOTH
    /// <c>GetRequiredService&lt;T&gt;</c> and <c>GetService&lt;T&gt;</c> still resolves to a
    /// real implementation type (the first recognized service-locator call wins). The walk
    /// must terminate cleanly without exception when multiple calls are present.
    /// </summary>
    [TestMethod]
    public async Task Factory_Lambda_With_Mixed_GetRequiredService_And_GetService_Resolves()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        await WriteServiceCollectionShimAsync(workspace, CancellationToken.None);

        // Lambda constructs CompositeWrapper from an inner CompositeImpl pulled via
        // GetRequiredService, plus a side-channel GetService<ISnapshotReader>() that is
        // ignored. The resolved impl should be one of CompositeImpl/ISnapshotReader — the
        // assertion is "non-factory string is returned", not the precise selection.
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
        Assert.AreNotEqual("factory", iCompositeEntry.ImplementationType,
            "Lambda body containing GetRequiredService<T> must resolve T even when other calls are present.");
        Assert.AreEqual("CompositeImpl", ImplementationLeaf(iCompositeEntry.ImplementationType),
            "First recognized service-locator call (GetRequiredService<CompositeImpl>) supplies the resolved impl type.");
    }

    /// <summary>
    /// get-di-registrations-multi-registration-overcounting Bug 2 (d): lambda whose body has
    /// NO recognizable <c>GetRequiredService&lt;T&gt;</c> / <c>GetService&lt;T&gt;</c> call
    /// falls back to <c>"factory"</c> rather than throwing.
    /// </summary>
    [TestMethod]
    public async Task Factory_Lambda_Without_Service_Locator_Falls_Back_To_Factory_String()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        await WriteServiceCollectionShimAsync(workspace, CancellationToken.None);

        // Lambda body constructs a new OpaqueImpl directly — no GetRequiredService/GetService
        // call. The resolved impl must fall back to "factory" rather than throw or return a
        // bogus type name.
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
        Assert.AreEqual("factory", iOpaqueEntry.ImplementationType,
            "Lambdas without a GetRequiredService/GetService forwarding call must fall back to the \"factory\" sentinel.");
    }

    [TestMethod]
    public async Task Summary_Mode_Returns_Compact_Counts_And_Paged_Override_Chains()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        await WriteServiceCollectionShimAsync(workspace, CancellationToken.None);
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
        await WriteServiceCollectionShimAsync(workspace, CancellationToken.None);
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
    /// response shape without interacting with the shared shim used by the other tests.
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

    /// <summary>
    /// Writes a self-contained shim of <c>IServiceCollection</c> + extension methods to
    /// SampleLib. The DI registration walker matches by containing-type name shape
    /// ("ServiceCollection") and method-name lifetime mapping, so this gives full semantic
    /// binding without dragging in Microsoft.Extensions.DependencyInjection.
    /// </summary>
    private static async Task WriteServiceCollectionShimAsync(IsolatedWorkspaceScope workspace, CancellationToken ct)
    {
        // get-di-registrations-multi-registration-overcounting: shim extended with the
        // single-type-arg factory overload, an IServiceProvider stub with GetRequiredService /
        // GetService extensions, and additional types/interfaces that the new tests register
        // multiple times via IEnumerable<T>/IReadOnlyList<T>/T[] consumption.
        var shim = """
namespace SampleLib;

public interface IServiceCollection { }

public sealed class FakeServiceCollection : IServiceCollection { }

public interface IServiceProvider { }

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSingleton<TService, TImpl>(this IServiceCollection services)
        where TImpl : TService => services;
    public static IServiceCollection AddScoped<TService, TImpl>(this IServiceCollection services)
        where TImpl : TService => services;
    public static IServiceCollection AddTransient<TService, TImpl>(this IServiceCollection services)
        where TImpl : TService => services;
    public static IServiceCollection AddSingleton<TService>(this IServiceCollection services, System.Func<IServiceProvider, TService> factory) => services;
    public static IServiceCollection AddScoped<TService>(this IServiceCollection services, System.Func<IServiceProvider, TService> factory) => services;
    public static IServiceCollection AddTransient<TService>(this IServiceCollection services, System.Func<IServiceProvider, TService> factory) => services;
    public static IServiceCollection TryAddSingleton<TService, TImpl>(this IServiceCollection services)
        where TImpl : TService => services;
    public static IServiceCollection TryAddScoped<TService, TImpl>(this IServiceCollection services)
        where TImpl : TService => services;
    public static IServiceCollection TryAddTransient<TService, TImpl>(this IServiceCollection services)
        where TImpl : TService => services;
}

public static class ServiceProviderExtensions
{
    public static T GetRequiredService<T>(this IServiceProvider provider) => default!;
    public static T? GetService<T>(this IServiceProvider provider) => default;
}

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
""";
        await File.WriteAllTextAsync(workspace.GetPath("SampleLib", "DiShim.cs"), shim, ct).ConfigureAwait(false);
    }

    private static async Task WriteRegistrationFileAsync(IsolatedWorkspaceScope workspace, string fileName, string contents, CancellationToken ct)
    {
        await File.WriteAllTextAsync(workspace.GetPath("SampleLib", fileName), contents, ct).ConfigureAwait(false);
    }

    private static string ImplementationLeaf(string fullyQualifiedTypeName)
    {
        var lastDot = fullyQualifiedTypeName.LastIndexOf('.');
        return lastDot >= 0 ? fullyQualifiedTypeName[(lastDot + 1)..] : fullyQualifiedTypeName;
    }
}
