using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RoslynMcp.Tests.Support;

namespace RoslynMcp.Tests.Skills;

/// <summary>
/// Build-only echo of the prefix-agnostic half of <c>eng/verify-skills-are-generic.ps1</c>.
/// The PowerShell gate is canonical; this mirrors it so a contributor without <c>pwsh</c> still
/// catches drift via <c>dotnet test</c>.
///
/// Contract enforced here:
///   1. The shipped policy (<c>eng/banned-skill-markers.json</c>) carries a populated
///      <c>prefixAgnostic</c> section and at least the two canonical precheck block forms.
///   2. The imperative rule fires on the pre-fix note's shapes — "Before running any
///      <c>mcp__roslyn__*</c> tool call", "Call <c>mcp__roslyn__server_info</c>", "Run
///      <c>mcp__roslyn__server_heartbeat</c>" — because the MCP tool prefix is CLIENT-ASSIGNED
///      and a shipped skill must never instruct the agent to call a hard-coded literal.
///   3. The rule does NOT fire on the canonical note's own illustrative prefixes, which carry the
///      "examples, not an allowed list" disclaimer. That false positive would make the whole
///      assertion unshippable.
///   4. Every canonical precheck block on disk is byte-identical to the policy text, with
///      per-skill trailing prose after it allowed (the workspace-health case).
///   5. <c>residualUnsweptAllowlist</c> is a SHRINKING amnesty — every entry exists on disk, and
///      the count never exceeds the ratchet recorded when the assertion landed.
/// </summary>
[TestClass]
public sealed class SkillPrefixGenericityTests
{
    /// <summary>
    /// Shrink ratchet. Five shipped skills still carried the pre-fix bare-prefix precheck when this
    /// gate landed (<c>semantic-find</c>, <c>test-coverage</c>, <c>trace-flow</c>,
    /// <c>version-bump</c>, <c>workspace-health</c>). The allowlist may only ever get SMALLER —
    /// lowering this constant as the sweep batches land is the intended edit; raising it is not.
    /// </summary>
    private const int ResidualUnsweptRatchet = 5;

    [TestMethod]
    public void PrefixAgnosticPolicy_IsPopulated()
    {
        var policy = LoadPolicy();

        Assert.IsNotNull(policy.PrefixAgnostic, "eng/banned-skill-markers.json is missing prefixAgnostic.");
        Assert.IsNotEmpty(policy.PrefixAgnostic.ImperativePatterns);
        Assert.IsNotEmpty(policy.PrefixAgnostic.ExemptSpans);
        Assert.IsNotNull(policy.CanonicalPrecheckBlocks);

        var ids = policy.CanonicalPrecheckBlocks.Select(b => b.Id).ToList();
        CollectionAssert.Contains(ids, "connectivity-precheck-section");
        CollectionAssert.Contains(ids, "tool-prefix-inline-blockquote");

        foreach (var block in policy.CanonicalPrecheckBlocks)
        {
            Assert.IsNotEmpty(block.Text, $"Canonical block '{block.Id}' has no text.");
            Assert.IsFalse(
                string.IsNullOrWhiteSpace(block.AnchorPattern),
                $"Canonical block '{block.Id}' has no anchorPattern.");
        }
    }

    [TestMethod]
    [DataRow("Before running any `mcp__roslyn__*` tool call, probe the server once:")]
    [DataRow("1. Call `mcp__roslyn__server_info` — confirm the response includes `connection.state: \"ready\"`.")]
    [DataRow("Run `mcp__roslyn__server_heartbeat` to confirm connection state, then re-run this skill.")]
    [DataRow("2. Invoke `mcp__plugin_roslyn-mcp_roslyn__workspace_load` before anything else.")]
    [DataRow("Verify `mcp__roslyn__server_info` appears in your tool surface before proceeding.")]
    public void ImperativeRule_FiresOnHardCodedPrefixInstructions(string line)
    {
        Assert.IsTrue(
            MatchesImperativeRule(line),
            $"Expected the prefix-agnostic rule to flag a hard-coded prefix imperative: {line}");
    }

    [TestMethod]
    [DataRow("> **The prefix is client-assigned — never hard-code it.** … the same tool surfaces as `mcp__roslyn__server_info` on a dev-build entry, as `mcp__plugin_roslyn-mcp_roslyn__server_info` on the marketplace install. Those two are **examples, not an allowed list** — every prefix is valid.")]
    [DataRow("The prefix does not matter — your client assigns it, so it may be `mcp__roslyn__…` or anything else.")]
    [DataRow("Resolve once by suffix and then pin the prefix: a missing `mcp__roslyn__`-prefixed literal is **not** grounds to halt.")]
    [DataRow("1. Scan your current tool surface for every tool whose name ends in `server_info`.")]
    public void ImperativeRule_DoesNotFireOnIllustrativePrefixes(string line)
    {
        Assert.IsFalse(
            MatchesImperativeRule(line),
            $"The prefix-agnostic rule must not flag an illustrative/disclaimed prefix: {line}");
    }

