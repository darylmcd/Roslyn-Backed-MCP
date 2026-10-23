using System.Text.Json;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ExplicitReloadAnalyzerLifetimeTests
{
    [TestMethod]
    public async Task RegisteredHostLoad_RestoreReloadDrainsItsExistingNativeReader()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("Collectible shadow-image retirement is the Windows contract."); return; }
        MsBuildInitializer.EnsureInitialized();
        NativeReloadRouteFixture? fixture = null;
        McpRootsTestServerFactory.Session? server = null;
        Task? reader = null;
        Task<string>? replacement = null;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Action<string>? observe = null;
        Exception? primary = null;
        try
        {
            fixture = await NativeReloadRouteFixture.CreateAsync(restoreDrift: true);
            var services = fixture.Services;
            var id = fixture.Status.WorkspaceId;
            Assert.IsTrue(services.WorkspaceManager.GetStatus(id).RestoreRequired);
            Assert.IsFalse(File.Exists(fixture.RestoreMarker));
            server = await McpRootsTestServerFactory.CreateWithSanctionedRootAsync(fixture.Source.Root, CancellationToken.None);
            services.FileWatcher.ClearStale(id);
            reader = NativeAnalyzerReaderLifetime.RunReaderAsync(services.WorkspaceExecutionGate, services.WorkspaceManager,
                id, fixture.Images, entered, release.Task, CancellationToken.None);
            await NativeAnalyzerReaderLifetime.AwaitEnteredOrFailureAsync(entered.Task, reader);
            observe = actual => { if (actual == id) reloaded.TrySetResult(); };
            services.WorkspaceManager.WorkspaceReloaded += observe;
            replacement = WorkspaceTools.LoadWorkspace(server.Server, services.WorkspaceExecutionGate, services.WorkspaceManager,
                services.WorkspaceWarmService, services.GatedCommandExecutor, new ValidationServiceOptions(), fixture.LoadedPath,
                autoRestore: true, prewarm: false, ct: CancellationToken.None);
            await AssertReplacementDrainsAsync(services, id, reader, replacement, reloaded.Task, release);
            using var result = JsonDocument.Parse(await replacement);
            Assert.AreEqual(id, result.RootElement.GetProperty("workspaceId").GetString(), "The public load must deduplicate the actual registered session.");
            Assert.IsTrue(File.Exists(fixture.RestoreMarker), "The actual native Restore target must execute before this route's reload completes.");
            Assert.HasCount(1, services.WorkspaceManager.ListWorkspaces());
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            release.TrySetResult();
            await CleanupFailureCollector.RunAfterFailureAsync("Registered host reload lifetime cleanup failed.", primary,
                () => reader is null ? ValueTask.CompletedTask : new ValueTask(reader),
                () => replacement is null ? ValueTask.CompletedTask : new ValueTask(replacement),
                CleanupFailureCollector.FromAction(() => { if (fixture is not null && observe is not null) fixture.Services.WorkspaceManager.WorkspaceReloaded -= observe; }),
                () => server is null ? ValueTask.CompletedTask : server.DisposeAsync(),
                CleanupFailureCollector.FromAction(() => fixture?.Dispose()));
        }
    }

    [TestMethod]
    public async Task IssuedForkDestination_ReloadDrainsItsExistingNativeReader()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("Collectible shadow-image retirement is the Windows contract."); return; }
        MsBuildInitializer.EnsureInitialized();
        NativeReloadRouteFixture? fixture = null;
        PhysicalDirectoryScope.OwnedRoot? destinationOwner = null;
        string? privateRoot = null;
        IDisposable? policyScope = null;
        IDisposable? copyScope = null;
        Task? reader = null;
        Task? replacement = null;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Action<string>? observe = null;
        Exception? primary = null;
        try
        {
            fixture = await NativeReloadRouteFixture.CreateAsync(restoreDrift: false);
            var services = fixture.Services;
            var manager = services.WorkspaceManager;
            var source = manager.GetCurrentSolution(fixture.Status.WorkspaceId);
            var loader = (IWorkspaceEvaluationLoader)manager;
            var layout = await PhysicalGraphLayout.CreateAsync(fixture.Status.WorkspaceId, fixture.LoadedPath, source,
                services.PathPolicy, CancellationToken.None);
            privateRoot = Path.Combine(TestTempRoot.Current, "fork-reader-" + Guid.NewGuid().ToString("N"));
            destinationOwner = PhysicalDirectoryScope.CreateOwnedRoot(privateRoot);
            var destination = NativePackageForkCopy.Copy(fixture.Source.Root, destinationOwner, layout, fixture.LoadedPath);
            var directory = Path.GetDirectoryName(destination)!;
            var imageDirectory = Path.Combine(directory, "generator");
            Directory.CreateDirectory(imageDirectory);
            foreach (var path in Directory.EnumerateFiles(Path.GetDirectoryName(fixture.Images.GeneratorPath)!, "*.dll"))
                File.Copy(path, Path.Combine(imageDirectory, Path.GetFileName(path)));
            var properties = OwnedNativeLoadContext.Properties(destination, new Dictionary<string, string>());
            var restored = await new DotnetCommandRunner().RunAsync(directory, destination,
                MsBuildCommandProperties.Append(["restore", destination, "--nologo", "--source", Path.Combine(fixture.Source.Root, "feed"),
                    "--packages", fixture.Source.Cache], properties), CancellationToken.None);
            Assert.IsTrue(restored.Succeeded, "The actual private native project must restore before issuing its invocation.");
            var packages = loader.CapturePackageReadAuthority(fixture.Status.WorkspaceId, source);
            var invocation = loader.CaptureForkInvocationAuthority(fixture.Status.WorkspaceId, source, destination, properties, destinationOwner, layout);
            policyScope = services.PathPolicy.Enter(invocation);
            copyScope = invocation.EnterOwnedCopy(destinationOwner, layout);
            var status = await loader.LoadWithPropertiesAsync(destination, properties, packages, invocation, CancellationToken.None);
            Assert.AreNotEqual(fixture.Status.WorkspaceId, status.WorkspaceId);
            Assert.AreEqual(destination, manager.GetStatus(status.WorkspaceId).LoadedPath);
            services.FileWatcher.ClearStale(status.WorkspaceId);
            var images = fixture.Images with { GeneratorPath = Path.Combine(imageDirectory, Path.GetFileName(fixture.Images.GeneratorPath)) };
            reader = NativeAnalyzerReaderLifetime.RunReaderAsync(services.WorkspaceExecutionGate, manager, status.WorkspaceId,
                images, entered, release.Task, CancellationToken.None);
            await NativeAnalyzerReaderLifetime.AwaitEnteredOrFailureAsync(entered.Task, reader);
            observe = actual => { if (actual == status.WorkspaceId) reloaded.TrySetResult(); };
            manager.WorkspaceReloaded += observe;
            replacement = services.WorkspaceForkApplyService.ReloadForkAsync(status.WorkspaceId, invocation, CancellationToken.None);
            await AssertReplacementDrainsAsync(services, status.WorkspaceId, reader, replacement, reloaded.Task, release);
            Assert.HasCount(2, manager.ListWorkspaces());
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            release.TrySetResult();
            await CleanupFailureCollector.RunAfterFailureAsync("Issued fork reload lifetime cleanup failed.", primary,
                () => reader is null ? ValueTask.CompletedTask : new ValueTask(reader),
                () => replacement is null ? ValueTask.CompletedTask : new ValueTask(replacement),
                CleanupFailureCollector.FromAction(() => { if (fixture is not null && observe is not null) fixture.Services.WorkspaceManager.WorkspaceReloaded -= observe; }),
                CleanupFailureCollector.FromAction(() => fixture?.Services.Dispose()),
                CleanupFailureCollector.FromAction(() => OwnedResourceCleanup.Dispose("Issued reader invocation cleanup failed.", null, copyScope, policyScope, destinationOwner)),
                CleanupFailureCollector.FromAction(() => { if (privateRoot is not null) TestFixtureFileSystem.DeleteDirectoryIfExists(privateRoot); }),
                CleanupFailureCollector.FromAction(() => fixture?.Source.Dispose()));
        }
    }

    private static async Task AssertReplacementDrainsAsync(TestServiceContainer services, string id, Task reader, Task replacement,
        Task reloaded, TaskCompletionSource release)
    {
        var deadline = new CancellationTokenSource(new ExecutionGateOptions().RequestTimeout);
        Exception? primary = null;
        try
        {
            while (!reloaded.IsCompleted && !NativeAnalyzerReaderLifetime.HasWaitingWriter(services.WorkspaceExecutionGate, id))
            {
                if (replacement.IsCompleted)
                {
                    await replacement;
                    Assert.Fail("The actual replacement must reload or wait in this session's existing writer queue.");
                }
                await Task.Delay(1, deadline.Token);
            }
            var retiredBeforeReaderExit = reloaded.IsCompleted;
            Console.WriteLine("Actual explicit native reload before reader exit: " + retiredBeforeReaderExit);
            release.TrySetResult();
            await reader;
            await replacement;
            Assert.IsFalse(retiredBeforeReaderExit, "Session replacement must drain the admitted native reader before retiring its images.");
            Assert.IsTrue(reloaded.IsCompleted);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Explicit native reload witness cleanup failed.", primary,
                CleanupFailureCollector.FromAction(deadline.Dispose));
        }
    }

    private sealed class NativeReloadRouteFixture(NuGetInstallationFixture source, TestServiceContainer services,
        NativeAnalyzerReaderLifetime.Images images, string loadedPath, WorkspaceStatusDto status) : IDisposable
    {
        internal NuGetInstallationFixture Source { get; } = source;
        internal TestServiceContainer Services { get; } = services;
        internal NativeAnalyzerReaderLifetime.Images Images { get; } = images;
        internal string LoadedPath { get; } = loadedPath;
        internal WorkspaceStatusDto Status { get; } = status;
        internal string RestoreMarker => Path.Combine(Source.Root, "host-restore-observed.txt");

        internal static async Task<NativeReloadRouteFixture> CreateAsync(bool restoreDrift)
        {
            MsBuildInitializer.EnsureInitialized();
            NuGetInstallationFixture? source = null;
            TestServiceContainer? services = null;
            try
            {
                source = await NuGetInstallationFixture.CreateAsync("Owned.Reader", "1.0.0",
                    new Dictionary<string, byte[]> { ["contentFiles/cs/any/Input.cs"] = "class ReaderInput { }"u8.ToArray() });
                var owner = Path.Combine(source.Root, "Owner.csproj");
                var images = await NativeAnalyzerReaderLifetime.CreateImagesAsync(Path.Combine(source.Root, "generator"), CancellationToken.None);
                var definition = XDocument.Load(owner);
                definition.Root!.Add(new XElement("ItemGroup", new XElement("Analyzer", new XAttribute("Include",
                    "generator/" + Path.GetFileName(images.GeneratorPath)))));
                var loadedPath = owner;
                if (restoreDrift)
                {
                    var package = definition.Descendants("PackageReference").Single();
                    Assert.AreEqual("1.0.0", package.Attribute("Version")!.Value);
                    package.SetAttributeValue("Version", "[1.0.0,2.0.0)");
                    definition.Root.Add(new XElement("Target", new XAttribute("Name", "ObserveHostRestore"), new XAttribute("AfterTargets", "Restore"),
                        new XElement("WriteLinesToFile", new XAttribute("File", Path.Combine(source.Root, "host-restore-observed.txt")),
                            new XAttribute("Lines", "actual native restore completed"), new XAttribute("Overwrite", "true"))));
                }
                await File.WriteAllTextAsync(owner, definition.ToString());
                Assert.IsTrue(File.Exists(source.Assets), "The actual prior restore must retain its readable selecting assets before native load.");
                var nativeConfigurationPolicy = await NativeSdkRestoreFixture.CreatePolicyAsync(owner, CancellationToken.None);
                services = TestServiceContainer.Create(new ValidationServiceOptions(), new ForkInvocationPathPolicy(nativeConfigurationPolicy));
                var status = await services.WorkspaceManager.LoadAsync(loadedPath,
                    OwnedNativeLoadContext.Properties(loadedPath, new Dictionary<string, string>()), CancellationToken.None);
                var certificate = NativeCompilerContextCertificate.Require(services.WorkspaceManager.GetCurrentSolution(status.WorkspaceId));
                Assert.AreEqual(NativeCompilerContextAvailability.Certified, certificate.Availability);
                Assert.IsNotEmpty(certificate.InstalledPackages.AssetInputs);
                Assert.IsTrue(certificate.InstalledPackages.AssetInputs.All(input => input.Exists),
                    "The native reader must be admitted from complete actual assets, independently of package-range drift.");
                if (restoreDrift) Assert.IsTrue(status.RestoreRequired,
                    "The requested bounded package range must differ from the prior actual restore range and select the public restore/reload route.");
                return new(source, services, images, loadedPath, status);
            }
            catch (Exception failure)
            {
                OwnedResourceCleanup.Dispose("Native explicit reload fixture setup failed.", failure, services, source);
                throw;
            }
        }

        public void Dispose() => OwnedResourceCleanup.Dispose("Native explicit reload fixture cleanup failed.", null, Services, Source);
    }
}
