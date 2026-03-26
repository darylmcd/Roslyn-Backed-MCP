using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Executes <c>dotnet test</c> against a loaded workspace or specific test project and returns
/// structured pass/fail results.
/// </summary>
public interface ITestRunnerService
{
    Task<TestRunResultDto> RunTestsAsync(string workspaceId, string? projectName, string? filter, CancellationToken ct);
}
