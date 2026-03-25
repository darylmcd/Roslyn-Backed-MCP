using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class CrossProjectRefactoringTools
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool(Name = "move_type_to_project_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Preview moving a C# type declaration into another project in the loaded workspace.")]
    public static Task<string> PreviewMoveTypeToProject(
        IWorkspaceExecutionGate gate,
        ICrossProjectRefactoringService crossProjectRefactoringService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the source file containing the type")] string sourceFilePath,
        [Description("Name of the type declaration to move")] string typeName,
        [Description("Target project name or project file path")] string targetProjectName,
        [Description("Optional: explicit target namespace. Current implementation supports the original namespace only")] string? targetNamespace = null,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var result = await crossProjectRefactoringService.PreviewMoveTypeToProjectAsync(
                    workspaceId,
                    sourceFilePath,
                    typeName,
                    targetProjectName,
                    targetNamespace,
                    c).ConfigureAwait(false);
                return JsonSerializer.Serialize(result, JsonOptions);
            }, ct));
    }

    [McpServerTool(Name = "extract_interface_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Preview extracting an interface from a type, optionally into another project in the loaded workspace.")]
    public static Task<string> PreviewExtractInterface(
        IWorkspaceExecutionGate gate,
        ICrossProjectRefactoringService crossProjectRefactoringService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the source file containing the type")] string filePath,
        [Description("Name of the type declaration to extract from")] string typeName,
        [Description("Optional: interface name. Defaults to I + type name")] string? interfaceName = null,
        [Description("Optional: target project name or project file path")] string? targetProjectName = null,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var result = await crossProjectRefactoringService.PreviewExtractInterfaceAsync(
                    workspaceId,
                    filePath,
                    typeName,
                    interfaceName,
                    targetProjectName,
                    c).ConfigureAwait(false);
                return JsonSerializer.Serialize(result, JsonOptions);
            }, ct));
    }

    [McpServerTool(Name = "dependency_inversion_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Preview extracting an interface and updating constructor dependencies to use it across the loaded workspace.")]
    public static Task<string> PreviewDependencyInversion(
        IWorkspaceExecutionGate gate,
        ICrossProjectRefactoringService crossProjectRefactoringService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the source file containing the concrete type")] string filePath,
        [Description("Name of the concrete type to invert dependencies around")] string typeName,
        [Description("Target project name or project file path for the extracted interface")] string interfaceProjectName,
        [Description("Optional: interface name. Defaults to I + type name")] string? interfaceName = null,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var result = await crossProjectRefactoringService.PreviewDependencyInversionAsync(
                    workspaceId,
                    filePath,
                    typeName,
                    interfaceName,
                    interfaceProjectName,
                    c).ConfigureAwait(false);
                return JsonSerializer.Serialize(result, JsonOptions);
            }, ct));
    }
}