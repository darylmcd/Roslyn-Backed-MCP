using System.Text.RegularExpressions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Tests.Services;

[TestClass]
public sealed class EarlyKillContractTests
{
    [TestMethod]
    [DataRow(typeof(IGatedCommandExecutor), nameof(IGatedCommandExecutor.ExecuteAsync))]
    [DataRow(typeof(IDotnetCommandRunner), nameof(IDotnetCommandRunner.RunAsync))]
    public void PatternAwareMethod_IsRequired(Type contract, string methodName)
    {
        var method = contract.GetMethods().Single(candidate =>
            candidate.Name == methodName
            && candidate.GetParameters().Any(parameter => parameter.ParameterType == typeof(IReadOnlyList<EarlyKillPattern>))
            && candidate.GetParameters().All(parameter => parameter.Name != "executablePath"));

        Assert.IsTrue(method.IsAbstract, "A patternless implementation must not satisfy this contract.");
    }

    [TestMethod]
    public async Task RunnerConvenienceMethods_PreserveRequestedBehavior()
    {
        var recording = new RecordingRunner();
        IDotnetCommandRunner runner = recording;
        var ct = CancellationToken.None;
        EarlyKillPattern[] patterns = [new(new Regex("sentinel"), "sentinel")];

        await runner.RunAsync(".", "test.csproj", ["test"], ct);
        await runner.RunAsync(".", "test.csproj", ["test"], patterns, "dotnet", ct);
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() =>
            runner.RunAsync(".", "test.csproj", ["test"], patterns, "custom-dotnet", ct));

        Assert.HasCount(2, recording.Calls, "An unsupported executable must not start a command.");
        Assert.IsNull(recording.Calls[0]);
        Assert.AreSame(patterns, recording.Calls[1]);
    }

    private sealed class RecordingRunner : IDotnetCommandRunner
    {
        public List<IReadOnlyList<EarlyKillPattern>?> Calls { get; } = [];

        public Task<CommandExecutionDto> RunAsync(
            string workingDirectory, string targetPath, IReadOnlyList<string> arguments,
            IReadOnlyList<EarlyKillPattern>? earlyKillPatterns, CancellationToken ct)
        {
            Calls.Add(earlyKillPatterns);
            return Task.FromResult(new CommandExecutionDto(
                "dotnet", arguments, workingDirectory, targetPath, 0, true, 0, string.Empty, string.Empty));
        }
    }
}
