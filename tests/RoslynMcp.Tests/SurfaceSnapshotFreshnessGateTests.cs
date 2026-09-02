using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// surface-snapshot-stale-surface-audit: the .ai-doc-audit.md Live Surface table is
/// hand-maintained and nothing gated it, so it drifted across twelve tagged releases while its
/// backlog row sat unpicked at Low. eng/verify-surface-snapshot-freshness.ps1 turns that reminder
/// into a release-cut refusal condition. These tests cover both halves of the fix: the script
/// detects drift, and the release-cut skill actually invokes it before it mutates anything.
/// </summary>
[TestClass]
public sealed class SurfaceSnapshotFreshnessGateTests
{
    private const string ReadmeLiveSurface =
        "## Live Surface\n\n"
        + "The current release exposes **174 tools** (113 stable / 61 experimental), "
        + "**14 resources** (9 stable / 5 experimental), and **20 prompts** (all experimental).\n";

    [TestMethod]
    [DataRow(113, 61, 174, 9, 5, 14, 32, 0, DisplayName = "snapshot matches live surface")]
    [DataRow(111, 57, 168, 9, 4, 13, 32, 1, DisplayName = "stale tool and resource counts")]
    [DataRow(113, 61, 174, 9, 5, 14, 31, 1, DisplayName = "stale bundled-skill count")]
    public async Task VerifySurfaceSnapshotFreshness_ComparesSnapshotAgainstLiveSurface(
        int toolsStable,
        int toolsExperimental,
        int toolsTotal,
        int resourcesStable,
        int resourcesExperimental,
        int resourcesTotal,
        int skills,
        int expectedExitCode)
    {
        var fixture = CreateFixtureRoot();
        try
        {
            File.WriteAllText(Path.Combine(fixture, "README.md"), ReadmeLiveSurface);

            // The shipped skill count is measured from disk, never from a doc claim.
            for (var i = 0; i < 32; i++)
            {
                var skillDirectory = Path.Combine(fixture, "skills", $"skill-{i:D2}");
                Directory.CreateDirectory(skillDirectory);
                File.WriteAllText(Path.Combine(skillDirectory, "SKILL.md"), "---\nname: stub\n---\n");
            }

            File.WriteAllText(
                Path.Combine(fixture, ".ai-doc-audit.md"),
                "## Live Surface (snapshot 2026-09-02)\n\n"
                + "| Component | Stable | Experimental | Total |\n"
                + "|-----------|--------|--------------|-------|\n"
                + $"| Tools | {toolsStable} | {toolsExperimental} | {toolsTotal} |\n"
                + $"| Resources | {resourcesStable} | {resourcesExperimental} | {resourcesTotal} |\n"
                + "| Prompts | 0 | 20 | 20 |\n"
                + $"| Bundled skills | - | - | {skills} |\n\n"
                + "## Known Gaps\n");

            var result = await RunGateAsync(fixture);

            Assert.AreEqual(expectedExitCode, result.ExitCode, result.AllOutput);
            if (expectedExitCode != 0)
            {
                StringAssert.Contains(result.AllOutput, "SURFACE SNAPSHOT DRIFT DETECTED");
            }
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixture);
        }
    }

    [TestMethod]
    public async Task VerifySurfaceSnapshotFreshness_FailsWhenSnapshotHeadingHasNoDate()
    {
        var fixture = CreateFixtureRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(fixture, "skills"));
            File.WriteAllText(Path.Combine(fixture, "README.md"), ReadmeLiveSurface);
            File.WriteAllText(
                Path.Combine(fixture, ".ai-doc-audit.md"),
                "## Live Surface\n\n| Tools | 113 | 61 | 174 |\n");

            var result = await RunGateAsync(fixture);

            Assert.AreEqual(1, result.ExitCode, result.AllOutput);
            StringAssert.Contains(result.AllOutput, "snapshot YYYY-MM-DD");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixture);
        }
    }

    [TestMethod]
    public void ReleaseCutSkill_RunsSnapshotGateBeforeDelegatingToBump()
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var skillPath = Path.Combine(repositoryRoot, ".claude", "skills", "release-cut", "SKILL.md");
        var skill = File.ReadAllText(skillPath);
        const string invocation = "pwsh -NoProfile -File eng/verify-surface-snapshot-freshness.ps1";

        StringAssert.Contains(
            skill,
            invocation,
            "release-cut must run the surface-snapshot gate; a prose reminder is what previously failed.");

        var gateIndex = skill.IndexOf(invocation, StringComparison.Ordinal);
        var bumpIndex = skill.IndexOf("### Step 2: Bump", StringComparison.Ordinal);

        Assert.IsTrue(bumpIndex >= 0, "release-cut must still delegate to /bump in Step 2.");
        Assert.IsTrue(
            gateIndex < bumpIndex,
            "The surface-snapshot gate must run in Step 1 preflight, before any version-file mutation.");
    }

    private static string CreateFixtureRoot()
    {
        var fixture = Path.Combine(Path.GetTempPath(), $"roslynmcp-surface-snapshot-{Guid.NewGuid():N}");
        Directory.CreateDirectory(fixture);
        return fixture;
    }

    private static Task<PwshScriptResult> RunGateAsync(string fixtureRoot)
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        return PwshScriptRunner.RunAsync(
            [
                "-NoProfile",
                "-File",
                Path.Combine(repositoryRoot, "eng", "verify-surface-snapshot-freshness.ps1"),
                "-RepositoryRoot",
                fixtureRoot,
            ],
            timeout: TimeSpan.FromSeconds(60),
            description: "surface-snapshot freshness gate");
    }
}
