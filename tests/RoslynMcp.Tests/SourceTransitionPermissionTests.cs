using System.Security.AccessControl;
using System.Security.Principal;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

// Namespace-transition authority requires more than data-write permission.
// Qualify the next-major refusal without changing source bytes or fixture permissions.
[TestClass]
public sealed class SourceTransitionPermissionTests
{
    [TestMethod]
    public async Task Windows_WriteAllowedDeleteDenied_RefusesWithoutChangingSource()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("Windows ACL permission contract."); return; }
        var root = Path.Combine(TestTempRoot.Current, nameof(SourceTransitionPermissionTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "source.bin");
        await File.WriteAllBytesAsync(path, [1]);
        var directory = new DirectoryInfo(root);
        var file = new FileInfo(path);
        var originalDirectoryAcl = directory.GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access);
        var originalFileAcl = file.GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access);
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User ?? throw new AssertFailedException("The test requires the current Windows user SID.");
        Exception? primary = null;
        try
        {
            var directoryAcl = directory.GetAccessControl();
            directoryAcl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.DeleteSubdirectoriesAndFiles,
                InheritanceFlags.None, PropagationFlags.None, AccessControlType.Deny));
            directory.SetAccessControl(directoryAcl);
            var fileAcl = file.GetAccessControl();
            fileAcl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.Delete, AccessControlType.Deny));
            file.SetAccessControl(fileAcl);
            await File.WriteAllBytesAsync(path, [2]);
            CollectionAssert.AreEqual(new byte[] { 2 }, await File.ReadAllBytesAsync(path), "The original in-place persistence operation must succeed under this exact ACL.");
            var retained = new List<PhysicalSourceTransition>();
            var namespaces = new List<string>();
            await Assert.ThrowsExactlyAsync<IOException>(() => PhysicalSourceTransition.ApplyAsync(path, [2], [3],
                PhysicalSourceTransitionMode.PreserveExistingObject, AcquireFixtureCustodyAsync, Authorize, retained.Add, namespaces.Add,
                PhysicalSourceTransition.OpenParent, null, CancellationToken.None));
            CollectionAssert.AreEqual(new byte[] { 2 }, await File.ReadAllBytesAsync(path));
            Assert.HasCount(0, retained);
            CollectionAssert.AreEqual(new[] { path }, Directory.GetFileSystemEntries(root));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Permission contract fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() =>
                {
                    if (!OperatingSystem.IsWindows()) throw new AssertFailedException("Windows ACL cleanup requires Windows.");
                    var restored = new FileSecurity();
                    restored.SetSecurityDescriptorSddlForm(originalFileAcl, AccessControlSections.Access);
                    file.SetAccessControl(restored);
                    Assert.AreEqual(originalFileAcl, file.GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access));
                }),
                CleanupFailureCollector.FromAction(() =>
                {
                    if (!OperatingSystem.IsWindows()) throw new AssertFailedException("Windows ACL cleanup requires Windows.");
                    var restored = new DirectorySecurity();
                    restored.SetSecurityDescriptorSddlForm(originalDirectoryAcl, AccessControlSections.Access);
                    directory.SetAccessControl(restored);
                    Assert.AreEqual(originalDirectoryAcl, directory.GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access));
                }),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    [TestMethod]
    public async Task Unix_WriteAllowedParentRenameDenied_RefusesWithoutChangingSource()
    {
        if (OperatingSystem.IsWindows()) { Assert.Inconclusive("Unix directory permission contract."); return; }
        var root = Path.Combine(TestTempRoot.Current, nameof(SourceTransitionPermissionTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "source.bin");
        await File.WriteAllBytesAsync(path, [1]);
        var originalMode = File.GetUnixFileMode(root);
        Exception? primary = null;
        try
        {
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            await File.WriteAllBytesAsync(path, [2]);
            CollectionAssert.AreEqual(new byte[] { 2 }, await File.ReadAllBytesAsync(path), "The original in-place persistence operation must succeed with a non-writable parent.");
            var retained = new List<PhysicalSourceTransition>();
            var namespaces = new List<string>();
            await Assert.ThrowsExactlyAsync<IOException>(() => PhysicalSourceTransition.ApplyAsync(path, [2], [3],
                PhysicalSourceTransitionMode.PreserveExistingObject, AcquireFixtureCustodyAsync, Authorize, retained.Add, namespaces.Add,
                PhysicalSourceTransition.OpenParent, null, CancellationToken.None));
            CollectionAssert.AreEqual(new byte[] { 2 }, await File.ReadAllBytesAsync(path));
            Assert.HasCount(0, retained);
            CollectionAssert.AreEqual(new[] { path }, Directory.GetFileSystemEntries(root));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Permission contract fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() =>
                {
                    if (OperatingSystem.IsWindows()) throw new AssertFailedException("Unix mode cleanup requires Unix.");
                    File.SetUnixFileMode(root, originalMode);
                }),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    private static async Task<PhysicalSourceCustody> AcquireFixtureCustodyAsync(PhysicalDirectoryScope source,
        string leaf, string temporary, CancellationToken ct)
    {
        await Authorize([source.Path, Path.Combine(source.Path, leaf), Path.Combine(source.Path, temporary)], ct);
        var parent = PhysicalDirectoryScope.OpenRoute(source.Path);
        return new PhysicalSourceCustody(source, parent, leaf, parent.AssertCurrentPath, parent.AssertCurrentPath, Authorize);
    }

    private static Task Authorize(IReadOnlyList<string> paths, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        foreach (var path in paths) RoslynMcp.Roslyn.Services.WorkspaceManager.EnsurePhysicalPath(path);
        return Task.CompletedTask;
    }
}
