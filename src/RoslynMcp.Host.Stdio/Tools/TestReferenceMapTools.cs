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
        "Statically map source symbols to test references; returns covered/uncovered symbol lists and a coverage percentage."),
     Description("Map productive source symbols to the test methods that reference them statically. Useful for identifying untested public/internal APIs without running tests.")]
    public static Task<string> BuildTestReferenceMap(
        IWorkspaceExecutionGate gate,
        ITestReferenceMapService testReferenceMapService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: restrict to a single project (name or path)")] string? projectName = null,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync("test_reference_map", () =>
            gate.RunReadAsync(workspaceId, async c =>
            {
                var result = await testReferenceMapService.BuildAsync(workspaceId, projectName, c).ConfigureAwait(false);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }
}
