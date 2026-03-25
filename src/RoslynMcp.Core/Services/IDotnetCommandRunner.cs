using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Executes <c>dotnet</c> CLI commands as child processes and captures their output.
/// </summary>
public interface IDotnetCommandRunner
{
    /// <summary>
    /// Runs a <c>dotnet</c> command with the given arguments and returns the captured execution result.
    /// </summary>
    /// <param name="workingDirectory">The working directory for the process.</param>
    /// <param name="targetPath">The solution or project path passed as the primary target argument.</param>
    /// <param name="arguments">Additional arguments to pass to <c>dotnet</c>.</param>
    /// <param name="ct">Cancellation token. Cancellation will attempt to kill the child process.</param>
    Task<CommandExecutionDto> RunAsync(
        string workingDirectory,
        string targetPath,
        IReadOnlyList<string> arguments,
        CancellationToken ct);
}
