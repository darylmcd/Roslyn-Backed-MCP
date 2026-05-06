using Microsoft.VisualStudio.TestTools.UnitTesting;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Tests.Skills;

/// <summary>
/// promote-tier-skill: round-trip parity test for the maintainer-side `/promote-tier` skill.
///
/// The skill documents two mechanical string-flip edits per tool promotion:
///   (1) `[McpToolMetadata("category", "experimental", ...)]` -> `[McpToolMetadata("category", "stable", ...)]` on the tool source
///   (2) `Tool("name", "category", "experimental", ...)` -> `Tool("name", "category", "stable", ...)` on the catalog partial
/// (For resources and prompts, only the catalog partial — the McpServerResource / McpServerPrompt attributes carry no tier marker today.)
///
/// This test asserts the documented mechanics are bit-reversible end-to-end: a forward + reverse promotion
/// leaves the source tree exactly as it found it. The test reads the source files, performs the flip in memory,
/// asserts the change took effect, reverses it, and asserts byte-for-byte content equality with the original.
/// This is the safety net that lets the skill ship without a more elaborate AST-based implementation —
/// the dual-write contract (catalog entry vs. attribute) is enforced by SurfaceCatalogTests; the bit-reversibility
/// contract is enforced here.
///
/// Choose subjects from the live catalog so the test stays correct as the catalog evolves:
///   - Tool subject:     pick a workspace-category experimental tool (workspace_drift_check today).
///   - Resource subject: pick an experimental resource (server_catalog_full today).
///   - Prompt subject:   pick an experimental prompt (explain_error today; all prompts are experimental).
/// If the chosen subject is later promoted to stable in the catalog, the test will pick a different experimental
/// entry from the same kind via the `PickExperimentalEntry` helper. The test fails loudly with a clear message
/// when no experimental entry of a kind exists (no false-passes).
///
/// HARD INVARIANT: this test NEVER writes to disk. All flips are in-memory string manipulation; the source files
/// are only read. A failure of this invariant would pollute the catalog and is a regression to root-cause and revert.
/// </summary>
[TestClass]
public sealed class PromoteTierRoundTripTests
{
    [TestMethod]
    public void Tool_TierFlip_RoundTripIsBitIdentical()
    {
        var entry = PickExperimentalEntry(ServerSurfaceCatalog.Tools, kind: "tool");

        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var catalogPartial = ResolveToolCatalogPartial(repoRoot, entry.Category);
        var implFile = ResolveToolImplFile(repoRoot, entry.Name);

        // Read originals.
        var originalCatalog = File.ReadAllText(catalogPartial);
        var originalImpl = File.ReadAllText(implFile);

        // Forward flip experimental -> stable.
        var flippedCatalog = FlipCatalogTierForTool(originalCatalog, entry.Name, entry.Category, fromTier: "experimental", toTier: "stable");
        var flippedImpl = FlipImplTierForTool(originalImpl, entry.Category, fromTier: "experimental", toTier: "stable");

        Assert.AreNotEqual(originalCatalog, flippedCatalog,
            $"Forward flip produced no change to catalog partial '{catalogPartial}'. "
            + $"The documented Tool(...) match for {entry.Name} did not match — has the catalog factory shape changed? "
            + "Update the skill's Step 4 documentation and this test's FlipCatalogTierForTool helper in lockstep.");
        Assert.AreNotEqual(originalImpl, flippedImpl,
            $"Forward flip produced no change to impl file '{implFile}'. "
            + $"The [McpToolMetadata(\"{entry.Category}\", \"experimental\", ...)] literal did not match — has the attribute shape changed? "
            + "Update the skill's Step 3 documentation and this test's FlipImplTierForTool helper in lockstep.");

        // Reverse flip stable -> experimental.
        var roundTrippedCatalog = FlipCatalogTierForTool(flippedCatalog, entry.Name, entry.Category, fromTier: "stable", toTier: "experimental");
        var roundTrippedImpl = FlipImplTierForTool(flippedImpl, entry.Category, fromTier: "stable", toTier: "experimental");

        // Assert bit-identical with originals.
        Assert.AreEqual(originalCatalog, roundTrippedCatalog,
            $"Round-trip flip on catalog partial '{catalogPartial}' is not bit-identical. "
            + "The skill's documented edit shape is not strictly reversible — investigate FlipCatalogTierForTool.");
        Assert.AreEqual(originalImpl, roundTrippedImpl,
            $"Round-trip flip on impl file '{implFile}' is not bit-identical. "
            + "The skill's documented edit shape is not strictly reversible — investigate FlipImplTierForTool.");

        // Hard invariant — no disk write occurred during the test.
        var liveCatalogOnDisk = File.ReadAllText(catalogPartial);
        var liveImplOnDisk = File.ReadAllText(implFile);
        Assert.AreEqual(originalCatalog, liveCatalogOnDisk,
            "Disk-state invariant violation: catalog partial was modified during the test. "
            + "PromoteTierRoundTripTests must NEVER write to disk.");
        Assert.AreEqual(originalImpl, liveImplOnDisk,
            "Disk-state invariant violation: impl file was modified during the test. "
            + "PromoteTierRoundTripTests must NEVER write to disk.");
    }

