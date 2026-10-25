using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class WorkspaceExplicitReloadTests
{
    [TestMethod]
    [DataRow(false, PreviewStore.DefaultMaxVersionSpan)]
    [DataRow(true, PreviewStore.DefaultMaxVersionSpan)]
    [DataRow(false, 3)]
    [DataRow(true, 3)]
    public async Task PublicReload_RefreshesOnceAndPreservesExactPreviewRange(bool stale, int versionSpan)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var root = Path.Combine(TestTempRoot.Current, "explicit-reload-" + Guid.NewGuid().ToString("N"));
        var project = Path.Combine(root, "Entry.csproj");
        var source = Path.Combine(root, "Entry.cs");
        const string original = "public class Entry { public const int Value = 1; }";
        const string changed = "public class Entry { public const int Value = 2; }";
        FileWatcherService? watcher = null;
        WorkspaceManager? manager = null;
        WorkspaceExecutionGate? gate = null;
        Exception? primaryFailure = null;
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit></PropertyGroup></Project>");
            File.WriteAllText(source, original);
            var store = new PreviewStore(maxVersionSpan: versionSpan);
            watcher = new FileWatcherService(NullLogger<FileWatcherService>.Instance);
            manager = new WorkspaceManager(NullLogger<WorkspaceManager>.Instance, store, watcher,
                options: null, cacheStore: null, sessionLoader: null, ownsFileWatcher: false);
            gate = new WorkspaceExecutionGate(new ExecutionGateOptions(), manager);
            var loaded = await manager.LoadAsync(project, timeout.Token);
            var solution = manager.GetCurrentSolution(loaded.WorkspaceId);
            var document = solution.Projects.Single().Documents.Single(d => d.FilePath == source);
            Assert.AreEqual(original, (await document.GetTextAsync(timeout.Token)).ToString());
            var token = store.Store(loaded.WorkspaceId, solution, loaded.WorkspaceVersion, "reload range");
            if (stale)
            {
                await File.WriteAllTextAsync(source, changed, timeout.Token);
                await watcher.WaitForStaleAsync(loaded.WorkspaceId, timeout.Token);
                Assert.IsTrue(manager.GetStatus(loaded.WorkspaceId).IsStale);
            }
            else
            {
                Assert.IsFalse(manager.GetStatus(loaded.WorkspaceId).IsStale);
            }

            for (var transition = 1; transition <= versionSpan + 1; transition++)
            {
                var json = await WorkspaceTools.ReloadWorkspace(gate, manager, new RejectingExecutor(),
                    new ValidationServiceOptions(), loaded.WorkspaceId, autoRestore: false, ct: timeout.Token);
                using var response = JsonDocument.Parse(json);
                Assert.AreEqual(loaded.WorkspaceVersion + transition,
                    response.RootElement.GetProperty("workspaceVersion").GetInt32(),
                    "One public reload must produce exactly one native transition.");
                Assert.AreEqual(loaded.WorkspaceVersion + transition, manager.GetCurrentVersion(loaded.WorkspaceId));
                var refreshed = manager.GetCurrentSolution(loaded.WorkspaceId).Projects.Single()
                    .Documents.Single(d => d.FilePath == source);
                Assert.AreEqual(stale ? changed : original, (await refreshed.GetTextAsync(timeout.Token)).ToString());
                if (transition <= versionSpan)
                    Assert.AreEqual(loaded.WorkspaceId, store.PeekWorkspaceId(token), "Keep the token through its pinned ceiling.");
                else
                    Assert.IsNull(store.PeekWorkspaceId(token), "Expire the token only after crossing its configured ceiling.");
            }
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
            throw;
        }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Explicit reload fixture cleanup failed.", primaryFailure,
                async () => { if (manager is not null) await manager.DisposeAsync(); },
                CleanupFailureCollector.FromAction(() => gate?.Dispose()),
                CleanupFailureCollector.FromAction(() => watcher?.Dispose()),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    private sealed class RejectingExecutor : IGatedCommandExecutor
    {
        public Task<CommandExecutionDto> ExecuteAsync(string workspaceId, string targetPath,
            IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken ct)
            => throw new AssertFailedException("autoRestore:false must never execute a restore.");

        public ProjectStatusDto ResolveProject(string workspaceId, string projectName)
            => throw new AssertFailedException("Reload must not resolve a command project.");

        public void Dispose() { }
    }
}
