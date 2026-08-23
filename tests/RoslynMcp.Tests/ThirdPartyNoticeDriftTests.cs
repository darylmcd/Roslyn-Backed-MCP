using System.Diagnostics;

namespace RoslynMcp.Tests;

[TestClass]
[TestCategory("Process")]
public sealed class ThirdPartyNoticeDriftTests
{
    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task VerifyMode_AcceptsCurrentInventory_AndRejectsIntentionalPinDrift()
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var fixtureRoot = Path.Combine(Path.GetTempPath(), "RoslynMcpThirdPartyNotices", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixtureRoot);

        try
        {
            var packagesPath = Path.Combine(fixtureRoot, "Directory.Packages.props");
            File.Copy(Path.Combine(repositoryRoot, "Directory.Packages.props"), packagesPath);
            File.Copy(Path.Combine(repositoryRoot, "THIRD-PARTY-NOTICES.md"), Path.Combine(fixtureRoot, "THIRD-PARTY-NOTICES.md"));

            var current = await RunVerifierAsync(repositoryRoot, fixtureRoot);
            Assert.AreEqual(0, current.ExitCode, current.AllOutput);

            var packages = await File.ReadAllTextAsync(packagesPath);
            packages = packages.Replace(
                "<PackageVersion Include=\"ModelContextProtocol\" Version=\"2.1.0\" />",
                "<PackageVersion Include=\"ModelContextProtocol\" Version=\"99.0.0-test\" />",
                StringComparison.Ordinal);
            await File.WriteAllTextAsync(packagesPath, packages);

            var drifted = await RunVerifierAsync(repositoryRoot, fixtureRoot);
            Assert.AreNotEqual(0, drifted.ExitCode, "Intentional central-pin drift must fail verification.");
            StringAssert.Contains(drifted.AllOutput, "THIRD-PARTY-NOTICES.md is stale");
        }
        finally
        {
            Directory.Delete(fixtureRoot, recursive: true);
        }
    }

    private static async Task<PwshResult> RunVerifierAsync(string repositoryRoot, string fixtureRoot)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(Path.Combine(repositoryRoot, "eng", "update-third-party-notices.ps1"));
        startInfo.ArgumentList.Add("-RepoRoot");
        startInfo.ArgumentList.Add(fixtureRoot);
        startInfo.ArgumentList.Add("-Verify");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start third-party notice verifier.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("Third-party notice verifier timed out after 30 seconds.");
        }

        return new(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private sealed record PwshResult(int ExitCode, string StdOut, string StdErr)
    {
        public string AllOutput => StdOut + Environment.NewLine + StdErr;
    }
}
