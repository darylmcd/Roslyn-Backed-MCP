using System.Text.Json.Serialization;

namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents the result of executing a build-related command together with reported diagnostics.
/// </summary>
public sealed record BuildResultDto(
    CommandExecutionDto Execution,
    IReadOnlyList<DiagnosticDto> Diagnostics,
    int ErrorCount,
    int WarningCount)
{
    /// <summary>
    /// Non-fatal conditions observed while producing the result (for example
    /// <c>workspaceChangedDuringRun</c>, emitted when the workspace version moved while the build
    /// command ran without the workspace lock, so diagnostic spans were not enriched against the
    /// newer solution). Omitted when there is nothing to report.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Warnings { get; init; }

    /// <summary>
    /// Wall-clock milliseconds the build command itself took, including its command-queue wait
    /// (the budget <c>BuildTimeout</c> bounds). Omitted when the result was not produced by a
    /// timed command run.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? CommandDurationMs { get; init; }
}
