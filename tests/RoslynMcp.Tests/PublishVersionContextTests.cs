using System.Diagnostics;

namespace RoslynMcp.Tests;

/// <summary>
/// Drives eng/verify-publish-version-context.ps1 out-of-process against a synthetic
/// repository root so the publish-time provenance gate is proven for every GitHub
/// event shape, and asserts the workflow wires it ahead of build/pack.
/// </summary>
[TestClass]
public sealed class PublishVersionContextTests
{
    private const string FixtureVersion = "4.2.1";

    [TestMethod]
    public async Task PushEvent_WithMatchingTag_Succeeds()
    {
        using var fixture = new VersionFixture(FixtureVersion);

        var result = await RunAsync(fixture.Root, "push", refName: $"v{FixtureVersion}");

        Assert.AreEqual(0, result.ExitCode, result.Combined);
        StringAssert.Contains(result.Combined, "Release provenance OK");
    }

    [TestMethod]
    public async Task PushEvent_WithMismatchedTag_FailsNamingBothVersions()
    {
        using var fixture = new VersionFixture(FixtureVersion);

        var result = await RunAsync(fixture.Root, "push", refName: "v0.0.1");

        Assert.AreEqual(1, result.ExitCode, result.Combined);
        StringAssert.Contains(result.Combined, "v0.0.1");
        StringAssert.Contains(result.Combined, FixtureVersion);
    }

    [TestMethod]
    public async Task ReleaseEvent_WithMatchingTagName_Succeeds()
    {
        using var fixture = new VersionFixture(FixtureVersion);

        var result = await RunAsync(fixture.Root, "release", releaseTag: $"v{FixtureVersion}");

        Assert.AreEqual(0, result.ExitCode, result.Combined);
        StringAssert.Contains(result.Combined, "github.event.release.tag_name");
    }

    [TestMethod]
    public async Task ReleaseEvent_WithMismatchedTagName_Fails()
    {
        using var fixture = new VersionFixture(FixtureVersion);

        var result = await RunAsync(fixture.Root, "release", releaseTag: "v9.9.9");

        Assert.AreEqual(1, result.ExitCode, result.Combined);
        StringAssert.Contains(result.Combined, "v9.9.9");
    }

    [TestMethod]
    public async Task ReleaseEvent_IgnoresRefNameAndComparesTagName()
    {
        using var fixture = new VersionFixture(FixtureVersion);

        // A release published from a branch carries ref_name = the branch; only the
        // release tag is authoritative, so a matching ref_name must not rescue a bad tag.
        var result = await RunAsync(
            fixture.Root,
            "release",
            refName: $"v{FixtureVersion}",
            releaseTag: "v0.0.1");

        Assert.AreEqual(1, result.ExitCode, result.Combined);
        StringAssert.Contains(result.Combined, "github.event.release.tag_name");
    }

    [TestMethod]
    [DataRow("push", "refs/heads/main")]
    [DataRow("push", "v3.0")]
    [DataRow("push", "release-4.2.1")]
    public async Task PushEvent_WithMalformedTag_Fails(string eventName, string refName)
    {
        using var fixture = new VersionFixture(FixtureVersion);

        var result = await RunAsync(fixture.Root, eventName, refName: refName);

        Assert.AreEqual(1, result.ExitCode, result.Combined);
        StringAssert.Contains(result.Combined, refName);
    }

    [TestMethod]
    public async Task PushEvent_WithEmptyRefName_Fails()
    {
        using var fixture = new VersionFixture(FixtureVersion);

        var result = await RunAsync(fixture.Root, "push");

        Assert.AreEqual(1, result.ExitCode, result.Combined);
        StringAssert.Contains(result.Combined, "was empty");
    }

    [TestMethod]
    public async Task ReleaseEvent_WithEmptyTagName_Fails()
    {
        using var fixture = new VersionFixture(FixtureVersion);

        var result = await RunAsync(fixture.Root, "release");

        Assert.AreEqual(1, result.ExitCode, result.Combined);
        StringAssert.Contains(result.Combined, "was empty");
    }

