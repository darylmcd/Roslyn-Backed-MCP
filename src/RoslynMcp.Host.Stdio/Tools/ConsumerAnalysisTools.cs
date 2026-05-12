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
        "Find all types that depend on a given type or interface, classified by dependency kind. Accepts an optional projectFilter (case-sensitive Project.Name; comma-separated)."),
     Description("Find all types that depend on a given type or interface, classified by dependency kind (Constructor, Field, Parameter, BaseType, LocalVariable, Property, ReturnType, GenericArgument, StaticMemberAccess). Optional `projectFilter` (case-sensitive Project.Name; comma-separated for multi) restricts the consumer walk to the listed project(s) — matches semantic_grep's filter semantics.")]
    public static Task<string> FindConsumers(
        IWorkspaceExecutionGate gate,
        IConsumerAnalysisService consumerAnalysisService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: absolute path to the source file")] string? filePath = null,
        [Description("Optional: 1-based line number")] int? line = null,
        [Description("Optional: 1-based column number")] int? column = null,
        [Description("Optional: stable symbol handle returned by other semantic tools")] string? symbolHandle = null,
        [Description("Optional: fully qualified metadata name, e.g. Namespace.IMyInterface")] string? metadataName = null,
        [Description("Optional: case-sensitive Project.Name filter to scope the consumer walk; comma-separated for multiple projects (e.g. 'Foo.Core,Foo.Tests'). Null/empty preserves the unfiltered solution-wide walk.")] string? projectFilter = null,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            var locator = SymbolLocatorFactory.Create(filePath, line, column, symbolHandle, metadataName);
            var filterSet = SymbolTools.ParseProjectFilter(projectFilter);
            var result = await consumerAnalysisService.FindConsumersAsync(workspaceId, locator, c, filterSet);
            if (result is null) throw new KeyNotFoundException(SymbolLocatorFactory.FormatSymbolNotFoundMessage(locator));
            return JsonSerializer.Serialize(result, JsonDefaults.Indented);
        }, ct);
    }
}
