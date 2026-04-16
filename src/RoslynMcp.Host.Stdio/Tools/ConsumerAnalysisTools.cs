using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class ConsumerAnalysisTools
{

    [McpServerTool(Name = "find_consumers", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("analysis", "stable", true, false,
        "Find all types that depend on a given type or interface, classified by dependency kind."),
     Description("Find all types that depend on a given type or interface, classified by dependency kind (Constructor, Field, Parameter, BaseType, LocalVariable, Property, ReturnType, GenericArgument)")]
    public static Task<string> FindConsumers(
        IWorkspaceExecutionGate gate,
        IConsumerAnalysisService consumerAnalysisService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: absolute path to the source file")] string? filePath = null,
        [Description("Optional: 1-based line number")] int? line = null,
        [Description("Optional: 1-based column number")] int? column = null,
        [Description("Optional: stable symbol handle returned by other semantic tools")] string? symbolHandle = null,
        [Description("Optional: fully qualified metadata name, e.g. Namespace.IMyInterface")] string? metadataName = null,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            var locator = SymbolLocatorFactory.Create(filePath, line, column, symbolHandle, metadataName);
            var result = await consumerAnalysisService.FindConsumersAsync(workspaceId, locator, c);
            if (result is null) throw new KeyNotFoundException("No symbol found at the specified location");
            return JsonSerializer.Serialize(result, JsonDefaults.Indented);
        }, ct);
    }
}
