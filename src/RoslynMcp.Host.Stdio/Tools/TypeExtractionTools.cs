using System.ComponentModel;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// MCP tool entry points for Roslyn type-extraction refactorings. WS1 phase 1.4 —
/// each shim body delegates to the corresponding <see cref="ToolDispatch"/> helper
/// instead of carrying the 7-line dispatch boilerplate inline. See
/// <c>CodeActionTools</c> (canary, PR #305), <c>BulkRefactoringTools</c> (phase 1.3),
/// and <c>ai_docs/plans/20260421T123658Z_post-audit-followups.md</c> for the migration
/// rationale and the deferred-generator blocker.
/// </summary>
[McpServerToolType]
public static class TypeExtractionTools
{
    [McpServerTool(Name = "extract_type_preview", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("refactoring", "stable", true, false,
        "Preview extracting selected members from a type into a new type. Adds a private field and constructor parameter for composition. Use get_cohesion_metrics and find_shared_members to plan the extraction."),
     Description("Preview extracting selected members from a type into a new type. The source type gets a private field for the new type and the extracted members are moved. Use this for SRP refactoring.")]
    public static Task<string> PreviewExtractType(
        IWorkspaceExecutionGate gate,
        ITypeExtractionService typeExtractionService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the source file containing the type")] string filePath,
        [Description("Name of the source type to extract from")] string typeName,
        [Description("Names of members to extract into the new type")] string[] memberNames,
        [Description("Name for the new type")] string newTypeName,
        [Description("Optional: target file path for the new type. If omitted, defaults to {NewTypeName}.cs in the same directory")] string? newFilePath = null,
        CancellationToken ct = default)
        => ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            c => typeExtractionService.PreviewExtractTypeAsync(
                workspaceId, filePath, typeName, memberNames, newTypeName, newFilePath, c),
            ct);

    [McpServerTool(Name = "extract_type_apply", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     McpToolMetadata("refactoring", "experimental", false, true,
        "Apply a previewed type extraction. Moves members to the new type file and wires composition in the source type."),
     Description("Apply a previously previewed type extraction")]
    public static Task<string> ApplyExtractType(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        IPreviewStore previewStore,
        [Description("The preview token returned by extract_type_preview")] string previewToken,
        CancellationToken ct = default)
        => ToolDispatch.ApplyByTokenAsync(
            gate,
            previewStore,
            previewToken,
            c => refactoringService.ApplyRefactoringAsync(previewToken, "extract_type_apply", c),
            ct);
}
