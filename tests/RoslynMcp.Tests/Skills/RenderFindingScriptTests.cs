using System.Diagnostics;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace RoslynMcp.Tests.Skills;

/// <summary>
/// Behavior tests for the shared finding-rendering helper at
/// `skills/mcp-server-surface-test/lib/render-finding.ps1`. The helper is the single source of
/// truth for the GitHub-Issue body shape used by both
/// `/mcp-server-surface-test --auto-file` (consumer) and `/backlog-intake --publish` (maintainer).
/// Body bytes MUST be identical across both auto-file paths — that's the contract Row 2 of the
/// move-to-git-issues design ships, and the determinism + refusal-contract tests below pin it.
///
/// Each test invokes pwsh out-of-process, dot-sources the renderer, calls one function with a
/// known finding hashtable, and asserts on either the rendered text (determinism, anchor sort,
/// banner placement) or a JSON projection of structured output (refusedPublic flag, repo-id slug).
/// No live `gh` invocation runs in any test.
/// </summary>
[TestClass]
public sealed class RenderFindingScriptTests
{
    private string _tempRoot = string.Empty;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempRoot = Path.Combine(TestTempRoot.Current, "RenderFinding", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        TestFixtureFileSystem.DeleteDirectoryIfExists(_tempRoot);
    }

    [TestMethod]
    public void Script_FileExistsAtDocumentedPath()
    {
        var scriptPath = ResolveScriptPath();
        Assert.IsTrue(
            File.Exists(scriptPath),
            $"render-finding.ps1 was not found at '{scriptPath}'. Both `/mcp-server-surface-test` " +
            "and `/backlog-intake --publish` route through this file; without it both auto-file paths break.");
    }

    [TestMethod]
    public void RenderFindingBody_IsDeterministic_SameInputProducesIdenticalOutput()
    {
        var scriptPath = ResolveScriptPath();
        // Anchors deliberately out of order on input — the renderer must sort them alphabetically.
        // Two runs with the same logical input must produce byte-identical bodies; otherwise the
        // maintainer publish path and the consumer auto-file path will diverge over time.
        var pwshSnippet = $$"""
            . '{{EscapeForPwshSingleQuoted(scriptPath)}}'
            $f = @{
                id = 'test-finding-id'; source_repo = 'sample-repo'
                severity = 'P2'; area = 'tools'; server_version = '1.35.0'
                anchors = @('z/late.cs:1','a/early.cs:2','m/middle.cs:3')
                finding = 'X happens'; repro = 'do Y'; proposed_fix = 'fix Z'
            }
            $a = Render-FindingBody -Finding $f
            $b = Render-FindingBody -Finding $f
            "identical=" + ($a -ceq $b)
            "---"
            $a
            """;

        var result = RunPwshSnippet(pwshSnippet);
        AssertExitCodeZero(result);

        var lines = result.StdOut.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        Assert.IsTrue(lines.Any(l => l == "identical=True"),
            $"Render-FindingBody is non-deterministic on identical input — same hashtable produced different bytes on two consecutive calls. stdout: {result.StdOut}");

        // Anchors must appear in alphabetical order (a/, m/, z/) regardless of input order.
        var aIdx = Array.FindIndex(lines, l => l == "  - a/early.cs:2");
        var mIdx = Array.FindIndex(lines, l => l == "  - m/middle.cs:3");
        var zIdx = Array.FindIndex(lines, l => l == "  - z/late.cs:1");
        Assert.IsTrue(aIdx > 0 && mIdx > aIdx && zIdx > mIdx,
            $"Anchors must be sorted alphabetically in the rendered body so two logically-equivalent inputs (different list order) emit identical bytes. " +
            $"Got a={aIdx}, m={mIdx}, z={zIdx}. stdout:\n{result.StdOut}");
    }

