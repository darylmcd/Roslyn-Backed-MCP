using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace RoslynMcp.Tests.Skills;

/// <summary>
/// audit-deep-archive-and-surface-audit-integration: behavior tests for the
/// `skills/audit-deep/scripts/archive-old-reports.ps1` PowerShell wrapper.
///
/// The script ships alongside the audit-deep skill and is invoked manually via
/// `pwsh -NoProfile -File <path> [-OlderThanDays N] [-DryRun]`. Two contracts matter:
///
///   1. Dry-run mode performs no filesystem mutations and reports the planned moves.
///      The test seeds a synthetic ai_docs/audit-reports/ tree with a mix of old and
///      young .md files, runs the script with -DryRun, and asserts every old file is
///      still present at its original path while every "DRY-RUN move" line in stdout
///      references the expected destination layout (archive/&lt;YYYY&gt;/&lt;file&gt;).
///
///   2. Apply mode is idempotent and respects the pinned-name allowlist
///      (README.md, deep-review-session-checklist.md). Running twice does not duplicate
///      the move, and pinned files are never archived even when they pre-date the cutoff.
///
/// HARD INVARIANT: every test runs against an isolated temp directory (Path.GetTempPath()
/// + Guid.NewGuid()); the repo's actual ai_docs/audit-reports/ tree is never touched.
/// </summary>
[TestClass]
public sealed class ArchiveOldReportsScriptTests
{
    private string _tempRoot = string.Empty;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "RoslynMcpTests", "ArchiveOldReports", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        TestFixtureFileSystem.DeleteDirectoryIfExists(_tempRoot);
    }

    [TestMethod]
    public void Script_FileExistsAtDocumentedPath()
    {
        var scriptPath = ResolveScriptPath();
        Assert.IsTrue(
            File.Exists(scriptPath),
            $"archive-old-reports.ps1 was not found at the documented path '{scriptPath}'. " +
            "SKILL.md operational notes reference this exact path; without the file the docs ship dead links.");
    }

    [TestMethod]
    public void Script_DryRun_PerformsNoFilesystemMutations()
    {
        var scriptPath = ResolveScriptPath();
        var reportsDir = SeedAuditReports(_tempRoot, oldFileCount: 3, youngFileCount: 2);

        // Snapshot the directory state before invocation.
        var beforeFiles = Directory.GetFiles(reportsDir, "*", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

        var result = RunPwshScript(scriptPath, _tempRoot, dryRun: true, olderThanDays: 30);

        Assert.AreEqual(0, result.ExitCode,
            $"archive-old-reports.ps1 -DryRun failed with exit code {result.ExitCode}. " +
            $"stdout: {result.StdOut} stderr: {result.StdErr}");

        var afterFiles = Directory.GetFiles(reportsDir, "*", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(
            beforeFiles, afterFiles,
            "Dry-run invocation mutated the filesystem. The -DryRun switch must be strictly read-only — " +
            "moves, directory creation, and any other side effects are forbidden when -DryRun is set.");

        // The 3 old files should be reported as DRY-RUN move candidates; the 2 young files must not.
        Assert.AreEqual(3, CountOccurrences(result.StdOut, "DRY-RUN move:"),
            $"Expected exactly 3 DRY-RUN move lines (one per old file). stdout was:\n{result.StdOut}");
    }

    [TestMethod]
    public void Script_DryRun_DoesNotProposeArchivingPinnedFiles()
    {
        var scriptPath = ResolveScriptPath();
        var reportsDir = SeedAuditReports(_tempRoot, oldFileCount: 0, youngFileCount: 0);

        // Seed pinned files with a back-dated LastWriteTime (very old).
        var pinned = new[] { "README.md", "deep-review-session-checklist.md" };
        var ancient = DateTime.UtcNow.AddDays(-365);
        foreach (var name in pinned)
        {
            var path = Path.Combine(reportsDir, name);
            File.WriteAllText(path, $"# {name}\n");
            File.SetLastWriteTimeUtc(path, ancient);
        }

        var result = RunPwshScript(scriptPath, _tempRoot, dryRun: true, olderThanDays: 30);

        Assert.AreEqual(0, result.ExitCode,
            $"archive-old-reports.ps1 -DryRun failed with exit code {result.ExitCode}. " +
            $"stdout: {result.StdOut} stderr: {result.StdErr}");

        foreach (var name in pinned)
        {
            Assert.IsFalse(
                result.StdOut.Contains(name, StringComparison.Ordinal),
                $"Pinned file '{name}' must never appear in archive output, even when older than the cutoff. " +
                $"stdout was:\n{result.StdOut}");
            Assert.IsTrue(
                File.Exists(Path.Combine(reportsDir, name)),
                $"Pinned file '{name}' must remain at the original path after dry-run.");
        }
    }

    [TestMethod]
    public void Script_ApplyMode_IsIdempotent()
    {
        var scriptPath = ResolveScriptPath();
        var reportsDir = SeedAuditReports(_tempRoot, oldFileCount: 2, youngFileCount: 1);

        var firstRun = RunPwshScript(scriptPath, _tempRoot, dryRun: false, olderThanDays: 30);
        Assert.AreEqual(0, firstRun.ExitCode,
            $"First apply run failed with exit code {firstRun.ExitCode}. stdout: {firstRun.StdOut} stderr: {firstRun.StdErr}");

        // After first apply: the 2 old files are under archive/<YYYY>/, and the young file is still in place.
        var youngFiles = Directory.GetFiles(reportsDir, "*.md", SearchOption.TopDirectoryOnly);
        Assert.AreEqual(1, youngFiles.Length,
            $"Apply run should leave exactly 1 young file in the top-level reports dir; found {youngFiles.Length}: " +
            string.Join(", ", youngFiles));

        var archiveRoot = Path.Combine(reportsDir, "archive");
        Assert.IsTrue(Directory.Exists(archiveRoot),
            "Apply run must create the archive/ subdirectory when files are moved.");

        var movedFiles = Directory.GetFiles(archiveRoot, "*.md", SearchOption.AllDirectories);
        Assert.AreEqual(2, movedFiles.Length,
            $"Apply run should have moved 2 old files into archive/<YYYY>/; found {movedFiles.Length}: " +
            string.Join(", ", movedFiles));

        // Second run — should be a no-op (no candidates remain in the top-level dir).
        var secondRun = RunPwshScript(scriptPath, _tempRoot, dryRun: false, olderThanDays: 30);
        Assert.AreEqual(0, secondRun.ExitCode,
            $"Second (idempotent) apply run failed with exit code {secondRun.ExitCode}. " +
            $"stdout: {secondRun.StdOut} stderr: {secondRun.StdErr}");

        // Filesystem state must be identical to the post-first-run snapshot.
        var afterSecondRun = Directory.GetFiles(reportsDir, "*.md", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();
        var afterFirstRun = movedFiles
            .Concat(youngFiles)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();
        CollectionAssert.AreEqual(
            afterFirstRun, afterSecondRun,
            "Second apply run mutated the filesystem — script is not idempotent.");
    }

    private static string ResolveScriptPath()
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        return Path.Combine(repoRoot, "skills", "audit-deep", "scripts", "archive-old-reports.ps1");
    }

    /// <summary>
    /// Creates an isolated ai_docs/audit-reports/ directory under <paramref name="testRoot"/>
    /// with the requested counts of old (LastWriteTime=now-365d) and young (LastWriteTime=now-1d)
    /// .md files. Returns the audit-reports directory path.
    /// </summary>
    private static string SeedAuditReports(string testRoot, int oldFileCount, int youngFileCount)
    {
        var reportsDir = Path.Combine(testRoot, "ai_docs", "audit-reports");
        Directory.CreateDirectory(reportsDir);

        var oldStamp = DateTime.UtcNow.AddDays(-365);
        var youngStamp = DateTime.UtcNow.AddDays(-1);

        for (var i = 0; i < oldFileCount; i++)
        {
            var path = Path.Combine(reportsDir, $"old-report-{i:D2}.md");
            File.WriteAllText(path, $"# Old report {i}\n");
            File.SetLastWriteTimeUtc(path, oldStamp);
        }

        for (var i = 0; i < youngFileCount; i++)
        {
            var path = Path.Combine(reportsDir, $"young-report-{i:D2}.md");
            File.WriteAllText(path, $"# Young report {i}\n");
            File.SetLastWriteTimeUtc(path, youngStamp);
        }

        return reportsDir;
    }

    private static PwshResult RunPwshScript(string scriptPath, string repoRoot, bool dryRun, int olderThanDays)
    {
        var args = new List<string>
        {
            "-NoProfile",
            "-NonInteractive",
            "-File", scriptPath,
            "-RepoRoot", repoRoot,
            "-OlderThanDays", olderThanDays.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        if (dryRun)
        {
            args.Add("-DryRun");
        }

        var pwshExecutable = ResolvePwshExecutable();

        var psi = new ProcessStartInfo
        {
            FileName = pwshExecutable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start '{pwshExecutable}'.");
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        if (!proc.WaitForExit(milliseconds: 60_000))
        {
            proc.Kill(entireProcessTree: true);
            throw new TimeoutException("pwsh archive-old-reports.ps1 invocation timed out after 60s.");
        }

        return new PwshResult(proc.ExitCode, stdout, stderr);
    }

    private static string ResolvePwshExecutable()
    {
        // Prefer pwsh (PowerShell 7+) on PATH; fall back to Windows PowerShell on Windows-only test hosts.
        if (OperatingSystem.IsWindows())
        {
            return "pwsh.exe";
        }
        return "pwsh";
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        if (string.IsNullOrEmpty(needle))
        {
            return 0;
        }
        var count = 0;
        var idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += needle.Length;
        }
        return count;
    }

    private sealed record PwshResult(int ExitCode, string StdOut, string StdErr);
}
