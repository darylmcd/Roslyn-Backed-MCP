using System.Diagnostics;
using System.Text.Json;
using RoslynMcp.Tests.Support;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class CodexChangelogHookTests
{
    private static readonly TimeSpan HookProcessTimeout = TimeSpan.FromMinutes(2);

    [TestMethod]
    public void HookConfiguration_CoversSupportedShellAliasesAndGithubPublicationTools()
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var configurationPath = Path.Combine(repositoryRoot, ".codex", "hooks.json");
        using var document = JsonDocument.Parse(File.ReadAllText(configurationPath));

        var matcher = document.RootElement
            .GetProperty("hooks")
            .GetProperty("PreToolUse")[0]
            .GetProperty("matcher")
            .GetString();

        Assert.IsNotNull(matcher);
        StringAssert.Contains(matcher, "Bash");
        StringAssert.Contains(matcher, "shell_command");
        StringAssert.Contains(matcher, "github_");
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task Hook_EnforcesPublicationBoundariesAndDelegatesToVerifier()
    {
        RequireGit();
        var fixtureRoot = CreateFixture();
        try
        {
            var nonPublication = await InvokeHookAsync(
                fixtureRoot,
                ToolInput("shell_command", command: "rg -n \"git push\" docs"));
            AssertQuietSuccess(nonPublication, "non-publication command");

            foreach (var (toolName, command) in new[]
            {
                ("Bash", "git commit --dry-run"),
                ("shell_command", "git push --dry-run"),
                ("shell_command", "gh pr create --title fixture"),
                ("shell_command", "gbash C:/fixture/ship-preflight.sh"),
            })
            {
                var denial = await InvokeHookAsync(fixtureRoot, ToolInput(toolName, command));
                AssertDenial(denial, command);
            }

            var fixtureName = Path.GetFileName(fixtureRoot);
            foreach (var toolName in new[]
            {
                "mcp__codex_apps__github_create_pull_request",
                "mcp__codex_apps__github_merge_pull_request",
            })
            {
                var denial = await InvokeHookAsync(
                    fixtureRoot,
                    ToolInput(toolName, repositoryFullName: $"fixture/{fixtureName}"));
                AssertDenial(denial, toolName);
            }

            var otherRepository = await InvokeHookAsync(
                fixtureRoot,
                ToolInput(
                    "mcp__codex_apps__github_create_pull_request",
                    repositoryFullName: "fixture/another-repository"));
            AssertQuietSuccess(otherRepository, "another repository");

            File.WriteAllText(
                Path.Combine(fixtureRoot, "changelog.d", "fixture-change.md"),
                "---\ncategory: Fixed\n---\n\n- **Fixed:** Fixture publication contract.\n");
            var allowed = await InvokeHookAsync(
                fixtureRoot,
                ToolInput("shell_command", command: "git push --dry-run"));
            AssertQuietSuccess(allowed, "valid changed fragment");

            File.WriteAllText(
                Path.Combine(fixtureRoot, "changelog.d", "fixture-change.md"),
                "invalid fragment\n");
            var malformedFragment = await InvokeHookAsync(
                fixtureRoot,
                ToolInput("shell_command", command: "git push --dry-run"));
            AssertDenial(malformedFragment, "malformed fragment");
            StringAssert.Contains(malformedFragment.StdOut, "fragment validation failed");

            var missingVerifier = await InvokeHookAsync(
                fixtureRoot,
                ToolInput("shell_command", command: "git push --dry-run"),
                Path.Combine(fixtureRoot, "eng", "missing-verifier.ps1"));
            AssertDenial(missingVerifier, "missing verifier");
            StringAssert.Contains(missingVerifier.StdOut, "verifier is missing");

            var malformedInput = await InvokeHookAsync(fixtureRoot, "{");
            Assert.AreEqual(2, malformedInput.ExitCode, malformedInput.StdErr);
            StringAssert.Contains(malformedInput.StdErr, "failed closed");
            Assert.IsFalse(
                malformedInput.StdErr.Contains("{", StringComparison.Ordinal),
                "Fail-closed diagnostics must not echo malformed hook payloads.");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    private static string CreateFixture()
    {
        var root = Path.Combine(
            TestTempRoot.Current,
            nameof(CodexChangelogHookTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "eng"));
        Directory.CreateDirectory(Path.Combine(root, "changelog.d"));
        Directory.CreateDirectory(Path.Combine(root, "src"));

        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        File.Copy(
            Path.Combine(repositoryRoot, "eng", "verify-changelog-fragments.ps1"),
            Path.Combine(root, "eng", "verify-changelog-fragments.ps1"));
        File.WriteAllText(Path.Combine(root, "changelog.d", "README.md"), "# Fixture fragments\n");
        File.WriteAllText(Path.Combine(root, "src", "fixture.txt"), "baseline\n");

        GitFixtureRunner.RunGit(root, "init", "-q", "-b", "main");
        GitFixtureRunner.RunGit(root, "add", "-A");
        GitFixtureRunner.RunGit(
            root,
            "-c",
            "user.email=fixture@roslynmcp.invalid",
            "-c",
            "user.name=Codex Hook Fixture",
            "commit",
            "-q",
            "-m",
            "fixture baseline");
        File.AppendAllText(Path.Combine(root, "src", "fixture.txt"), "changed\n");
        return root;
    }

    private static string ToolInput(
        string toolName,
        string? command = null,
        string? repositoryFullName = null)
        => JsonSerializer.Serialize(new
        {
            session_id = "fixture-session",
            turn_id = "fixture-turn",
            hook_event_name = "PreToolUse",
            tool_name = toolName,
            tool_input = command is not null
                ? (object)new { command }
                : new { repository_full_name = repositoryFullName },
        });

    private static async Task<ProcessResult> InvokeHookAsync(
        string fixtureRoot,
        string input,
        string? verifierPath = null)
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var hookPath = Path.Combine(repositoryRoot, ".codex", "hooks", "pre-publish-changelog.ps1");
        var startInfo = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(hookPath);
        startInfo.ArgumentList.Add("-RepoRoot");
        startInfo.ArgumentList.Add(fixtureRoot);
        if (verifierPath is not null)
        {
            startInfo.ArgumentList.Add("-VerifierPath");
            startInfo.ArgumentList.Add(verifierPath);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the Codex changelog hook.");
        await process.StandardInput.WriteAsync(input);
        process.StandardInput.Close();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(HookProcessTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(
                $"Codex changelog hook did not exit within {HookProcessTimeout.TotalSeconds:F0} seconds.");
        }

        return new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static void AssertDenial(ProcessResult result, string scenario)
    {
        Assert.AreEqual(0, result.ExitCode, $"{scenario}: stderr={result.StdErr}");
        using var document = JsonDocument.Parse(result.StdOut);
        var hookOutput = document.RootElement.GetProperty("hookSpecificOutput");
        Assert.AreEqual("PreToolUse", hookOutput.GetProperty("hookEventName").GetString(), scenario);
        Assert.AreEqual("deny", hookOutput.GetProperty("permissionDecision").GetString(), scenario);
        StringAssert.Contains(
            hookOutput.GetProperty("permissionDecisionReason").GetString(),
            "changelog",
            scenario);
    }

    private static void AssertQuietSuccess(ProcessResult result, string scenario)
    {
        Assert.AreEqual(0, result.ExitCode, $"{scenario}: stderr={result.StdErr}");
        Assert.IsTrue(string.IsNullOrWhiteSpace(result.StdOut), $"{scenario}: stdout={result.StdOut}");
        Assert.IsTrue(string.IsNullOrWhiteSpace(result.StdErr), $"{scenario}: stderr={result.StdErr}");
    }

    private static void RequireGit()
    {
        if (!GitFixtureRunner.IsAvailable(out var failureReason))
            Assert.Inconclusive($"git is required for Codex hook fixtures: {failureReason}");
    }

    private sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);
}
