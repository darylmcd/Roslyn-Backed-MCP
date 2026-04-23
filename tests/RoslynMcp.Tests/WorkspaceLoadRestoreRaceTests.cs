using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace RoslynMcp.Tests;

/// <summary>
/// Covers <c>dr-9-10-initial-does-not-wait-for-concurrent-to-finaliz</c> (P4): when a
/// concurrent out-of-process <c>dotnet restore</c> is mutating
/// <c>obj/project.assets.json</c> during <see cref="WorkspaceManager.LoadAsync"/>, the
/// loader must wait (bounded by <see cref="WorkspaceManagerOptions.RestoreRaceWaitMs"/>)
/// for the mtime to stabilise before handing the <c>MSBuildWorkspace</c> snapshot to
/// callers. Eliminates the CS1705 drift pattern from the 2026-04-15 samplesolution
/// experimental-promotion audit.
///
/// Each test constructs its own <see cref="WorkspaceManager"/> against an isolated
/// sample-solution copy so toggling <c>RestoreRaceWaitMs</c> cannot bleed into other
/// test classes.
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class WorkspaceLoadRestoreRaceTests : SharedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    /// <summary>
    /// Baseline: when no in-flight restore exists (no <c>obj/</c> artefacts) the wait path
    /// must be a no-op. Without this invariant every single <c>workspace_load</c> on a
    /// pristine checkout would eat the full configured cap.
    /// </summary>
    /// <remarks>
    /// Timing threshold design: MSBuild's <c>OpenSolutionAsync</c> on the sample solution
    /// typically costs 1.5–3s wall time (JIT, NuGet cache warmup, target-framework probing).
    /// To detect "the wait cap fired despite no artefacts" without being flaky on a cold
    /// host, we use a deliberately large cap (<c>30000 ms</c>) and assert the load finishes
    /// in substantially less — a regression that forgets the artefacts-empty short-circuit
    /// would block for the full 30s rather than the ~2s a real no-op takes.
    /// </remarks>
    [TestMethod]
    public async Task LoadAsync_NoRestoreArtifacts_DoesNotDelayLoad()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;
        using var manager = CreateIsolatedManager(restoreRaceWaitMs: 30000);
        try
        {
            // Scrub any obj/ directories that other test classes may have seeded into the
            // source samples tree before the copy ran. Without this, running the race tests
            // after an integration-style test that triggered a restore would include stale
            // project.assets.json files in the copy, causing the probe to enter its stable-
            // window wait (~250ms) and defeating the "no artefacts = no wait" invariant.
            ScrubObjDirectories(copiedRoot);
            Assert.IsFalse(
                Directory.EnumerateFiles(copiedRoot, "project.assets.json", SearchOption.AllDirectories).Any(),
                "Precondition: scrubbed sample solution must not contain restore artefacts.");

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var status = await manager.LoadAsync(copiedSolutionPath, CancellationToken.None);
            stopwatch.Stop();

            Assert.IsTrue(status.IsLoaded);
            // The 30s cap must be entirely bypassed. 10s is a conservative ceiling that
            // catches a "wait fires despite empty artefact set" regression (which would
            // block for the full 30000 ms) while tolerating cold-MSBuild wall-time noise.
            Assert.IsTrue(
                stopwatch.ElapsedMilliseconds < 10000,
                $"LoadAsync with no restore artefacts should not invoke the wait path (cap 30000 ms). Elapsed: {stopwatch.ElapsedMilliseconds} ms.");
        }
        finally
        {
            DeleteDirectoryIfExists(copiedRoot);
        }
    }

    /// <summary>
    /// Positive path: if <c>project.assets.json</c> is being rewritten when we start the
    /// load, the wait must persist until the writer stops touching the file and the 250ms
    /// stability window elapses. Proves the mtime probe is actually observed.
    /// </summary>
    /// <remarks>
    /// Behavioural invariant (not a timing check): the writer task declares the instant it
    /// stops touching the file; the load must have stabilised against that timestamp before
    /// returning. This removes reliance on wall-time jitter in MSBuild's <c>OpenSolutionAsync</c>,
    /// which varies by 2× between cold / warm CI runs.
    /// </remarks>
    [TestMethod]
    public async Task LoadAsync_InFlightRestore_WaitsForMtimeToStabilize()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;
        using var manager = CreateIsolatedManager(restoreRaceWaitMs: 5000);
        try
        {
            // Start from a clean slate — unrelated obj/ artefacts copied over from the
            // shared samples tree would also enter the wait window and blur the measurement
            // of the single asset file we're about to touch.
            ScrubObjDirectories(copiedRoot);

            // Simulate the on-disk state of an in-flight restore: create an obj/ directory
            // with a project.assets.json under one of the copied projects. The background
            // writer below keeps touching the mtime for ~800 ms, reproducing the fsnotify
            // signal that MSBuildWorkspace would otherwise latch onto.
            var writerProjectDir = Path.Combine(copiedRoot, "SampleLib");
            Assert.IsTrue(Directory.Exists(writerProjectDir), "Precondition: SampleLib project directory must exist in the copy.");
            var objDir = Path.Combine(writerProjectDir, "obj");
            Directory.CreateDirectory(objDir);
            var assetsPath = Path.Combine(objDir, "project.assets.json");
            await File.WriteAllTextAsync(assetsPath, "{\"version\":3,\"targets\":{}}", CancellationToken.None);

            using var writerCts = new CancellationTokenSource();
            var writerStop = TimeSpan.FromMilliseconds(800);
            DateTime writerLastTouchUtc = DateTime.MinValue;
            var writerTask = Task.Run(async () =>
            {
                var start = DateTime.UtcNow;
                while (!writerCts.IsCancellationRequested && (DateTime.UtcNow - start) < writerStop)
                {
                    try
                    {
                        var now = DateTime.UtcNow;
                        File.SetLastWriteTimeUtc(assetsPath, now);
                        writerLastTouchUtc = now;
                    }
                    catch (IOException)
                    {
                        // Transient sharing violation on Windows — the probe itself also
                        // tolerates these; loop and retry.
                    }
                    await Task.Delay(40, CancellationToken.None);
                }
            }, writerCts.Token);

            var loadStartUtc = DateTime.UtcNow;
            var status = await manager.LoadAsync(copiedSolutionPath, CancellationToken.None);
            var loadCompletedUtc = DateTime.UtcNow;

            writerCts.Cancel();
            try { await writerTask; } catch (OperationCanceledException) { /* expected */ }

            Assert.IsTrue(status.IsLoaded);

            // Behavioural invariant: the load must have completed AFTER the writer's last
            // mtime touch plus the stability window. If the wait short-circuited we'd see
            // loadCompletedUtc inside the writer's active window.
            Assert.AreNotEqual(DateTime.MinValue, writerLastTouchUtc,
                "Precondition: the writer must have successfully touched the mtime at least once.");
            var gapFromLastTouchMs = (loadCompletedUtc - writerLastTouchUtc).TotalMilliseconds;
            Assert.IsTrue(
                gapFromLastTouchMs >= 100,
                $"LoadAsync must return only AFTER the writer stops touching the mtime (stability window ~250 ms, allow 100 ms floor for scheduler jitter). Gap: {gapFromLastTouchMs} ms.");

            // Sanity: the load wasn't suspiciously fast. A load that returns before the
            // writer even starts would indicate the mtime probe never ran.
            var loadDurationMs = (loadCompletedUtc - loadStartUtc).TotalMilliseconds;
            Assert.IsTrue(
                loadDurationMs >= 500,
                $"LoadAsync should have waited at least through part of the writer's 800 ms activity window. Duration: {loadDurationMs} ms.");

            // Upper bound — cap (5000 ms) plus MSBuild load time. 30s catches the
            // pathological "wait never returned" case without being flaky on cold MSBuild.
            Assert.IsTrue(
                loadDurationMs < 30000,
                $"LoadAsync should finish within a reasonable bound of the cap. Duration: {loadDurationMs} ms.");
        }
        finally
        {
            DeleteDirectoryIfExists(copiedRoot);
        }
    }

    /// <summary>
    /// Kill-switch path: setting <c>RestoreRaceWaitMs = 0</c> must restore the legacy
    /// pre-fix behaviour (no wait, no mtime probing). Confirms operators have a runtime
    /// opt-out via <c>ROSLYNMCP_RESTORE_RACE_WAIT_MS=0</c>.
    /// </summary>
    /// <remarks>
    /// Writer runs for 20 seconds so the test can reliably observe "wait did NOT fire"
    /// without timing noise — a regression that ignores the disable flag would block for
    /// 20s (well above the 10s ceiling), while an honest disabled path completes in
    /// roughly the MSBuild load cost (~2–3s).
    /// </remarks>
    [TestMethod]
    public async Task LoadAsync_WaitDisabled_DoesNotWaitEvenWithInFlightWriter()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;
        using var manager = CreateIsolatedManager(restoreRaceWaitMs: 0);
        try
        {
            ScrubObjDirectories(copiedRoot);

            var writerProjectDir = Path.Combine(copiedRoot, "SampleLib");
            var objDir = Path.Combine(writerProjectDir, "obj");
            Directory.CreateDirectory(objDir);
            var assetsPath = Path.Combine(objDir, "project.assets.json");
            await File.WriteAllTextAsync(assetsPath, "{\"version\":3,\"targets\":{}}", CancellationToken.None);

            using var writerCts = new CancellationTokenSource();
            var writerStop = TimeSpan.FromSeconds(20);
            var writerTask = Task.Run(async () =>
            {
                var start = DateTime.UtcNow;
                while (!writerCts.IsCancellationRequested && (DateTime.UtcNow - start) < writerStop)
                {
                    try { File.SetLastWriteTimeUtc(assetsPath, DateTime.UtcNow); }
                    catch (IOException) { /* tolerate */ }
                    await Task.Delay(40, CancellationToken.None);
                }
            }, writerCts.Token);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var status = await manager.LoadAsync(copiedSolutionPath, CancellationToken.None);
            stopwatch.Stop();

            writerCts.Cancel();
            try { await writerTask; } catch (OperationCanceledException) { /* expected */ }

            Assert.IsTrue(status.IsLoaded);
            // With the wait disabled, load cost should be dominated by MSBuild itself.
            // The writer runs for 20s, so an honest disabled path completes well under
            // 10s; a regression that ignores the flag would sit inside the wait loop
            // until the writer stops (~20s).
            Assert.IsTrue(
                stopwatch.ElapsedMilliseconds < 10000,
                $"With RestoreRaceWaitMs=0, LoadAsync must not block on the writer. Elapsed: {stopwatch.ElapsedMilliseconds} ms.");
        }
        finally
        {
            DeleteDirectoryIfExists(copiedRoot);
        }
    }

    [TestMethod]
    public async Task ReloadAsync_WithAutoRestore_ClearsRestoreRequiredAfterPackageVersionEdit()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;
        using var manager = CreateIsolatedManager(restoreRaceWaitMs: 0);
        var commandRunner = new DotnetCommandRunner();

        try
        {
            await RunRestoreAsync(commandRunner, copiedSolutionPath, CancellationToken.None);

            var status = await manager.LoadAsync(copiedSolutionPath, CancellationToken.None);
            Assert.IsFalse(status.RestoreRequired, "Freshly restored workspace should not report restore drift.");

            var packagesPropsPath = Path.Combine(copiedRoot, "Directory.Packages.props");
            var originalProps = await File.ReadAllTextAsync(packagesPropsPath, CancellationToken.None);
            const string originalVersion = "<PackageVersion Include=\"Microsoft.NET.Test.Sdk\" Version=\"17.14.0\" />";
            const string updatedVersion = "<PackageVersion Include=\"Microsoft.NET.Test.Sdk\" Version=\"17.14.1\" />";
            StringAssert.Contains(originalProps, originalVersion, "Test fixture drifted; update the expected central package version.");
            await File.WriteAllTextAsync(
                packagesPropsPath,
                originalProps.Replace(originalVersion, updatedVersion, StringComparison.Ordinal),
                CancellationToken.None);

            status = await manager.ReloadAsync(status.WorkspaceId, CancellationToken.None);
            Assert.IsTrue(status.RestoreRequired, "Reload must signal restoreRequired after a package-version edit without restore.");

            var summary = WorkspaceStatusSummaryDto.From(status);
            Assert.IsTrue(summary.RestoreRequired, "Summary projection must preserve restoreRequired.");
            StringAssert.Contains(summary.RestoreHint ?? string.Empty, "dotnet restore",
                "Summary hint should point callers at restore when package inputs drift.");

            status = await WorkspaceTools.RestoreAndReloadIfRequiredAsync(
                commandRunner,
                manager,
                status,
                autoRestore: true,
                CancellationToken.None);

            Assert.IsFalse(status.RestoreRequired, "Auto-restore should clear restoreRequired after rerunning restore and reload.");
            Assert.AreEqual(
                "17.14.1",
                ReadCentralPackageVersion(
                    Path.Combine(copiedRoot, "SampleLib.Tests", "obj", "project.assets.json"),
                    "Microsoft.NET.Test.Sdk"),
                "dotnet restore should refresh project.assets.json to the edited central package version.");
        }
        finally
        {
            DeleteDirectoryIfExists(copiedRoot);
        }
    }

    /// <summary>
    /// Removes every <c>obj/</c> subtree in the copied sample solution so the probe sees
    /// a clean tree before each test seeds its own artefacts. Guards against cross-test
    /// contamination: other test classes may trigger a <c>dotnet restore</c> against the
    /// source <c>samples/</c> tree, and <see cref="CreateSampleSolutionCopy"/> propagates
    /// those <c>obj/</c> directories into the temp copy verbatim.
    /// </summary>
    private static void ScrubObjDirectories(string copiedRoot)
    {
        foreach (var objDir in Directory.EnumerateDirectories(copiedRoot, "obj", SearchOption.AllDirectories).ToArray())
        {
            try
            {
                Directory.Delete(objDir, recursive: true);
            }
            catch (IOException)
            {
                // Tolerate transient IO errors — a concurrent build may hold a handle.
            }
            catch (UnauthorizedAccessException)
            {
                // Same — best effort only.
            }
        }
    }

    private static WorkspaceManager CreateIsolatedManager(int restoreRaceWaitMs)
    {
        return new WorkspaceManager(
            NullLogger<WorkspaceManager>.Instance,
            new PreviewStore(),
            new FileWatcherService(NullLogger<FileWatcherService>.Instance),
            new WorkspaceManagerOptions
            {
                MaxConcurrentWorkspaces = 4,
                RestoreRaceWaitMs = restoreRaceWaitMs,
            });
    }

    private static async Task RunRestoreAsync(DotnetCommandRunner commandRunner, string solutionPath, CancellationToken ct)
    {
        var workingDirectory = Path.GetDirectoryName(solutionPath)!;
        var execution = await commandRunner.RunAsync(
            workingDirectory,
            solutionPath,
            ["restore", solutionPath, "--nologo"],
            ct);

        Assert.IsTrue(
            execution.Succeeded,
            $"dotnet restore failed for test fixture. ExitCode={execution.ExitCode} StdOut={execution.StdOut} StdErr={execution.StdErr}");
    }

    private static string? ReadCentralPackageVersion(string assetsPath, string packageId)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(assetsPath));
        if (!document.RootElement.TryGetProperty("project", out var project) ||
            !project.TryGetProperty("frameworks", out var frameworks))
        {
            return null;
        }

        foreach (var framework in frameworks.EnumerateObject())
        {
            if (!framework.Value.TryGetProperty("centralPackageVersions", out var centralVersions))
            {
                continue;
            }

            foreach (var package in centralVersions.EnumerateObject())
            {
                if (string.Equals(package.Name, packageId, StringComparison.OrdinalIgnoreCase))
                {
                    return package.Value.GetString();
                }
            }
        }

        return null;
    }
}
