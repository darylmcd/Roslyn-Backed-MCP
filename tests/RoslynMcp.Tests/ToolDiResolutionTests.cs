using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio;
using RoslynMcp.Host.Stdio.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Guard against <c>di-register-latest-version-provider</c>-class regressions.
///
/// In v1.19.0 <see cref="ILatestVersionProvider"/> shipped registered only against
/// its concrete type (<see cref="NuGetVersionChecker"/>). The MCP SDK's parameter
/// binder, on failing to resolve the interface from DI, falls back to exposing the
/// parameter in the tool schema as a required user-supplied object — which breaks
/// every invocation of <c>server_info</c>.
///
/// The existing <c>ServerInfoUpdateLatestTests</c> and <c>SurfaceCatalogTests</c>
/// both construct <see cref="ServerTools.GetServerInfo"/>'s arguments directly,
/// bypassing DI entirely, so they cannot catch this class of bug.
///
/// This test mirrors the host's <c>Program.cs</c> DI graph and asserts every
/// interface parameter on every <c>[McpServerTool]</c> method resolves — closing
/// that gap.
/// </summary>
[TestClass]
public sealed class ToolDiResolutionTests
{
    [TestMethod]
    public void EveryToolMethod_InterfaceParameter_ResolvesFromHostServiceProvider()
    {
        using var provider = BuildHostServiceProvider();

        var toolAssembly = typeof(ServerTools).Assembly;
        var toolMethods = toolAssembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .ToList();

        Assert.IsTrue(toolMethods.Count > 0,
            "No [McpServerTool] methods were discovered — test fixture broken.");

        var failures = new List<string>();
        foreach (var method in toolMethods)
        {
            var toolName = method.GetCustomAttribute<McpServerToolAttribute>()!.Name;
            foreach (var param in method.GetParameters())
            {
                if (!IsAppServiceInterface(param.ParameterType))
                {
                    // Skip:
                    //   - non-interface parameters (user inputs like string/int/DTO)
                    //   - BCL and MCP-SDK interfaces (IProgress<T>, IReadOnlyList<T>, CancellationToken, etc.)
                    //     — these are either SDK-injected at invocation time or JSON-deserialized user inputs.
                    // Only our own service interfaces (declared in RoslynMcp.* assemblies) must resolve from DI.
                    continue;
                }

                var resolved = provider.GetService(param.ParameterType);
                if (resolved is null)
                {
                    failures.Add(
                        $"{toolName} ({method.DeclaringType!.Name}.{method.Name}): " +
                        $"parameter '{param.Name}' of type {param.ParameterType.FullName} " +
                        "cannot be resolved from the host service provider. Either register " +
                        "it against its interface in DI (Program.cs or AddRoslynServices), or " +
                        "change the parameter to a concrete type that is already registered.");
                }
            }
        }

        Assert.AreEqual(0, failures.Count,
            "Tool methods have interface parameters that do not resolve from the host DI " +
            "graph. The MCP SDK will leak these as required user-supplied tool inputs and " +
            "break every invocation of the affected tool.\n\n" +
            string.Join("\n\n", failures));
    }

    [TestMethod]
    public void LatestVersionProvider_ResolvesAsNuGetVersionChecker()
    {
        // Targeted regression pin for the v1.19.0 shipped bug: the interface must resolve,
        // and it must resolve to the same instance as the concrete type so both parameter
        // shapes (interface vs concrete) observe the same cached NuGet fetch state.
        using var provider = BuildHostServiceProvider();

        var viaInterface = provider.GetService<ILatestVersionProvider>();
        var viaConcrete = provider.GetService<NuGetVersionChecker>();

        Assert.IsNotNull(viaInterface, "ILatestVersionProvider must resolve from the host DI graph.");
        Assert.IsNotNull(viaConcrete, "NuGetVersionChecker must resolve from the host DI graph.");
        Assert.AreSame(viaConcrete, viaInterface,
            "Interface and concrete registrations must share the same singleton so both see the same cached latest version.");
    }

    /// <summary>
    /// Is this parameter type an interface declared by the application (an RoslynMcp service)
    /// rather than a BCL collection interface (IReadOnlyList&lt;T&gt;), an SDK-injected interface
    /// (IProgress&lt;T&gt;), or any other out-of-DI interface?
    ///
    /// The MCP SDK hands IProgress and friends to the tool method at invocation time; BCL
    /// collection interfaces are deserialized from the JSON payload as user input. Only our
    /// own interfaces need to resolve from the host service provider.
    /// </summary>
    private static bool IsAppServiceInterface(Type type)
    {
        if (!type.IsInterface)
        {
            return false;
        }

        var assemblyName = type.Assembly.GetName().Name;
        return assemblyName is not null
            && assemblyName.StartsWith("RoslynMcp.", StringComparison.Ordinal);
    }

    /// <summary>
    /// Mirrors the service registrations in <c>src/RoslynMcp.Host.Stdio/Program.cs</c>
    /// via the shared <c>AddRoslynMcpHostServices</c> extension method, so this test
    /// fixture stays in lock-step with production composition automatically — adding
    /// a new host-side singleton in the extension lights it up here too
    /// (di-graph-triple-registration-cleanup).
    /// </summary>
    private static ServiceProvider BuildHostServiceProvider()
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

        return services.BuildServiceProvider();
    }
}
