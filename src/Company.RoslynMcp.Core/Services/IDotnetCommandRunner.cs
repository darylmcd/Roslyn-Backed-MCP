using Company.RoslynMcp.Core.Models;

namespace Company.RoslynMcp.Core.Services;

public interface IDotnetCommandRunner
{
    Task<CommandExecutionDto> RunAsync(
        string workingDirectory,
        string targetPath,
        IReadOnlyList<string> arguments,
        CancellationToken ct);
}
