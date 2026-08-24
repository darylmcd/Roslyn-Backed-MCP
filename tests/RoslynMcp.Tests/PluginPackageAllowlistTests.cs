using System.Diagnostics;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class PluginPackageAllowlistTests
{
    [TestMethod]
    public void VerifyPluginPackageFiles_PassesCanonicalAllowlist()
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var result = RunVerifier(repoRoot);

        Assert.AreEqual(0, result.ExitCode, result.StdErr + result.StdOut);
        StringAssert.Contains(result.StdOut, "Plugin package allowlist verified");
    }

    [TestMethod]
    public void VerifyPluginPackageFiles_RejectsRepoInternalAllowlistEntry()
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var badAllowlist = Path.Combine(Path.GetTempPath(), $"bad-plugin-allowlist-{Guid.NewGuid():N}.txt");
        File.WriteAllText(badAllowlist, "ai_docs/**");

        try
        {
            var result = RunVerifier(repoRoot, allowlistPath: badAllowlist);

            Assert.AreNotEqual(0, result.ExitCode,
                "Repo-internal allowlist entries such as ai_docs/** must be rejected.");
            StringAssert.Contains(result.StdErr + result.StdOut, "repo-internal path");
        }
        finally
        {
            File.Delete(badAllowlist);
        }
    }

    [TestMethod]
    public void VerifyPluginPackageFiles_FailsUnexpectedCandidateFile()
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var candidateList = Path.Combine(Path.GetTempPath(), $"plugin-candidates-{Guid.NewGuid():N}.txt");
        File.WriteAllLines(candidateList, [".claude-plugin/plugin.json", "src/RoslynMcp.Host.Stdio/Program.cs"]);

        try
        {
            var result = RunVerifier(repoRoot, candidateFileListPath: candidateList);

            Assert.AreNotEqual(0, result.ExitCode,
                "Candidate cache contents must fail when a repo-internal source file is present.");
            StringAssert.Contains(result.StdErr + result.StdOut, "non-allowlisted file");
        }
        finally
        {
            File.Delete(candidateList);
        }
    }

    private static PwshResult RunVerifier(
        string repoRoot,
        string? allowlistPath = null,
        string? candidateFileListPath = null)
    {
        var scriptPath = Path.Combine(repoRoot, "eng", "verify-plugin-package-files.ps1");
        var startInfo = new ProcessStartInfo
        {
            FileName = ResolvePwsh(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("-RepoRoot");
        startInfo.ArgumentList.Add(repoRoot);

        if (allowlistPath is not null)
        {
            startInfo.ArgumentList.Add("-AllowlistPath");
            startInfo.ArgumentList.Add(allowlistPath);
        }

        if (candidateFileListPath is not null)
        {
            startInfo.ArgumentList.Add("-CandidateFileListPath");
            startInfo.ArgumentList.Add(candidateFileListPath);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start PowerShell.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new PwshResult(process.ExitCode, stdout, stderr);
    }

    private static string ResolvePwsh() => OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh";

    private sealed record PwshResult(int ExitCode, string StdOut, string StdErr);
}
