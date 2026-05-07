using System.Diagnostics;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace RoslynMcp.Tests.Skills;

/// <summary>
/// per-repo-promotion-scorecard: behavior tests for `eng/aggregate-promotion-scorecards.ps1`.
///
/// The script gathers per-repo `_latest-promotion-scorecard.json` files from sibling repos
/// under a parent folder and emits a quorum-aware aggregated verdict per `<kind>|<name>` entry:
///
///   * `promote: ready`         — at least 2 sibling repos voted `promote` AND zero
///                                `keep-experimental` AND zero `deprecate` votes.
///   * `promote: blocked`       — at least one `keep-experimental` or `deprecate` vote.
///   * `needs-more-evidence`    — fewer than 2 `promote` votes, no blockers.
///
/// HARD INVARIANT: every test seeds an isolated temp directory hierarchy as the synthetic
/// "sibling-repo parent"; the aggregator runs against that parent and never touches the
/// real Code-Repo tree. Pattern mirrors `ArchiveOldReportsScriptTests.cs`.
/// </summary>
[TestClass]
public sealed class AggregatePromotionScorecardsScriptTests
{
    private string _siblingParent = string.Empty;

    [TestInitialize]
    public void TestInitialize()
    {
        _siblingParent = Path.Combine(Path.GetTempPath(), "RoslynMcpTests", "AggregatePromotionScorecards", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_siblingParent);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        TestFixtureFileSystem.DeleteDirectoryIfExists(_siblingParent);
    }

    [TestMethod]
    public void Script_FileExistsAtDocumentedPath()
    {
        var scriptPath = ResolveScriptPath();
        Assert.IsTrue(
            File.Exists(scriptPath),
            $"aggregate-promotion-scorecards.ps1 was not found at the documented path '{scriptPath}'. " +
            "publish-preflight Step 8 references this exact path; without the file the docs ship dead links.");
    }

    [TestMethod]
    public void Aggregator_TwoPromoteOneNeedsMore_EmitsPromoteReady()
    {
        // 3 sibling repos: 2 vote `promote`, 1 votes `needs-more-evidence` for tool X.
        // Quorum requires ≥2 `promote` and 0 blockers — verdict should be `promote: ready`.
        SeedScorecard("repo-a", new[] { ("tool", "scaffold_test_apply", "scaffolding", "experimental", "promote") });
        SeedScorecard("repo-b", new[] { ("tool", "scaffold_test_apply", "scaffolding", "experimental", "promote") });
        SeedScorecard("repo-c", new[] { ("tool", "scaffold_test_apply", "scaffolding", "experimental", "needs-more-evidence") });

        var result = RunAggregator();
        Assert.AreEqual(0, result.ExitCode, $"Aggregator failed: stdout={result.StdOut} stderr={result.StdErr}");

        using var doc = JsonDocument.Parse(result.StdOut);
        var entries = doc.RootElement.GetProperty("entries").EnumerateArray().ToArray();

        Assert.AreEqual(1, entries.Length, $"Expected exactly 1 aggregated entry, got {entries.Length}. JSON: {result.StdOut}");
        var entry = entries[0];
        Assert.AreEqual("scaffold_test_apply", entry.GetProperty("name").GetString());
        Assert.AreEqual("promote: ready", entry.GetProperty("verdict").GetString());
        Assert.AreEqual(2, entry.GetProperty("promoteVotes").GetInt32());
        Assert.AreEqual(0, entry.GetProperty("keepExperimentalVotes").GetInt32());

        var summary = doc.RootElement.GetProperty("summary");
        Assert.AreEqual(1, summary.GetProperty("promoteReady").GetInt32());
        Assert.AreEqual(0, summary.GetProperty("promoteBlocked").GetInt32());
    }

    [TestMethod]
    public void Aggregator_OnePromoteOneKeepExperimental_EmitsPromoteBlocked()
    {
        // 3 siblings: 1 `promote`, 1 `keep-experimental`, 1 missing scorecard entirely.
        // The single keep-experimental blocks the quorum regardless of promote count.
        SeedScorecard("repo-a", new[] { ("tool", "split_class_preview", "refactoring", "experimental", "promote") });
        SeedScorecard("repo-b", new[] { ("tool", "split_class_preview", "refactoring", "experimental", "keep-experimental") });
        // repo-c has no scorecard file on disk.
        Directory.CreateDirectory(Path.Combine(_siblingParent, "repo-c"));

        var result = RunAggregator();
        Assert.AreEqual(0, result.ExitCode, $"Aggregator failed: stdout={result.StdOut} stderr={result.StdErr}");

        using var doc = JsonDocument.Parse(result.StdOut);
        var entries = doc.RootElement.GetProperty("entries").EnumerateArray().ToArray();
        Assert.AreEqual(1, entries.Length);
        var entry = entries[0];
        Assert.AreEqual("split_class_preview", entry.GetProperty("name").GetString());
        Assert.AreEqual("promote: blocked", entry.GetProperty("verdict").GetString());
        Assert.AreEqual(1, entry.GetProperty("promoteVotes").GetInt32());
        Assert.AreEqual(1, entry.GetProperty("keepExperimentalVotes").GetInt32());

        var missing = doc.RootElement.GetProperty("siblingReposMissingScorecard").EnumerateArray()
            .Select(e => e.GetString() ?? string.Empty)
            .ToArray();
        Assert.IsTrue(missing.Any(m => m.StartsWith("repo-c", StringComparison.Ordinal)),
            $"repo-c should appear in siblingReposMissingScorecard. Got: [{string.Join(", ", missing)}]");
    }

