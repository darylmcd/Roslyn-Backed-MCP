namespace RoslynMcp.Core.Models;

/// <summary>
/// Fired periodically while Roslyn is compiling or executing a script in <see cref="IScriptingService.EvaluateAsync"/>.
/// Used for MCP progress notifications and stuck-style diagnostics.
/// </summary>
public readonly record struct ScriptEvaluationProgress(
    TimeSpan Elapsed,
    TimeSpan Budget,
    int HeartbeatIndex);
