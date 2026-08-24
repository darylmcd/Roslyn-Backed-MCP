using System.Diagnostics;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class VerifyReleaseChildScriptTests
{
    private const int _childFailureExitCode = 37;

    private static readonly ChildScript[] _childScripts =
    [
        new("verify-package-family-parity.ps1", "Package family parity validation"),
        new("verify-version-drift.ps1", "Version drift validation"),
        new("verify-skills-are-generic.ps1", "Shipped skill validation"),
        new("verify-plugin-package-files.ps1", "Plugin package allowlist validation"),
        new("verify-changelog-fragments.ps1", "Changelog fragment validation"),
        new("verify-breaking-version-bump.ps1", "Breaking version validation"),
        new("verify-registry-readiness.ps1", "Registry readiness validation"),
    ];

    [TestMethod]
    [TestCategory("Process")]
    public async Task VerifyRelease_PropagatesEveryChildScriptFailureAsync()
    {
        var cases = _childScripts
            .Select(child => new ReleaseCase(
                $"{child.FileName} exits nonzero",
                child.FileName,
                FailureMode.Exit,
                RequireConsumedFragments: false,
                child.Description,
                ShouldReachDotnet: false))
            .Concat(
            [
                new ReleaseCase(
                    "breaking gate publish mode exits nonzero",
                    "verify-breaking-version-bump.ps1",
                    FailureMode.Exit,
                    RequireConsumedFragments: true,
                    "Breaking version validation",
                    ShouldReachDotnet: false),
                new ReleaseCase(
                    "terminating child error propagates",
                    "verify-plugin-package-files.ps1",
                    FailureMode.Throw,
                    RequireConsumedFragments: false,
                    "fixture terminating error",
                    ShouldReachDotnet: false),
                new ReleaseCase(
                    "all children return without explicit exit",
                    FailingScript: null,
                    FailureMode.None,
                    RequireConsumedFragments: false,
                    ExpectedText: null,
                    ShouldReachDotnet: true),
                new ReleaseCase(
                    "all children return without explicit exit in publish mode",
                    FailingScript: null,
                    FailureMode.None,
                    RequireConsumedFragments: true,
                    ExpectedText: null,
                    ShouldReachDotnet: true),
            ])
            .ToArray();

        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();

        foreach (var testCase in cases)
        {
            var fixtureRoot = Path.Combine(
                TestTempRoot.Current,
                nameof(VerifyReleaseChildScriptTests),
                Guid.NewGuid().ToString("N"));

            try
            {
                var fixture = WriteFixture(repositoryRoot, fixtureRoot, testCase);
                var result = await RunVerifyReleaseAsync(fixture, testCase.RequireConsumedFragments);
                var combinedOutput = PowerShellOutputNormalizer.Normalize(
                    result.StdOut + result.StdErr);
                var diagnostic = $"{testCase.Name}: stdout={result.StdOut} stderr={result.StdErr}";

                if (testCase.ShouldReachDotnet)
                {
                    Assert.AreEqual(0, result.ExitCode, diagnostic);
                    Assert.IsTrue(
                        File.Exists(fixture.DotnetSentinelPath),
                        $"{testCase.Name}: the all-green preflight never reached fake dotnet.");
                    continue;
                }

                Assert.AreNotEqual(0, result.ExitCode, diagnostic);
                Assert.IsFalse(
                    File.Exists(fixture.DotnetSentinelPath),
                    $"{testCase.Name}: restore began after a failed preflight.");
                StringAssert.Contains(combinedOutput, testCase.ExpectedText!, diagnostic);
                if (testCase.FailureMode == FailureMode.Exit)
                {
                    StringAssert.Contains(
                        combinedOutput,
                        $"exit code {_childFailureExitCode}",
                        diagnostic);
                }

                Assert.IsFalse(
                    combinedOutput.Contains("-RequireConsumedFragments", StringComparison.Ordinal),
                    $"{testCase.Name}: the safe parent diagnostic leaked child arguments.");
                Assert.IsFalse(
                    combinedOutput.Contains("-Quiet", StringComparison.Ordinal),
                    $"{testCase.Name}: the safe parent diagnostic leaked child arguments.");
                Assert.IsFalse(
                    combinedOutput.Contains(fixture.DotnetSentinelPath, StringComparison.Ordinal),
                    $"{testCase.Name}: the parent diagnostic leaked an environment value.");
            }
            finally
            {
                TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
            }
        }
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task VerifyRelease_FailsWhenSuccessfulTestCommandDoesNotProduceValidTrxAsync()
    {
        var cases = new[]
        {
            new TrxCase("missing", "without producing the required TRX result"),
            new TrxCase("malformed", "produced an unreadable TRX result"),
            new TrxCase("empty", "does not prove any tests executed"),
            new TrxCase("zero", "does not prove any tests executed"),
        };
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();

        foreach (var testCase in cases)
        {
            var fixtureRoot = Path.Combine(
                TestTempRoot.Current,
                nameof(VerifyReleaseChildScriptTests),
                Guid.NewGuid().ToString("N"));
            try
            {
                var releaseCase = new ReleaseCase(
                    $"{testCase.Mode} TRX",
                    FailingScript: null,
                    FailureMode.None,
                    RequireConsumedFragments: false,
                    ExpectedText: null,
                    ShouldReachDotnet: true);
                var fixture = WriteFixture(repositoryRoot, fixtureRoot, releaseCase);

                var result = await RunVerifyReleaseAsync(
                    fixture,
                    requireConsumedFragments: false,
                    trxMode: testCase.Mode);
                var combinedOutput = PowerShellOutputNormalizer.Normalize(
                    result.StdOut + result.StdErr);
                var diagnostic = $"stdout={result.StdOut} stderr={result.StdErr}";

                Assert.AreNotEqual(0, result.ExitCode, diagnostic);
                StringAssert.Contains(combinedOutput, testCase.ExpectedText, diagnostic);
                Assert.IsFalse(
                    File.Exists(Path.Combine(
                        fixtureRoot,
                        "artifacts",
                        "manifests",
                        "host-stdio-sha256.txt")),
                    "Publishing must not continue without trustworthy test evidence.");
            }
            finally
            {
                TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
            }
        }
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task VerifyRelease_TestShardOnlySkipsPolicyAndPublishAsync()
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var fixtureRoot = Path.Combine(
            TestTempRoot.Current,
            nameof(VerifyReleaseChildScriptTests),
            Guid.NewGuid().ToString("N"));
        try
        {
            var releaseCase = new ReleaseCase(
                "test shard only",
                FailingScript: "verify-version-drift.ps1",
                FailureMode.Throw,
                RequireConsumedFragments: false,
                ExpectedText: null,
                ShouldReachDotnet: true);
            var fixture = WriteFixture(repositoryRoot, fixtureRoot, releaseCase);
            WriteFakeShardPlanner(fixtureRoot);

            var result = await RunVerifyReleaseAsync(
                fixture,
                requireConsumedFragments: false,
                testShardOnly: true,
                testShardIndex: 0,
                testShardCount: 2);
            var diagnostic = $"stdout={result.StdOut} stderr={result.StdErr}";

            Assert.AreEqual(0, result.ExitCode, diagnostic);
            Assert.IsTrue(File.Exists(fixture.DotnetSentinelPath), diagnostic);
            var dotnetArguments = await File.ReadAllTextAsync(fixture.DotnetArgumentsPath);
            StringAssert.Contains(dotnetArguments, "--filter", diagnostic);
            StringAssert.Contains(
                dotnetArguments,
                "(ClassName=RoslynMcp.Tests.ShardSentinel)&TestCategory!=Benchmark",
                "The verifier must pass the selected exact class filter into dotnet test.");
            StringAssert.Contains(
                dotnetArguments,
                "TMPDIR=",
                "Linux testhosts must receive the same private temp root as Windows testhosts.");
            Assert.IsFalse(
                Directory.Exists(Path.Combine(fixtureRoot, "artifacts", "publish")),
                "A test-only shard must not publish release output.");
            Assert.IsFalse(
                Directory.Exists(Path.Combine(fixtureRoot, "artifacts", "manifests")),
                "A test-only shard must not create release manifests.");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task VerifyRelease_RetriesTransientCleanupAndFailsClosedOnPersistentCleanupAsync()
    {
        var cases = new[]
        {
            new CleanupCase(
                "transient cleanup denial",
                "transient",
                ExpectedExitCode: 0,
                ExpectedAttempts: 2,
                ShouldPublish: true,
                FailDotnetTest: false),
            new CleanupCase(
                "persistent cleanup denial",
                "persistent",
                ExpectedExitCode: 1,
                ExpectedAttempts: 8,
                ShouldPublish: false,
                FailDotnetTest: false),
            new CleanupCase(
                "test and cleanup failure",
                "persistent",
                ExpectedExitCode: 1,
                ExpectedAttempts: 8,
                ShouldPublish: false,
                FailDotnetTest: true),
        };
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();

        foreach (var testCase in cases)
        {
            var fixtureRoot = Path.Combine(
                TestTempRoot.Current,
                nameof(VerifyReleaseChildScriptTests),
                Guid.NewGuid().ToString("N"));
            string? privateTestRoot = null;

            try
            {
                var releaseCase = new ReleaseCase(
                    testCase.Name,
                    FailingScript: null,
                    FailureMode.None,
                    RequireConsumedFragments: false,
                    ExpectedText: null,
                    ShouldReachDotnet: true);
                var fixture = WriteFixture(repositoryRoot, fixtureRoot, releaseCase);
                fixture = fixture with { ScriptPath = WriteCleanupDenialWrapper(fixtureRoot) };

                var result = await RunVerifyReleaseAsync(
                    fixture,
                    requireConsumedFragments: false,
                    cleanupMode: testCase.Mode,
                    failDotnetTest: testCase.FailDotnetTest);
                var combinedOutput = result.StdOut + result.StdErr;
                // Also rejoins the formatter's '|' continuation gutter, which survives a
                // plain whitespace collapse and splits expected phrases on Linux runners.
                var normalizedOutput = PowerShellOutputNormalizer.Normalize(combinedOutput);
                var diagnostic = $"{testCase.Name}: stdout={result.StdOut} stderr={result.StdErr}";
                var attemptPath = Path.Combine(fixtureRoot, "cleanup-attempts.txt");
                var targetPath = Path.Combine(fixtureRoot, "cleanup-target.txt");

                Assert.IsTrue(File.Exists(attemptPath), diagnostic);
                Assert.IsTrue(File.Exists(targetPath), diagnostic);
                Assert.AreEqual(
                    testCase.ExpectedAttempts,
                    int.Parse(File.ReadAllText(attemptPath), System.Globalization.CultureInfo.InvariantCulture),
                    diagnostic);
                privateTestRoot = File.ReadAllText(targetPath);
                AssertPrivateTestRoot(privateTestRoot);

                Assert.AreEqual(testCase.ExpectedExitCode, result.ExitCode, diagnostic);
                Assert.IsTrue(File.Exists(fixture.DotnetSentinelPath), diagnostic);
                Assert.AreEqual(
                    testCase.ShouldPublish,
                    File.Exists(Path.Combine(
                        fixtureRoot,
                        "artifacts",
                        "manifests",
                        "host-stdio-sha256.txt")),
                    diagnostic);
                Assert.AreEqual(
                    testCase.ShouldPublish,
                    !Directory.Exists(privateTestRoot),
                    diagnostic);
                Assert.AreEqual(
                    testCase.ExpectedAttempts - 1,
                    combinedOutput.Split(
                        "Private test-temp cleanup was temporarily blocked",
                        StringSplitOptions.None).Length - 1,
                    diagnostic);
                Assert.IsFalse(
                    combinedOutput.Contains("fixture cleanup denial", StringComparison.Ordinal),
                    $"{testCase.Name}: the final diagnostic exposed the injected descendant failure detail.");
                if (testCase.ExpectedExitCode != 0)
                {
                    StringAssert.Contains(
                        normalizedOutput,
                        "test-temp cleanup remained blocked after 8 attempts.",
                        diagnostic);
                }

                if (testCase.FailDotnetTest)
                {
                    StringAssert.Contains(
                        normalizedOutput,
                        "dotnet test failed and test-environment cleanup also failed.",
                        diagnostic);
                    StringAssert.Contains(
                        normalizedOutput,
                        "dotnet test failed with exit code 23.",
                        diagnostic);
                }
            }
            finally
            {
                if (privateTestRoot is not null && Directory.Exists(privateTestRoot))
                {
                    TestFixtureFileSystem.DeleteDirectoryIfExists(privateTestRoot);
                }

                TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
            }
        }
    }

    private static ReleaseFixture WriteFixture(
        string repositoryRoot,
        string fixtureRoot,
        ReleaseCase testCase)
    {
        var fixtureEngDirectory = Path.Combine(fixtureRoot, "eng");
        Directory.CreateDirectory(fixtureEngDirectory);

        File.Copy(
            Path.Combine(repositoryRoot, "eng", "verify-release.ps1"),
            Path.Combine(fixtureEngDirectory, "verify-release.ps1"));

        foreach (var child in _childScripts)
        {
            var failureMode = string.Equals(
                child.FileName,
                testCase.FailingScript,
                StringComparison.Ordinal)
                ? testCase.FailureMode
                : FailureMode.None;
            File.WriteAllText(
                Path.Combine(fixtureEngDirectory, child.FileName),
                CreateChildScript(failureMode));
        }

        var dotnetSentinelPath = Path.Combine(fixtureRoot, "dotnet-invoked.txt");
        var dotnetArgumentsPath = Path.Combine(fixtureRoot, "dotnet-arguments.jsonl");
        WriteFakeDotnetFunction(fixtureRoot);
        var wrapperPath = WriteVerifyReleaseWrapper(fixtureRoot);

        return new ReleaseFixture(
            wrapperPath,
            dotnetSentinelPath,
            dotnetArgumentsPath,
            fixtureRoot);
    }

    private static string CreateChildScript(FailureMode failureMode)
    {
        var failure = failureMode switch
        {
            FailureMode.Exit => $"exit {_childFailureExitCode}",
            FailureMode.Throw => "throw 'fixture terminating error'",
            _ => "# Success deliberately has no explicit exit so stale exit state is observable.",
        };

        return $$"""
            param(
                [switch]$RequireConsumedFragments,
                [switch]$Quiet
            )

            {{failure}}

            if ($MyInvocation.MyCommand.Name -eq 'verify-registry-readiness.ps1' -and -not $Quiet) {
                throw 'registry quiet switch missing'
            }
            """;
    }

    private static void WriteFakeDotnetFunction(string fixtureRoot)
    {
        File.WriteAllText(
            Path.Combine(fixtureRoot, "fake-dotnet.ps1"),
            """
            function global:dotnet {
                $arguments = @($args)
                [System.IO.File]::WriteAllText($env:ROSLYNMCP_DOTNET_SENTINEL, 'invoked')
                [System.IO.File]::AppendAllText(
                    $env:ROSLYNMCP_DOTNET_ARGUMENTS,
                    (($arguments | ConvertTo-Json -Compress) + [System.Environment]::NewLine))
                $global:LASTEXITCODE = 0

                if ($arguments.Count -gt 0 -and $arguments[0] -eq 'msbuild') {
                    Write-Output 'C:/fixture/RoslynMcp.Tests.dll'
                    return
                }
                if ($arguments.Count -eq 0 -or $arguments[0] -ne 'test') {
                    return
                }
                if ($env:ROSLYNMCP_DOTNET_TEST_FAIL -eq '1') {
                    $global:LASTEXITCODE = 23
                    return
                }
                if ($env:ROSLYNMCP_DOTNET_TRX_MODE -eq 'missing') {
                    return
                }

                $trxArgument = $arguments |
                    Where-Object { $_ -is [string] -and $_.StartsWith('trx;LogFileName=', [System.StringComparison]::Ordinal) } |
                    Select-Object -Last 1
                if ([string]::IsNullOrWhiteSpace($trxArgument)) {
                    return
                }

                $trxPath = $trxArgument.Substring('trx;LogFileName='.Length)
                $contents = switch ($env:ROSLYNMCP_DOTNET_TRX_MODE) {
                    'malformed' { '<TestRun><broken>'; break }
                    'empty' { '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010" />'; break }
                    'zero' {
                        '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><ResultSummary outcome="Completed"><Counters total="0" executed="0" passed="0" failed="0" /></ResultSummary></TestRun>'
                        break
                    }
                    default {
                        '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><ResultSummary outcome="Completed"><Counters total="1" executed="1" passed="1" failed="0" /></ResultSummary></TestRun>'
                    }
                }
                [System.IO.File]::WriteAllText($trxPath, $contents)
            }
            """);
    }

    private static string WriteVerifyReleaseWrapper(string fixtureRoot)
    {
        var wrapperPath = Path.Combine(fixtureRoot, "verify-release-fixture.ps1");
        File.WriteAllText(
            wrapperPath,
            """
            param(
                [switch]$NoCoverage,
                [switch]$RequireConsumedFragments,
                [switch]$TestShardOnly,
                [int]$TestShardIndex = 0,
                [int]$TestShardCount = 1
            )

            . (Join-Path $PSScriptRoot 'fake-dotnet.ps1')
            & (Join-Path $PSScriptRoot 'eng/verify-release.ps1') `
                -NoCoverage:$NoCoverage `
                -RequireConsumedFragments:$RequireConsumedFragments `
                -TestShardOnly:$TestShardOnly `
                -TestShardIndex $TestShardIndex `
                -TestShardCount $TestShardCount
            """);
        return wrapperPath;
    }

    private static void WriteFakeShardPlanner(string fixtureRoot)
    {
        File.WriteAllText(
            Path.Combine(fixtureRoot, "eng", "get-test-shard-plan.ps1"),
            """
            param(
                [Parameter(Mandatory)][string]$TestAssemblyPath,
                [Parameter(Mandatory)][int]$TestShardCount,
                [Parameter(Mandatory)][int]$TestShardIndex
            )

            if ($TestShardCount -ne 2 -or $TestShardIndex -ne 0) {
                throw "Unexpected shard selection $TestShardIndex/$TestShardCount."
            }

            [pscustomobject]@{
                SchemaVersion = 1
                SelectedFilter = 'ClassName=RoslynMcp.Tests.ShardSentinel'
                Shards = @(
                    [pscustomobject]@{ Index = 0; ClassCount = 1; StaticCaseWeight = 1 },
                    [pscustomobject]@{ Index = 1; ClassCount = 1; StaticCaseWeight = 1 }
                )
            } | ConvertTo-Json -Depth 4
            """);
    }

    private static string WriteCleanupDenialWrapper(string fixtureRoot)
    {
        var wrapperPath = Path.Combine(fixtureRoot, "verify-release-with-cleanup-denial.ps1");
        File.WriteAllText(
            wrapperPath,
            """
            param(
                [switch]$NoCoverage,
                [switch]$RequireConsumedFragments,
                [switch]$TestShardOnly,
                [int]$TestShardIndex = 0,
                [int]$TestShardCount = 1
            )

            . (Join-Path $PSScriptRoot 'fake-dotnet.ps1')
            $script:cleanupAttempts = 0
            function global:Remove-Item {
                [CmdletBinding()]
                param(
                    [Parameter(Mandatory)]
                    [string]$LiteralPath,
                    [switch]$Recurse,
                    [switch]$Force
                )

                $script:cleanupAttempts++
                [System.IO.File]::WriteAllText(
                    $env:ROSLYNMCP_CLEANUP_ATTEMPTS,
                    $script:cleanupAttempts.ToString([System.Globalization.CultureInfo]::InvariantCulture))
                [System.IO.File]::WriteAllText($env:ROSLYNMCP_CLEANUP_TARGET, $LiteralPath)

                if ($env:ROSLYNMCP_CLEANUP_MODE -eq 'persistent') {
                    throw [System.UnauthorizedAccessException]::new('fixture cleanup denial')
                }

                if ($script:cleanupAttempts -eq 1) {
                    throw [System.IO.IOException]::new('fixture cleanup denial')
                }

                Microsoft.PowerShell.Management\Remove-Item @PSBoundParameters
            }

            & (Join-Path $PSScriptRoot 'eng/verify-release.ps1') `
                -NoCoverage:$NoCoverage `
                -RequireConsumedFragments:$RequireConsumedFragments `
                -TestShardOnly:$TestShardOnly `
                -TestShardIndex $TestShardIndex `
                -TestShardCount $TestShardCount
            """);
        return wrapperPath;
    }

    private static void AssertPrivateTestRoot(string path)
    {
        var canonicalPath = Path.GetFullPath(path);
        var testRunParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "RoslynMcpTestRuns"));
        var relativePath = Path.GetRelativePath(testRunParent, canonicalPath);
        Assert.IsTrue(
            relativePath.Length == 32 && relativePath.All(Uri.IsHexDigit),
            $"Unexpected private test-root shape: {canonicalPath}");
    }

    private static async Task<PwshResult> RunVerifyReleaseAsync(
        ReleaseFixture fixture,
        bool requireConsumedFragments,
        string? cleanupMode = null,
        bool failDotnetTest = false,
        string? trxMode = null,
        bool testShardOnly = false,
        int testShardIndex = 0,
        int testShardCount = 1)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh",
            WorkingDirectory = fixture.RepositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(fixture.ScriptPath);
        startInfo.ArgumentList.Add("-NoCoverage");
        if (requireConsumedFragments)
        {
            startInfo.ArgumentList.Add("-RequireConsumedFragments");
        }
        if (testShardOnly)
        {
            startInfo.ArgumentList.Add("-TestShardOnly");
        }
        startInfo.ArgumentList.Add("-TestShardIndex");
        startInfo.ArgumentList.Add(testShardIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("-TestShardCount");
        startInfo.ArgumentList.Add(testShardCount.ToString(System.Globalization.CultureInfo.InvariantCulture));

        startInfo.Environment["ROSLYNMCP_DOTNET_SENTINEL"] = fixture.DotnetSentinelPath;
        startInfo.Environment["ROSLYNMCP_DOTNET_ARGUMENTS"] = fixture.DotnetArgumentsPath;
        if (cleanupMode is not null)
        {
            startInfo.Environment["ROSLYNMCP_CLEANUP_MODE"] = cleanupMode;
            startInfo.Environment["ROSLYNMCP_CLEANUP_ATTEMPTS"] = Path.Combine(
                fixture.RepositoryRoot,
                "cleanup-attempts.txt");
            startInfo.Environment["ROSLYNMCP_CLEANUP_TARGET"] = Path.Combine(
                fixture.RepositoryRoot,
                "cleanup-target.txt");
        }

        if (failDotnetTest)
        {
            startInfo.Environment["ROSLYNMCP_DOTNET_TEST_FAIL"] = "1";
        }
        if (trxMode is not null)
        {
            startInfo.Environment["ROSLYNMCP_DOTNET_TRX_MODE"] = trxMode;
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start PowerShell.");
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
            throw new TimeoutException("verify-release fixture did not exit within 30 seconds.");
        }

        return new PwshResult(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private enum FailureMode
    {
        None,
        Exit,
        Throw,
    }

    private sealed record ChildScript(string FileName, string Description);

    private sealed record ReleaseCase(
        string Name,
        string? FailingScript,
        FailureMode FailureMode,
        bool RequireConsumedFragments,
        string? ExpectedText,
        bool ShouldReachDotnet);

    private sealed record ReleaseFixture(
        string ScriptPath,
        string DotnetSentinelPath,
        string DotnetArgumentsPath,
        string RepositoryRoot);

    private sealed record CleanupCase(
        string Name,
        string Mode,
        int ExpectedExitCode,
        int ExpectedAttempts,
        bool ShouldPublish,
        bool FailDotnetTest);

    private sealed record TrxCase(string Mode, string ExpectedText);

    private sealed record PwshResult(int ExitCode, string StdOut, string StdErr);
}
