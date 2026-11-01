using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class AnalyzerShadowLeaseDisposalTests
{
    [TestMethod]
    public void Dispose_AfterLoaderFailure_CanFinishCleanupOnRetry()
    {
        var root = Path.Combine(TestTempRoot.Current, "shadow-dispose-retry-" + Guid.NewGuid().ToString("N"));
        var shadowRoot = Path.Combine(root, "lease");
        Directory.CreateDirectory(shadowRoot);
        File.WriteAllText(Path.Combine(shadowRoot, "copy.dll"), "fixture");
        var loaders = new List<AnalyzerReferenceIsolation.ShadowCopyAnalyzerAssemblyLoader> { null! };
        var lease = new AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease(
            shadowRoot, loaders, 0, NullLogger.Instance);

        try
        {
            Assert.ThrowsExactly<NullReferenceException>(() => lease.Dispose());
            Assert.IsTrue(AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease.IsLiveRoot(shadowRoot));

            loaders.Clear();
            lease.Dispose();

            Assert.IsFalse(Directory.Exists(shadowRoot));
            Assert.IsFalse(AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease.IsLiveRoot(shadowRoot));
            lease.Dispose();
        }
        finally
        {
            loaders.Clear();
            lease.Dispose();
            TestFixtureFileSystem.DeleteDirectoryIfExists(root);
        }
    }
}
