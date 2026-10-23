using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression for <c>workspace-load-missing-assets-auto-restore</c>: an omitted <c>autoRestore</c>
/// restores only never-restored projects (missing <c>project.assets.json</c>) and never fails the
/// load; <c>true</c> restores for any restoreRequired and fails the call; <c>false</c> never
/// restores. A scripted <see cref="IGatedCommandExecutor"/> stands in for <c>dotnet restore</c>.
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class WorkspaceLoadAutoRestoreTests : SharedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [TestMethod]
    public async Task Omitted_MissingAssets_RestoresAndReloads()
    {
        await WithWorkspace(assets: AssetsState.Missing, async (manager, status, root) =>
        {
            var executor = new ScriptedExecutor(ExecutionResult.Success);

            var outcome = await WorkspaceTools.RestoreAndReloadIfRequiredAsync(
                executor, ValidationOptions, manager, status, autoRestore: null, NullLogger.Instance, CancellationToken.None, timeProvider:System.TimeProvider.System);

            Assert.HasCount(status.ProjectCount, executor.Invocations, "The never-restored graph must restore each evaluated project context.");
            Assert.IsNull(outcome.FailureReason);
        });
    }

    [TestMethod]
    public async Task Omitted_AssetsPresentButStale_DoesNotRestore()
    {
        await WithWorkspace(assets: AssetsState.PresentStale, async (manager, status, root) =>
        {
            var executor = new ScriptedExecutor(ExecutionResult.Success);

            var outcome = await WorkspaceTools.RestoreAndReloadIfRequiredAsync(
                executor, ValidationOptions, manager, status, autoRestore: null, NullLogger.Instance, CancellationToken.None, timeProvider:System.TimeProvider.System);

            Assert.IsEmpty(executor.Invocations, "Package drift alone must not spawn a restore on the default path.");
            Assert.IsTrue(outcome.Status.RestoreRequired);
            Assert.IsNull(outcome.FailureReason);
        });
    }

    [TestMethod]
    public async Task True_AssetsPresentButStale_Restores()
    {
        await WithWorkspace(assets: AssetsState.PresentStale, async (manager, status, root) =>
        {
            var executor = new ScriptedExecutor(ExecutionResult.Success);

            await WorkspaceTools.RestoreAndReloadIfRequiredAsync(
                executor, ValidationOptions, manager, status, autoRestore: true, NullLogger.Instance, CancellationToken.None, timeProvider:System.TimeProvider.System);

            Assert.HasCount(status.ProjectCount, executor.Invocations, "Explicit autoRestore=true restores each evaluated project context for drift.");
        });
    }

    [TestMethod]
    public async Task False_MissingAssets_DoesNotRestore()
    {
        await WithWorkspace(assets: AssetsState.Missing, async (manager, status, root) =>
        {
            var executor = new ScriptedExecutor(ExecutionResult.Success);

            var outcome = await WorkspaceTools.RestoreAndReloadIfRequiredAsync(
                executor, ValidationOptions, manager, status, autoRestore: false, NullLogger.Instance, CancellationToken.None, timeProvider:System.TimeProvider.System);

            Assert.IsEmpty(executor.Invocations);
            Assert.IsTrue(outcome.Status.RestoreRequired);
            Assert.IsNull(outcome.FailureReason);
        });
    }

    [TestMethod]
    public async Task Omitted_RestoreFails_IsNonFatalAndReportsPathFreeReason()
    {
        await WithWorkspace(assets: AssetsState.Missing, async (manager, status, root) =>
        {
            var executor = new ScriptedExecutor(ExecutionResult.ExitCodeOne);

            var outcome = await WorkspaceTools.RestoreAndReloadIfRequiredAsync(
                executor, ValidationOptions, manager, status, autoRestore: null, NullLogger.Instance, CancellationToken.None, timeProvider:System.TimeProvider.System);

            Assert.IsTrue(outcome.Status.RestoreRequired, "A failed restore must leave restoreRequired=true.");
            Assert.IsNotNull(outcome.FailureReason);
            StringAssert.Contains(outcome.FailureReason, "auto-restore failed");
            StringAssert.Contains(outcome.FailureReason, "exit code 1");
            AssertPathFree(outcome.FailureReason, root);

            using var payload = JsonDocument.Parse(
                WorkspaceTools.SerializeWorkspaceLoadResult(outcome.Status, verbose: false, prewarmResult: null, outcome.FailureReason));
            Assert.AreEqual(outcome.FailureReason, payload.RootElement.GetProperty("restoreFailureReason").GetString());
            Assert.AreEqual("workspace_reload", payload.RootElement.GetProperty("nextCall").GetProperty("tool").GetString());
        });
    }

    [TestMethod]
    public async Task Omitted_UnreadableProjectFile_ReportsIndeterminateRestoreAndPreservesLoadedSession()
    {
        await WithWorkspace(assets: AssetsState.Missing, async (manager, status, root) =>
        {
            var executor = new ScriptedExecutor(ExecutionResult.Success);
            var beforeSolution = manager.GetCurrentSolution(status.WorkspaceId);
            var beforeVersion = manager.GetCurrentVersion(status.WorkspaceId);
            using var locks = new DisposableList(
                status.Projects.Select(p => new FileStream(p.FilePath, FileMode.Open, FileAccess.Read, FileShare.None)));

            var outcome = await WorkspaceTools.RestoreAndReloadIfRequiredAsync(
                executor, ValidationOptions, manager, status, autoRestore: null, NullLogger.Instance, CancellationToken.None, timeProvider:System.TimeProvider.System);

            Assert.IsEmpty(executor.Invocations, "Indeterminate graph evaluation must not issue a restore command.");
            Assert.IsNotNull(outcome.FailureReason);
            AssertPathFree(outcome.FailureReason, root);
            Assert.IsTrue(outcome.Status.RestoreRequired);
            Assert.AreSame(status, outcome.Status);
            Assert.AreSame(beforeSolution, manager.GetCurrentSolution(status.WorkspaceId));
            Assert.AreEqual(beforeVersion, manager.GetCurrentVersion(status.WorkspaceId));
        });
    }

    private sealed class DisposableList(IEnumerable<IDisposable> items) : IDisposable
    {
        private readonly List<IDisposable> _items = items.ToList();

        public void Dispose()
        {
            foreach (var item in _items)
            {
                item.Dispose();
            }
        }
    }

    [TestMethod]
    public async Task Omitted_RestoreTimesOut_IsNonFatalAndReasonOmitsCommandLine()
    {
        await WithWorkspace(assets: AssetsState.Missing, async (manager, status, root) =>
        {
            var executor = new ScriptedExecutor(ExecutionResult.TimesOut);

            var outcome = await WorkspaceTools.RestoreAndReloadIfRequiredAsync(
                executor, ValidationOptions, manager, status, autoRestore: null, NullLogger.Instance, CancellationToken.None, timeProvider:System.TimeProvider.System);

            Assert.IsTrue(outcome.Status.RestoreRequired);
            Assert.IsNotNull(outcome.FailureReason);
            StringAssert.Contains(outcome.FailureReason, "timed out");
            AssertPathFree(outcome.FailureReason, root);
        });
    }

    [TestMethod]
    public async Task True_RestoreFails_StillThrows()
    {
        await WithWorkspace(assets: AssetsState.Missing, async (manager, status, root) =>
        {
            var executor = new ScriptedExecutor(ExecutionResult.ExitCodeOne);

            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                WorkspaceTools.RestoreAndReloadIfRequiredAsync(
                    executor, ValidationOptions, manager, status, autoRestore: true, NullLogger.Instance, CancellationToken.None, timeProvider:System.TimeProvider.System));
        });
    }

    [TestMethod]
    public async Task Omitted_CallerCancelled_PropagatesInsteadOfBecomingAFailureReason()
    {
        await WithWorkspace(assets: AssetsState.Missing, async (manager, status, root) =>
        {
            var executor = new ScriptedExecutor(ExecutionResult.Success);
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                WorkspaceTools.RestoreAndReloadIfRequiredAsync(
                    executor, ValidationOptions, manager, status, autoRestore: null, NullLogger.Instance, cts.Token, timeProvider:System.TimeProvider.System));
        });
    }

    [TestMethod]
    public async Task Omitted_RestoreNotRequired_DoesNotRestoreEvenWithMissingAssets()
    {
        await WithWorkspace(assets: AssetsState.Missing, async (manager, status, root) =>
        {
            var executor = new ScriptedExecutor(ExecutionResult.Success);

            await WorkspaceTools.RestoreAndReloadIfRequiredAsync(
                executor, ValidationOptions, manager, status with { RestoreRequired = false }, autoRestore: null, NullLogger.Instance, CancellationToken.None, timeProvider:System.TimeProvider.System);

            Assert.IsEmpty(executor.Invocations);
        });
    }

    private static void AssertPathFree(string reason, string root)
    {
        Assert.IsFalse(reason.Contains(root, StringComparison.OrdinalIgnoreCase), $"Reason leaked the workspace path: {reason}");
        Assert.IsFalse(reason.Contains(".sln", StringComparison.OrdinalIgnoreCase), $"Reason leaked a project path: {reason}");
        Assert.IsFalse(reason.Contains(".csproj", StringComparison.OrdinalIgnoreCase), $"Reason leaked a project path: {reason}");
    }

    private enum AssetsState
    {
        Missing,
        PresentStale,
    }

    private static async Task WithWorkspace(
        AssetsState assets,
        Func<WorkspaceManager, WorkspaceStatusDto, string, Task> body)
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;
        try
        {
            using var manager = new WorkspaceManager(new RoslynMcp.Roslyn.Services.ForkInvocationPathPolicy(new RoslynMcp.Tests.Helpers.OwnedTestWorkspacePathPolicy()),
                NullLogger<WorkspaceManager>.Instance,
                new PreviewStore(),
                new FileWatcherService(NullLogger<FileWatcherService>.Instance),
                new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 4, RestoreRaceWaitMs = 0 });
            var status = await manager.LoadAsync(copiedSolutionPath, RoslynMcp.Tests.Helpers.OwnedNativeLoadContext.Properties(copiedSolutionPath, new Dictionary<string, string>()), CancellationToken.None);

            foreach (var objDir in Directory.EnumerateDirectories(copiedRoot, "obj", SearchOption.AllDirectories).ToArray())
            {
                TestFixtureFileSystem.DeleteDirectoryIfExists(objDir);
            }

            if (assets == AssetsState.PresentStale)
            {
                foreach (var project in status.Projects)
                {
                    var objDirectory = Path.Combine(Path.GetDirectoryName(project.FilePath)!, "obj");
                    Directory.CreateDirectory(objDirectory);
                    File.WriteAllText(Path.Combine(objDirectory, "project.assets.json"), "{\"project\":{\"frameworks\":{}}}");
                }
            }

            await body(manager, status with { RestoreRequired = true }, copiedRoot);
        }
        finally
        {
            DeleteDirectoryIfExists(copiedRoot);
        }
    }

    private enum ExecutionResult
    {
        Success,
        ExitCodeOne,
        TimesOut,
    }

    /// <summary>Scripted stand-in for the gated <c>dotnet restore</c> executor; records each invocation.</summary>
    private sealed class ScriptedExecutor(ExecutionResult result) : IGatedCommandExecutor
    {
        public Task<BuildCommandRun> ExecuteBuildAsync(BuildCommandPlan plan, TimeSpan timeout, CancellationToken ct) =>
            throw new NotSupportedException("This fixture does not execute builds.");

        public async Task<CommandExecutionDto> ExecuteAuthorizedAsync(
            string workspaceId, string targetPath, IReadOnlyList<string> arguments, TimeSpan timeout,
            Func<CancellationToken, Task> authorizeStart, CancellationToken ct)
        {
            await authorizeStart(ct).ConfigureAwait(false);
            return await ExecuteAsync(workspaceId, targetPath, arguments, timeout, ct).ConfigureAwait(false);
        }
        public Task<TestRunResultDto> ExecuteTestAsync(
            string workspaceId, string targetPath, IReadOnlyList<string> arguments,
            Func<CommandExecutionDto, CancellationToken, Task<TestRunResultDto>> collectResults,
            TimeSpan timeout, IReadOnlyList<EarlyKillPattern>? earlyKillPatterns, Func<CancellationToken, Task> authorizeStart, CancellationToken ct) =>
            throw new NotSupportedException("This fixture does not execute tests.");

        public Task<CommandExecutionDto> ExecuteAsync(
            string workspaceId, string targetPath, IReadOnlyList<string> arguments, TimeSpan timeout,
            IReadOnlyList<EarlyKillPattern>? earlyKillPatterns, CancellationToken ct)
        {
            if (earlyKillPatterns is { Count: > 0 })
                throw new NotSupportedException("This fixture does not simulate streaming early-kill patterns.");
            return ExecuteAsync(workspaceId, targetPath, arguments, timeout, ct);
        }

        private readonly List<IReadOnlyList<string>> _invocations = [];

        public IReadOnlyList<IReadOnlyList<string>> Invocations => _invocations;

        public Task<CommandExecutionDto> ExecuteAsync(
            string workspaceId,
            string targetPath,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _invocations.Add(arguments.ToArray());
            if (result == ExecutionResult.TimesOut)
            {
                // Mirrors GatedCommandExecutor: the real message embeds the command line (absolute paths).
                throw new TimeoutException($"The command 'dotnet {string.Join(" ", arguments)}' did not complete within the total timeout budget.");
            }

            var succeeded = result == ExecutionResult.Success;
            return Task.FromResult(new CommandExecutionDto(
                "dotnet",
                arguments,
                Path.GetDirectoryName(targetPath)!,
                targetPath,
                succeeded ? 0 : 1,
                succeeded,
                0,
                succeeded ? string.Empty : $"restore output for {targetPath}",
                string.Empty));
        }

        public ProjectStatusDto ResolveProject(string workspaceId, string projectName) =>
            throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}
