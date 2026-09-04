using Microsoft.VisualStudio.TestTools.UnitTesting;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

/// <summary>
/// Slice-scoped ratchet for the method-description diet across the server/discovery surface
/// (<c>server_info</c>, <c>server_heartbeat</c>, <c>recommend_workflow</c>,
/// <c>suggest_refactorings</c>, <c>get_prompt_text</c>). Deliberately narrower than
/// <see cref="SurfaceCatalogTests"/>'s assembly-wide 2,000-char guard: that constant stays
/// owned by the global ratchet, while these four types are held to a ~200-char capability
/// statement with operational detail living in XML remarks.
/// </summary>
[TestClass]
public sealed class MethodDescriptionDietServerSurfaceTests
{
    /// <summary>Per-tool ceiling for a capability statement in this slice.</summary>
    private const int _maxDescriptionCharacters = 200;

    /// <summary>
    /// Aggregate ceiling for the slice, per the initiative spec. Measured 2,826 chars across
    /// 5 tools before the diet and 979 after. <see cref="_maxDescriptionCharacters"/> is a
    /// per-tool CEILING, not a target, so the aggregate is not bounded below by 5 x 200; this
    /// constant is the measured post-diet total plus a small drafting headroom.
    /// </summary>
    private const int _maxAggregateDescriptionCharacters = 1_050;

    private static readonly Type[] _sliceToolTypes =
    [
        typeof(ServerTools),
        typeof(WorkflowRecommendationTools),
        typeof(SuggestionTools),
        typeof(PromptShimTools),
    ];

    private static readonly ToolDescriptionBudgetHarness.TriggerExpectation[] _triggerExpectations =
    [
        new("server_heartbeat", "Do not poll waiting for `idle` to self-advance"),
        new("server_info", "workspace_list is authoritative"),
        new("server_heartbeat", "cheaper than server_info"),
        new("suggest_refactorings", "LCOM4"),
        new("get_prompt_text", "prompts/get"),
        new("recommend_workflow", "requiredWorkspaceState"),
    ];

    [TestMethod]
    public void SliceToolDescriptions_AreCapabilityStatements() =>
        ToolDescriptionBudgetHarness.AssertPerToolBudget(_sliceToolTypes, _maxDescriptionCharacters);

    [TestMethod]
    public void SliceToolDescriptions_StayUnderAggregateBudget() =>
        ToolDescriptionBudgetHarness.AssertSliceTotalBudget(
            _sliceToolTypes, _maxAggregateDescriptionCharacters);

    [TestMethod]
    public void SliceTools_AllHaveNonEmptyDescriptions() =>
        ToolDescriptionBudgetHarness.AssertAllHaveNonEmptyDescription(_sliceToolTypes);

    [TestMethod]
    public void TrimmedDescriptions_KeepTheirDiscriminatingTriggers() =>
        ToolDescriptionBudgetHarness.AssertDiscriminatingTriggers(_sliceToolTypes, _triggerExpectations);
}
