using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// MCP tool entry points for file-operation refactorings (create / delete / move).
/// WS1 phase 1.6 — the three <c>Apply*</c> methods (pure dispatch) delegate to
/// <see cref="ToolDispatch.ApplyByTokenAsync{TDto}(IWorkspaceExecutionGate, IPreviewStore, string, Func{CancellationToken, Task{TDto}}, CancellationToken)"/>;
/// the <c>Preview*</c> methods keep their hand-written bodies because they
/// perform async <see cref="ClientRootPathValidator.ValidatePathAgainstRootsAsync"/>
/// calls inside the gate before dispatching to the service (validation must run
/// under the gate's cancellation token — the helper's single-lambda shape doesn't
/// fit). See <c>CodeActionTools</c> (canary, PR #305) and
/// <c>ai_docs/plans/20260421T123658Z_post-audit-followups.md</c> for the migration
/// rationale and the deferred-generator blocker.
/// </summary>
[McpServerToolType]
public static class FileOperationTools
{

    [McpServerTool(Name = "create_file_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     McpToolMetadata("file-operations", "stable", true, false,
        "Preview creating a new source file in a project."),
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
        return gate.RunReadAsync(workspaceId, async c =>
        {
            await ClientRootPathValidator.ValidatePathAgainstRootsAsync(server, filePath, c).ConfigureAwait(false);
            var result = await fileOperationService.PreviewCreateFileAsync(workspaceId, new CreateFileDto(projectName, filePath, content), c).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "create_file_apply", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     McpToolMetadata("file-operations", "experimental", false, true,
        "Apply a previously previewed file creation."),
     Description("Apply a previously previewed file creation using its preview token.")]
    public static Task<string> ApplyCreateFile(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        IPreviewStore previewStore,
        [Description("The preview token returned by create_file_preview")] string previewToken,
        CancellationToken ct = default)
        => ToolDispatch.ApplyByTokenAsync(
            gate,
            previewStore,
            previewToken,
            c => refactoringService.ApplyRefactoringAsync(previewToken, "create_file_apply", c),
            ct);

    [McpServerTool(Name = "delete_file_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     McpToolMetadata("file-operations", "stable", true, false,
        "Preview deleting an existing source file."),
     Description("Preview deleting an existing source file from a loaded workspace. Returns the file diff and a preview token.")]
    public static Task<string> PreviewDeleteFile(
        McpServer server,
        IWorkspaceExecutionGate gate,
        IFileOperationService fileOperationService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the source file to delete")] string filePath,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            await ClientRootPathValidator.ValidatePathAgainstRootsAsync(server, filePath, c).ConfigureAwait(false);
            var result = await fileOperationService.PreviewDeleteFileAsync(workspaceId, new DeleteFileDto(filePath), c).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "delete_file_apply", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     McpToolMetadata("file-operations", "experimental", false, true,
        "Apply a previously previewed file deletion."),
     Description("Apply a previously previewed file deletion using its preview token.")]
    public static Task<string> ApplyDeleteFile(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        IPreviewStore previewStore,
        [Description("The preview token returned by delete_file_preview")] string previewToken,
        CancellationToken ct = default)
        => ToolDispatch.ApplyByTokenAsync(
            gate,
            previewStore,
            previewToken,
            c => refactoringService.ApplyRefactoringAsync(previewToken, "delete_file_apply", c),
            ct);

    [McpServerTool(Name = "move_file_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     McpToolMetadata("file-operations", "stable", true, false,
        "Preview moving a source file, optionally updating its namespace."),
     Description("Preview moving an existing source file, optionally updating its namespace to match the destination folder.")]
    public static Task<string> PreviewMoveFile(
        McpServer server,
        IWorkspaceExecutionGate gate,
        IFileOperationService fileOperationService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the existing source file")] string sourceFilePath,
        [Description("Absolute path to the destination source file")] string targetFilePath,
        [Description("Optional: destination project name or project file path; defaults to the source project")] string? destinationProjectName = null,
        [Description("When true, updates the namespace declaration in the moved file to match the destination folder")] bool updateNamespace = false,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            await ClientRootPathValidator.ValidatePathAgainstRootsAsync(server, sourceFilePath, c).ConfigureAwait(false);
            await ClientRootPathValidator.ValidatePathAgainstRootsAsync(server, targetFilePath, c).ConfigureAwait(false);
            var result = await fileOperationService.PreviewMoveFileAsync(
                workspaceId,
                new MoveFileDto(sourceFilePath, targetFilePath, destinationProjectName, updateNamespace),
                c).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "move_file_apply", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     McpToolMetadata("file-operations", "experimental", false, true,
        "Apply a previously previewed file move."),
     Description("Apply a previously previewed file move using its preview token.")]
    public static Task<string> ApplyMoveFile(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        IPreviewStore previewStore,
        [Description("The preview token returned by move_file_preview")] string previewToken,
        CancellationToken ct = default)
        => ToolDispatch.ApplyByTokenAsync(
            gate,
            previewStore,
            previewToken,
            c => refactoringService.ApplyRefactoringAsync(previewToken, "move_file_apply", c),
            ct);
}
