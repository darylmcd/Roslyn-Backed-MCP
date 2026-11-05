using System.Diagnostics;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression guard for <c>analyzer-shadow-sweep-live-child-authority</c>. A foreign host's
/// sweeper used to authorize deleting a workspace-owned lease child from directory AGE alone
/// (plus the sweeper's own in-process registry, which cannot see another host's leases). Age is
/// not liveness: a lease open for more than a day can still copy a dependency late
/// (<c>ShadowCopy</c>, then <c>LoadFromAssemblyPath</c>), and a copy that lands after the sweeper
/// read the age but before the load loses its file. The authority is now the per-lease sibling
/// marker <c>&lt;workspaceDirectory&gt;/&lt;leaseId&gt;.owner.lock</c>: held means live, released
/// means disposed or abandoned.
///
/// <para>
/// Evidence boundary: the exact interleaving (age snapshot, then late copy, then delete) is source
/// proof only; no timing seam is added and no timer padding is used. What these tests reproduce at
/// runtime is its consequence on the pre-fix code: an unmapped late copy in a stale lease directory
/// is deleted. They also correct a premise the row repeated: the Windows recursive delete does NOT
/// continue past a locked file. It aborts at the first entry it cannot open, so a mapped sibling
/// protects only the entries enumerated after it and earlier siblings are still lost (a partial
/// delete), which is why the sibling case pins the file names.
/// </para>
/// </summary>
// [DoNotParallelize] is not needed: every test uses its own shared parent under TestTempRoot and
// never touches the machine-global shadow directory, except the production-path test, which uses a
// unique workspace id.
[TestClass]
public sealed class AnalyzerShadowForeignLeaseAuthorityTests
{
    private const string IdentityGuid = "0123456789abcdef0123456789abcdef";

    // Cross-platform: the holder is this test process, which is not the registered owner of the lease
    // (no lease object exists), so the sweeper must consult the marker to know the lease is live.
    [TestMethod]
    public void HeldMarker_LeaseWithOnlyUnmappedLateCopy_IsPreservedDespiteStaleAge_ThenReclaimedOnceReleased()
    {
        var shared = NewSharedParent();
        try
        {
            var workspace = Path.Combine(shared, $"workspace-{Environment.ProcessId}-{IdentityGuid}-late");
            var lease = Path.Combine(workspace, "lease");
            var marker = MarkerPath(lease);
            Directory.CreateDirectory(lease);
            // Only an unmapped late copy: nothing locks it, so the pre-fix delete removes it.
            var late = Path.Combine(lease, "late.dll");
            File.WriteAllText(late, "late dependency copy");
            using (var held = HoldMarker(marker))
            {
                Age(lease);
                AnalyzerReferenceIsolation.SweepAbandonedRoots(shared, NullLogger.Instance);
                Assert.IsTrue(File.Exists(late), "A lease whose marker is held is live however old its directory is.");
                Assert.IsTrue(File.Exists(marker));
            }

            AnalyzerReferenceIsolation.SweepAbandonedRoots(shared, NullLogger.Instance);
            Assert.IsFalse(Directory.Exists(lease), "A released marker authorizes reclaiming the stale tree.");
            Assert.IsFalse(File.Exists(marker), "The marker is deleted once its tree is gone.");
        }
        finally { TestFixtureFileSystem.DeleteDirectoryIfExists(shared); }
    }

    // Names are pinned: the pre-fix recursive delete visits entries in name order and aborts at the
    // first it cannot open, so only a late copy that sorts BEFORE the locked file is lost there.
    // The post-fix assertion (nothing in a held lease is touched) is order-independent.
    [TestMethod]
    public void HeldMarker_LateCopyBesideMappedSibling_IsNotPartiallyDeleted()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("A file open with FileShare.None blocks deletion only on Windows.");

        var shared = NewSharedParent();
        try
        {
            var workspace = Path.Combine(shared, $"workspace-{Environment.ProcessId}-{IdentityGuid}-sibling");
            var lease = Path.Combine(workspace, "lease");
            Directory.CreateDirectory(lease);
            var late = Path.Combine(lease, "a-late.dll");
            var mapped = Path.Combine(lease, "z-mapped.dll");
            File.WriteAllText(late, "late dependency copy");
            using var mappedHandle = new FileStream(mapped, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            using var held = HoldMarker(MarkerPath(lease));
            Age(lease);

            AnalyzerReferenceIsolation.SweepAbandonedRoots(shared, NullLogger.Instance);

            Assert.IsTrue(File.Exists(late), "A mapped sibling is not subtree-wide authority: the unmapped copy must survive.");
            Assert.IsTrue(File.Exists(mapped));
        }
        finally { TestFixtureFileSystem.DeleteDirectoryIfExists(shared); }
    }

