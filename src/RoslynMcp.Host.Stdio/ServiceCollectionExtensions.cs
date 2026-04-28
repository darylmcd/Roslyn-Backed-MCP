using Microsoft.Extensions.DependencyInjection;
using RoslynMcp.Host.Stdio.Services;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Host.Stdio;

/// <summary>
/// Extension methods for registering Host.Stdio composition-root services
/// (host-bound options + the NuGet version checker pair). Shared between the
/// production <c>Program.cs</c> startup path and DI test fixtures so both stay
/// in lock-step — adding a new host-side singleton in one place means it lights
/// up for both production and tests automatically.
/// </summary>
/// <remarks>
/// di-graph-triple-registration-cleanup: prior to this refactor, the same six
/// option singletons + <c>HttpClient</c> + <c>NuGetVersionChecker</c> +
/// <see cref="ILatestVersionProvider"/> registrations were copy-pasted across
/// <c>Program.cs</c>, <c>StartupDiagnosticsTests.BuildTestHost</c>, and
/// <c>ToolDiResolutionTests.BuildHostServiceProvider</c>. The copy-paste pattern
/// drifted (a comment in ToolDiResolutionTests explicitly warned operators
/// "when Program.cs adds a new service, update here too — or a new tool method
/// that injects an unregistered service will fail"). This extension consolidates
/// the pattern.
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the host-side options bag + the NuGet version-checker pair
    /// (both <see cref="NuGetVersionChecker"/> as a concrete singleton AND
    /// <see cref="ILatestVersionProvider"/> bridged to the same instance).
    /// </summary>
    /// <param name="services">The service collection to register against.</param>
    /// <param name="workspaceManagerOptions">Pre-bound workspace-manager options (env-var resolved in production, defaults in tests).</param>
    /// <param name="validationServiceOptions">Pre-bound validation-service options.</param>
    /// <param name="previewStoreOptions">Pre-bound preview-store options.</param>
    /// <param name="executionGateOptions">Pre-bound execution-gate options.</param>
    /// <param name="securityOptions">Pre-bound security options.</param>
    /// <param name="scriptingServiceOptions">Pre-bound scripting-service options.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <remarks>
    /// The dual <see cref="NuGetVersionChecker"/> / <see cref="ILatestVersionProvider"/>
    /// registration is intentional: the concrete registration is needed for code that
    /// imports the concrete type directly; the interface registration is what MCP tool
    /// methods (e.g. server_info) inject. v1.19.0 shipped with only the concrete
    /// registration, which caused the MCP SDK's parameter binder to fail to resolve
    /// <c>ILatestVersionProvider versionChecker</c> and leak the service parameter
    /// into the tool schema as a required user-supplied argument — breaking server_info
    /// (fix: di-register-latest-version-provider). The factory delegate guarantees the
    /// interface and concrete views observe the same singleton instance.
    /// </remarks>
    public static IServiceCollection AddRoslynMcpHostServices(
        this IServiceCollection services,
        WorkspaceManagerOptions workspaceManagerOptions,
        ValidationServiceOptions validationServiceOptions,
        PreviewStoreOptions previewStoreOptions,
        ExecutionGateOptions executionGateOptions,
        SecurityOptions securityOptions,
        ScriptingServiceOptions scriptingServiceOptions)
    {
        services.AddSingleton(workspaceManagerOptions);
        services.AddSingleton(validationServiceOptions);
        services.AddSingleton(previewStoreOptions);
        services.AddSingleton(executionGateOptions);
        services.AddSingleton(securityOptions);
        services.AddSingleton(scriptingServiceOptions);

        services.AddSingleton<HttpClient>();
        services.AddSingleton<NuGetVersionChecker>();
        services.AddSingleton<ILatestVersionProvider>(
            sp => sp.GetRequiredService<NuGetVersionChecker>());

        services.AddRoslynServices();
        return services;
    }
}
