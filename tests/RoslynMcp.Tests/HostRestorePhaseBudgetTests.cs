using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

public sealed partial class HostRestoreAuthorityTests
{
    [TestMethod]
    public Task HostRestore_PhaseDeadlineCancelsInitialCaptureAuthority() =>
        WithFixtureAsync(false, async fixture =>
        {
            var clock = new FakeTimeProvider();
            var authorize = fixture.AuthorizePaths;
            var entered = false;
            var cancelledAtGrant = false;
            fixture.AuthorizePaths = async (paths, token) =>
            {
                await authorize(paths, token);
                if (entered) return;
                entered = true;
                clock.Advance(TimeSpan.FromMinutes(2));
                cancelledAtGrant = token.IsCancellationRequested;
                token.ThrowIfCancellationRequested();
            };
            await WithBudgetExecutorAsync(fixture, async executor =>
                await Assert.ThrowsExactlyAsync<TimeoutException>(() => RestoreWithClockAsync(fixture, executor, clock,
                    autoRestore: true, CancellationToken.None)));
            Assert.IsTrue(entered, "The actual initial current-policy grant must run inside the restore phase.");
            Assert.IsTrue(cancelledAtGrant, "The initial capture callback must receive the phase's expired cancellation token.");
            Assert.AreEqual(0, fixture.Runner.RestoreCalls);
            Assert.AreEqual(0, fixture.ReloadCalls);
        });

    [TestMethod]
    [DataRow(true)]
    [DataRow(null)]
    public Task HostRestore_FinalProducerCannotOutlivePhaseDeadline(bool? autoRestore) =>
        WithFixtureAsync(false, async fixture =>
        {
            if (autoRestore is null)
            {
                var definition = System.Xml.Linq.XDocument.Load(fixture.OwnerPath);
                definition.Root!.Add(new System.Xml.Linq.XElement("ItemGroup", new System.Xml.Linq.XElement("PackageReference",
                    new System.Xml.Linq.XAttribute("Include", "Owned.Host.Budget"), new System.Xml.Linq.XAttribute("Version", "1.0.0"))));
                definition.Root.Element("PropertyGroup")!.Add(new System.Xml.Linq.XElement("RestorePackagesPath",
                    Microsoft.Build.Evaluation.ProjectCollection.Escape(fixture.Cache)));
                await Helpers.NativeSdkRestoreFixture.WriteProjectAsync(fixture.OwnerPath, definition.ToString(), CancellationToken.None);
                File.Delete(fixture.Assets(fixture.OwnerPath));
                var graph = EvaluatedBuildGraph.Source(fixture.Solution, CancellationToken.None);
                Assert.IsTrue(graph.Nodes.Any(node => node.Inputs.Packages.Count > 0),
                    "Omitted autoRestore selects missing assets only for an actual active package declaration.");
                Assert.IsTrue(await RestoreStalenessDetector.HasMissingAssetsAsync(graph, fixture.AuthorizePaths, CancellationToken.None));
            }
            var clock = new FakeTimeProvider();
            fixture.Runner.AfterRestore = _ =>
            {
                clock.Advance(TimeSpan.FromMinutes(2));
                return Task.CompletedTask;
            };
            await WithBudgetExecutorAsync(fixture, async executor =>
            {
                if (autoRestore is true)
                    await Assert.ThrowsExactlyAsync<TimeoutException>(() => RestoreWithClockAsync(fixture, executor, clock,
                        autoRestore, CancellationToken.None));
                else
                {
                    var outcome = await RestoreWithClockAsync(fixture, executor, clock, autoRestore, CancellationToken.None);
                    Assert.AreEqual(1, fixture.Runner.RestoreCalls, "The omitted-policy control must reach its actual restore producer before testing expiry classification. Actual preproducer reason: " + outcome.FailureReason);
                    Assert.IsNotNull(outcome.FailureReason, "Omitted autoRestore must report the expired phase instead of reloading.");
                }
            });
            Assert.AreEqual(1, fixture.Runner.RestoreCalls, "The real current authority must admit the final producer before its phase expires.");
            Assert.AreEqual(0, fixture.ReloadCalls, "An expired restore phase cannot proceed to native reload.");
        });

