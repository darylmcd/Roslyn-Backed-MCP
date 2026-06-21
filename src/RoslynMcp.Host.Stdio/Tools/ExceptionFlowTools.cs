using System.ComponentModel;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// Surfaces <see cref="IExceptionFlowService"/> as the <c>trace_exception_flow</c> MCP tool —
/// enumerates catch sites whose declared exception type is assignable from an input type so
/// error-handling refactors can discover handling sites (<c>find_references</c> returns
/// usage sites, not handling sites). WS1 phase 1.5 — body delegates to
/// <see cref="ToolDispatch.ReadByWorkspaceIdAsync"/> instead of carrying the dispatch
/// boilerplate inline.
/// </summary>
[McpServerToolType]
public static class ExceptionFlowTools
{
    [McpServerTool(Name = "trace_exception_flow", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("advanced-analysis", "experimental", true, false,
        "Trace catch clauses that handle a given exception type."),
     Description("Return both `catch` clauses (handling sites) and `throw new T(...)` sites (origination sites) across the workspace related to the input exception type. Catch sites whose declared type is assignable from the input carry the containing method, a body excerpt (with the `when` filter prepended when present), and a `rethrowAsTypeMetadataName` annotation when the body wraps the exception in `throw new X(...)` of a different type; catch sites are ranked type-specific (exact-match) first so a broad `catch (Exception)` never displaces a precise handler when results are truncated. Throw sites whose thrown type is assignable to the input carry the containing method, an `isUnhandledAtBoundary` flag (syntactic — true when the throw is not lexically inside a `catch`), and an excerpt. `countOmitted` reports how many catch+throw sites were dropped past `maxResults`. Untyped `catch { }` clauses are treated as catching `System.Exception`. Pair handling and origination sites for an exception-classification refactor.")]
    public static Task<string> TraceExceptionFlow(
        IWorkspaceExecutionGate gate,
        IExceptionFlowService exceptionFlowService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Fully qualified metadata name of the exception type to trace, e.g. 'System.Text.Json.JsonException' or 'System.ArgumentException'")] string exceptionTypeMetadataName,
        [Description("Optional: restrict the walk to a specific project name")] string? projectName = null,
        [Description("Optional: cap the returned catch-site list. Defaults to 200; absolute upper bound is 2000 to keep the payload bounded.")] int? maxResults = null,
        CancellationToken ct = default)
        => ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            c => exceptionFlowService.TraceExceptionFlowAsync(
                workspaceId, exceptionTypeMetadataName, projectName, maxResults, c),
            ct);
}
