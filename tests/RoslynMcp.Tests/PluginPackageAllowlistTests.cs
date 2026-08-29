using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class PluginPackageAllowlistTests
{
    [TestMethod]
    [TestCategory("Process")]
    public async Task VerifyPluginPackageFiles_PassesCanonicalAllowlist()
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var result = await RunVerifierAsync(repoRoot);

        Assert.AreEqual(0, result.ExitCode, result.StdErr + result.StdOut);
        StringAssert.Contains(result.StdOut, "Plugin package allowlist verified");
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task VerifyPluginPackageFiles_RejectsRepoInternalAllowlistEntry()
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var fixtureRoot = CreateFixtureRoot();
        var badAllowlist = Path.Combine(fixtureRoot, "bad-plugin-allowlist.txt");
        File.WriteAllText(badAllowlist, "ai_docs/**");

        try
        {
            var result = await RunVerifierAsync(repoRoot, allowlistPath: badAllowlist);

            Assert.AreNotEqual(0, result.ExitCode,
                "Repo-internal allowlist entries such as ai_docs/** must be rejected.");
            StringAssert.Contains(result.StdErr + result.StdOut, "repo-internal path");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task VerifyPluginPackageFiles_FailsUnexpectedCandidateFile()
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var fixtureRoot = CreateFixtureRoot();
        var candidateList = Path.Combine(fixtureRoot, "plugin-candidates.txt");
        File.WriteAllLines(candidateList, [".claude-plugin/plugin.json", "src/RoslynMcp.Host.Stdio/Program.cs"]);

        try
        {
            var result = await RunVerifierAsync(repoRoot, candidateFileListPath: candidateList);

            Assert.AreNotEqual(0, result.ExitCode,
                "Candidate cache contents must fail when a repo-internal source file is present.");
            StringAssert.Contains(result.StdErr + result.StdOut, "non-allowlisted file");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    private static Task<PwshScriptResult> RunVerifierAsync(
        string repoRoot,
        string? allowlistPath = null,
        string? candidateFileListPath = null)
    {
        var scriptPath = Path.Combine(repoRoot, "eng", "verify-plugin-package-files.ps1");
        var arguments = new List<string>
        {
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            scriptPath,
            "-RepoRoot",
            repoRoot,
        };

        if (allowlistPath is not null)
        {
            arguments.Add("-AllowlistPath");
            arguments.Add(allowlistPath);
        }

        if (candidateFileListPath is not null)
        {
            arguments.Add("-CandidateFileListPath");
            arguments.Add(candidateFileListPath);
        }

        return PwshScriptRunner.RunAsync(
            arguments,
            workingDirectory: repoRoot,
            timeout: TimeSpan.FromSeconds(60),
            description: "plugin package allowlist verifier");
    }

    private static string CreateFixtureRoot()
    {
        var path = Path.Combine(
            TestTempRoot.Current,
            nameof(PluginPackageAllowlistTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
