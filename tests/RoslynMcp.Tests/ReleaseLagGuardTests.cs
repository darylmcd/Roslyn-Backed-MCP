using System.Diagnostics;
using RoslynMcp.Tests.Helpers;
using RoslynMcp.Tests.Support;

namespace RoslynMcp.Tests;

/// <summary>
/// Pins the advisory release-lag guard in <c>eng/verify-changelog-fragments.ps1</c>: a current
/// <c>category: Fixed</c> fragment unreleased longer than <c>CHANGELOG_FIXED_MAX_AGE_DAYS</c>
/// (default 7) warns without failing. Fragment age is the committer date of the commit that added it.
/// </summary>
[TestClass]
public sealed class ReleaseLagGuardTests
{
    private const string StaleWarningText = "Fixed fragment unreleased for";
    private const string FixedFragmentName = "fixed-fixture.md";

    [TestMethod]
    [TestCategory("Process")]
    public async Task FreshFixedFragment_DoesNotWarn()
    {
        RequireGit();
        var root = CreateFixture("Fixed", DateTimeOffset.UtcNow);
        try
        {
            var result = await RunVerifierAsync(root);

            Assert.AreEqual(0, result.ExitCode, result.AllOutput);
            Assert.IsFalse(result.AllOutput.Contains(StaleWarningText, StringComparison.Ordinal), result.AllOutput);
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(root);
        }
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task StaleFixedFragment_WarnsAndStillPasses()
    {
        RequireGit();
        var root = CreateFixture("Fixed", DateTimeOffset.UtcNow.AddDays(-30));
        try
        {
            var result = await RunVerifierAsync(root);

            Assert.AreEqual(0, result.ExitCode, result.AllOutput);
            StringAssert.Contains(result.AllOutput, $"changelog.d/{FixedFragmentName}");
            StringAssert.Contains(result.AllOutput, StaleWarningText);
            StringAssert.Contains(result.AllOutput, "threshold 7");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(root);
        }
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task StaleChangedFragment_DoesNotWarn()
    {
        RequireGit();
        var root = CreateFixture("Changed", DateTimeOffset.UtcNow.AddDays(-30));
        try
        {
            var result = await RunVerifierAsync(root);

            Assert.AreEqual(0, result.ExitCode, result.AllOutput);
            Assert.IsFalse(result.AllOutput.Contains(StaleWarningText, StringComparison.Ordinal), result.AllOutput);
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(root);
        }
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task ThresholdEnvVariable_OverridesDefaultAndInvalidValuesFallBack()
    {
        RequireGit();
        var root = CreateFixture("Fixed", DateTimeOffset.UtcNow.AddDays(-10));
        try
        {
            var raised = await RunVerifierAsync(root, "30");
            Assert.AreEqual(0, raised.ExitCode, raised.AllOutput);
            Assert.IsFalse(raised.AllOutput.Contains(StaleWarningText, StringComparison.Ordinal), raised.AllOutput);

            var lowered = await RunVerifierAsync(root, "3");
            Assert.AreEqual(0, lowered.ExitCode, lowered.AllOutput);
            StringAssert.Contains(lowered.AllOutput, "threshold 3");

            foreach (var invalid in new[] { "abc", "0", "-5" })
            {
                var fallback = await RunVerifierAsync(root, invalid);
                Assert.AreEqual(0, fallback.ExitCode, $"{invalid}: {fallback.AllOutput}");
                StringAssert.Contains(fallback.AllOutput, "threshold 7", $"{invalid}: {fallback.AllOutput}");
            }
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(root);
        }
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task ShallowClone_SkipsGuardWithoutWarningOrFailure()
    {
        RequireGit();
        var source = CreateFixture("Fixed", DateTimeOffset.UtcNow.AddDays(-30));
        var clone = Path.Combine(
            TestTempRoot.Current,
            nameof(ReleaseLagGuardTests),
            Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(clone)!);
            GitFixtureRunner.RunGit(
                Path.GetDirectoryName(clone)!,
                "clone",
                "-q",
                "--depth",
                "1",
                new Uri(source).AbsoluteUri,
                clone);
            Assert.AreEqual(
                "true",
                GitFixtureRunner.RunGitCapture(clone, "rev-parse", "--is-shallow-repository").Trim());

            var result = await RunVerifierAsync(clone);

            Assert.AreEqual(0, result.ExitCode, result.AllOutput);
            Assert.IsFalse(result.AllOutput.Contains("WARNING", StringComparison.OrdinalIgnoreCase), result.AllOutput);
            Assert.IsFalse(result.AllOutput.Contains(StaleWarningText, StringComparison.Ordinal), result.AllOutput);
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(clone);
            TestFixtureFileSystem.DeleteDirectoryIfExists(source);
        }
    }

    private static string CreateFixture(string category, DateTimeOffset committedAt)
    {
        var root = Path.Combine(
            TestTempRoot.Current,
            nameof(ReleaseLagGuardTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "eng"));
        Directory.CreateDirectory(Path.Combine(root, "changelog.d"));

        File.Copy(
            Path.Combine(TestFixtureFileSystem.FindRepositoryRoot(), "eng", "verify-changelog-fragments.ps1"),
            Path.Combine(root, "eng", "verify-changelog-fragments.ps1"));
        File.WriteAllText(Path.Combine(root, "changelog.d", "README.md"), "# Fixture fragments\n");
        File.WriteAllText(
            Path.Combine(root, "changelog.d", FixedFragmentName),
            $"---\ncategory: {category}\n---\n\n- **{category}:** Fixture fragment.\n");

        GitFixtureRunner.InitializeRepository(root);
        GitFixtureRunner.RunGit(root, "add", "-A");
        CommitAt(root, committedAt);
        return root;
    }

    // GitFixtureRunner.RunGit has no environment parameter, so the backdated commit uses a
    // test-local Process. GIT_COMMITTER_DATE is what the guard reads (%ct).
    private static void CommitAt(string root, DateTimeOffset committedAt)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("commit");
        startInfo.ArgumentList.Add("-q");
        startInfo.ArgumentList.Add("-m");
        startInfo.ArgumentList.Add("fixture baseline");
        startInfo.Environment["GIT_COMMITTER_DATE"] = committedAt.ToString("o");
        startInfo.Environment["GIT_AUTHOR_DATE"] = committedAt.ToString("o");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start git commit.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromSeconds(30)))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("git commit did not exit within 30 seconds.");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git commit exited {process.ExitCode}. stdout=[{stdoutTask.GetAwaiter().GetResult()}] " +
                $"stderr=[{stderrTask.GetAwaiter().GetResult()}]");
        }
    }

    private static Task<PwshScriptResult> RunVerifierAsync(string fixtureRoot, string? maxAgeDays = null)
        => PwshScriptRunner.RunAsync(
            new[]
            {
                "-NoProfile",
                "-File",
                Path.Combine(fixtureRoot, "eng", "verify-changelog-fragments.ps1"),
                "-RepoRoot",
                fixtureRoot,
            },
            environment: new Dictionary<string, string?>
            {
                ["CHANGELOG_FIXED_MAX_AGE_DAYS"] = maxAgeDays,
                ["GITHUB_ACTIONS"] = null,
            },
            timeout: TimeSpan.FromSeconds(30),
            description: "changelog verifier");

    private static void RequireGit()
    {
        if (!GitFixtureRunner.IsAvailable(out var failureReason))
            Assert.Inconclusive($"git is required for release-lag fixtures: {failureReason}");
    }
}
