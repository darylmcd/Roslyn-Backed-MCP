using System.Collections.Immutable;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Tests.Helpers;

/// <summary>Explicit no-workspace/no-command context used by canned result projection fixtures.</summary>
internal static class SyntheticTestRunPlans
{
    internal static PreparedDotnetTestContext OwnedDefaultContext { get; } = new(
        [new DotnetConfigurationIdentity(Path.Combine(TestTempRoot.Current, "global.json"), true,
            [.. System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(TestTempRoot.DefaultGlobalJson))])], false, []);

    internal static TestRunPlan Create(string workspaceId, string? projectName, string? filter) =>
        new(workspaceId, projectName, filter, ImmutableDictionary<string, string>.Empty, OwnedDefaultContext, []);

    internal static void RequireSyntheticContext(TestRunPlan plan)
    {
        if (!ReferenceEquals(plan.Context, OwnedDefaultContext) || plan.Context.UsesNativeMtp || plan.Context.ExecutionInputs.IsDefault || !plan.Context.ExecutionInputs.IsEmpty || plan.EvaluationProperties.Count != 0 || plan.TargetPath is not null || plan.RequiresMtpNative ||
            plan.NativeTestArguments.IsDefault || !plan.NativeTestArguments.IsEmpty || plan.NoRestore || plan.WorkspaceVersion != 0)
            throw new NotSupportedException("This canned result fixture has no workspace, evaluation, command or version context.");
    }
}
