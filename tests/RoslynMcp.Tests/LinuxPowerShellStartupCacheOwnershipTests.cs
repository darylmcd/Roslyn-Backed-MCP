using System.Text.RegularExpressions;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class LinuxPowerShellStartupCacheOwnershipTests
{
    [TestMethod]
    public void FullSuiteJobs_InitializeTheSameCacheBeforeTheirFirstPowerShellStep()
    {
        var root = TestFixtureFileSystem.FindRepositoryRoot();
        var ci = File.ReadAllText(Path.Combine(root, ".github/workflows/ci.yml")).ReplaceLineEndings("\n");
        var publish = File.ReadAllText(Path.Combine(root, ".github/workflows/publish-nuget.yml")).ReplaceLineEndings("\n");
        AssertInitialization(GetJob(ci, "validate"), linuxMatrix: true);
        AssertInitialization(GetJob(publish, "publish"), linuxMatrix: false);
        foreach (var role in new[] { "route", "quality", "sdk_floor", "evidence_lint", "validate-gate" })
        {
            Assert.IsFalse(GetJob(ci, role).Contains("initialize-pwsh-startup-cache.sh", StringComparison.Ordinal), role);
        }

        var helper = File.ReadAllText(Path.Combine(root, "eng/initialize-pwsh-startup-cache.sh"));
        StringAssert.Contains(helper, "XDG_CACHE_HOME=%s\\n");
        StringAssert.Contains(helper, ">> \"$GITHUB_ENV\"");
        StringAssert.Contains(helper, "[System.Runtime.ProfileOptimization]::StartProfile($null)");
        StringAssert.Contains(helper, "chmod 444 \"$profile\"");
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task NativeCache_DeniesOrdinaryProfileWritesAndPreservesFunctionalConsumersAsync()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("Native Linux file permission ownership requires Linux.");
        }

        var root = TestFixtureFileSystem.FindRepositoryRoot();
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "pwsh-ownership-" + Guid.NewGuid().ToString("N"));
        var result = await PwshScriptRunner.RunExecutableAsync(
            "bash",
            [Path.Combine(root, "tests/RoslynMcp.Tests/Fixtures/pwsh-startup-cache-ownership.sh"), root, temporaryRoot],
            environment: null,
            workingDirectory: root,
            timeout: TimeSpan.FromMinutes(2),
            description: "native PowerShell startup ownership regression");
        Assert.AreEqual(0, result.ExitCode, result.AllOutput);
        StringAssert.Contains(result.StdOut, "OWNERSHIP_NATIVE_PASS");
        // The runner owns eventual cleanup: a failed descendant must never lose its input early.
    }

    private static void AssertInitialization(string job, bool linuxMatrix)
    {
        const string invocation = "run: bash ./eng/initialize-pwsh-startup-cache.sh";
        var start = job.IndexOf("      - name: Initialize PowerShell startup cache", StringComparison.Ordinal);
        Assert.IsTrue(start >= 0);
        var nextStep = job.IndexOf("      - name:", start + 1, StringComparison.Ordinal);
        Assert.IsTrue(nextStep > start);
        var step = job[start..nextStep];
        StringAssert.Contains(step, "shell: bash");
        StringAssert.Contains(step, invocation);
        if (linuxMatrix)
        {
            StringAssert.Contains(step, "if: runner.os == 'Linux'");
        }

        Assert.IsTrue(job.IndexOf("uses: actions/checkout@", StringComparison.Ordinal) < start);
        Assert.IsTrue(job.IndexOf("shell: pwsh", StringComparison.Ordinal) > nextStep);
        Assert.AreEqual(1, Regex.Matches(job, Regex.Escape(invocation)).Count);
    }

    private static string GetJob(string workflow, string job)
    {
        var match = Regex.Match(workflow, @"^  " + Regex.Escape(job) + @":\n.*?(?=^  [a-zA-Z_][a-zA-Z_0-9-]*:|\z)", RegexOptions.Multiline | RegexOptions.Singleline);
        Assert.IsTrue(match.Success, job);
        return match.Value;
    }
}
