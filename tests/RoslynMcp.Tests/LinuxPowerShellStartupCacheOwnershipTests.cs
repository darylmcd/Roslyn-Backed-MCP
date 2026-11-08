using System.Diagnostics;
using System.Text;
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

    [TestMethod]
    [TestCategory("Process")]
    public async Task NativeCacheVerification_WaitsForAnInProgressWriterAsync()
    {
        var root = TestFixtureFileSystem.FindRepositoryRoot();
        var cache = Path.Combine(TestTempRoot.Current, "module-cache-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cache);
        var file = Path.Combine(cache, "ModuleAnalysisCache-probe");
        var marker = Path.Combine(cache, "candidate-observed.marker");
        File.WriteAllBytes(file, Encoding.ASCII.GetBytes("PSMODULECACHEfixture"));
        try
        {
            using var writer = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var run = PwshScriptRunner.RunAsync(
                ["-NoProfile", "-NonInteractive", "-File",
                    Path.Combine(root, "tests/RoslynMcp.Tests/Fixtures/pwsh-startup-cache-verify.ps1"),
                    "-Cache", cache, "-BudgetSeconds", "10", "-ObservedMarkerPath", marker],
                timeout: TimeSpan.FromSeconds(15),
                description: "native module-cache writer readiness probe");
            var observedDeadline = DateTime.UtcNow.AddSeconds(10);
            while (!File.Exists(marker) && !run.IsCompleted && DateTime.UtcNow < observedDeadline)
            {
                await Task.Delay(25);
            }
            var candidateWasObserved = File.Exists(marker);
            var completedWhileLocked = await Task.WhenAny(run, Task.Delay(500)) == run;
            writer.Dispose();
            var result = await run;
            Assert.IsTrue(candidateWasObserved, result.AllOutput);
            Assert.IsFalse(completedWhileLocked, result.AllOutput);
            Assert.AreEqual(0, result.ExitCode, result.AllOutput);
            StringAssert.Contains(result.StdOut, "MODULE_CACHE_PASS=1");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(cache);
        }
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task NativeCacheVerification_WaitsForEveryObservedCandidateAsync()
    {
        var root = TestFixtureFileSystem.FindRepositoryRoot();
        var cache = Path.Combine(TestTempRoot.Current, "module-cache-cohort-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cache);
        var pending = Path.Combine(cache, "ModuleAnalysisCache-pending");
        var marker = Path.Combine(cache, "candidate-observed.marker");
        File.WriteAllBytes(Path.Combine(cache, "ModuleAnalysisCache-ready"),
            Encoding.ASCII.GetBytes("PSMODULECACHEfixture"));
        File.WriteAllBytes(pending, Encoding.ASCII.GetBytes("PSMODU"));
        try
        {
            using var writer = new FileStream(pending, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var run = PwshScriptRunner.RunAsync(
                ["-NoProfile", "-NonInteractive", "-File",
                    Path.Combine(root, "tests/RoslynMcp.Tests/Fixtures/pwsh-startup-cache-verify.ps1"),
                    "-Cache", cache, "-BudgetSeconds", "10", "-ObservedMarkerPath", marker],
                timeout: TimeSpan.FromSeconds(15),
                description: "native module-cache multi-candidate readiness probe");
            var observedDeadline = DateTime.UtcNow.AddSeconds(10);
            while (!File.Exists(marker) && !run.IsCompleted && DateTime.UtcNow < observedDeadline)
            {
                await Task.Delay(25);
            }
            var candidateWasObserved = File.Exists(marker);
            var completedWhileLocked = await Task.WhenAny(run, Task.Delay(500)) == run;
            writer.Dispose();
            File.WriteAllBytes(pending, Encoding.ASCII.GetBytes("PSMODULECACHEfixture"));
            var result = await run;
            Assert.IsTrue(candidateWasObserved, result.AllOutput);
            Assert.IsFalse(completedWhileLocked, result.AllOutput);
            Assert.AreEqual(0, result.ExitCode, result.AllOutput);
            StringAssert.Contains(result.StdOut, "MODULE_CACHE_PASS=2");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(cache);
        }
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task NativeCacheVerification_ReportsTheBlockedFileAtTheConfiguredDeadlineAsync()
    {
        var root = TestFixtureFileSystem.FindRepositoryRoot();
        var cache = Path.Combine(TestTempRoot.Current, "module-cache-timeout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cache);
        var file = Path.Combine(cache, "ModuleAnalysisCache-held");
        var marker = Path.Combine(cache, "candidate-observed.marker");
        File.WriteAllBytes(file, Encoding.ASCII.GetBytes("PSMODULECACHEfixture"));
        try
        {
            using var writer = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var run = PwshScriptRunner.RunAsync(
                ["-NoProfile", "-NonInteractive", "-File",
                    Path.Combine(root, "tests/RoslynMcp.Tests/Fixtures/pwsh-startup-cache-verify.ps1"),
                    "-Cache", cache, "-BudgetSeconds", "1", "-ObservedMarkerPath", marker],
                timeout: TimeSpan.FromSeconds(10),
                description: "native module-cache writer deadline probe");
            var observedDeadline = DateTime.UtcNow.AddSeconds(10);
            while (!File.Exists(marker) && !run.IsCompleted && DateTime.UtcNow < observedDeadline)
            {
                await Task.Delay(25);
            }
            var candidateWasObserved = File.Exists(marker);
            var observedAt = Stopwatch.GetTimestamp();
            var result = await run;
            Assert.IsTrue(candidateWasObserved, result.AllOutput);
            Assert.IsTrue(Stopwatch.GetElapsedTime(observedAt) >= TimeSpan.FromMilliseconds(600), result.AllOutput);
            Assert.AreNotEqual(0, result.ExitCode, result.AllOutput);
            StringAssert.Contains(result.AllOutput, "fixed 1-second budget");
            StringAssert.Contains(result.AllOutput, "last sharing violation");
            StringAssert.Contains(result.AllOutput, file);
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(cache);
        }
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task NativeCacheVerification_RejectsAnInvalidSignatureAsync()
    {
        var root = TestFixtureFileSystem.FindRepositoryRoot();
        var cache = Path.Combine(TestTempRoot.Current, "module-cache-invalid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cache);
        File.WriteAllBytes(Path.Combine(cache, "ModuleAnalysisCache-invalid"),
            Encoding.ASCII.GetBytes("INVALIDMODULECACHEfixture"));
        try
        {
            var result = await PwshScriptRunner.RunAsync(
                ["-NoProfile", "-NonInteractive", "-File",
                    Path.Combine(root, "tests/RoslynMcp.Tests/Fixtures/pwsh-startup-cache-verify.ps1"),
                    "-Cache", cache, "-BudgetSeconds", "1"],
                timeout: TimeSpan.FromSeconds(10),
                description: "native module-cache signature probe");
            Assert.AreNotEqual(0, result.ExitCode, result.AllOutput);
            StringAssert.Contains(result.AllOutput, "Unexpected native module-cache signature");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(cache);
        }
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
