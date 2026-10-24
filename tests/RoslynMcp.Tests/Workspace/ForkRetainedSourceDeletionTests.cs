using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ForkRetainedSourceDeletionTests
{
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    public async Task Sweep_ActualPoolWaiter_PreservesRetainedSource(bool nested, bool overrideManager)
    {
        await using var fixture = new Fixture(overrideManager);
        var original = fixture.OriginalManager;
        var originalStatus = original is null ? null : await original.LoadAsync(fixture.Project, fixture.Token);
        var originalSolution = originalStatus is null ? null : original!.GetCurrentSolution(originalStatus.WorkspaceId);
        var sourceA = await fixture.Manager.LoadAsync(fixture.Project, fixture.Token);
        var victim = await fixture.CreateExpiredAsync("active", nested);
        var inactive = await fixture.CreateExpiredAsync("inactive", false);
        var sourceB = await fixture.Manager.LoadAsync(victim.Project, fixture.Token);
        var authority = fixture.Capture(sourceB.WorkspaceId);
        var tokenA = fixture.Preview(sourceA.WorkspaceId);
        fixture.Gate.PauseId = sourceA.WorkspaceId;
        fixture.ObserveId = sourceB.WorkspaceId;
        Task<WorkspaceForkApplyResultDto>? a = null;
        Task<WorkspaceForkApplyResultDto>? b = null;
        Exception? primaryFailure = null;
        try
        {
            a = fixture.Service.ApplyAsync(sourceA.WorkspaceId, tokenA, "keep", false, null, "a", fixture.Token);
            await fixture.Gate.Entered.Task.WaitAsync(fixture.Token);
            Assert.IsTrue(WorkspaceForkApplyService.HasForkApplyLock(fixture.Pool), "A must own the actual canonical pool before B registers.");
            b = fixture.Service.ApplyAsync(sourceB.WorkspaceId, authority.PreviewToken, "keep", false, null, "b", fixture.Token);
            await fixture.Authorized.Task.WaitAsync(fixture.Token);
            Assert.IsFalse(b.IsCompleted, "Actual B must wait for A's pool after registering source retention.");
            fixture.Gate.Release.TrySetResult();
            Assert.IsTrue((await a.WaitAsync(fixture.Token)).Success);
            authority.AssertPreserved(fixture);
            Assert.IsFalse(Directory.Exists(inactive.Root), "Inactive verified expired material remains collectible.");
            Assert.IsTrue((await b.WaitAsync(fixture.Token)).Success, "B must subsequently copy its real retained source.");
            authority.AssertPreserved(fixture);
            if (originalStatus is not null)
            {
                Assert.AreEqual(1, original!.ListWorkspaces().Count);
                Assert.AreSame(originalSolution, original.GetCurrentSolution(originalStatus.WorkspaceId));
            }
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
            throw;
        }
        finally
        {
            fixture.Gate.Release.TrySetResult();
            fixture.Cancel();
            await DrainAsync(primaryFailure, a, b);
        }
    }

    [TestMethod]
    public async Task Cleanup_ActualPoolWaiter_PreservesRetainedSource()
    {
        await using var fixture = new Fixture(false);
        var source = await fixture.Manager.LoadAsync(fixture.Project, fixture.Token);
        var paused = new PausedValidation(fixture.Provider.GetRequiredService<IWorkspaceValidationService>());
        await using var first = fixture.CreateService(paused);
        Task<WorkspaceForkApplyResultDto>? a = null;
        Task<WorkspaceForkApplyResultDto>? b = null;
        Exception? primaryFailure = null;
        try
        {
            a = first.ApplyAsync(source.WorkspaceId, fixture.Preview(source.WorkspaceId), "drop-always", false, null, "a", fixture.Token);
            var forkId = await paused.Entered.Task.WaitAsync(fixture.Token);
            var authority = fixture.Capture(forkId);
            fixture.ObserveId = forkId;
            Assert.IsTrue(WorkspaceForkApplyService.HasForkApplyLock(fixture.Pool));
            b = fixture.Service.ApplyAsync(forkId, authority.PreviewToken, "keep", false, null, "b", fixture.Token);
            await fixture.Authorized.Task.WaitAsync(fixture.Token);
            Assert.IsFalse(b.IsCompleted);
            paused.Release.TrySetResult();
            var result = await a.WaitAsync(fixture.Token);
            authority.AssertPreserved(fixture);
            Assert.IsNotEmpty(result.CleanupWarnings, "Cleanup must report that its source was retained.");
            Assert.IsTrue((await b.WaitAsync(fixture.Token)).Success);
            authority.AssertPreserved(fixture);
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
            throw;
        }
        finally
        {
            paused.Release.TrySetResult();
            fixture.Cancel();
            await DrainAsync(primaryFailure, a, b);
        }
    }

    private static async Task DrainAsync(Exception? primaryFailure, params Task<WorkspaceForkApplyResultDto>?[] tasks)
    {
        await CleanupFailureCollector.RunAfterFailureAsync("Fork race operation cleanup failed.", primaryFailure,
            async () =>
            {
                try { await Task.WhenAll(tasks.OfType<Task<WorkspaceForkApplyResultDto>>()); }
                catch (OperationCanceledException) when (tasks.OfType<Task>().All(task => task.IsCompleted))
                {
                    // Finally deliberately cancels outstanding waits; faults still propagate.
                    var faulted = tasks.OfType<Task>().Where(task => task.IsFaulted).ToArray();
                    if (faulted.Length > 0) await Task.WhenAll(faulted);
                }
            });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DeletionWinner_RefusesActualApplyBeforeRegistrationOrCopy(bool alias)
    {
        await using var fixture = new Fixture(false);
        var victim = await fixture.CreateExpiredAsync("deleting", true);
        var loaded = await fixture.Manager.LoadAsync(victim.Project, fixture.Token);
        var authority = fixture.Capture(loaded.WorkspaceId);
        fixture.ObserveId = loaded.WorkspaceId;
        var deletionPath = victim.Root;
        if (alias)
        {
            deletionPath = Path.Combine(fixture.Root, "alias");
            WorkspaceForkGraphReplayTests.CreateLink(deletionPath, victim.Root);
        }
        using (var reservation = fixture.Manager.TryReserveForkStorageDeletion(deletionPath))
        {
            Assert.IsNotNull(reservation);
            var refusal = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => fixture.Service.ApplyAsync(
                loaded.WorkspaceId, authority.PreviewToken, "keep", false, null, "loser", fixture.Token));
            StringAssert.Contains(refusal.Message, "source");
            Assert.IsFalse(fixture.Authorized.Task.IsCompleted, "Deletion admission must refuse before source callback/authorization/copy.");
            authority.AssertPreserved(fixture);
            WorkspaceForkApplyService.DeleteDirectoryIfExists(victim.Root, fixture.Token);
        }
        await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => fixture.Service.ApplyAsync(
            loaded.WorkspaceId, authority.PreviewToken, "keep", false, null, "missing", fixture.Token));
        Assert.IsFalse(fixture.Authorized.Task.IsCompleted, "A loaded session cannot claim missing backing material after deletion completes.");
        Assert.AreSame(authority.Entry, FullEntry(fixture.Store, authority.PreviewToken));
    }

    [TestMethod]
    public async Task MultipleRegisteredSources_BlockEnclosingDeletionUntilBothFinish()
    {
        await using var fixture = new Fixture(false);
        var victim = await fixture.CreateExpiredAsync("multiple", true);
        var first = await fixture.Manager.LoadAsync(victim.Project, fixture.Token);
        var second = await fixture.Manager.LoadAsync(victim.Project,
            new Dictionary<string, string> { ["Configuration"] = "Debug" }, fixture.Token);
        Assert.AreNotEqual(first.WorkspaceId, second.WorkspaceId);
        var firstAuthority = fixture.Capture(first.WorkspaceId);
        var secondAuthority = fixture.Capture(second.WorkspaceId);
        using var poolOwnership = await WorkspaceForkApplyService.AcquireForkApplyLockAsync(fixture.Pool, fixture.Token);
        using var firstCancellation = CancellationTokenSource.CreateLinkedTokenSource(fixture.Token);
        using var secondCancellation = CancellationTokenSource.CreateLinkedTokenSource(fixture.Token);
        Task<WorkspaceForkApplyResultDto>? firstApply = null;
        Task<WorkspaceForkApplyResultDto>? secondApply = null;
        Exception? primaryFailure = null;
        try
        {
            fixture.ObserveId = first.WorkspaceId;
            firstApply = fixture.Service.ApplyAsync(first.WorkspaceId, firstAuthority.PreviewToken, "keep", false, null, "first", firstCancellation.Token);
            await fixture.Authorized.Task.WaitAsync(fixture.Token);
            fixture.Authorized = new(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.ObserveId = second.WorkspaceId;
            secondApply = fixture.Service.ApplyAsync(second.WorkspaceId, secondAuthority.PreviewToken, "keep", false, null, "second", secondCancellation.Token);
            await fixture.Authorized.Task.WaitAsync(fixture.Token);
            Assert.IsNull(fixture.Manager.TryReserveForkStorageDeletion(victim.Root));
            firstCancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => firstApply);
            Assert.IsNull(fixture.Manager.TryReserveForkStorageDeletion(victim.Root), "The second exact session still owns its logical source.");
            secondCancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => secondApply);
            using var deletion = fixture.Manager.TryReserveForkStorageDeletion(victim.Root);
            Assert.IsNotNull(deletion, "Cancellation must release each exact session registration.");
            firstAuthority.AssertPreserved(fixture);
            secondAuthority.AssertPreserved(fixture);
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
            throw;
        }
        finally
        {
            firstCancellation.Cancel();
            secondCancellation.Cancel();
            await DrainAsync(primaryFailure, firstApply, secondApply);
        }
    }

    [TestMethod]
    public async Task ActiveSourceAliasRetarget_RefusesSecondRegistrationAndFirstCopy()
    {
        await using var fixture = new Fixture(false);
        var first = await fixture.CreateExpiredAsync("original", true);
        var replacement = await fixture.CreateExpiredAsync("replacement", false);
        var alias = Path.GetDirectoryName(first.Project)!;
        var heldSource = alias + "-held";
        var loaded = await fixture.Manager.LoadAsync(first.Project, fixture.Token);
        Assert.AreEqual(first.Project, loaded.LoadedPath);
        var authority = fixture.Capture(loaded.WorkspaceId);
        fixture.ObserveId = loaded.WorkspaceId;
        fixture.Gate.PauseId = loaded.WorkspaceId;
        Task<WorkspaceForkApplyResultDto>? firstApply = null;
        Task<WorkspaceForkApplyResultDto>? secondApply = null;
        Exception? primaryFailure = null;
        try
        {
            firstApply = fixture.Service.ApplyAsync(loaded.WorkspaceId, authority.PreviewToken, "keep", false, null, "first", fixture.Token);
            await fixture.Gate.Entered.Task.WaitAsync(fixture.Token);
            Assert.IsFalse(firstApply.IsCompleted);
            Assert.IsTrue(WorkspaceForkApplyService.HasForkApplyLock(fixture.Pool),
                "The actual writer checkpoint follows original source-identity capture and pool acquisition.");
            Directory.Move(alias, heldSource);
            WorkspaceForkGraphReplayTests.CreateLink(alias, replacement.Root);
            secondApply = fixture.Service.ApplyAsync(loaded.WorkspaceId, authority.PreviewToken, "keep", false, null, "second", fixture.Token);
            Assert.IsTrue(secondApply.IsCompleted, "A second registration must immediately refuse a different physical source, without replacing the original retention.");
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => secondApply);
            secondApply = null; // Its expected refusal is observed above.
            Assert.IsNull(fixture.Manager.TryReserveForkStorageDeletion(first.Root), "The first operation still retains its original physical source.");
            using (var replacementAdmission = fixture.Manager.TryReserveForkStorageDeletion(replacement.Root))
                Assert.IsNotNull(replacementAdmission, "A refused registration must not claim the replacement physical source.");
            fixture.Gate.Release.TrySetResult();
            var refusal = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => firstApply);
            firstApply = null; // Existing in-writer physical revalidation refused before copy.
            StringAssert.Contains(refusal.Message, "source changed");
            using (var admission = fixture.Manager.TryReserveForkStorageDeletion(first.Root)) Assert.IsNotNull(admission);
            Directory.Delete(alias);
            Directory.Move(heldSource, alias);
            authority.AssertPreserved(fixture);
            Assert.AreEqual(2, Directory.GetDirectories(fixture.Pool).Length, "Neither retargeted operation may create a destination.");
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
            throw;
        }
        finally
        {
            fixture.Cancel();
            fixture.Gate.Release.TrySetResult();
            await DrainAsync(primaryFailure, firstApply, secondApply);
        }
    }

    [TestMethod]
    public async Task Cleanup_FailedClose_RetainsMaterialAndReportsWarning()
    {
        await using var fixture = new Fixture(false);
        var victim = await fixture.CreateExpiredAsync("close-refused", false);
        var source = await fixture.Manager.LoadAsync(victim.Project, fixture.Token);
        var authority = fixture.Capture(source.WorkspaceId);
        await using var service = new WorkspaceForkApplyService(new FailClosedWorkspaceManagerStub(), fixture.Gate,
            fixture.Store, fixture.Provider.GetRequiredService<IWorkspaceValidationService>(),
            fixture.Provider.GetRequiredService<ITestRunnerService>(), fixture.Provider.GetRequiredService<IDotnetCommandRunner>(),
            fixture.Provider.GetRequiredService<ForkStoragePathValidator>(), NullLogger<WorkspaceForkApplyService>.Instance,
            fixture.Manager.RunWithRetainedForkSourceAsync, fixture.Manager.TryReserveForkStorageDeletion);
        var warnings = new List<string>();
        typeof(WorkspaceForkApplyService).GetMethod("CleanupFork", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(service, [source.WorkspaceId, victim.Root, warnings, fixture.Token]);
        Assert.HasCount(1, warnings);
        StringAssert.Contains(warnings[0], "Failed to close");
        authority.AssertPreserved(fixture);
        using var admission = fixture.Manager.TryReserveForkStorageDeletion(victim.Root);
        Assert.IsNotNull(admission, "Failed-close cleanup must release its deletion reservation.");
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly CancellationTokenSource _timeout = new(TimeSpan.FromMinutes(3));
        private int _previewSequence = 1;
        internal string Root { get; } = Path.Combine(TestTempRoot.Current, "fork-del-" + Guid.NewGuid().ToString("N"));
        internal string Pool => Path.Combine(Root, "pool");
        internal string Project => Path.Combine(Root, "source", "Entry.csproj");
        internal CancellationToken Token => _timeout.Token;
        internal ServiceProvider Provider { get; }
        internal WorkspaceManager Manager { get; }
        internal WorkspaceManager? OriginalManager { get; }
        internal PreviewStore Store { get; }
        internal PausedGate Gate { get; }
        internal IWorkspaceForkApplyService Service { get; }
        internal string? ObserveId;
        internal TaskCompletionSource Authorized { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Fixture(bool overrideManager)
        {
            WriteProject(Project);
            Directory.CreateDirectory(Pool);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 12 });
            services.AddRoslynServices();
            if (overrideManager)
                services.AddSingleton<IWorkspaceManager>(sp => WorkspaceManager.CreateProviderOwned(
                    sp.GetRequiredService<ILogger<WorkspaceManager>>(), sp.GetRequiredService<IPreviewStore>(),
                    sp.GetRequiredService<IFileWatcherService>(), sp.GetRequiredService<WorkspaceManagerOptions>(), null,
                    sp.GetRequiredService<Lazy<IWorkspaceExecutionGate>>()));
            services.AddSingleton<IWorkspaceExecutionGate>(sp => new PausedGate(new WorkspaceExecutionGate(
                new ExecutionGateOptions(), sp.GetRequiredService<IWorkspaceManager>())));
            services.AddSingleton<ForkStoragePathValidator>(async (id, path, ct) =>
            {
                var requestedPool = Path.Combine(Path.GetDirectoryName(Project)!, ".roslynmcp", "forks");
                if (path == requestedPool) path = Pool;
                var canonical = await ForkStorageFixtureBoundary.ValidateAsync(id, path, ct);
                if (id == ObserveId && canonical == Pool) Authorized.TrySetResult();
                return canonical;
            });
            Provider = services.BuildServiceProvider();
            Manager = (WorkspaceManager)Provider.GetRequiredService<IWorkspaceManager>();
            if (overrideManager) OriginalManager = (WorkspaceManager)Provider.GetServices<IWorkspaceManager>().First();
            Store = (PreviewStore)Provider.GetRequiredService<IPreviewStore>();
            Gate = (PausedGate)Provider.GetRequiredService<IWorkspaceExecutionGate>();
            Service = Provider.GetRequiredService<IWorkspaceForkApplyService>();
        }

        internal WorkspaceForkApplyService CreateService(IWorkspaceValidationService validation) => new(
            Manager, Gate, Store, validation, Provider.GetRequiredService<ITestRunnerService>(),
            Provider.GetRequiredService<IDotnetCommandRunner>(), Provider.GetRequiredService<ForkStoragePathValidator>(),
            NullLogger<WorkspaceForkApplyService>.Instance, Manager.RunWithRetainedForkSourceAsync, Manager.TryReserveForkStorageDeletion);

        internal async Task<(string Root, string Project)> CreateExpiredAsync(string name, bool nested)
        {
            var outer = Path.Combine(Pool, "20000101T000000000Z-" + name);
            var outerProject = Path.Combine(outer, "Entry.csproj");
            WriteProject(outerProject);
            ForkAdmissionMaterial.Install(Path.GetDirectoryName(Project)!, outer, [outerProject], Token);
            var target = nested ? Path.Combine(outer, ".roslynmcp", "forks", "20000101T000000000Z-child", "Entry.csproj") : outerProject;
            if (nested)
            {
                WriteProject(target);
                ForkAdmissionMaterial.Install(outer, Path.GetDirectoryName(target)!, [target], Token);
            }
            return (outer, await RestoreAsync(target));
        }

        private async Task<string> RestoreAsync(string project)
        {
            var result = await Provider.GetRequiredService<IDotnetCommandRunner>().RunAsync(Path.GetDirectoryName(project)!, project,
                ["restore", project, "--nologo", "-nr:false"], Token);
            Assert.IsTrue(result.Succeeded, result.StdOut + result.StdErr);
            return project;
        }

        private static void WriteProject(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(path)!, "Entry.cs"), "public class Entry { public int Value => 1; }");
        }

        internal string Preview(string id)
        {
            var original = Manager.GetCurrentSolution(id);
            var document = original.Projects.SelectMany(project => project.Documents).First(document => document.Name == "Entry.cs");
            var value = Interlocked.Increment(ref _previewSequence).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var modified = original.WithDocumentText(document.Id, SourceText.From($"public class Entry {{ public int Value => {value}; }}"));
            Assert.IsTrue(modified.GetChanges(original).GetProjectChanges().Any(change => change.GetChangedDocuments().Any()),
                "Every source preview must contain a genuine document edit, including a fork used as a new source.");
            return Store.Store(id, original, modified, Manager.GetCurrentVersion(id), "source authority", false, PreviewKind.Unspecified);
        }

        internal Authority Capture(string id)
        {
            var token = Preview(id);
            var entry = FullEntry(Store, token);
            Assert.IsNotNull(entry);
            var status = Manager.GetStatus(id);
            var bytes = Directory.GetFiles(Path.GetDirectoryName(status.LoadedPath!)!, "*", SearchOption.AllDirectories)
                .ToDictionary(path => path, File.ReadAllBytes);
            return new(id, token, Manager.GetCurrentSolution(id), status, entry, bytes);
        }

        internal void Cancel() => _timeout.Cancel();
        public async ValueTask DisposeAsync()
        {
            Gate.Release.TrySetResult();
            _timeout.Cancel();
            await Provider.DisposeAsync();
            _timeout.Dispose();
            TestFixtureFileSystem.DeleteDirectoryIfExists(Root);
        }
    }

    private static object? FullEntry(PreviewStore store, string token) => typeof(BoundedStore<PreviewStore.PreviewEntry>)
        .GetMethod("RetrieveEntry", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(store, [token]);

    private sealed record Authority(string Id, string PreviewToken, Solution Solution, WorkspaceStatusDto Status, object? Entry,
        Dictionary<string, byte[]> Bytes)
    {
        internal void AssertPreserved(Fixture fixture)
        {
            Assert.IsTrue(fixture.Manager.ContainsWorkspace(Id));
            Assert.AreSame(Solution, fixture.Manager.GetCurrentSolution(Id));
            Assert.AreEqual(Status.WorkspaceVersion, fixture.Manager.GetCurrentVersion(Id));
            Assert.AreEqual(Status.SnapshotToken, fixture.Manager.GetStatus(Id).SnapshotToken);
            Assert.AreSame(Entry, FullEntry(fixture.Store, PreviewToken));
            foreach (var pair in Bytes)
            {
                Assert.IsTrue(File.Exists(pair.Key), "Retained source file deleted: " + pair.Key);
                CollectionAssert.AreEqual(pair.Value, File.ReadAllBytes(pair.Key), pair.Key);
            }
        }
    }

    private sealed class PausedGate(WorkspaceExecutionGate inner) : IWorkspaceExecutionGate, IDisposable
    {
        internal string? PauseId;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<T> RunReadAsync<T>(string id, Func<CancellationToken, Task<T>> action, CancellationToken ct) => inner.RunReadAsync(id, action, ct);
        public Task<T> RunWriteAsync<T>(string id, Func<CancellationToken, Task<T>> action, CancellationToken ct, bool applyStalenessPolicy = true) =>
            inner.RunWriteAsync(id, async token =>
            {
                if (id == PauseId && !applyStalenessPolicy)
                {
                    Entered.TrySetResult();
                    await Release.Task.WaitAsync(token);
                }
                return await action(token);
            }, ct, applyStalenessPolicy);
        public Task<T> RunLoadGateAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct) => inner.RunLoadGateAsync(action, ct);
        public void RemoveGate(string id) => inner.RemoveGate(id);
        public void Dispose() => inner.Dispose();
    }

    private sealed class PausedValidation(IWorkspaceValidationService inner) : IWorkspaceValidationService
    {
        internal TaskCompletionSource<string> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<WorkspaceValidationDto> ValidateAsync(string id, IReadOnlyList<string>? paths, bool runTests, CancellationToken ct, bool summary = false)
        {
            var result = await inner.ValidateAsync(id, paths, runTests, ct, summary);
            Entered.TrySetResult(id);
            await Release.Task.WaitAsync(ct);
            return result;
        }
        public Task<WorkspaceValidationPlan> PrepareValidationAsync(string id, IReadOnlyList<string>? paths, bool recentGitChanges, bool summary, CancellationToken ct) => inner.PrepareValidationAsync(id, paths, recentGitChanges, summary, ct);
        public Task<WorkspaceValidationDto> CompleteValidationTestsAsync(WorkspaceValidationPlan plan, CancellationToken ct) => inner.CompleteValidationTestsAsync(plan, ct);
        public Task<WorkspaceValidationDto> ValidateRecentGitChangesAsync(string id, bool runTests, CancellationToken ct, bool summary = false) => inner.ValidateRecentGitChangesAsync(id, runTests, ct, summary);
    }
}
