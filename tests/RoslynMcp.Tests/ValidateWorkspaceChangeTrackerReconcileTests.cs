using System.Diagnostics;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Support;

namespace RoslynMcp.Tests;

/// <summary>
/// validate-workspace-changetracker-no-disk-reconcile-after-git-checkout (gh #738):
/// when <c>ValidateAsync</c> is called with <c>changedFilePaths=null</c> the service
/// falls back to <see cref="ChangeTracker"/>'s append-only log. Pre-fix that log
/// included files that had been reverted out-of-band (<c>git checkout -- foo.cs</c>)
/// because the tracker has no mechanism to drop reverted entries. The reconcile path
/// intersects the tracker list against <c>git status --porcelain</c> so reverted
/// files no longer leak into <c>ChangedFilePaths</c>; non-git directories preserve
/// the existing unfiltered behavior.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class ValidateWorkspaceChangeTrackerReconcileTests : IsolatedWorkspaceTestBase
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
    // Repro for gh #738: a file mutated through the tracker is then reverted
    // on disk (simulating `git checkout -- foo.cs`); the validate-workspace
    // call with changedFilePaths=null must no longer include that reverted
    // file in ChangedFilePaths.
    // ------------------------------------------------------------------
    [TestMethod]
    public async Task ValidateAsync_NullChangedFilePaths_RevertedFile_ExcludedFromScope()
    {
        if (!IsGitAvailable())
        {
            Assert.Inconclusive("git not on PATH — cannot run the reconcile path test.");
            return;
        }

        await using var workspace = CreateIsolatedWorkspaceCopy();
        InitializeGitRepo(workspace.RootPath);
        GitFixtureRunner.StageAndCommitAll(workspace.RootPath);

        await workspace.LoadAsync(CancellationToken.None);
        var wsId = workspace.WorkspaceId;

        ChangeTracker.Clear(wsId);

        // Record the file in the tracker via a real apply, then revert the disk
        // contents back to HEAD so git considers it clean. Pre-fix the tracker
        // entry leaked through and the file showed up in ChangedFilePaths.
        var dogPath = workspace.GetPath("SampleLib", "Dog.cs");
        var originalContent = await File.ReadAllTextAsync(dogPath);

        var edit = new TextEditDto(1, 1, 1, 1, "// reconcile-marker\n");
        var editResult = await EditService.ApplyTextEditsAsync(
            wsId, dogPath, [edit], "apply_text_edit", CancellationToken.None);
        Assert.IsTrue(editResult.Success, "Sanity: the seed edit must apply.");

        // Confirm the tracker has the entry (this is the pre-fix bug surface).
        var tracked = ChangeTracker.GetChanges(wsId);
        Assert.AreEqual(1, tracked.Count,
            "Sanity: the tracker should hold exactly one entry after the seed edit.");

        // Revert on disk — this is the out-of-band operation the bug never reconciled.
        // The tracker still holds the original entry; only the disk content matches HEAD.
        await File.WriteAllTextAsync(dogPath, originalContent);

        // Sanity: git agrees the file is now clean.
        Assert.AreEqual("", RunGitCapture(workspace.RootPath, "status", "--porcelain", "SampleLib/Dog.cs").Trim(),
            "Sanity: after the revert, git status must report the file clean.");

        // Call ValidateAsync with changedFilePaths=null. Post-fix the reverted file
        // must NOT appear in ChangedFilePaths because git reports it clean.
        var result = await _validationService.ValidateAsync(
            wsId, changedFilePaths: null, runTests: false, CancellationToken.None);

        var normalizedScope = result.ChangedFilePaths
            .Select(p => Path.GetFullPath(p).Replace('\\', '/').ToLowerInvariant())
            .ToList();
        var normalizedDog = Path.GetFullPath(dogPath).Replace('\\', '/').ToLowerInvariant();

        CollectionAssert.DoesNotContain(normalizedScope, normalizedDog,
            $"Reverted file must be excluded from validation scope; got [{string.Join("; ", result.ChangedFilePaths)}].");
    }

    // ------------------------------------------------------------------
    // Negative space: a file that is still dirty on disk must remain in the
    // validation scope. The reconcile must not suppress legitimately-dirty
    // tracker entries.
    // ------------------------------------------------------------------
    [TestMethod]
    public async Task ValidateAsync_NullChangedFilePaths_StillDirtyFile_KeptInScope()
    {
        if (!IsGitAvailable())
        {
            Assert.Inconclusive("git not on PATH — cannot run the still-dirty test.");
            return;
        }

        await using var workspace = CreateIsolatedWorkspaceCopy();
        InitializeGitRepo(workspace.RootPath);
        GitFixtureRunner.StageAndCommitAll(workspace.RootPath);

        await workspace.LoadAsync(CancellationToken.None);
        var wsId = workspace.WorkspaceId;

        ChangeTracker.Clear(wsId);

        var dogPath = workspace.GetPath("SampleLib", "Dog.cs");
        var edit = new TextEditDto(1, 1, 1, 1, "// still-dirty-marker\n");
        var editResult = await EditService.ApplyTextEditsAsync(
            wsId, dogPath, [edit], "apply_text_edit", CancellationToken.None);
        Assert.IsTrue(editResult.Success, "Sanity: the seed edit must apply.");

        // Do NOT revert — file remains dirty. git status will report it Modified.
        var statusOutput = RunGitCapture(workspace.RootPath, "status", "--porcelain", "SampleLib/Dog.cs").Trim();
        Assert.IsFalse(string.IsNullOrEmpty(statusOutput),
            $"Sanity: git should report the edited file dirty; got [{statusOutput}].");

        var result = await _validationService.ValidateAsync(
            wsId, changedFilePaths: null, runTests: false, CancellationToken.None);

        var normalizedScope = result.ChangedFilePaths
            .Select(p => Path.GetFullPath(p).Replace('\\', '/').ToLowerInvariant())
            .ToList();
        var normalizedDog = Path.GetFullPath(dogPath).Replace('\\', '/').ToLowerInvariant();

        CollectionAssert.Contains(normalizedScope, normalizedDog,
            $"A still-dirty file must remain in validation scope; got [{string.Join("; ", result.ChangedFilePaths)}].");
    }

    // ------------------------------------------------------------------
    // Negative space: in a non-git directory the reconcile path degrades
    // gracefully — the unfiltered tracker list is used. This preserves
    // the existing ValidateAsync_NullChangedFilePaths_NoUnknownSurfaced
    // behavior covered in ValidateWorkspaceSummaryTests.
    // ------------------------------------------------------------------
    [TestMethod]
    public async Task ValidateAsync_NullChangedFilePaths_NoGitRepo_FallsBackToUnfilteredTracker()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        // Explicitly DO NOT git init. The isolated workspace defaults to a
        // non-git temp dir, but be belt-and-suspenders against ancestor repos.
        RemoveAnyAncestorGitDir(workspace.RootPath);

        await workspace.LoadAsync(CancellationToken.None);
        var wsId = workspace.WorkspaceId;

        ChangeTracker.Clear(wsId);

        var dogPath = workspace.GetPath("SampleLib", "Dog.cs");
        var edit = new TextEditDto(1, 1, 1, 1, "// no-git-marker\n");
        var editResult = await EditService.ApplyTextEditsAsync(
            wsId, dogPath, [edit], "apply_text_edit", CancellationToken.None);
        Assert.IsTrue(editResult.Success, "Sanity: the seed edit must apply.");

        var result = await _validationService.ValidateAsync(
            wsId, changedFilePaths: null, runTests: false, CancellationToken.None);

        // Without git, the reconcile branch is bypassed and the unfiltered tracker
        // list is used. The edited file must surface in ChangedFilePaths.
        var normalizedScope = result.ChangedFilePaths
            .Select(p => Path.GetFullPath(p).Replace('\\', '/').ToLowerInvariant())
            .ToList();
        var normalizedDog = Path.GetFullPath(dogPath).Replace('\\', '/').ToLowerInvariant();

        CollectionAssert.Contains(normalizedScope, normalizedDog,
            "Without git the tracker list is unfiltered; the edited file must appear in scope. "
            + $"Got [{string.Join("; ", result.ChangedFilePaths)}].");
    }

    // ------------------------------------------------------------------
    // Caller-supplied scope: the reconcile MUST NOT fire when changedFilePaths
    // is non-null. The caller passed an explicit scope; silently filtering
    // those entries would lie about what was validated.
    // ------------------------------------------------------------------
    [TestMethod]
    public async Task ValidateAsync_NonNullChangedFilePaths_ReconcileDoesNotFire()
    {
        if (!IsGitAvailable())
        {
            Assert.Inconclusive("git not on PATH — cannot run the caller-scope guard test.");
            return;
        }

        await using var workspace = CreateIsolatedWorkspaceCopy();
        InitializeGitRepo(workspace.RootPath);
        GitFixtureRunner.StageAndCommitAll(workspace.RootPath);

        await workspace.LoadAsync(CancellationToken.None);
        var wsId = workspace.WorkspaceId;

        ChangeTracker.Clear(wsId);

        // The file is clean on disk (we did not edit it), but the caller passes
        // it in changedFilePaths anyway. The reconcile path is the wrong layer
        // to drop it — the caller asked for that scope explicitly.
        var dogPath = workspace.GetPath("SampleLib", "Dog.cs");

        var result = await _validationService.ValidateAsync(
            wsId, changedFilePaths: [dogPath], runTests: false, CancellationToken.None);

        // The caller-supplied path lands in ChangedFilePaths via the standard
        // workspace-membership partition; reconcile is bypassed.
        var normalizedScope = result.ChangedFilePaths
            .Select(p => Path.GetFullPath(p).Replace('\\', '/').ToLowerInvariant())
            .ToList();
        var normalizedDog = Path.GetFullPath(dogPath).Replace('\\', '/').ToLowerInvariant();

        CollectionAssert.Contains(normalizedScope, normalizedDog,
            "Caller-supplied scope must not be filtered by the reconcile path. "
            + $"Got [{string.Join("; ", result.ChangedFilePaths)}].");
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
        // `-b main` forces an initial branch name — mirrors the helper in
        // ValidateRecentGitChangesTests; see comments there for rationale.
        GitFixtureRunner.RunGit(directory, "init", "-q", "-b", "main");
        if (!Directory.Exists(Path.Combine(directory, ".git")))
        {
            throw new InvalidOperationException(
                $"git init appeared to succeed but '.git' is missing in '{directory}'.");
        }
        File.WriteAllText(Path.Combine(directory, ".gitignore"), "bin/\nobj/\n");
        GitFixtureRunner.RunGit(directory, "config", "--local", "user.email", "ci@example.invalid");
        GitFixtureRunner.RunGit(directory, "config", "--local", "user.name", "CI");
        GitFixtureRunner.RunGit(directory, "config", "--local", "commit.gpgsign", "false");
        GitFixtureRunner.RunGit(directory, "config", "--local", "core.autocrlf", "false");
    }

    /// <summary>
    /// Capturing variant of <see cref="GitFixtureRunner.RunGit"/> — used for inspecting the porcelain
    /// output during sanity assertions ("is this file actually clean / dirty?").
    /// </summary>
    private static string RunGitCapture(string workingDirectory, params string[] arguments)
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
        if (!process.WaitForExit(30_000))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best effort. The timeout failure below is the actionable signal.
            }

            throw new TimeoutException($"git {string.Join(' ', arguments)} did not exit within 30 seconds.");
        }
        var stdout = process.StandardOutput.ReadToEnd();
        if (process.ExitCode != 0)
        {
            var stderr = process.StandardError.ReadToEnd();
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} exited {process.ExitCode}. stdout=[{stdout}] stderr=[{stderr}]");
        }
        return stdout;
    }

    /// <summary>
    /// Walks upward from <paramref name="directory"/> removing any <c>.git</c> entry we
    /// find so we can prove the "not inside a git repo" fallback. Same belt-and-braces
    /// strategy as <c>ValidateRecentGitChangesTests.RemoveAnyAncestorGitDir</c>.
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
                // Best-effort: the primary repo's `.git` is locked by the running test
                // host; the nested sample-solution-copy directory we care about sits
                // under %TEMP% and never contains a `.git` so this loop is purely
                // belt-and-braces.
                break;
            }
            current = current.Parent;
        }
    }
}
