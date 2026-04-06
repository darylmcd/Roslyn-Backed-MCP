using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class TypeExtractionTools
{

    [McpServerTool(Name = "extract_type_preview", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
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
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunReadAsync(workspaceId, async c =>
            {
                var result = await typeExtractionService.PreviewExtractTypeAsync(
                    workspaceId, filePath, typeName, memberNames, newTypeName, newFilePath, c);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "extract_type_apply", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     Description("Apply a previously previewed type extraction")]
    public static Task<string> ApplyExtractType(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        IPreviewStore previewStore,
        [Description("The preview token returned by extract_type_preview")] string previewToken,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
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