    [TestMethod]
    public void Resource_TierFlip_RoundTripIsBitIdentical()
    {
        var entry = PickExperimentalEntry(ServerSurfaceCatalog.Resources, kind: "resource");

        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var catalogPartial = Path.Combine(repoRoot, "src", "RoslynMcp.Host.Stdio", "Catalog", "ServerSurfaceCatalog.Resources.cs");
        Assert.IsTrue(File.Exists(catalogPartial), $"Resource catalog partial not found at {catalogPartial}");

        var originalCatalog = File.ReadAllText(catalogPartial);

        // Resources are catalog-only — no impl-side tier marker today (see SKILL.md "Step 2 — Resources" path).
        var flippedCatalog = FlipCatalogTierForResource(originalCatalog, entry.Name, entry.Category, fromTier: "experimental", toTier: "stable");
        Assert.AreNotEqual(originalCatalog, flippedCatalog,
            $"Forward flip produced no change to resource catalog '{catalogPartial}'. "
            + $"The documented Resource(\"{entry.Name}\", ...) match did not fire — has the resource factory shape changed?");

        var roundTrippedCatalog = FlipCatalogTierForResource(flippedCatalog, entry.Name, entry.Category, fromTier: "stable", toTier: "experimental");
        Assert.AreEqual(originalCatalog, roundTrippedCatalog,
            $"Round-trip flip on resource catalog '{catalogPartial}' is not bit-identical.");

        // Disk-state invariant.
        var liveCatalogOnDisk = File.ReadAllText(catalogPartial);
        Assert.AreEqual(originalCatalog, liveCatalogOnDisk,
            "Disk-state invariant violation: resource catalog partial was modified during the test.");
    }

    [TestMethod]
    public void Prompt_TierFlip_RoundTripIsBitIdentical()
    {
        var entry = PickExperimentalEntry(ServerSurfaceCatalog.Prompts, kind: "prompt");

        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var catalogPartial = Path.Combine(repoRoot, "src", "RoslynMcp.Host.Stdio", "Catalog", "ServerSurfaceCatalog.Prompts.cs");
        Assert.IsTrue(File.Exists(catalogPartial), $"Prompt catalog partial not found at {catalogPartial}");

        var originalCatalog = File.ReadAllText(catalogPartial);

        // Prompts are catalog-only today (see SKILL.md "Step 2 — Prompts" path).
        var flippedCatalog = FlipCatalogTierForPrompt(originalCatalog, entry.Name, entry.Category, fromTier: "experimental", toTier: "stable");
        Assert.AreNotEqual(originalCatalog, flippedCatalog,
            $"Forward flip produced no change to prompt catalog '{catalogPartial}'. "
            + $"The documented Prompt(\"{entry.Name}\", ...) match did not fire — has the prompt factory shape changed?");

        var roundTrippedCatalog = FlipCatalogTierForPrompt(flippedCatalog, entry.Name, entry.Category, fromTier: "stable", toTier: "experimental");
        Assert.AreEqual(originalCatalog, roundTrippedCatalog,
            $"Round-trip flip on prompt catalog '{catalogPartial}' is not bit-identical.");

        // Disk-state invariant.
        var liveCatalogOnDisk = File.ReadAllText(catalogPartial);
        Assert.AreEqual(originalCatalog, liveCatalogOnDisk,
            "Disk-state invariant violation: prompt catalog partial was modified during the test.");
    }

