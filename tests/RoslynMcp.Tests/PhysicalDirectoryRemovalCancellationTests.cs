using System.Reflection;
using Microsoft.Win32.SafeHandles;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class PhysicalDirectoryRemovalCancellationTests
{
    [TestMethod]
    public async Task PreCanceledRemoval_ClosesTransferredNativeHandle()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The transferred Windows removal handle has Windows sharing semantics.");
            return;
        }
        var root = Path.Combine(TestTempRoot.Current, "removal-cancel-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "owned.txt");
        SafeFileHandle? handle = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(path, "owned bytes", CancellationToken.None);
            handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Write);
            Assert.ThrowsExactly<IOException>(() => File.Delete(path),
                "The real open handle must prevent deletion before ownership transfer.");
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            MethodInfo method = typeof(PhysicalDirectoryLease).GetMethod("RequireNonDirectoryRemoval",
                BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new AssertFailedException("The actual ownership-taking helper must exist.");
            TargetInvocationException wrapper = Assert.ThrowsExactly<TargetInvocationException>(
                () => method.Invoke(null, [handle, canceled.Token]));
            Assert.IsInstanceOfType<OperationCanceledException>(wrapper.InnerException);
            Assert.AreEqual(canceled.Token, ((OperationCanceledException)wrapper.InnerException!).CancellationToken);
            Assert.IsTrue(handle.IsClosed, "Cancellation must release the transferred handle before cleanup can run.");
            File.Delete(path);
            Assert.IsFalse(File.Exists(path), "The canceled transfer must release its actual native deletion exclusion.");
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Canceled removal and fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => handle?.Dispose()),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }
}
