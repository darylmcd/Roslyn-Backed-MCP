using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Tests.Skills;

/// <summary>
/// Structural tests for the maintainer-only `mcp-server-stress` skill (lives under
/// `.claude/skills/mcp-server-stress/`).
///
/// After the unify-audit-prompt-single-source refactor (PR #633), this skill is a THIN ALIAS
/// for `/mcp-server-surface-test --output-mode=fragments`. The historical `prompts/maintainer-overlay.md`
/// was deleted; the canonical audit prompt now lives in the shipped surface-test skill at
/// `skills/mcp-server-surface-test/prompts/full.md`, driven by the new `--output-mode` flag.
///
/// Contract enforced here:
///   1. SKILL.md frontmatter has the expected fields (`name`, `description`, `user-invocable`, `argument-hint`).
///   2. Description identifies the alias relationship (mentions the surface-test invocation or the
///      `--output-mode=fragments` flag) so the slash-command picker conveys what the skill does.
///   3. SKILL.md body references the canonical invocation `/mcp-server-surface-test --output-mode=fragments`
///      so the alias is grep-able and auditable.
///   4. The hard precondition `mcp__roslyn__server_info` is documented (inherited from surface-test).
///   5. No residue from the OLD architecture survives: no `Phase 16b`, no `plugin-skill`, no historical
///      mode tokens (`mode=full`, `mode=promotion-only`, `mode=read-only`), no reference to the deleted
///      `prompts/maintainer-overlay.md` file.
///   6. Tool references resolve to the live `ServerSurfaceCatalog`.
///
/// What is NOT tested here:
///   - The audit prompt itself (`prompts/maintainer-overlay.md` was deleted). The canonical prompt is
///     now `skills/mcp-server-surface-test/prompts/full.md` and is covered by `McpServerSurfaceTestSkillTests`
///     and `AuditPhaseRunnerHandoffTests`.
///   - The `--output-mode=fragments` semantics (covered by surface-test SKILL.md tests).
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
            $"mcp-server-stress SKILL.md not found at {skillPath}. The maintainer-only alias is the muscle-memory entry point — its absence makes /mcp-server-stress moot.");
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

        // Description must accurately describe the post-refactor thin-alias shape — mention either
        // the surface-test invocation it aliases or the --output-mode=fragments flag (its distinctive
        // characteristic vs the consumer-default findings mode).
        var description = frontmatter["description"];
        var mentionsSurfaceTest = description.Contains("/mcp-server-surface-test", StringComparison.Ordinal);
        var mentionsFragments = description.Contains("--output-mode=fragments", StringComparison.Ordinal);
        Assert.IsTrue(
            mentionsSurfaceTest || mentionsFragments,
            $"mcp-server-stress description must mention either `/mcp-server-surface-test` or `--output-mode=fragments` — those are the alias's canonical descriptors after the unify-audit-prompt-single-source refactor. Actual: {description}");
    }

    [TestMethod]
    public void Skill_Body_IdentifiesAsAliasToSurfaceTest()
    {
        var skillPath = ResolveSkillPath();
        var contents = File.ReadAllText(skillPath);

        // SKILL.md must reference the canonical surface-test invocation with --output-mode=fragments.
        // This is the grep-able assertion that the alias is wired correctly; if a future edit removes
        // the reference, the alias becomes a dangling shell and /mcp-server-stress invocations would
        // have no clear target.
        Assert.IsTrue(
            contents.Contains("/mcp-server-surface-test --output-mode=fragments", StringComparison.Ordinal),
            "mcp-server-stress SKILL.md must reference `/mcp-server-surface-test --output-mode=fragments` — the canonical invocation it aliases. " +
            "If you renamed the flag or restructured the alias, update this assertion to match.");

        // The hard precondition — server-or-halt — must still be inherited or documented (since the alias
        // delegates to surface-test, which enforces this gate; the SKILL.md should at minimum mention the
        // tool name so the contract is auditable from inside this file).
        Assert.IsTrue(
            contents.Contains("mcp__roslyn__server_info", StringComparison.Ordinal),
            "mcp-server-stress SKILL.md must mention `mcp__roslyn__server_info` so the inherited hard-precondition contract is auditable from this file alone. " +
            "Without that, a cold reader cannot verify that the alias preserves the server-required gate.");
    }

    [TestMethod]
    public void Skill_Body_HasNoLegacyResidue()
    {
        // After the unify-audit-prompt-single-source refactor (PR #633) collapsed the maintainer-overlay.md
        // prompt into the shipped surface-test prompt, no residue from the prior architecture should remain.
        // This test catches accidental reintroduction of obsolete content during future refactors.
        var skillPath = ResolveSkillPath();
        var contents = File.ReadAllText(skillPath);

        // The plugin-skill audit was extracted into /surface-audit by `extract-skills-audit-from-server-stress`.
        Assert.IsFalse(
            contents.Contains("Phase 16b", StringComparison.Ordinal),
            "mcp-server-stress SKILL.md must not contain `Phase 16b` — the plugin-skill audit was extracted into /surface-audit.");
        Assert.IsFalse(
            contents.Contains("plugin-skill", StringComparison.Ordinal),
            "mcp-server-stress SKILL.md must not contain `plugin-skill` — extracted into /surface-audit.");

        // The historical mode tokens from the pre-collapse multi-mode prompt set.
        Assert.IsFalse(
            contents.Contains("mode=full", StringComparison.Ordinal),
            "mcp-server-stress SKILL.md must not contain `mode=full` — the multi-mode prompt set was collapsed into one canonical run.");
        Assert.IsFalse(
            contents.Contains("mode=promotion-only", StringComparison.Ordinal),
            "mcp-server-stress SKILL.md must not contain `mode=promotion-only`.");
        Assert.IsFalse(
            contents.Contains("mode=read-only", StringComparison.Ordinal),
            "mcp-server-stress SKILL.md must not contain `mode=read-only`.");

        // The legacy prompt file is gone. References to it (other than historical-context prose explaining
        // the deletion) should not appear; a stale link would break navigation from this file.
        // Note: the SKILL.md body can mention the deleted file in prose if it explains the deletion
        // context — we only ban markdown link syntax `(...maintainer-overlay.md)` that would 404.
        Assert.IsFalse(
            Regex.IsMatch(contents, @"\]\([^)]*maintainer-overlay\.md\)"),
            "mcp-server-stress SKILL.md must not link to the deleted `prompts/maintainer-overlay.md` file. " +
            "Prose mentions of the deletion are fine; markdown links to the deleted path are not.");
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
            // No tool references in the body — that is fine for a thin alias. The frontmatter test
            // already validates the structural contract.
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
            "Shipped/aliased skills must not reference renamed/removed tools.");
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
