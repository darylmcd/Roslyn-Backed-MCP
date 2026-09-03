using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Catalog;

/// <summary>
/// Applies catalog-backed protocol metadata and the configured support-tier gate to the SDK's
/// authoritative primitive collections. Removing a primitive here removes it from both discovery
/// and dispatch; presentation-only list filters would leave hidden tools, prompts, or resources
/// callable through their direct protocol methods.
/// </summary>
internal static class SurfaceRegistrationPolicy
{
    public static IServiceCollection AddRoslynMcpSurfaceRegistrationPolicy(
        this IServiceCollection services,
        ToolTierSelection selection)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(selection);

        services.AddSingleton(selection);
        services.PostConfigure<McpServerOptions>(options => Apply(options, selection));
        return services;
    }

    internal static void Apply(McpServerOptions options, ToolTierSelection selection)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(selection);

        ValidateCatalogSupportTiers(
            ServerSurfaceCatalog.Tools,
            ServerSurfaceCatalog.Resources,
            ServerSurfaceCatalog.Prompts);

        var tools = options.ToolCollection
            ?? throw new InvalidOperationException("The MCP SDK did not initialize its tool collection.");
        var prompts = options.PromptCollection
            ?? throw new InvalidOperationException("The MCP SDK did not initialize its prompt collection.");
        var resources = options.ResourceCollection
            ?? throw new InvalidOperationException("The MCP SDK did not initialize its resource collection.");

        var selectedToolNames = ServerSurfaceCatalog.SelectTools(selection)
            .Select(static entry => entry.Name)
            .ToHashSet(StringComparer.Ordinal);

        using var deferredToolNotifications = tools.DeferChangedEvents();
        foreach (var tool in tools.ToArray())
        {
            var toolName = tool.ProtocolTool.Name;
            var entry = GetCatalogEntry(ServerSurfaceCatalog.Tools, toolName, "tool");

            ProjectDeclaredOutputSchema(tool);

            if (!selectedToolNames.Contains(entry.Name))
            {
                tools.Remove(tool);
            }
        }

        using var deferredPromptNotifications = prompts.DeferChangedEvents();
        foreach (var prompt in prompts.ToArray())
        {
            var entry = GetCatalogEntry(
                ServerSurfaceCatalog.Prompts,
                prompt.ProtocolPrompt.Name,
                "prompt");
            if (!selection.Includes(entry.SupportTier))
            {
                prompts.Remove(prompt);
            }
        }

        using var deferredResourceNotifications = resources.DeferChangedEvents();
        foreach (var resource in resources.ToArray())
        {
            var entry = GetCatalogEntry(
                ServerSurfaceCatalog.Resources,
                resource.ProtocolResourceTemplate.Name,
                "resource");
            if (!selection.Includes(entry.SupportTier))
            {
                resources.Remove(resource);
                continue;
            }

            ProjectSelectedProfileDescription(resource, selection);
        }
    }

    /// <summary>
    /// Replaces the SDK-generated <c>outputSchema</c> with the catalog-declared one, which is the
    /// single generation authority for advertised structured output
    /// (see <see cref="ToolOutputSchemaIndex"/>). The overwrite is load-bearing rather than
    /// redundant: the SDK generator runs on its own serializer defaults, while the index clones
    /// <see cref="JsonDefaults.Indented"/> — the exact options the runtime
    /// <c>structuredContent</c> channel serializes with.
    /// <para>
    /// Both asymmetries fail closed, so neither authority can quietly become the only one:
    /// an SDK-generated schema the index does not declare would ship an unreviewed shape, and an
    /// index declaration the SDK never generated means the tool no longer opts into structured
    /// content while still advertising a schema.
    /// </para>
    /// </summary>
    private static void ProjectDeclaredOutputSchema(McpServerTool tool)
    {
        var toolName = tool.ProtocolTool.Name;
        var declared = ToolOutputSchemaIndex.GetSchema(toolName);
        var sdkGenerated = tool.ProtocolTool.OutputSchema;

        if (declared is null)
        {
            if (sdkGenerated is not null)
            {
                throw new InvalidOperationException(
                    $"MCP tool '{toolName}' advertises an SDK-generated outputSchema that " +
                    $"{nameof(ToolOutputSchemaIndex)} does not declare. Declare the tool's generation route " +
                    "there so one authority owns the advertised schema, or drop its OutputSchemaType.");
            }

            return;
        }

        if (sdkGenerated is null)
        {
            throw new InvalidOperationException(
                $"{nameof(ToolOutputSchemaIndex)} declares an outputSchema for MCP tool '{toolName}' but the " +
                "registered tool advertises none. The tool must keep [McpServerTool(UseStructuredContent = true, " +
                "OutputSchemaType = ...)] or the declaration must be removed.");
        }

        tool.ProtocolTool.OutputSchema = JsonSerializer.SerializeToElement(declared);
    }

    internal static void ValidateCatalogSupportTiers(
        IReadOnlyList<SurfaceEntry> tools,
        IReadOnlyList<SurfaceEntry> resources,
        IReadOnlyList<SurfaceEntry> prompts)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(prompts);

        foreach (var (entries, kind) in new[]
                 {
                     (tools, "tool"),
                     (resources, "resource"),
                     (prompts, "prompt"),
                 })
        {
            foreach (var entry in entries)
            {
                if (!ToolTierSelection.IsSupportedTier(entry.SupportTier))
                {
                    throw new InvalidOperationException(
                        $"Catalog {kind} '{entry.Name}' declares unsupported support tier '{entry.SupportTier}'. " +
                        "Use 'stable' or 'experimental'.");
                }
            }
        }
    }

    private static void ProjectSelectedProfileDescription(
        McpServerResource resource,
        ToolTierSelection selection)
    {
        if (selection.Includes("experimental"))
        {
            return;
        }

        var description = resource.ProtocolResourceTemplate.Name switch
        {
            "server_catalog" =>
                "Machine-readable catalog summary for the active support-tier profile: selected tool/prompt counts + resource list + usable workflow hints. Use tools/list and prompts/list for the selected entries.",
            "resource_templates" =>
                "Lists resource URI templates available in the active support-tier profile. Substitute workspaceId (and filePath where required) in workspace-scoped templates.",
            _ => null,
        };
        if (description is null)
        {
            return;
        }

        resource.ProtocolResourceTemplate.Description = description;
        if (resource.ProtocolResource is not null)
        {
            resource.ProtocolResource.Description = description;
        }
    }

    private static SurfaceEntry GetCatalogEntry(
        IReadOnlyList<SurfaceEntry> entries,
        string name,
        string primitiveKind)
    {
        return entries.FirstOrDefault(entry => string.Equals(entry.Name, name, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"Registered MCP {primitiveKind} '{name}' is absent from ServerSurfaceCatalog.");
    }
}
