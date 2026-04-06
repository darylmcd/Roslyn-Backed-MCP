using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Provides C# script evaluation using the Roslyn Scripting API for interactive
/// expression testing and prototyping.
/// </summary>
public interface IScriptingService
{
    /// <summary>
    /// Evaluates a C# script expression or code block and returns the result.
    /// </summary>
    /// <param name="code">The C# code to evaluate.</param>
    /// <param name="imports">Optional additional namespace imports.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="onProgress">
    /// Optional callback invoked on a heartbeat while Roslyn compiles or runs the script (see
    /// <see cref="ScriptEvaluationProgress"/>). Used by the MCP host for progress notifications and stuck-style diagnostics.
    /// </param>
    /// <param name="timeoutSecondsOverride">
    /// Optional per-call timeout in seconds (UX-002). When supplied, overrides the
    /// <c>ROSLYNMCP_SCRIPT_TIMEOUT_SECONDS</c> environment-default for this single invocation. Must be a
    /// positive value; <see langword="null"/> falls back to the configured default.
    /// </param>
    Task<ScriptEvaluationDto> EvaluateAsync(
        string code,
        string[]? imports,
        CancellationToken ct,
        Action<ScriptEvaluationProgress>? onProgress = null,
        int? timeoutSecondsOverride = null);
}
