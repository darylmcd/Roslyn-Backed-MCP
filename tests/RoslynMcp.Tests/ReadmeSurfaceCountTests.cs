using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Tests;

/// <summary>
/// readme-surface-counts-drift-from-live-catalog: user-facing docs and plugin metadata
/// hand-advertise live counts ("173 tools (113 stable / 60 experimental)", "32 skills").
/// Without a test gate those numbers drift from <see cref="ServerSurfaceCatalog"/> and the
/// shipped <c>skills/</c> directory. This fixture parses those numeric patterns and asserts
/// they match the live sources; on mismatch the failure names which number is off and by how
/// much. When a document is restructured, update the relevant pattern and keep the assertion
/// contract.
/// </summary>
[TestClass]
public sealed class ReadmeSurfaceCountTests
{
    // Golden regex contract (keep in sync with the user-facing surface-count paragraphs).
    // Matches phrases like:
    //   "159 tools (107 stable / 52 experimental)"
    //   "13 resources (9 stable / 4 experimental)"
    //   "20 prompts (all experimental)"
    //
    // kind       => tools | resources | prompts
    // total      => the leading integer immediately before the kind keyword
    // stable     => the "X stable" integer (absent for the prompts "all experimental" form)
    // experimental => the "Y experimental" integer OR "all" when prompts are 100% experimental
    //
    // A wording change that preserves these patterns keeps the test passing; a refactor to a
    // table or bullet list MUST update this regex and the per-kind assertions below.
    private static readonly Regex CountPattern = new(
        @"\*\*(?<total>\d+)\s+(?<kind>tools|resources|prompts)\*\*\s*\((?:(?<stable>\d+)\s+stable\s*/\s*(?<experimental>\d+)\s+experimental|(?<all>all)\s+(?<allTier>experimental|stable))\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SkillCountPattern = new(
        @"(?<total>\d+)\s+(?:bundled\s+agent\s+)?skills\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex BacklogCurrentToolClaimPattern = new(
        @"(?<count>\d+)\s+tools\s+is\s+approaching\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex BacklogSurfaceEvolutionPattern = new(
        @"server_info\.surface\.registered\.tools`\s+(?<from>\d+)\s*->\s*(?<to>\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [TestMethod]
    public void RootReadmeSurfaceCounts_MatchLiveServerSurfaceCatalog()
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var readmePath = Path.Combine(repoRoot, "README.md");

        AssertDocumentSurfaceCountsMatchCatalog(readmePath, "README.md");
    }

    [TestMethod]
    public void HostStdioReadmeSurfaceCounts_MatchLiveServerSurfaceCatalog()
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var readmePath = Path.Combine(repoRoot, "src", "RoslynMcp.Host.Stdio", "README.md");

