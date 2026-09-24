using System.Text.Json;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests.Skills;

[TestClass]
[TestCategory("Process")]
public sealed class DocumentationGateScriptTests
{
    [TestMethod]
    [DataRow("`[code](missing.md)`", false)]
    [DataRow("``[code](missing.md) ` nested``", false)]
    [DataRow("`multiline\n[code](missing.md)`", false)]
    [DataRow("```js\n[code](missing.md)\n```", false)]
    [DataRow("~~~~js\n[code](missing.md)\n~~~\n[code](missing.md)\n~~~~", false)]
    [DataRow("> ```js\n> [code](missing.md)\n> ```", false)]
    [DataRow("```js\n[code](missing.md)", false)]
    [DataRow("    [code](missing.md)", false)]
    [DataRow("[template]({}) [template]({target}) [valid](exists.md)", false)]
    [DataRow("[real](missing.md)", true)]
    [DataRow("[real](...)", true)]
    [DataRow("`unclosed [real](missing.md)", true)]
    [DataRow("\\`[real](missing.md)\\`", true)]
    [DataRow("[label with `code`](missing.md)", true)]
    [DataRow("```\n[code](ignored.md)\n```\n[real](missing.md)", true)]
    [DataRow("> ```\n> [code](ignored.md)\n\n[real](missing.md)", true)]
    public async Task MarkdownLinks_IgnoreCodeAndPreserveRealLinkValidationAsync(string markdown, bool hasIssue)
    {
        var issues = await RunContractAsync(
            "markdown-link-validation.ps1",
            "@(Get-MarkdownLinkIssue -Files ([System.IO.FileInfo]::new($args[1])))",
            markdown);

        Assert.AreEqual(hasIssue ? 1 : 0, issues.Length, string.Join(Environment.NewLine, issues));
        if (hasIssue)
        {
            StringAssert.Contains(issues[0], "Broken relative link");
            Assert.IsFalse(issues[0].Contains("ignored.md", StringComparison.Ordinal));
        }
    }

    [TestMethod]
    public async Task MarkdownLinks_MissingAbsolutePath_RemainsAnErrorAsync()
    {
        var missing = Path.Combine(TestTempRoot.Current, Guid.NewGuid().ToString("N"), "missing.md");
        var issues = await RunContractAsync(
            "markdown-link-validation.ps1",
            "@(Get-MarkdownLinkIssue -Files ([System.IO.FileInfo]::new($args[1])))",
            $"[real]({missing})");

        Assert.HasCount(1, issues);
        StringAssert.Contains(issues[0], OperatingSystem.IsWindows() ? "Broken absolute link" : "Broken relative link");
        StringAssert.Contains(issues[0], missing);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("\nUse `git worktree remove --force .worktrees/example` after reconciliation.")]
    [DataRow("\n```bash\ngit -C \"repo with spaces\" worktree remove -f .worktrees/example\n```\n")]
    [DataRow("\ngit worktree remove .worktrees/example\n")]
    public async Task Reconciliation_RequiresCanonicalCleanupAndRejectsRemovalCommandsAsync(string appendedInstruction)
    {
        var root = TestFixtureFileSystem.FindRepositoryRoot();
        var guidance = File.ReadAllText(Path.Combine(root, ".claude", "skills", "reconcile-backlog-sweep-plan", "SKILL.md"));
        var issues = await RunContractAsync(
            "reconciliation-cleanup-contract.ps1",
            "@(Get-ReconciliationCleanupIssue -Content ([System.IO.File]::ReadAllText($args[1])))",
            guidance + appendedInstruction);

        Assert.AreEqual(appendedInstruction.Length == 0 ? 0 : 1, issues.Length, string.Join(Environment.NewLine, issues));
        if (issues.Length > 0)
        {
            StringAssert.Contains(issues[0], "contains a worktree removal command");
        }
    }

    // Samples are assembled at runtime so this source file never contains a literal profile path
    // (the guard under test scans every tracked file, including this one).
    private const string Drive = "C:";
    private const string AccountName = "jdoe";

    [TestMethod]
    [DataRow("windows-backslash", true)]
    [DataRow("windows-forward", true)]
    [DataRow("json-escaped", true)]
    [DataRow("msys", true)]
    [DataRow("url-encoded", true)]
    [DataRow("claude-project-slug", true)]
    [DataRow("linux-home", true)]
    [DataRow("placeholder-foo", false)]
    [DataRow("angle-placeholder", false)]
    [DataRow("userprofile-variable", false)]
    [DataRow("github-runner-home", false)]
    [DataRow("github-owner-url", false)]
    public async Task LocalPathLeaks_RejectRealProfilePathsAndAcceptPlaceholdersAsync(string shape, bool hasIssue)
    {
        var content = shape switch
        {
            "windows-backslash" => Drive + "\\Users\\" + AccountName + "\\AppData\\Local\\Temp\\x.log",
            "windows-forward" => Drive + "/Users/" + AccountName + "/.claude/agents/a.md",
            "json-escaped" => "{\"log\": \"" + Drive + "\\\\Users\\\\" + AccountName + "\\\\gate.log\"}",
            "msys" => "/c/Users/" + AccountName + "/.claude",
            "url-encoded" => "roslyn://workspace/1/file/C%3A%2FUsers%2F" + AccountName + "%2Frepo%2Fa.cs",
            "claude-project-slug" => "projects/C--Users-" + AccountName + "--claude/memory",
            "linux-home" => "/home/" + AccountName + "/src/repo",
            "placeholder-foo" => Drive + "\\Users\\foo",
            "angle-placeholder" => "<user>/.nuget/packages/x",
            "userprofile-variable" => "%USERPROFILE%\\actions-runner and $env:USERPROFILE",
            "github-runner-home" => "/home/runner/work/Repo/Repo",
            "github-owner-url" => "https://github.com/darylmcd/Roslyn-Backed-MCP",
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        var issues = await RunContractAsync(
            "local-path-leak-validation.ps1",
            "@(Get-LocalPathLeakIssue -Files ([System.IO.FileInfo]::new($args[1])) -RepoRoot (Split-Path -Parent $args[1]))",
            content);

        Assert.AreEqual(hasIssue ? 1 : 0, issues.Length, string.Join(Environment.NewLine, issues));
        if (hasIssue)
        {
            StringAssert.Contains(issues[0], "Local user-profile path in input.md");
        }
    }

    private static async Task<string[]> RunContractAsync(string contractFile, string invocation, string content)
    {
        var fixture = Path.Combine(TestTempRoot.Current, "DocumentationGate", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        try
        {
            var contentPath = Path.Combine(fixture, "input.md");
            File.WriteAllText(contentPath, content);
            File.WriteAllText(Path.Combine(fixture, "exists.md"), "# Existing target\n");
            var driver = Path.Combine(fixture, "probe.ps1");
            File.WriteAllText(driver, "$ErrorActionPreference = 'Stop'\n. $args[0]\n$result = " + invocation + "\nConvertTo-Json -InputObject $result -Compress\n");
            var contract = Path.Combine(TestFixtureFileSystem.FindRepositoryRoot(), "eng", contractFile);
            var result = await PwshScriptRunner.RunAsync(
                ["-NoProfile", "-NonInteractive", "-File", driver, contract, contentPath],
                workingDirectory: fixture,
                timeout: TimeSpan.FromSeconds(30),
                description: "documentation validation contract");
            Assert.AreEqual(0, result.ExitCode, result.AllOutput);
            return JsonSerializer.Deserialize<string[]>(result.StdOut) ?? [];
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixture);
        }
    }
}
