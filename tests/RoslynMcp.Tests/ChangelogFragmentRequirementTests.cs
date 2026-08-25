using System.Diagnostics;
using RoslynMcp.Tests.Support;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ChangelogFragmentRequirementTests
{
    private static readonly string[] _releaseVersionPaths =
    {
        "Directory.Build.props",
        ".claude-plugin/plugin.json",
        ".claude-plugin/marketplace.json",
        "manifest.json",
        ".claude-plugin/mcp.json",
        ".claude-plugin/server.json",
        "CHANGELOG.md",
    };

    [TestMethod]
    public void CiWorkflow_FetchesBaseHistoryAndRunsGateOnPolicyDocsShards()
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(repositoryRoot, ".github", "workflows", "ci.yml"));
        var checkoutIndex = workflow.IndexOf("- name: Check out repository", StringComparison.Ordinal);
        var historyIndex = workflow.IndexOf("fetch-depth: 0", checkoutIndex, StringComparison.Ordinal);
        var changelogStepIndex = workflow.IndexOf(
            "- name: Verify changelog contract",
            historyIndex,
            StringComparison.Ordinal);
        var changelogOwnerConditionIndex = workflow.IndexOf(
            "if: matrix.leg.artifact_owner == true",
            changelogStepIndex,
            StringComparison.Ordinal);
        var changelogIndex = workflow.IndexOf(
            "run: ./eng/verify-changelog-fragments.ps1",
            changelogOwnerConditionIndex,
            StringComparison.Ordinal);
        var docsOnlyOutputIndex = workflow.IndexOf(
            "docs_only: ${{ steps.decide.outputs.docs_only }}",
            StringComparison.Ordinal);
        var docsLegIndex = workflow.IndexOf("New-Leg -Name 'docs-linux-1-of-2'", StringComparison.Ordinal);

        Assert.IsTrue(checkoutIndex >= 0, "CI must check out the repository.");
        Assert.IsTrue(historyIndex > checkoutIndex, "Changelog comparison requires full base history.");
        Assert.IsTrue(changelogStepIndex > historyIndex, "CI must declare the changelog verifier after checkout.");
        Assert.IsTrue(changelogOwnerConditionIndex > changelogStepIndex);
        Assert.IsTrue(changelogIndex > historyIndex, "CI must run the changelog verifier after checkout.");
        Assert.IsTrue(docsOnlyOutputIndex >= 0 && docsLegIndex > docsOnlyOutputIndex,
            "The router must expose docs-only state and emit a dedicated hosted docs leg.");
        Assert.IsTrue(
            changelogOwnerConditionIndex < changelogIndex,
            "The changelog verifier must run on the sole artifact-owning leg, including docs-only PRs.");

        var publishWorkflow = File.ReadAllText(
            Path.Combine(repositoryRoot, ".github", "workflows", "publish-nuget.yml"));
        var publishCheckoutIndex = publishWorkflow.IndexOf("- name: Check out repository", StringComparison.Ordinal);
        var publishHistoryIndex = publishWorkflow.IndexOf(
            "fetch-depth: 0",
            publishCheckoutIndex,
            StringComparison.Ordinal);
        var publishReleaseGateIndex = publishWorkflow.IndexOf(
            "./eng/verify-release.ps1",
            publishHistoryIndex,
            StringComparison.Ordinal);
        Assert.IsTrue(
            publishHistoryIndex > publishCheckoutIndex && publishReleaseGateIndex > publishHistoryIndex,
            "The NuGet publication job must fetch base history before release validation.");
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task ChangeRequirement_CoversGitStatesAndStrictReleaseAssembly()
    {
        RequireGit();
        var cases = new[]
        {
            new RequirementCase("clean", RequirementFixtureKind.Clean, 0, "fragment verification passed"),
            new RequirementCase("unstaged source", RequirementFixtureKind.UnstagedSource, 1, "Change-bearing work requires"),
            new RequirementCase("staged source", RequirementFixtureKind.StagedSource, 1, "Change-bearing work requires"),
            new RequirementCase("untracked source", RequirementFixtureKind.UntrackedSource, 1, "Change-bearing work requires"),
            new RequirementCase("committed branch source", RequirementFixtureKind.CommittedBranchSource, 1, "Change-bearing work requires"),
            new RequirementCase("missing base", RequirementFixtureKind.MissingBase, 1, "could not resolve the target branch"),
            new RequirementCase("planning only", RequirementFixtureKind.PlanningOnly, 0, "fragment verification passed"),
            new RequirementCase("changed fragment", RequirementFixtureKind.ChangedFragment, 0, "1 changed"),
            new RequirementCase("inherited fragment", RequirementFixtureKind.InheritedFragment, 1, "Change-bearing work requires"),
            new RequirementCase("assembled release", RequirementFixtureKind.AssembledRelease, 0, "strict assembled release"),
            new RequirementCase("release plus source", RequirementFixtureKind.ReleasePlusSource, 1, "Change-bearing work requires"),
        };

        foreach (var testCase in cases)
        {
            var fixtureRoot = CreateFixture(testCase.Kind);
            try
            {
                var result = await RunVerifierAsync(fixtureRoot);
                var allOutput = PowerShellOutputNormalizer.Normalize(
                    result.StdOut + Environment.NewLine + result.StdErr);
                var diagnostic = $"{testCase.Name}: output={allOutput}";
                Assert.AreEqual(testCase.ExpectedExitCode, result.ExitCode, diagnostic);
                StringAssert.Contains(allOutput, testCase.ExpectedText, diagnostic);
            }
            finally
            {
                TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
            }
        }
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task FragmentGrammar_RequiresCanonicalFilenameAndCategoryBodyParity()
    {
        RequireGit();
        var cases = new List<GrammarCase>();
        cases.AddRange(new[] { "Fixed", "Changed", "Changed — BREAKING", "Added", "Maintenance" }
            .Select(category => new GrammarCase(
                $"valid {category}",
                "fixture-entry.md",
                Fragment(category, $"- **{category}:** Fixture change."),
                0,
                "fragment verification passed")));
        cases.AddRange(new[]
        {
            new GrammarCase(
                "mismatched category",
                "fixture-entry.md",
                Fragment("Fixed", "- **Added:** Fixture change."),
                1,
                "first body line must begin"),
            new GrammarCase(
                "non-bullet body",
                "fixture-entry.md",
                Fragment("Fixed", "Fixture change."),
                1,
                "first body line must begin"),
            new GrammarCase(
                "duplicate category",
                "fixture-entry.md",
                Fragment("Fixed", "- **Fixed:** **Fixed:** Fixture change."),
                1,
                "repeats a leading category"),
            new GrammarCase(
                "multiple body bullets",
                "fixture-entry.md",
                Fragment("Maintenance", "- **Maintenance:** Fixture servicing.\n- **Fixed:** Unrelated correction."),
                1,
                "exactly one nonblank bullet line"),
            new GrammarCase(
                "empty body",
                "fixture-entry.md",
                "---\ncategory: Fixed\n---\n",
                1,
                "no body content"),
            new GrammarCase(
                "duplicate category key",
                "fixture-entry.md",
                "---\ncategory: Fixed\ncategory: Added\n---\n\n- **Fixed:** Fixture change.\n",
                1,
                "duplicate 'category' keys"),
            new GrammarCase(
                "invalid filename",
                "Fixture Entry.md",
                Fragment("Fixed", "- **Fixed:** Fixture change."),
                1,
                "lowercase kebab-case"),
        });

        foreach (var testCase in cases)
        {
            var fixtureRoot = CreateBaselineFixture(includeInheritedFragment: false);
            try
            {
                File.WriteAllText(
                    Path.Combine(fixtureRoot, "changelog.d", testCase.FileName),
                    testCase.Contents);
                var result = await RunVerifierAsync(fixtureRoot);
                var allOutput = PowerShellOutputNormalizer.Normalize(
                    result.StdOut + Environment.NewLine + result.StdErr);
                var diagnostic = $"{testCase.Name}: output={allOutput}";
                Assert.AreEqual(testCase.ExpectedExitCode, result.ExitCode, diagnostic);
                StringAssert.Contains(allOutput, testCase.ExpectedText, diagnostic);
            }
            finally
            {
                TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
            }
        }
    }

    private static string CreateFixture(RequirementFixtureKind kind)
    {
        var includeInheritedFragment = kind is RequirementFixtureKind.InheritedFragment
            or RequirementFixtureKind.AssembledRelease
            or RequirementFixtureKind.ReleasePlusSource;
        var root = CreateBaselineFixture(includeInheritedFragment);

        switch (kind)
        {
            case RequirementFixtureKind.Clean:
                break;
            case RequirementFixtureKind.UnstagedSource:
                File.AppendAllText(Path.Combine(root, "src", "fixture.txt"), "unstaged\n");
                break;
            case RequirementFixtureKind.StagedSource:
                File.AppendAllText(Path.Combine(root, "src", "fixture.txt"), "staged\n");
                GitFixtureRunner.RunGit(root, "add", "src/fixture.txt");
                break;
            case RequirementFixtureKind.UntrackedSource:
                File.WriteAllText(Path.Combine(root, "src", "untracked.txt"), "untracked\n");
                break;
            case RequirementFixtureKind.CommittedBranchSource:
                GitFixtureRunner.RunGit(root, "checkout", "-q", "-b", "fixture-change");
                File.AppendAllText(Path.Combine(root, "src", "fixture.txt"), "committed\n");
                GitFixtureRunner.RunGit(root, "add", "src/fixture.txt");
                Commit(root, "fixture source change");
                break;
            case RequirementFixtureKind.MissingBase:
                GitFixtureRunner.RunGit(root, "checkout", "-q", "-b", "fixture-no-base");
                GitFixtureRunner.RunGit(root, "branch", "-D", "main");
                File.AppendAllText(Path.Combine(root, "src", "fixture.txt"), "missing base\n");
                break;
            case RequirementFixtureKind.PlanningOnly:
                File.WriteAllText(Path.Combine(root, "ai_docs", "note.md"), "planning\n");
                break;
            case RequirementFixtureKind.ChangedFragment:
                File.AppendAllText(Path.Combine(root, "src", "fixture.txt"), "changed\n");
                File.WriteAllText(
                    Path.Combine(root, "changelog.d", "fixture-change.md"),
                    Fragment("Fixed", "- **Fixed:** Fixture source behavior changed."));
                break;
            case RequirementFixtureKind.InheritedFragment:
                File.AppendAllText(Path.Combine(root, "src", "fixture.txt"), "changed\n");
                break;
            case RequirementFixtureKind.AssembledRelease:
                PrepareRelease(root, includeSourceChange: false);
                break;
            case RequirementFixtureKind.ReleasePlusSource:
                PrepareRelease(root, includeSourceChange: true);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }

        return root;
    }

    private static string CreateBaselineFixture(bool includeInheritedFragment)
    {
        var root = Path.Combine(
            TestTempRoot.Current,
            nameof(ChangelogFragmentRequirementTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "eng"));
        Directory.CreateDirectory(Path.Combine(root, "changelog.d"));
        Directory.CreateDirectory(Path.Combine(root, "src"));
        Directory.CreateDirectory(Path.Combine(root, "ai_docs"));
        Directory.CreateDirectory(Path.Combine(root, ".claude-plugin"));

        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        File.Copy(
            Path.Combine(repositoryRoot, "eng", "verify-changelog-fragments.ps1"),
            Path.Combine(root, "eng", "verify-changelog-fragments.ps1"));
        File.WriteAllText(Path.Combine(root, "changelog.d", "README.md"), "# Fixture fragments\n");
        File.WriteAllText(Path.Combine(root, "src", "fixture.txt"), "baseline\n");
        foreach (var relativePath in _releaseVersionPaths)
        {
            var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, $"baseline {relativePath}\n");
        }

        if (includeInheritedFragment)
        {
            File.WriteAllText(
                Path.Combine(root, "changelog.d", "inherited-change.md"),
                Fragment("Changed", "- **Changed:** Inherited fixture change."));
        }

        GitFixtureRunner.RunGit(root, "init", "-q", "-b", "main");
        GitFixtureRunner.RunGit(root, "add", "-A");
        Commit(root, "fixture baseline");
        return root;
    }

    private static void PrepareRelease(string root, bool includeSourceChange)
    {
        File.Delete(Path.Combine(root, "changelog.d", "inherited-change.md"));
        foreach (var relativePath in _releaseVersionPaths)
        {
            var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            File.AppendAllText(fullPath, "release\n");
        }

        if (includeSourceChange)
        {
            File.AppendAllText(Path.Combine(root, "src", "fixture.txt"), "release source hitchhiker\n");
        }
    }

    private static void Commit(string root, string message)
        => GitFixtureRunner.RunGit(
            root,
            "-c",
            "user.email=fixture@roslynmcp.invalid",
            "-c",
            "user.name=Changelog Fixture",
            "commit",
            "-q",
            "-m",
            message);

    private static string Fragment(string category, string body)
        => $"---\ncategory: {category}\n---\n\n{body}\n";

    private static async Task<ProcessResult> RunVerifierAsync(string fixtureRoot)
    {
        var scriptPath = Path.Combine(fixtureRoot, "eng", "verify-changelog-fragments.ps1");
        var startInfo = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("-RepoRoot");
        startInfo.ArgumentList.Add(fixtureRoot);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the changelog verifier.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("Changelog verifier did not exit within 30 seconds.");
        }

        return new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static void RequireGit()
    {
        if (!GitFixtureRunner.IsAvailable(out var failureReason))
            Assert.Inconclusive($"git is required for changelog requirement fixtures: {failureReason}");
    }

    private enum RequirementFixtureKind
    {
        Clean,
        UnstagedSource,
        StagedSource,
        UntrackedSource,
        CommittedBranchSource,
        MissingBase,
        PlanningOnly,
        ChangedFragment,
        InheritedFragment,
        AssembledRelease,
        ReleasePlusSource,
    }

    private sealed record RequirementCase(
        string Name,
        RequirementFixtureKind Kind,
        int ExpectedExitCode,
        string ExpectedText);

    private sealed record GrammarCase(
        string Name,
        string FileName,
        string Contents,
        int ExpectedExitCode,
        string ExpectedText);

    private sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);
}
