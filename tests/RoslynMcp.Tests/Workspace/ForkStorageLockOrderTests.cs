using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ForkStorageLockOrderTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ActualReload_CanFinishBeforeCopyOrDuringForkLoad(bool afterCopy)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var forkCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var root = Path.Combine(TestTempRoot.Current, "fork-order-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "pool", "20000101T000000000Z-source");
        var project = Path.Combine(source, "Entry.csproj");
        Directory.CreateDirectory(source);
        File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><EnableDefaultItems>false</EnableDefaultItems><NuGetAudit>false</NuGetAudit></PropertyGroup></Project>");
        var commands = new DotnetCommandRunner();
        var restore = await commands.RunAsync(source, project, ["restore", project, "--nologo", "-nr:false"], timeout.Token);
        Assert.IsTrue(restore.Succeeded, restore.StdOut + restore.StdErr);
        ForkAdmissionMaterial.Install(root, source, [project], timeout.Token);
        var loader = new PausedLoader();
        ObservedGate? gate = null;
        var store = new PreviewStore();
        await using var manager = new WorkspaceManager(NullLogger<WorkspaceManager>.Instance, store,
            new FileWatcherService(NullLogger<FileWatcherService>.Instance),
            new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 4 }, cacheStore: null, sessionLoader: loader,
            evictionGate: new Lazy<IWorkspaceExecutionGate>(() => gate!));
        using var actualGate = new WorkspaceExecutionGate(new ExecutionGateOptions(), manager);
        gate = new ObservedGate(actualGate);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRoslynMcpHostServices(new WorkspaceManagerOptions(), new ValidationServiceOptions(),
            new PreviewStoreOptions(), new ExecutionGateOptions(), new SecurityOptions(), new ScriptingServiceOptions());
        services.AddSingleton<IWorkspaceManager>(manager);
        services.AddSingleton<IWorkspaceExecutionGate>(gate);
        services.AddSingleton<IPreviewStore>(store);
        services.AddSingleton<ForkStoragePathValidator>(async (id, path, ct) =>
        {
            var canonical = await ForkStorageFixtureBoundary.ValidateAsync(id, path, ct);
            if (!afterCopy && Interlocked.Increment(ref loader.AuthorizationCalls) == 2)
            {
                loader.Authorized.TrySetResult();
                await loader.ContinueAuthorization.Task.WaitAsync(ct);
            }
            return canonical;
        });
        await using var provider = services.BuildServiceProvider();
        Task<string>? reload = null;
        Task<WorkspaceForkApplyResultDto>? apply = null;
        WorkspaceForkApplyResultDto? result = null;
        try
        {
            var loaded = await manager.LoadAsync(project, timeout.Token);
            var solution = manager.GetCurrentSolution(loaded.WorkspaceId);
            var token = store.Store(loaded.WorkspaceId, solution, solution, loaded.WorkspaceVersion, "ordering", false, PreviewKind.Unspecified);
            loader.PausePath = afterCopy ? null : project;
            loader.PauseNewPath = afterCopy;
            gate.SourceId = loaded.WorkspaceId;
            apply = provider.GetRequiredService<IWorkspaceForkApplyService>().ApplyAsync(loaded.WorkspaceId,
                token, "keep", false, null, "next", forkCancellation.Token);
            if (afterCopy)
                await loader.Entered.Task.WaitAsync(timeout.Token);
            else
                await loader.Authorized.Task.WaitAsync(timeout.Token);
            reload = WorkspaceTools.ReloadWorkspace(gate, manager, provider.GetRequiredService<IGatedCommandExecutor>(),
                new ValidationServiceOptions(), loaded.WorkspaceId, autoRestore: false, ct: timeout.Token);
            if (!afterCopy)
            {
                await loader.Entered.Task.WaitAsync(timeout.Token);
                loader.ContinueAuthorization.TrySetResult();
                await gate.CopyRequested.Task.WaitAsync(timeout.Token);
                TestContext.WriteLine($"At copy request sourceOwnership={WorkspaceForkApplyService.HasForkApplyLock(source)} reloadCompleted={reload.IsCompleted}");
                loader.Release.TrySetResult();
            }
            // The post-copy loader remains paused here: no source/ancestor physical
            // lock may extend across fork loading or its later LRU/validation work.
            await reload.WaitAsync(TimeSpan.FromSeconds(15), timeout.Token);
            if (afterCopy)
            {
                Assert.IsFalse(apply.IsCompleted);
                loader.Release.TrySetResult();
                result = await apply.WaitAsync(timeout.Token);
                Assert.IsTrue(result.Success);
            }
            else
            {
                Assert.IsTrue(manager.GetCurrentVersion(loaded.WorkspaceId) > loaded.WorkspaceVersion);
                await Assert.ThrowsAsync<PreviewTokenStaleException>(() => apply);
                Assert.AreEqual(1, Directory.GetDirectories(Path.GetDirectoryName(source)!).Length,
                    "A stale preview must be refused before creating any fork destination.");
            }
        }
        finally
        {
            loader.ContinueAuthorization.TrySetResult();
            loader.Release.TrySetResult();
            forkCancellation.Cancel();
            if (apply is not null)
            {
                try { result = await apply.WaitAsync(timeout.Token); }
                catch (OperationCanceledException) when (forkCancellation.IsCancellationRequested) { }
                catch (PreviewTokenStaleException) { }
            }
            if (reload is not null) await reload.WaitAsync(timeout.Token);
            if (result?.ForkWorkspaceId is { } id) manager.Close(id);
            await provider.DisposeAsync();
            await manager.DisposeAsync();
            TestFixtureFileSystem.DeleteDirectoryIfExists(root);
        }
    }

    private sealed class PausedLoader : WorkspaceSessionLoader
    {
        private int _loads;
        internal string? PausePath;
        internal bool PauseNewPath;
        internal int AuthorizationCalls;
        internal TaskCompletionSource Authorized { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ContinueAuthorization { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async Task<(MSBuildWorkspace Workspace, AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease Lease)> CreateAndOpenAsync(
            string workspaceId, string path, IDictionary<string, string>? globalProperties,
            WorkspaceDiagnosticsSink diagnostics, ILogger logger, CancellationToken ct)
        {
            if (Interlocked.Increment(ref _loads) > 1 && (PauseNewPath || FileSystemPath.Comparer.Equals(path, PausePath)))
            {
                PauseNewPath = false;
                PausePath = null;
                Entered.TrySetResult();
                await Release.Task.WaitAsync(ct);
            }
            return await base.CreateAndOpenAsync(workspaceId, path, globalProperties, diagnostics, logger, ct);
        }
    }

    private sealed class ObservedGate(WorkspaceExecutionGate inner) : IWorkspaceExecutionGate
    {
        internal string? SourceId;
        internal TaskCompletionSource CopyRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<T> RunReadAsync<T>(string id, Func<CancellationToken, Task<T>> action, CancellationToken ct) => inner.RunReadAsync(id, action, ct);
        public Task<T> RunWriteAsync<T>(string id, Func<CancellationToken, Task<T>> action, CancellationToken ct, bool applyStalenessPolicy = true)
        {
            if (id == SourceId && !applyStalenessPolicy) CopyRequested.TrySetResult();
            return inner.RunWriteAsync(id, action, ct, applyStalenessPolicy);
        }
        public Task<T> RunLoadGateAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct) => inner.RunLoadGateAsync(action, ct);
        public void RemoveGate(string id) => inner.RemoveGate(id);
    }
}
