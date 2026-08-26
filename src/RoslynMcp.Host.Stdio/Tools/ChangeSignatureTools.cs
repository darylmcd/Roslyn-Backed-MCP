using System.ComponentModel;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Roslyn.Contracts;

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
        "Preview adding/removing/renaming/reordering a method parameter with all callsites updated atomically."),
     Description("Preview adding, removing, renaming, or reordering a method's parameters: the declaration and every callsite are rewritten under one preview token. Supply op plus its name/newName/parameterType/newOrder arguments.")]
    public static Task<string> PreviewChangeSignature(
        IWorkspaceExecutionGate gate,
        IChangeSignatureService changeSignatureService,
        [Description("Workspace session id from workspace_load.")] string workspaceId,
        [Description("Operation: 'add', 'remove', 'rename', or 'reorder'.")] string op,
        [Description("Optional: absolute path to the source file containing the method declaration")] string? filePath = null,
        [Description("Optional: 1-based line number of the method declaration")] int? line = null,
        [Description("Optional: 1-based column number of the method declaration")] int? column = null,
        [Description("Optional: stable symbol handle returned by other semantic tools")] string? symbolHandle = null,
        [Description("Optional: bare fully-qualified method name without parentheses (e.g. 'Foo.Bar.Baz'). For overloaded methods supply file/line/column to disambiguate, or use symbolHandle from symbol_search. Parenthesized signatures like 'Foo.Bar.Baz(string)' are NOT accepted.")] string? metadataName = null,
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
