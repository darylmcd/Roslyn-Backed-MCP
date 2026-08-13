using System.ComponentModel;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// <c>parameter-object-preview-tool</c> v1 (experimental): groups N parameters of a
/// method into a positional <c>sealed record</c> and rewrites every call site to flow
/// the grouped argument values through the new record's primary constructor. Redeem the
/// returned preview token through <c>apply_with_verify</c>. See
/// <c>ai_docs/items/parameter-object-preview-design.md</c> for the contract.
/// </summary>
[McpServerToolType]
public static class ParameterObjectTools
{
    [McpServerTool(Name = "parameter_object_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     McpToolMetadata("refactoring", "experimental", true, false,
        "Preview grouping N parameters of a method into a positional sealed record DTO with all callsites updated atomically."),
     Description("Preview replacing N parameters of a method with a single record DTO. Synthesizes a `public sealed record NewType(...)` (or `internal` when same-project + non-public containing type) and rewrites every call site so grouped arguments flow through `new NewType(...)`. Refuses (does not partially proceed) for: default-value omissions at any call site, ref/out/in/params parameters, the extension-method `this` receiver, local-function targets, and cross-project call sites where the caller project does not already reference the DTO project. Redeem the returned preview token via apply_with_verify. v1 always emits a positional record; class/struct generation is out of scope.")]
    public static Task<string> PreviewParameterObject(
        IWorkspaceExecutionGate gate,
        IParameterObjectService parameterObjectService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Names of method parameters to group, in the order they should appear on the new record's primary constructor. Must contain at least two entries.")] string[] parameterNames,
        [Description("PascalCase name of the new record type (e.g. 'OrderRequest').")] string newTypeName,
        [Description("Optional: absolute path to the source file containing the method declaration")] string? filePath = null,
        [Description("Optional: 1-based line number of the method declaration")] int? line = null,
        [Description("Optional: 1-based column number of the method declaration")] int? column = null,
        [Description("Optional: stable symbol handle returned by other semantic tools")] string? symbolHandle = null,
        [Description("Optional: bare fully-qualified method name without parentheses (e.g. 'Foo.Bar.Baz').")] string? metadataName = null,
        [Description("Optional override project for the new record. Defaults to the method's project. When different, every caller project must already reference it (no auto-add of <ProjectReference> in v1).")] string? dtoProjectName = null,
        [Description("Optional namespace for the new record. Defaults to the method's containing namespace.")] string? dtoNamespace = null,
        [Description("Optional folder segments under the project root for the new record file. Pass as a native JSON array, not a JSON-encoded string. Example: [\"Models\", \"Requests\"]. Defaults to folders derived from the namespace.")] string[]? dtoFolders = null,
        [Description("Optional name for the new single record-typed parameter on the rewritten method. Defaults to the camelCased newTypeName.")] string? parameterName = null,
        CancellationToken ct = default)
    {
        return ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            async c =>
            {
                var locator = new SymbolLocator(filePath, line, column, symbolHandle, metadataName);
                locator.Validate();
                var request = new ParameterObjectPreviewRequest(
                    parameterNames,
                    newTypeName,
                    dtoProjectName,
                    dtoNamespace,
                    dtoFolders,
                    parameterName);
                return await parameterObjectService
                    .PreviewParameterObjectAsync(workspaceId, locator, request, c)
                    .ConfigureAwait(false);
            },
            ct);
    }
}
