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
        GitFixtureRunner.InitializeRepository(workspace.RootPath);
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
    [DataRow("git", true)]
    [DataRow("explicit", true)]
    [DataRow("tracker", true)]
    [DataRow("git", false)]
    [DataRow("explicit", false)]
    [DataRow("tracker", false)]
    public async Task Validation_PathIdentity_PreservesExactScope(string source, bool caseDistinct)
    {
        if (!IsGitAvailable())
            Assert.Inconclusive($"git unavailable: {_gitUnavailableReason}");

        await using var workspace = CreateIsolatedWorkspaceCopy();
        var upper = workspace.GetPath("CaseProbe.cs");
        var lowerName = caseDistinct ? "caseprobe.cs" : "OtherProbe.cs";
        var lower = workspace.GetPath(lowerName);
        await File.WriteAllTextAsync(upper, "// upper baseline\n");
        if (File.Exists(lower))
            Assert.Inconclusive("This fixture filesystem does not support case-distinct paths.");
        await File.WriteAllTextAsync(lower, "// lower baseline\n");
        // MSBuild collapses case-only duplicate Compile items within one project. Use two
        // explicit projects so the workspace contains both distinct document identities.
        foreach (var (projectName, fileName) in new[] { ("Upper", "CaseProbe.cs"), ("Lower", lowerName) })
        {
            var projectDirectory = workspace.GetPath(projectName);
            Directory.CreateDirectory(projectDirectory);
            await File.WriteAllTextAsync(Path.Combine(projectDirectory, projectName + ".csproj"), $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
                  </PropertyGroup>
                  <ItemGroup><Compile Include="../{fileName}" /></ItemGroup>
                </Project>
                """);
        }
        await File.WriteAllTextAsync(workspace.SolutionPath, """
            <Solution>
              <Project Path="Upper/Upper.csproj" />
              <Project Path="Lower/Lower.csproj" />
            </Solution>
            """);
        GitFixtureRunner.InitializeRepository(workspace.RootPath);
        GitFixtureRunner.RunGit(workspace.RootPath, "add", "--", "CaseProbe.cs", lowerName, "Upper", "Lower");
        GitFixtureRunner.StageAndCommitAll(workspace.RootPath);
        await workspace.LoadAsync(CancellationToken.None);
        var documentPaths = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId)
            .Projects.SelectMany(project => project.Documents).Select(document => document.FilePath).ToArray();
        CollectionAssert.Contains(documentPaths, upper, "The fixture must load the upper-case document.");
        CollectionAssert.Contains(documentPaths, lower, "The fixture must load the lower-case document.");
        await File.AppendAllTextAsync(upper, "// upper changed\n");
        await File.AppendAllTextAsync(lower, "// lower changed\n");

        var wrongCase = workspace.GetPath(caseDistinct ? "CASEPROBE.cs" : "MissingProbe.cs");
        ChangeTracker.RecordChange(workspace.WorkspaceId, "case-distinct edits",
            [upper, lower], "test");
        var result = source switch
        {
            "git" => await _validationService.ValidateRecentGitChangesAsync(
                workspace.WorkspaceId, runTests: false, CancellationToken.None),
            "explicit" => await _validationService.ValidateAsync(
                workspace.WorkspaceId, [upper, lower, upper, wrongCase], runTests: false, CancellationToken.None),
            _ => await _validationService.ValidateAsync(
                workspace.WorkspaceId, changedFilePaths: null, runTests: false, CancellationToken.None),
        };

        CollectionAssert.AreEquivalent(new[] { upper, lower }, result.ChangedFilePaths.ToArray());
        CollectionAssert.AreEquivalent(source == "explicit" ? new[] { wrongCase } : Array.Empty<string>(),
            result.UnknownFilePaths.ToArray());
        Assert.IsEmpty(result.Warnings, string.Join("; ", result.Warnings));

        if (source == "tracker")
        {
            await File.WriteAllTextAsync(upper, "// upper baseline\n");
            var reconciled = await _validationService.ValidateAsync(
                workspace.WorkspaceId, changedFilePaths: null, runTests: false, CancellationToken.None);
            CollectionAssert.AreEqual(new[] { lower }, reconciled.ChangedFilePaths.ToArray(),
                "A dirty case-distinct sibling must not retain the reverted tracker path.");
        }
    }

    [TestMethod]
    public async Task ValidateAsync_WindowsCaseAliases_PreserveExistingDeduplication()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("Windows path-comparison control.");

        await using var workspace = CreateIsolatedWorkspaceCopy();
        await workspace.LoadAsync(CancellationToken.None);
        var path = workspace.GetPath("SampleLib", "AnimalService.cs");
        var alias = workspace.GetPath("SampleLib", "ANIMALSERVICE.CS");
        var result = await _validationService.ValidateAsync(
            workspace.WorkspaceId, [alias, path], runTests: false, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { alias }, result.ChangedFilePaths.ToArray());
        Assert.IsEmpty(result.UnknownFilePaths);
    }

    [TestMethod]
    [DataRow("ordinary")]
    [DataRow("case-distinct")]
    [DataRow("literal-backslash")]
    public async Task GitScope_BuildOutputFilter_UsesPlatformPathIdentity(string shape)
    {
        if (!IsGitAvailable())
            Assert.Inconclusive($"git unavailable: {_gitUnavailableReason}");
        if (shape == "literal-backslash" && OperatingSystem.IsWindows())
            Assert.Inconclusive("Windows does not support literal backslashes in file names.");

        await using var workspace = CreateIsolatedWorkspaceCopy();
        GitFixtureRunner.InitializeRepository(workspace.RootPath);
        GitFixtureRunner.StageAndCommitAll(workspace.RootPath);
        await workspace.LoadAsync(CancellationToken.None);

        string[] excluded = ["bin/Generated.cs", "obj/Generated.cs", "nested/bin/Generated.cs", "nested/obj/Generated.cs"];
        string[] retained = shape switch
        {
            "case-distinct" => ["Bin/Source.cs", "Obj/Source.cs", "nested/Bin/Source.cs", "nested/Obj/Source.cs"],
            "literal-backslash" => ["bin\\Source.cs", "obj\\Source.cs", "nested\\bin/Source.cs", "nested\\obj/Source.cs"],
            _ => ["binary/Source.cs", "objects/Source.cs", "nested/bin.cs", "nested/obj.cs"],
        };
        foreach (var relative in excluded)
        {
            var absolute = workspace.GetPath(relative);
            Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
            await File.WriteAllTextAsync(absolute, "// generated output\n");
        }
        if (shape == "case-distinct" && Directory.Exists(workspace.GetPath("Bin")))
            Assert.Inconclusive("This fixture filesystem does not support case-distinct directories.");

        foreach (var relative in retained)
        {
            var absolute = workspace.GetPath(relative);
            Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
            await File.WriteAllTextAsync(absolute, "// source outside the loaded projects\n");
        }
        // Force-stage outputs so the filter is exercised even with the fixture's .gitignore.
        foreach (var relative in excluded.Concat(retained))
            GitFixtureRunner.RunGit(workspace.RootPath, "add", "-f", "--", relative);
        var known = workspace.GetPath("SampleLib", "Dog.cs");
        await File.AppendAllTextAsync(known, "\n// known dirty source\n");

        var result = await _validationService.ValidateRecentGitChangesAsync(
            workspace.WorkspaceId, runTests: false, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { known }, result.ChangedFilePaths.ToArray());
        CollectionAssert.AreEquivalent(retained.Select(relative => Path.GetFullPath(workspace.GetPath(relative))).ToArray(),
            result.UnknownFilePaths.ToArray(), "Legitimate source paths must survive Git scope filtering.");
        Assert.IsEmpty(result.Warnings);
    }

    [TestMethod]
    public async Task ValidateRecentGitChangesAsync_NestedSolution_UsesRepositoryRelativePaths()
    {
        if (!IsGitAvailable())
            Assert.Inconclusive($"git unavailable: {_gitUnavailableReason}");

        await using var workspace = CreateIsolatedWorkspaceCopy();
        var nestedDirectory = workspace.GetPath("nested");
        Directory.CreateDirectory(nestedDirectory);
        var nestedSolution = Path.Combine(nestedDirectory, "Nested.slnx");
        await File.WriteAllTextAsync(nestedSolution,
            "<Solution><Project Path=\"../SampleLib/SampleLib.csproj\" /></Solution>");
        GitFixtureRunner.InitializeRepository(workspace.RootPath);
        GitFixtureRunner.RunGit(workspace.RootPath, "add", "--", "nested/Nested.slnx");
        GitFixtureRunner.StageAndCommitAll(workspace.RootPath);

        var inside = Path.Combine(nestedDirectory, "Inside.cs");
        var outside = workspace.GetPath("SampleLib", "AnimalService.cs");
        await File.WriteAllTextAsync(inside, "// nested untracked source\n");
        await File.AppendAllTextAsync(outside, "\n// changed outside the solution directory\n");
        var status = await WorkspaceManager.LoadAsync(nestedSolution, CancellationToken.None);
        try
        {
            var result = await _validationService.ValidateRecentGitChangesAsync(
                status.WorkspaceId, runTests: false, CancellationToken.None);

            Assert.IsEmpty(result.Warnings, string.Join("; ", result.Warnings));
            CollectionAssert.AreEquivalent(new[] { outside }, result.ChangedFilePaths.ToArray());
            CollectionAssert.AreEquivalent(new[] { inside }, result.UnknownFilePaths.ToArray());
            Assert.IsTrue(result.ChangedFilePaths.Concat(result.UnknownFilePaths).All(File.Exists),
                "Both known and unknown Git paths must identify actual files.");
        }
        finally
        {
            WorkspaceManager.Close(status.WorkspaceId);
        }
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
        GitFixtureRunner.InitializeRepository(workspace.RootPath);
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
        GitFixtureRunner.InitializeRepository(workspace.RootPath);
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
    // is preserved. Hold the exit wait until its token is cancelled so the timeout
    // branch is exercised independently of real git speed and timer scheduling.
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
        GitFixtureRunner.InitializeRepository(workspace.RootPath);
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
        var exitWaitCancelled = false;
        var timeoutService = new WorkspaceValidationService(
            new CleanCompileCheckService(),
            new CleanDiagnosticService(),
            TestDiscoveryService,
            TestRunnerService,
            WorkspaceManager,
            ChangeTracker,
            gitStatusTimeout: TimeSpan.FromMilliseconds(25),
            validationPhaseTimeout: TimeSpan.FromMinutes(2),
            waitForGitExitAsync: async (_, token) =>
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
                finally
                {
                    exitWaitCancelled = token.IsCancellationRequested;
                }
            });

        var result = await timeoutService.ValidateRecentGitChangesAsync(
            workspace.WorkspaceId, runTests: false, CancellationToken.None);

        Assert.IsTrue(exitWaitCancelled, "The injected exit wait must observe the service timeout token.");
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
        Assert.AreEqual(1, control.ChangedFilePaths.Count, "The real exit wait must preserve the dirty file scope.");
        Assert.AreEqual(Path.GetFullPath(touchedFile), Path.GetFullPath(control.ChangedFilePaths[0]));
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
        GitFixtureRunner.InitializeRepository(workspace.RootPath);
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

    [TestMethod]
    [DataRow("cancelled")]
    [DataRow("timeout")]
    [DataRow("failure")]
    public async Task GitCollection_InterruptedProcessExitsAndBothReadersCompleteAsync(string interruption)
    {
        var fixtureRoot = Path.Combine(TestTempRoot.Current, "git-owned-process", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(fixtureRoot, ".git"));
        var scriptPath = Path.Combine(fixtureRoot, "blocked.ps1");
        await File.WriteAllTextAsync(scriptPath,
            "[Console]::Out.WriteLine('stdout-ready')\n" +
            "[Console]::Error.WriteLine('stderr-ready')\nStart-Sleep -Seconds 60\n");
        using var cancellation = new CancellationTokenSource();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readers = new List<Task<string>>();
        var readyCount = 0;
        Process? observedProcess = null;
        var reported = new List<Exception>();
        var primaryFailure = new IOException("primary-failure-sentinel");
        var collector = new GitChangedFilesCollector(
            (exception, _) =>
            {
                reported.Add(exception);
                return new WorkspaceValidationFailureDetail("Internal", "sanitized failure");
            },
            waitForGitExitAsync: async (process, token) =>
            {
                observedProcess = Process.GetProcessById(process.Id);
                // Timeout/cancellation cannot run until the real child has opened both pipes.
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
                if (interruption == "failure")
                    throw primaryFailure;
                if (interruption == "cancelled")
                    cancellation.Cancel();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            },
            configureStartInfo: startInfo =>
            {
                startInfo.FileName = OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh";
                startInfo.ArgumentList.Clear();
                foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", scriptPath })
                    startInfo.ArgumentList.Add(argument);
            },
            readOutputAsync: (reader, token) =>
            {
                async Task<string> ReadAsync()
                {
                    var first = await reader.ReadLineAsync(token);
                    Assert.IsTrue(first is "stdout-ready" or "stderr-ready");
                    if (Interlocked.Increment(ref readyCount) == 2)
                        ready.TrySetResult();
                    return first + await reader.ReadToEndAsync(token);
                }
                var task = ReadAsync();
                readers.Add(task);
                return task;
            });
        try
        {
            var run = collector.CollectAsync(fixtureRoot,
                interruption == "timeout" ? TimeSpan.FromSeconds(2) : TimeSpan.FromSeconds(30), cancellation.Token);
            if (interruption == "cancelled")
            {
                await Assert.ThrowsAsync<OperationCanceledException>(() => run);
            }
            else
            {
                var result = await run;
                Assert.AreEqual(interruption == "timeout", result.TimedOut);
                Assert.AreEqual(string.Empty, result.StdOut);
                Assert.HasCount(1, result.Warnings);
                if (interruption == "failure")
                    CollectionAssert.Contains(reported, primaryFailure);
            }
            Assert.IsNotNull(observedProcess);
            Assert.IsTrue(observedProcess.HasExited, "Cleanup must await process exit before returning.");
            Assert.HasCount(2, readers);
            Assert.IsTrue(readers.All(task => task.IsCompletedSuccessfully), "Both pipes must reach EOF before returning.");
        }
        finally
        {
            if (observedProcess is not null)
            {
                if (!observedProcess.HasExited)
                {
                    observedProcess.Kill(entireProcessTree: true);
                    await observedProcess.WaitForExitAsync();
                }
                observedProcess.Dispose();
            }
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
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
        var killException = new System.ComponentModel.Win32Exception("private-path-sentinel");

        var service = new WorkspaceValidationService(
            CompileCheckService,
            DiagnosticService,
            TestDiscoveryService,
            TestRunnerService,
            WorkspaceManager,
            ChangeTracker,
            gitStatusTimeout: TimeSpan.FromSeconds(5),
            validationPhaseTimeout: TimeSpan.FromSeconds(5),
            logger: logger);
        var collector = new GitChangedFilesCollector(
            service.CreateUnexpectedFailure, logger, killProcessTree: _ => throw killException);

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
            Arguments = OperatingSystem.IsWindows() ? "/c exit" : "-c \"exit 0\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        Assert.IsNotNull(process, "Could not start a host process for the test.");
        Assert.IsTrue(process.WaitForExit(5_000), "The probe process must exit.");

        // Must not throw — the kill failure is best-effort and swallowed for the caller.
        collector.TryKillProcessTree(process);

        var entry = logger.Entries.SingleOrDefault(candidate =>
            candidate.Level == LogLevel.Warning &&
            candidate.Message.Contains("Failed to kill git process tree", StringComparison.Ordinal) &&
            candidate.Message.Contains(process.Id.ToString(), StringComparison.Ordinal) &&
            candidate.Exception is null &&
            !candidate.Message.Contains("private-path-sentinel", StringComparison.Ordinal));

        Assert.AreNotEqual(default, entry,
            "A git kill failure must be observable at Warning level with the process id; "
            + $"got [{string.Join("; ", logger.Entries.Select(e => $"{e.Level}:{e.Message}"))}].");
    }

}
