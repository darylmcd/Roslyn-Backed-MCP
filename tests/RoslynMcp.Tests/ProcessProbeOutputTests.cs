using System.Text;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ProcessProbeOutputTests
{
    [TestMethod]
    [TestCategory("Process")]
    public async Task CopyTargets_UseReferencedProjectsCustomOutputAndRuntimeCompanions()
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var fixtureRoot = Path.Combine(Path.GetTempPath(), $"roslynmcp-probe-output-{Guid.NewGuid():N}");
        var customOutput = Path.Combine(fixtureRoot, "custom");
        Directory.CreateDirectory(customOutput);
        try
        {
            foreach (var probe in new[] { "GitFixtureWorkerProbe", "ProcessPipeWorkerProbe" })
            {
                foreach (var suffix in new[] { ".dll", ".deps.json", ".runtimeconfig.json" })
                {
                    await File.WriteAllTextAsync(Path.Combine(customOutput, probe + suffix),
                        $"fresh-{probe}{suffix}");
                }
            }

            var result = await RunCopyTargetsAsync(repositoryRoot, customOutput,
                "CopyGitFixtureWorkerProbe,CopyProcessPipeWorkerProbe");
            Assert.AreEqual(0, result.ExitCode, result.AllOutput);

            foreach (var probe in new[] { "GitFixtureWorkerProbe", "ProcessPipeWorkerProbe" })
            {
                foreach (var suffix in new[] { ".dll", ".deps.json", ".runtimeconfig.json" })
                {
                    var copied = Path.Combine(customOutput, "ProcessProbes", probe, probe + suffix);
                    Assert.IsTrue(
                        (await File.ReadAllBytesAsync(copied)).SequenceEqual(
                            Encoding.UTF8.GetBytes($"fresh-{probe}{suffix}")),
                        $"Copy target did not use the custom output for {probe}{suffix}: {copied}");
                }
            }
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    [TestMethod]
    [TestCategory("Process")]
    [DataRow("CopyGitFixtureWorkerProbe", "Git fixture worker probe output is missing")]
    [DataRow("CopyProcessPipeWorkerProbe", "Process pipe worker probe output is missing")]
    public async Task CopyTargets_RejectMissingReferencedOutput(string target, string diagnostic)
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var fixtureRoot = Path.Combine(Path.GetTempPath(), $"roslynmcp-probe-output-{Guid.NewGuid():N}");
        Directory.CreateDirectory(fixtureRoot);
        try
        {
            var result = await RunCopyTargetsAsync(repositoryRoot, fixtureRoot, target);
            Assert.AreNotEqual(0, result.ExitCode, result.AllOutput);
            StringAssert.Contains(result.AllOutput, diagnostic);
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    [TestMethod]
    [TestCategory("Process")]
    [DataRow("CopyGitFixtureWorkerProbe", "GitFixtureWorkerProbe", ".deps.json")]
    [DataRow("CopyGitFixtureWorkerProbe", "GitFixtureWorkerProbe", ".runtimeconfig.json")]
    [DataRow("CopyProcessPipeWorkerProbe", "ProcessPipeWorkerProbe", ".deps.json")]
    [DataRow("CopyProcessPipeWorkerProbe", "ProcessPipeWorkerProbe", ".runtimeconfig.json")]
    public async Task CopyTargets_RejectMissingRuntimeCompanion(string target, string probe, string missingSuffix)
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var fixtureRoot = Path.Combine(Path.GetTempPath(), $"roslynmcp-probe-output-{Guid.NewGuid():N}");
        Directory.CreateDirectory(fixtureRoot);
        try
        {
            foreach (var suffix in new[] { ".dll", ".deps.json", ".runtimeconfig.json" })
            {
                if (suffix != missingSuffix)
                {
                    await File.WriteAllTextAsync(Path.Combine(fixtureRoot, probe + suffix),
                        $"fresh-{probe}{suffix}");
                }
            }

            var result = await RunCopyTargetsAsync(repositoryRoot, fixtureRoot, target);
            Assert.AreNotEqual(0, result.ExitCode, $"Missing {probe}{missingSuffix} must fail. {result.AllOutput}");
            StringAssert.Contains(result.AllOutput, probe + missingSuffix);
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    private static Task<PwshScriptResult> RunCopyTargetsAsync(string repositoryRoot, string output, string targets) =>
        PwshScriptRunner.RunExecutableAsync(
            "dotnet",
            ["msbuild", "tests/RoslynMcp.Tests/RoslynMcp.Tests.csproj",
             $"-target:{targets}", $"-property:OutDir={output}{Path.DirectorySeparatorChar}", "-nologo"],
            environment: null,
            workingDirectory: repositoryRoot,
            timeout: TimeSpan.FromMinutes(2),
            description: "process-probe output target regression");
}
