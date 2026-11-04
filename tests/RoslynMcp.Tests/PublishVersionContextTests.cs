using System.Diagnostics;

using RoslynMcp.Tests.Helpers;
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
    [TestCategory("Process")]
    public async Task TimedOutVerifier_ExitsAndDrainsBothStreamsBeforeFixtureDeletion()
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(PublishVersionContextTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var pidPath = Path.Combine(root, "verifier.pid");
        try
        {
            var scriptPath = Path.Combine(root, "blocked-verifier.ps1");
            await File.WriteAllTextAsync(scriptPath, """
                param([string]$PidPath)
                [IO.File]::WriteAllText($PidPath, [string]$PID)
                [Console]::Out.WriteLine('publish-stdout-sentinel')
                [Console]::Error.WriteLine('publish-stderr-sentinel')
                Start-Sleep -Seconds 30
                """);
            var failure = await Assert.ThrowsExactlyAsync<TimeoutException>(() =>
                RunVerifierProcessAsync(["-NoProfile", "-File", scriptPath, pidPath], TimeSpan.FromSeconds(3)));
            StringAssert.Contains(failure.Message, "publish-stdout-sentinel");
            StringAssert.Contains(failure.Message, "publish-stderr-sentinel");
            Assert.IsTrue(File.Exists(pidPath), "The verifier must start before its timeout.");
            var pid = int.Parse(await File.ReadAllTextAsync(pidPath), System.Globalization.CultureInfo.InvariantCulture);
            try
            {
                using var child = Process.GetProcessById(pid);
                Assert.IsTrue(child.HasExited, "The timed-out verifier must exit before helper completion.");
            }
            catch (ArgumentException)
            {
                // The OS already reaped the terminated verifier.
            }
        }
        finally
        {
            if (File.Exists(pidPath))
            {
                var pid = int.Parse(await File.ReadAllTextAsync(pidPath), System.Globalization.CultureInfo.InvariantCulture);
                try
                {
                    using var child = Process.GetProcessById(pid);
                    if (!child.HasExited)
                    {
                        child.Kill(entireProcessTree: true);
                        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                    }
                }
                catch (ArgumentException) { /* The verifier already exited. */ }
            }
            TestFixtureFileSystem.DeleteDirectoryIfExists(root);
        }
    }

    [TestMethod]
    public async Task PushEvent_WithMatchingTag_Succeeds()
    {
        var fixture = new VersionFixture(FixtureVersion);
        await FixtureCleanupScope.RunAsync(async () =>
        {

            var result = await RunAsync(fixture.Root, "push", refName: $"v{FixtureVersion}");

            Assert.AreEqual(0, result.ExitCode, result.Combined);
            StringAssert.Contains(result.Combined, "Release provenance OK");
        }, fixture.Dispose);
    }

    [TestMethod]
    public async Task PushEvent_WithMismatchedTag_FailsNamingBothVersions()
    {
        var fixture = new VersionFixture(FixtureVersion);
        await FixtureCleanupScope.RunAsync(async () =>
        {

            var result = await RunAsync(fixture.Root, "push", refName: "v0.0.1");

            Assert.AreEqual(1, result.ExitCode, result.Combined);
            StringAssert.Contains(result.Combined, "v0.0.1");
            StringAssert.Contains(result.Combined, FixtureVersion);
        }, fixture.Dispose);
    }

    [TestMethod]
    public async Task ReleaseEvent_WithMatchingTagName_Succeeds()
    {
        var fixture = new VersionFixture(FixtureVersion);
        await FixtureCleanupScope.RunAsync(async () =>
        {

            var result = await RunAsync(fixture.Root, "release", releaseTag: $"v{FixtureVersion}");

            Assert.AreEqual(0, result.ExitCode, result.Combined);
            StringAssert.Contains(result.Combined, "github.event.release.tag_name");
        }, fixture.Dispose);
    }

    [TestMethod]
    public async Task ReleaseEvent_WithMismatchedTagName_Fails()
    {
        var fixture = new VersionFixture(FixtureVersion);
        await FixtureCleanupScope.RunAsync(async () =>
        {

            var result = await RunAsync(fixture.Root, "release", releaseTag: "v9.9.9");

            Assert.AreEqual(1, result.ExitCode, result.Combined);
            StringAssert.Contains(result.Combined, "v9.9.9");
        }, fixture.Dispose);
    }

    [TestMethod]
    public async Task ReleaseEvent_IgnoresRefNameAndComparesTagName()
    {
        var fixture = new VersionFixture(FixtureVersion);
        await FixtureCleanupScope.RunAsync(async () =>
        {

            // A release published from a branch carries ref_name = the branch; only the
            // release tag is authoritative, so a matching ref_name must not rescue a bad tag.
            var result = await RunAsync(
                fixture.Root,
                "release",
                refName: $"v{FixtureVersion}",
                releaseTag: "v0.0.1");

            Assert.AreEqual(1, result.ExitCode, result.Combined);
            StringAssert.Contains(result.Combined, "github.event.release.tag_name");
        }, fixture.Dispose);
    }

    [TestMethod]
    [DataRow("push", "refs/heads/main")]
    [DataRow("push", "v3.0")]
    [DataRow("push", "release-4.2.1")]
    public async Task PushEvent_WithMalformedTag_Fails(string eventName, string refName)
    {
        var fixture = new VersionFixture(FixtureVersion);
        await FixtureCleanupScope.RunAsync(async () =>
        {

            var result = await RunAsync(fixture.Root, eventName, refName: refName);

            Assert.AreEqual(1, result.ExitCode, result.Combined);
            StringAssert.Contains(result.Combined, refName);
        }, fixture.Dispose);
    }

    [TestMethod]
    public async Task PushEvent_WithEmptyRefName_Fails()
    {
        var fixture = new VersionFixture(FixtureVersion);
        await FixtureCleanupScope.RunAsync(async () =>
        {

            var result = await RunAsync(fixture.Root, "push");

            Assert.AreEqual(1, result.ExitCode, result.Combined);
            StringAssert.Contains(result.Combined, "was empty");
        }, fixture.Dispose);
    }

    [TestMethod]
    public async Task ReleaseEvent_WithEmptyTagName_Fails()
    {
        var fixture = new VersionFixture(FixtureVersion);
        await FixtureCleanupScope.RunAsync(async () =>
        {

            var result = await RunAsync(fixture.Root, "release");

            Assert.AreEqual(1, result.ExitCode, result.Combined);
            StringAssert.Contains(result.Combined, "was empty");
        }, fixture.Dispose);
    }

    [TestMethod]
    public async Task WorkflowDispatch_WithoutAnyTag_SkipsComparison()
    {
        var fixture = new VersionFixture(FixtureVersion);
        await FixtureCleanupScope.RunAsync(async () =>
        {

            var result = await RunAsync(fixture.Root, "workflow_dispatch");

            Assert.AreEqual(0, result.ExitCode, result.Combined);
            StringAssert.Contains(result.Combined, "skipping provenance comparison");
            StringAssert.Contains(result.Combined, FixtureVersion);
        }, fixture.Dispose);
    }

    [TestMethod]
    public async Task UnversionedTagPrefixIsOptional()
    {
        var fixture = new VersionFixture(FixtureVersion);
        await FixtureCleanupScope.RunAsync(async () =>
        {

            var result = await RunAsync(fixture.Root, "push", refName: FixtureVersion);

            Assert.AreEqual(0, result.ExitCode, result.Combined);
        }, fixture.Dispose);
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

        // The gate receives its context through env: bindings, NOT ${{ }} interpolated into
        // the run: body. Single-quoting an interpolation is NOT a mitigation: ${{ }} is
        // substituted into the workflow source before any shell parses it, so a release tag
        // containing a quote closes the string and executes the rest as commands.
        StringAssert.Contains(
            workflow,
            "PUBLISH_EVENT_NAME: ${{ github.event_name }}",
            "The gate must receive the triggering event name via an env: binding.");
        StringAssert.Contains(
            workflow,
            "PUBLISH_REF_NAME: ${{ github.ref_name }}",
            "The gate must receive the pushed ref name via an env: binding.");
        StringAssert.Contains(
            workflow,
            "PUBLISH_RELEASE_TAG: ${{ github.event.release.tag_name }}",
            "The gate must receive the published release tag via an env: binding.");
        StringAssert.Contains(
            workflow,
            "-EventName $env:PUBLISH_EVENT_NAME -RefName $env:PUBLISH_REF_NAME -ReleaseTag $env:PUBLISH_RELEASE_TAG",
            "The gate must read its context from environment variables.");
    }

    [TestMethod]
    public void Workflows_NeverInterpolateContextIntoRunBodies()
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var offenders = new List<string>();
        var workflowsDirectory = Path.Combine(repositoryRoot, ".github", "workflows");
        var workflowPaths = Directory.EnumerateFiles(workflowsDirectory)
            .Where(path => Path.GetExtension(path) is ".yml" or ".yaml")
            .ToArray();
        Assert.IsNotEmpty(workflowPaths);
        foreach (var workflowPath in workflowPaths)
        {
            offenders.AddRange(FindInterpolatedRunBodies(File.ReadAllLines(workflowPath))
                .Select(offender => $"{workflowPath}:{offender}"));
        }

        Assert.AreEqual(
            0,
            offenders.Count,
            "GitHub Actions script injection: ${{ }} expressions must never be interpolated "
                + "into a run: body. Bind context via env: and read it from the shell. Offending lines:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, offenders));
    }

    [TestMethod]
    public void RunBodyGuard_RecognizesYamlBlockScalarVariants()
    {
        foreach (var indicator in new[] { "|", ">", "|-", ">-", "|+", ">+", "|2", ">2", "|+2", ">2+" })
        {
            var lines = new[]
            {
                $"        run: {indicator} # valid scalar",
                "        # A YAML comment can separate the indicator from its body.",
                "          echo ${{ github.ref_name }}",
            };
            Assert.HasCount(1, FindInterpolatedRunBodies(lines), indicator);
        }
    }

    [TestMethod]
    public void RunBodyGuard_RecognizesMultilineScalarsAndCommentsWithinScripts()
    {
        var cases = new[]
        {
            new[] { "        run: |", "          # ${{ github.event.pull_request.body }}" },
            new[] { "        run: echo safe", "          ${{ github.event.pull_request.body }}" },
            new[] { "        run: \"echo safe", "          ${{ github.event.pull_request.body }}\"" },
            new[] { "        run: \"echo safe", "          # ${{ github.event.pull_request.body }}\"" },
        };
        var detected = cases.Select(lines => FindInterpolatedRunBodies(lines).Count).ToArray();
        CollectionAssert.AreEqual(new[] { 1, 1, 1, 1 }, detected);
    }

    [TestMethod]
    public void RunBodyGuard_IgnoresExpressionsOutsideRunScalars()
    {
        var lines = new[]
        {
            "        # run: ${{ github.event.pull_request.body }}",
            "        env:",
            "          VALUE: ${{ github.ref_name }}",
            "        run: echo safe",
            "        # ${{ github.event.pull_request.body }}",
            "        next: done",
        };
        Assert.IsEmpty(FindInterpolatedRunBodies(lines));
    }

    [TestMethod]
    public void RunBodyGuard_IgnoresDeeperYamlCommentsAfterCompletedInlineScalars()
    {
        var cases = new[]
        {
            new[] { "        run: echo safe", "          # ${{ github.ref_name }}" },
            new[] { "        run: \"echo safe\"", "          # ${{ github.ref_name }}" },
            new[] { "        run: 'echo safe'", "          # ${{ github.ref_name }}" },
            new[] { "        run: \"echo", "          safe\"", "          # ${{ github.ref_name }}" },
        };
        var detected = cases.Select(lines => FindInterpolatedRunBodies(lines).Count).ToArray();
        CollectionAssert.AreEqual(new[] { 0, 0, 0, 0 }, detected);
    }

    private static List<string> FindInterpolatedRunBodies(string[] lines)
    {
        var offenders = new List<string>();
        var runBodyIndent = -1;
        var blockScalar = false;
        var openQuote = '\0';

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var indent = line.Length - line.TrimStart().Length;
            var trimmed = line.TrimStart();

            if (runBodyIndent >= 0)
            {
                // Block, plain, and quoted YAML scalars can all continue on deeper lines.
                // A comment at the key's indentation does not end a following scalar body.
                if (trimmed.Length == 0)
                {
                    continue;
                }

                if (indent > runBodyIndent)
                {
                    if ((!trimmed.StartsWith('#') || blockScalar || openQuote != '\0') &&
                        line.Contains("${{", StringComparison.Ordinal))
                    {
                        offenders.Add($"{i + 1}: {trimmed}");
                    }

                    if (openQuote != '\0' && HasClosingYamlQuote(trimmed, openQuote, 0))
                    {
                        openQuote = '\0';
                    }

                    continue;
                }

                if (trimmed.StartsWith('#'))
                {
                    continue;
                }

                runBodyIndent = -1;
            }

            var runKey = System.Text.RegularExpressions.Regex.Match(
                trimmed,
                "\\A(?:run|'run'|\\\"run\\\")\\s*:\\s*(?<value>.*)\\z",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);
            if (!runKey.Success)
            {
                continue;
            }

            if (line.Contains("${{", StringComparison.Ordinal))
            {
                offenders.Add($"{i + 1}: {trimmed}");
            }

            var scalar = runKey.Groups["value"].Value.TrimStart();
            blockScalar = System.Text.RegularExpressions.Regex.IsMatch(
                scalar,
                @"\A[|>](?:[1-9][-+]?|[-+]?[1-9]?|[-+])(?:\s+#.*)?\s*\z",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);
            openQuote = scalar.Length > 0 && (scalar[0] is '\'' or '"') &&
                !HasClosingYamlQuote(scalar, scalar[0], 1)
                    ? scalar[0]
                    : '\0';
            runBodyIndent = indent;
        }

        return offenders;
    }

    private static bool HasClosingYamlQuote(string text, char quote, int start)
    {
        for (var i = start; i < text.Length; i++)
        {
            if (quote == '"' && text[i] == '\\')
            {
                i++;
            }
            else if (text[i] == quote)
            {
                if (quote == '\'' && i + 1 < text.Length && text[i + 1] == '\'')
                {
                    i++;
                }
                else
                {
                    return true;
                }
            }
        }

        return false;
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

        var arguments = new List<string> { "-NoProfile", "-File", scriptPath, "-EventName", eventName };
        if (refName is not null)
        {
            arguments.AddRange(["-RefName", refName]);
        }

        if (releaseTag is not null)
        {
            arguments.AddRange(["-ReleaseTag", releaseTag]);
        }

        arguments.AddRange(["-RepoRoot", repoRoot]);
        return await RunVerifierProcessAsync(arguments, TimeSpan.FromSeconds(30));
    }

    private static async Task<ProcessResult> RunVerifierProcessAsync(IEnumerable<string> arguments, TimeSpan timeout)
    {
        var result = await PwshScriptRunner.RunAsync(arguments,
            timeout: timeout, description: "Publish provenance verifier");
        return new ProcessResult(result.ExitCode, result.StdOut, result.StdErr);
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
            => TestFixtureFileSystem.DeleteDirectoryIfExists(Root);
    }
}
