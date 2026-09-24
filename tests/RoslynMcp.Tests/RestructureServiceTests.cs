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
// donotparallelize-audit-wave-23: [DoNotParallelize] removed. The class reads the assembly-shared
// SampleSolution only through the synchronized WorkspaceIdCache (GetOrLoadWorkspaceIdAsync) and
// only calls RestructureService.PreviewRestructureAsync, which computes a candidate solution
// without TryApplyChanges — no *_apply, no reload, no disk writes, no UndoService/ChangeTracker
// writes. Its private RestructureService instance is class-local. Validated by a bounded repeated
// (3x) concurrent run alongside its wave-23 siblings and parallel-enabled workspace-loading
// classes, green every time.
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
        var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
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
        var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
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
        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            Service.PreviewRestructureAsync(
                WorkspaceId,
                pattern: "42",
                goal: "43",
                scope: new RestructureScope(null, null),
                ct: CancellationToken.None));

        StringAssert.Contains(ex.Message, "no matches found",
            "Orphaned-placeholder validation must not fire for a literal-only pattern/goal pair.");
    }

    [TestMethod]
    public async Task PreviewRestructure_RepeatedPlaceholder_RequiresConsistentTokenCapture()
    {
        var refactoringProbePath = Path.Combine(
            Path.GetDirectoryName(SampleSolutionPath)!,
            "SampleLib",
            "RefactoringProbe.cs");

        var preview = await Service.PreviewRestructureAsync(
            WorkspaceId,
            pattern: "__name__ = __name__ * 2;",
            goal: "__name__ *= 2;",
            scope: new RestructureScope(refactoringProbePath, null),
            ct: CancellationToken.None);

        Assert.AreEqual(1, preview.Changes.Count,
            "The repeated-placeholder pattern should match exactly the `result = result * 2;` statement.");
        StringAssert.Contains(preview.Description, "1 match(es)");
        StringAssert.Contains(preview.Changes[0].UnifiedDiff, "result *= 2;");
        StringAssert.Contains(preview.Changes[0].UnifiedDiff, "-        result = result * 2;");
    }

    // restructure-preview-splice-without-parenthesization: a captured expression spliced into a
    // higher-precedence goal slot must be parenthesized, or the rewrite silently changes meaning.
    [TestMethod]
    public async Task PreviewRestructure_BinaryCaptureInHigherPrecedenceSlot_IsParenthesized()
    {
        // `a + b + 1` parses as `(a + b) + 1`, so __a__ binds the binary `a + b`.
        // Pre-fix the splice produced `a + b * 3` (compiles, wrong semantics).
        var text = await RestructureAdhocAsync(
            "class C { int M(int a, int b) { return a + b + 1; } }",
            pattern: "__a__ + 1",
            goal: "__a__ * 3");

        StringAssert.Contains(text, "return (a + b) * 3;");
    }

    [TestMethod]
    public async Task PreviewRestructure_ConditionalCaptureInOperandSlot_IsParenthesized()
    {
        var text = await RestructureAdhocAsync(
            "class C { static int Foo(int v) => v; int M(int a, int b, bool flag) { return Foo(flag ? a : b); } }",
            pattern: "Foo(__x__)",
            goal: "__x__ + 1");

        StringAssert.Contains(text, "return (flag ? a : b) + 1;");
    }

    [TestMethod]
    public async Task PreviewRestructure_CaptureInDelimitedSlotOrPrimaryCapture_AddsNoParentheses()
    {
        // An argument slot accepts any expression, and an identifier capture binds tighter than
        // any operator, so neither splice may gain redundant parentheses.
        var argumentText = await RestructureAdhocAsync(
            "class C { static int Foo(int v) => v; static int Bar(int v) => v; int M(int a, int b) { return Foo(a + b); } }",
            pattern: "Foo(__x__)",
            goal: "Bar(__x__)");
        StringAssert.Contains(argumentText, "return Bar(a + b);");

        var primaryText = await RestructureAdhocAsync(
            "class C { int M(int a) { return a + 1; } }",
            pattern: "__a__ + 1",
            goal: "__a__ * 3");
        StringAssert.Contains(primaryText, "return a * 3;");
    }

    [TestMethod]
    public async Task PreviewRestructure_GoalSplicedIntoReceiverSlot_IsParenthesized()
    {
        // The whole substituted goal replaces a node sitting in a member-access receiver slot;
        // a lower-precedence goal must be wrapped there too (`a + 1.ToString()` would bind wrong).
        var text = await RestructureAdhocAsync(
            "class C { static int Foo(int v) => v; string M(int a) { return Foo(a).ToString(); } }",
            pattern: "Foo(__x__)",
            goal: "__x__ + 1");

        StringAssert.Contains(text, "return (a + 1).ToString();");
    }

    private static async Task<string> RestructureAdhocAsync(string source, string pattern, string goal)
    {
        using var workspace = new Microsoft.CodeAnalysis.AdhocWorkspace();
        var projectId = Microsoft.CodeAnalysis.ProjectId.CreateNewId();
        var filePath = Path.Combine(Path.GetTempPath(), "RestructurePrecedenceSample.cs");
        var solution = workspace.CurrentSolution
            .AddProject(projectId, "RestructurePrecedence", "RestructurePrecedence", Microsoft.CodeAnalysis.LanguageNames.CSharp)
            .AddDocument(
                Microsoft.CodeAnalysis.DocumentId.CreateNewId(projectId),
                "RestructurePrecedenceSample.cs",
                Microsoft.CodeAnalysis.Text.SourceText.From(source),
                filePath: filePath);

        var (newSolution, changes, _) = await Service.PreviewRestructureOnSolutionAsync(
            solution, pattern, goal, new RestructureScope(null, null), CancellationToken.None);

        Assert.AreEqual(1, changes.Count, "Exactly the one adhoc document should change.");
        var document = newSolution.Projects.Single().Documents.Single();
        return (await document.GetTextAsync(CancellationToken.None)).ToString();
    }
}
