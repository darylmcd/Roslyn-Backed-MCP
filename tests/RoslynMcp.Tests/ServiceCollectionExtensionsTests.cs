using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio;
using RoslynMcp.Host.Stdio.Services;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ServiceCollectionExtensionsTests
{
    [TestMethod]
    public void AddRoslynServices_Resolves_Core_Singletons()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.ClearProviders());
        services.AddRoslynServices();

        using var sp = services.BuildServiceProvider();

        Assert.IsNotNull(sp.GetRequiredService<IWorkspaceManager>());
        Assert.IsNotNull(sp.GetRequiredService<IWorkspaceExecutionGate>());
        Assert.IsNotNull(sp.GetRequiredService<ICompileCheckService>());
        Assert.IsNotNull(sp.GetRequiredService<ICodeActionService>());
        Assert.IsNotNull(sp.GetRequiredService<INamespaceDependencyService>());
        Assert.IsNotNull(sp.GetRequiredService<IDiRegistrationService>());
        Assert.IsNotNull(sp.GetRequiredService<INuGetDependencyService>());
        Assert.IsNotNull(sp.GetRequiredService<IDeadCodeService>());
        Assert.IsNotNull(sp.GetRequiredService<IPreviewStore>());
        Assert.IsNotNull(sp.GetRequiredService<IProjectMutationPreviewStore>());
        Assert.IsNotNull(sp.GetRequiredService<ICompositePreviewStore>());
        Assert.IsNotNull(sp.GetRequiredService<IPackageMigrationOrchestrator>());
        Assert.IsNotNull(sp.GetRequiredService<IClassSplitOrchestrator>());
        Assert.IsNotNull(sp.GetRequiredService<IExtractAndWireOrchestrator>());
        Assert.IsNotNull(sp.GetRequiredService<ICompositeApplyOrchestrator>());
    }

    /// <summary>
    /// di-graph-triple-registration-cleanup regression pin: each host-side option
    /// singleton + the NuGet version-checker pair must register exactly once. Prior
    /// to this cleanup, copy-paste between Program.cs and two test-fixture builders
    /// surfaced 9 service types each registered three times in the cross-assembly DI
    /// scan (last-write-wins held, but the dead lines obscured intent and risked
    /// drift). After the consolidation into <see cref="ServiceCollectionExtensions.AddRoslynMcpHostServices"/>,
    /// each service type has exactly one Add call inside the host extension.
    /// </summary>
    [TestMethod]
    public void AddRoslynMcpHostServices_RegistersEachOptionTypeExactlyOnce()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.ClearProviders());
        services.AddRoslynMcpHostServices(
            new WorkspaceManagerOptions(),
            new ValidationServiceOptions(),
            new PreviewStoreOptions(),
            new ExecutionGateOptions(),
            new SecurityOptions(),
            new ScriptingServiceOptions());

        AssertSingleRegistration<WorkspaceManagerOptions>(services);
        AssertSingleRegistration<ValidationServiceOptions>(services);
        AssertSingleRegistration<PreviewStoreOptions>(services);
        AssertSingleRegistration<ExecutionGateOptions>(services);
        AssertSingleRegistration<SecurityOptions>(services);
        AssertSingleRegistration<ScriptingServiceOptions>(services);
        AssertSingleRegistration<HttpClient>(services);
        AssertSingleRegistration<NuGetVersionChecker>(services);
        AssertSingleRegistration<ILatestVersionProvider>(services);
    }

    /// <summary>
    /// The interface and concrete <see cref="NuGetVersionChecker"/> registrations
    /// must resolve to the same singleton so both parameter shapes (interface vs
    /// concrete) observe the same cached NuGet fetch state. This is the contract
    /// the production factory in <see cref="ServiceCollectionExtensions.AddRoslynMcpHostServices"/>
    /// upholds via <c>sp =&gt; sp.GetRequiredService&lt;NuGetVersionChecker&gt;()</c>.
    /// </summary>
    [TestMethod]
    public void AddRoslynMcpHostServices_LatestVersionProvider_BridgesToConcreteSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.ClearProviders());
        services.AddRoslynMcpHostServices(
            new WorkspaceManagerOptions(),
            new ValidationServiceOptions(),
            new PreviewStoreOptions(),
            new ExecutionGateOptions(),
            new SecurityOptions(),
            new ScriptingServiceOptions());

        using var sp = services.BuildServiceProvider();
        var viaInterface = sp.GetRequiredService<ILatestVersionProvider>();
        var viaConcrete = sp.GetRequiredService<NuGetVersionChecker>();

        Assert.AreSame(viaConcrete, viaInterface,
            "Interface and concrete registrations must share the same singleton.");
    }

    private static void AssertSingleRegistration<T>(IServiceCollection services)
    {
        var count = services.Count(d => d.ServiceType == typeof(T));
        Assert.AreEqual(1, count,
            $"Service type {typeof(T).Name} must be registered exactly once via " +
            $"AddRoslynMcpHostServices; found {count}. Triple-registration regression.");
    }
}
