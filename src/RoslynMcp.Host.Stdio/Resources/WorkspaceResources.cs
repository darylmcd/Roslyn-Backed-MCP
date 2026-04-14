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
        IWorkspaceManager workspace,
        [Description("The workspace session identifier")] string workspaceId,
        CancellationToken ct = default) =>
        ToolErrorHandler.ExecuteResourceAsync("roslyn://workspace/{workspaceId}/status", async () =>
        {
            var status = await workspace.GetStatusAsync(workspaceId, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(WorkspaceStatusSummaryDto.From(status), JsonDefaults.Indented);
        });

    [McpServerResource(UriTemplate = "roslyn://workspace/{workspaceId}/status/verbose", Name = "workspace_status_verbose", MimeType = "application/json")]
    [Description("Verbose variant of roslyn://workspace/{workspaceId}/status — returns the full per-project tree and workspace diagnostics for the workspace. Prefer the summary resource at roslyn://workspace/{workspaceId}/status unless you need the full payload.")]
    public static Task<string> GetWorkspaceStatusVerbose(
        IWorkspaceManager workspace,
        [Description("The workspace session identifier")] string workspaceId,
        CancellationToken ct = default) =>
        ToolErrorHandler.ExecuteResourceAsync("roslyn://workspace/{workspaceId}/status/verbose", async () =>
        {
            var status = await workspace.GetStatusAsync(workspaceId, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(status, JsonDefaults.Indented);
        });

    [McpServerResource(UriTemplate = "roslyn://workspace/{workspaceId}/projects", Name = "workspace_projects", MimeType = "application/json")]
    [Description("Get the project dependency graph and project metadata for a workspace. Requires workspaceId from workspace_load; see roslyn://server/resource-templates for the URI pattern.")]
    public static string GetProjects(
        IWorkspaceManager workspace,
        [Description("The workspace session identifier")] string workspaceId) =>
        ToolErrorHandler.ExecuteResource("roslyn://workspace/{workspaceId}/projects", () =>
        {
            var graph = workspace.GetProjectGraph(workspaceId);
            return JsonSerializer.Serialize(graph, JsonDefaults.Indented);
        });

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
        IDiagnosticService diagnosticService,
        [Description("The workspace session identifier")] string workspaceId,
        CancellationToken ct = default) =>
        ToolErrorHandler.ExecuteResourceAsync("roslyn://workspace/{workspaceId}/diagnostics", async () =>
        {
            // diagnostics-resource-timeout: the resource has no pagination affordance, so apply
            // a Warning floor (skip Info, which dominates large solutions) and cap the returned
            // rows. Total* fields still reflect the full unfiltered result so callers can see
            // the real solution health and decide whether to switch to project_diagnostics for
            // deeper filtering.
            var diagnostics = await diagnosticService.GetDiagnosticsAsync(
                workspaceId, projectFilter: null, fileFilter: null,
                severityFilter: "Warning", diagnosticIdFilter: null, ct).ConfigureAwait(false);

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
        });

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
    [Description("Read the source text of a file in the loaded workspace. filePath must be URL-encoded; see roslyn://server/resource-templates. For a line range, use the sibling template roslyn://workspace/{workspaceId}/file/{filePath}/lines/{startLine}-{endLine}.")]
    public static async Task<string> GetSourceFile(
        IWorkspaceManager workspace,
        [Description("The workspace session identifier")] string workspaceId,
        [Description("Absolute path to the source file (URL-encoded)")] string filePath,
        CancellationToken ct = default)
    {
        const string source = "roslyn://workspace/{workspaceId}/file/{filePath}";
        try
        {
            var normalizedPath = Uri.UnescapeDataString(filePath).Replace('/', Path.DirectorySeparatorChar);
            if (!Path.IsPathFullyQualified(normalizedPath))
            {
                throw new InvalidOperationException(
                    $"filePath must be an absolute path after decoding. Received: {filePath}");
            }

            var text = await workspace.GetSourceTextAsync(workspaceId, normalizedPath, ct).ConfigureAwait(false);
            if (text is null)
                throw new KeyNotFoundException($"Document not found in workspace: {normalizedPath}");
            return text;
        }
        catch (KeyNotFoundException ex) { throw new McpToolException(source, $"Not found: {ex.Message}", ex); }
        catch (InvalidOperationException ex) { throw new McpToolException(source, $"Invalid operation: {ex.Message}", ex); }
    }

    /// <summary>
    /// source-file-resource-line-range-parity: sibling resource template that returns a
    /// 1-based inclusive line range from the file. The {lineRange} segment is decoded as
    /// "{startLine}-{endLine}" — e.g. "/lines/2021-3021" returns lines 2021..3021. Mirrors
    /// the get_source_text tool's startLine/endLine slicing for callers using the resource
    /// surface. The returned text is prefixed with a `// roslyn://… lines N..M of T` marker
    /// so agents can tell the slice apart from a whole-file read.
    /// </summary>
    [McpServerResource(UriTemplate = "roslyn://workspace/{workspaceId}/file/{filePath}/lines/{lineRange}", Name = "source_file_lines", MimeType = "text/x-csharp")]
    [Description("Read a 1-based inclusive line range from a file in the loaded workspace. filePath must be URL-encoded. lineRange is \"startLine-endLine\" (e.g. /lines/100-200). The response is prefixed with a comment marker noting the slice. For the whole file, use the sibling template without /lines/.")]
    public static async Task<string> GetSourceFileLines(
        IWorkspaceManager workspace,
        [Description("The workspace session identifier")] string workspaceId,
        [Description("Absolute path to the source file (URL-encoded)")] string filePath,
        [Description("Line range as \"startLine-endLine\" (1-based, inclusive). E.g. \"100-200\".")] string lineRange,
        CancellationToken ct = default)
    {
        const string source = "roslyn://workspace/{workspaceId}/file/{filePath}/lines/{lineRange}";
        try
        {
            var normalizedPath = Uri.UnescapeDataString(filePath).Replace('/', Path.DirectorySeparatorChar);
            if (!Path.IsPathFullyQualified(normalizedPath))
            {
                throw new InvalidOperationException(
                    $"filePath must be an absolute path after decoding. Received: {filePath}");
            }

            var (startLine, endLine) = ParseLineRange(lineRange);

            var text = await workspace.GetSourceTextAsync(workspaceId, normalizedPath, ct).ConfigureAwait(false);
            if (text is null)
                throw new KeyNotFoundException($"Document not found in workspace: {normalizedPath}");

            var totalLineCount = RoslynMcp.Roslyn.Helpers.SourceTextSlicer.CountLines(text);
            if (startLine > totalLineCount)
            {
                throw new InvalidOperationException(
                    $"startLine ({startLine}) is past the end of the file ({totalLineCount} lines).");
            }
            // Clamp end to file end for graceful behavior on over-shoot ranges.
            var clampedEnd = Math.Min(endLine, totalLineCount);

            var slice = RoslynMcp.Roslyn.Helpers.SourceTextSlicer.SliceLines(text, startLine, clampedEnd);
            var marker = $"// roslyn://workspace/{workspaceId}/file/.../lines/{startLine}-{clampedEnd} of {totalLineCount}{Environment.NewLine}";
            return marker + slice;
        }
        catch (KeyNotFoundException ex) { throw new McpToolException(source, $"Not found: {ex.Message}", ex); }
        catch (InvalidOperationException ex) { throw new McpToolException(source, $"Invalid operation: {ex.Message}", ex); }
        catch (ArgumentException ex) { throw new McpToolException(source, $"Invalid argument: {ex.Message}", ex); }
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
