using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// dr-9-1-regression-r17a-emits-literal-placeholder: regression coverage for
/// <see cref="RestructureService.PreviewRestructureAsync"/> placeholder substitution.
/// The service was shipping without any tests; these are the first, scoped to the R17A
/// defects surfaced in firewall-analyzer 2026-04-15 §9.1 (literal `__name__` emitted
/// in goal output).
/// </summary>
[TestClass]
public sealed class RestructureServiceTests : SharedWorkspaceTestBase
{
    private static string WorkspaceId { get; set; } = null!;
    private static RestructureService Service { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        WorkspaceId = await GetOrLoadWorkspaceIdAsync(SampleSolutionPath, CancellationToken.None);
        Service = new RestructureService(WorkspaceManager, PreviewStore);
    }

    [TestMethod]
    public async Task PreviewRestructure_GoalReferencesUnknownPlaceholder_FailsLoud()
    {
        // Pattern captures __items__; goal references a second placeholder __count__ that was
        // never captured. Pre-fix this produced output containing literal `__count__` text;
        // now the service rejects the preview before touching the solution.
        var ex = await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            Service.PreviewRestructureAsync(
                WorkspaceId,
                pattern: "return __items__;",
                goal: "return new { count = __count__, items = __items__ };",
                scope: new RestructureScope(null, null),
                ct: CancellationToken.None));

        StringAssert.Contains(ex.Message, "__count__",
            "Error must name the orphaned placeholder so the caller can fix the goal.");
        StringAssert.Contains(ex.Message, "not captured by the pattern",
            "Error must explain why the placeholder cannot be substituted.");
    }

    [TestMethod]
    public async Task PreviewRestructure_PatternHasNoPlaceholders_GoalWithPlaceholdersStillFailsFast()
    {
        // Canonical R17A shape: pattern has NO placeholders, goal has one. Pre-fix
        // `_placeholderNames.Count == 0` short-circuited Substitute and the goal's literal
        // `__x__` text was emitted verbatim. Now the mismatch is rejected at validation.
        var ex = await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            Service.PreviewRestructureAsync(
                WorkspaceId,
                pattern: "42",
                goal: "__x__",
                scope: new RestructureScope(null, null),
                ct: CancellationToken.None));

        StringAssert.Contains(ex.Message, "__x__",
            "Error must name the orphaned placeholder.");
    }

    [TestMethod]
    public async Task PreviewRestructure_PatternAndGoalBothLiteral_NoThrow()
    {
        // No placeholders on either side is a legitimate literal-rewrite shape. The new
        // validation must not break this case. The scope filter points at a file that does
        // not contain the pattern so we expect the "no matches" exception, NOT the orphaned
        // placeholder one — proves the validation path accepted the pattern/goal.
        var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            Service.PreviewRestructureAsync(
                WorkspaceId,
                pattern: "42",
                goal: "43",
                scope: new RestructureScope(null, null),
                ct: CancellationToken.None));

        StringAssert.Contains(ex.Message, "no matches found",
            "Orphaned-placeholder validation must not fire for a literal-only pattern/goal pair.");
    }
}
