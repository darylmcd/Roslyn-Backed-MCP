using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace RoslynMcp.Tests.Skills;

/// <summary>
/// Lockstep tests for the three surfaces that share the area / severity enums:
///
///   1. `ai_docs/items/backlog-d-fragment-schema.md` — canonical enum source of truth.
///   2. `.github/ISSUE_TEMPLATE/mcp-server-surface-test-finding.yml` — public form options.
///   3. `scripts/seed-issue-labels.ps1` — the `area:*` / `severity:*` GitHub labels.
///
/// Drift between any pair of these surfaces is a contract break:
///   - A schema enum value with no matching label means a `gh issue create --label area:X` call
///     fails for that value.
///   - A seed-script label with no schema entry means the maintainer creates a label that the
///     renderer can never produce — dead surface.
///   - A template option with no schema entry means a contributor can submit a finding the
///     intake flow will reject as malformed.
///
/// Two enum tokens are intentionally OMITTED from the public form and the label seeder:
///   - `severity: P0` — refused for public filing by `Test-FindingShouldRefusePublicFile`.
///   - `area: security` — same.
/// Both are valid in `backlog.d/` fragments (they're how the renderer detects the refusal),
/// but they never reach a public Issue. Tests below pin both directions: included in the
/// schema, excluded from the form + seeder.
/// </summary>
[TestClass]
public sealed class IssueTemplateAndLabelSeedTests
{
    // Canonical enums per the fragment schema doc. If these change here without a corresponding
    // doc edit, the doc-content test below catches it.
    private static readonly string[] AllAreaValues = new[]
    {
        "tools", "resources", "prompts", "skills", "concurrency", "perf", "docs", "security"
    };

    private static readonly string[] PubliclyFileableAreaValues = new[]
    {
        "tools", "resources", "prompts", "skills", "concurrency", "perf", "docs"
        // `security` deliberately omitted — refused by the renderer.
    };

    private static readonly string[] AllSeverityValues = new[] { "P0", "P1", "P2", "P3" };

    private static readonly string[] PubliclyFileableSeverityValues = new[]
    {
        "P1", "P2", "P3"
        // `P0` deliberately omitted — refused by the renderer.
    };

    [TestMethod]
    public void FragmentSchemaDoc_DocumentsTheCanonicalAreaEnum()
    {
        var schemaPath = ResolveSchemaDocPath();
        var contents = File.ReadAllText(schemaPath);

        // The doc cell for `area` must list every value in the canonical enum (including
        // `security`). Order is fixed in the doc cell so we can pin the substring.
        foreach (var value in AllAreaValues)
        {
            StringAssert.Contains(contents, $"`{value}`",
                $"backlog-d-fragment-schema.md must document the area enum value `{value}`. " +
                "If you intentionally removed it, update IssueTemplateAndLabelSeedTests.AllAreaValues to match.");
        }
    }

    [TestMethod]
    public void IssueTemplate_ParsesAndExposesOnlyPubliclyFileableAreaValues()
    {
        var templatePath = ResolveIssueTemplatePath();
        Assert.IsTrue(File.Exists(templatePath),
            $"Issue template not found at '{templatePath}'. The /mcp-server-surface-test public-funnel surface depends on it.");

        var contents = File.ReadAllText(templatePath);
        var areaOptions = ExtractDropdownOptions(contents, dropdownId: "area");

        CollectionAssert.AreEqual(
            PubliclyFileableAreaValues, areaOptions.ToArray(),
            $"Issue template `area` dropdown options drifted from the publicly-fileable subset. " +
            $"Expected: [{string.Join(", ", PubliclyFileableAreaValues)}]. " +
            $"Got: [{string.Join(", ", areaOptions)}]. " +
            "If you added an enum value, update both this test's expected list AND `area:<value>` in scripts/seed-issue-labels.ps1.");
    }

