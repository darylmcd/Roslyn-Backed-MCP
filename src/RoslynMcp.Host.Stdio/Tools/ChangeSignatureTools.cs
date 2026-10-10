using System.ComponentModel;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Roslyn.Contracts;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// Preview method parameter changes and class/struct primary-constructor add/remove.
/// Bound declarations and callsites are verified together before storing a token.
/// </summary>
[McpServerToolType]
public static class ChangeSignatureTools
{
    /// <remarks>
    /// The preview changes the declaration and every call site together. Choose <c>op</c> and
    /// supply the matching operation-specific arguments before redeeming the returned token.
    /// Invalid declarations, remaining parameter references, and changed retained bindings are refused.
    /// </remarks>
    [McpServerTool(Name = "change_signature_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     McpToolMetadata("refactoring", "experimental", true, false,
        "Preview method-parameter changes and class/struct primary-constructor add/remove with bound callsites updated atomically."),
     Description("Preview method parameters or class/struct primary-constructor add/remove; updates every callsite under one preview token. Records and primary-constructor rename/reorder are unsupported.")]
    public static Task<string> PreviewChangeSignature(
        IWorkspaceExecutionGate gate,
        IChangeSignatureService changeSignatureService,
        [Description("Workspace session id from workspace_load.")] string workspaceId,
        [Description("Operation: 'add', 'remove', 'rename', or 'reorder'.")] string op,
        [Description("Optional: absolute path to the source file containing the method, constructor, type declaration, or parameter")] string? filePath = null,
        [Description("Optional: 1-based line number of the target declaration or parameter")] int? line = null,
        [Description("Optional: 1-based column number of the target declaration or parameter")] int? column = null,
        [Description("Optional: stable symbol handle returned by other semantic tools")] string? symbolHandle = null,
        [Description("Optional: bare fully-qualified method or primary-constructor type name without parentheses (e.g. 'Foo.Bar.Baz'). For overloaded methods supply file/line/column to disambiguate, or use symbolHandle from symbol_search. Parenthesized signatures like 'Foo.Bar.Baz(string)' are NOT accepted.")] string? metadataName = null,
        [Description("Parameter name. For op='add': the new parameter's name. For op='remove': the existing parameter to drop (or use position). For op='rename': the current name.")] string? name = null,
        [Description("op='rename' only: the new parameter name.")] string? newName = null,
        [Description("op='add' only: the parameter type (e.g. 'string', 'IReadOnlyList<int>', 'CancellationToken').")] string? parameterType = null,
        [Description("op='add' only: the default value spliced into every existing callsite (e.g. 'null', 'default', '\"\"', '0').")] string? defaultValue = null,
        [Description("Optional position (0-based) of the parameter. For op='add': insertion index (defaults to trailing). For op='remove': index to drop.")] int? position = null,
        [Description("op='reorder' only: comma-separated permutation of parameter names or 0-based indices (e.g. 'b,a,c' or '1,0,2'). Must list every parameter exactly once.")] string? newOrder = null,
        CancellationToken ct = default)
    {
        return ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            async c =>
            {
                var locator = new SymbolLocator(filePath, line, column, symbolHandle, metadataName);
                locator.Validate();
                var request = new ChangeSignatureRequest(op, name, newName, parameterType, defaultValue, position, newOrder);
                return await changeSignatureService
                    .PreviewChangeSignatureAsync(workspaceId, locator, request, c)
                    .ConfigureAwait(false);
            },
            ct);
    }
}
