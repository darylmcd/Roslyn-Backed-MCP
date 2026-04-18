using System.Diagnostics;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// post-edit-validate-workspace-scoped-to-touched-files: covers the new
/// <c>validate_recent_git_changes</c> companion that auto-derives <c>changedFilePaths</c>
/// from <c>git status --porcelain</c> and falls back to full-workspace scope when git is
/// unavailable or the solution is outside a git repo.
/// </summary>
[TestClass]
public sealed class ValidateRecentGitChangesTests : TestBase
{
    private static WorkspaceValidationService _validationService = null!;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        InitializeServices();
        _validationService = new WorkspaceValidationService(
            CompileCheckService,
            DiagnosticService,
            TestDiscoveryService,
            TestRunnerService,
            WorkspaceManager,
            ChangeTracker);
    }

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    // ------------------------------------------------------------------
    // Happy path: a dirty working tree with 3 edited `.cs` files must scope
    // the validation to just those file-owning projects (proved by the
    // `ChangedFilePaths` returned on the DTO matching the dirty set).
    // ------------------------------------------------------------------
    [TestMethod]
    public async Task ValidateRecentGitChangesAsync_DirtyTree_ScopesToTouchedCsFiles()
    {
        if (!IsGitAvailable())
        {
            Assert.Inconclusive("git not on PATH — cannot run the happy-path test.");
            return;
        }

        var solutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(solutionPath)!;
        InitializeGitRepo(solutionDir);
        StageAndCommitAll(solutionDir); // seed HEAD so only our edits appear as Modified

        // Edit three existing `.cs` files so porcelain reports them as Modified — the
        // initial `git add -A && commit` above ensures nothing else is pending.
        var file1 = Path.Combine(solutionDir, "SampleLib", "AnimalService.cs");
        var file2 = Path.Combine(solutionDir, "SampleLib", "AnimalExtensions.cs");
        var file3 = Path.Combine(solutionDir, "SampleLib", "Cat.cs");
        foreach (var path in new[] { file1, file2, file3 })
        {
            // Append-only mutation keeps the file compilable (just adds a trailing comment).
            await File.AppendAllTextAsync(path, $"{Environment.NewLine}// touched {Guid.NewGuid():N}{Environment.NewLine}");
        }

        var status = await WorkspaceManager.LoadAsync(solutionPath, CancellationToken.None);
        var workspaceId = status.WorkspaceId;
        try
        {
            var result = await _validationService.ValidateRecentGitChangesAsync(
                workspaceId, runTests: false, CancellationToken.None);

            // Warnings must be empty — git was present, so the scoped path ran.
            Assert.AreEqual(0, result.Warnings.Count,
                $"Expected no warnings on happy path; got [{string.Join("; ", result.Warnings)}].");

            // Because we committed the seed state first, only our three edits are pending.
            // The scoped set must be exactly those three (set-comparison — order / casing
            // may vary across platforms).
            var changedLower = result.ChangedFilePaths
                .Select(p => Path.GetFullPath(p).ToLowerInvariant())
                .ToHashSet();
            Assert.AreEqual(3, changedLower.Count,
                $"Expected exactly 3 touched files; got: {string.Join("; ", result.ChangedFilePaths)}");
            Assert.IsTrue(changedLower.Contains(Path.GetFullPath(file1).ToLowerInvariant()),
                $"Missing {file1} in ChangedFilePaths: {string.Join("; ", result.ChangedFilePaths)}");
            Assert.IsTrue(changedLower.Contains(Path.GetFullPath(file2).ToLowerInvariant()),
                $"Missing {file2} in ChangedFilePaths: {string.Join("; ", result.ChangedFilePaths)}");
            Assert.IsTrue(changedLower.Contains(Path.GetFullPath(file3).ToLowerInvariant()),
                $"Missing {file3} in ChangedFilePaths: {string.Join("; ", result.ChangedFilePaths)}");

            // UnknownFilePaths must be empty — every touched file lives in the workspace.
            Assert.AreEqual(0, result.UnknownFilePaths.Count,
                $"Unexpected unknown paths: [{string.Join("; ", result.UnknownFilePaths)}].");

            // OverallStatus must surface — the bundle ran end-to-end.
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.OverallStatus));
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
        }
    }

    // ------------------------------------------------------------------
    // No-git-repo fallback: copy the sample solution to a temp directory that
    // is NOT inside a git repo. Must fall back to full-workspace scope and
    // surface the reason in Warnings.
    // ------------------------------------------------------------------
    [TestMethod]
    public async Task ValidateRecentGitChangesAsync_NoGitRepo_FallsBackWithWarning()
    {
        var solutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(solutionPath)!;

        // Ensure no `.git` entry is anywhere at or above the solution dir. The helper
        // returns a path under `%TEMP%` which should never be inside a repo, but we
        // guard explicitly so the test is robust against future changes.
        RemoveAnyAncestorGitDir(solutionDir);

        var status = await WorkspaceManager.LoadAsync(solutionPath, CancellationToken.None);
        var workspaceId = status.WorkspaceId;
        try
        {
            var result = await _validationService.ValidateRecentGitChangesAsync(
                workspaceId, runTests: false, CancellationToken.None);

            Assert.AreEqual(1, result.Warnings.Count,
                $"Expected exactly one warning on git-unavailable fallback; got [{string.Join("; ", result.Warnings)}].");
            StringAssert.Contains(result.Warnings[0], "git",
                "Warning must explain why git scoping was skipped.");
            StringAssert.Contains(result.Warnings[0], "full workspace",
                "Warning must indicate the fallback scope.");

            // OverallStatus must still surface — the bundle ran end-to-end against the
            // full workspace instead of the (now-empty) scoped set.
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.OverallStatus));
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
        }
    }

    // ------------------------------------------------------------------
    // Clean working tree in a git repo: `git status` returns nothing. The
    // scoped set is empty — no fallback warning, but also no test discovery.
    // This codifies the "empty scope is a clean signal, not a fallback trigger"
    // design choice in ValidateRecentGitChangesAsync.
    // ------------------------------------------------------------------
    [TestMethod]
    public async Task ValidateRecentGitChangesAsync_CleanRepo_EmptyScope_NoWarnings()
    {
        if (!IsGitAvailable())
        {
            Assert.Inconclusive("git not on PATH — cannot run the clean-tree test.");
            return;
        }

        var solutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(solutionPath)!;
        InitializeGitRepo(solutionDir);
        StageAndCommitAll(solutionDir);

        var status = await WorkspaceManager.LoadAsync(solutionPath, CancellationToken.None);
        var workspaceId = status.WorkspaceId;
        try
        {
            var result = await _validationService.ValidateRecentGitChangesAsync(
                workspaceId, runTests: false, CancellationToken.None);

            Assert.AreEqual(0, result.Warnings.Count,
                $"Expected no warnings on clean tree; got [{string.Join("; ", result.Warnings)}].");
            Assert.AreEqual(0, result.ChangedFilePaths.Count,
                $"Expected empty scope on clean tree; got [{string.Join("; ", result.ChangedFilePaths)}].");
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.OverallStatus));
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
        }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static bool IsGitAvailable()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process is null) return false;
            process.WaitForExit(5_000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void InitializeGitRepo(string directory)
    {
        // `-b main` forces an initial branch name (avoids the "hint: Using 'master'" chatter
        // and the 'unborn HEAD' state that breaks `git config` on some git versions where
        // config discovery walks from HEAD rather than the worktree root).
        RunGit(directory, "init", "-q", "-b", "main");
        if (!Directory.Exists(Path.Combine(directory, ".git")))
        {
            throw new InvalidOperationException(
                $"git init appeared to succeed but '.git' is missing in '{directory}'.");
        }
        // `--local` targets the repo just initialized explicitly so git doesn't fall back
        // to searching an ancestor when CWD discovery is flaky.
        RunGit(directory, "config", "--local", "user.email", "ci@example.invalid");
        RunGit(directory, "config", "--local", "user.name", "CI");
        RunGit(directory, "config", "--local", "commit.gpgsign", "false");
    }

    private static void StageAndCommitAll(string directory)
    {
        RunGit(directory, "add", "-A");
        RunGit(directory, "commit", "-q", "-m", "seed");
    }

    private static void RunGit(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start git {string.Join(' ', arguments)}.");
        process.WaitForExit(30_000);
        if (process.ExitCode != 0)
        {
            var stderr = process.StandardError.ReadToEnd();
            var stdout = process.StandardOutput.ReadToEnd();
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} exited {process.ExitCode}. stdout=[{stdout}] stderr=[{stderr}]");
        }
    }

    /// <summary>
    /// Walks upward from <paramref name="directory"/> removing any <c>.git</c> entry we
    /// find so we can prove the "not inside a git repo" fallback. Defensive: the helper
    /// in <c>CreateSampleSolutionCopy</c> already targets a temp path outside the repo,
    /// but a future refactor could change that — this guarantee keeps the test
    /// deterministic.
    /// </summary>
    private static void RemoveAnyAncestorGitDir(string directory)
    {
        DirectoryInfo? current = new(directory);
        while (current is not null)
        {
            var gitEntry = Path.Combine(current.FullName, ".git");
            try
            {
                if (Directory.Exists(gitEntry))
                    Directory.Delete(gitEntry, recursive: true);
                else if (File.Exists(gitEntry))
                    File.Delete(gitEntry);
            }
            catch
            {
                // Best-effort: the primary repo's `.git` is locked by the running test host
                // and cannot be removed. That's fine — the nested sample-solution-copy
                // directory we care about sits under %TEMP% and never contains a `.git`
                // anyway, so this loop is purely belt-and-braces.
                break;
            }
            current = current.Parent;
        }
    }
}