    [TestMethod]
    public void IssueTemplate_OmitsRefusedAreaSecurityFromDropdown()
    {
        var templatePath = ResolveIssueTemplatePath();
        var contents = File.ReadAllText(templatePath);
        var areaOptions = ExtractDropdownOptions(contents, dropdownId: "area");

        Assert.IsFalse(
            areaOptions.Contains("security"),
            "Issue template `area` dropdown must NOT expose `security` — that value is refused for public filing by the shared renderer's Test-FindingShouldRefusePublicFile predicate. Listing it here would invite contributors to file a vulnerability publicly, which is the exact failure mode SECURITY.md exists to prevent.");
    }

    [TestMethod]
    public void IssueTemplate_ParsesAndExposesOnlyPubliclyFileableSeverityValues()
    {
        var templatePath = ResolveIssueTemplatePath();
        var contents = File.ReadAllText(templatePath);
        var severityOptions = ExtractDropdownOptions(contents, dropdownId: "severity");

        CollectionAssert.AreEqual(
            PubliclyFileableSeverityValues, severityOptions.ToArray(),
            $"Issue template `severity` dropdown options drifted from the publicly-fileable subset. " +
            $"Expected: [{string.Join(", ", PubliclyFileableSeverityValues)}]. " +
            $"Got: [{string.Join(", ", severityOptions)}]. " +
            "If you added an enum value, update both this test's expected list AND `severity:<value>` in scripts/seed-issue-labels.ps1.");
    }

    [TestMethod]
    public void IssueTemplate_OmitsRefusedSeverityP0FromDropdown()
    {
        var templatePath = ResolveIssueTemplatePath();
        var contents = File.ReadAllText(templatePath);
        var severityOptions = ExtractDropdownOptions(contents, dropdownId: "severity");

        Assert.IsFalse(
            severityOptions.Contains("P0"),
            "Issue template `severity` dropdown must NOT expose `P0` — refused for public filing by the shared renderer. Listing it here would let a contributor file a critical bug publicly before a fix lands.");
    }

    [TestMethod]
    public void SeedLabelsScript_FileExistsAtDocumentedPath()
    {
        var scriptPath = ResolveSeedScriptPath();
        Assert.IsTrue(
            File.Exists(scriptPath),
            $"scripts/seed-issue-labels.ps1 not found at '{scriptPath}'. The maintainer-bootstrap workflow + the shipped skill's README reference this exact path; without the file the docs ship dead links.");
    }

    [TestMethod]
    public void SeedLabelsScript_AreaLabelsMatchPubliclyFileableEnum()
    {
        var scriptPath = ResolveSeedScriptPath();
        var contents = File.ReadAllText(scriptPath);
        var labels = ExtractScriptLabelNames(contents, prefix: "area:");

        CollectionAssert.AreEquivalent(
            PubliclyFileableAreaValues.Select(v => $"area:{v}").ToArray(),
            labels.ToArray(),
            $"scripts/seed-issue-labels.ps1 area labels drifted from the publicly-fileable enum. " +
            $"Expected: [{string.Join(", ", PubliclyFileableAreaValues.Select(v => $"area:{v}"))}]. " +
            $"Got: [{string.Join(", ", labels)}].");
    }

    [TestMethod]
    public void SeedLabelsScript_OmitsRefusedAreaSecurityLabel()
    {
        var scriptPath = ResolveSeedScriptPath();
        var contents = File.ReadAllText(scriptPath);
        var labels = ExtractScriptLabelNames(contents, prefix: "area:");

        Assert.IsFalse(
            labels.Contains("area:security"),
            "scripts/seed-issue-labels.ps1 must NOT seed `area:security` — that label has no public Issue surface. Refused findings are routed through GitHub security advisories per SECURITY.md, not via a public label.");
    }

