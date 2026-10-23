using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class CommandStreamingContractTests
{
    [TestMethod]
    [DataRow(typeof(IGatedCommandExecutor), "ExecuteAsync", 6)]
    [DataRow(typeof(IDotnetCommandRunner), "RunAsync", 5)]
    public async Task LegacyOnlyImplementation_CannotSilentlyAcceptStreamingRequest(Type contract, string name, int parameterCount)
    {
        var patterns = new[] { new EarlyKillPattern(new Regex("MSB3027", RegexOptions.None, TimeSpan.FromSeconds(1)), "controlled lock") };
        await RequiredInterfaceContractProbe.RejectsLegacyOnlyAsync(contract, name, parameterCount, parameter =>
            parameter.ParameterType == typeof(string) ? (object)"probe"
            : parameter.ParameterType == typeof(TimeSpan) ? TimeSpan.FromSeconds(1)
            : parameter.ParameterType == typeof(CancellationToken) ? CancellationToken.None
            : parameter.ParameterType == typeof(IReadOnlyList<EarlyKillPattern>) ? patterns
            : (object)new[] { "test", "literal value;$(not-a-shell)" });
    }

    [TestMethod]
    public async Task ConcreteExecutor_ForwardsStreamingStateAndOwnsCallerCancellation()
    {
        var runner = new StreamingProbeRunner();
        GatedCommandExecutor? executor = null;
        CancellationTokenSource? cancellation = null;
        Task<CommandExecutionDto>? pending = null;
        Exception? primary = null;
        try
        {
            executor = new GatedCommandExecutor(new FailClosedWorkspaceManagerStub(), runner, NullLogger<GatedCommandExecutor>.Instance);
            cancellation = new CancellationTokenSource();
            IReadOnlyList<string> arguments = ["test", "literal value;$(not-a-shell)"];
            IReadOnlyList<EarlyKillPattern> patterns = [new(new Regex("MSB3027", RegexOptions.None, TimeSpan.FromSeconds(1)), "controlled lock")];
            pending = executor.ExecuteAsync("ws", "C:/probe.csproj", arguments, TimeSpan.FromSeconds(20), patterns, cancellation.Token);
            await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreSame(arguments, runner.Arguments);
            Assert.AreSame(patterns, runner.Patterns);
            cancellation.Cancel();
            var exception = await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => pending);
            Assert.AreEqual(runner.Token, exception.CancellationToken);
            Assert.IsTrue(runner.Token.IsCancellationRequested);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Streaming command lifetime cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => cancellation?.Cancel()),
                async () =>
                {
                    if (pending is null) return;
                    try { await pending; }
                    catch (OperationCanceledException failure) when (runner.Token.IsCancellationRequested
                        && failure.CancellationToken == runner.Token) { }
                },
                CleanupFailureCollector.FromAction(() => cancellation?.Dispose()),
                CleanupFailureCollector.FromAction(() => executor?.Dispose()));
        }
    }

    private sealed class StreamingProbeRunner : IDotnetCommandRunner
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal IReadOnlyList<string>? Arguments { get; private set; }
        internal IReadOnlyList<EarlyKillPattern>? Patterns { get; private set; }
        internal CancellationToken Token { get; private set; }
        public Task<CommandExecutionDto> RunAsync(string workingDirectory, string targetPath, IReadOnlyList<string> arguments, CancellationToken ct) =>
            throw new AssertFailedException("A streaming request must not enter the legacy runner.");
        public async Task<CommandExecutionDto> RunAsync(string workingDirectory, string targetPath, IReadOnlyList<string> arguments,
            IReadOnlyList<EarlyKillPattern>? earlyKillPatterns, CancellationToken ct)
        {
            Arguments = arguments;
            Patterns = earlyKillPatterns;
            Token = ct;
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new AssertFailedException("The controlled runner must be cancelled.");
        }
    }
}
