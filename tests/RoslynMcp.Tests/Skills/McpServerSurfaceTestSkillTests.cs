using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Tests.Support;

namespace RoslynMcp.Tests.Skills;

/// <summary>
/// Structural + tool-reference parity for the shipped consumer-facing `mcp-server-surface-test` skill
/// (lives under `skills/mcp-server-surface-test/`, bundled by `plugin.json` and distributed to every
/// installer). This is the consumer-generic half of the maintainer-only `/mcp-server-stress` skill;
/// the genericity gate at `eng/verify-skills-are-generic.ps1` enforces that no banned repo-only marker
/// reaches the SKILL.md.
///
/// Contract enforced here:
///   1. SKILL.md exists at `skills/mcp-server-surface-test/SKILL.md` and has the expected frontmatter
///      fields (`name`, `description`, `user-invocable`, `argument-hint`).
///   2. Both prompt files exist — `prompts/quick.md` (≤15-min read-only smoke pass) and
///      `prompts/full.md` (90–180-min comprehensive sweep). The argument-hint advertises both tiers.
///   3. Every `mcp__roslyn__&lt;tool&gt;` reference in the SKILL.md body resolves to a name in the live
///      `ServerSurfaceCatalog.Tools` list.
///   4. The SKILL.md body and frontmatter contain none of the banned repo-only markers
///      (`ai_docs/`, `backlog.md`, `eng/`, etc.) — a redundant in-process echo of the
///      `verify-skills-are-generic.ps1` gate, kept here so a build-only contributor (no `pwsh`) still
///      catches genericity drift.
///   5. The canonical promotion scorecard (`audit-reports/_latest-promotion-scorecard.json`) is
///      git-TRACKED in this repo. It is the release-gating input read by
///      `eng/aggregate-promotion-scorecards.ps1`; without history a lost refresh is silent and
///      unrecoverable (the 2026-05-31 run computed a fresh scorecard that never reached disk).
///   6. `prompts/phases/output-and-close.md` states at BOTH artifact-write sites that
///      `&lt;audited-repo-root&gt;` means the audited repo's PRIMARY checkout — never the disposable
///      Phase-6 worktree — requires copy-back before worktree teardown, and exempts the two
///      canonical artifact paths from the run-end primary-checkout clean gate (otherwise every
///      future run on a repo that tracks the scorecard self-reports a false `audit-prompt-leak`).
///
/// The prompt bodies (`prompts/quick.md`, `prompts/full.md`) are intentionally NOT scanned for tool
/// names here — the maintainer prompt has the same exemption (the prompt's "live catalog wins"
/// contract is verified at runtime via Phase 0 drift detection, not statically).
/// </summary>
[TestClass]
public sealed class McpServerSurfaceTestSkillTests
{
    private const string SkillName = "mcp-server-surface-test";

    /// <summary>
    /// Canonical promotion-scorecard path, repo-relative with forward slashes (git pathspec form).
    /// </summary>
    private const string CanonicalScorecardPath = "audit-reports/_latest-promotion-scorecard.json";

    [TestMethod]
    public void Skill_FilePresent_AtExpectedPath()
    {
        var skillPath = ResolveSkillPath();
        Assert.IsTrue(
            File.Exists(skillPath),
            $"mcp-server-surface-test SKILL.md not found at {skillPath}. The shipped skill is the consumer-facing entry point — its absence makes /mcp-server-surface-test moot.");
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

        // The argument-hint must advertise both tier flags so the consumer skill router has a usable
        // surface description in the slash-command picker.
        var argumentHint = frontmatter["argument-hint"];
        Assert.IsTrue(
            argumentHint.Contains("--quick", StringComparison.Ordinal) && argumentHint.Contains("--full", StringComparison.Ordinal),
            $"mcp-server-surface-test argument-hint must advertise both `--quick` and `--full` tier flags. Actual: {argumentHint}");
        Assert.IsTrue(
            argumentHint.Contains("--auto-file", StringComparison.Ordinal),
            $"mcp-server-surface-test argument-hint must advertise the `--auto-file` opt-in. Actual: {argumentHint}");
        Assert.IsTrue(
            argumentHint.Contains("--cleanup-only", StringComparison.Ordinal),
            $"mcp-server-surface-test argument-hint must advertise the crash-recovery cleanup mode. Actual: {argumentHint}");
    }

