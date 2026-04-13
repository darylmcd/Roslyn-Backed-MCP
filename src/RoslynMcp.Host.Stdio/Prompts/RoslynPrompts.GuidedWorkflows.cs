using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Catalog;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Prompts;

public static partial class RoslynPrompts
{
    [McpServerPrompt(Name = "guided_extract_method")]
    [Description("Generate a prompt for extract-method refactoring with data-flow and control-flow checks.")]
    public static async Task<IEnumerable<PromptMessage>> GuidedExtractMethod(
        IWorkspaceManager workspace,
        [Description("The workspace session identifier")] string workspaceId,
        [Description("Absolute path to the source file")] string filePath,
        [Description("1-based start line of the selection")] int startLine,
        [Description("1-based start column of the selection")] int startColumn,
        [Description("1-based end line of the selection")] int endLine,
        [Description("1-based end column of the selection")] int endColumn,
        [Description("Name for the new method")] string methodName,
        CancellationToken ct = default)
    {
        try
        {
            var sourceText = await workspace.GetSourceTextAsync(workspaceId, filePath, ct).ConfigureAwait(false);
            if (sourceText is null)
                return [PromptMessageBuilder.CreatePromptMessage($"File not found in workspace: {filePath}")];

            var context = PromptMessageBuilder.FormatSourceContext(sourceText, startLine, endLine);

            return
            [
                PromptMessageBuilder.CreatePromptMessage($"""
                    Extract the selected statements into a new method named `{methodName}` using the Roslyn MCP extract-method workflow.

                    **File:** {filePath}
                    **Selection:** {startLine}:{startColumn} - {endLine}:{endColumn}

                    **Source context:**
                    ```csharp
                    {context}
                    ```

                    Use this workflow:
                    1. Call `analyze_data_flow` on the same line/column range to see variables read, written, or captured — the selection should be a coherent region; multiple exits (`return`/`throw`) inside the span may block extraction.
                    2. Call `analyze_control_flow` on the range to confirm suitability (single-entry / exit expectations for the snippet).
                    3. Call `extract_method_preview` with `workspaceId`, `filePath`, `startLine`, `startColumn`, `endLine`, `endColumn`, and `methodName`. Show inferred parameters and return value to the user.
                    4. If the preview is acceptable, call `extract_method_apply` with the preview token.
                    5. Run `compile_check` or `build_project` for the owning project after apply.
                    6. If apply was wrong, offer `revert_last_apply`, adjust the selection, and preview again.

                    Complete one extraction and verify compilation before starting another refactoring.
                    """)
            ];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return [PromptMessageBuilder.CreateErrorMessage("guided_extract_method", ex)];
        }
    }

    [McpServerPrompt(Name = "msbuild_inspection")]
    [Description("Generate a prompt for evaluating MSBuild properties and items for a project file.")]
    public static Task<IEnumerable<PromptMessage>> MsbuildInspection(
        [Description("The workspace session identifier")] string workspaceId,
        [Description("Absolute path to the .csproj or project file")] string projectFilePath,
        [Description("Optional: a single property name to evaluate (e.g. TargetFramework, OutputPath)")] string? propertyName = null,
        [Description("Optional: MSBuild item type to list (e.g. Compile, PackageReference)")] string? itemType = null)
    {
        var propertyNote = string.IsNullOrWhiteSpace(propertyName)
            ? "For specific property values, call `evaluate_msbuild_property` with the project path and property name."
            : $"Call `evaluate_msbuild_property` for `{propertyName}` on this project first.";

        var itemNote = string.IsNullOrWhiteSpace(itemType)
            ? "For evaluated includes and metadata, call `evaluate_msbuild_items` with the item type you need (e.g. `Compile`, `PackageReference`)."
            : $"Call `evaluate_msbuild_items` with item type `{itemType}`.";

        IEnumerable<PromptMessage> result =
        [
            PromptMessageBuilder.CreatePromptMessage($"""
                Inspect MSBuild evaluation for this project using Roslyn MCP tools.

                **workspaceId:** `{workspaceId}`
                **Project file:** `{projectFilePath}`

                Workflow:
                1. {propertyNote}
                2. For a broad dump of evaluated properties, call `get_msbuild_properties` (large output — focus on what the user asked).
                3. {itemNote}
                4. If results disagree with `dotnet build` or the IDE, confirm the correct project file, multi-targeting, and imports from `Directory.Build.props` / `Directory.Packages.props`.
                5. After editing project files, call `workspace_reload` before relying on compilation or symbol tools.

                Treat this as inspection and troubleshooting unless the user explicitly asked to mutate project files.
                """)
        ];

        return Task.FromResult(result);
    }

    [McpServerPrompt(Name = "session_undo")]
    [Description("Generate a prompt for inspecting session mutations and undoing the last apply operation.")]
    public static Task<IEnumerable<PromptMessage>> SessionUndo(
        [Description("The workspace session identifier")] string workspaceId)
    {
        IEnumerable<PromptMessage> result =
        [
            PromptMessageBuilder.CreatePromptMessage($"""
                Manage undo and session history for workspace `{workspaceId}`.

                Steps:
                1. Call `workspace_changes` to list mutations applied in this session (tools, files, timestamps).
                2. To undo only the **most recent** Roslyn solution-level apply, call `revert_last_apply` for this `workspaceId`.
                3. If files changed on disk outside MCP or the workspace is stale, call `workspace_reload` when safe.
                4. To tear down the session, call `workspace_close` and reload with `workspace_load` if needed.

                `revert_last_apply` reverses one apply — not arbitrary history. Use git or manual edits for broader rollback.

                After revert or reload, run `compile_check` or `build_workspace` to verify the tree.
                """)
        ];

        return Task.FromResult(result);
    }
}

