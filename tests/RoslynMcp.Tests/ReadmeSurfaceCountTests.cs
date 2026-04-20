using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Tests;

/// <summary>
/// readme-surface-counts-drift-from-live-catalog: README.md's "Supported Surface" paragraph
/// hand-advertises per-kind counts ("159 tools (107 stable / 52 experimental), ..."). Without
/// a test gate those numbers drift from <see cref="ServerSurfaceCatalog"/> — v1.22.0 shipped
/// with a documented off-by-one tool count. This fixture parses the README's numeric
/// patterns and asserts they match the live catalog; on mismatch the failure names which
/// number is off and by how much. When the README paragraph is restructured (e.g. moved
/// into a table), update <see cref="CountPattern"/> and keep the assertion contract.
/// </summary>
[TestClass]
public sealed class ReadmeSurfaceCountTests
{
    // Golden regex contract (keep in sync with the README paragraph at approximately
    // README.md:282). Matches phrases like:
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

    [TestMethod]
    public void ReadmeCounts_MatchLiveServerSurfaceCatalog()
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var readmePath = Path.Combine(repoRoot, "README.md");
        Assert.IsTrue(File.Exists(readmePath), $"README.md not found at {readmePath}");

        var readmeText = File.ReadAllText(readmePath);
        var matches = CountPattern.Matches(readmeText);

        // Three kinds (tools, resources, prompts) must all appear. If this fails, the
        // README paragraph was restructured and the regex + per-kind assertions need a
        // matching update — see CountPattern's golden-comment.
        var parsed = matches
            .Select(ParseMatch)
            .ToDictionary(x => x.Kind, x => x, StringComparer.OrdinalIgnoreCase);

        var missing = new[] { "tools", "resources", "prompts" }
            .Where(kind => !parsed.ContainsKey(kind))
            .ToArray();
        Assert.AreEqual(
            0,
            missing.Length,
            $"README.md is missing count phrases for: {string.Join(", ", missing)}. "
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
            "README.md surface counts drifted from ServerSurfaceCatalog:\n  "
            + string.Join("\n  ", failures)
            + $"\nUpdate README.md at approximately line 282 to match the live counts. "
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

    private sealed record ParsedCount(string Kind, int Total, int? Stable, int? Experimental, string? AllTier);
}
