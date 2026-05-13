using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace RoslynMcp.Tests.Skills;

/// <summary>
/// Lockstep contract test that asserts every SKILL.md in both the maintainer-only
/// `.claude/skills/*/SKILL.md` tree and the shipped `skills/*/SKILL.md` tree has an
/// <c>installed_as:</c> frontmatter field with a valid value.
///
/// The <c>installed_as:</c> field is the machine-readable contract that lets the slash-command
/// picker, backlog-sweep planner, and semantic-search routing unambiguously identify which
/// skill handles a given invocation token.  Without it, Claude must guess from freeform
/// <c>description:</c> text, which causes mis-routing (e.g. session a26fdd10 — "stress test"
/// routed to the wrong skill because no canonical identifier was present).
///
/// Valid values:
///   <c>installed_as: &lt;bare-name&gt;</c>          — bare kebab-case identifier (no namespace)
///   <c>installed_as: roslyn-mcp:&lt;bare-name&gt;</c> — plugin-namespaced identifier
///
/// This test is shipped <c>[Ignore]</c>-marked to avoid blocking CI until the bulk frontmatter
/// migration (backlog row <c>skill-namespace-installed-as-bulk-frontmatter-migration</c>) is
/// complete.  Once that row ships, remove the <c>[Ignore]</c> attribute.
/// </summary>
[TestClass]
public sealed class SkillFrontmatterInstalledAsTests
{
    // Valid installed_as values.
    //   bare:       foo-bar, my-skill-name
    //   namespaced: roslyn-mcp:foo-bar
    private static readonly Regex ValidInstalledAsPattern =
        new(@"^(?:roslyn-mcp:)?[a-z][a-z0-9-]+$", RegexOptions.Compiled);

    // Frontmatter block: leading `---\n` ... `\n---\n` (non-greedy, dotall).
    private static readonly Regex FrontmatterPattern =
        new(@"^---\s*\r?\n(?<body>.*?)\r?\n---\s*\r?\n", RegexOptions.Compiled | RegexOptions.Singleline);

    [TestMethod]
    public void AllSkillFiles_ShouldHave_InstalledAs_Frontmatter()
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();

        // Collect SKILL.md files from both trees.
        var skillFiles = CollectSkillFiles(repoRoot);

        Assert.IsTrue(
            skillFiles.Count > 0,
            $"No SKILL.md files found under '.claude/skills/' or 'skills/' relative to repo root '{repoRoot}'. " +
            "If the skill directories were reorganised, update the paths in this test.");

        var failures = new List<string>();

        foreach (var path in skillFiles)
        {
            var contents = File.ReadAllText(path);
            var frontmatter = ExtractFrontmatter(contents, path);

            if (frontmatter is null)
            {
                failures.Add($"{RelPath(repoRoot, path)}: missing `---`-delimited frontmatter block");
                continue;
            }

            if (!frontmatter.TryGetValue("installed_as", out var installedAs) ||
                string.IsNullOrWhiteSpace(installedAs))
            {
                failures.Add($"{RelPath(repoRoot, path)}: missing `installed_as:` frontmatter field");
                continue;
            }

            if (!ValidInstalledAsPattern.IsMatch(installedAs))
            {
                failures.Add(
                    $"{RelPath(repoRoot, path)}: `installed_as: {installedAs}` does not match " +
                    @"required pattern `^(?:roslyn-mcp:)?[a-z][a-z0-9-]+$`");
            }
        }

        Assert.AreEqual(
            0, failures.Count,
            $"{failures.Count} of {skillFiles.Count} SKILL.md file(s) are missing a valid `installed_as:` field:\n" +
            string.Join("\n", failures) + "\n\n" +
            "Add `installed_as: <bare-name>` or `installed_as: roslyn-mcp:<bare-name>` to each frontmatter block. " +
            "See backlog row `skill-namespace-installed-as-bulk-frontmatter-migration` for the migration plan.");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static List<string> CollectSkillFiles(string repoRoot)
    {
        var result = new List<string>();
        var skillDirs = new[]
        {
            Path.Combine(repoRoot, ".claude", "skills"),
            Path.Combine(repoRoot, "skills"),
        };

        foreach (var dir in skillDirs)
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(dir, "SKILL.md", SearchOption.AllDirectories))
            {
                result.Add(file);
            }
        }

        return result;
    }

    private static Dictionary<string, string>? ExtractFrontmatter(string contents, string path)
    {
        var match = FrontmatterPattern.Match(contents);
        if (!match.Success)
        {
            return null;
        }

        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in match.Groups["body"].Value.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line)) { continue; }
            var colon = line.IndexOf(':');
            if (colon < 0) { continue; }
            var key   = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            // Strip surrounding double quotes.
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            {
                value = value[1..^1];
            }
            dict[key] = value;
        }
        return dict;
    }

    private static string RelPath(string repoRoot, string fullPath)
    {
        if (fullPath.StartsWith(repoRoot, StringComparison.OrdinalIgnoreCase))
        {
            return fullPath[repoRoot.Length..].TrimStart(Path.DirectorySeparatorChar, '/').Replace('\\', '/');
        }
        return fullPath;
    }
}