    [TestMethod]
    public void Skill_FilePresent_AndDocumentsAllThreeKinds()
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var skillPath = Path.Combine(repoRoot, ".claude", "skills", "promote-tier", "SKILL.md");
        Assert.IsTrue(File.Exists(skillPath),
            $"promote-tier SKILL.md not found at {skillPath}. The skill is the user-facing contract — its absence makes the round-trip test moot.");

        var skillContents = File.ReadAllText(skillPath);

        // Each of the three resolver paths must appear in the skill body — these mirror the test's per-kind round-trips.
        Assert.IsTrue(
            skillContents.Contains("Tools (kind == `tool`)", StringComparison.Ordinal),
            "promote-tier SKILL.md is missing the Tools resolver path (Step 2 — `tool` heading).");
        Assert.IsTrue(
            skillContents.Contains("Resources (kind == `resource`)", StringComparison.Ordinal),
            "promote-tier SKILL.md is missing the Resources resolver path (Step 2 — `resource` heading).");
        Assert.IsTrue(
            skillContents.Contains("Prompts (kind == `prompt`)", StringComparison.Ordinal),
            "promote-tier SKILL.md is missing the Prompts resolver path (Step 2 — `prompt` heading).");

        // The dual-write safety-net citation must be present (the SurfaceCatalogTests reference).
        Assert.IsTrue(
            skillContents.Contains("SurfaceCatalogTests", StringComparison.Ordinal),
            "promote-tier SKILL.md is missing its citation of SurfaceCatalogTests as the dual-write safety net.");
    }

    /// <summary>
    /// Pick the first experimental entry of the given kind from the live catalog. Fails loudly if none — the
    /// test cannot run a meaningful round-trip without a real subject. This makes the test self-correcting
    /// as the catalog evolves: as long as at least one experimental entry of each kind exists, the test runs.
    /// </summary>
    private static SurfaceEntry PickExperimentalEntry(IReadOnlyList<SurfaceEntry> entries, string kind)
    {
        var subject = entries.FirstOrDefault(e => string.Equals(e.SupportTier, "experimental", StringComparison.Ordinal));
        Assert.IsNotNull(subject,
            $"No experimental {kind} entries found in the live catalog. PromoteTierRoundTripTests requires at least one "
            + $"experimental entry of each kind to exercise the round-trip. If every {kind} is now stable, this test "
            + "needs to be skipped or rewritten to flip stable -> experimental -> stable instead.");
        return subject!;
    }

    private static string ResolveToolCatalogPartial(string repoRoot, string category)
    {
        // The catalog is split by category prefix matching the partial filename suffix.
        // Categories (today): workspace, symbols, analysis, refactoring, editing, orchestration.
        // The map is intentionally explicit rather than dynamic — partials are stable in number.
        var partialName = category switch
        {
            "workspace" or "server" => "ServerSurfaceCatalog.Workspace.cs",
            "symbols" => "ServerSurfaceCatalog.Symbols.cs",
            "analysis" => "ServerSurfaceCatalog.Analysis.cs",
            "refactoring" => "ServerSurfaceCatalog.Refactoring.cs",
            "editing" => "ServerSurfaceCatalog.Editing.cs",
            "orchestration" => "ServerSurfaceCatalog.Orchestration.cs",
            _ => throw new InvalidOperationException(
                $"Unknown tool category '{category}'. Update PromoteTierRoundTripTests.ResolveToolCatalogPartial when a new partial is added.")
        };
        var path = Path.Combine(repoRoot, "src", "RoslynMcp.Host.Stdio", "Catalog", partialName);
        Assert.IsTrue(File.Exists(path), $"Tool catalog partial not found at {path}");
        return path;
    }

    /// <summary>
    /// Find the impl file containing [McpServerTool(Name = "&lt;name&gt;")]. The skill's documented mechanics
    /// say "use mcp__roslyn__symbol_search" — for the purposes of this test, a directory walk over Tools/*.cs
    /// + a substring match on the [McpServerTool(Name = "&lt;name&gt;")] literal is sufficient and faster.
    /// </summary>
    private static string ResolveToolImplFile(string repoRoot, string toolName)
    {
        var toolsDir = Path.Combine(repoRoot, "src", "RoslynMcp.Host.Stdio", "Tools");
        Assert.IsTrue(Directory.Exists(toolsDir), $"Tools directory not found at {toolsDir}");

        var needle = $"Name = \"{toolName}\"";
        foreach (var path in Directory.EnumerateFiles(toolsDir, "*.cs", SearchOption.AllDirectories))
        {
            if (File.ReadAllText(path).Contains(needle, StringComparison.Ordinal))
            {
                return path;
            }
        }
        Assert.Fail($"Could not locate the impl file carrying [McpServerTool(Name = \"{toolName}\")] under {toolsDir}");
        return null!;
    }

    private static string FlipCatalogTierForTool(string source, string name, string category, string fromTier, string toTier)
    {
        // Match Tool("name", "category", "fromTier", ... → Tool("name", "category", "toTier", ...
        var oldLiteral = $"Tool(\"{name}\", \"{category}\", \"{fromTier}\"";
        var newLiteral = $"Tool(\"{name}\", \"{category}\", \"{toTier}\"";
        AssertSingleOccurrence(source, oldLiteral, $"Tool literal for {name} (tier={fromTier})");
        return source.Replace(oldLiteral, newLiteral);
    }

    private static string FlipImplTierForTool(string source, string category, string fromTier, string toTier)
    {
        // Match McpToolMetadata("category", "fromTier", → McpToolMetadata("category", "toTier",
        // Note: no leading '[' — the attribute often appears as the 2nd attribute in a `[Attr1, Attr2]` pair, e.g.
        //   [McpServerTool(Name = "..."), McpToolMetadata("category", "experimental", ...)]
        // so anchoring on '[' would miss those. The attribute name itself is unique enough on its own.
        var oldLiteral = $"McpToolMetadata(\"{category}\", \"{fromTier}\"";
        var newLiteral = $"McpToolMetadata(\"{category}\", \"{toTier}\"";
        AssertSingleOccurrence(source, oldLiteral, $"McpToolMetadata literal for category={category}, tier={fromTier}");
        return source.Replace(oldLiteral, newLiteral);
    }

    private static string FlipCatalogTierForResource(string source, string name, string category, string fromTier, string toTier)
    {
        var oldLiteral = $"Resource(\"{name}\", \"{category}\", \"{fromTier}\"";
        var newLiteral = $"Resource(\"{name}\", \"{category}\", \"{toTier}\"";
        AssertSingleOccurrence(source, oldLiteral, $"Resource literal for {name} (tier={fromTier})");
        return source.Replace(oldLiteral, newLiteral);
    }

    private static string FlipCatalogTierForPrompt(string source, string name, string category, string fromTier, string toTier)
    {
        var oldLiteral = $"Prompt(\"{name}\", \"{category}\", \"{fromTier}\"";
        var newLiteral = $"Prompt(\"{name}\", \"{category}\", \"{toTier}\"";
        AssertSingleOccurrence(source, oldLiteral, $"Prompt literal for {name} (tier={fromTier})");
        return source.Replace(oldLiteral, newLiteral);
    }

    /// <summary>
    /// The skill assumes each tier-flip target literal appears EXACTLY once in its file. If it appears
    /// zero times, the documented match is wrong — fail with a clear message. If it appears more than
    /// once, the skill could mutate the wrong instance — fail with a clear message. Both branches point
    /// the maintainer at the SKILL.md edit-shape contract.
    /// </summary>
    private static void AssertSingleOccurrence(string source, string literal, string description)
    {
        var occurrences = CountOccurrences(source, literal);
        Assert.AreEqual(
            1, occurrences,
            $"Expected exactly 1 occurrence of {description} in source; found {occurrences}. "
            + "If 0: the literal shape changed (update SKILL.md and the helper in lockstep). "
            + "If >1: the skill's edit could match the wrong instance — narrow the literal with more context.");
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }
}
