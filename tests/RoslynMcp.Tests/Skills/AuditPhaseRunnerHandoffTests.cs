using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace RoslynMcp.Tests.Skills;

/// <summary>
/// Static contract tests for the audit-phase-runner handoff.
///
/// After the unify-audit-prompt-single-source refactor (PR #633), the canonical audit prompt is
/// `skills/mcp-server-surface-test/prompts/full.md` (shipped), and its Phase 0.5 is the source of
/// truth for the dispatch plan that hands log-heavy/read-side phases to `audit-phase-runner`
/// subagents while keeping Phase 6 + every preview/apply chain in the orchestrator's context.
///
/// The runtime subagent boundary is client-host behavior, not something the MSTest suite can
/// execute directly. These tests pin the file contracts that make the handoff reviewable.
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
    public void Prompt_DispatchesViaAuditPhaseRunner()
    {
        // The canonical audit prompt's Phase 0.5 establishes the subagent dispatch plan. The dispatch
        // is the only way the --full tier achieves 250+ tool-call coverage without burning a single
        // agent's context window.
        var prompt = File.ReadAllText(ResolveFullPromptPath());

        StringAssert.Contains(prompt, "audit-phase-runner");
        StringAssert.Contains(prompt, "Phase 0.5: Subagent dispatch plan");
        StringAssert.Contains(prompt, "Subagent dispatch groups");
    }

    [TestMethod]
    public void Prompt_KeepsPhase6AndOrchestratorOwnedPhasesInline()
    {
        // Phase 6 (write-mode applies to disposable worktree) and its setup/teardown are explicitly
        // orchestrator-owned — a crashed subagent must not leak the worktree, so the try/finally
        // discipline lives in a single surviving caller.
        var prompt = File.ReadAllText(ResolveFullPromptPath());

        StringAssert.Contains(prompt, "Orchestrator-owned phases");
        StringAssert.Contains(prompt, "Phase 6 **setup**");
        StringAssert.Contains(prompt, "teardown");
        StringAssert.Contains(prompt, "try/finally");
    }

    [TestMethod]
    public void Runner_ForbidsMutationApplyAndGitActions()
    {
        var runner = File.ReadAllText(ResolveRunnerPath());

        StringAssert.Contains(runner, "Do not call any `*_apply`, `apply_*`, file-write, git-mutation, PR, or merge command.");
        StringAssert.Contains(runner, "Phase 6 refactoring and any preview/apply chain are forbidden");
    }

    [TestMethod]
    public void PromptAndRunner_AgreeOnDelegatedPhaseSet()
    {
        // The runner agent declares the contract: phase parameter is one of `1`, `2`, `3`, `4`, `8`, or `8b`.
        // The canonical prompt's Phase 0.5 dispatch plan must include those phase numbers somewhere
        // in its group tables so the two files stay in sync. Phase 3 (deep symbol analysis) and
        // Phase 4 (flow analysis) were added in the surface-test-subagent-symbol-selection-must-be-autonomous
        // initiative so G2 subagents have a codified phase listing and selection rule.
        var prompt = File.ReadAllText(ResolveFullPromptPath());
        var runner = File.ReadAllText(ResolveRunnerPath());

        StringAssert.Contains(runner, "one of `1`, `2`, `3`, `4`, `8`, or `8b`");

        // Each of these phase numbers must appear in the prompt's dispatch plan section. We don't
        // pin exact group placement because Phase 0.5 may legitimately re-group phases across
        // refactors; what we care about is that the runner-delegated phases stay accounted for.
        // The dispatch plan section starts at "### Phase 0.5" and is bounded by the next "### Phase 1".
        var dispatchPlanStart = prompt.IndexOf("### Phase 0.5", StringComparison.Ordinal);
        var dispatchPlanEnd = prompt.IndexOf("### Phase 1:", StringComparison.Ordinal);
        Assert.IsTrue(dispatchPlanStart >= 0, "Phase 0.5 section not found in full.md");
        Assert.IsTrue(dispatchPlanEnd > dispatchPlanStart, "Phase 1 section not found after Phase 0.5 in full.md");
        var dispatchPlan = prompt[dispatchPlanStart..dispatchPlanEnd];

        foreach (var phase in new[] { "1, 2", "3, 4", "7, 8, 8b" })
        {
            StringAssert.Contains(dispatchPlan, phase);
        }
    }

    private static string ResolveRunnerPath()
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        return Path.Combine(repoRoot, "agents", "audit-phase-runner.md");
    }

    private static string ResolveFullPromptPath()
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        return Path.Combine(repoRoot, "skills", "mcp-server-surface-test", "prompts", "full.md");
    }
}
