using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class InterfaceExtractionTools
{

    [McpServerTool(Name = "extract_interface_preview", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("refactoring", "experimental", true, false,
        "Preview extracting an interface from a concrete type within the same project. Optionally replaces concrete type references with the interface."),
     Description("Preview extracting an interface from a concrete type. Creates a new interface file with selected member signatures, adds it to the type's base list, and optionally replaces concrete type references with the interface.")]
    public static Task<string> PreviewExtractInterface(
        IWorkspaceExecutionGate gate,
        IInterfaceExtractionService interfaceExtractionService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the source file containing the type")] string filePath,
        [Description("Name of the concrete type to extract from")] string typeName,
        [Description("Name for the new interface (e.g., IMyService)")] string interfaceName,
        [Description("Optional: specific member names to include. If omitted, all public instance members are included.")] string[]? memberNames = null,
        [Description("If true, replace parameter and field references to the concrete type with the interface (default: false)")] bool replaceUsages = false,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync("extract_interface_preview", () =>
            gate.RunReadAsync(workspaceId, async c =>
            {
                var result = await interfaceExtractionService.PreviewExtractInterfaceAsync(
                    workspaceId, filePath, typeName, interfaceName, memberNames, replaceUsages, c);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "extract_interface_apply", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     McpToolMetadata("refactoring", "experimental", false, true,
        "Apply a previewed interface extraction. Creates the interface file, updates the type's base list, and applies usage replacements if requested."),
     Description("Apply a previously previewed interface extraction")]
    public static Task<string> ApplyExtractInterface(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        IPreviewStore previewStore,
        [Description("The preview token returned by extract_interface_preview")] string previewToken,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync("extract_interface_apply", () =>
        {
            var wsId = previewStore.PeekWorkspaceId(previewToken)
                ?? throw new KeyNotFoundException($"Preview token '{previewToken}' not found or expired.");
            return gate.RunWriteAsync(wsId, async c =>
            {
                var result = await refactoringService.ApplyRefactoringAsync(previewToken, c);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct);
        });
    }
}
