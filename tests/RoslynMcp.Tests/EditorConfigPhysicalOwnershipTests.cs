using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class EditorConfigPhysicalOwnershipTests
{
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task ActualCoordinatedWrite_RefusesForeignAliasBeforeChangingItsBytes(bool replaceParent, bool seedPriorOwnedWrite)
    {
        NuGetInstallationFixture? fixture = null;
        TestServiceContainer? services = null;
        var external = Path.Combine(TestTempRoot.Current, nameof(EditorConfigPhysicalOwnershipTests), Guid.NewGuid().ToString("N"));
        string? originalEntry = null;
        string? retainedEntry = null;
        var moved = false;
        var linked = false;
        Exception? replacementFailure = null;
        Exception? primary = null;
        try
        {
            fixture = await NuGetInstallationFixture.CreateAsync("Owned.EditorConfigMutation", "1.0.0",
                new Dictionary<string, byte[]> { ["marker.txt"] = "owned dependency"u8.ToArray() });
            var directory = Path.Combine(fixture.Root, "source");
            Directory.CreateDirectory(directory);
            Directory.CreateDirectory(external);
            var source = Path.Combine(directory, "Entry.cs");
            var config = Path.Combine(directory, ".editorconfig");
            var foreignConfig = Path.Combine(external, ".editorconfig");
            var before = "[*.cs]\nindent_size = 4\n"u8.ToArray();
            await File.WriteAllTextAsync(source, "public class Entry { }");
            await File.WriteAllTextAsync(Path.Combine(external, "Entry.cs"), "public class Entry { }");
            await File.WriteAllBytesAsync(config, before);
            await File.WriteAllBytesAsync(foreignConfig, before);
            if (!replaceParent && OperatingSystem.IsWindows())
            {
                var probe = Path.Combine(external, "file-link-capability");
                if (!TestFixtureFileSystem.TryCreateFileLink(probe, foreignConfig))
                {
                    Assert.Inconclusive("The current Windows token cannot create the required file symbolic link.");
                    return;
                }
                File.Delete(probe);
            }
            services = TestServiceContainer.Create(new ValidationServiceOptions(),
                new ForkInvocationPathPolicy(new OwnedTestWorkspacePathPolicy()));
            var manager = services.WorkspaceManager;
            WorkspaceStatusDto status = await manager.LoadAsync(Path.Combine(fixture.Root, "Owner.csproj"),
                fixture.LoadProperties, CancellationToken.None);
            if (seedPriorOwnedWrite)
            {
                await services.EditorConfigService.SetOptionAsync(status.WorkspaceId, source, "indent_size", "6",
                    "set_editorconfig_option", CancellationToken.None);
                before = await File.ReadAllBytesAsync(config);
                await File.WriteAllBytesAsync(foreignConfig, before);
            }
            var snapshot = manager.GetCurrentSolution(status.WorkspaceId);
            _ = NativeCompilerContextCertificate.Require(snapshot);
            var version = manager.GetCurrentVersion(status.WorkspaceId);
            var undo = services.UndoService.GetLastOperation(status.WorkspaceId);
            if (seedPriorOwnedWrite)
            {
                Assert.IsNotNull(undo);
                var repeated = services.UndoService.GetLastOperation(status.WorkspaceId);
                Assert.IsNotNull(repeated);
                Assert.AreNotSame(undo, repeated, "GetLastOperation returns a new public DTO for its stored operation.");
                Assert.AreEqual(undo.WorkspaceId, repeated.WorkspaceId);
                Assert.AreEqual(undo.Description, repeated.Description);
                Assert.AreEqual(undo.AppliedAtUtc, repeated.AppliedAtUtc);
            }
            else Assert.IsNull(undo);
            originalEntry = replaceParent ? directory : config;
            retainedEntry = replaceParent ? Path.Combine(fixture.Root, "retained-source") : Path.Combine(directory, "retained-config");
            var coordinator = new BeforeActualWriteCoordinator(manager.EditorConfigWriteCoordinator
                ?? throw new AssertFailedException("The real manager must expose its actual watcher coordinator."), () =>
            {
                try
                {
                if (replaceParent) Directory.Move(originalEntry, retainedEntry);
                else File.Move(originalEntry, retainedEntry);
                moved = true;
                if (replaceParent && OperatingSystem.IsWindows()) WindowsDirectoryJunction.Create(originalEntry, external);
                else if (replaceParent) Directory.CreateSymbolicLink(originalEntry, external);
                else File.CreateSymbolicLink(originalEntry, foreignConfig);
                linked = true;
                }
                catch (Exception failure) { replacementFailure = failure; throw; }
            });
            var service = new EditorConfigService(manager, services.UndoService, services.ChangeTracker,
                NullLogger<EditorConfigService>.Instance, coordinator);
            Exception? refused = null;
            try { await service.SetOptionAsync(status.WorkspaceId, source, "indent_size", "8", "set_editorconfig_option", CancellationToken.None); }
            catch (Exception failure) { refused = failure; }
            Assert.AreEqual(1, coordinator.Attempts, "The actor must run at the real post-admission coordinator boundary.");
            Assert.IsTrue(moved && linked, $"The actual admitted entry must be replaced with a real foreign alias. moved={moved}; linked={linked}; replacement={replacementFailure}");
            CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(foreignConfig),
                "A late refusal cannot authorize changing foreign bytes through the replacement alias.");
            Assert.IsNotNull(refused, "The replaced physical coordinate must refuse mutation.");
            Assert.AreSame(snapshot, manager.GetCurrentSolution(status.WorkspaceId));
            Assert.AreEqual(version, manager.GetCurrentVersion(status.WorkspaceId));
            var afterUndo = services.UndoService.GetLastOperation(status.WorkspaceId);
            if (seedPriorOwnedWrite)
            {
                Assert.IsNotNull(afterUndo);
                Assert.AreEqual(undo!.WorkspaceId, afterUndo.WorkspaceId);
                Assert.AreEqual(undo.Description, afterUndo.Description);
                Assert.AreEqual(undo.AppliedAtUtc, afterUndo.AppliedAtUtc);
            }
            else Assert.AreSame(undo, afterUndo);
            Assert.IsNull(((IWorkspaceRecoveryAuthority)manager).GetPendingRecovery(status.WorkspaceId),
                "A refusal before any source transition must not create recovery authority.");
            CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(replaceParent
                ? Path.Combine(retainedEntry, ".editorconfig") : retainedEntry));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Editorconfig physical ownership fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => services?.Dispose()),
                CleanupFailureCollector.FromAction(() =>
                {
                    if (linked)
                    {
                        if (replaceParent) Directory.Delete(originalEntry!);
                        else File.Delete(originalEntry!);
                    }
                    if (moved)
                    {
                        if (replaceParent) Directory.Move(retainedEntry!, originalEntry!);
                        else File.Move(retainedEntry!, originalEntry!);
                    }
                }),
                CleanupFailureCollector.FromAction(() => fixture?.Dispose()),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(external)));
        }
    }

    private sealed class BeforeActualWriteCoordinator(IEditorConfigWriteCoordinator inner, Action replace) : IEditorConfigWriteCoordinator
    {
        internal int Attempts { get; private set; }
        public Task RunOwnedWriteAsync(EditorConfigWriteOperation operation, CancellationToken ct)
        {
            Attempts++;
            replace();
            return inner.RunOwnedWriteAsync(operation, ct);
        }
    }
}
