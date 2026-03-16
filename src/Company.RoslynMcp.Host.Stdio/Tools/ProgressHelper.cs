using ModelContextProtocol;

namespace Company.RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// Helper for reporting MCP progress via IProgress&lt;ProgressNotificationValue&gt;.
/// The MCP SDK automatically binds IProgress&lt;ProgressNotificationValue&gt; parameters in tools
/// to forward notifications to the client when a ProgressToken is provided.
/// </summary>
public static class ProgressHelper
{
    /// <summary>
    /// Reports progress safely, handling null progress instances.
    /// </summary>
    public static void Report(IProgress<ProgressNotificationValue>? progress, float current, float? total = null)
    {
        progress?.Report(new ProgressNotificationValue { Progress = current, Total = total });
    }
}
