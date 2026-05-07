using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Tests.Skills;

/// <summary>
/// mcp-server-stress-relocate: structural + tool-reference parity for the maintainer-only
/// `mcp-server-stress` skill (relocated from `skills/audit-deep/` to `.claude/skills/mcp-server-stress/`).
///
/// Contract enforced here:
///   1. SKILL.md frontmatter has the expected fields (`name`, `description`, `user-invocable`, `argument-hint`)
///      and the `name` field matches the directory name. This is the same contract the deep-review prompt's
///      Phase 16b applies dynamically via `glob .claude/skills/*/SKILL.md` — pinning it as a unit test means the build
///      fails before a malformed skill ships.
///   2. The single canonical prompt file exists at the documented relative path under `prompts/prompt.md`.
///      The historical `prompts/full.md` / `prompts/promotion-only.md` / `prompts/read-only.md` mode wrappers
///      were collapsed into one canonical run by `mcp-server-stress-single-mode` — SKILL.md must route to the
///      single-prompt path and must not re-introduce mode tokens.
///   3. Every `mcp__roslyn__&lt;tool&gt;` reference in the SKILL.md body resolves to a name in the live
///      `ServerSurfaceCatalog.Tools` list. A reference to a renamed/removed tool is the highest-impact
///      shipped-skill defect (Phase 16b's "P2 FAIL" classification) — catch it at test time.
///
/// The canonical prompt body itself is intentionally NOT scanned for tool names here. The prompt is the 950+-line
/// living audit prompt; its tool references are validated by Phase 16b's runtime `glob` + catalog cross-check,
/// not by a static unit test. Pinning them statically would conflict with the prompt's "live catalog wins"
/// contract.
/// </summary>
[TestClass]
public sealed class McpServerStressSkillFrontmatterTests
{
    private const string SkillName = "mcp-server-stress";

    [TestMethod]
    public void Skill_FilePresent_AtExpectedPath()
    {
        var skillPath = ResolveSkillPath();
        Assert.IsTrue(
            File.Exists(skillPath),
            $"mcp-server-stress SKILL.md not found at {skillPath}. The shipped skill is the consumer-facing entry point — its absence makes /mcp-server-stress moot.");
    }

    [TestMethod]
    public void Skill_Frontmatter_HasExpectedFields()
    {
        var skillPath = ResolveSkillPath();
        var contents = File.ReadAllText(skillPath);
        var frontmatter = ExtractFrontmatter(contents, skillPath);

        AssertFrontmatterField(frontmatter, "name", expectedValue: SkillName, skillPath);
        AssertFrontmatterField(frontmatter, "description", expectedValue: null, skillPath);
        AssertFrontmatterField(frontmatter, "user-invocable", expectedValue: "true", skillPath);
        AssertFrontmatterField(frontmatter, "argument-hint", expectedValue: null, skillPath);

        // Description must reflect the single-mode shape — no `mode=` token, and at least one of the
        // canonical-run descriptors ("disposable worktree" or "scorecard") must be present so the description
        // accurately conveys what the skill does.
        var description = frontmatter["description"];
        Assert.IsFalse(
            description.Contains("mode=", StringComparison.Ordinal),
            $"mcp-server-stress description must not mention `mode=` — the modes were collapsed into one canonical run. Actual: {description}");
        var mentionsDisposableWorktree = description.Contains("disposable worktree", StringComparison.Ordinal);
        var mentionsScorecard = description.Contains("scorecard", StringComparison.Ordinal);
        Assert.IsTrue(
            mentionsDisposableWorktree || mentionsScorecard,
            $"mcp-server-stress description must mention either `disposable worktree` or `scorecard` — those are the canonical-run distinguishing features. Actual: {description}");
    }

    [TestMethod]
    public void Skill_Body_DocumentsSingleModeAndPrecondition()
    {
        var skillPath = ResolveSkillPath();
        var contents = File.ReadAllText(skillPath);

        // SKILL.md must route to the single-prompt path.
        Assert.IsTrue(
            contents.Contains("prompts/prompt.md", StringComparison.Ordinal),
            "mcp-server-stress SKILL.md must reference `prompts/prompt.md` — that is the single canonical prompt file. " +
            "If you renamed the prompt, update the route in Step 2.");

        // Negative assertions — the historical mode tokens must not survive the collapse.
        Assert.IsFalse(
            contents.Contains("mode=full", StringComparison.Ordinal),
            "mcp-server-stress SKILL.md must not contain `mode=full` — the modes were collapsed into one canonical run by `mcp-server-stress-single-mode`.");
        Assert.IsFalse(
            contents.Contains("mode=promotion-only", StringComparison.Ordinal),
            "mcp-server-stress SKILL.md must not contain `mode=promotion-only` — the modes were collapsed into one canonical run by `mcp-server-stress-single-mode`.");
        Assert.IsFalse(
            contents.Contains("mode=read-only", StringComparison.Ordinal),
            "mcp-server-stress SKILL.md must not contain `mode=read-only` — the modes were collapsed into one canonical run by `mcp-server-stress-single-mode`.");

        // The B4/B5 hard precondition — server-or-halt — must be unambiguous.
        Assert.IsTrue(
            contents.Contains("mcp__roslyn__server_info", StringComparison.Ordinal),
            "mcp-server-stress SKILL.md must require `mcp__roslyn__server_info` as the hard precondition (B4/B5). " +
            "Without this gate, the skill could fall back to a non-MCP audit and produce no audit-grade evidence.");
    }

