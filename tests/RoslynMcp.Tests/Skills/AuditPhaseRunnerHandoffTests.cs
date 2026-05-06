using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace RoslynMcp.Tests.Skills;

/// <summary>
/// audit-deep-subagent-orchestration: static contract tests for the repo-local
/// audit phase runner handoff.
///
/// The runtime subagent boundary is client-host behavior, not something the MSTest
/// suite can execute directly. These tests pin the file contracts that make the
/// handoff reviewable: only log-heavy/read-side phases are delegated, mutation-heavy
/// phases remain inline, and the runner returns a compact summary instead of raw logs.
/// </summary>
[TestClass]
public sealed class AuditPhaseRunnerHandoffTests
{
    [TestMethod]
    public void Runner_FileExists_WithStructuredSummaryContract()
    {
        var runner = File.ReadAllText(ResolveRunnerPath());

        StringAssert.Contains(runner, "## Audit Phase Runner Summary");
        StringAssert.Contains(runner, "| Phase | Phase <n> - <name> |");
        StringAssert.Contains(runner, "| Tool calls |");
        StringAssert.Contains(runner, "| Result counts |");
        StringAssert.Contains(runner, "| Anomalies |");
        StringAssert.Contains(runner, "No raw logs.");
    }

    [TestMethod]
    public void Skill_DelegatesOnlyLogHeavyReadSidePhases()
    {
        var skill = File.ReadAllText(ResolveSkillPath());

        StringAssert.Contains(skill, "Phase 1 — broad diagnostics scan | `audit-phase-runner`");
        StringAssert.Contains(skill, "Phase 2 — code quality metrics | `audit-phase-runner`");
        StringAssert.Contains(skill, "Phase 8 — build and test validation | `audit-phase-runner`");
        StringAssert.Contains(skill, "Phase 8b — concurrency audit | `audit-phase-runner`");
        StringAssert.Contains(skill, "inline fallback otherwise");
    }

    [TestMethod]
    public void Skill_KeepsPhase6AndPreviewApplyInline()
    {
        var skill = File.ReadAllText(ResolveSkillPath());

        StringAssert.Contains(skill, "Run these phases inline in the main audit context");
        StringAssert.Contains(skill, "Phase -1, 0, 3, 4, 5, 6, 7");
        StringAssert.Contains(skill, "Phase 6 and every preview/apply chain stay inline.");
        StringAssert.Contains(skill, "Do not delegate workspace-version-sensitive mutations");
    }

    [TestMethod]
    public void Runner_ForbidsMutationApplyAndGitActions()
    {
        var runner = File.ReadAllText(ResolveRunnerPath());

        StringAssert.Contains(runner, "Do not call any `*_apply`, `apply_*`, file-write, git-mutation, PR, or merge command.");
        StringAssert.Contains(runner, "Phase 6 refactoring and any preview/apply chain are forbidden");
    }

    [TestMethod]
    public void Skill_AndRunner_AgreeOnDelegatedPhaseSet()
    {
        var skill = File.ReadAllText(ResolveSkillPath());
        var runner = File.ReadAllText(ResolveRunnerPath());

        foreach (var phase in new[] { "1", "2", "8", "8b" })
        {
            StringAssert.Contains(skill, $"`phase`: one of `1`, `2`, `8`, or `8b`");
            StringAssert.Contains(runner, $"one of `1`, `2`, `8`, or `8b`");
            StringAssert.Contains(runner, $"`{phase}`");
        }
    }

    private static string ResolveSkillPath()
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        return Path.Combine(repoRoot, "skills", "audit-deep", "SKILL.md");
    }

    private static string ResolveRunnerPath()
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        return Path.Combine(repoRoot, ".claude", "agents", "audit-phase-runner.md");
    }
}
