using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class PhysicalInputIdentityCancellationTests
{
    [TestMethod]
    [DataRow("file")]
    [DataRow("directory")]
    [DataRow("missing")]
    public async Task Capture_AlreadyCanceled_RefusesEveryPhysicalKind(string kind)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(PhysicalInputIdentityCancellationTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "input");
            if (kind == "file") await File.WriteAllTextAsync(path, "owned bytes");
            else if (kind == "directory") Directory.CreateDirectory(path);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var failure = await Assert.ThrowsAsync<OperationCanceledException>(() => PhysicalInputIdentity.CaptureAsync(path, cancellation.Token));
            Assert.AreEqual(cancellation.Token, failure.CancellationToken);
        }
        finally { TestFixtureFileSystem.DeleteDirectoryIfExists(root); }
    }
}