    [TestMethod]
    public void Skill_Body_DocumentsTiersAndPrecondition()
    {
        var skillPath = ResolveSkillPath();
        var contents = File.ReadAllText(skillPath);

        // SKILL.md must route to both prompt paths so the tier picker has a destination.
        Assert.IsTrue(
            contents.Contains("prompts/quick.md", StringComparison.Ordinal),
            "mcp-server-surface-test SKILL.md must reference `prompts/quick.md` — that is the bounded read-only tier prompt.");
        Assert.IsTrue(
            contents.Contains("prompts/full.md", StringComparison.Ordinal),
            "mcp-server-surface-test SKILL.md must reference `prompts/full.md` — that is the comprehensive tier prompt.");

        // The hard precondition — server-or-halt — must be unambiguous.
        Assert.IsTrue(
            contents.Contains("mcp__roslyn__server_info", StringComparison.Ordinal),
            "mcp-server-surface-test SKILL.md must require `mcp__roslyn__server_info` as the hard precondition. " +
            "Without this gate, the skill could fall back to a non-MCP audit and produce no audit-grade evidence.");

        // The P0/security refusal contract is the load-bearing pre-disclosure safeguard. Pin it.
        Assert.IsTrue(
            contents.Contains("P0", StringComparison.Ordinal) && contents.Contains("security", StringComparison.Ordinal),
            "mcp-server-surface-test SKILL.md must document the P0 / security refusal contract for `--auto-file`. " +
            "Without it, the skill could file a vulnerability to a public Issue before the fix lands.");

        Assert.IsTrue(
            contents.Contains("--cleanup-only", StringComparison.Ordinal) && contents.Contains("crash recovery", StringComparison.Ordinal),
            "mcp-server-surface-test SKILL.md must document the cleanup-only recovery mode for interrupted full runs.");
    }

    [TestMethod]
    public void Skill_PromptFiles_ExistAtDocumentedPaths()
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var quickPath = Path.Combine(repoRoot, "skills", SkillName, "prompts", "quick.md");
        var fullPath = Path.Combine(repoRoot, "skills", SkillName, "prompts", "full.md");

