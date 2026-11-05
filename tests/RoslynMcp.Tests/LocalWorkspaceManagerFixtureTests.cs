using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class LocalWorkspaceManagerFixtureTests
{
    [TestMethod]
    public async Task SecondCopyFailure_DisposesManagerAndDeletesFirstCopy()
    {
        WorkspaceManager? manager = null;
        string? firstRoot = null;
        var copyFailure = new IOException("Second solution copy failed.");
        var copyCount = 0;

        var observed = await Assert.ThrowsExactlyAsync<IOException>(() =>
            LocalWorkspaceManagerFixture.RunAsync(
                () => manager = new WorkspaceManager(
                    NullLogger<WorkspaceManager>.Instance,
                    new PreviewStore(),
                    new FileWatcherService(NullLogger<FileWatcherService>.Instance)),
                () =>
                {
                    if (++copyCount == 2)
                        throw copyFailure;

                    firstRoot = Path.Combine(TestTempRoot.Current, Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(firstRoot);
                    return Path.Combine(firstRoot, "Sample.slnx");
                },
                2,
                (_, _) => throw new AssertFailedException("The body must not run after copy failure.")));

        Assert.AreSame(copyFailure, observed);
        Assert.AreEqual(2, copyCount);
        Assert.IsNotNull(firstRoot);
        Assert.IsFalse(Directory.Exists(firstRoot), "The first copy must be removed after the second copy fails.");
        Assert.IsNotNull(manager);
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() =>
            manager.LoadAsync(firstRoot, CancellationToken.None));
    }
}