    [TestMethod]
    public Task HostRestore_FinalProducerPreservesAmbientReloadReserve() =>
        WithFixtureAsync(false, async fixture =>
        {
            var clock = new FakeTimeProvider();
            using var deadline = RequestDeadline.Begin(clock, TimeSpan.FromSeconds(90));
            fixture.Runner.AfterRestore = _ =>
            {
                clock.Advance(TimeSpan.FromSeconds(51));
                return Task.CompletedTask;
            };
            await WithBudgetExecutorAsync(fixture, async executor =>
                await Assert.ThrowsExactlyAsync<TimeoutException>(() => WorkspaceTools.RestoreAndReloadIfRequiredAsync(
                    executor, new ValidationServiceOptions { RestoreTimeout = TimeSpan.FromMinutes(1), RestoreReloadReserve = TimeSpan.FromSeconds(40) },
                    fixture.Manager, fixture.Status, true, NullLogger.Instance, CancellationToken.None, clock)));
            Assert.AreEqual(1, fixture.Runner.RestoreCalls);
            Assert.AreEqual(0, fixture.ReloadCalls);
        });

    [TestMethod]
    public Task HostRestore_CallerCancellationWinsSimultaneousPhaseExpiry() =>
        WithFixtureAsync(false, async fixture =>
        {
            var clock = new FakeTimeProvider();
            using var caller = new CancellationTokenSource();
            fixture.Runner.AfterRestore = _ =>
            {
                caller.Cancel();
                clock.Advance(TimeSpan.FromMinutes(2));
                return Task.CompletedTask;
            };
            await WithBudgetExecutorAsync(fixture, async executor =>
            {
                var failure = await Assert.ThrowsAsync<OperationCanceledException>(() =>
                    RestoreWithClockAsync(fixture, executor, clock, true, caller.Token));
                Assert.AreEqual(caller.Token, failure.CancellationToken);
            });
            Assert.AreEqual(1, fixture.Runner.RestoreCalls);
            Assert.AreEqual(0, fixture.ReloadCalls);
        });

    [TestMethod]
    public Task HostRestore_UnexpiredPhaseRetainsSuccessfulReload() =>
        WithFixtureAsync(false, async fixture =>
        {
            await WithBudgetExecutorAsync(fixture, async executor =>
            {
                var outcome = await RestoreWithClockAsync(fixture, executor, new FakeTimeProvider(), true, CancellationToken.None);
                Assert.IsNull(outcome.FailureReason);
            });
            Assert.AreEqual(1, fixture.Runner.RestoreCalls);
            Assert.AreEqual(1, fixture.ReloadCalls);
        });

    private static Task<WorkspaceTools.RestoreReloadOutcome> RestoreWithClockAsync(Fixture fixture, IGatedCommandExecutor executor,
        TimeProvider clock, bool? autoRestore, CancellationToken ct) => WorkspaceTools.RestoreAndReloadIfRequiredAsync(executor,
            new ValidationServiceOptions { RestoreTimeout = TimeSpan.FromMinutes(1) }, fixture.Manager, fixture.Status,
            autoRestore, NullLogger.Instance, ct, clock);

    private static async Task WithBudgetExecutorAsync(Fixture fixture, Func<IGatedCommandExecutor, Task> inspect)
    {
        var executor = fixture.CreateExecutor();
        Exception? primary = null;
        try { await inspect(executor); }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await Helpers.CleanupFailureCollector.RunAfterFailureAsync("Host restore budget executor cleanup failed.", primary,
                Helpers.CleanupFailureCollector.FromAction(executor.Dispose));
        }
    }
}
