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
        StringAssert.Contains(script, "VerifyLicenseFromNuGet");
    }

    [TestMethod]
    [TestCategory("Process")]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task VerifyMode_UsesRestoredMetadata_AndRejectsPinOrLicenseDrift()
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var mcpVersion = ReadCentralMcpVersion(repositoryRoot);
        var restoredPackagesRoot = FindRestoredPackagesRoot(repositoryRoot, mcpVersion);
        var fixtureRoot = Path.Combine(TestTempRoot.Current, "ThirdPartyNotices", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixtureRoot);

        try
        {
            var packagesPath = Path.Combine(fixtureRoot, "Directory.Packages.props");
            File.Copy(Path.Combine(repositoryRoot, "Directory.Packages.props"), packagesPath);
            File.Copy(Path.Combine(repositoryRoot, "THIRD-PARTY-NOTICES.md"), Path.Combine(fixtureRoot, "THIRD-PARTY-NOTICES.md"));

            var current = await RunVerifierAsync(repositoryRoot, fixtureRoot, restoredPackagesRoot);
            Assert.AreEqual(0, current.ExitCode, current.AllOutput);

            var packages = await File.ReadAllTextAsync(packagesPath);
            packages = packages.Replace(
                $"<PackageVersion Include=\"ModelContextProtocol\" Version=\"{mcpVersion}\" />",
                "<PackageVersion Include=\"ModelContextProtocol\" Version=\"99.0.0-test\" />",
                StringComparison.Ordinal);
            await File.WriteAllTextAsync(packagesPath, packages);

            var pinDrift = await RunVerifierAsync(repositoryRoot, fixtureRoot, restoredPackagesRoot);
            Assert.AreNotEqual(0, pinDrift.ExitCode, "Intentional central-pin drift must fail verification.");
            StringAssert.Contains(pinDrift.AllOutput, "Unable to read authoritative restored package metadata");

            File.Copy(Path.Combine(repositoryRoot, "Directory.Packages.props"), packagesPath, overwrite: true);
            var fixturePackagesRoot = Path.Combine(fixtureRoot, "packages");
            var sourceNuspec = GetMcpNuspecPath(restoredPackagesRoot, mcpVersion);
            var fixtureNuspec = GetMcpNuspecPath(fixturePackagesRoot, mcpVersion);
            Directory.CreateDirectory(Path.GetDirectoryName(fixtureNuspec)!);
            File.Copy(sourceNuspec, fixtureNuspec);

            var nuspec = System.Xml.Linq.XDocument.Load(fixtureNuspec);
            var license = nuspec.Descendants().Single(element => element.Name.LocalName == "license");
            Assert.AreEqual("Apache-2.0", license.Value, "Restored MCP metadata must prove the reviewed baseline license.");
            license.Value = "MIT";
            nuspec.Save(fixtureNuspec);

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

    private static string ReadCentralMcpVersion(string repositoryRoot)
    {
        var packages = System.Xml.Linq.XDocument.Load(Path.Combine(repositoryRoot, "Directory.Packages.props"));
        return packages.Descendants("PackageVersion")
            .Single(element => string.Equals((string?)element.Attribute("Include"), "ModelContextProtocol", StringComparison.Ordinal))
            .Attribute("Version")?.Value
            ?? throw new InvalidOperationException("The central ModelContextProtocol pin has no Version value.");
    }

    private static string FindRestoredPackagesRoot(string repositoryRoot, string mcpVersion)
    {
        var assetsPath = Path.Combine(repositoryRoot, "tests", "RoslynMcp.Tests", "obj", "project.assets.json");
        Assert.IsTrue(File.Exists(assetsPath), "Release restore must produce the test project's assets file.");
        using var assets = JsonDocument.Parse(File.ReadAllText(assetsPath));
        Assert.IsTrue(
            assets.RootElement.GetProperty("libraries").TryGetProperty($"ModelContextProtocol/{mcpVersion}", out _),
            $"The restored test graph must resolve the central MCP pin {mcpVersion}.");
        var packagesRoot = assets.RootElement.GetProperty("packageFolders")
            .EnumerateObject()
            .Select(folder => folder.Name)
            .FirstOrDefault(folder => File.Exists(GetMcpNuspecPath(folder, mcpVersion)));
        Assert.IsNotNull(
            packagesRoot,
            "The restored graph must resolve the exact MCP nuspec from an effective package root.");

        var nuspecPath = GetMcpNuspecPath(packagesRoot, mcpVersion);
        Assert.IsTrue(
            File.Exists(nuspecPath),
            $"Release restore must materialize authoritative MCP package metadata at '{nuspecPath}'.");
        return packagesRoot;
    }

    private static string GetMcpNuspecPath(string packagesRoot, string version) =>
        Path.Combine(packagesRoot, "modelcontextprotocol", version, "modelcontextprotocol.nuspec");

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
}