    [TestMethod]
    public void RenderFindingBody_PopulatesAllFields_WithoutVariableShadowingBug()
    {
        // Regression: the renderer's `Render-FindingBody` originally used a local `$finding`
        // variable that case-collided with the `$Finding` parameter (PowerShell variable names
        // are case-insensitive). The collision overwrote the parameter mid-function, blanking
        // every subsequent field read. This test pins the fix.
        var scriptPath = ResolveScriptPath();
        var pwshSnippet = $$"""
            . '{{EscapeForPwshSingleQuoted(scriptPath)}}'
            $f = @{
                id = 'i'; source_repo = 'r'; severity = 'P3'; area = 'docs'; server_version = '1.0'
                anchors = @('only/file.cs:7')
                finding = 'F-VALUE'; repro = 'R-VALUE'; proposed_fix = 'P-VALUE'
            }
            Render-FindingBody -Finding $f
            """;

        var result = RunPwshSnippet(pwshSnippet);
        AssertExitCodeZero(result);

        StringAssert.Contains(result.StdOut, "- finding: F-VALUE",
            "Render-FindingBody dropped the `finding` field. This is the variable-shadowing regression.");
        StringAssert.Contains(result.StdOut, "- repro: R-VALUE",
            "Render-FindingBody dropped the `repro` field. The local `$finding` variable still shadows the `$Finding` parameter.");
        StringAssert.Contains(result.StdOut, "- proposed-fix: P-VALUE",
            "Render-FindingBody dropped the `proposed_fix` field. The local `$finding` variable still shadows the `$Finding` parameter.");
    }

    [TestMethod]
    public void TestFindingShouldRefusePublicFile_ReturnsTrueOnP0()
    {
        var result = RunRefusalProbe(severity: "P0", area: "tools");
        AssertExitCodeZero(result);
        StringAssert.Contains(result.StdOut, "refused=True",
            "Test-FindingShouldRefusePublicFile must return $true for severity=P0. This is the load-bearing pre-disclosure safeguard — both auto-file paths short-circuit on it.");
    }

    [TestMethod]
    public void TestFindingShouldRefusePublicFile_ReturnsTrueOnSecurityArea()
    {
        var result = RunRefusalProbe(severity: "P3", area: "security");
        AssertExitCodeZero(result);
        StringAssert.Contains(result.StdOut, "refused=True",
            "Test-FindingShouldRefusePublicFile must return $true for area=security regardless of severity. " +
            "Pre-disclosure-relevant findings get the security-advisory escalation path, not a public Issue.");
    }

    [TestMethod]
    public void TestFindingShouldRefusePublicFile_ReturnsFalseOnP2Tools()
    {
        var result = RunRefusalProbe(severity: "P2", area: "tools");
        AssertExitCodeZero(result);
        StringAssert.Contains(result.StdOut, "refused=False",
            "Test-FindingShouldRefusePublicFile must return $false on a routine P2/tools finding. The refusal must be narrow — every other finding ships through the public path.");
    }

    [TestMethod]
    public void RenderFindingIssue_PrependsBannerOnRefused()
    {
        var scriptPath = ResolveScriptPath();
        var pwshSnippet = $$"""
            . '{{EscapeForPwshSingleQuoted(scriptPath)}}'
            $f = @{
                id = 'refused-id'; source_repo = 'r'; severity = 'P0'; area = 'security'
                server_version = '1.0'; anchors = @('a:1')
                finding = 'f'; repro = 'r'; proposed_fix = 'p'
            }
            $issue = Render-FindingIssue -Finding $f
            "refusedPublic=" + $issue.refusedPublic
            "---"
            $issue.body
            """;

        var result = RunPwshSnippet(pwshSnippet);
        AssertExitCodeZero(result);

        StringAssert.Contains(result.StdOut, "refusedPublic=True",
            "Render-FindingIssue must mark P0/security findings refusedPublic=True so callers short-circuit before invoking gh.");
        // Use an ASCII-only substring — pwsh's stdout encoding on Windows isn't always UTF-8, so the
        // em-dash in the full banner phrase can mangle through the test harness even though the
        // renderer emits it correctly.
        StringAssert.Contains(result.StdOut, "DO NOT FILE PUBLICLY",
            "Render-FindingIssue must prepend the SECURITY/P0 banner to the body when the finding is refused. Defense in depth — even a misconfigured caller that ignores `refusedPublic` ships the warning to whatever destination it picks.");
        StringAssert.Contains(result.StdOut, "https://github.com/darylmcd/Roslyn-Backed-MCP/security/advisories/new",
            "The refusal banner must direct the operator to GitHub security advisories — that is the documented private-disclosure path.");
    }

