using System.Collections.Immutable;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class AnalyzerShadowNamespaceAdmissionTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExistingShadowRoot_IsNeverAdoptedForMaterialization(bool populated)
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Analyzer shadow materialization is Windows-specific.");
        var fixture = Path.Combine(TestTempRoot.Current, nameof(AnalyzerShadowNamespaceAdmissionTests), Guid.NewGuid().ToString("N"));
        var shadow = Path.Combine(fixture, "already-present");
        var sentinel = Path.Combine(shadow, "preserve.txt");
        var bytes = ImmutableArray.Create<byte>(1, 2, 3, 4);
        var source = Path.Combine(fixture, "source", "candidate.dll");
        var entry = new AnalyzerInputCatalog.Entry(source, null, bytes, Convert.ToHexString(SHA256.HashData(bytes.AsSpan())));
        AnalyzerReferenceIsolation.ShadowCopyAnalyzerAssemblyLoader? loader = null;
        PhysicalDirectoryScope.OwnedRoot? owner = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(shadow);
            if (populated) await File.WriteAllTextAsync(sentinel, "Existing namespace bytes");
            Exception? refusal = null;
            try
            {
                // Old-code evidence exercised CreateAsync's string-path admission. The
                // required owner now makes that admission explicit before materialization.
                owner = PhysicalDirectoryScope.CreateOwnedRoot(shadow);
                loader = await AnalyzerReferenceIsolation.ShadowCopyAnalyzerAssemblyLoader.CreateAsync(
                    [entry], owner, string.Empty, NullLogger.Instance, isCollectible: true, CancellationToken.None);
            }
            catch (IOException failure) { refusal = failure; }
            Assert.IsNotNull(refusal, "An existing directory is not fresh empty-object admission for this materialization.");
            Assert.IsFalse(File.Exists(Path.Combine(shadow, "candidate.dll")), "Admission must fail before copying bytes into the unrelated namespace.");
            if (populated) Assert.AreEqual("Existing namespace bytes", await File.ReadAllTextAsync(sentinel));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Analyzer namespace admission fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => loader?.UnloadAndRelease()),
                CleanupFailureCollector.FromAction(() => owner?.Dispose()),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(fixture)));
        }
    }
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CopyFailureAfterFirstImage_ReleasesPinsAndRetainsExactPrimary(bool replaceAncestor)
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Analyzer shadow materialization is Windows-specific.");
        var fixture = Path.Combine(TestTempRoot.Current, nameof(AnalyzerShadowNamespaceAdmissionTests), Guid.NewGuid().ToString("N"));
        var parent = Path.Combine(fixture, "parent");
        var shadow = Path.Combine(parent, "issued");
        var foreign = Path.Combine(fixture, "foreign");
        PhysicalDirectoryScope.OwnedRoot? owner = null;
        var bytes = ImmutableArray.Create<byte>(1, 2, 3, 4);
        var entry = new AnalyzerInputCatalog.Entry(Path.Combine(fixture, "candidate.dll"), null, bytes,
            Convert.ToHexString(SHA256.HashData(bytes.AsSpan())));
        var primary = new InvalidDataException("exact failure after first image");
        var attempted = false;
        var blocked = false;
        var entries = new InterruptedCatalog(entry, () => File.Exists(Path.Combine(shadow, "candidate.dll")), () =>
        {
            attempted = true;
            Assert.IsTrue(File.Exists(Path.Combine(shadow, "candidate.dll")), "The failure must occur after the first actual image write.");
            try { Directory.Move(replaceAncestor ? parent : shadow, Path.Combine(fixture, "retained")); }
            catch (IOException) { blocked = true; }
            throw primary;
        });
        Exception? failure = null;
        try
        {
            Directory.CreateDirectory(parent);
            Directory.CreateDirectory(foreign);
            await File.WriteAllTextAsync(Path.Combine(foreign, "preserve.txt"), "foreign namespace");
            owner = PhysicalDirectoryScope.CreateOwnedRoot(shadow);
            var observed = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
                AnalyzerReferenceIsolation.ShadowCopyAnalyzerAssemblyLoader.CreateAsync(entries, owner, string.Empty,
                    NullLogger.Instance, true, CancellationToken.None));
            Assert.AreSame(primary, observed);
            Assert.IsTrue(attempted && blocked, "The held Windows namespace must prevent root and ancestor replacement during materialization.");
            var retirement = new AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease(owner, [], 0, NullLogger.Instance);
            owner = null; // Transfer once; explicit retirement is part of the native lifetime assertion.
            retirement.Dispose();
            Assert.IsFalse(Directory.Exists(shadow), "Failed loader handles must release before exact-owner retirement.");
            Assert.IsFalse(AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease.IsLiveRoot(shadow));
            Assert.AreEqual("foreign namespace", await File.ReadAllTextAsync(Path.Combine(foreign, "preserve.txt")));
        }
        catch (Exception observed) { failure = observed; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Partial analyzer fixture cleanup failed.", failure,
                CleanupFailureCollector.FromAction(() => owner?.Dispose()),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(fixture)));
        }
    }

    private sealed class InterruptedCatalog(AnalyzerInputCatalog.Entry entry, Func<bool> materialized, Action interrupt) : IReadOnlyList<AnalyzerInputCatalog.Entry>
    {
        public int Count => 1;
        public AnalyzerInputCatalog.Entry this[int index] => index == 0 ? entry : throw new ArgumentOutOfRangeException(nameof(index));
        public IEnumerator<AnalyzerInputCatalog.Entry> GetEnumerator()
        {
            yield return entry;
            // Identity computation may enumerate before effects. Inject only at
            // the real partial-copy boundary while the first image is present.
            if (materialized()) interrupt();
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

}