    [TestMethod]
    public void SeedLabelsScript_SeverityLabelsMatchPubliclyFileableEnum()
    {
        var scriptPath = ResolveSeedScriptPath();
        var contents = File.ReadAllText(scriptPath);
        var labels = ExtractScriptLabelNames(contents, prefix: "severity:");

        CollectionAssert.AreEquivalent(
            PubliclyFileableSeverityValues.Select(v => $"severity:{v}").ToArray(),
            labels.ToArray(),
            $"scripts/seed-issue-labels.ps1 severity labels drifted from the publicly-fileable enum. " +
            $"Expected: [{string.Join(", ", PubliclyFileableSeverityValues.Select(v => $"severity:{v}"))}]. " +
            $"Got: [{string.Join(", ", labels)}].");
    }

    [TestMethod]
    public void SeedLabelsScript_OmitsRefusedSeverityP0Label()
    {
        var scriptPath = ResolveSeedScriptPath();
        var contents = File.ReadAllText(scriptPath);
        var labels = ExtractScriptLabelNames(contents, prefix: "severity:");

        Assert.IsFalse(
            labels.Contains("severity:P0"),
            "scripts/seed-issue-labels.ps1 must NOT seed `severity:P0` — refused findings have no public Issue surface.");
    }

    // ---------- Helpers ----------

    private static string ResolveSchemaDocPath()
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        return Path.Combine(repoRoot, "ai_docs", "items", "backlog-d-fragment-schema.md");
    }

    private static string ResolveIssueTemplatePath()
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        return Path.Combine(repoRoot, ".github", "ISSUE_TEMPLATE", "mcp-server-surface-test-finding.yml");
    }

    private static string ResolveSeedScriptPath()
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        return Path.Combine(repoRoot, "scripts", "seed-issue-labels.ps1");
    }

    /// <summary>
    /// Extract the values of a `type: dropdown` block whose `id:` matches <paramref name="dropdownId"/>.
    /// Walks line by line; finds the matching id, then collects `- value` entries inside the
    /// nearest following `options:` list, stopping at the next top-level `- type:` block.
    /// </summary>
    private static List<string> ExtractDropdownOptions(string templateContents, string dropdownId)
    {
        var lines = templateContents.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        var options = new List<string>();

        int idLineIndex = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim() == $"id: {dropdownId}")
            {
                idLineIndex = i;
                break;
            }
        }
        if (idLineIndex < 0)
        {
            return options;
        }

        // Find the `options:` line after the id.
        int optionsLineIndex = -1;
        for (int i = idLineIndex; i < lines.Length; i++)
        {
            if (lines[i].Trim() == "options:")
            {
                optionsLineIndex = i;
                break;
            }
            // Stop at the next dropdown block start.
            if (i > idLineIndex && lines[i].TrimStart().StartsWith("- type:", StringComparison.Ordinal))
            {
                return options;
            }
        }
        if (optionsLineIndex < 0)
        {
            return options;
        }

        // Collect option lines: leading whitespace, `- `, then the value.
        var optionRegex = new Regex(@"^\s+-\s+(?<value>\S.*?)\s*$");
        for (int i = optionsLineIndex + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            // Stop at a sibling key (less indent than the options' children) or a new `- type:` block.
            if (line.TrimStart().StartsWith("- type:", StringComparison.Ordinal))
            {
                break;
            }
            // A non-list line at the same indent as `options:` (or less) ends the list.
            if (!string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith("-", StringComparison.Ordinal) && !line.StartsWith(" ", StringComparison.Ordinal))
            {
                break;
            }
            var m = optionRegex.Match(line);
            if (m.Success)
            {
                options.Add(m.Groups["value"].Value.Trim());
            }
            else if (!string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith("-", StringComparison.Ordinal))
            {
                // Mid-block non-list line → stop.
                break;
            }
        }

        return options;
    }

    /// <summary>
    /// Extract `name = '<prefix><value>'` entries from the seed script's lockstep tables.
    /// </summary>
    private static List<string> ExtractScriptLabelNames(string scriptContents, string prefix)
    {
        var names = new List<string>();
        var pattern = new Regex(@"name\s*=\s*'(?<name>" + Regex.Escape(prefix) + @"[^']+)'");
        foreach (Match m in pattern.Matches(scriptContents))
        {
            names.Add(m.Groups["name"].Value);
        }
        return names;
    }
}
