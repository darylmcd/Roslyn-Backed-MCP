using System.ComponentModel;
using System.IO;
using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Resources;

[McpServerResourceType]
public static class WorkspaceResources
{

    [McpServerResource(UriTemplate = "roslyn://workspaces", Name = "workspaces", MimeType = "application/json")]
    [Description("List all currently loaded workspace sessions with a lean summary per workspace (counts and load state, no per-project tree). For the verbose payload use roslyn://workspaces/verbose. Workspace-scoped resources (status, projects, diagnostics, source_file) use URI templates — if your MCP client only shows static resources from resources/list, read roslyn://server/resource-templates to discover those templates.")]
    public static string GetWorkspaces(IWorkspaceManager workspace) =>
        ToolErrorHandler.ExecuteResource("roslyn://workspaces", () =>
        {
            var workspaces = workspace.ListWorkspaces();
            var summaries = workspaces.Select(WorkspaceStatusSummaryDto.From).ToList();
            return JsonSerializer.Serialize(new { count = summaries.Count, workspaces = summaries }, JsonDefaults.Indented);
        });

    [McpServerResource(UriTemplate = "roslyn://workspaces/verbose", Name = "workspaces_verbose", MimeType = "application/json")]
    [Description("Verbose variant of roslyn://workspaces — returns the full per-project tree and workspace diagnostics for every loaded session. Can be large on multi-project solutions; prefer the summary resource at roslyn://workspaces unless you need the full payload.")]
    public static string GetWorkspacesVerbose(IWorkspaceManager workspace) =>
        ToolErrorHandler.ExecuteResource("roslyn://workspaces/verbose", () =>
        {
            var workspaces = workspace.ListWorkspaces();
            return JsonSerializer.Serialize(new { count = workspaces.Count, workspaces }, JsonDefaults.Indented);
        });

    [McpServerResource(UriTemplate = "roslyn://workspace/{workspaceId}/status", Name = "workspace_status", MimeType = "application/json")]
    [Description("Get a lean summary of a loaded workspace's status (counts and load state, no per-project tree). For the verbose payload use roslyn://workspace/{workspaceId}/status/verbose. URI uses the workspace_status template; see roslyn://server/resource-templates if your client does not list template URIs.")]
    public static Task<string> GetWorkspaceStatus(
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        [Description("The workspace session identifier")] string workspaceId,
        CancellationToken ct = default) =>
        // roslyn-fetch-resource-timing: gate the workspace-scoped read through the same
        // per-workspace reader lock as the workspace_status tool so an in-flight
        // workspace_load / workspace_reload cannot race this resource dispatch and yield
        // a KeyNotFoundException / partially-loaded snapshot. Matches the tool's semantics
        // (staleness auto-reload, rate limit, global throttle, TOCTOU-safe existence check).
        ToolErrorHandler.ExecuteResourceAsync("roslyn://workspace/{workspaceId}/status", () =>
            gate.RunReadAsync(workspaceId, async innerCt =>
            {
                var status = await workspace.GetStatusAsync(workspaceId, innerCt).ConfigureAwait(false);
                return JsonSerializer.Serialize(WorkspaceStatusSummaryDto.From(status), JsonDefaults.Indented);
            }, ct));

    [McpServerResource(UriTemplate = "roslyn://workspace/{workspaceId}/status/verbose", Name = "workspace_status_verbose", MimeType = "application/json")]
    [Description("Verbose variant of roslyn://workspace/{workspaceId}/status — returns the full per-project tree and workspace diagnostics for the workspace. Prefer the summary resource at roslyn://workspace/{workspaceId}/status unless you need the full payload.")]
    public static Task<string> GetWorkspaceStatusVerbose(
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        [Description("The workspace session identifier")] string workspaceId,
        CancellationToken ct = default) =>
        ToolErrorHandler.ExecuteResourceAsync("roslyn://workspace/{workspaceId}/status/verbose", () =>
            gate.RunReadAsync(workspaceId, async innerCt =>
            {
                var status = await workspace.GetStatusAsync(workspaceId, innerCt).ConfigureAwait(false);
                return JsonSerializer.Serialize(status, JsonDefaults.Indented);
            }, ct));

