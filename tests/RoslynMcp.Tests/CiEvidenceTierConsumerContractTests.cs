using System.Text.RegularExpressions;

namespace RoslynMcp.Tests;

/// <summary>
/// Guards the CI evidence tier (<c>eng/resolve-ci-topology.ps1</c>): pull requests that change only
/// evidence paths skip every .NET build/test leg, which is safe only while no test or eng/ script
/// reads those paths. Every test, eng/ script, or workflow that references an evidence root (or
/// walks <c>ai_docs</c> recursively) must be listed here with the reason its reference is safe. A new,
/// unreviewed consumer fails this test, so it cannot quietly land behind the fast route: classify it,
/// or move the file it reads into the router's evidence exclusions (docs tier).
/// </summary>
[TestClass]
public sealed class CiEvidenceTierConsumerContractTests
{
    // Pinned copy of the router's tier definition. Changing the tier must be a deliberate edit to both.
    private const string ExpectedEvidencePattern =
        "$evidencePattern = '^(ai_docs/audits/|ai_docs/reports/|ai_docs/items/|audit-reports/)'";

    private static readonly string[] _expectedEvidenceExclusions =
    [
        "'ai_docs/items/backlog-d-fragment-schema.md'",
        "'audit-reports/_latest-promotion-scorecard.json'",
    ];

    private static readonly Regex _evidenceRootReference = new(
        @"ai_docs[\\/""', ]+(audits|reports|items)\b|audit-reports",
        RegexOptions.CultureInvariant);

    private static readonly Regex _recursiveAiDocsWalk = new(
        @"AllDirectories|-Recurse\b",
        RegexOptions.CultureInvariant);

    // Relative path -> why the reference cannot make an evidence-only change break CI.
    private static readonly Dictionary<string, string> _reviewedConsumers = new(StringComparer.Ordinal)
    {
        ["eng/aggregate-promotion-scorecards.ps1"] = "Maintainer tool; CI runs it only against synthetic temp roots (AggregatePromotionScorecardsScriptTests).",
        ["eng/intake-from-issues.py"] = "Maintainer intake tool; never run by CI.",
        ["eng/process-audit-reports.ps1"] = "Maintainer tool; never run by CI.",
        ["eng/propose-promotion-scorecard-backlog-rows.ps1"] = "Maintainer tool; never run by CI.",
        ["eng/resolve-ci-topology.ps1"] = "Defines the evidence tier.",
        ["eng/stage-review-inbox.ps1"] = "Maintainer tool; never run by CI.",
        ["eng/verify-ai-docs.ps1"] = "Reads only ai_docs/items/backlog-d-fragment-schema.md (an evidence exclusion); runs on the evidence route itself.",
        ["eng/verify-changelog-fragments.ps1"] = "Path exemption list only; runs on the evidence route itself.",
        ["tests/RoslynMcp.Tests/ChangelogFragmentRequirementTests.cs"] = "Synthetic temp repositories only.",
        ["tests/RoslynMcp.Tests/CiEvidenceTierConsumerContractTests.cs"] = "This guard.",
        ["tests/RoslynMcp.Tests/CiTopologyDecisionContractTests.cs"] = "Synthetic router payloads only.",
        ["tests/RoslynMcp.Tests/ParameterObjectPreviewTests.cs"] = "Doc-comment mention only.",
        ["tests/RoslynMcp.Tests/ReadmeSurfaceCountTests.cs"] = "Recursive ai_docs walk filtered to backlog.md, planning_index.md and plans/ (docs tier).",
        ["tests/RoslynMcp.Tests/Skills/AggregatePromotionScorecardsScriptTests.cs"] = "Synthetic temp roots only.",
        ["tests/RoslynMcp.Tests/Skills/ArchiveOldReportsScriptTests.cs"] = "Synthetic temp roots only.",
        ["tests/RoslynMcp.Tests/Skills/IssueTemplateAndLabelSeedTests.cs"] = "Reads ai_docs/items/backlog-d-fragment-schema.md (an evidence exclusion).",
        ["tests/RoslynMcp.Tests/Skills/McpServerSurfaceTestSkillTests.cs"] = "Asserts the promotion scorecard (an evidence exclusion) stays git-tracked.",
        ["tests/RoslynMcp.Tests/Skills/ShippedSkillBacklogCitationTests.cs"] = "Reads ai_docs/backlog.md (docs tier); its recursive walk is over skills/, not ai_docs.",
    };

    [TestMethod]
    public void EvidenceTierConsumers_AreExactlyTheReviewedSet()
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var candidates = EnumerateSources(repoRoot, "tests", "*.cs")
            .Concat(EnumerateSources(repoRoot, "eng", "*.ps1"))
            .Concat(EnumerateSources(repoRoot, "eng", "*.psm1"))
            .Concat(EnumerateSources(repoRoot, "eng", "*.py"))
            .Concat(EnumerateSources(repoRoot, "eng", "*.sh"))
            .Concat(EnumerateSources(repoRoot, "eng", "*.mjs"))
            .Concat(EnumerateSources(repoRoot, Path.Combine(".github", "workflows"), "*.yml"));

        var actual = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var path in candidates)
        {
            var text = File.ReadAllText(path);
            var referencesEvidenceRoot = _evidenceRootReference.IsMatch(text);
            var walksAiDocsRecursively = text.Contains("\"ai_docs\"", StringComparison.Ordinal)
                && _recursiveAiDocsWalk.IsMatch(text);
            if (referencesEvidenceRoot || walksAiDocsRecursively)
            {
                actual.Add(Path.GetRelativePath(repoRoot, path).Replace('\\', '/'));
            }
        }

        var unreviewed = actual.Except(_reviewedConsumers.Keys, StringComparer.Ordinal).ToArray();
        var stale = _reviewedConsumers.Keys.Except(actual, StringComparer.Ordinal).ToArray();
        Assert.IsEmpty(
            unreviewed,
            "New reference(s) to a CI evidence-tier root. If CI reads the evidence file, add it to " +
            "$evidenceExclusions in eng/resolve-ci-topology.ps1 (docs tier); otherwise classify the " +
            "reference in _reviewedConsumers: " + string.Join(", ", unreviewed));
        Assert.IsEmpty(
            stale,
            "Reviewed consumer(s) no longer reference an evidence root; remove them from _reviewedConsumers: " +
            string.Join(", ", stale));
    }

    [TestMethod]
    public void RouterEvidenceTierDefinition_MatchesThisGuard()
    {
        var router = File.ReadAllText(Path.Combine(
            TestFixtureFileSystem.FindRepositoryRoot(), "eng", "resolve-ci-topology.ps1"));

        StringAssert.Contains(router, ExpectedEvidencePattern);
        foreach (var exclusion in _expectedEvidenceExclusions)
        {
            StringAssert.Contains(router, exclusion);
        }
    }

    private static IEnumerable<string> EnumerateSources(string repoRoot, string relativeDirectory, string pattern)
    {
        var directory = Path.Combine(repoRoot, relativeDirectory);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(directory, pattern, SearchOption.AllDirectories)
            .Where(path =>
            {
                var relative = Path.GetRelativePath(repoRoot, path).Replace('\\', '/');
                return !relative.Contains("/bin/", StringComparison.Ordinal)
                    && !relative.Contains("/obj/", StringComparison.Ordinal);
            });
    }
}
