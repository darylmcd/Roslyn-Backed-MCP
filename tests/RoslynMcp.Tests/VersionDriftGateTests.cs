using System.Diagnostics;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class VersionDriftGateTests
{
    [TestMethod]
    [DataRow(false, "Darylmcd.RoslynMcp@9.8.7", null, 0)]
    [DataRow(false, "Darylmcd.RoslynMcp@9.8.6", null, 1)]
    [DataRow(false, "Darylmcd.RoslynMcp@9.8.7", "Darylmcd.RoslynMcp@9.8.6", 1)]
    [DataRow(true, null, null, 1)]
    public void VerifyVersionDrift_ValidatesPluginDnxPin(
        bool omitArgs,
        string? packagePin,
        string? extraPackagePin,
        int expectedExitCode)
    {
        var sourceRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var fixture = Path.Combine(Path.GetTempPath(), $"roslynmcp-version-drift-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(fixture, "eng"));
        Directory.CreateDirectory(Path.Combine(fixture, ".claude-plugin"));
        try
        {
            File.Copy(
                Path.Combine(sourceRoot, "eng", "verify-version-drift.ps1"),
                Path.Combine(fixture, "eng", "verify-version-drift.ps1"));
            File.WriteAllText(Path.Combine(fixture, "Directory.Build.props"),
                "<Project><PropertyGroup><Version>9.8.7</Version></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(fixture, "manifest.json"), "{\"version\":\"9.8.7\"}");
            File.WriteAllText(Path.Combine(fixture, ".claude-plugin", "plugin.json"),
                "{\"version\":\"9.8.7\"}");
            File.WriteAllText(Path.Combine(fixture, ".claude-plugin", "marketplace.json"),
                "{\"plugins\":[{\"version\":\"9.8.7\"}]}");
            var argsJson = omitArgs
                ? string.Empty
                : $",\"args\":[\"{packagePin}\",\"--source\",\"https://api.nuget.org/v3/index.json\"" +
                  (extraPackagePin is null ? "]" : $",\"{extraPackagePin}\"]");
            File.WriteAllText(Path.Combine(fixture, ".claude-plugin", "mcp.json"),
                $"{{\"roslyn\":{{\"command\":\"dnx\"{argsJson}}}}}");
            File.WriteAllText(Path.Combine(fixture, ".claude-plugin", "server.json"),
                "{\"version\":\"9.8.7\",\"packages\":[{\"version\":\"9.8.7\"}]}");
            File.WriteAllText(Path.Combine(fixture, "CHANGELOG.md"), "## [9.8.7] - 2026-08-21");

            var result = RunVerifier(fixture);

            Assert.AreEqual(expectedExitCode, result.ExitCode, result.StdErr + result.StdOut);
            if (expectedExitCode != 0)
            {
                StringAssert.Contains(result.StdErr + result.StdOut, ".claude-plugin/mcp.json package pin");
            }
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixture);
        }
    }

    private static (int ExitCode, string StdOut, string StdErr) RunVerifier(string root)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "pwsh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(Path.Combine(root, "eng", "verify-version-drift.ps1"));
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start PowerShell.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout, stderr);
    }
}