    [McpServerResource(UriTemplate = "roslyn://workspace/{workspaceId}/projects", Name = "workspace_projects", MimeType = "application/json")]
    [Description("Get the project dependency graph and project metadata for a workspace. Requires workspaceId from workspace_load; see roslyn://server/resource-templates for the URI pattern.")]
    public static Task<string> GetProjects(
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        [Description("The workspace session identifier")] string workspaceId,
        CancellationToken ct = default) =>
        ToolErrorHandler.ExecuteResourceAsync("roslyn://workspace/{workspaceId}/projects", () =>
            gate.RunReadAsync(workspaceId, innerCt =>
            {
                var graph = workspace.GetProjectGraph(workspaceId);
                return Task.FromResult(JsonSerializer.Serialize(graph, JsonDefaults.Indented));
            }, ct));

    /// <summary>
    /// Maximum diagnostics returned by the diagnostics resource. The companion
    /// `project_diagnostics` tool exposes offset/limit; resources cannot accept query
    /// parameters in MCP, so the resource enforces a hard cap. Pre-fix this resource
    /// returned every diagnostic and timed out on solutions like Jellyfin (3433 entries).
    /// </summary>
    private const int DiagnosticsResourceCap = 500;

    [McpServerResource(UriTemplate = "roslyn://workspace/{workspaceId}/diagnostics", Name = "workspace_diagnostics", MimeType = "application/json")]
    [Description("Get diagnostics for a loaded workspace, capped at 500 entries (Warning floor by default — Info diagnostics are excluded). For full pagination control or to include Info, use the project_diagnostics tool. Workspace-scoped URI; see roslyn://server/resource-templates.")]
    public static Task<string> GetDiagnostics(
        IWorkspaceExecutionGate gate,
        IDiagnosticService diagnosticService,
        [Description("The workspace session identifier")] string workspaceId,
        CancellationToken ct = default) =>
        ToolErrorHandler.ExecuteResourceAsync("roslyn://workspace/{workspaceId}/diagnostics", () =>
            gate.RunReadAsync(workspaceId, async innerCt =>
            {
                // diagnostics-resource-timeout: the resource has no pagination affordance, so apply
                // a Warning floor (skip Info, which dominates large solutions) and cap the returned
                // rows. Total* fields still reflect the full unfiltered result so callers can see
                // the real solution health and decide whether to switch to project_diagnostics for
                // deeper filtering.
                var diagnostics = await diagnosticService.GetDiagnosticsAsync(
                    workspaceId, projectFilter: null, fileFilter: null,
                    severityFilter: "Warning", diagnosticIdFilter: null, innerCt).ConfigureAwait(false);

                var workspaceList = diagnostics.WorkspaceDiagnostics.ToList();
                var compilerList = diagnostics.CompilerDiagnostics.ToList();
                var analyzerList = diagnostics.AnalyzerDiagnostics.ToList();

                var totalReturned = workspaceList.Count + compilerList.Count + analyzerList.Count;
                var hasMore = totalReturned > DiagnosticsResourceCap;

                // Cap each bucket proportionally so workspace-load failures (highest signal-to-noise)
                // are always visible. Workspace bucket fits in full first, then compiler, then
                // analyzer; the analyzer bucket is the loudest and gets truncated last.
                var remaining = DiagnosticsResourceCap;
                var pagedWorkspace = TakeAndDecrement(workspaceList, ref remaining);
                var pagedCompiler = TakeAndDecrement(compilerList, ref remaining);
                var pagedAnalyzer = TakeAndDecrement(analyzerList, ref remaining);

                return JsonSerializer.Serialize(new
                {
                    totalErrors = diagnostics.TotalErrors,
                    totalWarnings = diagnostics.TotalWarnings,
                    totalInfo = diagnostics.TotalInfo,
                    compilerErrors = diagnostics.CompilerErrors,
                    analyzerErrors = diagnostics.AnalyzerErrors,
                    workspaceErrors = diagnostics.WorkspaceErrors,
                    returnedDiagnostics = pagedWorkspace.Count + pagedCompiler.Count + pagedAnalyzer.Count,
                    cap = DiagnosticsResourceCap,
                    hasMore,
                    paginationNote = hasMore
                        ? "More diagnostics exist than the resource cap (500). Use the project_diagnostics tool with offset/limit/filters to page through the full result, or to include Info severity."
                        : null,
                    severityFloor = "Warning",
                    severityNote = "Resource omits Info-severity diagnostics by default. Use project_diagnostics with severity=\"Info\" or omit the filter for full coverage.",
                    workspaceDiagnostics = pagedWorkspace,
                    compilerDiagnostics = pagedCompiler,
                    analyzerDiagnostics = pagedAnalyzer
                }, JsonDefaults.Indented);
            }, ct));

