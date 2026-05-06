using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Tests.Skills;

/// <summary>
/// audit-deep-skill-migration: structural + tool-reference parity for the shipped `audit-deep` skill.
///
/// Contract enforced here:
///   1. SKILL.md frontmatter has the expected fields (`name`, `description`, `user-invocable`, `argument-hint`)
///      and the `name` field matches the directory name. This is the same contract the deep-review prompt's
///      Phase 16b applies dynamically via `glob skills/*/SKILL.md` — pinning it as a unit test means the build
///      fails before a malformed skill ships.
///   2. The three mode prompt files exist at the documented relative paths under `prompts/`.
///   3. Every `mcp__roslyn__&lt;tool&gt;` reference in the SKILL.md body resolves to a name in the live
///      `ServerSurfaceCatalog.Tools` list. A reference to a renamed/removed tool is the highest-impact
///      shipped-skill defect (Phase 16b's "P2 FAIL" classification) — catch it at test time.
///
/// The mode prompt bodies (full / promotion-only / read-only) intentionally are NOT scanned for tool names
/// here. The full prompt is the 955-line living audit prompt; its tool references are validated by Phase 16b's
/// runtime `glob` + catalog cross-check, not by a static unit test. Pinning them statically would conflict
/// with the prompt's "live catalog wins" contract.
/// </summary>
[TestClass]
public sealed class AuditDeepSkillFrontmatterTests
{
    private const string SkillName = "audit-deep";

    [TestMethod]
    public void Skill_FilePresent_AtExpectedPath()
    {
        var skillPath = ResolveSkillPath();
        Assert.IsTrue(
            File.Exists(skillPath),
            $"audit-deep SKILL.md not found at {skillPath}. The shipped skill is the consumer-facing entry point — its absence makes /roslyn-mcp:audit-deep moot.");
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

        // Description must mention the three modes — they are the user-facing argument shape.
        var description = frontmatter["description"];
        StringAssert.Contains(description, "full",
            $"audit-deep description must mention the `full` mode. Actual: {description}");
        StringAssert.Contains(description, "promotion-only",
            $"audit-deep description must mention the `promotion-only` mode. Actual: {description}");
        StringAssert.Contains(description, "read-only",
            $"audit-deep description must mention the `read-only` mode. Actual: {description}");
    }

    [TestMethod]
    public void Skill_Body_DocumentsAllThreeModes()
    {
        var skillPath = ResolveSkillPath();
        var contents = File.ReadAllText(skillPath);

        Assert.IsTrue(contents.Contains("mode=full", StringComparison.Ordinal),
            "audit-deep SKILL.md is missing the `mode=full` token. Step 2 must enumerate every accepted mode.");
        Assert.IsTrue(contents.Contains("mode=promotion-only", StringComparison.Ordinal),
            "audit-deep SKILL.md is missing the `mode=promotion-only` token. Step 2 must enumerate every accepted mode.");
        Assert.IsTrue(contents.Contains("mode=read-only", StringComparison.Ordinal),
            "audit-deep SKILL.md is missing the `mode=read-only` token. Step 2 must enumerate every accepted mode.");

        // The B4/B5 hard precondition — server-or-halt — must be unambiguous.
        Assert.IsTrue(
            contents.Contains("mcp__roslyn__server_info", StringComparison.Ordinal),
            "audit-deep SKILL.md must require `mcp__roslyn__server_info` as the hard precondition (B4/B5). " +
            "Without this gate, the skill could fall back to a non-MCP audit and produce no audit-grade evidence.");
    }

    [TestMethod]
    public void Skill_ModePrompts_ExistAtDocumentedPaths()
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var promptsRoot = Path.Combine(repoRoot, "skills", SkillName, "prompts");

        foreach (var modeFile in new[] { "full.md", "promotion-only.md", "read-only.md" })
        {
            var path = Path.Combine(promptsRoot, modeFile);
            Assert.IsTrue(File.Exists(path),
                $"audit-deep mode prompt not found at {path}. SKILL.md Step 2 routes to this file by convention; " +
                "missing it breaks the corresponding mode invocation.");
        }
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
            // mode prompts. The frontmatter test already validates the structural contract.
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
            $"audit-deep SKILL.md references {unknown.Count} tool name(s) that do not exist in the live ServerSurfaceCatalog: " +
            string.Join(", ", unknown.Distinct()) + ". " +
            "Per Phase 16b, this is a P2 FAIL — shipped skills must not reference renamed/removed tools.");
    }

    private static string ResolveSkillPath()
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        return Path.Combine(repoRoot, "skills", SkillName, "SKILL.md");
    }

    private static Dictionary<string, string> ExtractFrontmatter(string contents, string skillPath)
    {
        // Frontmatter shape: a leading `---\n` ... `\n---\n` block. The contents are a small set of
        // `key: "value"` or `key: value` lines — we do not need a YAML parser.
        var match = Regex.Match(contents, @"^---\s*\r?\n(?<body>.*?)\r?\n---\s*\r?\n", RegexOptions.Singleline);
        Assert.IsTrue(match.Success,
            $"audit-deep SKILL.md at {skillPath} is missing a `---`-delimited frontmatter block at the file head.");

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
            $"audit-deep frontmatter at {skillPath} is missing the `{field}` field.");
        var actual = frontmatter[field];
        Assert.IsFalse(string.IsNullOrWhiteSpace(actual),
            $"audit-deep frontmatter field `{field}` at {skillPath} is empty.");
        if (expectedValue is not null)
        {
            Assert.AreEqual(expectedValue, actual,
                $"audit-deep frontmatter field `{field}` at {skillPath} must equal `{expectedValue}`.");
        }
    }
}
