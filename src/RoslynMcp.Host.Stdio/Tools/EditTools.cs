using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class EditTools
{

    [McpServerTool(Name = "apply_text_edit", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     McpToolMetadata("editing", "stable", false, true,
        "Apply direct text edits to a single file."),
     Description("Apply one or more text edits to a source file in the workspace. Each edit specifies a range (start/end line and column) and the replacement text. The workspace is updated in-place and a diff is returned. Revertible via revert_last_apply (one snapshot per call, single-slot per workspace). Prefer a semantic preview/apply Roslyn tool whenever one exists; only fall back to apply_text_edit when no semantic equivalent is available.")]
    public static Task<string> ApplyTextEdit(
        McpServer server,
        IWorkspaceExecutionGate gate,
        IEditService editService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the source file to edit")] string filePath,
        [Description("Array of text edits. Each edit has startLine, startColumn, endLine, endColumn (1-based), and newText. Example: [{\"startLine\":10,\"startColumn\":5,\"endLine\":10,\"endColumn\":15,\"newText\":\"newValue\"}]. All positions are 1-based inclusive; non-overlapping edits are applied in the order given.")] TextEditDto[] edits,
        CancellationToken ct = default,
        [Description("When false (default), C# files are parsed after edits and lexer/parser errors block the apply. Set true only for intentional intermediate invalid states.")] bool skipSyntaxCheck = false)
    {
        return gate.RunWriteAsync(workspaceId, async c =>
        {
            await ClientRootPathValidator.ValidatePathAgainstRootsAsync(server, filePath, c).ConfigureAwait(false);
            var result = await editService.ApplyTextEditsAsync(workspaceId, filePath, edits, c, skipSyntaxCheck);
            return JsonSerializer.Serialize(result, JsonDefaults.Indented);
        }, ct);
    }
}
