using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ForkRestorePhaseBudgetTests
{
    [TestMethod]
    [DataRow("phase")]
    [DataRow("caller")]
    [DataRow("caller-timeout")]
    [DataRow("unexpired")]
    [DataRow("already-cancelled")]
    public async Task NativeRestoreHelper_FinalAuthorityCannotOutliveItsPhase(string scenario)
    {
        MsBuildInitializer.EnsureInitialized();
        await WithNativeAsync(async context =>
        {
            var authorityCalls = 0;
            var accepted = 0;
            var actor = false;
            var expiredAtCallback = false;
            if (scenario == "already-cancelled") context.Caller.Cancel();
            async Task AssertAuthority(CancellationToken token)
            {
                authorityCalls++;
                await context.Certificate.AssertCurrentAsync(context.Solution, context.WorkspaceId,
                    context.Services.PathPolicy, token);
                if (authorityCalls != 2 || scenario is "unexpired" or "already-cancelled") return;
                Assert.AreEqual(1, context.Runner.Calls, "Only the final successful producer observation may arm this actor.");
                Assert.AreEqual(1, accepted);
                if (scenario is "caller" or "caller-timeout") context.Caller.Cancel();
                context.Clock.Advance(TimeSpan.FromMinutes(WorkspaceForkApplyService.ResolveForkRestoreTimeoutMinutes()) + TimeSpan.FromSeconds(1));
                actor = true;
                expiredAtCallback = token.IsCancellationRequested;
                if (scenario == "caller-timeout") throw new TimeoutException("The owned final authority observed its expired phase.");
                // A callback may observe expiry and return normally. The phase
                // owner must check its token before reporting success.
            }
            async Task Accept(bool final, CancellationToken token)
            {
                Assert.IsTrue(final);
                accepted++;
                await context.Certificate.AssertCurrentAsync(context.Solution, context.WorkspaceId,
                    context.Services.PathPolicy, token);
            }
            Exception? observed = null;
            try
            {
                await context.Service.RunRestorePhaseAsync(token =>
                    context.Service.RestoreForkAsync(context.Plan, AssertAuthority, Accept,
                        static failure => failure, token), context.Caller.Token);
            }
            catch (Exception failure) { observed = failure; }

            if (scenario == "already-cancelled")
            {
                Assert.AreEqual(0, authorityCalls);
                Assert.AreEqual(0, context.Runner.Calls);
                Assert.AreEqual(0, accepted);
                Assert.IsFalse(actor);
            }
            else
            {
                Assert.AreEqual(2, authorityCalls, "Both actual current-authority observations must precede the verdict.");
                Assert.AreEqual(1, context.Runner.Calls);
                Assert.AreEqual(1, accepted);
                Assert.AreEqual(scenario != "unexpired", actor);
                Assert.AreEqual(scenario != "unexpired", expiredAtCallback,
                    "The actual final callback must receive the phase-linked expired token before classifying the outcome.");
            }
            CollectionAssert.AreEqual(context.AssetsBefore, await File.ReadAllBytesAsync(context.Source.Assets));
            if (scenario == "unexpired") Assert.IsNull(observed);
            else if (scenario == "phase") Assert.IsInstanceOfType<TimeoutException>(observed);
            else
            {
                Assert.IsInstanceOfType<OperationCanceledException>(observed);
                Assert.AreEqual(context.Caller.Token, ((OperationCanceledException)observed).CancellationToken);
            }
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NativeRestorePhase_InitialCaptureReceivesItsOriginalPhaseDeadline(bool callerCancellation)
    {
        MsBuildInitializer.EnsureInitialized();
        await WithNativeAsync(async context =>
        {
            var actor = false;
            var canceledAtGrant = false;
            Exception? observed = null;
            try
            {
                await context.Service.RunRestorePhaseAsync(async token =>
                {
                    var graph = EvaluatedBuildGraph.Source(context.Solution,
                        System.Collections.Immutable.ImmutableDictionary<string, byte[]>.Empty, token);
                    Assert.HasCount(1, graph.Nodes.Where(node => !node.IsOuterBuild).ToArray());
                    var inputs = await RestoreInputAuthority.CaptureAsync(graph.Nodes, graph.Nodes, context.Solution,
                        async (paths, currentToken) =>
                        {
                            await context.Services.PathPolicy.ValidateAsync(context.WorkspaceId, paths, currentToken);
                            if (!actor && paths.Contains(context.Plan.Single().TargetPath, FileSystemPath.Comparer))
                            {
                                context.Clock.Advance(TimeSpan.FromMinutes(WorkspaceForkApplyService.ResolveForkRestoreTimeoutMinutes()) + TimeSpan.FromSeconds(1));
                                actor = true;
                                canceledAtGrant = currentToken.IsCancellationRequested;
                                if (callerCancellation) context.Caller.Cancel();
                            }
                            currentToken.ThrowIfCancellationRequested();
                        }, token);
                    await inputs.AssertCurrentAsync(token);
                }, context.Caller.Token);
            }
            catch (Exception failure) { observed = failure; }

            Assert.IsTrue(actor, "The actual initial native capture must renew its selecting project grant before the verdict.");
            Assert.AreEqual(0, context.Runner.Calls, "Initial capture must not start a restore process.");
            CollectionAssert.AreEqual(context.AssetsBefore, await File.ReadAllBytesAsync(context.Source.Assets));
            Assert.IsTrue(canceledAtGrant, "The initial capture callback must receive the original expired phase token.");
            if (callerCancellation)
            {
                Assert.IsInstanceOfType<OperationCanceledException>(observed);
                Assert.AreEqual(context.Caller.Token, ((OperationCanceledException)observed).CancellationToken);
            }
            else Assert.IsInstanceOfType<TimeoutException>(observed);
        });
    }

    private static async Task WithNativeAsync(Func<NativeBudgetContext, Task> action)
    {
        NuGetInstallationFixture? source = null;
        TestServiceContainer? services = null;
        CancellationTokenSource? caller = null;
        Exception? primary = null;
        try
        {
            caller = new CancellationTokenSource();
            source = await NuGetInstallationFixture.CreateAsync("Owned.ForkPhase", "1.0.0",
                new Dictionary<string, byte[]> { ["marker.txt"] = "owned restore budget input"u8.ToArray() });
            var project = Path.Combine(source.Root, "Owner.csproj");
            await File.WriteAllTextAsync(Path.Combine(source.Root, "Source.cs"), "public class Source { }");
            var policy = await NativeSdkRestoreFixture.CreatePolicyAsync(project, CancellationToken.None);
            services = TestServiceContainer.Create(new ValidationServiceOptions(), new ForkInvocationPathPolicy(policy));
            var status = await services.WorkspaceManager.LoadAsync(project, source.LoadProperties, CancellationToken.None);
            var solution = services.WorkspaceManager.GetCurrentSolution(status.WorkspaceId);
            var certificate = NativeCompilerContextCertificate.Require(solution);
            Assert.AreEqual(NativeCompilerContextAvailability.Certified, certificate.Availability);
            await certificate.AssertCurrentAsync(solution, status.WorkspaceId, services.PathPolicy, CancellationToken.None);
            var graph = EvaluatedBuildGraph.Source(solution, System.Collections.Immutable.ImmutableDictionary<string, byte[]>.Empty,
                CancellationToken.None);
            var plan = RestoreInvocationPlanner.Plan(project, RestoreInvocationPlanner.CaptureNodes(graph.Nodes, CancellationToken.None));
            Assert.HasCount(1, plan);
            Assert.AreEqual(project, plan[0].TargetPath);
            Assert.AreEqual(source.Cache, RestoreStalenessDetector.ReadRequiredRestorePackagesPathFromAssets(source.Assets, CancellationToken.None));
            var runner = new RestoreRecorder();
            var clock = new FakeTimeProvider();
            var service = new WorkspaceForkApplyService(services.PathPolicy, services.WorkspaceManager,
                services.WorkspaceExecutionGate, services.PreviewStore, services.WorkspaceValidationService,
                services.TestRunnerService, runner, NullLogger<WorkspaceForkApplyService>.Instance,
                PhysicalGraphNamespace.ForkParent, clock);
            await action(new(source, services, status.WorkspaceId, solution, certificate, plan, runner, clock, caller,
                service, await File.ReadAllBytesAsync(source.Assets)));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Native fork restore phase fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => services?.Dispose()),
                CleanupFailureCollector.FromAction(() => source?.Dispose()),
                CleanupFailureCollector.FromAction(() => caller?.Dispose()));
        }
    }

    private sealed record NativeBudgetContext(NuGetInstallationFixture Source, TestServiceContainer Services,
        string WorkspaceId, Solution Solution, NativeCompilerContextCertificate Certificate,
        IReadOnlyList<RestoreInvocationPlanner.Invocation> Plan, RestoreRecorder Runner, FakeTimeProvider Clock,
        CancellationTokenSource Caller, WorkspaceForkApplyService Service, byte[] AssetsBefore);

    private sealed class RestoreRecorder : CannedCommandRunner
    {
        internal int Calls { get; private set; }
        public override Task<CommandExecutionDto> RunAsync(string workingDirectory, string targetPath,
            IReadOnlyList<string> arguments, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Assert.AreEqual("restore", arguments[0]);
            Calls++;
            return Task.FromResult(new CommandExecutionDto("dotnet", arguments, workingDirectory, targetPath, 0, true, 1, "", ""));
        }
    }
}
