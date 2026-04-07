using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class OrchestrationTools
{

    [McpServerTool(Name = "migrate_package_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Preview migrating one package dependency to another across all affected projects in the loaded workspace.")]
    public static Task<string> PreviewMigratePackage(
        IWorkspaceExecutionGate gate,
        IOrchestrationService orchestrationService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Existing package id to replace")] string oldPackageId,
        [Description("Replacement package id")] string newPackageId,
        [Description("Replacement package version")] string newVersion,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync("migrate_package_preview", () =>
            gate.RunReadAsync(workspaceId, async c =>
            {
                var result = await orchestrationService.PreviewMigratePackageAsync(workspaceId, oldPackageId, newPackageId, newVersion, c).ConfigureAwait(false);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "split_class_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Preview splitting a type into a new partial class file by moving selected members.")]
    public static Task<string> PreviewSplitClass(
        IWorkspaceExecutionGate gate,
        IOrchestrationService orchestrationService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the source file containing the type")] string filePath,
        [Description("Type name to split")] string typeName,
        [Description("Member names to move into the new partial class file")] string[] memberNames,
        [Description("File name for the new partial class file, for example Dog.Behavior.cs")] string newFileName,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync("split_class_preview", () =>
            gate.RunReadAsync(workspaceId, async c =>
            {
                var result = await orchestrationService.PreviewSplitClassAsync(workspaceId, filePath, typeName, memberNames, newFileName, c).ConfigureAwait(false);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "extract_and_wire_interface_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Preview extracting an interface and optionally rewriting DI registrations to use it.")]
    public static Task<string> PreviewExtractAndWireInterface(
        IWorkspaceExecutionGate gate,
        IOrchestrationService orchestrationService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the source file containing the concrete type")] string filePath,
        [Description("Name of the concrete type to extract from")] string typeName,
        [Description("Target project name or project file path for the interface")] string targetProjectName,
        [Description("Optional: explicit interface name. Defaults to I + type name")] string? interfaceName = null,
        [Description("Whether to rewrite matching DI registrations to use the extracted interface")] bool updateDiRegistrations = true,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync("extract_and_wire_interface_preview", () =>
            gate.RunReadAsync(workspaceId, async c =>
            {
                var result = await orchestrationService.PreviewExtractAndWireInterfaceAsync(
                    workspaceId,
                    filePath,
                    typeName,
                    interfaceName,
                    targetProjectName,
                    updateDiRegistrations,
                    c).ConfigureAwait(false);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "apply_composite_preview", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     Description("Apply a previously previewed orchestration operation using its preview token.")]
    public static Task<string> ApplyCompositePreview(
        IWorkspaceExecutionGate gate,
        IOrchestrationService orchestrationService,
        ICompositePreviewStore compositePreviewStore,
        [Description("The preview token returned by an orchestration preview tool")] string previewToken,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync("apply_composite_preview", () =>
        {
            var wsId = compositePreviewStore.PeekWorkspaceId(previewToken)
                ?? throw new KeyNotFoundException($"Preview token '{previewToken}' not found or expired.");
            return gate.RunWriteAsync(wsId, async c =>
            {
                var result = await orchestrationService.ApplyCompositeAsync(previewToken, c).ConfigureAwait(false);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct);
        });
    }
}
