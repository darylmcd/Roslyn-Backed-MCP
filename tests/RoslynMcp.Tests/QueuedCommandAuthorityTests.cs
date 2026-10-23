using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class QueuedCommandAuthorityTests
{
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task QueuedProducer_RevalidatesGrantsAndInputsBeforeStarting(bool parsedTest, bool mutateInput)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(QueuedCommandAuthorityTests), Guid.NewGuid().ToString("N"));
        HoldingRunner? runner = null;
        GatedCommandExecutor? executor = null;
        Task? blocker = null;
        Task? pending = null;
        var pendingObserved = false;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var input = Path.Combine(root, "Input.props");
            await File.WriteAllTextAsync(input, "before");
            var identity = await PhysicalInputIdentity.CaptureAsync(input, CancellationToken.None);
            runner = new HoldingRunner();
            executor = new GatedCommandExecutor(new FailClosedWorkspaceManagerStub(), runner, NullLogger<GatedCommandExecutor>.Instance);
            var granted = true;
            var checks = 0;
            async Task AuthorizeAsync(CancellationToken token)
            {
                checks++;
                if (!granted) throw new UnauthorizedAccessException("Controlled revoked grant.");
                await PhysicalInputIdentity.AssertCurrentAsync([identity], token);
            }
            blocker = executor.ExecuteAsync("ws", input, ["hold"], TimeSpan.FromSeconds(30), CancellationToken.None);
            await runner.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            pending = parsedTest
                ? executor.ExecuteTestAsync("ws", input, ["test"], (_, _) => throw new AssertFailedException("A refused queued command cannot collect results."), TimeSpan.FromSeconds(30), null, AuthorizeAsync, CancellationToken.None)
                : executor.ExecuteAuthorizedAsync("ws", input, ["restore"], TimeSpan.FromSeconds(30), AuthorizeAsync, CancellationToken.None);
            Assert.AreEqual(0, checks, "Start authorization belongs after queue acquisition.");
            if (mutateInput) await File.WriteAllTextAsync(input, "after");
            else granted = false;
            runner.Release.TrySetResult();
            await blocker;
            pendingObserved = true;
            if (mutateInput) await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => pending);
            else await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => pending);
            Assert.AreEqual(1, checks);
            Assert.AreEqual(1, runner.Calls, "Only the blocking command may reach the process runner.");
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            runner?.Release.TrySetResult();
            await CleanupFailureCollector.RunAfterFailureAsync("Queued command fixture cleanup failed.", primary,
                async () => { if (blocker is not null) await blocker; },
                async () => { if (pending is not null && !pendingObserved) await pending; },
                CleanupFailureCollector.FromAction(() => executor?.Dispose()),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    [TestMethod]
    public async Task StartAuthorization_IsInsideTimeoutAndPreservesSanitizedPolicyCategory()
    {
        GatedCommandExecutor? executor = null;
        Exception? primary = null;
        try
        {
            var runner = new HoldingRunner();
            executor = new GatedCommandExecutor(new FailClosedWorkspaceManagerStub(), runner, NullLogger<GatedCommandExecutor>.Instance);
            await Assert.ThrowsExactlyAsync<TimeoutException>(() => executor.ExecuteAuthorizedAsync("ws", "probe.csproj", ["restore"],
                TimeSpan.FromMilliseconds(100), token => Task.Delay(Timeout.InfiniteTimeSpan, token), CancellationToken.None));
            var failure = new PublicArgumentException("Controlled private-synthetic policy denial.", "paths");
            WorkspacePathPolicyRefusal.Mark(failure, WorkspacePathPolicyRefusalKind.SanctionedRootBoundary);
            var observed = await Assert.ThrowsExactlyAsync<PublicArgumentException>(() => executor.ExecuteAuthorizedAsync("ws", "probe.csproj",
                ["restore", "-p:Probe=private-synthetic"], TimeSpan.FromSeconds(5), _ => Task.FromException(failure), CancellationToken.None));
            Assert.IsTrue(WorkspacePathPolicyRefusal.Is(observed, WorkspacePathPolicyRefusalKind.SanctionedRootBoundary));
            Assert.IsFalse(observed.ToString().Contains("private-synthetic", StringComparison.Ordinal));
            Assert.AreEqual(0, runner.Calls);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally { OwnedResourceCleanup.Dispose("Failed to release authorization executor.", primary, executor); }
    }

    private sealed class HoldingRunner : IDotnetCommandRunner
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Calls { get; private set; }
        public Task<CommandExecutionDto> RunAsync(string workingDirectory, string targetPath, IReadOnlyList<string> arguments, CancellationToken ct) =>
            RunAsync(workingDirectory, targetPath, arguments, null, ct);
        public async Task<CommandExecutionDto> RunAsync(string workingDirectory, string targetPath, IReadOnlyList<string> arguments,
            IReadOnlyList<EarlyKillPattern>? earlyKillPatterns, CancellationToken ct)
        {
            Calls++;
            if (arguments[0] == "hold")
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(ct);
            }
            return new("dotnet", arguments, workingDirectory, targetPath, 0, true, 1, "", "");
        }
    }
}
