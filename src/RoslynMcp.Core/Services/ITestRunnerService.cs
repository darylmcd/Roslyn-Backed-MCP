using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

public interface ITestRunnerService
{
    Task<TestRunResultDto> RunTestsAsync(string workspaceId, string? projectName, string? filter, CancellationToken ct);
}