    [TestMethod]
    public void Skill_PromptFile_ExistsAtDocumentedPath()
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var promptPath = Path.Combine(repoRoot, ".claude", "skills", SkillName, "prompts", "prompt.md");

        Assert.IsTrue(File.Exists(promptPath),
            $"mcp-server-stress canonical prompt not found at {promptPath}. SKILL.md Step 2 routes to this single file; " +
            "missing it breaks every invocation of /mcp-server-stress.");
    }

    [TestMethod]
    public void Skill_ToolReferences_AllResolveToLiveCatalog()
    {
        var skillPath = ResolveSkillPath();
        var contents = File.ReadAllText(skillPath);

        // Match `mcp__roslyn__<name>` references — the canonical fully-qualified tool name shape used
        // throughout the deep-review prompt. The trailing word boundary stops at `(`, `\`` (backtick),
        // ` `, `.`, `,`, `)`, end-of-line, etc.
        var matches = Regex.Matches(contents, @"mcp__roslyn__([a-zA-Z_][a-zA-Z0-9_]*)");
        if (matches.Count == 0)
        {
            // No tool references in the body — that is fine; SKILL.md may delegate all tool naming to the
            // canonical prompt. The frontmatter test already validates the structural contract.
            return;
        }

        var liveToolNames = new HashSet<string>(
            ServerSurfaceCatalog.Tools.Select(t => t.Name),
            StringComparer.Ordinal);

        var unknown = new List<string>();
        foreach (Match m in matches)
        {
            var name = m.Groups[1].Value;
            if (!liveToolNames.Contains(name))
            {
                unknown.Add(name);
            }
        }

        Assert.AreEqual(
            0, unknown.Count,
            $"mcp-server-stress SKILL.md references {unknown.Count} tool name(s) that do not exist in the live ServerSurfaceCatalog: " +
            string.Join(", ", unknown.Distinct()) + ". " +
            "Per Phase 16b, this is a P2 FAIL — shipped skills must not reference renamed/removed tools.");
    }

    private static string ResolveSkillPath()
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        return Path.Combine(repoRoot, ".claude", "skills", SkillName, "SKILL.md");
    }

    private static Dictionary<string, string> ExtractFrontmatter(string contents, string skillPath)
    {
        // Frontmatter shape: a leading `---\n` ... `\n---\n` block. The contents are a small set of
        // `key: "value"` or `key: value` lines — we do not need a YAML parser.
        var match = Regex.Match(contents, @"^---\s*\r?\n(?<body>.*?)\r?\n---\s*\r?\n", RegexOptions.Singleline);
        Assert.IsTrue(match.Success,
            $"mcp-server-stress SKILL.md at {skillPath} is missing a `---`-delimited frontmatter block at the file head.");

        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in match.Groups["body"].Value.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line)) continue;
            var colon = line.IndexOf(':');
            if (colon < 0) continue;
            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            // Strip surrounding double quotes if present.
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            {
                value = value[1..^1];
            }
            dict[key] = value;
        }
        return dict;
    }

    private static void AssertFrontmatterField(
        Dictionary<string, string> frontmatter,
        string field,
        string? expectedValue,
        string skillPath)
    {
        Assert.IsTrue(frontmatter.ContainsKey(field),
            $"mcp-server-stress frontmatter at {skillPath} is missing the `{field}` field.");
        var actual = frontmatter[field];
        Assert.IsFalse(string.IsNullOrWhiteSpace(actual),
            $"mcp-server-stress frontmatter field `{field}` at {skillPath} is empty.");
        if (expectedValue is not null)
        {
            Assert.AreEqual(expectedValue, actual,
                $"mcp-server-stress frontmatter field `{field}` at {skillPath} must equal `{expectedValue}`.");
        }
    }
}
