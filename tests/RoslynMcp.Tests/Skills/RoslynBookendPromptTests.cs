using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace RoslynMcp.Tests.Skills;

/// <summary>
/// Static contract tests confirming the Roslyn workspace-bookend prelude is present in
/// the targeted routine-flow prompts and refactor skills.
///
/// After the routine-flows-wrap-csharp-work-with-roslyn-bookends initiative, four files
/// contain an auto-probe block that globs CWD for *.slnx / *.sln / *.csproj, loads the
/// workspace on a positive hit, and surfaces the top 5 Roslyn semantic primitives as a
/// recommended-tool block. Non-C# repos skip cleanly via the `applicable: false` path.
///
/// The bookend behaviour is prompt/skill instruction — not executable C#. These tests pin
/// the text contracts so that a reviewer or future edit cannot silently remove the prelude.
/// </summary>
[TestClass]
public sealed class RoslynBookendPromptTests
{
    // -------------------------------------------------------------------------
    // backlog-sweep-plan.md
    // -------------------------------------------------------------------------

    [TestMethod]
    public void BacklogSweepPlan_HasWorkspaceBookendStep0()
    {
        var text = File.ReadAllText(ResolvePath("ai_docs", "prompts", "backlog-sweep-plan.md"));

        StringAssert.Contains(text, "## Step 0 — Workspace bookend");
    }

    [TestMethod]
    public void BacklogSweepPlan_BookendGlobsCwd()
    {
        var text = File.ReadAllText(ResolvePath("ai_docs", "prompts", "backlog-sweep-plan.md"));

        StringAssert.Contains(text, "*.slnx");
        StringAssert.Contains(text, "*.sln");
        StringAssert.Contains(text, "*.csproj");
    }

    [TestMethod]
    public void BacklogSweepPlan_BookendHasNonCSharpSkipPath()
    {
        var text = File.ReadAllText(ResolvePath("ai_docs", "prompts", "backlog-sweep-plan.md"));

        StringAssert.Contains(text, "applicable: false");
    }

    [TestMethod]
    public void BacklogSweepPlan_BookendSurfacesRecommendedToolBlock()
    {
        var text = File.ReadAllText(ResolvePath("ai_docs", "prompts", "backlog-sweep-plan.md"));

        StringAssert.Contains(text, "find_references");
        StringAssert.Contains(text, "find_consumers");
        StringAssert.Contains(text, "find_implementations");
        StringAssert.Contains(text, "compile_check");
        StringAssert.Contains(text, "validate_workspace");
    }

    [TestMethod]
    public void BacklogSweepPlan_BookendHasPostMutationExitGate()
    {
        var text = File.ReadAllText(ResolvePath("ai_docs", "prompts", "backlog-sweep-plan.md"));

        StringAssert.Contains(text, "Post-mutation exit gate");
        StringAssert.Contains(text, "validate_workspace");
    }

    // -------------------------------------------------------------------------
    // backlog-sweep-execute.md
    // -------------------------------------------------------------------------

    [TestMethod]
    public void BacklogSweepExecute_HasWorkspacePreludeInStep3()
    {
        var text = File.ReadAllText(ResolvePath("ai_docs", "prompts", "backlog-sweep-execute.md"));

        StringAssert.Contains(text, "Workspace prelude");
    }

    [TestMethod]
    public void BacklogSweepExecute_PreludeGlobsCwd()
    {
        var text = File.ReadAllText(ResolvePath("ai_docs", "prompts", "backlog-sweep-execute.md"));

        StringAssert.Contains(text, "*.slnx");
        StringAssert.Contains(text, "*.sln");
        StringAssert.Contains(text, "*.csproj");
    }

    [TestMethod]
    public void BacklogSweepExecute_PreludeHasNonCSharpSkipPath()
    {
        var text = File.ReadAllText(ResolvePath("ai_docs", "prompts", "backlog-sweep-execute.md"));

        StringAssert.Contains(text, "applicable: false");
    }

    [TestMethod]
    public void BacklogSweepExecute_PreludeSurfacesRecommendedToolBlock()
    {
        var text = File.ReadAllText(ResolvePath("ai_docs", "prompts", "backlog-sweep-execute.md"));

        StringAssert.Contains(text, "find_references");
        StringAssert.Contains(text, "find_consumers");
        StringAssert.Contains(text, "find_implementations");
        StringAssert.Contains(text, "compile_check");
        StringAssert.Contains(text, "validate_workspace");
    }