    [TestMethod]
    public void Aggregator_FourSiblingsOneScorecard_EmitsNeedsMoreEvidence()
    {
        // 4 sibling repos, only 1 has a scorecard with a `promote` vote for tool X.
        // <2 promote votes and no blockers → `needs-more-evidence` (insufficient sample size).
        SeedScorecard("repo-a", new[] { ("tool", "find_dead_locals", "diagnostics", "experimental", "promote") });
        Directory.CreateDirectory(Path.Combine(_siblingParent, "repo-b"));
        Directory.CreateDirectory(Path.Combine(_siblingParent, "repo-c"));
        Directory.CreateDirectory(Path.Combine(_siblingParent, "repo-d"));

        var result = RunAggregator();
        Assert.AreEqual(0, result.ExitCode, $"Aggregator failed: stdout={result.StdOut} stderr={result.StdErr}");

        using var doc = JsonDocument.Parse(result.StdOut);
        var entries = doc.RootElement.GetProperty("entries").EnumerateArray().ToArray();
        Assert.AreEqual(1, entries.Length);
        var entry = entries[0];
        Assert.AreEqual("find_dead_locals", entry.GetProperty("name").GetString());
        Assert.AreEqual("needs-more-evidence", entry.GetProperty("verdict").GetString());
        Assert.AreEqual(1, entry.GetProperty("promoteVotes").GetInt32());

        var summary = doc.RootElement.GetProperty("summary");
        Assert.AreEqual(0, summary.GetProperty("promoteReady").GetInt32());
        Assert.AreEqual(1, summary.GetProperty("needsMoreEvidence").GetInt32());
        Assert.IsFalse(summary.GetProperty("noScorecardsAvailable").GetBoolean(),
            "noScorecardsAvailable should be false when at least one sibling has a scorecard.");
    }

    [TestMethod]
    public void Aggregator_EmptySiblingSet_EmitsNoScorecardsAvailable()
    {
        // Empty parent — no sibling repos at all. Aggregator should emit a clean
        // "no scorecards available" verdict with zero entries and no crash.
        var result = RunAggregator();
        Assert.AreEqual(0, result.ExitCode, $"Aggregator failed on empty parent: stdout={result.StdOut} stderr={result.StdErr}");

        using var doc = JsonDocument.Parse(result.StdOut);
        var entries = doc.RootElement.GetProperty("entries").EnumerateArray().ToArray();
        Assert.AreEqual(0, entries.Length, "Empty sibling set must produce zero aggregated entries.");

        var summary = doc.RootElement.GetProperty("summary");
        Assert.IsTrue(summary.GetProperty("noScorecardsAvailable").GetBoolean(),
            "noScorecardsAvailable must be true when no sibling has a scorecard on file.");
        Assert.AreEqual(0, summary.GetProperty("promoteReady").GetInt32());
        Assert.AreEqual(0, summary.GetProperty("promoteBlocked").GetInt32());
    }

    private static string ResolveScriptPath()
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        return Path.Combine(repoRoot, "eng", "aggregate-promotion-scorecards.ps1");
    }

    /// <summary>
    /// Seeds a synthetic sibling repo at <c>_siblingParent/&lt;repoName&gt;</c> with a
    /// scorecard JSON containing the supplied entries. Tuple shape:
    /// <c>(kind, name, category, currentTier, recommendation)</c>.
    /// </summary>
    private void SeedScorecard(string repoName, IEnumerable<(string kind, string name, string category, string currentTier, string recommendation)> entries)
    {
        var repoDir = Path.Combine(_siblingParent, repoName);
        var auditDir = Path.Combine(repoDir, "ai_docs", "audit-reports");
        Directory.CreateDirectory(auditDir);

        var scorecardEntries = entries.Select(e => new
        {
            kind = e.kind,
            name = e.name,
            category = e.category,
            currentTier = e.currentTier,
            recommendation = e.recommendation,
            evidenceCount = 1,
            evidence = new[] { "synthetic test fixture" },
            blockers = e.recommendation == "promote" ? Array.Empty<string>() : new[] { "synthetic blocker" }
        }).ToArray();

        var scorecard = new
        {
            schemaVersion = 1,
            generatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            auditedRepo = repoName,
            scorecard = scorecardEntries,
            summary = new { promote = 0, deprecate = 0 }
        };

        var json = JsonSerializer.Serialize(scorecard, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(auditDir, "_latest-promotion-scorecard.json"), json);
    }

    private PwshResult RunAggregator()
    {
        var scriptPath = ResolveScriptPath();
        var args = new List<string>
        {
            "-NoProfile",
            "-NonInteractive",
            "-File", scriptPath,
            "-SiblingRepoParent", _siblingParent
        };

        var pwshExecutable = OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh";

        var psi = new ProcessStartInfo
        {
            FileName = pwshExecutable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start '{pwshExecutable}'.");
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        if (!proc.WaitForExit(milliseconds: 60_000))
        {
            proc.Kill(entireProcessTree: true);
            throw new TimeoutException("pwsh aggregate-promotion-scorecards.ps1 invocation timed out after 60s.");
        }

        return new PwshResult(proc.ExitCode, stdout, stderr);
    }

    private sealed record PwshResult(int ExitCode, string StdOut, string StdErr);
}
