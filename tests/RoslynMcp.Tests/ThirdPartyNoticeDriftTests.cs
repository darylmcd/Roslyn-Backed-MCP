using System.Diagnostics;
using System.Text.Json;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ThirdPartyNoticeDriftTests
{
    [TestMethod]
    public void NoticeGenerator_HasNoLiveRegistryDependency()
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(repositoryRoot, "eng", "update-third-party-notices.ps1"));

        Assert.IsFalse(script.Contains("Invoke-WebRequest", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(script.Contains("Invoke-RestMethod", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(script.Contains("VerifyLicenseFromNuGet", StringComparison.Ordinal));
        StringAssert.Contains(script, "Get-RestoredNuspecPath");

        var releaseGate = File.ReadAllText(Path.Combine(repositoryRoot, "eng", "verify-release.ps1"));
        StringAssert.Contains(releaseGate, "VerifyRestoredLicenses = $true");
    }

    [TestMethod]
    [TestCategory("Process")]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task VerifyMode_UsesEveryCentralPin_AndRejectsNonMcpLicenseDrift()
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var packages = ReadCentralPackages(repositoryRoot);
        var mcpVersion = packages.Single(package => package.Id == "ModelContextProtocol").Version;
        var restoredPackagesRoot = FindRestoredPackagesRoot(repositoryRoot, packages);
        var fixtureRoot = Path.Combine(TestTempRoot.Current, "ThirdPartyNotices", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixtureRoot);

        try
        {
            var packagesPath = Path.Combine(fixtureRoot, "Directory.Packages.props");
            File.Copy(Path.Combine(repositoryRoot, "Directory.Packages.props"), packagesPath);
            File.Copy(Path.Combine(repositoryRoot, "THIRD-PARTY-NOTICES.md"), Path.Combine(fixtureRoot, "THIRD-PARTY-NOTICES.md"));

            var current = await RunVerifierAsync(repositoryRoot, fixtureRoot, restoredPackagesRoot);
            Assert.AreEqual(0, current.ExitCode, current.AllOutput);

            var packagesText = await File.ReadAllTextAsync(packagesPath);
            packagesText = packagesText.Replace(
                $"<PackageVersion Include=\"ModelContextProtocol\" Version=\"{mcpVersion}\" />",
                "<PackageVersion Include=\"ModelContextProtocol\" Version=\"99.0.0-test\" />",
                StringComparison.Ordinal);
            await File.WriteAllTextAsync(packagesPath, packagesText);

            var pinDrift = await RunVerifierAsync(repositoryRoot, fixtureRoot, restoredPackagesRoot);
            Assert.AreNotEqual(0, pinDrift.ExitCode, "Intentional central-pin drift must fail verification.");
            StringAssert.Contains(pinDrift.AllOutput, "Unable to read authoritative restored package metadata");

            File.Copy(Path.Combine(repositoryRoot, "Directory.Packages.props"), packagesPath, overwrite: true);
            var fixturePackagesRoot = Path.Combine(fixtureRoot, "packages");
            foreach (var package in packages)
            {
                var sourceNuspec = GetNuspecPath(restoredPackagesRoot, package);
                var fixtureNuspec = GetNuspecPath(fixturePackagesRoot, package);
                Directory.CreateDirectory(Path.GetDirectoryName(fixtureNuspec)!);
                File.Copy(sourceNuspec, fixtureNuspec);
            }

            var diffPlex = packages.Single(package => package.Id == "DiffPlex");
            var mutatedNuspec = GetNuspecPath(fixturePackagesRoot, diffPlex);
            var nuspec = System.Xml.Linq.XDocument.Load(mutatedNuspec);
            var license = nuspec.Descendants().Single(element => element.Name.LocalName == "license");
            Assert.AreEqual("Apache-2.0", license.Value, "Restored DiffPlex metadata must prove the reviewed baseline license.");
            license.Value = "MIT";
            nuspec.Save(mutatedNuspec);

            var licenseDrift = await RunVerifierAsync(repositoryRoot, fixtureRoot, fixturePackagesRoot);
            Assert.AreNotEqual(0, licenseDrift.ExitCode, "Authoritative license drift must fail verification.");
            StringAssert.Contains(licenseDrift.AllOutput, "declares license 'MIT'");
            StringAssert.Contains(licenseDrift.AllOutput, "reviewed attribution");
            StringAssert.Contains(licenseDrift.AllOutput, "declares 'Apache-2.0'");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    private static CentralPackage[] ReadCentralPackages(string repositoryRoot)
    {
        var packages = System.Xml.Linq.XDocument.Load(Path.Combine(repositoryRoot, "Directory.Packages.props"));
        return packages.Descendants("PackageVersion")
            .Select(element => new CentralPackage(
                element.Attribute("Include")?.Value
                    ?? throw new InvalidOperationException("A central package pin has no Include value."),
                element.Attribute("Version")?.Value
                    ?? throw new InvalidOperationException("A central package pin has no Version value.")))
            .ToArray();
    }

    private static string FindRestoredPackagesRoot(
        string repositoryRoot,
        IReadOnlyCollection<CentralPackage> packages)
    {
        var assetsPath = Path.Combine(repositoryRoot, "tests", "RoslynMcp.Tests", "obj", "project.assets.json");
        Assert.IsTrue(File.Exists(assetsPath), "Release restore must produce the test project's assets file.");
        using var assets = JsonDocument.Parse(File.ReadAllText(assetsPath));
        var packagesRoot = assets.RootElement.GetProperty("packageFolders")
            .EnumerateObject()
            .Select(folder => folder.Name)
            .FirstOrDefault(folder => packages.All(package => File.Exists(GetNuspecPath(folder, package))));
        Assert.IsNotNull(
            packagesRoot,
            "The restored graph must resolve every exact central-pin nuspec from one effective package root.");

        return packagesRoot;
    }

    private static string GetNuspecPath(string packagesRoot, CentralPackage package) =>
        Path.Combine(
            packagesRoot,
            package.Id.ToLowerInvariant(),
            package.Version.ToLowerInvariant(),
            package.Id.ToLowerInvariant() + ".nuspec");

    private static async Task<PwshResult> RunVerifierAsync(
        string repositoryRoot,
        string fixtureRoot,
        string packageMetadataRoot)
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
        startInfo.ArgumentList.Add("-PackageMetadataRoot");
        startInfo.ArgumentList.Add(packageMetadataRoot);
        startInfo.ArgumentList.Add("-Verify");
        startInfo.ArgumentList.Add("-VerifyRestoredLicenses");

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
        public string AllOutput => PowerShellOutputNormalizer.Normalize(StdOut + Environment.NewLine + StdErr);
    }

    private sealed record CentralPackage(string Id, string Version);
}
