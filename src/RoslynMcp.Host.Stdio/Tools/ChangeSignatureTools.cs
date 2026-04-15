using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Host.Stdio.Catalog;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// Item 3 (v1.18, <c>change-signature-add-parameter-cross-callsite</c>): add / remove /
/// rename a method parameter and update every callsite atomically. Composite preview that
/// rewrites both the declaration and every <see cref="Microsoft.CodeAnalysis.IMethodSymbol"/>
/// caller's argument list.
/// </summary>
[McpServerToolType]
public static class ChangeSignatureTools
{
    [McpServerTool(Name = "change_signature_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     McpToolMetadata("refactoring", "experimental", true, false,
        "Preview adding/removing/renaming a method parameter with all callsites updated atomically."),
     Description("Preview a change to a method's signature: add, remove, or rename a parameter. The declaration AND every callsite are rewritten in one preview token. For 'add', supply Name + ParameterType + DefaultValue (default value is spliced into every existing callsite). For 'remove', supply Name OR Position. For 'rename', supply Name (current) + NewName. Reorder is reserved for a future release.")]
    public static Task<string> PreviewChangeSignature(
        IWorkspaceExecutionGate gate,
        IChangeSignatureService changeSignatureService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Operation: 'add', 'remove', 'rename', or 'reorder' (reorder errors out — reserved for follow-up release).")] string op,
        [Description("Optional: absolute path to the source file containing the method declaration")] string? filePath = null,
        [Description("Optional: 1-based line number of the method declaration")] int? line = null,
        [Description("Optional: 1-based column number of the method declaration")] int? column = null,
        [Description("Optional: stable symbol handle returned by other semantic tools")] string? symbolHandle = null,
        [Description("Optional: fully qualified metadata name of the method")] string? metadataName = null,
        [Description("Parameter name. For op='add': the new parameter's name. For op='remove': the existing parameter to drop (or use position). For op='rename': the current name.")] string? name = null,
        [Description("op='rename' only: the new parameter name.")] string? newName = null,
        [Description("op='add' only: the parameter type (e.g. 'string', 'IReadOnlyList<int>', 'CancellationToken').")] string? parameterType = null,
        [Description("op='add' only: the default value spliced into every existing callsite (e.g. 'null', 'default', '\"\"', '0').")] string? defaultValue = null,
        [Description("Optional position (0-based) of the parameter. For op='add': insertion index (defaults to trailing). For op='remove': index to drop.")] int? position = null,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync("change_signature_preview", () =>
            gate.RunReadAsync(workspaceId, async c =>
            {
                var locator = new SymbolLocator(filePath, line, column, symbolHandle, metadataName);
                locator.Validate();
                var request = new ChangeSignatureRequest(op, name, newName, parameterType, defaultValue, position);
                var dto = await changeSignatureService.PreviewChangeSignatureAsync(workspaceId, locator, request, c).ConfigureAwait(false);
                return JsonSerializer.Serialize(dto, JsonDefaults.Indented);
            }, ct));
    }
}
