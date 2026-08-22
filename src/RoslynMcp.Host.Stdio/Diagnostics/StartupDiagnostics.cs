using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Diagnostics;

/// <summary>
/// concurrent-mcp-instances-no-tools: at startup, cross-check three independently
/// derived surface counts — SDK-registered (<see cref="McpServerOptions.ToolCollection"/>
/// etc.), source-reflected (<c>[McpServerTool]</c>-decorated methods in the host
/// assembly), and catalog-declared (<see cref="ServerSurfaceCatalog"/>). A healthy
/// process has registered counts equal to the selected-tier expectation, while reflection
/// continues to match the complete catalog in every category.
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
        internal int? SelectedToolsExpected { get; init; }
        internal int? SelectedResourcesExpected { get; init; }
        internal int? SelectedPromptsExpected { get; init; }

        public int ToolsExpected => SelectedToolsExpected ?? ToolsInCatalog;
        public int ResourcesExpected => SelectedResourcesExpected ?? ResourcesInCatalog;
        public int PromptsExpected => SelectedPromptsExpected ?? PromptsInCatalog;

        public IReadOnlyList<string> ToolTiers { get; init; } = ["stable", "experimental"];

        public bool ToolParityOk => ToolsRegistered == ToolsExpected && ToolsReflected == ToolsInCatalog;
        public bool ResourceParityOk => ResourcesRegistered == ResourcesExpected && ResourcesReflected == ResourcesInCatalog;
        public bool PromptParityOk => PromptsRegistered == PromptsExpected && PromptsReflected == PromptsInCatalog;
        public bool AllParityOk => ToolParityOk && ResourceParityOk && PromptParityOk;
    }

    /// <summary>
    /// Builds the report from the finalized <see cref="McpServerOptions"/> primitive collections
    /// and the host tool assembly. Reading the options collections observes post-configuration
    /// policies such as support-tier filtering, unlike counting the unfiltered DI registrations.
    /// <paramref name="toolAssembly"/> is the assembly passed to
    /// <c>WithToolsFromAssembly()</c> (the Host.Stdio assembly by default).
    /// </summary>
    public static SurfaceRegistrationReport Capture(IServiceProvider services, Assembly toolAssembly)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(toolAssembly);

        var serverOptions = services.GetRequiredService<IOptions<McpServerOptions>>().Value;
        var toolsRegistered = serverOptions.ToolCollection?.Count ?? 0;
        var resourcesRegistered = serverOptions.ResourceCollection?.Count ?? 0;
        var promptsRegistered = serverOptions.PromptCollection?.Count ?? 0;
        var selection = services.GetService<ToolTierSelection>() ?? ToolTierSelection.All;
        var toolsExpected = ServerSurfaceCatalog.SelectTools(selection).Count;
        var resourcesExpected = ServerSurfaceCatalog.Resources.Count(entry => selection.Includes(entry.SupportTier));
        var promptsExpected = ServerSurfaceCatalog.Prompts.Count(entry => selection.Includes(entry.SupportTier));

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
            PromptsInCatalog: ServerSurfaceCatalog.Prompts.Count)
        {
            SelectedToolsExpected = toolsExpected,
            SelectedResourcesExpected = resourcesExpected,
            SelectedPromptsExpected = promptsExpected,
            ToolTiers = selection.Tiers.OrderBy(static tier => tier, StringComparer.Ordinal).ToArray(),
        };
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
                "Startup surface: pid={Pid} version={Version} toolTiers={ToolTiers} tools={ToolsRegistered}/{ToolsExpected}/{ToolsInCatalog} resources={ResourcesRegistered}/{ResourcesExpected}/{ResourcesInCatalog} prompts={PromptsRegistered}/{PromptsExpected}/{PromptsInCatalog} parity=ok",
                pid,
                version,
                string.Join(',', report.ToolTiers),
                report.ToolsRegistered,
                report.ToolsExpected,
                report.ToolsInCatalog,
                report.ResourcesRegistered,
                report.ResourcesExpected,
                report.ResourcesInCatalog,
                report.PromptsRegistered,
                report.PromptsExpected,
                report.PromptsInCatalog);
            return;
        }

        logger.LogError(
            "Startup surface PARITY MISMATCH: pid={Pid} version={Version} toolTiers={ToolTiers} " +
            "tools registered={ToolsRegistered} expected={ToolsExpected} reflected={ToolsReflected} catalog={ToolsInCatalog} parityOk={ToolParityOk}; " +
            "resources registered={ResourcesRegistered} expected={ResourcesExpected} reflected={ResourcesReflected} catalog={ResourcesInCatalog} parityOk={ResourceParityOk}; " +
            "prompts registered={PromptsRegistered} expected={PromptsExpected} reflected={PromptsReflected} catalog={PromptsInCatalog} parityOk={PromptParityOk}. " +
            "A zero 'registered' count means WithToolsFromAssembly() failed to discover attributed methods in this process; " +
            "compare this process's stderr with the other MCP instances to isolate server-side registration from client presentation.",
            pid,
            version,
            string.Join(',', report.ToolTiers),
            report.ToolsRegistered,
            report.ToolsExpected,
            report.ToolsReflected,
            report.ToolsInCatalog,
            report.ToolParityOk,
            report.ResourcesRegistered,
            report.ResourcesExpected,
            report.ResourcesReflected,
            report.ResourcesInCatalog,
            report.ResourceParityOk,
            report.PromptsRegistered,
            report.PromptsExpected,
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