        AssertDocumentSurfaceCountsMatchCatalog(readmePath, "src/RoslynMcp.Host.Stdio/README.md");
    }

    [TestMethod]
    public void ReadmePackageAndPluginSkillCounts_MatchShippedSkillDirectory()
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var expectedSkillCount = CountShippedSkills(repoRoot);
        var failures = new List<string>();

        CompareSkillClaims(
            path: Path.Combine(repoRoot, "README.md"),
            displayPath: "README.md",
            expectedSkillCount,
            failures);

        CompareSkillClaims(
            path: Path.Combine(repoRoot, "src", "RoslynMcp.Host.Stdio", "README.md"),
            displayPath: "src/RoslynMcp.Host.Stdio/README.md",
            expectedSkillCount,
            failures);

        var pluginPath = Path.Combine(repoRoot, ".claude-plugin", "plugin.json");
        var description = ReadPluginDescription(pluginPath);
        CompareSkillMatches(
            SkillCountPattern.Matches(description),
            displayPath: ".claude-plugin/plugin.json description",
            expectedSkillCount,
            failures);

        Assert.AreEqual(
            0,
            failures.Count,
            "Documented plugin skill counts drifted from the shipped skills/ directory:\n  "
            + string.Join("\n  ", failures));
    }

    [TestMethod]
    public void BacklogPlanningSurfaceCountClaims_MatchLiveServerSurfaceCatalog()
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var documents = Directory
            .EnumerateFiles(Path.Combine(repoRoot, "ai_docs"), "*.md", SearchOption.AllDirectories)
            .Where(path => IsPlanningOrBacklogDocument(repoRoot, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        var failures = new List<string>();
        foreach (var path in documents)
        {
            var text = File.ReadAllText(path);
            var displayPath = Path.GetRelativePath(repoRoot, path).Replace('\\', '/');

            foreach (Match match in BacklogCurrentToolClaimPattern.Matches(text))
            {
                var parsed = int.Parse(match.Groups["count"].Value);
                if (parsed != ServerSurfaceCatalog.Tools.Count)
                {
                    var delta = parsed - ServerSurfaceCatalog.Tools.Count;
                    failures.Add(
                        $"{displayPath}: current tool count claim={parsed}, catalog={ServerSurfaceCatalog.Tools.Count} "
                        + $"(claim is off by {delta:+#;-#;0}).");
                }
            }

            foreach (Match match in BacklogSurfaceEvolutionPattern.Matches(text))
            {
                var parsed = int.Parse(match.Groups["to"].Value);
                if (parsed != ServerSurfaceCatalog.Tools.Count)
                {
                    var delta = parsed - ServerSurfaceCatalog.Tools.Count;
                    failures.Add(
                        $"{displayPath}: surface evolution current-side tool count={parsed}, "
                        + $"catalog={ServerSurfaceCatalog.Tools.Count} (claim is off by {delta:+#;-#;0}).");
                }
            }
        }

        Assert.AreEqual(
            0,
            failures.Count,
            "AI-facing backlog/planning surface-count claims drifted from ServerSurfaceCatalog:\n  "
            + string.Join("\n  ", failures));
    }

    private static void AssertDocumentSurfaceCountsMatchCatalog(string path, string displayPath)
    {
        Assert.IsTrue(File.Exists(path), $"{displayPath} not found at {path}");

        var documentText = File.ReadAllText(path);
        var matches = CountPattern.Matches(documentText);

        // Three kinds (tools, resources, prompts) must all appear. If this fails, the
        // README paragraph was restructured and the regex + per-kind assertions need a
        // matching update — see CountPattern's golden-comment.
        var parsedMatches = matches.Select(ParseMatch).ToArray();
        var duplicateKinds = parsedMatches
            .GroupBy(x => x.Kind, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        Assert.AreEqual(
            0,
            duplicateKinds.Length,
            $"{displayPath} has duplicate surface-count phrases for: {string.Join(", ", duplicateKinds)}. "
            + "Keep exactly one count phrase per kind so drift failures point at one authoritative claim.");

        var parsed = parsedMatches.ToDictionary(x => x.Kind, x => x, StringComparer.OrdinalIgnoreCase);

        var missing = new[] { "tools", "resources", "prompts" }
            .Where(kind => !parsed.ContainsKey(kind))
            .ToArray();
        Assert.AreEqual(
            0,
            missing.Length,
            $"{displayPath} is missing count phrases for: {string.Join(", ", missing)}. "
            + "Expected 'X tools (A stable / B experimental)', 'Y resources (C stable / D experimental)', "
            + "'Z prompts (all experimental|stable)' — if the wording was restructured, update "
            + $"{nameof(CountPattern)} in {nameof(ReadmeSurfaceCountTests)}.");

        var failures = new List<string>();

        CompareKind(
            "tools",
            parsed["tools"],
            liveTotal: ServerSurfaceCatalog.Tools.Count,
            liveStable: CountByTier(ServerSurfaceCatalog.Tools, "stable"),
            liveExperimental: CountByTier(ServerSurfaceCatalog.Tools, "experimental"),
            failures);
        CompareKind(
            "resources",
            parsed["resources"],
            liveTotal: ServerSurfaceCatalog.Resources.Count,
            liveStable: CountByTier(ServerSurfaceCatalog.Resources, "stable"),
            liveExperimental: CountByTier(ServerSurfaceCatalog.Resources, "experimental"),
            failures);
        CompareKind(
            "prompts",
            parsed["prompts"],
            liveTotal: ServerSurfaceCatalog.Prompts.Count,
            liveStable: CountByTier(ServerSurfaceCatalog.Prompts, "stable"),
            liveExperimental: CountByTier(ServerSurfaceCatalog.Prompts, "experimental"),
            failures);

        Assert.AreEqual(
            0,
            failures.Count,
            $"{displayPath} surface counts drifted from ServerSurfaceCatalog:\n  "
            + string.Join("\n  ", failures)
            + $"\nUpdate {displayPath} to match the live counts. "
            + "Authoritative source: src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs.");
    }

    private static void CompareKind(
        string kind,
        ParsedCount parsed,
        int liveTotal,
        int liveStable,
        int liveExperimental,
        List<string> failures)
    {
        if (parsed.Total != liveTotal)
        {
            var delta = parsed.Total - liveTotal;
            failures.Add(
                $"{kind}: total README={parsed.Total}, catalog={liveTotal} (README is off by {delta:+#;-#;0}).");
        }

        // "all experimental" / "all stable" form: assert the non-zero tier equals the total,
        // and the opposite tier is zero.
        if (parsed.AllTier is not null)
        {
            if (string.Equals(parsed.AllTier, "experimental", StringComparison.OrdinalIgnoreCase))
            {
                if (liveStable != 0)
                    failures.Add(
                        $"{kind}: README says 'all experimental' but catalog has {liveStable} stable entries.");
                if (liveExperimental != liveTotal)
                    failures.Add(
                        $"{kind}: README says 'all experimental' ({liveTotal} total) but catalog has "
                        + $"{liveExperimental} experimental of {liveTotal} total.");
            }
            else // "all stable"
            {
                if (liveExperimental != 0)
                    failures.Add(
                        $"{kind}: README says 'all stable' but catalog has {liveExperimental} experimental entries.");
                if (liveStable != liveTotal)
                    failures.Add(
                        $"{kind}: README says 'all stable' ({liveTotal} total) but catalog has "
                        + $"{liveStable} stable of {liveTotal} total.");
            }
            return;
        }

        // "X stable / Y experimental" form.
        if (parsed.Stable != liveStable)
        {
            var delta = parsed.Stable!.Value - liveStable;
            failures.Add(
                $"{kind}: stable README={parsed.Stable}, catalog={liveStable} (README is off by {delta:+#;-#;0}).");
        }
        if (parsed.Experimental != liveExperimental)
        {
            var delta = parsed.Experimental!.Value - liveExperimental;
            failures.Add(
                $"{kind}: experimental README={parsed.Experimental}, catalog={liveExperimental} "
                + $"(README is off by {delta:+#;-#;0}).");
        }
    }

    private static ParsedCount ParseMatch(Match match)
    {
        var kind = match.Groups["kind"].Value;
        var total = int.Parse(match.Groups["total"].Value);
        var allGroup = match.Groups["all"];
        if (allGroup.Success)
        {
            return new ParsedCount(kind, total, Stable: null, Experimental: null, AllTier: match.Groups["allTier"].Value);
        }

        return new ParsedCount(
            kind,
            total,
            Stable: int.Parse(match.Groups["stable"].Value),
            Experimental: int.Parse(match.Groups["experimental"].Value),
            AllTier: null);
    }

    private static int CountByTier(IReadOnlyList<SurfaceEntry> entries, string tier) =>
        entries.Count(entry => string.Equals(entry.SupportTier, tier, StringComparison.Ordinal));

    private static int CountShippedSkills(string repoRoot)
    {
        var skillsPath = Path.Combine(repoRoot, "skills");
        Assert.IsTrue(Directory.Exists(skillsPath), $"skills/ directory not found at {skillsPath}");

        return Directory
            .EnumerateDirectories(skillsPath)
            .Count(path => File.Exists(Path.Combine(path, "SKILL.md")));
    }

    private static void CompareSkillClaims(
        string path,
        string displayPath,
        int expectedSkillCount,
        List<string> failures)
    {
        Assert.IsTrue(File.Exists(path), $"{displayPath} not found at {path}");

        CompareSkillMatches(
            SkillCountPattern.Matches(File.ReadAllText(path)),
            displayPath,
            expectedSkillCount,
            failures);
    }

    private static void CompareSkillMatches(
        MatchCollection matches,
        string displayPath,
        int expectedSkillCount,
        List<string> failures)
    {
        Assert.IsTrue(
            matches.Count > 0,
            $"{displayPath} is missing a skill-count phrase matching '{SkillCountPattern}'.");

        foreach (Match match in matches)
        {
            var parsed = int.Parse(match.Groups["total"].Value);
            if (parsed == expectedSkillCount)
                continue;

            var delta = parsed - expectedSkillCount;
            failures.Add(
                $"{displayPath}: skill count={parsed}, skills/={expectedSkillCount} "
                + $"(claim is off by {delta:+#;-#;0}).");
        }
    }

    private static string ReadPluginDescription(string pluginPath)
    {
        Assert.IsTrue(File.Exists(pluginPath), $".claude-plugin/plugin.json not found at {pluginPath}");

        using var document = JsonDocument.Parse(File.ReadAllText(pluginPath));
        Assert.IsTrue(
            document.RootElement.TryGetProperty("description", out var descriptionElement),
            ".claude-plugin/plugin.json is missing a description property.");

        return descriptionElement.GetString() ?? string.Empty;
    }

    private static bool IsPlanningOrBacklogDocument(string repoRoot, string path)
    {
        var relative = Path.GetRelativePath(repoRoot, path).Replace('\\', '/');
        return string.Equals(relative, "ai_docs/backlog.md", StringComparison.Ordinal)
            || string.Equals(relative, "ai_docs/planning_index.md", StringComparison.Ordinal)
            || relative.StartsWith("ai_docs/plans/", StringComparison.Ordinal);
    }

    private sealed record ParsedCount(string Kind, int Total, int? Stable, int? Experimental, string? AllTier);
}
