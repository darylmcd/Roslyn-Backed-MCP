using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Roslyn.Helpers;
using CreatorLiveness = RoslynMcp.Roslyn.Helpers.AnalyzerReferenceIsolation.CreatorLiveness;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class AnalyzerShadowCreatorOwnershipTests
{
    private const string IdentityGuid = "0123456789abcdef0123456789abcdef";

    [TestMethod]
    [DataRow("process-17-0123456789abcdef0123456789abcdef")]
    [DataRow("workspace-17-0123456789abcdef0123456789abcdef-id-with-hyphens")]
    public void IdentifiedParent_RequiresProvenAbsence(string name)
    {
        Assert.AreEqual(17, AnalyzerReferenceIsolation.GetCreatorProcessId(name));
        Assert.IsTrue(AnalyzerReferenceIsolation.CanPruneParent(name, CreatorLiveness.Absent));
        Assert.IsFalse(AnalyzerReferenceIsolation.CanPruneParent(name, CreatorLiveness.Live), "PID reuse also retains the parent.");
        Assert.IsFalse(AnalyzerReferenceIsolation.CanPruneParent(name, CreatorLiveness.Unknown));
    }

    [TestMethod]
    [DataRow("process-0-0123456789abcdef0123456789abcdef")]
    [DataRow("process--17-0123456789abcdef0123456789abcdef")]
    [DataRow("process-+17-0123456789abcdef0123456789abcdef")]
    [DataRow("process- 17-0123456789abcdef0123456789abcdef")]
    [DataRow("process-2147483648-0123456789abcdef0123456789abcdef")]
    [DataRow("process-17-bad")]
    [DataRow("process-17- 0123456789abcdef0123456789abcdef ")]
    [DataRow("process-17-")]
    [DataRow("process-17-0123456789abcdef0123456789abcdef-extra")]
    [DataRow("PROCESS-17-0123456789abcdef0123456789abcdef")]
    [DataRow("workspace-17-0123456789abcdef0123456789abcdef")]
    [DataRow("workspace-17-0123456789abcdef0123456789abcdef-")]
    [DataRow("workspace-0-0123456789abcdef0123456789abcdef-id")]
    [DataRow("workspace-17-bad-id")]
    [DataRow("workspace-17- 0123456789abcdef0123456789abcdef -id")]
    [DataRow("legacy-workspace")]
    [DataRow("other-17-0123456789abcdef0123456789abcdef")]
    public void UnknownIdentity_NeverAuthorizesPruning(string name)
    {
        Assert.IsNull(AnalyzerReferenceIsolation.GetCreatorProcessId(name));
        Assert.AreEqual(CreatorLiveness.Unknown, AnalyzerReferenceIsolation.ObserveCreator(name));
        Assert.IsFalse(AnalyzerReferenceIsolation.CanPruneParent(name, CreatorLiveness.Absent));
        Assert.IsFalse(AnalyzerReferenceIsolation.CanPruneParent(name, CreatorLiveness.Live));
        Assert.IsFalse(AnalyzerReferenceIsolation.CanPruneParent(name, CreatorLiveness.Unknown));
    }

    [TestMethod]
    public void WorkspaceDirectory_IsStableAndCarriesCurrentProcessIdentity()
    {
        var first = AnalyzerReferenceIsolation.GetWorkspaceShadowDirectory("workspace-with-hyphens");
        Assert.AreEqual(first, AnalyzerReferenceIsolation.GetWorkspaceShadowDirectory("workspace-with-hyphens"));
        Assert.AreNotEqual(first, AnalyzerReferenceIsolation.GetWorkspaceShadowDirectory("another-workspace"));
        Assert.AreEqual(Environment.ProcessId, AnalyzerReferenceIsolation.GetCreatorProcessId(Path.GetFileName(first)));
        Assert.AreEqual(CreatorLiveness.Live, AnalyzerReferenceIsolation.ObserveCreator(Path.GetFileName(first)));
    }

    [TestMethod]
    [DataRow("process-initializing")]
    [DataRow("PROCESS-17-0123456789abcdef0123456789abcdef")]
    [DataRow("process-0-0123456789abcdef0123456789abcdef")]
    public void MissingMarker_UnknownProcessFamily_PreservesEntireRoot(string name)
    {
        var shared = NewSharedParent();
        var parent = Path.Combine(shared, name);
        var stale = CreateStaleChild(parent);
        var fresh = Path.Combine(parent, "fresh");
        Directory.CreateDirectory(fresh);
        try
        {
            AnalyzerReferenceIsolation.SweepAbandonedRoots(shared, NullLogger.Instance);
            Assert.IsTrue(Directory.Exists(stale));
            Assert.IsTrue(Directory.Exists(fresh));
        }
        finally { TestFixtureFileSystem.DeleteDirectoryIfExists(shared); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void WorkspaceChildren_AreSweptWhileLiveOrLegacyParentRemains(bool legacy)
    {
        var shared = NewSharedParent();
        var name = legacy ? "legacy-workspace" : $"workspace-{Environment.ProcessId}-{IdentityGuid}-id";
        var parent = Path.Combine(shared, name);
        var stale = CreateStaleChild(parent);
        try
        {
            AnalyzerReferenceIsolation.SweepAbandonedRoots(shared, NullLogger.Instance);
            Assert.IsFalse(Directory.Exists(stale));
            Assert.IsTrue(Directory.Exists(parent));
        }
        finally { TestFixtureFileSystem.DeleteDirectoryIfExists(shared); }
    }

    [TestMethod]
    public void RegisteredLiveLease_SurvivesActualPrivateSweepDespiteStaleTimestamps()
    {
        var shared = NewSharedParent();
        var parent = Path.Combine(shared, $"workspace-{Environment.ProcessId}-{IdentityGuid}-registered");
        var stale = CreateStaleChild(parent);
        var unregistered = CreateStaleChild(Path.Combine(shared, "legacy-unregistered"));
        try
        {
            using (var lease = new AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease(
                stale, [], 1, NullLogger.Instance))
            {
                Assert.IsTrue(AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease.IsLiveRoot(stale));
                AnalyzerReferenceIsolation.SweepAbandonedRoots(shared, NullLogger.Instance);
                Assert.IsTrue(Directory.Exists(stale), "Actual live-lease registration must exclude even stale children from deletion.");
                Assert.IsFalse(Directory.Exists(unregistered), "The same sweep must exercise stale-child deletion for an unregistered sibling.");
            }

            Assert.IsFalse(AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease.IsLiveRoot(stale));
            Assert.IsFalse(Directory.Exists(stale), "The real lease owner reclaims its private root on disposal.");
            Assert.IsTrue(Directory.Exists(parent), "Disposal owns only the unique lease root.");
        }
        finally { TestFixtureFileSystem.DeleteDirectoryIfExists(shared); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ReleasedMarker_AuthorizesCleanupEvenWithCurrentOrUnknownCreator(bool knownCreator)
    {
        var shared = NewSharedParent();
        var name = knownCreator ? $"process-{Environment.ProcessId}-{IdentityGuid}" : "PROCESS-legacy";
        var parent = Path.Combine(shared, name);
        Directory.CreateDirectory(parent);
        try
        {
            using (File.Create(Path.Combine(parent, ".owner.lock"))) { }
            AnalyzerReferenceIsolation.SweepAbandonedRoots(shared, NullLogger.Instance);
            Assert.IsFalse(Directory.Exists(parent));
        }
        finally { TestFixtureFileSystem.DeleteDirectoryIfExists(shared); }
    }

    [TestMethod]
    public async Task OwnedPeer_LiveThenExitedCreator_ControlsMissingMarkerAndWorkspaceCleanupAsync()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("The owned cmd stdin barrier and retained Windows process handle are Windows-specific.");

        var shared = NewSharedParent();
        var startInfo = new ProcessStartInfo("cmd.exe")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = shared
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("echo ready & set /p RMCP_SHADOW_WAIT=");
        using var peer = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start owned peer.");
        try
        {
            using var readyCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var ready = await peer.StandardOutput.ReadLineAsync(readyCancellation.Token);
            Assert.AreEqual("ready", ready?.Trim());
            var name = $"process-{peer.Id}-{IdentityGuid}";
            Assert.AreEqual(CreatorLiveness.Live, AnalyzerReferenceIsolation.ObserveCreator(name));
            var parent = Path.Combine(shared, name);
            var stale = CreateStaleChild(parent);
            var liveWorkspace = Path.Combine(shared, $"workspace-{peer.Id}-{IdentityGuid}-live");
            var workspaceStale = CreateStaleChild(liveWorkspace);
            AnalyzerReferenceIsolation.SweepAbandonedRoots(shared, NullLogger.Instance);
            Assert.IsTrue(Directory.Exists(stale), "A live markerless creator protects its entire process root.");
            Assert.IsFalse(Directory.Exists(workspaceStale));
            Assert.IsTrue(Directory.Exists(liveWorkspace));

            peer.StandardInput.Close();
            await peer.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            // set /p reports EOF as exit 1. Retain the started Process handle through all
            // observations so this exited PID cannot be recycled on Windows.
            Assert.AreEqual(1, peer.ExitCode);
            Assert.AreEqual(CreatorLiveness.Absent, AnalyzerReferenceIsolation.ObserveCreator(name));
            var deadProcess = Path.Combine(shared, $"process-{peer.Id}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(deadProcess);
            var deadWorkspace = Path.Combine(shared, $"workspace-{peer.Id}-{Guid.NewGuid():N}-dead");
            var deadStale = CreateStaleChild(deadWorkspace);
            AnalyzerReferenceIsolation.SweepAbandonedRoots(shared, NullLogger.Instance);
            Assert.IsFalse(Directory.Exists(deadProcess));
            Assert.IsFalse(Directory.Exists(deadStale));
            Assert.IsFalse(Directory.Exists(deadWorkspace));
            Assert.IsFalse(Directory.Exists(parent));
            Assert.IsFalse(Directory.Exists(liveWorkspace));
        }
        finally
        {
            peer.StandardInput.Close();
            if (!peer.HasExited) peer.Kill(entireProcessTree: true);
            await peer.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            TestFixtureFileSystem.DeleteDirectoryIfExists(shared);
        }
    }

    private static string NewSharedParent()
    {
        var shared = Path.Combine(TestTempRoot.Current, "creator-ownership-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(shared);
        return shared;
    }

    private static string CreateStaleChild(string parent)
    {
        var child = Path.Combine(parent, "stale-lease");
        Directory.CreateDirectory(child);
        File.WriteAllText(Path.Combine(child, "unused.dll"), "stale copy");
        var timestamp = DateTime.UtcNow - TimeSpan.FromDays(2);
        Directory.SetCreationTimeUtc(child, timestamp);
        Directory.SetLastWriteTimeUtc(child, timestamp);
        return child;
    }
}