    private static List<T> TakeAndDecrement<T>(List<T> source, ref int remaining)
    {
        if (remaining <= 0) return [];
        if (source.Count <= remaining)
        {
            remaining -= source.Count;
            return source;
        }
        var taken = source.Take(remaining).ToList();
        remaining = 0;
        return taken;
    }

    // source_file is text/x-csharp, not JSON, so we cannot return a JSON envelope on error.
    // Throwing McpToolException with the OriginatingSource keeps the framework's default
    // exception path; the MCP client will see a generic error rather than a structured envelope.
    [McpServerResource(UriTemplate = "roslyn://workspace/{workspaceId}/file/{filePath}", Name = "source_file", MimeType = "text/x-csharp")]
    [Description("Read the source text of a file in the loaded workspace. filePath accepts URL-encoded form (recommended for cross-client portability — Windows absolute paths contain `:` and `\\` which are reserved in URI grammar) or a raw absolute path; both are normalized server-side. Forward slashes are converted to the platform separator. For a line range, use the sibling template roslyn://workspace/{workspaceId}/file/{filePath}/lines/{startLine}-{endLine}.")]
    public static async Task<string> GetSourceFile(
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        [Description("The workspace session identifier")] string workspaceId,
        [Description("Absolute path to the source file. URL-encoded preferred (e.g. C%3A%5CUsers%5Cfoo) — Windows raw paths contain `:` and `\\` which are reserved per RFC 3986 and may be rejected by some MCP clients before reaching the server.")] string filePath,
        CancellationToken ct = default)
    {
        const string source = "roslyn://workspace/{workspaceId}/file/{filePath}";
        try
        {
            // roslyn-fetch-resource-timing: gate through the per-workspace reader lock so this
            // cannot race with a concurrent workspace_load / workspace_reload that would leave
            // session.Workspace momentarily disposed and produce a spurious KeyNotFoundException.
            return await gate.RunReadAsync(workspaceId, async innerCt =>
            {
                var normalizedPath = NormalizeFilePathForResource(filePath);
                if (!Path.IsPathFullyQualified(normalizedPath))
                {
                    throw new InvalidOperationException(
                        $"filePath must be an absolute path after decoding. Received: {filePath}");
                }

                var text = await workspace.GetSourceTextAsync(workspaceId, normalizedPath, innerCt).ConfigureAwait(false);
                if (text is null)
                    throw new KeyNotFoundException($"Document not found in workspace: {normalizedPath}");
                return text;
            }, ct).ConfigureAwait(false);
        }
        catch (KeyNotFoundException ex) { throw new McpToolException(source, $"Not found: {ex.Message}", ex); }
        catch (InvalidOperationException ex) { throw new McpToolException(source, $"Invalid operation: {ex.Message}", ex); }
    }

    /// <summary>
    /// file-resource-uri-windows-path-handling: centralized normalizer that handles every
    /// shape a client may send filePath in:
    /// <list type="bullet">
    ///   <item><description>Fully URL-encoded (e.g. <c>C%3A%5CUsers%5Cfoo</c>) — single decode.</description></item>
    ///   <item><description>Raw Windows absolute (e.g. <c>C:\Users\foo</c>) — already in usable form.</description></item>
    ///   <item><description>Forward-slash variant (e.g. <c>C:/Users/foo</c>) — separator-normalized.</description></item>
    ///   <item><description>Mixed encoding (some segments encoded, others not) — decode is idempotent for non-`%` content.</description></item>
    /// </list>
    /// </summary>
    private static string NormalizeFilePathForResource(string filePath)
    {
        // UnescapeDataString is idempotent on already-decoded paths (no `%XX` sequences = no-op).
        // Replace forward slashes with the platform separator AFTER decoding so encoded `%2F`
        // sequences are also normalized.
        return Uri.UnescapeDataString(filePath).Replace('/', Path.DirectorySeparatorChar);
    }

