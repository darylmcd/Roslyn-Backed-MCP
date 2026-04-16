using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Catalog;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// Item 10: static reference-based test coverage map. Fast alternative to the runtime
/// <c>test_coverage</c> tool for "is this symbol tested at all" questions.
/// </summary>
[McpServerToolType]
public static class TestReferenceMapTools
{
    [McpServerTool(Name = "test_reference_map", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("validation", "experimental", true, false,
        "Statically map source symbols to test references; returns covered/uncovered symbol lists and a coverage percentage. Supports offset/limit pagination and projectName scoping."),
     Description("Map productive source symbols to the test methods that reference them statically. Useful for identifying untested public/internal APIs without running tests. Responses paginate via offset/limit; projectName scopes both the productive-symbol collection (when the name matches a productive project) and the test scan (when it matches a test project).")]
    public static Task<string> BuildTestReferenceMap(
        IWorkspaceExecutionGate gate,
        ITestReferenceMapService testReferenceMapService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: restrict to a single project (name or path). Matching a productive project filters the covered/uncovered sets to that project's symbols; matching a test project scopes the test scan. Unknown name → structured error.")] string? projectName = null,
        [Description("0-based start index. Clamped to [0, total]. Default 0.")] int offset = 0,
        [Description("Max entries returned per page. Clamped to [1, 500]. Default 200.")] int limit = 200,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            var result = await testReferenceMapService.BuildAsync(workspaceId, projectName, offset, limit, c).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, JsonDefaults.Indented);
        }, ct);
    }
}
