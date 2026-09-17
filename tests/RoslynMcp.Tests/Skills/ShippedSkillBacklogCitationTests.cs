using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RoslynMcp.Tests.Support;

namespace RoslynMcp.Tests.Skills;

/// <summary>
/// Shipped skills under <c>skills/**</c> must not cite backlog rows that no longer exist.
///
/// A prompt that says "Known limitation — backlog <c>&lt;id&gt;</c>" tells every auditor to suppress a
/// finding. Once that row ships and is closed, the citation becomes a false instruction that hides a
/// regression (the surface-test apply phase kept reporting the fixed change_signature_preview
/// callsite-summary gap as a known limitation long after the fix landed).
///
/// Contract: every <c>backlog `&lt;kebab-id&gt;`</c> citation in a shipped skill markdown file resolves
/// to a live row id in the repository backlog index. The scan is generic — no specific id is
/// hard-coded here, so this test never itself becomes a stale citation.
/// </summary>
[TestClass]
public sealed class ShippedSkillBacklogCitationTests
{
    /// <summary>
    /// "backlog `id`", "backlog row `id`", "backlog id `id`", "backlog item `id`" — case-insensitive.
    /// Ids are kebab-case with at least two segments, matching the backlog's stable-id convention.
    /// </summary>
    private static readonly Regex CitationPattern = new(
        @"\bbacklog(?:\s+(?:row|id|item))?\s+`(?<id>[a-z0-9]+(?:-[a-z0-9]+)+)`",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex BacklogRowIdPattern = new(
        @"^\|\s*`(?<id>[^`]+)`\s*\|",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    [TestMethod]
    public void CitationPattern_DetectsCitationShapes()
    {
        var id = string.Join("-", "sample", "row", "id");
        foreach (var text in new[]
        {
            $"**Known limitation — backlog `{id}` (P3):** do not re-raise.",
            $"tracked as Backlog row `{id}`.",
            $"see backlog id `{id}`",
        })
        {
            var ids = ExtractCitedIds(text).ToList();
            CollectionAssert.AreEqual(new[] { id }, ids, $"Pattern failed to detect citation in: {text}");
        }

        Assert.IsEmpty(ExtractCitedIds("prior backlog; zero FAIL findings `server_info`").ToList());
    }

    [TestMethod]
    public void ShippedSkillBacklogCitations_ResolveToLiveBacklogRows()
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var backlogPath = Path.Combine(repoRoot, "ai_docs", "backlog.md");
        Assert.IsTrue(File.Exists(backlogPath), $"Backlog index not found at {backlogPath}.");

        var liveIds = BacklogRowIdPattern.Matches(File.ReadAllText(backlogPath))
            .Select(m => m.Groups["id"].Value)
            .ToHashSet(StringComparer.Ordinal);
        Assert.IsNotEmpty(liveIds, "Parsed zero row ids from the backlog index; the row pattern is stale.");

        var skillsRoot = Path.Combine(repoRoot, "skills");
        Assert.IsTrue(Directory.Exists(skillsRoot), $"Shipped skills directory not found at {skillsRoot}.");

        var stale = new List<string>();
        foreach (var file in Directory.EnumerateFiles(skillsRoot, "*.md", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
            var lineNumber = 0;
            foreach (var line in File.ReadLines(file))
            {
                lineNumber++;
                foreach (var id in ExtractCitedIds(line))
                {
                    if (!liveIds.Contains(id))
                    {
                        stale.Add($"{relative}:{lineNumber} cites backlog `{id}`, which is not a live backlog row.");
                    }
                }
            }
        }

        Assert.IsEmpty(
            stale,
            "Shipped skills cite closed or unknown backlog rows. Rewrite the text to describe current behavior "
            + "(or restore a bounded row):\n" + string.Join("\n", stale));
    }

    private static IEnumerable<string> ExtractCitedIds(string text) =>
        CitationPattern.Matches(text).Select(m => m.Groups["id"].Value);
}