    [TestMethod]
    public void ReleasedMarker_StaleLease_IsSweptAndItsMarkerRemoved()
    {
        var shared = NewSharedParent();
        try
        {
            var workspace = Path.Combine(shared, $"workspace-{Environment.ProcessId}-{IdentityGuid}-released");
            var lease = Path.Combine(workspace, "lease");
            Directory.CreateDirectory(lease);
            File.WriteAllText(Path.Combine(lease, "copy.dll"), "copy");
            using (File.Create(MarkerPath(lease))) { }
            Age(lease);

            AnalyzerReferenceIsolation.SweepAbandonedRoots(shared, NullLogger.Instance);

            Assert.IsFalse(Directory.Exists(lease));
            Assert.IsFalse(File.Exists(MarkerPath(lease)), "A reclaimed lease must not leave its marker behind.");
        }
        finally { TestFixtureFileSystem.DeleteDirectoryIfExists(shared); }
    }

    [TestMethod]
    public void OrphanMarker_ReleasedIsReaped_HeldIsKept()
    {
        var shared = NewSharedParent();
        try
        {
            var workspace = Path.Combine(shared, $"workspace-{Environment.ProcessId}-{IdentityGuid}-orphans");
            Directory.CreateDirectory(workspace);
            var released = Path.Combine(workspace, "released-lease.owner.lock");
            var heldPath = Path.Combine(workspace, "held-lease.owner.lock");
            using (File.Create(released)) { }
            using var held = HoldMarker(heldPath);

            AnalyzerReferenceIsolation.SweepAbandonedRoots(shared, NullLogger.Instance);

            Assert.IsFalse(File.Exists(released), "A released marker with no lease directory is an orphan.");
            Assert.IsTrue(File.Exists(heldPath), "A held marker with no directory yet is a lease before its first copy: never touched.");
        }
        finally { TestFixtureFileSystem.DeleteDirectoryIfExists(shared); }
    }

    // The reap must precede the prune check: a released marker whose tree is already gone would
    // otherwise keep an exited creator's workspace directory non-empty forever.
    [TestMethod]
    public async Task DeadCreator_WorkspaceWithOnlyAReleasedOrphanMarker_IsPruned()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("The owned cmd.exe peer is Windows-specific.");

