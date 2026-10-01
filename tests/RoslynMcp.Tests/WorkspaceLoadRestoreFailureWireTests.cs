using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression for <c>workspace-restore-public-failure</c>: an explicit <c>autoRestore:true</c>
/// failure must reach the caller as a path-free, actionable public reason instead of the generic
/// <c>InvalidOperationException</c> redaction, while the detailed path/output tails stay on the
/// inner exception for logs. Fake runner, real <see cref="GatedCommandExecutor"/>, real tool
/// error envelope (<see cref="ToolErrorHandler.ClassifyAndFormat"/>).
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class WorkspaceLoadRestoreFailureWireTests : SharedWorkspaceTestBase
{
    private const string SecretOutputMarker = "SECRET-RESTORE-OUTPUT-TAIL";

    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task RestoreFailure_ThroughToolErrorEnvelope_ReturnsPathFreePublicReason()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;
        try
        {
            using var manager = CreateIsolatedManager();
            var status = await manager.LoadAsync(copiedSolutionPath, CancellationToken.None);
            var restoreRequired = status with { RestoreRequired = true };
            using var executor = new GatedCommandExecutor(
                manager, new FailingRestoreRunner(), NullLogger<GatedCommandExecutor>.Instance);

            var thrown = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                WorkspaceTools.RestoreAndReloadIfRequiredAsync(
                    executor, ValidationOptions, manager, restoreRequired, autoRestore: true, NullLogger.Instance, CancellationToken.None));

            // Detail stays on the inner exception for logs.
            Assert.IsNotNull(thrown.InnerException);
            StringAssert.Contains(thrown.InnerException.Message, copiedSolutionPath);
            StringAssert.Contains(thrown.InnerException.Message, SecretOutputMarker);

            var json = ToolErrorHandler.ClassifyAndFormat(thrown, "workspace_load");
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            Assert.AreEqual("InvalidOperation", root.GetProperty("category").GetString());
            Assert.AreEqual(nameof(PublicInvalidOperationException), root.GetProperty("exceptionType").GetString());

            var message = root.GetProperty("message").GetString()!;
            StringAssert.Contains(message, "auto-restore failed");
            StringAssert.Contains(message, "exit code 1");
            StringAssert.Contains(message, "workspace_reload");
            Assert.IsFalse(message.Contains(copiedRoot, StringComparison.OrdinalIgnoreCase), $"Message leaked the project path: {message}");
            Assert.IsFalse(message.Contains(copiedSolutionPath, StringComparison.OrdinalIgnoreCase), $"Message leaked the project path: {message}");
            Assert.IsFalse(message.Contains(SecretOutputMarker, StringComparison.Ordinal), $"Message leaked restore output: {message}");
            Assert.IsFalse(json.Contains(copiedRoot.Replace("\\", "\\\\", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase)
                || json.Contains(copiedRoot, StringComparison.OrdinalIgnoreCase), $"Envelope leaked the project path: {json}");
            Assert.IsFalse(json.Contains(SecretOutputMarker, StringComparison.Ordinal), $"Envelope leaked restore output: {json}");
        }
        finally
        {
            DeleteDirectoryIfExists(copiedRoot);
        }
    }

    [TestMethod]
    public async Task RestoreBudgetExpiry_ThroughToolErrorEnvelope_ReturnsPathFreeTimeoutMessage()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;
        try
        {
            using var manager = CreateIsolatedManager();
            var status = await manager.LoadAsync(copiedSolutionPath, CancellationToken.None);
            var restoreRequired = status with { RestoreRequired = true };
            var runner = new HoldingRunner();
            using var executor = new GatedCommandExecutor(
                manager, runner, NullLogger<GatedCommandExecutor>.Instance);

            var holder = executor.ExecuteAsync(
                status.WorkspaceId, copiedSolutionPath, ["build"], TimeSpan.FromMinutes(1), CancellationToken.None);
            await runner.BuildStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

            try
            {
                var shortBudget = new ValidationServiceOptions { RestoreTimeout = TimeSpan.FromMilliseconds(200) };
                var thrown = await Assert.ThrowsExactlyAsync<TimeoutException>(() =>
                    WorkspaceTools.RestoreAndReloadIfRequiredAsync(
                        executor, shortBudget, manager, restoreRequired, autoRestore: true, NullLogger.Instance, CancellationToken.None));

                var json = ToolErrorHandler.ClassifyAndFormat(thrown, "workspace_load");
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                Assert.AreEqual("Timeout", root.GetProperty("category").GetString());
                var message = root.GetProperty("message").GetString()!;
                StringAssert.Contains(message, "timed out");
                Assert.IsFalse(json.Contains(copiedRoot, StringComparison.OrdinalIgnoreCase), $"Envelope leaked the project path: {json}");
                Assert.IsFalse(json.Contains(copiedRoot.Replace("\\", "\\\\", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase), $"Envelope leaked the project path: {json}");
            }
            finally
            {
                runner.ReleaseBuild.SetResult();
                await holder;
            }
        }
        finally
        {
            DeleteDirectoryIfExists(copiedRoot);
        }
    }

    private static WorkspaceManager CreateIsolatedManager() =>
        new(
            NullLogger<WorkspaceManager>.Instance,
            new PreviewStore(),
            new FileWatcherService(NullLogger<FileWatcherService>.Instance),
            new WorkspaceManagerOptions
            {
                MaxConcurrentWorkspaces = 4,
                RestoreRaceWaitMs = 0,
            });

    /// <summary>Every command fails (exit code 1) with output that embeds the target path and a marker.</summary>
    private sealed class FailingRestoreRunner : IDotnetCommandRunner
    {
        public Task<CommandExecutionDto> RunAsync(
            string workingDirectory,
            string targetPath,
            IReadOnlyList<string> arguments,
            CancellationToken ct) =>
            Task.FromResult(new CommandExecutionDto(
                "dotnet",
                arguments,
                workingDirectory,
                targetPath,
                1,
                false,
                0,
                $"{SecretOutputMarker} while restoring {targetPath}",
                $"error NU1301 at {targetPath}"));
    }

    /// <summary>A "build" command blocks until released, holding the workspace command gate.</summary>
    private sealed class HoldingRunner : IDotnetCommandRunner
    {
        public TaskCompletionSource BuildStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseBuild { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<CommandExecutionDto> RunAsync(
            string workingDirectory,
            string targetPath,
            IReadOnlyList<string> arguments,
            CancellationToken ct)
        {
            if (arguments[0] == "build")
            {
                BuildStarted.TrySetResult();
                await ReleaseBuild.Task.WaitAsync(ct);
            }

            return new CommandExecutionDto(
                "dotnet", arguments, workingDirectory, targetPath, 0, true, 0, string.Empty, string.Empty);
        }
    }
}
