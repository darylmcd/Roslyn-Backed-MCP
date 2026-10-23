using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeUnusedMutationAuthorityTests
{
    [TestMethod]
    [DataRow("unverified")]
    [DataRow("canceledAttempt")]
    [DataRow("disposed")]
    [DataRow("completed")]
    [DataRow("foreign")]
    public async Task CancelUnusedAuthority_RequiresLiveVerifiedReceiptAndNoTransitionEntry(string phase)
    {
        NuGetInstallationFixture? fixture = null;
        TestServiceContainer? services = null;
        IWorkspaceMutationLease? lease = null;
        IPhysicalRestorationAuthority? staged = null;
        NuGetInstallationFixture? foreignFixture = null;
        TestServiceContainer? foreignServices = null;
        IWorkspaceMutationLease? foreignLease = null;
        IPhysicalRestorationAuthority? foreignStage = null;
        Exception? primary = null;
        try
        {
            fixture = await NuGetInstallationFixture.CreateAsync("Owned.UnusedMutation", "1.0.0",
                new Dictionary<string, byte[]> { ["marker.txt"] = "owned package"u8.ToArray() });
            var before = "[*.cs]\nindent_size = 4\n"u8.ToArray();
            var desired = "[*.cs]\nindent_size = 8\n"u8.ToArray();
            var config = await WriteSourceAsync(fixture.Root, before);
            services = CreateServices();
            var manager = services.WorkspaceManager;
            var status = await manager.LoadAsync(Path.Combine(fixture.Root, "Owner.csproj"), fixture.LoadProperties, CancellationToken.None);
            var snapshot = manager.GetCurrentSolution(status.WorkspaceId);
            Assert.AreEqual(NativeCompilerContextAvailability.Certified, NativeCompilerContextCertificate.Require(snapshot).Availability);
            var version = manager.GetCurrentVersion(status.WorkspaceId);
            var authority = (IWorkspaceRecoveryAuthority)manager;
            var operation = await authority.CaptureRecoveryAsync(status.WorkspaceId, "Unused native mutation", [FileSnapshotDto.FromExistingBytes(config, before)], CancellationToken.None);
            lease = await authority.BeginMutationAsync(operation, CancellationToken.None);
            var intended = FileSnapshotDto.FromExistingBytes(config, desired);
            lease.BeforeWrite(intended);
            staged = await authority.StageMutationAsync(operation, [intended], CancellationToken.None);
            if (phase == "unverified")
            {
                Assert.ThrowsExactly<WorkspaceMutationOwnershipException>(() => lease.CancelBeforeSourceTransition(staged));
                await authority.VerifyMutationNamespacesAsync(operation, staged, MutationNamespaceTarget.CapturedSource, CancellationToken.None);
                lease.CancelBeforeSourceTransition(staged);
            }
            else
            {
                await authority.VerifyMutationNamespacesAsync(operation, staged, MutationNamespaceTarget.CapturedSource, CancellationToken.None);
                if (phase == "canceledAttempt")
                {
                    using var canceled = new CancellationTokenSource();
                    canceled.Cancel();
                    var observed = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => operation.TransitionSourceAsync(config,
                        before, desired, PhysicalSourceTransitionMode.PreserveExistingObject, authority, staged, canceled.Token));
                    Assert.AreEqual(canceled.Token, observed.CancellationToken);
                    Assert.IsTrue(operation.HasAttemptedSourceTransition, "Even cancellation before all physical effects is a real attempted transition.");
                    CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(config));
                    Assert.ThrowsExactly<WorkspaceMutationOwnershipException>(() => lease.CancelBeforeSourceTransition(staged));
                    await lease.AbortAsync(CancellationToken.None);
                }
                else if (phase == "disposed")
                {
                    var released = staged;
                    staged = null;
                    released.Dispose();
                    Assert.ThrowsExactly<WorkspaceMutationOwnershipException>(() => lease.CancelBeforeSourceTransition(released));
                    await lease.AbortAsync(CancellationToken.None);
                }
                else if (phase == "foreign")
                {
                    foreignFixture = await NuGetInstallationFixture.CreateAsync("Owned.ForeignUnusedMutation", "1.0.0",
                        new Dictionary<string, byte[]> { ["marker.txt"] = "foreign owned package"u8.ToArray() });
                    var foreignConfig = await WriteSourceAsync(foreignFixture.Root, before);
                    foreignServices = CreateServices();
                    var foreignStatus = await foreignServices.WorkspaceManager.LoadAsync(Path.Combine(foreignFixture.Root, "Owner.csproj"),
                        foreignFixture.LoadProperties, CancellationToken.None);
                    var foreignAuthority = (IWorkspaceRecoveryAuthority)foreignServices.WorkspaceManager;
                    var foreignOperation = await foreignAuthority.CaptureRecoveryAsync(foreignStatus.WorkspaceId, "Other unused mutation",
                        [FileSnapshotDto.FromExistingBytes(foreignConfig, before)], CancellationToken.None);
                    foreignLease = await foreignAuthority.BeginMutationAsync(foreignOperation, CancellationToken.None);
                    var foreignIntent = FileSnapshotDto.FromExistingBytes(foreignConfig, desired);
                    foreignLease.BeforeWrite(foreignIntent);
                    foreignStage = await foreignAuthority.StageMutationAsync(foreignOperation, [foreignIntent], CancellationToken.None);
                    await foreignAuthority.VerifyMutationNamespacesAsync(foreignOperation, foreignStage,
                        MutationNamespaceTarget.CapturedSource, CancellationToken.None);
                    Assert.AreNotEqual(status.WorkspaceId, foreignStatus.WorkspaceId);
                    Assert.ThrowsExactly<WorkspaceMutationOwnershipException>(() => lease.CancelBeforeSourceTransition(foreignStage));
                    CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(foreignConfig));
                    foreignLease.CancelBeforeSourceTransition(foreignStage);
                    Assert.IsNull(foreignAuthority.GetPendingRecovery(foreignStatus.WorkspaceId));
                    lease.CancelBeforeSourceTransition(staged);
                }
                else
                {
                    lease.CancelBeforeSourceTransition(staged);
                    Assert.ThrowsExactly<WorkspaceMutationOwnershipException>(() => lease.CancelBeforeSourceTransition(staged));
                }
            }
            CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(config));
            Assert.AreSame(snapshot, manager.GetCurrentSolution(status.WorkspaceId));
            Assert.AreEqual(version, manager.GetCurrentVersion(status.WorkspaceId));
            Assert.IsNull(authority.GetPendingRecovery(status.WorkspaceId));
            Assert.IsNull(services.UndoService.GetLastOperation(status.WorkspaceId));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Unused mutation authority fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => foreignStage?.Dispose()),
                CleanupFailureCollector.FromAction(() => foreignLease?.Dispose()),
                CleanupFailureCollector.FromAction(() => foreignServices?.Dispose()),
                CleanupFailureCollector.FromAction(() => foreignFixture?.Dispose()),
                CleanupFailureCollector.FromAction(() => staged?.Dispose()),
                CleanupFailureCollector.FromAction(() => lease?.Dispose()),
                CleanupFailureCollector.FromAction(() => services?.Dispose()),
                CleanupFailureCollector.FromAction(() => fixture?.Dispose()));
        }
    }

    private static TestServiceContainer CreateServices() => TestServiceContainer.Create(new ValidationServiceOptions(),
        new ForkInvocationPathPolicy(new OwnedTestWorkspacePathPolicy()));

    private static async Task<string> WriteSourceAsync(string root, byte[] before)
    {
        var directory = Path.Combine(root, "source");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "Entry.cs"), "public class Entry { }");
        var config = Path.Combine(directory, ".editorconfig");
        await File.WriteAllBytesAsync(config, before);
        return config;
    }
}