        var shared = NewSharedParent();
        using var peer = StartPeer(shared);
        try
        {
            await ReadReadyAsync(peer);
            var workspace = Path.Combine(shared, $"workspace-{peer.Id}-{IdentityGuid}-dead");
            Directory.CreateDirectory(workspace);
            using (File.Create(Path.Combine(workspace, "gone-lease.owner.lock"))) { }

            peer.StandardInput.Close();
            await peer.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            // The started Process handle is retained, so the exited PID cannot be recycled.
            AnalyzerReferenceIsolation.SweepAbandonedRoots(shared, NullLogger.Instance);

            Assert.IsFalse(Directory.Exists(workspace));
        }
        finally
        {
            peer.StandardInput.Close();
            if (!peer.HasExited) peer.Kill(entireProcessTree: true);
            await peer.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            TestFixtureFileSystem.DeleteDirectoryIfExists(shared);
        }
    }

    // The cross-process proof: a REAL second host (the peer is the workspace's creator and stays alive)
    // and a REAL separate process holding the marker, the shape the production lease has.
    [TestMethod]
    public async Task LivePeer_ForeignHolderOfMarker_PreservesLateCopy_ThenReleasedMarkerIsSweptWhilePeerLives()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("The owned cmd.exe peer and marker holder are Windows-specific.");

        var shared = NewSharedParent();
        using var peer = StartPeer(shared);
        using var holder = new MarkerHolder();
        try
        {
            await ReadReadyAsync(peer);
            var workspace = Path.Combine(shared, $"workspace-{peer.Id}-{IdentityGuid}-peer");
            var lease = Path.Combine(workspace, "lease");
            var marker = MarkerPath(lease);
            Directory.CreateDirectory(lease);
            var late = Path.Combine(lease, "late.dll");
            File.WriteAllText(late, "late dependency copy");
            await holder.StartAsync(marker);
            Age(lease);

            AnalyzerReferenceIsolation.SweepAbandonedRoots(shared, NullLogger.Instance);
            Assert.IsTrue(File.Exists(late), "A live peer's lease with a held marker must keep an unmapped late copy.");
            Assert.IsTrue(File.Exists(marker));

            holder.Kill();
            // The marker handle is held by the cmd group and its ping child: their exit is observable
            // slightly before the last handle closes, so re-sweep until the reclaim lands.
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            do
            {
                AnalyzerReferenceIsolation.SweepAbandonedRoots(shared, NullLogger.Instance);
                if (!Directory.Exists(lease)) break;
                await Task.Delay(50);
            }
            while (DateTime.UtcNow < deadline);

            Assert.IsFalse(Directory.Exists(lease), "A released marker authorizes reclaiming even a live peer's stale lease.");
            Assert.IsFalse(File.Exists(marker));
            Assert.IsTrue(Directory.Exists(workspace), "The live creator's workspace directory is never pruned.");
        }
        finally
        {
            holder.Kill();
            peer.StandardInput.Close();
            if (!peer.HasExited) peer.Kill(entireProcessTree: true);
            await peer.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            TestFixtureFileSystem.DeleteDirectoryIfExists(shared);
        }
    }

    // A disposed lease whose tree could not be deleted must keep its (released) marker: that released
    // marker is the only thing that lets a foreign sweeper, including a live peer's, reclaim the
    // leftover. Deleting the marker anyway would make the tree markerless and preserved until the
    // peer exits.
    [TestMethod]
    public async Task DisposedLease_WithUndeletableTree_KeepsItsReleasedMarker_SoALivePeerSweepReclaimsItOnceUnlocked()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("An undeletable tree needs Windows file-lock semantics and the owned cmd.exe peer.");

        var shared = NewSharedParent();
        using var peer = StartPeer(shared);
        FileStream? locked = null;
        try
        {
            await ReadReadyAsync(peer);
            var workspace = Path.Combine(shared, $"workspace-{peer.Id}-{IdentityGuid}-dispose");
            var lease = Path.Combine(workspace, "lease");
            var marker = MarkerPath(lease);
            Directory.CreateDirectory(lease);
            locked = new FileStream(Path.Combine(lease, "locked.dll"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);

            var shadowLease = new AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease(
                lease, AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease.AcquireOwnerMarker(lease), [], 1, NullLogger.Instance);
            shadowLease.Dispose();

            Assert.IsTrue(Directory.Exists(lease), "The locked tree cannot be deleted.");
            Assert.IsTrue(File.Exists(marker), "The disposed lease must leave its released marker with the leftover tree.");
            Assert.IsFalse(AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease.IsLiveRoot(lease));

            Age(lease);
            AnalyzerReferenceIsolation.SweepAbandonedRoots(shared, NullLogger.Instance);
            Assert.IsTrue(Directory.Exists(lease), "Still locked: the sweep fails and keeps the tree.");
            Assert.IsTrue(File.Exists(marker), "A failed reclaim must keep the released marker for the next sweep.");

            locked.Dispose();
            AnalyzerReferenceIsolation.SweepAbandonedRoots(shared, NullLogger.Instance);
            Assert.IsFalse(Directory.Exists(lease), "The live peer's sweep reclaims a disposed lease through its released marker.");
            Assert.IsFalse(File.Exists(marker));
        }
        finally
        {
            locked?.Dispose();
            peer.StandardInput.Close();
            if (!peer.HasExited) peer.Kill(entireProcessTree: true);
            await peer.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            TestFixtureFileSystem.DeleteDirectoryIfExists(shared);
        }
    }

    // B4: acquiring the marker must precede binding any reference to a lease loader, so a failure to
    // create it cannot leave references bound to loaders no lease will ever dispose.
    [TestMethod]
    public void Retarget_MarkerCreationFailure_Throws_AndLeavesReferencesUnbound()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("Analyzer shadow-file isolation is Windows-specific.");

        var projectRoot = Path.Combine(TestTempRoot.Current, "foreign-lease-b4-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(projectRoot);
        var workspaceId = "marker-failure-" + Guid.NewGuid().ToString("N");
        // A FILE where the workspace directory must be makes creating the marker fail.
        var blocker = AnalyzerReferenceIsolation.GetWorkspaceShadowDirectory(workspaceId);
        Directory.CreateDirectory(Path.GetDirectoryName(blocker)!);
        File.WriteAllText(blocker, "blocks the workspace shadow directory");
        try
        {
            var analyzerPath = Path.Combine(projectRoot, "OwnedAnalyzer.dll");
            File.Copy(typeof(AnalyzerReferenceIsolation).Assembly.Location, analyzerPath);
            using var adhoc = new AdhocWorkspace();
            var project = adhoc.AddProject(ProjectInfo.Create(
                ProjectId.CreateNewId(), VersionStamp.Create(), "B4Probe", "B4Probe", LanguageNames.CSharp,
                filePath: Path.Combine(projectRoot, "B4Probe.csproj")));
            var original = new UnreachableLoader();
            var reference = new AnalyzerFileReference(analyzerPath, original);
            var solution = project.Solution.AddAnalyzerReference(project.Id, reference);

            Assert.ThrowsExactly<IOException>(() =>
                AnalyzerReferenceIsolation.RetargetFileReferencesToShadowLoader(solution, workspaceId, NullLogger.Instance));

            var bound = typeof(AnalyzerFileReference)
                .GetField("_assemblyLoader", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(reference);
            Assert.AreSame(original, bound, "A reference must not be bound to a lease loader when the lease cannot exist.");
        }
        finally
        {
            File.Delete(blocker);
            TestFixtureFileSystem.DeleteDirectoryIfExists(projectRoot);
        }
    }

    private static string MarkerPath(string leaseDirectory) => leaseDirectory + ".owner.lock";

    private static FileStream HoldMarker(string markerPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
        return new FileStream(markerPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
    }

    private static string NewSharedParent()
    {
        var shared = Path.Combine(TestTempRoot.Current, "foreign-lease-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(shared);
        return shared;
    }

    /// <summary>Ages after the last write: writing into a directory refreshes its timestamps.</summary>
    private static void Age(string directory)
    {
        var timestamp = DateTime.UtcNow - TimeSpan.FromDays(2);
        Directory.SetCreationTimeUtc(directory, timestamp);
        Directory.SetLastWriteTimeUtc(directory, timestamp);
    }

    private static Process StartPeer(string workingDirectory)
    {
        var startInfo = new ProcessStartInfo("cmd.exe")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("echo ready & set /p RMCP_SHADOW_WAIT=");
        return Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start owned peer.");
    }

    private static async Task ReadReadyAsync(Process peer)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Assert.AreEqual("ready", (await peer.StandardOutput.ReadLineAsync(cancellation.Token))?.Trim());
    }

    /// <summary>
    /// A separate process that holds a file open until killed: the group-wide <c>9&gt;&gt;</c>
    /// redirection keeps the marker open for the group's lifetime (the pattern
    /// <c>AnalyzerShadowLoaderLifecycleTests</c> uses for the process-root marker).
    /// </summary>
    private sealed class MarkerHolder : IDisposable
    {
        private readonly string _readyPath = Path.Combine(TestTempRoot.Current, "marker-ready-" + Guid.NewGuid().ToString("N"));
        private readonly string _scriptPath = Path.Combine(TestTempRoot.Current, "marker-holder-" + Guid.NewGuid().ToString("N") + ".cmd");
        private Process? _process;

        public async Task StartAsync(string markerPath)
        {
            // ping.exe by absolute path: a bare `ping` resolves through the inherited PATH, and a
            // session without System32 on it ends the group at once, silently releasing the marker.
            File.WriteAllText(
                _scriptPath,
                "@echo off\r\n" +
                "( (echo ready>\"%RMCP_HOLD_READY%\") & \"%SystemRoot%\\System32\\ping.exe\" -n 120 127.0.0.1 >nul ) 9>>\"%RMCP_HOLD_FILE%\"\r\n");
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _scriptPath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                }
            };
            process.StartInfo.Environment["RMCP_HOLD_FILE"] = markerPath;
            process.StartInfo.Environment["RMCP_HOLD_READY"] = _readyPath;
            _process = process;
            Assert.IsTrue(process.Start(), "The marker holder must start.");
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            while (!File.Exists(_readyPath) && !process.HasExited && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }

            Assert.IsTrue(File.Exists(_readyPath),
                process.HasExited ? await process.StandardError.ReadToEndAsync() : "The holder did not open the marker.");
        }

        public void Kill()
        {
            if (_process is { HasExited: false } running)
            {
                running.Kill(entireProcessTree: true);
                running.WaitForExit(10_000);
            }
        }

        public void Dispose()
        {
            Kill();
            _process?.Dispose();
            File.Delete(_readyPath);
            File.Delete(_scriptPath);
        }
    }

    private sealed class UnreachableLoader : IAnalyzerAssemblyLoader
    {
        public void AddDependencyLocation(string fullPath)
        {
        }

        public Assembly LoadFromPath(string fullPath) =>
            throw new InvalidOperationException("The original loader must never be asked to load.");
    }
}
