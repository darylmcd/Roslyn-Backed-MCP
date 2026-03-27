using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class FileOperationTools
{

    [McpServerTool(Name = "create_file_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Preview creating a new source file inside a loaded workspace project. Returns the file diff and a preview token.")]
    public static Task<string> PreviewCreateFile(
        McpServer server,
        IWorkspaceExecutionGate gate,
        IFileOperationService fileOperationService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Project name or project file path within the loaded workspace")] string projectName,
        [Description("Absolute path to the source file to create")] string filePath,
        [Description("Initial file contents")] string content,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                await ClientRootPathValidator.ValidatePathAgainstRootsAsync(server, filePath, c).ConfigureAwait(false);
                var result = await fileOperationService.PreviewCreateFileAsync(workspaceId, new CreateFileDto(projectName, filePath, content), c).ConfigureAwait(false);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "create_file_apply", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     Description("Apply a previously previewed file creation using its preview token.")]
    public static Task<string> ApplyCreateFile(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        IPreviewStore previewStore,
        [Description("The preview token returned by create_file_preview")] string previewToken,
        CancellationToken ct = default)
    {
        var gateKey = RefactoringTools.ApplyGateKeyFor(previewStore, previewToken);
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(gateKey, async c =>
            {
                var result = await refactoringService.ApplyRefactoringAsync(previewToken, c).ConfigureAwait(false);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "delete_file_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Preview deleting an existing source file from a loaded workspace. Returns the file diff and a preview token.")]
    public static Task<string> PreviewDeleteFile(
        McpServer server,
        IWorkspaceExecutionGate gate,
        IFileOperationService fileOperationService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the source file to delete")] string filePath,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                await ClientRootPathValidator.ValidatePathAgainstRootsAsync(server, filePath, c).ConfigureAwait(false);
                var result = await fileOperationService.PreviewDeleteFileAsync(workspaceId, new DeleteFileDto(filePath), c).ConfigureAwait(false);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "delete_file_apply", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     Description("Apply a previously previewed file deletion using its preview token.")]
    public static Task<string> ApplyDeleteFile(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        IPreviewStore previewStore,
        [Description("The preview token returned by delete_file_preview")] string previewToken,
        CancellationToken ct = default)
    {
        var gateKey = RefactoringTools.ApplyGateKeyFor(previewStore, previewToken);
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(gateKey, async c =>
            {
                var result = await refactoringService.ApplyRefactoringAsync(previewToken, c).ConfigureAwait(false);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "move_file_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Preview moving an existing source file, optionally updating its namespace to match the destination folder.")]
    public static Task<string> PreviewMoveFile(
        McpServer server,
        IWorkspaceExecutionGate gate,
        IFileOperationService fileOperationService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the existing source file")] string sourceFilePath,
        [Description("Absolute path to the destination source file")] string destinationFilePath,
        [Description("Optional: destination project name or project file path; defaults to the source project")] string? destinationProjectName = null,
        [Description("When true, updates the namespace declaration in the moved file to match the destination folder")] bool updateNamespace = false,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                await ClientRootPathValidator.ValidatePathAgainstRootsAsync(server, sourceFilePath, c).ConfigureAwait(false);
                await ClientRootPathValidator.ValidatePathAgainstRootsAsync(server, destinationFilePath, c).ConfigureAwait(false);
                var result = await fileOperationService.PreviewMoveFileAsync(
                    workspaceId,
                    new MoveFileDto(sourceFilePath, destinationFilePath, destinationProjectName, updateNamespace),
                    c).ConfigureAwait(false);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "move_file_apply", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     Description("Apply a previously previewed file move using its preview token.")]
    public static Task<string> ApplyMoveFile(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        IPreviewStore previewStore,
        [Description("The preview token returned by move_file_preview")] string previewToken,
        CancellationToken ct = default)
    {
        var gateKey = RefactoringTools.ApplyGateKeyFor(previewStore, previewToken);
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(gateKey, async c =>
            {
                var result = await refactoringService.ApplyRefactoringAsync(previewToken, c).ConfigureAwait(false);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }
}