    [TestMethod]
    public void ImperativeRule_ClearsEveryShippedSkillOutsideTheAllowlist()
    {
        var policy = LoadPolicy();
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var allowed = AllowlistSet(policy);
        var offenders = new List<string>();

        foreach (var (relative, lines) in EnumerateShippedSkillFiles(repoRoot, policy))
        {
            if (allowed.Contains(relative))
            {
                continue;
            }

            for (var i = 0; i < lines.Length; i++)
            {
                if (MatchesImperativeRule(lines[i], policy))
                {
                    offenders.Add($"{relative}:{i + 1}: {lines[i]}");
                }
            }
        }

        Assert.AreEqual(
            0,
            offenders.Count,
            $"Shipped skills must not hard-code an MCP tool prefix inside a call/verify instruction:{Environment.NewLine}"
                + string.Join(Environment.NewLine, offenders));
    }

    [TestMethod]
    public void CanonicalPrecheckBlocks_AreByteIdenticalWhereverTheyAppear()
    {
        var policy = LoadPolicy();
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var allowed = AllowlistSet(policy);
        var drift = new List<string>();
        var matched = 0;

        foreach (var (relative, lines) in EnumerateShippedSkillFiles(repoRoot, policy))
        {
            if (allowed.Contains(relative))
            {
                continue;
            }

            foreach (var block in policy.CanonicalPrecheckBlocks)
            {
                var anchor = new Regex(block.AnchorPattern);
                for (var i = 0; i < lines.Length; i++)
                {
                    if (!anchor.IsMatch(lines[i]))
                    {
                        continue;
                    }

                    matched++;
                    var extracted = Extract(lines, i, block.TerminatorPattern);
                    if (extracted.Count < block.Text.Length)
                    {
                        drift.Add($"{relative}:{i + 1}: block '{block.Id}' truncated to {extracted.Count} lines (expected >= {block.Text.Length}).");
                        continue;
                    }

                    for (var k = 0; k < block.Text.Length; k++)
                    {
                        if (!string.Equals(extracted[k], block.Text[k], StringComparison.Ordinal))
                        {
                            drift.Add($"{relative}:{i + 1 + k}: block '{block.Id}' drifted -> {extracted[k]}");
                            break;
                        }
                    }
                }
            }
        }

        Assert.IsTrue(matched > 0, "No shipped skill matched any canonical precheck anchor — the identity assertion is vacuous.");
        Assert.AreEqual(
            0,
            drift.Count,
            $"Canonical precheck blocks drifted from eng/banned-skill-markers.json:{Environment.NewLine}"
                + string.Join(Environment.NewLine, drift));
    }

    [TestMethod]
    public void CanonicalBlockIdentity_DetectsASingleCharacterMutation()
    {
        var policy = LoadPolicy();
        var block = policy.CanonicalPrecheckBlocks.Single(b => b.Id == "connectivity-precheck-section");
        var mutated = (string[])block.Text.Clone();
        mutated[2] = mutated[2].Replace("**once**", "**Once**", StringComparison.Ordinal);

        Assert.AreNotEqual(
            block.Text[2],
            mutated[2],
            "Fixture setup failed — the mutation did not change the line.");
        Assert.IsFalse(
            block.Text.SequenceEqual(mutated, StringComparer.Ordinal),
            "A one-character drift inside the canonical block must not compare equal.");
    }

    [TestMethod]
    public void CanonicalBlockIdentity_AllowsPerSkillTrailingProse()
    {
        var policy = LoadPolicy();
        var block = policy.CanonicalPrecheckBlocks.Single(b => b.Id == "connectivity-precheck-section");
        var withTrailer = block.Text
            .Concat(["Note: this skill reports server status, so a failing precheck is itself the answer.", string.Empty])
            .ToArray();

        // The gate compares the canonical text as the LEADING slice, so appended per-skill prose
        // must still pass while the canonical portion stays byte-identical.
        var extracted = Extract(withTrailer, 0, terminatorPattern: null);
        Assert.HasCount(1, extracted, "Fixture setup failed — a null terminator must extract exactly one line.");

        Assert.IsTrue(
            withTrailer.Length > block.Text.Length,
            "Fixture setup failed — no trailer was appended.");
        for (var k = 0; k < block.Text.Length; k++)
        {
            Assert.AreEqual(
                block.Text[k],
                withTrailer[k],
                $"Line {k} of the canonical block diverged under a per-skill trailer.");
        }
    }

