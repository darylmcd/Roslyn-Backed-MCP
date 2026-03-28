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
    Task<ScriptEvaluationDto> EvaluateAsync(string code, string[]? imports, CancellationToken ct);
}
