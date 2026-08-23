using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
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
    private static string? _gitUnavailableReason;

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
            Assert.Inconclusive($"git unavailable — cannot run the happy-path test. {_gitUnavailableReason}");
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
    public async Task ValidateRecentGitChangesAsync_AmbientRepositoryOverrides_DoNotEscapeSolutionScope()
    {
        if (!IsGitAvailable())
        {
            Assert.Inconclusive($"git unavailable — cannot run the process-environment test. {_gitUnavailableReason}");
            return;
        }

        await using var workspace = CreateIsolatedWorkspaceCopy();
        InitializeGitRepo(workspace.RootPath);
        GitFixtureRunner.StageAndCommitAll(workspace.RootPath);
        var touchedFile = workspace.GetPath("SampleLib", "AnimalService.cs");
        await File.AppendAllTextAsync(touchedFile, $"{Environment.NewLine}// ambient git override {Guid.NewGuid():N}{Environment.NewLine}");
        await workspace.LoadAsync(CancellationToken.None);

        var priorGitDir = Environment.GetEnvironmentVariable("GIT_DIR");
        var priorGitWorkTree = Environment.GetEnvironmentVariable("GIT_WORK_TREE");
        try
        {
            Environment.SetEnvironmentVariable("GIT_DIR", workspace.GetPath("missing-git-dir"));
            Environment.SetEnvironmentVariable("GIT_WORK_TREE", workspace.GetPath("missing-work-tree"));

            var result = await _validationService.ValidateRecentGitChangesAsync(
                workspace.WorkspaceId, runTests: false, CancellationToken.None);

            Assert.AreEqual(0, result.Warnings.Count,
                $"Git scoping must ignore ambient repository overrides; got [{string.Join("; ", result.Warnings)}].");
            Assert.AreEqual(1, result.ChangedFilePaths.Count);
            Assert.AreEqual(Path.GetFullPath(touchedFile), Path.GetFullPath(result.ChangedFilePaths[0]));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GIT_DIR", priorGitDir);
            Environment.SetEnvironmentVariable("GIT_WORK_TREE", priorGitWorkTree);
        }
    }

    [TestMethod]
    public async Task ValidateRecentGitChangesAsync_SlowValidationPhase_ReturnsRetryableTimeoutWithGitScope()
    {
        if (!IsGitAvailable())
        {
            Assert.Inconclusive($"git unavailable — cannot run the timeout-scope test. {_gitUnavailableReason}");
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
    // validate-recent-git-changes-status-timeout-false-clean: when the dedicated
    // `git status` collection times out, the bundle falls back to the in-process
    // change-tracker scope — which is EMPTY for edits made outside this MCP session.
    // Pre-fix that produced `overallStatus: clean` + `changedFilePaths: []` against a
    // demonstrably dirty tree, indistinguishable from a genuinely clean repo. The
    // verdict must now degrade to `git-status-unknown` while the retryable warning
    // is preserved. A 1 ms git timeout makes the timeout branch fire deterministically
    // regardless of real git speed (mirrors the SlowValidationPhase test's approach).
    // ------------------------------------------------------------------
    [TestMethod]
    public async Task ValidateRecentGitChangesAsync_GitStatusTimeout_ReportsGitStatusUnknownNotClean()
    {
        if (!IsGitAvailable())
        {
            Assert.Inconclusive($"git unavailable — cannot run the git-status-timeout test. {_gitUnavailableReason}");
            return;
        }

        await using var workspace = CreateIsolatedWorkspaceCopy();
        InitializeGitRepo(workspace.RootPath);
        GitFixtureRunner.StageAndCommitAll(workspace.RootPath);

        // Dirty the tree so a `clean` verdict would be provably wrong.
        var touchedFile = workspace.GetPath("SampleLib", "AnimalService.cs");
        await File.AppendAllTextAsync(touchedFile, $"{Environment.NewLine}// git-status timeout {Guid.NewGuid():N}{Environment.NewLine}");

        await workspace.LoadAsync(CancellationToken.None);

        // The compile/diagnostic stages are stubbed clean on purpose. The isolated fixture
        // copy has no restored NuGet packages, so the real CompileCheckService reports
        // CS0246-class errors and every verdict would be "compile-error" — which would mask
        // the behavior under test. Pinning the underlying verdict to "clean" isolates the one
        // thing this test is about: whether a git-status timeout downgrades it.
        var timeoutService = new WorkspaceValidationService(
            new CleanCompileCheckService(),
            new CleanDiagnosticService(),
            TestDiscoveryService,
            TestRunnerService,
            WorkspaceManager,
            ChangeTracker,
            gitStatusTimeout: TimeSpan.FromMilliseconds(1),
            validationPhaseTimeout: TimeSpan.FromMinutes(2));

        var result = await timeoutService.ValidateRecentGitChangesAsync(
            workspace.WorkspaceId, runTests: false, CancellationToken.None);

        Assert.AreEqual("git-status-unknown", result.OverallStatus,
            "A clean verdict computed over an unobserved git scope must degrade to git-status-unknown; "
            + $"warnings were [{string.Join("; ", result.Warnings)}].");

        // The fallback widened to changedFilePaths=null; this workspace id has no
        // change-tracker entries, so the scope really is empty — which is exactly the
        // false-clean shape the row reported (dirty tree, empty scope, passing verdict).
        Assert.AreEqual(0, result.ChangedFilePaths.Count,
            $"Expected the empty change-tracker fallback scope; got [{string.Join("; ", result.ChangedFilePaths)}].");

        Assert.IsTrue(result.Warnings.Any(w =>
                w.Contains("git status exceeded the timeout", StringComparison.Ordinal)
                && w.Contains("retryable=true", StringComparison.Ordinal)),
            $"Expected the retryable git-status timeout warning; got [{string.Join("; ", result.Warnings)}].");

        // Control: same stubs, same dirty tree, a git timeout that does NOT fire. The verdict
        // must stay "clean" — proving the new status is gated on the timeout rather than being
        // emitted unconditionally.
        var normalService = new WorkspaceValidationService(
            new CleanCompileCheckService(),
            new CleanDiagnosticService(),
            TestDiscoveryService,
            TestRunnerService,
            WorkspaceManager,
            ChangeTracker,
            gitStatusTimeout: TimeSpan.FromSeconds(30),
            validationPhaseTimeout: TimeSpan.FromMinutes(2));

        var control = await normalService.ValidateRecentGitChangesAsync(
            workspace.WorkspaceId, runTests: false, CancellationToken.None);

        Assert.AreEqual("clean", control.OverallStatus,
            $"Control run must stay clean; warnings were [{string.Join("; ", control.Warnings)}].");
        Assert.AreEqual(0, control.Warnings.Count,
            $"Control run must not warn; got [{string.Join("; ", control.Warnings)}].");
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
        RemoveGitEntry(workspace.RootPath);

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
            Assert.Inconclusive($"git unavailable — cannot run the clean-tree test. {_gitUnavailableReason}");
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
        => GitFixtureRunner.IsAvailable(out _gitUnavailableReason);

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
    /// Removes only the owned fixture's <c>.git</c> entry. Never walk into ancestors:
    /// a fixture-placement change must not turn this test into a repository-deletion hazard.
    /// </summary>
    private static void RemoveGitEntry(string directory)
    {
        var gitEntry = Path.Combine(directory, ".git");
        if (Directory.Exists(gitEntry))
        {
            DeleteDirectoryIfExists(gitEntry);
        }
        else if (File.Exists(gitEntry))
        {
            File.Delete(gitEntry);
        }
    }

    [TestMethod]
    public async Task ValidationBundleTools_Failure_PropagatesToStructuredFilterBoundary()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => ValidationBundleTools.ValidateRecentGitChanges(
                WorkspaceExecutionGate,
                new ThrowingWorkspaceValidationService(),
                workspace.WorkspaceId,
                ct: CancellationToken.None));

        Assert.AreEqual("injected validation failure", exception.Message);
    }

    /// <summary>
    /// validate-recent-git-changes-status-timeout-false-clean: pins the compile stage to a
    /// zero-error result so <c>ComputeOverallStatus</c> yields <c>clean</c> independently of the
    /// isolated fixture's (unrestored, therefore error-laden) compilation state.
    /// </summary>
    private sealed class CleanCompileCheckService : ICompileCheckService
    {
        public Task<CompileCheckDto> CheckAsync(
            string workspaceId,
            CompileCheckOptions options,
            CancellationToken ct) =>
            Task.FromResult(new CompileCheckDto(
                Success: true,
                ErrorCount: 0,
                WarningCount: 0,
                TotalDiagnostics: 0,
                ReturnedDiagnostics: 0,
                Offset: 0,
                Limit: 200,
                HasMore: false,
                Diagnostics: Array.Empty<DiagnosticDto>(),
                ElapsedMs: 0,
                Cancelled: false));
    }

    /// <summary>
    /// Companion to <see cref="CleanCompileCheckService"/> — the second diagnostic harvest inside
    /// <c>ValidateInternalAsync</c> would otherwise re-introduce the fixture's unrestored-package
    /// errors and force an <c>analyzer-error</c> verdict.
    /// </summary>
    private sealed class CleanDiagnosticService : IDiagnosticService
    {
        public Task<DiagnosticsResultDto> GetDiagnosticsAsync(
            string workspaceId,
            string? projectFilter,
            string? fileFilter,
            string? severityFilter,
            string? diagnosticIdFilter,
            CancellationToken ct) =>
            Task.FromResult(new DiagnosticsResultDto(
                WorkspaceDiagnostics: Array.Empty<DiagnosticDto>(),
                CompilerDiagnostics: Array.Empty<DiagnosticDto>(),
                AnalyzerDiagnostics: Array.Empty<DiagnosticDto>(),
                TotalErrors: 0,
                TotalWarnings: 0,
                TotalInfo: 0));

        public Task<DiagnosticDetailsDto?> GetDiagnosticDetailsAsync(
            string workspaceId,
            string diagnosticId,
            string filePath,
            int line,
            int column,
            CancellationToken ct) =>
            throw new NotSupportedException("Diagnostic details are not exercised by the validation bundle.");
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

    private sealed class ThrowingWorkspaceValidationService : IWorkspaceValidationService
    {
        public Task<WorkspaceValidationDto> ValidateAsync(
            string workspaceId,
            IReadOnlyList<string>? changedFilePaths,
            bool runTests,
            CancellationToken ct,
            bool summary = false) =>
            throw new NotSupportedException("The direct validation path is not used by this test.");

        public Task<WorkspaceValidationDto> ValidateRecentGitChangesAsync(
            string workspaceId,
            bool runTests,
            CancellationToken ct,
            bool summary = false) =>
            throw new InvalidOperationException("injected validation failure");
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

        Assert.AreNotEqual(default, entry,
            "A git kill failure must be observable at Warning level with the process id; "
            + $"got [{string.Join("; ", logger.Entries.Select(e => $"{e.Level}:{e.Message}"))}].");
    }

}