    [TestMethod]
    public void ResidualUnsweptAllowlist_OnlyShrinksAndEveryEntryExists()
    {
        var policy = LoadPolicy();
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var allowlist = policy.PrefixAgnostic.ResidualUnsweptAllowlist;

        Assert.IsTrue(
            allowlist.Length <= ResidualUnsweptRatchet,
            $"residualUnsweptAllowlist grew to {allowlist.Length}; the ratchet is {ResidualUnsweptRatchet}. "
                + "The allowlist is a shrinking amnesty — sweep the skill onto the canonical note instead of adding an entry.");

        foreach (var entry in allowlist)
        {
            Assert.IsTrue(
                File.Exists(Path.Combine(repoRoot, entry.Replace('/', Path.DirectorySeparatorChar))),
                $"residualUnsweptAllowlist entry '{entry}' no longer exists — drop the stale entry rather than banking permanent amnesty.");
        }
    }

    [TestMethod]
    public void PowerShellGate_ConsumesThePrefixAgnosticPolicy()
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(repoRoot, "eng", "verify-skills-are-generic.ps1"));

        StringAssert.Contains(script, "$policy.prefixAgnostic");
        StringAssert.Contains(script, "$policy.canonicalPrecheckBlocks");
        StringAssert.Contains(script, "Assert-PrefixAgnostic");
        StringAssert.Contains(script, "Assert-CanonicalBlockIdentity");
        StringAssert.Contains(script, "residualUnsweptAllowlist");
    }

    private static List<string> Extract(string[] lines, int start, string? terminatorPattern)
    {
        var extracted = new List<string>();
        if (string.IsNullOrEmpty(terminatorPattern))
        {
            extracted.Add(lines[start]);
            return extracted;
        }

        var terminator = new Regex(terminatorPattern);
        for (var j = start; j < lines.Length; j++)
        {
            if (j > start && terminator.IsMatch(lines[j]))
            {
                break;
            }

            extracted.Add(lines[j]);
        }

        return extracted;
    }

    private static IEnumerable<(string Relative, string[] Lines)> EnumerateShippedSkillFiles(
        string repoRoot,
        GenericityPolicy policy)
    {
        var skillsDir = Path.Combine(repoRoot, "skills");
        if (!Directory.Exists(skillsDir))
        {
            yield break;
        }

        foreach (var path in Directory.EnumerateFiles(skillsDir, policy.FilePattern, SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(repoRoot, path).Replace('\\', '/');
            yield return (relative, File.ReadAllLines(path));
        }
    }

    private static HashSet<string> AllowlistSet(GenericityPolicy policy) =>
        new(policy.PrefixAgnostic.ResidualUnsweptAllowlist, StringComparer.OrdinalIgnoreCase);

    private static bool MatchesImperativeRule(string line) => MatchesImperativeRule(line, LoadPolicy());

    private static bool MatchesImperativeRule(string line, GenericityPolicy policy)
    {
        foreach (var span in policy.PrefixAgnostic.ExemptSpans)
        {
            if (Regex.IsMatch(line, span))
            {
                return false;
            }
        }

        return policy.PrefixAgnostic.ImperativePatterns.Any(pattern => Regex.IsMatch(line, pattern));
    }

    private static GenericityPolicy LoadPolicy()
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var policyPath = Path.Combine(repoRoot, "eng", "banned-skill-markers.json");
        var policy = JsonSerializer.Deserialize<GenericityPolicy>(
            File.ReadAllText(policyPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return policy ?? throw new AssertFailedException($"Could not deserialize {policyPath}.");
    }

    /// <summary>
    /// Local DTO. The sibling <c>McpServerSurfaceTestSkillTests</c> declares a private
    /// three-property record for the banned-pattern half of the same file; this one models the
    /// prefix-agnostic half without widening that type.
    /// </summary>
    private sealed record GenericityPolicy
    {
        [JsonPropertyName("filePattern")]
        public string FilePattern { get; init; } = "*.md";

        [JsonPropertyName("prefixAgnostic")]
        public PrefixAgnosticPolicy PrefixAgnostic { get; init; } = new();

        [JsonPropertyName("canonicalPrecheckBlocks")]
        public CanonicalBlock[] CanonicalPrecheckBlocks { get; init; } = [];
    }

    private sealed record PrefixAgnosticPolicy
    {
        [JsonPropertyName("imperativePatterns")]
        public string[] ImperativePatterns { get; init; } = [];

        [JsonPropertyName("exemptSpans")]
        public string[] ExemptSpans { get; init; } = [];

        [JsonPropertyName("residualUnsweptAllowlist")]
        public string[] ResidualUnsweptAllowlist { get; init; } = [];
    }

    private sealed record CanonicalBlock
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("anchorPattern")]
        public string AnchorPattern { get; init; } = string.Empty;

        [JsonPropertyName("terminatorPattern")]
        public string? TerminatorPattern { get; init; }

        [JsonPropertyName("text")]
        public string[] Text { get; init; } = [];
    }
}