    [TestMethod]
    public void RenderFindingIssue_DoesNotPrependBannerOnNormalFinding()
    {
        var scriptPath = ResolveScriptPath();
        var pwshSnippet = $$"""
            . '{{EscapeForPwshSingleQuoted(scriptPath)}}'
            $f = @{
                id = 'ok-id'; source_repo = 'r'; severity = 'P2'; area = 'tools'
                server_version = '1.0'; anchors = @('a:1')
                finding = 'f'; repro = 'r'; proposed_fix = 'p'
            }
            $issue = Render-FindingIssue -Finding $f
            "refusedPublic=" + $issue.refusedPublic
            "---"
            $issue.body
            """;

        var result = RunPwshSnippet(pwshSnippet);
        AssertExitCodeZero(result);

        StringAssert.Contains(result.StdOut, "refusedPublic=False",
            "Render-FindingIssue must mark routine findings refusedPublic=False so the public Issue path proceeds.");
        Assert.IsFalse(result.StdOut.Contains("DO NOT FILE PUBLICLY", StringComparison.Ordinal),
            "Render-FindingIssue must NOT prepend the SECURITY banner on routine findings. Over-applying the refusal would block normal contributions.");
    }

    [TestMethod]
    public void GetFindingRepoId_DerivesFromGitRemote()
    {
        // Seed a temp repo with a remote URL — the function should parse owner/repo from it.
        var repoRoot = Path.Combine(_tempRoot, "WeirdCasing-RepoName");
        Directory.CreateDirectory(repoRoot);
        RunGit(repoRoot, "init", "-q");
        RunGit(repoRoot, "remote", "add", "origin", "https://github.com/example-owner/MixedCase-Repo.git");

        var scriptPath = ResolveScriptPath();
        var pwshSnippet = $$"""
            . '{{EscapeForPwshSingleQuoted(scriptPath)}}'
            Get-FindingRepoId -RepoRoot '{{EscapeForPwshSingleQuoted(repoRoot)}}'
            """;

        var result = RunPwshSnippet(pwshSnippet);
        AssertExitCodeZero(result);

        var stdoutTrimmed = result.StdOut.Trim();
        Assert.AreEqual("mixedcase-repo", stdoutTrimmed,
            $"Get-FindingRepoId must parse owner/repo from `git remote get-url origin` and slugify the repo half. " +
            $"Expected 'mixedcase-repo', got '{stdoutTrimmed}'. stderr: {result.StdErr}");
    }

    [TestMethod]
    public void GetFindingRepoId_FallsBackToDirectoryBasenameWhenNoRemote()
    {
        // Repo with no remote — falls back to the directory basename slug.
        var repoRoot = Path.Combine(_tempRoot, "MyLocalRepo");
        Directory.CreateDirectory(repoRoot);
        RunGit(repoRoot, "init", "-q");

        var scriptPath = ResolveScriptPath();
        var pwshSnippet = $$"""
            . '{{EscapeForPwshSingleQuoted(scriptPath)}}'
            Get-FindingRepoId -RepoRoot '{{EscapeForPwshSingleQuoted(repoRoot)}}'
            """;

        var result = RunPwshSnippet(pwshSnippet);
        AssertExitCodeZero(result);

        var stdoutTrimmed = result.StdOut.Trim();
        Assert.AreEqual("mylocalrepo", stdoutTrimmed,
            $"Get-FindingRepoId must fall back to the resolved directory basename (slugified) when no remote is set. " +
            $"Expected 'mylocalrepo', got '{stdoutTrimmed}'.");
    }

