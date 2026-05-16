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
        var shim = """
namespace SampleLib;

public interface IServiceCollection { }

public sealed class FakeServiceCollection : IServiceCollection { }

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSingleton<TService, TImpl>(this IServiceCollection services)
        where TImpl : TService => services;
    public static IServiceCollection AddScoped<TService, TImpl>(this IServiceCollection services)
        where TImpl : TService => services;
    public static IServiceCollection AddTransient<TService, TImpl>(this IServiceCollection services)
        where TImpl : TService => services;
    public static IServiceCollection TryAddSingleton<TService, TImpl>(this IServiceCollection services)
        where TImpl : TService => services;
    public static IServiceCollection TryAddScoped<TService, TImpl>(this IServiceCollection services)
        where TImpl : TService => services;
    public static IServiceCollection TryAddTransient<TService, TImpl>(this IServiceCollection services)
        where TImpl : TService => services;
}

public interface IFoo { }
public sealed class FooSingleton : IFoo { }
public sealed class FooScoped : IFoo { }

public interface IBar { }
public sealed class BarFirst : IBar { }
public sealed class BarSecond : IBar { }

public interface IBaz { }
public sealed class BazOnly : IBaz { }
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
