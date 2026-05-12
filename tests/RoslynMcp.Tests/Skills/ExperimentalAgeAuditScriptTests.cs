using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace RoslynMcp.Tests.Skills;

/// <summary>
/// eternal-experimental-age-audit: behavior tests for `eng/audit-experimental-age.ps1`.
///
/// The script walks `ServerSurfaceCatalog.*.cs` partials, extracts experimental-tier
/// factory call lines, runs `git blame` on each to compute age, and emits a Markdown
/// table for entries older than `$ThresholdDays` days (default 180).
///
/// Strategy: all tests run the script against an ISOLATED temp directory — never the
/// real Code-Repo tree. To avoid a live git-blame dependency (which requires committed
/// history), tests use `-ThresholdDays 0` so the age check passes for any entry whose
/// blame timestamp is readable. Tests that specifically probe the table format seed a
/// real git repo in the temp directory so blame returns valid output.
/// </summary>
[TestClass]
public sealed class ExperimentalAgeAuditScriptTests
{
    private string _tempDir = string.Empty;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "RoslynMcpTests", "ExperimentalAgeAudit", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        if (!Directory.Exists(_tempDir)) return;
        // .git directories may contain readonly files on Windows; clear attributes before delete.
        foreach (var file in Directory.EnumerateFiles(_tempDir, "*", SearchOption.AllDirectories))
        {
            try { File.SetAttributes(file, FileAttributes.Normal); } catch { /* best effort */ }
        }
        TestFixtureFileSystem.DeleteDirectoryIfExists(_tempDir);
    }

    [TestMethod]
    public void Script_FileExistsAtDocumentedPath()
    {
        var scriptPath = ResolveScriptPath();
        Assert.IsTrue(
            File.Exists(scriptPath),
            $"audit-experimental-age.ps1 was not found at '{scriptPath}'. " +
            "publish-preflight Step 8.5 references this exact path; without the file the docs ship dead links.");
    }

    [TestMethod]
    public void Script_NoEntriesFound_EmitsNoEntriesMessage()
    {
        // Catalog dir with a file that has NO experimental entries.
        var catalogDir = CreateCatalogDir();
        File.WriteAllText(
            Path.Combine(catalogDir, "ServerSurfaceCatalog.Empty.cs"),
            "// No experimental entries here.\n");

        var result = RunScript(repoRoot: _tempDir, thresholdDays: 0);

        Assert.AreEqual(0, result.ExitCode, $"Script exited non-zero: stderr={result.StdErr}");
        Assert.IsTrue(
            result.StdOut.Contains("No experimental entries found", StringComparison.OrdinalIgnoreCase),
            $"Expected 'no entries' message when catalog has no experimental entries. stdout: {result.StdOut}");
    }

    [TestMethod]
    public void Script_ExperimentalEntryPresent_WithThresholdZeroAndRealGit_EmitsEntryInTable()
    {
        // Seed a minimal git repo in _tempDir so git blame can return real output.
        InitGitRepo(_tempDir);

        var catalogDir = CreateCatalogDir();
        var catalogFile = Path.Combine(catalogDir, "ServerSurfaceCatalog.Test.cs");

        // Write a catalog partial with one experimental Tool entry.
        var content = "namespace X; public static partial class ServerSurfaceCatalog {\n" +
                      "    static object[] Tools = { Tool(\"test_audit_tool\", \"tools\", \"experimental\", true, false, \"desc\") };\n" +
                      "}\n";
        File.WriteAllText(catalogFile, content);
        CommitFile(_tempDir, catalogFile, "add test catalog entry");

        // ThresholdDays=0 means emit ALL entries regardless of age.
        var result = RunScript(repoRoot: _tempDir, thresholdDays: 0);

        Assert.AreEqual(0, result.ExitCode, $"Script exited non-zero: stderr={result.StdErr}");
        Assert.IsTrue(
            result.StdOut.Contains("test_audit_tool", StringComparison.Ordinal),
            $"Expected 'test_audit_tool' in output table. stdout:\n{result.StdOut}");
        Assert.IsTrue(
            result.StdOut.Contains("Experimental-Tier Age Audit", StringComparison.OrdinalIgnoreCase),
            $"Expected table header in output. stdout:\n{result.StdOut}");
    }

    [TestMethod]
    public void Script_MultipleKinds_AllAppearInTableWithThresholdZero()
    {
        InitGitRepo(_tempDir);
        var catalogDir = CreateCatalogDir();

        // Seed one Tool, one Resource, one Prompt — all experimental.
        var content =
            "namespace X; public static partial class ServerSurfaceCatalog {\n" +
            "    static object a = Tool(\"my_tool\", \"tools\", \"experimental\", true, false, \"t\");\n" +
            "    static object b = Resource(\"my_resource\", \"resources\", \"experimental\", true, false, \"r\");\n" +
            "    static object c = Prompt(\"my_prompt\", \"prompts\", \"experimental\", true, false, \"p\");\n" +
            "}\n";
        var catalogFile = Path.Combine(catalogDir, "ServerSurfaceCatalog.Multi.cs");
        File.WriteAllText(catalogFile, content);
        CommitFile(_tempDir, catalogFile, "add multi-kind catalog entries");

        var result = RunScript(repoRoot: _tempDir, thresholdDays: 0);

        Assert.AreEqual(0, result.ExitCode, $"Script exited non-zero: stderr={result.StdErr}");
        Assert.IsTrue(result.StdOut.Contains("my_tool", StringComparison.Ordinal),
            $"Expected 'my_tool' in output. stdout:\n{result.StdOut}");
        Assert.IsTrue(result.StdOut.Contains("my_resource", StringComparison.Ordinal),
            $"Expected 'my_resource' in output. stdout:\n{result.StdOut}");
        Assert.IsTrue(result.StdOut.Contains("my_prompt", StringComparison.Ordinal),
            $"Expected 'my_prompt' in output. stdout:\n{result.StdOut}");
    }

    [TestMethod]
    public void Script_HighThreshold_SkipsRecentEntries()
    {
        InitGitRepo(_tempDir);
        var catalogDir = CreateCatalogDir();

        var catalogFile = Path.Combine(catalogDir, "ServerSurfaceCatalog.Recent.cs");
        File.WriteAllText(
            catalogFile,
            "namespace X; public static partial class S {\n" +
            "    static object x = Tool(\"recent_tool\", \"tools\", \"experimental\", true, false, \"d\");\n" +
            "}\n");
        CommitFile(_tempDir, catalogFile, "add recent tool");

        // ThresholdDays=9999 — a brand-new commit will NEVER be this old.
        var result = RunScript(repoRoot: _tempDir, thresholdDays: 9999);

        Assert.AreEqual(0, result.ExitCode, $"Script exited non-zero: stderr={result.StdErr}");
        Assert.IsFalse(
            result.StdOut.Contains("recent_tool", StringComparison.Ordinal),
            $"Expected 'recent_tool' NOT to appear when threshold is 9999 days. stdout:\n{result.StdOut}");
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static string ResolveScriptPath()
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        return Path.Combine(repoRoot, "eng", "audit-experimental-age.ps1");
    }

    private string CreateCatalogDir()
    {
        var dir = Path.Combine(_tempDir, "src", "RoslynMcp.Host.Stdio", "Catalog");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void InitGitRepo(string dir)
    {
        RunGit(dir, "init", "-b", "main");
        RunGit(dir, "config", "user.email", "test@test.local");
        RunGit(dir, "config", "user.name", "Test");
        // Create an initial commit so HEAD exists.
        var readme = Path.Combine(dir, "README.md");
        File.WriteAllText(readme, "test");
        RunGit(dir, "add", "README.md");
        RunGit(dir, "commit", "-m", "init");
    }

    private static void CommitFile(string repoDir, string absoluteFilePath, string message)
    {
        // Stage using relative path from repo root.
        var relPath = Path.GetRelativePath(repoDir, absoluteFilePath).Replace('\\', '/');
        RunGit(repoDir, "add", relPath);
        RunGit(repoDir, "commit", "-m", message);
    }

    private static void RunGit(string workDir, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git.");
        proc.WaitForExit(10_000);
    }

    private PwshResult RunScript(string repoRoot, int thresholdDays)
    {
        var scriptPath = ResolveScriptPath();
        var pwsh = OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh";

        var psi = new ProcessStartInfo
        {
            FileName = pwsh,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(scriptPath);
        psi.ArgumentList.Add("-RepoRoot");
        psi.ArgumentList.Add(repoRoot);
        psi.ArgumentList.Add("-ThresholdDays");
        psi.ArgumentList.Add(thresholdDays.ToString());

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start '{pwsh}'.");
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        if (!proc.WaitForExit(milliseconds: 60_000))
        {
            proc.Kill(entireProcessTree: true);
            throw new TimeoutException("audit-experimental-age.ps1 timed out after 60s.");
        }

        return new PwshResult(proc.ExitCode, stdout, stderr);
    }

    private sealed record PwshResult(int ExitCode, string StdOut, string StdErr);
}
