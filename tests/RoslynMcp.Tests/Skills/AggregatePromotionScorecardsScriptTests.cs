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
        _siblingParent = Path.Combine(TestTempRoot.Current, "AggregatePromotionScorecards", Guid.NewGuid().ToString("N"));
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
    public void Aggregate_PrefersCanonicalRepoRootScorecard_OverStaleAiDocsCopy()
    {
        // Regression guard for the $ScorecardSearchPaths ordering (PR #937): the canonical
        // repo-root `audit-reports/_latest-promotion-scorecard.json` MUST be probed BEFORE the
        // deprecated `ai_docs/audit-reports/...` fallback, so a stale ai_docs copy never shadows
        // the live scorecard. One sibling repo carries BOTH files with distinguishable tool names:
        //   * canonical (audit-reports/)        -> tool `canonical_tool`, recommendation `promote`
        //   * stale fallback (ai_docs/audit-reports/) -> tool `stale_ai_docs_tool`, recommendation `promote`
        // "First match wins per repo" means the aggregator must consume ONLY the canonical entry.
        SeedCanonicalScorecard("repo-a", new[] { ("tool", "canonical_tool", "analysis", "experimental", "promote") });
        SeedScorecard("repo-a", new[] { ("tool", "stale_ai_docs_tool", "analysis", "experimental", "promote") });

        var result = RunAggregator();
        Assert.AreEqual(0, result.ExitCode, $"Aggregator failed: stdout={result.StdOut} stderr={result.StdErr}");

        using var doc = JsonDocument.Parse(result.StdOut);
        var entries = doc.RootElement.GetProperty("entries").EnumerateArray().ToArray();

        // Exactly one entry — the canonical one. If the order regressed and the stale ai_docs copy
        // were probed first, this entry would be `stale_ai_docs_tool` instead, failing the asserts.
        Assert.AreEqual(1, entries.Length,
            $"Expected exactly 1 aggregated entry from the single sibling's winning scorecard, got {entries.Length}. JSON: {result.StdOut}");
        var names = entries.Select(e => e.GetProperty("name").GetString()).ToArray();
        CollectionAssert.Contains(names, "canonical_tool",
            $"Aggregator must consume the canonical repo-root scorecard (tool `canonical_tool`). Names: [{string.Join(", ", names)}]. JSON: {result.StdOut}");
        CollectionAssert.DoesNotContain(names, "stale_ai_docs_tool",
            $"Aggregator must NOT consume the stale ai_docs/audit-reports fallback (tool `stale_ai_docs_tool`) when the canonical copy exists — the $ScorecardSearchPaths order regressed. Names: [{string.Join(", ", names)}]. JSON: {result.StdOut}");

        // The sibling counts as having a scorecard (the canonical one), not missing.
        var withScorecard = doc.RootElement.GetProperty("siblingReposWithScorecard").EnumerateArray()
            .Select(e => e.GetString() ?? string.Empty)
            .ToArray();
        CollectionAssert.Contains(withScorecard, "repo-a");
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

    [TestMethod]
    public void BacklogProposal_BlockedAndNeedsMoreEvidenceEntriesProduceStableRows()
    {
        var aggregatedPath = Path.Combine(_siblingParent, "_aggregated-promotion-scorecard.json");
        var aggregatedJson = """
            {
              "schemaVersion": 1,
              "generatedAt": "2026-05-22T00:00:00Z",
              "entries": [
                {
                  "kind": "tool",
                  "name": "split_class_preview",
                  "category": "refactoring",
                  "currentTier": "experimental",
                  "verdict": "promote: blocked",
                  "promoteVotes": 1,
                  "keepExperimentalVotes": 1,
                  "deprecateVotes": 0,
                  "needsMoreEvidenceVotes": 0,
                  "sourceRepos": {
                    "promote": [ "repo-a" ],
                    "keep-experimental": [ "repo-b" ],
                    "needs-more-evidence": [],
                    "deprecate": []
                  },
                  "blockers": [ "repo-b: source edit preview produced a stale token" ]
                },
                {
                  "kind": "prompt",
                  "name": "get_prompt_text",
                  "category": "prompts",
                  "currentTier": "experimental",
                  "verdict": "needs-more-evidence",
                  "promoteVotes": 1,
                  "keepExperimentalVotes": 0,
                  "deprecateVotes": 0,
                  "needsMoreEvidenceVotes": 1,
                  "sourceRepos": {
                    "promote": [ "repo-a" ],
                    "keep-experimental": [],
                    "needs-more-evidence": [ "repo-c" ],
                    "deprecate": []
                  },
                  "blockers": []
                },
                {
                  "kind": "tool",
                  "name": "semantic_grep",
                  "category": "analysis",
                  "currentTier": "experimental",
                  "verdict": "promote: ready",
                  "promoteVotes": 2,
                  "keepExperimentalVotes": 0,
                  "deprecateVotes": 0,
                  "needsMoreEvidenceVotes": 0,
                  "sourceRepos": {
                    "promote": [ "repo-a", "repo-b" ],
                    "keep-experimental": [],
                    "needs-more-evidence": [],
                    "deprecate": []
                  },
                  "blockers": []
                }
              ],
              "summary": {
                "promoteReady": 1,
                "promoteBlocked": 1,
                "needsMoreEvidence": 1,
                "noScorecardsAvailable": false
              }
            }
            """;
        File.WriteAllText(aggregatedPath, aggregatedJson);

        var result = RunBacklogProposalScript(aggregatedPath);
        Assert.AreEqual(0, result.ExitCode, $"Proposal script failed: stdout={result.StdOut} stderr={result.StdErr}");

        using var doc = JsonDocument.Parse(result.StdOut);
        var proposals = doc.RootElement.GetProperty("proposals").EnumerateArray().ToArray();
        Assert.AreEqual(2, proposals.Length, $"Expected only blocked/needs-more-evidence proposals. JSON: {result.StdOut}");

        var ids = proposals.Select(p => p.GetProperty("id").GetString()).ToArray();
        CollectionAssert.Contains(ids, "promotion-scorecard-tool-split-class-preview-blocked");
        CollectionAssert.Contains(ids, "promotion-scorecard-prompt-get-prompt-text-needs-more-evidence");

        var blocked = proposals.Single(p => p.GetProperty("id").GetString() == "promotion-scorecard-tool-split-class-preview-blocked");
        StringAssert.Contains(blocked.GetProperty("do").GetString()!, "repo-b: source edit preview produced a stale token");
        StringAssert.Contains(blocked.GetProperty("do").GetString()!, "`promote: blocked`");
        Assert.IsFalse(blocked.GetProperty("do").GetString()!.Contains("$verdict", StringComparison.Ordinal));
        StringAssert.Contains(blocked.GetProperty("do").GetString()!, "audit-reports/_aggregated-promotion-scorecard.json");

        var skipped = doc.RootElement.GetProperty("skipped").EnumerateArray().ToArray();
        Assert.AreEqual(1, skipped.Length);
        Assert.AreEqual("semantic_grep", skipped[0].GetProperty("name").GetString());
        Assert.AreEqual("promote-ready", skipped[0].GetProperty("reason").GetString());
    }

    [TestMethod]
    public void Aggregator_IncludeSelf_SelfFolderInSiblingParent_CountedExactlyOnce()
    {
        // Regression guard for the -IncludeSelf double-count bug:
        // When -IncludeSelf is passed the script explicitly adds the real repo root at line ~145.
        // The sibling-discovery loop also enumerates $SiblingRepoParent (which defaults to the
        // parent of the real repo folder). If the test creates a folder under the temp
        // _siblingParent whose name matches the real repo's leaf name — simulating the scenario
        // where $SiblingRepoParent IS the real parent — the bug would add the repo twice to
        // $roots, doubling its entry in siblingReposScanned and potentially its vote count.
        //
        // HARD INVARIANT: this test never touches the real Code-Repo tree; the script resolves
        // the real repo root itself (via $PSScriptRoot), but we only seed a decoy folder under
        // the isolated temp _siblingParent. Whether the real repo root has a scorecard on disk
        // or not, the invariant is: repoLeafName appears EXACTLY ONCE in siblingReposScanned.

        var realRepoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var repoLeafName = Path.GetFileName(realRepoRoot);

        // Create a folder under the temp parent with the SAME name as the real repo leaf.
        // This is the "self re-discovered as a sibling" trap.
        var selfDecoyDir = Path.Combine(_siblingParent, repoLeafName);
        Directory.CreateDirectory(selfDecoyDir);

        // Optionally seed a scorecard in the decoy — makes the assertion more robust if the
        // real repo has no scorecard, and exercises both the scanned and withScorecard lists.
        var decoyAuditDir = Path.Combine(selfDecoyDir, "audit-reports");
        Directory.CreateDirectory(decoyAuditDir);
        var decoyScorecard = new
        {
            schemaVersion = 1,
            generatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            auditedRepo = repoLeafName,
            scorecard = new[]
            {
                new { kind = "tool", name = "decoy_tool", category = "test", currentTier = "experimental",
                      recommendation = "promote", evidenceCount = 1,
                      evidence = new[] { "synthetic decoy" }, blockers = Array.Empty<string>() }
            },
            summary = new { promote = 1, deprecate = 0 }
        };
        File.WriteAllText(
            Path.Combine(decoyAuditDir, "_latest-promotion-scorecard.json"),
            System.Text.Json.JsonSerializer.Serialize(decoyScorecard, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

        var result = RunAggregatorWithIncludeSelf();
        Assert.AreEqual(0, result.ExitCode,
            $"Aggregator failed with -IncludeSelf: stdout={result.StdOut} stderr={result.StdErr}");

        using var doc = System.Text.Json.JsonDocument.Parse(result.StdOut);

        var scanned = doc.RootElement.GetProperty("siblingReposScanned")
            .EnumerateArray()
            .Select(e => e.GetString() ?? string.Empty)
            .ToArray();

        var occurrences = scanned.Count(n => string.Equals(n, repoLeafName, StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(1, occurrences,
            $"'{repoLeafName}' must appear EXACTLY ONCE in siblingReposScanned. " +
            $"Got {occurrences} occurrence(s) — bug: -IncludeSelf + sibling-discovery both adding self. " +
            $"siblingReposScanned=[{string.Join(", ", scanned)}]. JSON: {result.StdOut}");
    }

    [TestMethod]
    public void Aggregator_StaleServerVersionAndGeneratedAt_ReportsScorecardStaleness()
    {
        // The scorecard is the release-gating input, but the aggregator historically read
        // `serverVersion`/`generatedAt` without ever comparing them — a sibling frozen at an old
        // build kept voting in the quorum with no signal that its evidence was stale.
        // One sibling seeded with BOTH staleness axes: an old serverVersion and an ancient
        // generatedAt (relative to now, so the assertion never rots as the calendar moves).
        var staleGeneratedAt = DateTime.UtcNow.AddDays(-120).ToString("yyyy-MM-ddTHH:mm:ssZ");
        SeedCanonicalScorecardWithMetadata(
            "repo-stale",
            new[] { ("tool", "semantic_grep", "analysis", "experimental", "promote") },
            serverVersion: "1.38.1",
            generatedAt: staleGeneratedAt);

        var result = RunAggregatorWithStalenessBaseline("2.3.8", maxScorecardAgeDays: 45);

        // Warn-only by contract: staleness must NOT change the exit code, or every consumer that
        // pipes the aggregator's stdout breaks the moment one sibling drifts.
        Assert.AreEqual(0, result.ExitCode,
            $"Staleness detection must stay warn-only (exit 0): stdout={result.StdOut} stderr={result.StdErr}");

        // Stdout is JSON and NOTHING else. The warning must go to stderr via [Console]::Error —
        // `Write-Warning` would have the host render it onto stdout with ANSI escapes under
        // `pwsh -File`, breaking every consumer that pipes this script's output through a parser.
        StringAssert.StartsWith(result.StdOut.TrimStart(), "{",
            $"Aggregator stdout must be pure JSON, but it began with non-JSON output: {result.StdOut}");
        StringAssert.Contains(result.StdErr, "Stale promotion scorecard for 'repo-stale'",
            $"The staleness warning must be emitted on stderr. stderr={result.StdErr}");

        using var doc = JsonDocument.Parse(result.StdOut);

        var staleness = doc.RootElement.GetProperty("scorecardStaleness").EnumerateArray().ToArray();
        Assert.AreEqual(1, staleness.Length,
            $"Expected exactly 1 scorecardStaleness row (one sibling with metadata). JSON: {result.StdOut}");

        var row = staleness[0];
        Assert.AreEqual("repo-stale", row.GetProperty("repo").GetString());
        Assert.AreEqual("1.38.1", row.GetProperty("serverVersion").GetString());
        Assert.AreEqual("2.3.8", row.GetProperty("currentServerVersion").GetString());
        Assert.IsTrue(row.GetProperty("versionStale").GetBoolean(),
            $"serverVersion 1.38.1 vs current 2.3.8 must report versionStale. JSON: {result.StdOut}");
        Assert.AreEqual(staleGeneratedAt, row.GetProperty("generatedAt").GetString());
        Assert.IsTrue(row.GetProperty("ageStale").GetBoolean(),
            $"A generatedAt 120 days old must report ageStale against -MaxScorecardAgeDays 45. JSON: {result.StdOut}");
        Assert.IsTrue(row.GetProperty("ageDays").GetDouble() > 45,
            $"ageDays must exceed the 45-day threshold. JSON: {result.StdOut}");

        var summary = doc.RootElement.GetProperty("summary");
        Assert.IsTrue(summary.GetProperty("staleScorecardCount").GetInt32() >= 1,
            $"summary.staleScorecardCount must count the stale sibling. JSON: {result.StdOut}");

        // The stale sibling still contributes its vote — staleness is a signal, not a filter.
        var entries = doc.RootElement.GetProperty("entries").EnumerateArray().ToArray();
        Assert.AreEqual(1, entries.Length, $"Stale scorecards must still be aggregated. JSON: {result.StdOut}");
        Assert.AreEqual("semantic_grep", entries[0].GetProperty("name").GetString());
    }

    private static string ResolveScriptPath()
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        return Path.Combine(repoRoot, "eng", "aggregate-promotion-scorecards.ps1");
    }

    private static string ResolveBacklogProposalScriptPath()
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        return Path.Combine(repoRoot, "eng", "propose-promotion-scorecard-backlog-rows.ps1");
    }

    /// <summary>
    /// Seeds a synthetic sibling repo at <c>_siblingParent/&lt;repoName&gt;</c> with a scorecard
    /// JSON at the DEPRECATED <c>ai_docs/audit-reports/</c> fallback path (the location #937
    /// demoted from canonical). Tuple shape: <c>(kind, name, category, currentTier, recommendation)</c>.
    /// </summary>
    private void SeedScorecard(string repoName, IEnumerable<(string kind, string name, string category, string currentTier, string recommendation)> entries)
        => WriteScorecard(repoName, Path.Combine("ai_docs", "audit-reports"), entries);

    /// <summary>
    /// Seeds a synthetic sibling repo with a scorecard at the CANONICAL repo-root
    /// <c>audit-reports/</c> path (#937 — written by /mcp-server-surface-test). Used to prove the
    /// aggregator prefers this over a stale <c>ai_docs/audit-reports/</c> copy.
    /// </summary>
    private void SeedCanonicalScorecard(string repoName, IEnumerable<(string kind, string name, string category, string currentTier, string recommendation)> entries)
        => WriteScorecard(repoName, "audit-reports", entries);

    /// <summary>
    /// Seeds a synthetic sibling repo with a CANONICAL repo-root scorecard carrying explicit
    /// staleness metadata (<c>serverVersion</c> / <c>generatedAt</c>). The default
    /// <see cref="WriteScorecard"/> emits a fresh <c>generatedAt</c> and no <c>serverVersion</c>
    /// at all, which is exactly the non-stale shape the other tests rely on.
    /// </summary>
    private void SeedCanonicalScorecardWithMetadata(
        string repoName,
        IEnumerable<(string kind, string name, string category, string currentTier, string recommendation)> entries,
        string serverVersion,
        string generatedAt)
        => WriteScorecard(repoName, "audit-reports", entries, serverVersion, generatedAt);

    private void WriteScorecard(
        string repoName,
        string relativeAuditDir,
        IEnumerable<(string kind, string name, string category, string currentTier, string recommendation)> entries,
        string? serverVersion = null,
        string? generatedAt = null)
    {
        var repoDir = Path.Combine(_siblingParent, repoName);
        var auditDir = Path.Combine(repoDir, relativeAuditDir);
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

        // Dictionary rather than an anonymous type so `serverVersion` can be omitted entirely —
        // the aggregator's staleness probe distinguishes "field absent" from "field present".
        var scorecard = new Dictionary<string, object>
        {
            ["schemaVersion"] = 1,
            ["generatedAt"] = generatedAt ?? DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["auditedRepo"] = repoName,
            ["scorecard"] = scorecardEntries,
            ["summary"] = new { promote = 0, deprecate = 0 }
        };

        if (serverVersion is not null)
        {
            scorecard["serverVersion"] = serverVersion;
        }

        var json = JsonSerializer.Serialize(scorecard, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(auditDir, "_latest-promotion-scorecard.json"), json);
    }

    private PwshResult RunAggregator()
        => RunPwshScript(
            ResolveScriptPath(),
            "aggregate-promotion-scorecards.ps1",
            "-SiblingRepoParent", _siblingParent);

    /// <summary>
    /// Variant of <see cref="RunAggregator"/> that passes <c>-IncludeSelf</c> to the script.
    /// Used by <see cref="Aggregator_IncludeSelf_SelfFolderInSiblingParent_CountedExactlyOnce"/>
    /// to exercise the self-exclusion-from-sibling-discovery invariant.
    /// </summary>
    private PwshResult RunAggregatorWithIncludeSelf()
        => RunPwshScript(
            ResolveScriptPath(),
            "aggregate-promotion-scorecards.ps1 -IncludeSelf",
            "-SiblingRepoParent", _siblingParent, "-IncludeSelf");

    /// <summary>
    /// Variant of <see cref="RunAggregator"/> that pins the staleness baseline explicitly, so the
    /// assertion does not couple to this checkout's live <c>Directory.Build.props</c> version.
    /// </summary>
    private PwshResult RunAggregatorWithStalenessBaseline(string currentServerVersion, int maxScorecardAgeDays)
        => RunPwshScript(
            ResolveScriptPath(),
            "aggregate-promotion-scorecards.ps1 -CurrentServerVersion -MaxScorecardAgeDays",
            "-SiblingRepoParent", _siblingParent,
            "-CurrentServerVersion", currentServerVersion,
            "-MaxScorecardAgeDays", maxScorecardAgeDays.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static PwshResult RunBacklogProposalScript(string aggregatedPath)
    {
        var scriptPath = ResolveBacklogProposalScriptPath();
        Assert.IsTrue(
            File.Exists(scriptPath),
            $"propose-promotion-scorecard-backlog-rows.ps1 was not found at the documented path '{scriptPath}'.");

        return RunPwshScript(
            scriptPath,
            "propose-promotion-scorecard-backlog-rows.ps1",
            "-AggregatedScorecardPath", aggregatedPath);
    }

    private static PwshResult RunPwshScript(
        string scriptPath,
        string operationName,
        params string[] scriptArguments)
    {
        var pwshExecutable = OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh";
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
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(scriptPath);
        foreach (var argument in scriptArguments)
        {
            psi.ArgumentList.Add(argument);
        }

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start '{pwshExecutable}'.");
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        if (!proc.WaitForExit(milliseconds: 60_000))
        {
            proc.Kill(entireProcessTree: true);
            throw new TimeoutException($"pwsh {operationName} invocation timed out after 60s.");
        }

        return new PwshResult(
            proc.ExitCode,
            stdoutTask.GetAwaiter().GetResult(),
            stderrTask.GetAwaiter().GetResult());
    }

    private sealed record PwshResult(int ExitCode, string StdOut, string StdErr);
}