    /// <summary>
    /// source-file-resource-line-range-parity: sibling resource template that returns a
    /// 1-based inclusive line range from the file. The {lineRange} segment is decoded as
    /// "{startLine}-{endLine}" — e.g. "/lines/2021-3021" returns lines 2021..3021. Mirrors
    /// the get_source_text tool's startLine/endLine slicing for callers using the resource
    /// surface. The returned text is prefixed with a `// roslyn://… lines N..M of T` marker
    /// so agents can tell the slice apart from a whole-file read.
    /// </summary>
    // dr-9-13-flag-resource-invalid-range-resource-returns-ge:
    // Wrapped in ToolErrorHandler.ExecuteResourceAsync so invalid lineRange/filePath inputs
    // return a structured JSON error envelope (category, message, tool) instead of bubbling
    // a generic JSON-RPC -32603 through the framework. On success the response is still the
    // marker-prefixed source slice; on failure clients get an actionable error document with
    // the resource URI template as the `tool` field.
    [McpServerResource(UriTemplate = "roslyn://workspace/{workspaceId}/file/{filePath}/lines/{lineRange}", Name = "source_file_lines", MimeType = "text/x-csharp")]
    [Description("Read a 1-based inclusive line range from a file in the loaded workspace. filePath must be URL-encoded. lineRange is \"startLine-endLine\" (e.g. /lines/100-200). The response is prefixed with a comment marker noting the slice. For the whole file, use the sibling template without /lines/. Invalid ranges (e.g. non-numeric, endLine < startLine, or startLine past EOF) return a structured JSON error envelope — not the C# MIME success shape.")]
    public static Task<string> GetSourceFileLines(
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        [Description("The workspace session identifier")] string workspaceId,
        [Description("Absolute path to the source file (URL-encoded)")] string filePath,
        [Description("Line range as \"startLine-endLine\" (1-based, inclusive). E.g. \"100-200\".")] string lineRange,
        CancellationToken ct = default)
    {
        const string source = "roslyn://workspace/{workspaceId}/file/{filePath}/lines/{lineRange}";
        return ToolErrorHandler.ExecuteResourceAsync(source, () =>
            gate.RunReadAsync(workspaceId, async innerCt =>
            {
                var normalizedPath = NormalizeFilePathForResource(filePath);
                if (!Path.IsPathFullyQualified(normalizedPath))
                {
                    throw new ArgumentException(
                        $"filePath must be an absolute path after decoding. Received: {filePath}",
                        nameof(filePath));
                }

                var (startLine, endLine) = ParseLineRange(lineRange);

                var text = await workspace.GetSourceTextAsync(workspaceId, normalizedPath, innerCt).ConfigureAwait(false);
                if (text is null)
                    throw new KeyNotFoundException($"Document not found in workspace: {normalizedPath}");

                var totalLineCount = RoslynMcp.Roslyn.Helpers.SourceTextSlicer.CountLines(text);
                if (startLine > totalLineCount)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(lineRange),
                        $"startLine ({startLine}) is past the end of the file ({totalLineCount} lines).");
                }
                // Clamp end to file end for graceful behavior on over-shoot ranges.
                var clampedEnd = Math.Min(endLine, totalLineCount);

                var slice = RoslynMcp.Roslyn.Helpers.SourceTextSlicer.SliceLines(text, startLine, clampedEnd);
                var marker = $"// roslyn://workspace/{workspaceId}/file/.../lines/{startLine}-{clampedEnd} of {totalLineCount}{Environment.NewLine}";
                return marker + slice;
            }, ct));
    }

    private static (int StartLine, int EndLine) ParseLineRange(string lineRange)
    {
        if (string.IsNullOrWhiteSpace(lineRange))
        {
            throw new ArgumentException("lineRange must be in the format \"startLine-endLine\" (1-based).", nameof(lineRange));
        }

        var dash = lineRange.IndexOf('-');
        if (dash < 1 || dash == lineRange.Length - 1)
        {
            throw new ArgumentException(
                $"lineRange must be in the format \"startLine-endLine\" (e.g. \"100-200\"). Got: {lineRange}",
                nameof(lineRange));
        }

        if (!int.TryParse(lineRange[..dash], out var start) || start < 1)
        {
            throw new ArgumentException($"startLine must be a positive integer. Got: {lineRange[..dash]}", nameof(lineRange));
        }
        if (!int.TryParse(lineRange[(dash + 1)..], out var end) || end < 1)
        {
            throw new ArgumentException($"endLine must be a positive integer. Got: {lineRange[(dash + 1)..]}", nameof(lineRange));
        }
        if (end < start)
        {
            throw new ArgumentException($"endLine ({end}) must be >= startLine ({start}).", nameof(lineRange));
        }

        return (start, end);
    }
}
