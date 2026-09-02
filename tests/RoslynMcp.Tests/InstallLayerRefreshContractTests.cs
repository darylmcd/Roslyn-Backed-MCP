using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// The server ships on two install surfaces that drift apart silently, in both directions:
/// v1.29.0 and v1.34.2 refreshed the Layer 1 global tool and left the Layer 2 plugin cache
/// stale, and v4.1.2 refreshed Layer 2 while Layer 1 sat a release behind because the
/// global-tool update was documented as optional. These tests pin the contract that
/// <c>/release-cut</c> Step 6 refreshes both and proves it with
/// <c>eng/verify-install-layers.ps1</c>, and cover the verifier's own comparison logic.
/// </summary>
[TestClass]
public sealed class InstallLayerRefreshContractTests
{
    private const string VerifierInvocation = "pwsh -NoProfile -File eng/verify-install-layers.ps1";

    [TestMethod]
    public void ReleaseCutSkill_RefreshesBothLayersAndProvesThem()
    {
        var skill = ReadSkill("release-cut");

        StringAssert.Contains(
            skill,
            "just tool-update",
            "Step 6 must refresh the Layer 1 global tool, not leave it to the maintainer.");
        StringAssert.Contains(
            skill,
            "eng/update-claude-plugin.ps1",
            "Step 6 must refresh the Layer 2 plugin cache.");
        StringAssert.Contains(
            skill,
            VerifierInvocation,
            "Step 6 must prove both layers with the install-layer verifier.");

        Assert.IsFalse(
            skill.Contains("global-tool update is optional", StringComparison.OrdinalIgnoreCase),
            "The Layer 1 refresh must not be documented as optional again.");
    }

    [TestMethod]
    public void MaintainerUpdateSkill_TreatsGlobalToolRefreshAsRequired()
    {
        var skill = ReadSkill("update");

        StringAssert.Contains(skill, "just tool-update");
        StringAssert.Contains(skill, VerifierInvocation);
        Assert.IsFalse(
            skill.Contains("Optional standalone global tool", StringComparison.OrdinalIgnoreCase),
            "The maintainer /update skill must not restore the optional global-tool step.");
    }

    [TestMethod]
    [DataRow("4.1.2", "4.1.2", 0, DisplayName = "both layers current")]
    [DataRow("4.1.1", "4.1.2", 1, DisplayName = "layer 1 stale")]
    [DataRow("4.1.2", "4.1.1", 1, DisplayName = "layer 2 stale")]
    public async Task VerifyInstallLayers_FailsWhenEitherLayerLagsTheRelease(
        string layer1Version,
        string cachedVersion,
        int expectedExitCode)
    {
        const string expectedVersion = "4.1.2";
        var cacheRoot = Path.Combine(
            Path.GetTempPath(),
            $"roslynmcp-install-layers-{Guid.NewGuid():N}");
        try
        {
            var versionDirectory = Path.Combine(cacheRoot, cachedVersion, ".claude-plugin");
            Directory.CreateDirectory(versionDirectory);
            File.WriteAllText(
                Path.Combine(versionDirectory, "plugin.json"),
                $"{{\"version\":\"{cachedVersion}\"}}");

            var result = await RunVerifierAsync(expectedVersion, cacheRoot, layer1Version);

            Assert.AreEqual(expectedExitCode, result.ExitCode, result.AllOutput);
            if (expectedExitCode != 0)
            {
                StringAssert.Contains(result.AllOutput, "INSTALL LAYER DRIFT DETECTED");
                var staleLayer = layer1Version == expectedVersion ? "Layer 2" : "Layer 1";
                StringAssert.Contains(
                    result.AllOutput,
                    staleLayer,
                    "The failure must name which layer is stale.");
            }
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(cacheRoot);
        }
    }

    [TestMethod]
    public async Task VerifyInstallLayers_FailsWhenLayer2RetainsAStaleVersionDirectory()
    {
        const string expectedVersion = "4.1.2";
        var cacheRoot = Path.Combine(
            Path.GetTempPath(),
            $"roslynmcp-install-layers-{Guid.NewGuid():N}");
        try
        {
            // The updater prunes old directories; a leftover means the refresh did not complete.
            Directory.CreateDirectory(Path.Combine(cacheRoot, expectedVersion));
            Directory.CreateDirectory(Path.Combine(cacheRoot, "4.1.1"));

            var result = await RunVerifierAsync(expectedVersion, cacheRoot, expectedVersion);

            Assert.AreEqual(1, result.ExitCode, result.AllOutput);
            StringAssert.Contains(result.AllOutput, "stale version directories");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(cacheRoot);
        }
    }

    private static string ReadSkill(string skillName) => File.ReadAllText(
        Path.Combine(
            TestFixtureFileSystem.FindRepositoryRoot(),
            ".claude",
            "skills",
            skillName,
            "SKILL.md"));

    private static Task<PwshScriptResult> RunVerifierAsync(
        string expectedVersion,
        string pluginCacheRoot,
        string layer1Version)
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        return PwshScriptRunner.RunAsync(
            [
                "-NoProfile",
                "-File",
                Path.Combine(repositoryRoot, "eng", "verify-install-layers.ps1"),
                "-ExpectedVersion",
                expectedVersion,
                "-RepositoryRoot",
                repositoryRoot,
                "-PluginCacheRoot",
                pluginCacheRoot,
                "-Layer1Version",
                layer1Version,
            ],
            timeout: TimeSpan.FromSeconds(60),
            description: "install-layer verifier");
    }
}
