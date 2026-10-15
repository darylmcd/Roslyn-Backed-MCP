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
        await GitFixtureRunner.RequireAvailableAsync();
        var root = await CreateFixtureAsync("Fixed", DateTimeOffset.UtcNow);
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
        await GitFixtureRunner.RequireAvailableAsync();
        var root = await CreateFixtureAsync("Fixed", DateTimeOffset.UtcNow.AddDays(-30));
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
        await GitFixtureRunner.RequireAvailableAsync();
        var root = await CreateFixtureAsync("Changed", DateTimeOffset.UtcNow.AddDays(-30));
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
        await GitFixtureRunner.RequireAvailableAsync();
        var root = await CreateFixtureAsync("Fixed", DateTimeOffset.UtcNow.AddDays(-10));
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
        await GitFixtureRunner.RequireAvailableAsync();
        var source = await CreateFixtureAsync("Fixed", DateTimeOffset.UtcNow.AddDays(-30));
        var clone = Path.Combine(
            TestTempRoot.Current,
            nameof(ReleaseLagGuardTests),
            Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(clone)!);
            await GitFixtureRunner.RunGitAsync(
                Path.GetDirectoryName(clone)!,
                "clone",
                "-q",
                "--depth",
                "1",
                new Uri(source).AbsoluteUri,
                clone);
            Assert.AreEqual(
                "true",
                (await GitFixtureRunner.RunGitCaptureAsync(clone, "rev-parse", "--is-shallow-repository")).Trim());

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

    private static async Task<string> CreateFixtureAsync(string category, DateTimeOffset committedAt)
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

        await GitFixtureRunner.InitializeRepositoryAsync(root);
        await GitFixtureRunner.RunGitAsync(root, "add", "-A");
        await CommitAtAsync(root, committedAt);
        return root;
    }

    private static async Task CommitAtAsync(string root, DateTimeOffset committedAt)
    {
        var result = await PwshScriptRunner.RunExecutableAsync(
            "git",
            ["commit", "-q", "-m", "fixture baseline"],
            new Dictionary<string, string?>
            {
                ["GIT_COMMITTER_DATE"] = committedAt.ToString("o"),
                ["GIT_AUTHOR_DATE"] = committedAt.ToString("o"),
            },
            workingDirectory: root,
            timeout: TimeSpan.FromSeconds(30),
            description: "git commit fixture baseline");
        Assert.AreEqual(0, result.ExitCode, result.AllOutput);
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

}