    [TestMethod]
    public void BacklogSweepExecute_PreludeHasPostMutationExitGate()
    {
        var text = File.ReadAllText(ResolvePath("ai_docs", "prompts", "backlog-sweep-execute.md"));

        StringAssert.Contains(text, "Post-mutation exit gate");
        StringAssert.Contains(text, "validate_workspace");
    }

    // -------------------------------------------------------------------------
    // skills/refactor/SKILL.md
    // -------------------------------------------------------------------------

    [TestMethod]
    public void RefactorSkill_HasAutoProbeNotConditionalAsk()
    {
        var text = File.ReadAllText(ResolvePath("skills", "refactor", "SKILL.md"));

        // The old conditional-ask must be replaced (not merely augmented).
        Assert.IsFalse(
            text.Contains("If a workspace is not already loaded, ask the user for the solution path"),
            "The old conditional-ask line should have been replaced by the auto-probe block.");

        StringAssert.Contains(text, "Workspace auto-probe");
    }

    [TestMethod]
    public void RefactorSkill_AutoProbeGlobsCwd()
    {
        var text = File.ReadAllText(ResolvePath("skills", "refactor", "SKILL.md"));

        StringAssert.Contains(text, "*.slnx");
        StringAssert.Contains(text, "*.sln");
        StringAssert.Contains(text, "*.csproj");
    }

    [TestMethod]
    public void RefactorSkill_AutoProbeHasNonCSharpSkipPath()
    {
        var text = File.ReadAllText(ResolvePath("skills", "refactor", "SKILL.md"));

        StringAssert.Contains(text, "applicable: false");
    }

    [TestMethod]
    public void RefactorSkill_AutoProbeSurfacesRecommendedToolBlock()
    {
        var text = File.ReadAllText(ResolvePath("skills", "refactor", "SKILL.md"));

        StringAssert.Contains(text, "find_references");
        StringAssert.Contains(text, "find_consumers");
        StringAssert.Contains(text, "find_implementations");
        StringAssert.Contains(text, "compile_check");
        StringAssert.Contains(text, "validate_workspace");
    }

    // -------------------------------------------------------------------------
    // skills/refactor-loop/SKILL.md
    // -------------------------------------------------------------------------

    [TestMethod]
    public void RefactorLoopSkill_HasAutoProbeNotConditionalAsk()
    {
        var text = File.ReadAllText(ResolvePath("skills", "refactor-loop", "SKILL.md"));

        // The old conditional-ask must be replaced (not merely augmented).
        Assert.IsFalse(
            text.Contains("If no workspace is loaded, ask for the solution path"),
            "The old conditional-ask line should have been replaced by the auto-probe block.");

        StringAssert.Contains(text, "Workspace auto-probe");
    }

    [TestMethod]
    public void RefactorLoopSkill_AutoProbeGlobsCwd()
    {
        var text = File.ReadAllText(ResolvePath("skills", "refactor-loop", "SKILL.md"));

        StringAssert.Contains(text, "*.slnx");
        StringAssert.Contains(text, "*.sln");
        StringAssert.Contains(text, "*.csproj");
    }

    [TestMethod]
    public void RefactorLoopSkill_AutoProbeHasNonCSharpSkipPath()
    {
        var text = File.ReadAllText(ResolvePath("skills", "refactor-loop", "SKILL.md"));

        StringAssert.Contains(text, "applicable: false");
    }

    [TestMethod]
    public void RefactorLoopSkill_AutoProbeSurfacesRecommendedToolBlock()
    {
        var text = File.ReadAllText(ResolvePath("skills", "refactor-loop", "SKILL.md"));

        StringAssert.Contains(text, "find_references");
        StringAssert.Contains(text, "find_consumers");
        StringAssert.Contains(text, "find_implementations");
        StringAssert.Contains(text, "compile_check");
        StringAssert.Contains(text, "validate_workspace");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string ResolvePath(params string[] segments)
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        return Path.Combine([repoRoot, .. segments]);
    }
}
