using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ActionlintGateContractTests
{
    [TestMethod]
    public void VerifyActionlintScript_DeclaresPinnedVersionAndPerRidArchiveAndBinaryHashes()
    {
        var script = File.ReadAllText(ResolveScriptPath());

        StringAssert.Contains(script, "$PinnedVersion = '1.7.12'");

        foreach (var rid in new[] { "win-x64", "linux-x64", "linux-arm64", "osx-arm64" })
        {
            Assert.IsTrue(
                script.Contains($"'{rid}'", StringComparison.Ordinal),
                $"Pin table must declare an entry for RID '{rid}'.");
        }

        // Every declared hash must be a lowercase 64-character hex string (SHA-256), pinned
        // literally in the script text -- not derived at runtime from an unpinned source.
        var hexHashPattern = new System.Text.RegularExpressions.Regex(
            @"(?:ArchiveSha256|BinarySha256)\s*=\s*'([0-9a-f]{64})'");
        var matches = hexHashPattern.Matches(script);
        Assert.AreEqual(
            8,
            matches.Count,
            $"Expected one ArchiveSha256 + one BinarySha256 per RID (4 RIDs x 2 = 8); found {matches.Count}. Script:{Environment.NewLine}{script}");
    }

    [TestMethod]
    public void JustfileAndCiPolicy_WireTheActionlintGateIntoRequiredValidation()
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var justfile = File.ReadAllText(Path.Combine(repositoryRoot, "justfile"));
        var ciPolicy = File.ReadAllText(Path.Combine(repositoryRoot, "CI_POLICY.md"));

        StringAssert.Contains(justfile, "verify-actionlint:");
        StringAssert.Contains(justfile, "pwsh -NoProfile -File ./eng/verify-actionlint.ps1");

        var ciRecipeIndex = justfile.IndexOf("\nci:", StringComparison.Ordinal);
        Assert.IsTrue(ciRecipeIndex >= 0, "justfile must declare a 'ci:' aggregate recipe.");
        var ciRecipeLineEnd = justfile.IndexOf('\n', ciRecipeIndex + 1);
        var ciRecipeLine = justfile[ciRecipeIndex..(ciRecipeLineEnd < 0 ? justfile.Length : ciRecipeLineEnd)];
        StringAssert.Contains(
            ciRecipeLine,
            "verify-actionlint",
            "The 'just ci' aggregate must depend on verify-actionlint so a malformed workflow expression fails locally before push.");

        StringAssert.Contains(ciPolicy, "./eng/verify-actionlint.ps1");
        StringAssert.Contains(
            ciPolicy,
            "actionlint",
            "CI_POLICY.md's just ci composition sentence must name the actionlint gate as one of the composed children.");
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task VerifyActionlint_CachedBinaryHashMismatch_FailsClosedWithoutAttemptingDownload()
    {
        var fixtureRoot = CreateFixtureRoot();
        try
        {
            var toolRoot = Path.Combine(fixtureRoot, "artifacts", "tools", "actionlint", "1.7.12");
            Directory.CreateDirectory(toolRoot);
            const string binaryName = "actionlint.exe";
            // A cached binary whose bytes do NOT match the pinned hash -- this must be detected
            // and refused BEFORE the script ever considers re-downloading, so a tampered or
            // corrupted cache entry never silently runs.
            File.WriteAllText(Path.Combine(toolRoot, binaryName), "not the real actionlint binary");

            var result = await RunGateAsync(
                fixtureRoot,
                platformForTest: "windows",
                architectureForTest: "x64");

            Assert.AreNotEqual(0, result.ExitCode, result.AllOutput);
            StringAssert.Contains(result.AllOutput, "hash mismatch");
            StringAssert.Contains(result.AllOutput, "cached binary");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task VerifyActionlint_RoslynMcpActionlintPathOverride_HashMismatchFailsClosedAndNeverRuns()
    {
        var fixtureRoot = CreateFixtureRoot();
        var overridePath = Path.Combine(fixtureRoot, "not-actionlint.exe");
        File.WriteAllText(overridePath, "definitely not the pinned binary");
        try
        {
            var result = await RunGateAsync(
                fixtureRoot,
                environment: new Dictionary<string, string?>
                {
                    ["ROSLYNMCP_ACTIONLINT_PATH"] = overridePath,
                },
                platformForTest: "windows",
                architectureForTest: "x64");

            Assert.AreNotEqual(0, result.ExitCode, result.AllOutput);
            StringAssert.Contains(result.AllOutput, "ROSLYNMCP_ACTIONLINT_PATH");
            StringAssert.Contains(result.AllOutput, "hash mismatch");
            StringAssert.Contains(result.AllOutput, "refusing to run an unverified binary");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task VerifyActionlint_ChmodFailure_ReportsBoundedDiagnosticWithoutNetworkAccess()
    {
        var fixtureRoot = CreateFixtureRoot();
        try
        {
            var result = await RunGateAsync(fixtureRoot, failChmodForTest: true);

            Assert.AreNotEqual(0, result.ExitCode, result.AllOutput);
            const string diagnostic =
                "verify-actionlint: failed to mark actionlint executable (chmod exit code 1).";
            Assert.AreEqual(diagnostic, result.StdErr.Trim());
            Assert.AreEqual(string.Empty, result.StdOut);
            Assert.IsFalse(
                result.AllOutput.Contains("could not be downloaded", StringComparison.Ordinal),
                $"The failure-only test seam must run before network access. Output:{Environment.NewLine}{result.AllOutput}");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task VerifyActionlint_UnsupportedPlatform_FailsClosedBeforeFilesystemOrNetworkMutation()
    {
        var fixtureRoot = CreateFixtureRoot();
        try
        {
            var result = await RunGateAsync(fixtureRoot, platformForTest: "unsupported");

            Assert.AreNotEqual(0, result.ExitCode, result.AllOutput);
            const string diagnostic =
                "verify-actionlint: unsupported platform (neither Windows, macOS, nor Linux detected).";
            Assert.AreEqual(diagnostic, result.StdErr.Trim());
            Assert.AreEqual(string.Empty, result.StdOut);
            Assert.IsFalse(
                Directory.Exists(Path.Combine(fixtureRoot, "artifacts")),
                "Unsupported-platform detection must fail before creating the actionlint cache.");
            Assert.IsFalse(
                result.AllOutput.Contains("could not be downloaded", StringComparison.Ordinal),
                $"The unsupported-platform test seam must fail before network access. Output:{Environment.NewLine}{result.AllOutput}");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    [TestMethod]
    [TestCategory("Process")]
    [DataRow("windows", "x64", "actionlint.exe", "54ca21be3de4c7cfa26914aa8b61bd76bf573ef3caac5f80d110558cdf241718")]
    [DataRow("macos", "arm64", "actionlint", "8db11704dc296f096216db4db65d86cd7f0ebfdf4c38453a1da276b137b88388")]
    [DataRow("linux", "x64", "actionlint", "c872d6db8c6bf83a8eaa704fc93999f027d55dffbc63b8a6abdccb47df5f4cd4")]
    [DataRow("linux", "arm64", "actionlint", "ac0323433c2853ec3fb978c611430c5b3dc5d43c58d1a1ec031b00ab572beb60")]
    public async Task VerifyActionlint_SupportedPlatformArchitecture_UsesMatchingPin(
        string platform,
        string architecture,
        string binaryName,
        string expectedBinaryHash)
    {
        var fixtureRoot = CreateFixtureRoot();
        try
        {
            var toolRoot = Path.Combine(fixtureRoot, "artifacts", "tools", "actionlint", "1.7.12");
            Directory.CreateDirectory(toolRoot);
            File.WriteAllText(Path.Combine(toolRoot, binaryName), "not the real actionlint binary");

            var result = await RunGateAsync(
                fixtureRoot,
                platformForTest: platform,
                architectureForTest: architecture);

            Assert.AreNotEqual(0, result.ExitCode, result.AllOutput);
            StringAssert.Contains(result.AllOutput, "cached binary");
            StringAssert.Contains(result.AllOutput, expectedBinaryHash);
            Assert.IsFalse(
                result.AllOutput.Contains("could not be downloaded", StringComparison.Ordinal),
                $"Supported RID selection must use its cached pin without network access. Output:{Environment.NewLine}{result.AllOutput}");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task VerifyActionlint_UnpinnedPlatformArchitecture_FailsClosedBeforeFilesystemOrNetworkMutation()
    {
        var fixtureRoot = CreateFixtureRoot();
        try
        {
            var result = await RunGateAsync(
                fixtureRoot,
                platformForTest: "windows",
                architectureForTest: "arm64");

            Assert.AreEqual(1, result.ExitCode, result.AllOutput);
            const string diagnostic =
                "verify-actionlint: no pinned actionlint archive/hash recorded for RID 'win-arm64'.";
            Assert.AreEqual(diagnostic, result.StdErr.Trim());
            Assert.AreEqual(string.Empty, result.StdOut);
            Assert.IsFalse(
                Directory.Exists(Path.Combine(fixtureRoot, "artifacts")),
                "Unpinned-RID detection must fail before creating the actionlint cache.");
            Assert.IsFalse(
                result.AllOutput.Contains("could not be downloaded", StringComparison.Ordinal),
                $"The unpinned-RID test seam must fail before network access. Output:{Environment.NewLine}{result.AllOutput}");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task VerifyActionlint_TarExtractionFailure_ReportsBoundedDiagnosticBeforeExecution()
    {
        var fixtureRoot = CreateFixtureRoot();
        try
        {
            var result = await RunGateAsync(fixtureRoot, failTarExtractionForTest: true);

            Assert.AreEqual(23, result.ExitCode, result.AllOutput);
            const string diagnostic =
                "verify-actionlint: 'tar' extraction of 'test-only.tar.gz' failed with exit code 23.";
            Assert.AreEqual(diagnostic, result.StdErr.Trim());
            Assert.AreEqual(string.Empty, result.StdOut);
            Assert.IsFalse(
                Directory.Exists(Path.Combine(fixtureRoot, "artifacts")),
                "The failure-only extraction seam must run before cache creation or download.");
            Assert.IsFalse(
                result.AllOutput.Contains("running pinned actionlint", StringComparison.Ordinal),
                $"A failed extraction must not reach actionlint execution. Output:{Environment.NewLine}{result.AllOutput}");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    // Live-network smoke test: downloads the real pinned release once (verifying the archive
    // hash), then re-runs against the now-populated cache and asserts the second run completes
    // in well under the time a fresh download+extract would take -- the offline cache-hit path,
    // not a live re-download. Marked Network per CI_POLICY.md (excluded on PR; included
    // weekly/manual), mirroring NuGetVulnerabilityScanIntegrationTests' precedent for a gate
    // that legitimately needs one real external call.
    [TestMethod]
    [TestCategory("Network")]
    public async Task VerifyActionlint_ColdDownloadThenCacheHit_LintsRealWorkflowsAndStaysOfflineOnRerun()
    {
        var fixtureRoot = CreateFixtureRoot();
        try
        {
            var coldResult = await RunGateAsync(fixtureRoot, timeout: TimeSpan.FromMinutes(2));
            Assert.AreEqual(0, coldResult.ExitCode, coldResult.AllOutput);
            StringAssert.Contains(coldResult.AllOutput, "no issues found");

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var cachedResult = await RunGateAsync(fixtureRoot, timeout: TimeSpan.FromSeconds(30));
            stopwatch.Stop();

            Assert.AreEqual(0, cachedResult.ExitCode, cachedResult.AllOutput);
            Assert.IsTrue(
                stopwatch.Elapsed < TimeSpan.FromSeconds(10),
                $"A cache hit must not re-download the archive; took {stopwatch.Elapsed.TotalSeconds:F1}s.");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    private static string ResolveScriptPath() => Path.Combine(
        TestFixtureFileSystem.FindRepositoryRoot(),
        "eng",
        "verify-actionlint.ps1");

    private static string CreateFixtureRoot()
    {
        var root = Path.Combine(
            TestTempRoot.Current,
            nameof(ActionlintGateContractTests),
            Guid.NewGuid().ToString("N"));
        var workflowsDir = Path.Combine(root, ".github", "workflows");
        Directory.CreateDirectory(workflowsDir);
        File.WriteAllText(
            Path.Combine(workflowsDir, "fixture.yml"),
            """
            name: fixture
            on:
              push:
            jobs:
              noop:
                runs-on: ubuntu-latest
                steps:
                  - run: echo ok
            """);
        return root;
    }

    private static Task<PwshScriptResult> RunGateAsync(
        string fixtureRoot,
        IReadOnlyDictionary<string, string?>? environment = null,
        TimeSpan? timeout = null,
        bool failChmodForTest = false,
        bool failTarExtractionForTest = false,
        string? platformForTest = null,
        string? architectureForTest = null)
    {
        var arguments = new List<string>
        {
            "-NoProfile",
            "-File",
            ResolveScriptPath(),
            "-RepoRoot",
            fixtureRoot,
        };
        if (failChmodForTest)
        {
            arguments.Add("-FailChmodForTest");
        }
        if (failTarExtractionForTest)
        {
            arguments.Add("-FailTarExtractionForTest");
        }
        if (platformForTest is not null)
        {
            arguments.Add("-PlatformForTest");
            arguments.Add(platformForTest);
        }
        if (architectureForTest is not null)
        {
            arguments.Add("-ArchitectureForTest");
            arguments.Add(architectureForTest);
        }

        return PwshScriptRunner.RunAsync(
            arguments,
            environment: environment,
            timeout: timeout ?? TimeSpan.FromSeconds(30),
            description: "actionlint gate");
    }
}
