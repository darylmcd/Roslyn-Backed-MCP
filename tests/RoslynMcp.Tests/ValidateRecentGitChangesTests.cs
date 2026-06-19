using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Support;

namespace RoslynMcp.Tests;

/// <summary>
/// post-edit-validate-workspace-scoped-to-touched-files: covers the new
/// <c>validate_recent_git_changes</c> companion that auto-derives <c>changedFilePaths</c>
/// from <c>git status --porcelain</c> and falls back to full-workspace scope when git is
/// unavailable or the solution is outside a git repo.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class ValidateRecentGitChangesTests : IsolatedWorkspaceTestBase
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

        await using var workspace = CreateIsolatedWorkspaceCopy();
        InitializeGitRepo(workspace.RootPath);
        GitFixtureRunner.StageAndCommitAll(workspace.RootPath); // seed HEAD so only our edits appear as Modified

        // Edit three existing `.cs` files so porcelain reports them as Modified — the
        // initial `git add -A && commit` above ensures nothing else is pending.
        var file1 = workspace.GetPath("SampleLib", "AnimalService.cs");
        var file2 = workspace.GetPath("SampleLib", "AnimalExtensions.cs");
        var file3 = workspace.GetPath("SampleLib", "Cat.cs");
        foreach (var path in new[] { file1, file2, file3 })
        {
            // Append-only mutation keeps the file compilable (just adds a trailing comment).
            await File.AppendAllTextAsync(path, $"{Environment.NewLine}// touched {Guid.NewGuid():N}{Environment.NewLine}");
        }

        await workspace.LoadAsync(CancellationToken.None);

        var result = await _validationService.ValidateRecentGitChangesAsync(
            workspace.WorkspaceId, runTests: false, CancellationToken.None);

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

    [TestMethod]
    public async Task ValidateRecentGitChangesAsync_SlowValidationPhase_ReturnsRetryableTimeoutWithGitScope()
    {
        if (!IsGitAvailable())
        {
            Assert.Inconclusive("git not on PATH — cannot run the timeout-scope test.");
            return;
        }

        await using var workspace = CreateIsolatedWorkspaceCopy();
        InitializeGitRepo(workspace.RootPath);
        GitFixtureRunner.StageAndCommitAll(workspace.RootPath);

        var touchedFile = workspace.GetPath("SampleLib", "AnimalService.cs");
        await File.AppendAllTextAsync(touchedFile, $"{Environment.NewLine}// timeout scope {Guid.NewGuid():N}{Environment.NewLine}");

        await workspace.LoadAsync(CancellationToken.None);
        var timeoutService = new WorkspaceValidationService(
            new SlowCompileCheckService(),
            DiagnosticService,
            TestDiscoveryService,
            TestRunnerService,
            WorkspaceManager,
            ChangeTracker,
            gitStatusTimeout: TimeSpan.FromSeconds(5),
            validationPhaseTimeout: TimeSpan.FromMilliseconds(25));

        var result = await timeoutService.ValidateRecentGitChangesAsync(
            workspace.WorkspaceId, runTests: false, CancellationToken.None);

        Assert.AreEqual("timeout", result.OverallStatus);
        Assert.AreEqual(1, result.ChangedFilePaths.Count,
            $"Timeout envelope should keep the git-derived changed-file set; got [{string.Join("; ", result.ChangedFilePaths)}].");
        Assert.AreEqual(Path.GetFullPath(touchedFile), Path.GetFullPath(result.ChangedFilePaths[0]));
        Assert.IsTrue(result.Warnings.Any(w => w.Contains("compile_check", StringComparison.Ordinal)
            && w.Contains("retryable=true", StringComparison.Ordinal)),
            $"Expected retryable compile_check timeout warning; got [{string.Join("; ", result.Warnings)}].");
        Assert.IsNotNull(result.TestRunResult?.FailureEnvelope,
            "Timeout result should include a structured retry envelope.");
        Assert.AreEqual("Timeout", result.TestRunResult.FailureEnvelope.ErrorKind);
        Assert.IsTrue(result.TestRunResult.FailureEnvelope.IsRetryable);
        StringAssert.Contains(result.TestRunResult.FailureEnvelope.Summary, "compile_check");
    }

    // ------------------------------------------------------------------
    // No-git-repo fallback: copy the sample solution to a temp directory that
    // is NOT inside a git repo. Must fall back to full-workspace scope and
    // surface the reason in Warnings.
    // ------------------------------------------------------------------
    [TestMethod]
    public async Task ValidateRecentGitChangesAsync_NoGitRepo_FallsBackWithWarning()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        // Ensure no `.git` entry is anywhere at or above the solution dir. The helper
        // returns a path under `%TEMP%` which should never be inside a repo, but we
        // guard explicitly so the test is robust against future changes.
        RemoveAnyAncestorGitDir(workspace.RootPath);

        await workspace.LoadAsync(CancellationToken.None);

        var result = await _validationService.ValidateRecentGitChangesAsync(
            workspace.WorkspaceId, runTests: false, CancellationToken.None);

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

        await using var workspace = CreateIsolatedWorkspaceCopy();
        InitializeGitRepo(workspace.RootPath);
        GitFixtureRunner.StageAndCommitAll(workspace.RootPath);

        await workspace.LoadAsync(CancellationToken.None);

        var result = await _validationService.ValidateRecentGitChangesAsync(
            workspace.WorkspaceId, runTests: false, CancellationToken.None);

        Assert.AreEqual(0, result.Warnings.Count,
            $"Expected no warnings on clean tree; got [{string.Join("; ", result.Warnings)}].");
        Assert.AreEqual(0, result.ChangedFilePaths.Count,
            $"Expected empty scope on clean tree; got [{string.Join("; ", result.ChangedFilePaths)}].");
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.OverallStatus));
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
        GitFixtureRunner.RunGit(directory, "init", "-q", "-b", "main");
        if (!Directory.Exists(Path.Combine(directory, ".git")))
        {
            throw new InvalidOperationException(
                $"git init appeared to succeed but '.git' is missing in '{directory}'.");
        }
        File.WriteAllText(Path.Combine(directory, ".gitignore"), "bin/\nobj/\n");
        // `--local` targets the repo just initialized explicitly so git doesn't fall back
        // to searching an ancestor when CWD discovery is flaky.
        GitFixtureRunner.RunGit(directory, "config", "--local", "user.email", "ci@example.invalid");
        GitFixtureRunner.RunGit(directory, "config", "--local", "user.name", "CI");
        GitFixtureRunner.RunGit(directory, "config", "--local", "commit.gpgsign", "false");
        GitFixtureRunner.RunGit(directory, "config", "--local", "core.autocrlf", "false");
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

    private sealed class SlowCompileCheckService : ICompileCheckService
    {
        public async Task<CompileCheckDto> CheckAsync(
            string workspaceId,
            CompileCheckOptions options,
            CancellationToken ct)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("unreachable");
        }
    }

    // ------------------------------------------------------------------
    // Observability: a git process-tree kill failure during the
    // git-status timeout/failure cleanup must be logged at Warning (with the
    // process id) when a logger is present, never swallowed silently — mirrors
    // DotnetCommandRunner.TryKillProcessTree. The kill delegate is injected so
    // the failure path is deterministic (no reliance on a real git timeout race).
    // ------------------------------------------------------------------
    [TestMethod]
    public void TryKillProcessTree_KillThrows_LogsWarningWithProcessId()
    {
        var logger = new ListLogger<WorkspaceValidationService>();
        var killException = new InvalidOperationException("simulated kill failure");

        var service = new WorkspaceValidationService(
            CompileCheckService,
            DiagnosticService,
            TestDiscoveryService,
            TestRunnerService,
            WorkspaceManager,
            ChangeTracker,
            gitStatusTimeout: TimeSpan.FromSeconds(5),
            validationPhaseTimeout: TimeSpan.FromSeconds(5),
            logger: logger,
            killProcessTree: _ => throw killException);

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
            Arguments = OperatingSystem.IsWindows() ? "/c exit" : "-c \"exit 0\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        Assert.IsNotNull(process, "Could not start a host process for the test.");
        process.WaitForExit(5_000);

        // Must not throw — the kill failure is best-effort and swallowed for the caller.
        service.TryKillProcessTree(process, "timeout");

        var entry = logger.Entries.SingleOrDefault(candidate =>
            candidate.Level == LogLevel.Warning &&
            candidate.Message.Contains("Failed to kill git process tree", StringComparison.Ordinal) &&
            candidate.Message.Contains(process.Id.ToString(), StringComparison.Ordinal) &&
            ReferenceEquals(candidate.Exception, killException));

        Assert.IsNotNull(entry,
            "A git kill failure must be observable at Warning level with the process id; "
            + $"got [{string.Join("; ", logger.Entries.Select(e => $"{e.Level}:{e.Message}"))}].");
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception), exception));
        }
    }
}