        Assert.IsTrue(File.Exists(quickPath),
            $"mcp-server-surface-test quick-tier prompt not found at {quickPath}. SKILL.md Step 4 routes to this file when `--quick` is passed.");
        Assert.IsTrue(File.Exists(fullPath),
            $"mcp-server-surface-test full-tier prompt not found at {fullPath}. SKILL.md Step 4 routes to this file by default and on `--full`.");
    }

    [TestMethod]
    public void PromptFiles_UseServerInfoResourceServerNameHints()
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var promptFiles = new[]
        {
            Path.Combine(repoRoot, "skills", SkillName, "prompts", "quick.md"),
            Path.Combine(repoRoot, "skills", SkillName, "prompts", "phases", "setup-and-analysis.md"),
        };

        foreach (var promptFile in promptFiles)
        {
            var contents = File.ReadAllText(promptFile);
            StringAssert.Contains(contents, "resourceServerNames");
            StringAssert.Contains(contents, "match an alias exactly");
            StringAssert.Contains(contents, "Do not hand-convert `plugin:roslyn-mcp:roslyn` into an underscore name");
        }
    }

    [TestMethod]
    public void FullPrompt_DocumentsCheckpointResumeContract()
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var fullPath = Path.Combine(repoRoot, "skills", SkillName, "prompts", "full.md");
        var contents = File.ReadAllText(fullPath);

        StringAssert.Contains(contents, ".audit-state.json");
        StringAssert.Contains(contents, "nextPhase");
        StringAssert.Contains(contents, "resume");
        StringAssert.Contains(contents, "cleanup path");
    }

    [TestMethod]
    public void CanonicalPromotionScorecard_IsGitTracked()
    {
        if (!GitFixtureRunner.IsAvailable(out var gitUnavailableReason))
        {
            Assert.Inconclusive($"git is not available on PATH — cannot assert tracked state. {gitUnavailableReason}");
        }

        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var tracked = GitFixtureRunner.RunGitCapture(repoRoot, "ls-files", "--", CanonicalScorecardPath);

        Assert.IsFalse(
            string.IsNullOrWhiteSpace(tracked),
            $"`{CanonicalScorecardPath}` is not git-tracked. It is the release-gating input read by " +
            "eng/aggregate-promotion-scorecards.ps1; untracked it has no history, so a refresh that " +
            "never reaches disk (the 2026-05-31 loss) is silent and unrecoverable. Keep the " +
            "`audit-reports/*` + `!audit-reports/_latest-promotion-scorecard.json` .gitignore pair " +
            "intact — the contents form is load-bearing because git does not descend into an " +
            "excluded directory.");
    }

    [TestMethod]
    public void OutputAndClosePhase_PinsArtifactWritesToPrimaryCheckout()
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var phasePath = Path.Combine(
            repoRoot, "skills", SkillName, "prompts", "phases", "output-and-close.md");
        var contents = File.ReadAllText(phasePath);

        // Both artifact-write sites (prose report + scorecard) must pin `<audited-repo-root>` to the
        // primary checkout, or a run that treats the disposable Phase-6 worktree as the audited root
        // loses both artifacts at teardown.
        var primaryCheckoutMentions = Regex.Matches(contents, "PRIMARY checkout").Count;
        Assert.IsTrue(
            primaryCheckoutMentions >= 2,
            $"output-and-close.md names the PRIMARY checkout {primaryCheckoutMentions} time(s); both " +
            "artifact-write sites (prose report and _latest-promotion-scorecard.json) must state that " +
            "`<audited-repo-root>` is the audited repo's primary checkout, never the disposable " +
            $"Phase-6 worktree. Path: {phasePath}");

        // Copy-back before teardown — teardown is irreversible.
        StringAssert.Contains(contents, "BEFORE `git worktree remove`");

        // The run-end clean gate must exempt the two canonical artifact paths, otherwise every run
        // against a repo that TRACKS the scorecard files a false `audit-prompt-leak` P1 against itself.
        StringAssert.Contains(contents, "Exemption — the two canonical run artifacts");
        StringAssert.Contains(contents, "Do not file `audit-prompt-leak` for those two paths");
    }

    [TestMethod]
    public void Skill_Body_ContainsNoBannedRepoMarkers()
    {
        // Mirror of `eng/verify-skills-are-generic.ps1`. The gate is the canonical check; this is a
        // build-only echo so `dotnet test` catches genericity drift without invoking pwsh.
        var skillPath = ResolveSkillPath();
        var contents = File.ReadAllText(skillPath);

        // Strip URLs before scanning — GitHub links to this repo's public docs legitimately contain
        // `ai_docs/` and are explicitly allowed by the gate.
        var stripped = Regex.Replace(contents, @"https?://[^\s)]+", string.Empty);

        var bannedPatterns = new[]
        {
            @"state\.json",
            @"backlog-sweep",
            @"schemaVersion",
            @"\bai_docs/",
            @"\bbacklog\.md\b",
            @"\beng/",
            @"just verify-",
            @"Directory\.Build\.props",
            @"BannedSymbols\.txt",
        };

        var violations = new List<string>();
        foreach (var pattern in bannedPatterns)
        {
            if (Regex.IsMatch(stripped, pattern))
            {
                violations.Add(pattern);
            }
        }

        Assert.AreEqual(
            0, violations.Count,
            $"mcp-server-surface-test SKILL.md contains banned repo-only marker(s): {string.Join(", ", violations)}. " +
            "Shipped skills must be generic. Move repo-coupled content to the `.claude/skills/mcp-server-stress/` overlay.");
    }

    [TestMethod]
    public void Skill_ToolReferences_AllResolveToLiveCatalog()
    {
        var skillPath = ResolveSkillPath();
        var contents = File.ReadAllText(skillPath);

        var matches = Regex.Matches(contents, @"mcp__roslyn__([a-zA-Z_][a-zA-Z0-9_]*)");
        if (matches.Count == 0)
        {
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
            $"mcp-server-surface-test SKILL.md references {unknown.Count} tool name(s) that do not exist in the live ServerSurfaceCatalog: " +
            string.Join(", ", unknown.Distinct()) + ". " +
            "Shipped skills must not reference renamed/removed tools.");
    }

    [TestMethod]
    public void Skill_PromptFiles_BelowReadTokenCap()
    {
        // Every file under skills/*/prompts/ (including sub-directories such as prompts/phases/) must
        // stay at or below 100,000 bytes (~25K-token proxy with margin). This prevents any agent that
        // needs whole-prompt comprehension from hitting the Read tool's token cap on a single call.
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var skillsRoot = Path.Combine(repoRoot, "skills");

        var promptFiles = Directory.EnumerateFiles(skillsRoot, "*.md", SearchOption.AllDirectories)
            .Where(f =>
            {
                // Match any file under a "prompts" directory segment anywhere in the skills tree.
                var relative = Path.GetRelativePath(skillsRoot, f);
                var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return Array.Exists(segments, s => string.Equals(s, "prompts", StringComparison.OrdinalIgnoreCase));
            })
            .ToList();

        Assert.IsTrue(promptFiles.Count > 0,
            $"No prompt files found under {skillsRoot}. Expected at least one prompts/ directory.");

        const long MaxBytes = 100_000;
        var violations = new List<string>();
        foreach (var file in promptFiles)
        {
            var length = new FileInfo(file).Length;
            if (length > MaxBytes)
            {
                violations.Add($"{Path.GetRelativePath(repoRoot, file)} ({length:N0} bytes > {MaxBytes:N0})");
            }
        }

        Assert.AreEqual(
            0, violations.Count,
            $"The following prompt file(s) exceed the {MaxBytes:N0}-byte Read-tool cap ({violations.Count} violation(s)):\n" +
            string.Join("\n", violations) + "\n\nSplit the file into smaller sub-files under a prompts/phases/ subdirectory.");
    }

    private static string ResolveSkillPath()
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        return Path.Combine(repoRoot, "skills", SkillName, "SKILL.md");
    }

    private static Dictionary<string, string> ExtractFrontmatter(string contents, string skillPath)
    {
        var match = Regex.Match(contents, @"^---\s*\r?\n(?<body>.*?)\r?\n---\s*\r?\n", RegexOptions.Singleline);
        Assert.IsTrue(match.Success,
            $"mcp-server-surface-test SKILL.md at {skillPath} is missing a `---`-delimited frontmatter block at the file head.");

        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in match.Groups["body"].Value.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line)) continue;
            var colon = line.IndexOf(':');
            if (colon < 0) continue;
            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
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
            $"mcp-server-surface-test frontmatter at {skillPath} is missing the `{field}` field.");
        var actual = frontmatter[field];
        Assert.IsFalse(string.IsNullOrWhiteSpace(actual),
            $"mcp-server-surface-test frontmatter field `{field}` at {skillPath} is empty.");
        if (expectedValue is not null)
        {
            Assert.AreEqual(expectedValue, actual,
                $"mcp-server-surface-test frontmatter field `{field}` at {skillPath} must equal `{expectedValue}`.");
        }
    }
}