    [TestMethod]
    public void RenderFindingFragment_IncludesServerVersionFrontmatter()
    {
        // Row 2 contract: server_version is a required frontmatter key. The fragment renderer must
        // emit it under the YAML --- block so /backlog-intake's Phase 0.5 schema check passes.
        var scriptPath = ResolveScriptPath();
        var pwshSnippet = $$"""
            . '{{EscapeForPwshSingleQuoted(scriptPath)}}'
            $f = @{
                id = 'frag-test'; source_audit = 'audit.md'; source_repo = 'r'
                severity = 'P3'; area = 'docs'; server_version = '2.7.1-rc.4'
                anchors = @('docs/x.md:5')
                finding = 'f'; repro = 'r'; proposed_fix = 'p'
            }
            Render-FindingFragment -Finding $f
            """;

        var result = RunPwshSnippet(pwshSnippet);
        AssertExitCodeZero(result);

        StringAssert.Contains(result.StdOut, "server_version: 2.7.1-rc.4",
            "Render-FindingFragment must include `server_version:` in the YAML frontmatter. " +
            "The fragment-schema validator in /backlog-intake's Phase 0.5 step 1 requires it.");
    }

    // ---------- Helpers ----------

    private PwshResult RunRefusalProbe(string severity, string area)
    {
        var scriptPath = ResolveScriptPath();
        var pwshSnippet = $$"""
            . '{{EscapeForPwshSingleQuoted(scriptPath)}}'
            $f = @{
                id = 'x'; source_repo = 'r'; severity = '{{severity}}'; area = '{{area}}'
                server_version = '1.0'; anchors = @('a:1')
                finding = 'f'; repro = 'r'; proposed_fix = 'p'
            }
            "refused=" + (Test-FindingShouldRefusePublicFile -Finding $f)
            """;
        return RunPwshSnippet(pwshSnippet);
    }

    private static string ResolveScriptPath()
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        return Path.Combine(repoRoot, "skills", "mcp-server-surface-test", "lib", "render-finding.ps1");
    }

    private static string EscapeForPwshSingleQuoted(string value)
    {
        // PowerShell single-quoted string: `'` -> `''`. Backslashes pass through literally.
        return value.Replace("'", "''");
    }

    private static PwshResult RunPwshSnippet(string snippet)
    {
        var pwshExecutable = ResolvePwshExecutable();

        var psi = new ProcessStartInfo
        {
            FileName = pwshExecutable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(snippet);

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start '{pwshExecutable}'.");
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        if (!proc.WaitForExit(milliseconds: 60_000))
        {
            proc.Kill(entireProcessTree: true);
            throw new TimeoutException("pwsh render-finding.ps1 invocation timed out after 60s.");
        }

        return new PwshResult(proc.ExitCode, stdout, stderr);
    }

    private static void RunGit(string workingDir, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in args) { psi.ArgumentList.Add(a); }

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git.");
        proc.WaitForExit(30_000);
        if (proc.ExitCode != 0)
        {
            var err = proc.StandardError.ReadToEnd();
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {err}");
        }
    }

    private static string ResolvePwshExecutable()
    {
        if (OperatingSystem.IsWindows()) { return "pwsh.exe"; }
        return "pwsh";
    }

    private static void AssertExitCodeZero(PwshResult result)
    {
        Assert.AreEqual(0, result.ExitCode,
            $"pwsh exited with code {result.ExitCode}. stdout: {result.StdOut} | stderr: {result.StdErr}");
    }

    private sealed record PwshResult(int ExitCode, string StdOut, string StdErr);
}
