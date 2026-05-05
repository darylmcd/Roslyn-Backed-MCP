using ModelContextProtocol;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// Helper for reporting MCP progress via IProgress&lt;ProgressNotificationValue&gt;.
/// The MCP SDK automatically binds IProgress&lt;ProgressNotificationValue&gt; parameters in tools
/// to forward notifications to the client when a ProgressToken is provided.
/// </summary>
/// <remarks>
/// <para>
/// <b>Stage-fine emissions for long-running tools.</b> The plain
/// <see cref="Report(System.IProgress{ProgressNotificationValue}?, float, float?)"/> overload
/// emits only progress fractions (0..1). Long-running tools (workspace_load, workspace_warm,
/// build_workspace, test_run) should additionally call the
/// <see cref="ReportStage(System.IProgress{ProgressNotificationValue}?, float, float, string)"/>
/// overload at coarse-grained stage boundaries — e.g. "evaluating-msbuild", "loading-workspace",
/// "running-tests", "done" — so MCP clients can render an intermediate label instead of waiting
/// silently while the tool runs. Stage labels are STAGE-LEVEL ("loading-workspace"), not
/// per-project counts: emitting per-project N/M would require service-interface changes that
/// are out of scope for the audit-coverage initiative. See
/// <c>progress-emit-audit-coverage</c> in the backlog for the broader treatment.
/// </para>
/// <para>
/// <b>Picking stage labels.</b> Labels SHOULD be short (≤ 32 chars), kebab-case, and stable
/// across releases — clients may key UI strings off them. Avoid embedding mutable state
/// (project counts, file paths) in the label itself; that's what
/// <see cref="ProgressNotificationValue.Progress"/> + <see cref="ProgressNotificationValue.Total"/>
/// are for. The currently emitted labels (used by <c>workspace_load</c>, <c>workspace_warm</c>,
/// <c>build_workspace</c>, and <c>test_run</c>) are documented inline at each tool's call site.
/// </para>
/// </remarks>
public static class ProgressHelper
{
    /// <summary>
    /// Reports progress safely, handling null progress instances. Use this overload when no
    /// stage label is meaningful (e.g. tools fast enough that a single 0→1 emission suffices).
    /// </summary>
    public static void Report(IProgress<ProgressNotificationValue>? progress, float current, float? total = null)
    {
        progress?.Report(new ProgressNotificationValue { Progress = current, Total = total });
    }

    /// <summary>
    /// Reports progress with an additional stage label so MCP clients can render a textual
    /// description of the current phase (e.g. "evaluating-msbuild", "running-tests").
    /// </summary>
    /// <param name="progress">The progress sink supplied by the MCP SDK; may be <see langword="null"/>.</param>
    /// <param name="current">Current progress value (typically a stage index, e.g. 1 of 4).</param>
    /// <param name="total">Total progress value (typically the stage count, e.g. 4).</param>
    /// <param name="stageLabel">
    /// Short kebab-case label naming the stage. Stable across releases; clients may key UI
    /// strings off it. See the remarks on <see cref="ProgressHelper"/> for guidance.
    /// </param>
    public static void ReportStage(
        IProgress<ProgressNotificationValue>? progress,
        float current,
        float total,
        string stageLabel)
    {
        progress?.Report(new ProgressNotificationValue
        {
            Progress = current,
            Total = total,
            Message = stageLabel,
        });
    }
}
