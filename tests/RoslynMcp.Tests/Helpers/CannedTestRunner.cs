using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Tests.Helpers;

/// <summary>Shares phase plumbing only for fixtures with explicitly synthetic execution context.</summary>
internal abstract class CannedTestRunner : ITestRunnerService
{
    public virtual Task<TestRunPlan> PrepareTestRunAsync(
        string workspaceId, string? projectName, string? filter, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(SyntheticTestRunPlans.Create(workspaceId, projectName, filter));
    }

    public Task<TestRunResultDto> RunTestCommandAsync(TestRunPlan plan, CancellationToken ct)
    {
        SyntheticTestRunPlans.RequireSyntheticContext(plan);
        return RunTestsAsync(plan.WorkspaceId, plan.ProjectName, plan.Filter, ct);
    }

    public Task<TestRunResultDto> CompleteTestRunAsync(TestRunPlan plan, TestRunResultDto result, CancellationToken ct)
    {
        SyntheticTestRunPlans.RequireSyntheticContext(plan);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(result);
    }

    public abstract Task<TestRunResultDto> RunTestsAsync(
        string workspaceId, string? projectName, string? filter, CancellationToken ct);
}
