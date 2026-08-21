using System.Text.RegularExpressions;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ReleaseManagedFileGuardDocumentationTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    [TestMethod]
    public void WorkflowTable_MatchesCanonicalGuardSet()
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var guardScript = File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "guard-release-managed-files.ps1"));
        var workflow = File.ReadAllText(
            Path.Combine(repositoryRoot, "ai_docs", "workflow.md"));

        var managedBlock = Regex.Match(
            guardScript,
            @"\$managedExact\s*=\s*@\((?<entries>.*?)\)",
            RegexOptions.Singleline,
            RegexTimeout);
        Assert.IsTrue(managedBlock.Success, "Could not locate the guard script's $managedExact array.");

        var guardedPaths = Regex.Matches(
                managedBlock.Groups["entries"].Value,
                @"'(?<path>[^']+)'",
                RegexOptions.None,
                RegexTimeout)
            .Select(match => match.Groups["path"].Value.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);

        var basenameRule = Regex.Match(
            guardScript,
            @"\$basenameLower\s+-eq\s+'(?<path>[^']+)'",
            RegexOptions.None,
            RegexTimeout);
        Assert.IsTrue(basenameRule.Success, "Could not locate the guard script's basename rule.");
        guardedPaths.Add(basenameRule.Groups["path"].Value);

        var documentedSection = Regex.Match(
            workflow,
            @"## Release-managed file guard(?<section>.*?)\*\*Bypass mechanism\.\*\*",
            RegexOptions.Singleline,
            RegexTimeout);
        Assert.IsTrue(documentedSection.Success, "Could not locate the workflow's release-managed guard section.");
        var documentedSectionText = documentedSection.Groups["section"].Value;

        var documentedPaths = Regex.Matches(
                documentedSectionText,
                @"^\|\s*\d+\s*\|\s*`(?<path>[^`]+)`",
                RegexOptions.Multiline,
                RegexTimeout)
            .Select(match => match.Groups["path"].Value.ToLowerInvariant())
            .ToList();

        Assert.AreEqual(
            documentedPaths.Count,
            documentedPaths.Distinct(StringComparer.Ordinal).Count(),
            "The workflow guard table must not list a path more than once.");
        CollectionAssert.AreEquivalent(
            guardedPaths.Order(StringComparer.Ordinal).ToList(),
            documentedPaths.Order(StringComparer.Ordinal).ToList(),
            "The workflow guard table must match the canonical guard script set exactly.");
        StringAssert.Contains(
            documentedSectionText.Replace('\n', ' '),
            "Treat `eng/guard-release-managed-files.ps1` as the canonical path list");
    }

    [TestMethod]
    public void GuardScript_PointsToRepoLocalHookConfiguration()
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var guardScript = File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "guard-release-managed-files.ps1"));

        StringAssert.Contains(
            guardScript,
            "Hook config: .claude/settings.json -> PreToolUse -> Edit|Write|MultiEdit.");
        Assert.IsFalse(
            guardScript.Contains("Hook config: hooks/hooks.json", StringComparison.Ordinal),
            "The shipped hook configuration must not be documented as owning the repo-local command guard.");
    }
}