    [TestMethod]
    public async Task WorkflowDispatch_WithoutAnyTag_SkipsComparison()
    {
        using var fixture = new VersionFixture(FixtureVersion);

        var result = await RunAsync(fixture.Root, "workflow_dispatch");

        Assert.AreEqual(0, result.ExitCode, result.Combined);
        StringAssert.Contains(result.Combined, "skipping provenance comparison");
        StringAssert.Contains(result.Combined, FixtureVersion);
    }

    [TestMethod]
    public async Task UnversionedTagPrefixIsOptional()
    {
        using var fixture = new VersionFixture(FixtureVersion);

        var result = await RunAsync(fixture.Root, "push", refName: FixtureVersion);

        Assert.AreEqual(0, result.ExitCode, result.Combined);
    }

    [TestMethod]
    public void PublishWorkflow_RunsProvenanceGateBeforeVerifyAndPack()
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var workflow = File.ReadAllText(
            Path.Combine(repositoryRoot, ".github", "workflows", "publish-nuget.yml"));

        var gateIndex = workflow.IndexOf(
            "./eng/verify-publish-version-context.ps1",
            StringComparison.Ordinal);
        var verifyIndex = workflow.IndexOf(
            "- name: Verify release build",
            StringComparison.Ordinal);
        var packIndex = workflow.IndexOf("- name: Pack NuGet package", StringComparison.Ordinal);

        Assert.IsTrue(gateIndex >= 0, "The publish workflow must run the release-provenance gate.");
        Assert.IsTrue(
            verifyIndex > gateIndex,
            "The provenance gate must run before the release build so a mistag fails fast.");
        Assert.IsTrue(
            packIndex > gateIndex,
            "The provenance gate must run before the package is produced.");

        StringAssert.Contains(
            workflow,
            "-EventName '${{ github.event_name }}'",
            "The gate must receive the triggering event name.");
        StringAssert.Contains(
            workflow,
            "-RefName '${{ github.ref_name }}'",
            "The gate must receive the pushed ref name, single-quoted against injection.");
        StringAssert.Contains(
            workflow,
            "-ReleaseTag '${{ github.event.release.tag_name }}'",
            "The gate must receive the published release tag, single-quoted against injection.");
    }

    private static async Task<ProcessResult> RunAsync(
        string repoRoot,
        string eventName,
        string? refName = null,
        string? releaseTag = null)
    {
        var scriptPath = Path.Combine(
            TestFixtureFileSystem.FindRepositoryRoot(),
            "eng",
            "verify-publish-version-context.ps1");

        var startInfo = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("-EventName");
        startInfo.ArgumentList.Add(eventName);
        if (refName is not null)
        {
            startInfo.ArgumentList.Add("-RefName");
            startInfo.ArgumentList.Add(refName);
        }

        if (releaseTag is not null)
        {
            startInfo.ArgumentList.Add("-ReleaseTag");
            startInfo.ArgumentList.Add(releaseTag);
        }

        startInfo.ArgumentList.Add("-RepoRoot");
        startInfo.ArgumentList.Add(repoRoot);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the publish provenance verifier.");
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
            throw new TimeoutException("Publish provenance verifier did not exit within 30 seconds.");
        }

        return new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private sealed record ProcessResult(int ExitCode, string StdOut, string StdErr)
    {
        public string Combined => StdOut + StdErr;
    }

    private sealed class VersionFixture : IDisposable
    {
        public VersionFixture(string version)
        {
            Root = Path.Combine(Path.GetTempPath(), "roslynmcp-provenance-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            File.WriteAllText(
                Path.Combine(Root, "Directory.Build.props"),
                $"<Project>\n  <PropertyGroup>\n    <Version>{version}</Version>\n  </PropertyGroup>\n</Project>\n");
        }

        public string Root { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                    Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort temp cleanup; a locked fixture directory must not fail the test.
            }
        }
    }
}
