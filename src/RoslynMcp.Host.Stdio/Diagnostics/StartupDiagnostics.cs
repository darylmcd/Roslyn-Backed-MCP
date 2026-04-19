using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Diagnostics;

/// <summary>
/// concurrent-mcp-instances-no-tools: at startup, cross-check three independently
/// derived surface counts — SDK-registered (<see cref="McpServerOptions.ToolCollection"/>
/// etc.), source-reflected (<c>[McpServerTool]</c>-decorated methods in the host
/// assembly), and catalog-declared (<see cref="ServerSurfaceCatalog"/>). A healthy
/// process shows all three equal per category.
/// <para>
/// When multiple <c>roslynmcp</c> processes start concurrently and the client reports
/// "no tools available" on the N-th instance, each process's stderr carries a
/// <c>Startup surface: …</c> line — operators can tell at a glance whether the fault
/// is server-side (registered=0 on that instance) or client-side (registered=N on
/// every instance, but the host presented an empty tool list to the agent).
/// </para>
/// </summary>
public static class StartupDiagnostics
{
    /// <summary>
    /// Snapshot of surface counts captured right after <c>host.Build()</c>. Published
    /// via DI so <c>server_info</c> can echo it to clients as
    /// <c>surface.registered</c>, giving end-to-end observability even when stderr
    /// is not reachable.
    /// </summary>
    public sealed record SurfaceRegistrationReport(
        int ToolsRegistered,
        int ToolsReflected,
        int ToolsInCatalog,
        int ResourcesRegistered,
        int ResourcesReflected,
        int ResourcesInCatalog,
        int PromptsRegistered,
        int PromptsReflected,
        int PromptsInCatalog)
    {
        public bool ToolParityOk => ToolsRegistered == ToolsInCatalog && ToolsReflected == ToolsInCatalog;
        public bool ResourceParityOk => ResourcesRegistered == ResourcesInCatalog && ResourcesReflected == ResourcesInCatalog;
        public bool PromptParityOk => PromptsRegistered == PromptsInCatalog && PromptsReflected == PromptsInCatalog;
        public bool AllParityOk => ToolParityOk && ResourceParityOk && PromptParityOk;
    }

    /// <summary>
    /// Builds the report from the DI-registered <see cref="McpServerTool"/> /
    /// <see cref="McpServerResource"/> / <see cref="McpServerPrompt"/> instances and
    /// the host tool assembly. <c>WithToolsFromAssembly()</c> and its siblings add
    /// each attributed method to the service collection as a singleton instance of
    /// the corresponding type, so counting <c>IEnumerable&lt;McpServerTool&gt;</c>
    /// (etc.) from the provider is the authoritative post-registration count — and
    /// it is stable from <c>host.Build()</c> onward, unlike
    /// <c>McpServerOptions.ToolCollection</c> which is only materialized when the
    /// <see cref="McpServer"/> instance is constructed at hosted-service start.
    /// <paramref name="toolAssembly"/> is the assembly passed to
    /// <c>WithToolsFromAssembly()</c> (the Host.Stdio assembly by default).
    /// </summary>
    public static SurfaceRegistrationReport Capture(IServiceProvider services, Assembly toolAssembly)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(toolAssembly);

        var toolsRegistered = services.GetServices<McpServerTool>().Count();
        var resourcesRegistered = services.GetServices<McpServerResource>().Count();
        var promptsRegistered = services.GetServices<McpServerPrompt>().Count();

        var (toolsReflected, resourcesReflected, promptsReflected) = CountDecoratedMethods(toolAssembly);

        return new SurfaceRegistrationReport(
            ToolsRegistered: toolsRegistered,
            ToolsReflected: toolsReflected,
            ToolsInCatalog: ServerSurfaceCatalog.Tools.Count,
            ResourcesRegistered: resourcesRegistered,
            ResourcesReflected: resourcesReflected,
            ResourcesInCatalog: ServerSurfaceCatalog.Resources.Count,
            PromptsRegistered: promptsRegistered,
            PromptsReflected: promptsReflected,
            PromptsInCatalog: ServerSurfaceCatalog.Prompts.Count);
    }

    /// <summary>
    /// Emits one <see cref="LogLevel.Information"/> line summarizing the healthy path
    /// (all parities OK) or one <see cref="LogLevel.Error"/> line flagging the mismatch.
    /// Either way the PID and version are present so concurrent-startup investigations
    /// can correlate per-instance stderr lines across multiple <c>roslynmcp</c>
    /// processes.
    /// </summary>
    public static void LogStartup(ILogger logger, SurfaceRegistrationReport report, string version)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(report);

        var pid = Environment.ProcessId;

        if (report.AllParityOk)
        {
            logger.LogInformation(
                "Startup surface: pid={Pid} version={Version} tools={ToolsRegistered}/{ToolsInCatalog} resources={ResourcesRegistered}/{ResourcesInCatalog} prompts={PromptsRegistered}/{PromptsInCatalog} parity=ok",
                pid,
                version,
                report.ToolsRegistered,
                report.ToolsInCatalog,
                report.ResourcesRegistered,
                report.ResourcesInCatalog,
                report.PromptsRegistered,
                report.PromptsInCatalog);
            return;
        }

        logger.LogError(
            "Startup surface PARITY MISMATCH: pid={Pid} version={Version} " +
            "tools registered={ToolsRegistered} reflected={ToolsReflected} catalog={ToolsInCatalog} parityOk={ToolParityOk}; " +
            "resources registered={ResourcesRegistered} reflected={ResourcesReflected} catalog={ResourcesInCatalog} parityOk={ResourceParityOk}; " +
            "prompts registered={PromptsRegistered} reflected={PromptsReflected} catalog={PromptsInCatalog} parityOk={PromptParityOk}. " +
            "A zero 'registered' count means WithToolsFromAssembly() failed to discover attributed methods in this process — " +
            "see ai_docs/backlog.md row concurrent-mcp-instances-no-tools for the investigation framework.",
            pid,
            version,
            report.ToolsRegistered,
            report.ToolsReflected,
            report.ToolsInCatalog,
            report.ToolParityOk,
            report.ResourcesRegistered,
            report.ResourcesReflected,
            report.ResourcesInCatalog,
            report.ResourceParityOk,
            report.PromptsRegistered,
            report.PromptsReflected,
            report.PromptsInCatalog,
            report.PromptParityOk);
    }

    private static (int Tools, int Resources, int Prompts) CountDecoratedMethods(Assembly toolAssembly)
    {
        var toolCount = 0;
        var resourceCount = 0;
        var promptCount = 0;

        foreach (var method in toolAssembly.GetTypes()
                     .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)))
        {
            if (method.GetCustomAttribute<McpServerToolAttribute>() is not null) toolCount++;
            if (method.GetCustomAttribute<McpServerResourceAttribute>() is not null) resourceCount++;
            if (method.GetCustomAttribute<McpServerPromptAttribute>() is not null) promptCount++;
        }

        return (toolCount, resourceCount, promptCount);
    }
}
