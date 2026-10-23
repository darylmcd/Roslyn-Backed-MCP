using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class TestRunnerPhaseContractTests
{
    [TestMethod]
    [DataRow(nameof(ITestRunnerService.PrepareTestRunAsync), 4)]
    [DataRow(nameof(ITestRunnerService.RunTestCommandAsync), 2)]
    [DataRow(nameof(ITestRunnerService.CompleteTestRunAsync), 3)]
    public Task LegacyOnlyImplementation_CannotSilentlyDiscardPreparedPhase(string name, int parameterCount)
    {
        var plan = new TestRunPlan("probe", "tests", "filter",
            System.Collections.Immutable.ImmutableDictionary<string, string>.Empty.Add("Configuration", "Release"),
            SyntheticTestRunPlans.OwnedDefaultContext, [],
            TargetPath: "controlled.csproj", WorkspaceVersion: 2);
        var result = new TestRunResultDto(new CommandExecutionDto("dotnet", [], "", "", 0, true, 1, "", ""), 1, 1, 0, 0, []);
        return RequiredInterfaceContractProbe.RejectsLegacyOnlyAsync(typeof(ITestRunnerService), name, parameterCount, parameter =>
            parameter.ParameterType == typeof(string) ? (object)"probe"
            : parameter.ParameterType == typeof(CancellationToken) ? CancellationToken.None
            : parameter.ParameterType == typeof(TestRunPlan) ? plan
            : result);
    }
}
