using System.Xml.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class AutomaticReloadAnalyzerLifetimeTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize] public static void Initialize(TestContext _) => InitializeServices();

    [TestMethod]
    public async Task AutomaticReload_DrainsReaderBeforeRetiringItsLateDependencyImages()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The held collectible analyzer shadow lifetime is the Windows contract.");
            return;
        }
        var source = await CreateNativeProjectFixtureAsync("Owner", CancellationToken.None);
        WorkspaceManager? manager = null;
        WorkspaceExecutionGate? gate = null;
        FileWatcherService? watcher = null;
        Task? reader = null;
        Task? staleReader = null;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? primary = null;
        try
        {
            var projectPath = source.GetPath("Owner", "Owner.csproj");
            var directory = source.GetPath("Owner", "generator");
            var images = await NativeAnalyzerReaderLifetime.CreateImagesAsync(directory, CancellationToken.None);
            var definition = XDocument.Load(projectPath);
            definition.Root!.Add(new XElement("ItemGroup", new XElement("Analyzer", new XAttribute("Include", images.GeneratorPath))));
            await File.WriteAllTextAsync(projectPath, definition.ToString());
            var policy = new ForkInvocationPathPolicy(await NativeSdkRestoreFixture.CreatePolicyAsync(projectPath, CancellationToken.None));
            watcher = new FileWatcherService(NullLogger<FileWatcherService>.Instance);
            manager = new WorkspaceManager(policy, NullLogger<WorkspaceManager>.Instance, new PreviewStore(), watcher,
                new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 2 });
            gate = new WorkspaceExecutionGate(new ExecutionGateOptions { OnStale = StalenessPolicy.AutoReload }, manager);
            var status = await manager.LoadAsync(projectPath,
                OwnedNativeLoadContext.Properties(projectPath, new Dictionary<string, string>()), CancellationToken.None);
            var snapshot = manager.GetCurrentSolution(status.WorkspaceId);
            Assert.AreEqual(NativeCompilerContextAvailability.Certified, NativeCompilerContextCertificate.Require(snapshot).Availability);
            watcher.ClearStale(status.WorkspaceId);
            reader = NativeAnalyzerReaderLifetime.RunReaderAsync(gate, manager, status.WorkspaceId, images, entered, release.Task, CancellationToken.None);
            await NativeAnalyzerReaderLifetime.AwaitEnteredOrFailureAsync(entered.Task, reader);
            manager.WorkspaceReloaded += id => { if (id == status.WorkspaceId) reloaded.TrySetResult(); };
            watcher.MarkStale(status.WorkspaceId, "Automatic reload lifetime control");
            Assert.IsTrue(manager.IsStale(status.WorkspaceId));
            staleReader = gate.RunReadAsync(status.WorkspaceId, _ => Task.FromResult(true), CancellationToken.None);
            using var observationDeadline = new CancellationTokenSource(new ExecutionGateOptions().RequestTimeout);
            while (!reloaded.Task.IsCompleted && !NativeAnalyzerReaderLifetime.HasWaitingWriter(gate, status.WorkspaceId))
            {
                if (staleReader.IsCompleted) await staleReader;
                await Task.Delay(1, observationDeadline.Token);
            }
            // Either positive witnesses the real old retirement or the repaired
            // writer queue; elapsed time and an absent event are never the oracle.
            var retiredBeforeReaderExit = reloaded.Task.IsCompleted;
            release.TrySetResult();
            await reader;
            await staleReader;
            Assert.IsFalse(retiredBeforeReaderExit, "Automatic reload must enter the existing writer queue before retiring a reader's native images.");
            Assert.IsTrue(reloaded.Task.IsCompleted, "The queued automatic reload must still complete after the reader exits.");
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            release.TrySetResult();
            await CleanupFailureCollector.RunAfterFailureAsync("Automatic reload analyzer fixture cleanup failed.", primary,
                () => reader is null ? ValueTask.CompletedTask : new ValueTask(reader),
                () => staleReader is null ? ValueTask.CompletedTask : new ValueTask(staleReader),
                CleanupFailureCollector.FromAction(() => OwnedResourceCleanup.Dispose("Automatic reload native resource cleanup failed.", null,
                    gate, manager, manager is null ? watcher : null)),
                () => source.DisposeAsync());
        }
    }

}
