using System.Diagnostics;

namespace RoslynMcp.Tests;

/// <summary>
/// Process-level contract tests for the manual NuGet publisher
/// (<c>eng/publish-nuget.ps1</c>). Each test copies the real script into a
/// sandbox fixture, stubs its child dependencies (<c>verify-release.ps1</c>
/// and the <c>dotnet</c> CLI), and drives it end-to-end through
/// <c>pwsh -NoProfile -File</c> to pin the provenance guarantees:
/// gate-before-pack ordering, canonical-version assertion, fresh-pack staging
/// (a stale <c>nupkg/</c> artifact is never selected), <c>-NoPush</c> dry-run
/// behavior, safe push-argument construction, and push-failure propagation.
/// </summary>
[TestClass]
public sealed class ManualNuGetPublishContractTests
{
    private const string PackageFileName = "Darylmcd.RoslynMcp.9.9.9.nupkg";
    private const string FixtureApiKey = "fixture-not-a-secret-key";
    private const string GateInvokedMarker = "release-gate-invoked";

    [TestMethod]
    [TestCategory("Process")]
    public async Task MissingApiKey_FailsFastBeforeReleaseGate()
    {
        using var fixture = PublisherFixture.Create();
        var result = await fixture.RunAsync(apiKey: null);

        Assert.AreNotEqual(0, result.ExitCode, result.Output);
        StringAssert.Contains(result.Output, "NUGET_API_KEY");
        Assert.IsFalse(
            result.Output.Contains(GateInvokedMarker, StringComparison.Ordinal),
            "A missing API key must fail before the ~10-15 minute release gate runs.");
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task ReleaseGateFailure_AbortsBeforeAnyDotnetInvocation()
    {
        using var fixture = PublisherFixture.Create(gateExitCode: 37);
        var result = await fixture.RunAsync();

        Assert.AreNotEqual(0, result.ExitCode, result.Output);
        StringAssert.Contains(result.Output, "Release validation failed with exit code 37.");
        Assert.IsFalse(result.Output.Contains("Done.", StringComparison.Ordinal), result.Output);
        Assert.AreEqual(
            0,
            fixture.DotnetInvocations.Count,
            "No dotnet pack/push may run after the release gate fails.");
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task NoPush_PacksFreshIntoStagingAndNeverPushes()
    {
        using var fixture = PublisherFixture.Create();
        var result = await fixture.RunAsync(noPush: true);

        Assert.AreEqual(0, result.ExitCode, result.Output);
        StringAssert.Contains(result.Output, "Validate-only run complete (-NoPush)");
        Assert.IsFalse(result.Output.Contains("Done.", StringComparison.Ordinal), result.Output);

        var packInvocation = fixture.DotnetInvocations.SingleOrDefault(
            line => line.StartsWith("pack ", StringComparison.Ordinal));
        Assert.IsNotNull(packInvocation, "A -NoPush run must still pack the package.");
        StringAssert.Contains(
            packInvocation,
            Path.Combine("artifacts", "publish-nuget", "9.9.9"),
            "The pack output must be the owned per-version staging directory.");
        Assert.IsFalse(
            fixture.DotnetInvocations.Any(
                line => line.StartsWith("nuget push", StringComparison.Ordinal)),
            "-NoPush must never invoke dotnet nuget push.");
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task Push_UsesFreshlyPackedStagingArtifactNotTheStaleNupkgDirectory()
    {
        using var fixture = PublisherFixture.Create();
        var result = await fixture.RunAsync();

        Assert.AreEqual(0, result.ExitCode, result.Output);
        StringAssert.Contains(result.Output, "Done.");

        var pushInvocation = fixture.DotnetInvocations.Single(
            line => line.StartsWith("nuget push", StringComparison.Ordinal));
        StringAssert.Contains(
            pushInvocation,
            Path.Combine("artifacts", "publish-nuget", "9.9.9", PackageFileName),
            "The pushed package must be the freshly packed staging artifact.");
        Assert.IsFalse(
            pushInvocation.Contains(
                Path.Combine("nupkg", PackageFileName),
                StringComparison.Ordinal),
            "A pre-existing package in nupkg/ must never be selected for push.");
        StringAssert.Contains(pushInvocation, "--source");
        StringAssert.Contains(pushInvocation, "--api-key");
        StringAssert.Contains(pushInvocation, "--skip-duplicate");
        Assert.IsFalse(
            result.Output.Contains(FixtureApiKey, StringComparison.Ordinal),
            "The API key value must never be echoed to stdout/stderr.");
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task PushFailure_PropagatesNonzeroExitAndSuppressesDone()
    {
        using var fixture = PublisherFixture.Create(pushExitCode: 41);
        var result = await fixture.RunAsync();

        Assert.AreNotEqual(0, result.ExitCode, result.Output);
        StringAssert.Contains(
            PowerShellOutputNormalizer.Normalize(result.Output),
            "dotnet nuget push failed with exit code 41.");
        Assert.IsFalse(
            result.Output.Contains("Done.", StringComparison.Ordinal),
            "A failed push must never report Done.");
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task VersionParameter_IsAnAssertionThatRejectsMismatch()
    {
        using var fixture = PublisherFixture.Create();
        var result = await fixture.RunAsync(versionArgument: "1.2.3");

        Assert.AreNotEqual(0, result.ExitCode, result.Output);
        StringAssert.Contains(
            PowerShellOutputNormalizer.Normalize(result.Output),
            "disagrees with the canonical Directory.Build.props version 9.9.9");
        Assert.IsFalse(
            fixture.DotnetInvocations.Any(
                line => line.StartsWith("pack ", StringComparison.Ordinal)),
            "A mismatched -Version must abort before packing.");
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task PackProducingWrongFileName_FailsTheIdentityAssertion()
    {
        using var fixture = PublisherFixture.Create(
            packedFileName: "Darylmcd.RoslynMcp.0.0.1.nupkg");
        var result = await fixture.RunAsync(noPush: true);

        Assert.AreNotEqual(0, result.ExitCode, result.Output);
        StringAssert.Contains(
            PowerShellOutputNormalizer.Normalize(result.Output),
            "did not produce the expected Darylmcd.RoslynMcp.9.9.9.nupkg");
        Assert.IsFalse(
            fixture.DotnetInvocations.Any(
                line => line.StartsWith("nuget push", StringComparison.Ordinal)),
            "A failed package-identity assertion must abort before push.");
    }

    private sealed class PublisherFixture : IDisposable
    {
        private PublisherFixture(string root, string binDirectory, string scriptPath)
        {
            Root = root;
            BinDirectory = binDirectory;
            ScriptPath = scriptPath;
        }

        private string Root { get; }

        private string BinDirectory { get; }

        private string ScriptPath { get; }

        private string InvocationLogPath => Path.Combine(BinDirectory, "dotnet-invocations.log");

        public IReadOnlyList<string> DotnetInvocations =>
            File.Exists(InvocationLogPath)
                ? File.ReadAllLines(InvocationLogPath)
                : Array.Empty<string>();

        public static PublisherFixture Create(
            int gateExitCode = 0,
            int pushExitCode = 0,
            string packedFileName = PackageFileName)
        {
            var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
            var root = Path.Combine(
                TestTempRoot.Current,
                nameof(ManualNuGetPublishContractTests),
                Guid.NewGuid().ToString("N"));
            var engDirectory = Path.Combine(root, "eng");
            var binDirectory = Path.Combine(root, "bin");
            var stalePackageDirectory = Path.Combine(root, "nupkg");
            Directory.CreateDirectory(engDirectory);
            Directory.CreateDirectory(binDirectory);
            Directory.CreateDirectory(stalePackageDirectory);

            var scriptPath = Path.Combine(engDirectory, "publish-nuget.ps1");
            File.Copy(Path.Combine(repositoryRoot, "eng", "publish-nuget.ps1"), scriptPath);

            // Stub release gate: prints a marker (so fail-fast tests can prove it
            // never ran) then exits with the configured code.
            File.WriteAllText(
                Path.Combine(engDirectory, "verify-release.ps1"),
                $"Write-Host '{GateInvokedMarker}'\nexit {gateExitCode}\n");

            // Canonical version source the script derives from.
            File.WriteAllText(
                Path.Combine(root, "Directory.Build.props"),
                """
                <Project>
                  <PropertyGroup>
                    <Version>9.9.9</Version>
                  </PropertyGroup>
                </Project>
                """);

            // Stale decoy that the legacy script would have pushed verbatim.
            File.WriteAllText(
                Path.Combine(stalePackageDirectory, PackageFileName),
                "stale package from an earlier release");

            // dotnet stub: logs every invocation to a capture file (never stdout,
            // so the api key cannot leak into the process output under test),
            // materializes the configured .nupkg on pack, and honors the
            // configured push exit code.
            File.WriteAllText(
                Path.Combine(binDirectory, "dotnet-stub.ps1"),
                $$"""
                Set-StrictMode -Version Latest
                Add-Content -Path (Join-Path $PSScriptRoot 'dotnet-invocations.log') -Value ($args -join ' ')
                if ($args.Count -ge 1 -and $args[0] -eq 'pack') {
                    $outIndex = [Array]::IndexOf($args, '-o')
                    if ($outIndex -ge 0 -and ($outIndex + 1) -lt $args.Count) {
                        $outDirectory = $args[$outIndex + 1]
                        New-Item -ItemType Directory -Force -Path $outDirectory | Out-Null
                        Set-Content -Path (Join-Path $outDirectory '{{packedFileName}}') -Value 'stub package'
                    }
                    exit 0
                }
                if ($args.Count -ge 2 -and $args[0] -eq 'nuget' -and $args[1] -eq 'push') {
                    exit {{pushExitCode}}
                }
                exit 0
                """);

            if (OperatingSystem.IsWindows())
            {
                File.WriteAllText(
                    Path.Combine(binDirectory, "dotnet.cmd"),
                    "@echo off\r\npwsh -NoProfile -File \"%~dp0dotnet-stub.ps1\" %*\r\nexit /b %ERRORLEVEL%\r\n");
            }
            else
            {
                var shimPath = Path.Combine(binDirectory, "dotnet");
                File.WriteAllText(
                    shimPath,
                    "#!/bin/sh\nexec pwsh -NoProfile -File \"$(dirname \"$0\")/dotnet-stub.ps1\" \"$@\"\n");
                File.SetUnixFileMode(
                    shimPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }

            return new PublisherFixture(root, binDirectory, scriptPath);
        }

        public async Task<PublisherResult> RunAsync(
            string? apiKey = FixtureApiKey,
            bool noPush = false,
            string? versionArgument = null)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Root,
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(ScriptPath);
            if (noPush)
            {
                startInfo.ArgumentList.Add("-NoPush");
            }

            if (versionArgument is not null)
            {
                startInfo.ArgumentList.Add("-Version");
                startInfo.ArgumentList.Add(versionArgument);
            }

            // Prepend the stub bin so `dotnet` resolves to the fixture shim.
            var pathVariableName = OperatingSystem.IsWindows() ? "Path" : "PATH";
            startInfo.Environment[pathVariableName] =
                BinDirectory + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
            if (apiKey is not null)
            {
                startInfo.Environment["NUGET_API_KEY"] = apiKey;
            }
            else
            {
                startInfo.Environment.Remove("NUGET_API_KEY");
            }

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start the manual publisher fixture.");
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException("Manual publisher fixture did not exit within 60 seconds.");
            }

            return new PublisherResult(process.ExitCode, await stdoutTask + await stderrTask);
        }

        public void Dispose() => TestFixtureFileSystem.DeleteDirectoryIfExists(Root);
    }

    private sealed record PublisherResult(int ExitCode, string Output);
}
