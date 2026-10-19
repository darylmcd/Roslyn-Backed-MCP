using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;
using SuggestionService = RoslynMcp.Roslyn.Services.RefactoringSuggestionService;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class RefactoringSuggestionTests : IsolatedWorkspaceTestBase
{
    private static IsolatedWorkspaceScope? SuggestionWorkspace { get; set; }

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        SuggestionWorkspace = await CreateSuggestionWorkspaceAsync();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        SuggestionWorkspace?.Dispose();
        SuggestionWorkspace = null;
    }

    [TestMethod]
    public async Task SuggestRefactorings_ReturnsResults()
    {
        var suggestions = await RefactoringSuggestionService.SuggestRefactoringsAsync(
            GetSuggestionWorkspaceId(), projectFilter: "SampleLib", limit: 50, CancellationToken.None);

        Assert.IsTrue(suggestions.Any(s =>
                s.Category == "complexity" && s.TargetSymbol == "Evaluate"),
            $"The fixture's deliberately complex Evaluate method must produce a complexity suggestion. " +
            $"Got: [{string.Join(", ", suggestions.Select(s => $"{s.Category}:{s.TargetSymbol}"))}]");
        Assert.IsTrue(suggestions.Any(s =>
                s.Category == "parameter-count" && s.TargetSymbol == "Evaluate"),
            "The fixture's seven-parameter Evaluate method must produce a parameter-count suggestion.");
        foreach (var s in suggestions)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(s.Category));
            Assert.IsFalse(string.IsNullOrWhiteSpace(s.Severity));
            Assert.IsFalse(string.IsNullOrWhiteSpace(s.TargetSymbol));
            Assert.IsTrue(s.RecommendedTools.Count > 0);
        }
    }

    [TestMethod]
    public async Task SevenParameters_RecommendRegisteredParameterObjectWorkflow()
    {
        var suggestions = await RefactoringSuggestionService.SuggestRefactoringsAsync(
            GetSuggestionWorkspaceId(), projectFilter: "SampleLib", limit: 50, CancellationToken.None);

        var suggestion = suggestions.Single(item => item.Category == "parameter-count" && item.TargetSymbol == "Evaluate");
        CollectionAssert.AreEqual(new[] { "parameter_object_preview", "apply_with_verify", "compile_check" },
            suggestion.RecommendedTools.ToArray());
    }

    [TestMethod]
    public async Task SimpleSevenParameterMethod_IsSuggestedAfterMoreThanOneHundredOtherMethods()
    {
        var suggestions = await RefactoringSuggestionService.SuggestRefactoringsAsync(
            GetSuggestionWorkspaceId(), projectFilter: "SampleLib", limit: 1000, CancellationToken.None);

        var suggestion = suggestions.Single(item => item.Category == "parameter-count" && item.TargetSymbol == "SumSeven");
        CollectionAssert.AreEqual(new[] { "parameter_object_preview", "apply_with_verify", "compile_check" },
            suggestion.RecommendedTools.ToArray());
        Assert.IsFalse(suggestions.Any(item => item.Category == "complexity" && item.TargetSymbol == "SumSeven"));
        Assert.IsFalse(suggestions.Any(item => item.Category == "parameter-count" && item.TargetSymbol == ".ctor"
            && item.RecommendedTools.Contains("parameter_object_preview")),
            "parameter_object_preview explicitly refuses constructors.");
    }

    [TestMethod]
    [DataRow(".ctor")]
    [DataRow("VirtualTarget")]
    [DataRow("AbstractTarget")]
    [DataRow("OverrideTarget")]
    [DataRow("InterfaceTarget")]
    [DataRow("ExplicitTarget")]
    [DataRow("NativeTarget")]
    [DataRow("PairedTarget")]
    public async Task UnsupportedParameterObjectTarget_RecommendsManualWorkWithoutRefusedTools(string methodName)
    {
        var suggestions = await RefactoringSuggestionService.SuggestRefactoringsAsync(
            GetSuggestionWorkspaceId(), projectFilter: "SampleLib", limit: 1000, CancellationToken.None);
        var matches = suggestions.Where(item => item.Category == "parameter-count"
            && item.TargetSymbol.EndsWith(methodName, StringComparison.Ordinal)).ToList();

        Assert.IsNotEmpty(matches);
        foreach (var suggestion in matches)
        {
            StringAssert.Contains(suggestion.Description, "manually");
            CollectionAssert.AreEqual(new[] { "compile_check" }, suggestion.RecommendedTools.ToArray());
        }
        if (methodName == "PairedTarget")
        {
            Assert.HasCount(1, matches, "A partial definition and implementation are one method.");
            var metrics = await CodeMetricsService.GetComplexityMetricsAsync(
                GetSuggestionWorkspaceId(), filePath: null, filePaths: null, projectFilter: "SampleLib",
                minComplexity: null, limit: int.MaxValue, CancellationToken.None);
            Assert.AreEqual(metrics.Where(item => item.SymbolName == "PairedTarget").Max(item => item.Line),
                matches[0].Line, "Navigate to the implementation rather than its bodyless definition.");
        }
    }

    [TestMethod]
    [DataRow("AllByRef", false)]
    [DataRow("OneEligible", false)]
    [DataRow("ParamsOneEligible", false)]
    [DataRow("ReceiverOnly", false)]
    [DataRow("TwoEligible", true)]
    [DataRow("ParamsTwoEligible", true)]
    public async Task ParameterObjectSuggestions_RequireTwoGroupableParameters(string methodName, bool supported)
    {
        var suggestions = await RefactoringSuggestionService.SuggestRefactoringsAsync(
            GetSuggestionWorkspaceId(), projectFilter: "SampleLib", limit: 1000, CancellationToken.None);
        var suggestion = suggestions.Single(item => item.Category == "parameter-count" && item.TargetSymbol == methodName);

        CollectionAssert.AreEqual(supported
            ? new[] { "parameter_object_preview", "apply_with_verify", "compile_check" }
            : new[] { "compile_check" }, suggestion.RecommendedTools.ToArray());
    }

    [TestMethod]
    [DataRow("GenericOnly", false)]
    [DataRow("InaccessibleTypes", false)]
    [DataRow("AssignedOnly", false)]
    [DataRow("SensitiveMetadata", false)]
    [DataRow("DefaultOnly", false)]
    [DataRow("DelegateOnly", false)]
    [DataRow("ReservedOnly", false)]
    [DataRow("MixedGeneric", true)]
    [DataRow("MixedAssigned", true)]
    public async Task ParameterObjectSuggestions_RequireViableReadOnlyPreflight(string methodName, bool supported)
    {
        var workspaceId = GetSuggestionWorkspaceId();
        var version = WorkspaceManager.GetCurrentVersion(workspaceId);
        var solution = WorkspaceManager.GetCurrentSolution(workspaceId);
        var suggestions = await RefactoringSuggestionService.SuggestRefactoringsAsync(
            workspaceId, projectFilter: "SampleLib", limit: 1000, CancellationToken.None);
        var suggestion = suggestions.Single(item => item.Category == "parameter-count" && item.TargetSymbol == methodName);

        CollectionAssert.AreEqual(supported
            ? new[] { "parameter_object_preview", "apply_with_verify", "compile_check" }
            : new[] { "compile_check" }, suggestion.RecommendedTools.ToArray());
        Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(workspaceId));
        Assert.AreSame(solution, WorkspaceManager.GetCurrentSolution(workspaceId));
    }

    [TestMethod]
    public async Task ParameterObjectFeasibility_CancellationAndUnexpectedFailuresPropagate()
    {
        var solution = WorkspaceManager.GetCurrentSolution(GetSuggestionWorkspaceId());
        var project = solution.Projects.Single(item => item.Name == "SampleLib");
        var compilation = await project.GetCompilationAsync(CancellationToken.None);
        Assert.IsNotNull(compilation);
        var type = compilation.GetTypeByMetadataName("SampleLib.ParameterFeasibility");
        Assert.IsNotNull(type);
        var method = type.GetMembers("MixedGeneric").OfType<IMethodSymbol>().Single();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            global::RoslynMcp.Roslyn.Services.ParameterObjectService.HasViableParameterGroupAsync(
                solution, method, cancellation.Token));
        using var unrelated = new AdhocWorkspace();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            global::RoslynMcp.Roslyn.Services.ParameterObjectService.HasViableParameterGroupAsync(
                unrelated.CurrentSolution, method, CancellationToken.None));
    }

    [TestMethod]
    public void ParameterObjectRefusalOwnership_PreservesExceptionContractsWithoutClaimingUnexpectedErrors()
    {
        var argument = ParameterObjectRefusals.Argument("authored refusal", "grouped");
        Assert.IsInstanceOfType<PublicArgumentException>(argument);
        Assert.AreEqual("grouped", argument.ParamName);
        Assert.AreEqual(new PublicArgumentException("authored refusal", "grouped").Message, argument.Message);
        Assert.AreEqual(0, argument.Data.Count);
        Assert.IsTrue(ParameterObjectRefusals.IsExpected(argument));
        var operation = ParameterObjectRefusals.InvalidOperation("authored refusal");
        Assert.AreEqual(typeof(InvalidOperationException), operation.GetType());
        Assert.AreEqual("authored refusal", operation.Message);
        Assert.IsTrue(ParameterObjectRefusals.IsExpected(operation));
        Assert.IsFalse(ParameterObjectRefusals.IsExpected(new InvalidOperationException("authored refusal")));
        Assert.IsFalse(ParameterObjectRefusals.IsExpected(new OperationCanceledException()));
    }

    [TestMethod]
    public async Task SameLineOverloads_UseManualGuidanceWhenMeasuredTargetIsAmbiguous()
    {
        var suggestions = await RefactoringSuggestionService.SuggestRefactoringsAsync(
            GetSuggestionWorkspaceId(), projectFilter: "SampleLib", limit: 1000, CancellationToken.None);
        var matches = suggestions.Where(item => item.Category == "parameter-count" && item.TargetSymbol == "SameLine").ToList();

        Assert.HasCount(2, matches);
        Assert.IsTrue(matches.All(item => item.RecommendedTools.SequenceEqual(new[] { "compile_check" })));
        var distinct = suggestions.Where(item => item.Category == "parameter-count" && item.TargetSymbol == "Distinct").ToList();
        Assert.HasCount(2, distinct, "Distinct overloads are separate method identities.");
        Assert.IsTrue(distinct.All(item => item.RecommendedTools.Contains("parameter_object_preview")));
    }

    [TestMethod]
    public async Task UnresolvedMeasuredMethod_DoesNotBorrowNeighboringMethodEligibility()
    {
        var workspaceId = GetSuggestionWorkspaceId();
        var metrics = await CodeMetricsService.GetComplexityMetricsAsync(
            workspaceId, filePath: null, filePaths: null, projectFilter: "SampleLib", minComplexity: null,
            limit: int.MaxValue, CancellationToken.None);
        var metric = metrics.Single(item => item.SymbolName == "Evaluate") with { SymbolName = "MissingMeasuredName" };
        var service = new SuggestionService(WorkspaceManager, new FixedMetricsService(metric),
            CohesionAnalysisService, UnusedCodeAnalyzer, NullLogger<SuggestionService>.Instance);
        var suggestions = await service.SuggestRefactoringsAsync(workspaceId, "SampleLib", 1000, CancellationToken.None);

        var suggestion = suggestions.Single(item => item.Category == "parameter-count");
        StringAssert.Contains(suggestion.Description, "manually");
        CollectionAssert.AreEqual(new[] { "compile_check" }, suggestion.RecommendedTools.ToArray());
    }

    [TestMethod]
    public async Task SuggestRefactorings_RespectsLimit()
    {
        var allSuggestions = await RefactoringSuggestionService.SuggestRefactoringsAsync(
            GetSuggestionWorkspaceId(), projectFilter: "SampleLib", limit: 50, CancellationToken.None);
        var limitedSuggestions = await RefactoringSuggestionService.SuggestRefactoringsAsync(
            GetSuggestionWorkspaceId(), projectFilter: "SampleLib", limit: 1, CancellationToken.None);

        Assert.IsTrue(allSuggestions.Count > 1,
            "The positive fixture must produce more suggestions than the requested limit.");
        Assert.AreEqual(1, limitedSuggestions.Count,
            "A limit of one must truncate a non-empty multi-suggestion result to exactly one item.");
        Assert.AreEqual(allSuggestions[0].Category, limitedSuggestions[0].Category,
            "Limiting must preserve the highest-ranked suggestion category.");
        Assert.AreEqual(allSuggestions[0].TargetSymbol, limitedSuggestions[0].TargetSymbol,
            "Limiting must preserve the highest-ranked suggestion target.");
        Assert.AreEqual(allSuggestions[0].Description, limitedSuggestions[0].Description,
            "Limiting must preserve the highest-ranked suggestion details.");
    }

    [TestMethod]
    public async Task SuggestRefactorings_FacadeType_DoesNotEmitCohesionSplitSuggestion()
    {
        // suggest-refactorings-facade-extraction-false-positive: A zero-field facade implementing
        // an interface with all-delegating public methods previously surfaced as a top-severity
        // "Split <type>" cohesion suggestion because the cohesion loop only filtered on
        // Lcom4Score + FilePath. After the fix, the loop additionally skips any type whose
        // LifecyclePattern is non-null (e.g., "action-triad" or "facade"), so the aggregator
        // never emits a Category="cohesion" suggestion for a detected lifecycle pattern.
        await using var workspace = CreateIsolatedWorkspaceCopy();

        var filePath = workspace.GetPath("SampleLib", "SuggestRefactoringsFacadeSubject.cs");
        await File.WriteAllTextAsync(filePath, """
namespace SampleLib;

public interface IFacadeSurface
{
    string ReadOne();
    string ReadTwo();
    int ReadThree();
}

public class SuggestRefactoringsFacadeSubject : IFacadeSurface
{
    public string ReadOne() => "one";

    public string ReadTwo() => "two";

    public int ReadThree() => 3;
}
""", CancellationToken.None);

        await workspace.LoadAsync(CancellationToken.None);

        var suggestions = await RefactoringSuggestionService.SuggestRefactoringsAsync(
            workspace.WorkspaceId, projectFilter: null, limit: 100, CancellationToken.None);

        var facadeCohesionSuggestion = suggestions.FirstOrDefault(s =>
            s.Category == "cohesion" && s.TargetSymbol == "SuggestRefactoringsFacadeSubject");
        Assert.IsNull(facadeCohesionSuggestion,
            "Facade/adapter types must not produce a cohesion 'Split' suggestion — the lifecycle pattern softens the recommendation upstream and the aggregator must respect it.");
    }

    private static async Task<IsolatedWorkspaceScope> CreateSuggestionWorkspaceAsync()
    {
        var workspace = CreateIsolatedWorkspaceCopy();
        return await InitializeWithCleanupAsync(
            workspace,
            static async (scope, ct) =>
            {
                var filePath = scope.GetPath("SampleLib", "RefactoringSuggestionSubject.cs");
                await File.WriteAllTextAsync(filePath, """
namespace SampleLib;

public sealed class RefactoringSuggestionSubject
{
    public int Evaluate(int a, int b, int c, int d, int e, int f, int g)
    {
        var score = 0;
        if (a > 0) score++;
        if (b > 0) score++;
        if (c > 0) score++;
        if (d > 0) score++;
        if (e > 0) score++;
        if (f > 0) score++;
        if (g > 0) score++;
        if (a > b) score++;
        if (b > c) score++;
        if (c > d) score++;
        if (d > e) score++;
        if (e > f) score++;
        if (f > g) score++;
        if (a + b > c) score++;
        if (d + e > f) score++;
        return score;
    }
}
""", ct).ConfigureAwait(false);
                var simpleMethods = string.Join(Environment.NewLine,
                    Enumerable.Range(0, 110).Select(index => $"    public int Read{index}() => {index};"));
                await File.WriteAllTextAsync(scope.GetPath("SampleLib", "SimpleParameterSubject.cs"),
                    "namespace SampleLib;\npublic sealed class SimpleParameterSubject\n{\n" + simpleMethods +
                    "\n    public SimpleParameterSubject(int a, int b, int c, int d, int e, int f, int g) { }" +
                    "\n    public int SumSeven(int a, int b, int c, int d, int e, int f, int g) => a + b + c + d + e + f + g;\n}\n",
                    ct).ConfigureAwait(false);
                await File.WriteAllTextAsync(scope.GetPath("SampleLib", "ParameterEligibilitySubject.cs"), """
namespace SampleLib;

public interface IParameterEligibility
{
    int InterfaceTarget(int a, int b, int c, int d, int e, int f, int g);
    int ExplicitTarget(int a, int b, int c, int d, int e, int f, int g);
}

public abstract class ParameterDispatchBase
{
    public virtual int VirtualTarget(int a, int b, int c, int d, int e, int f, int g) => a;
    public abstract int AbstractTarget(int a, int b, int c, int d, int e, int f, int g);
    public virtual int OverrideTarget(int a, int b, int c, int d, int e, int f, int g) => a;
}

public sealed partial class ParameterDispatchSubject : ParameterDispatchBase, IParameterEligibility
{
    public override int AbstractTarget(int a, int b, int c, int d, int e, int f, int g) => a;
    public override int OverrideTarget(int a, int b, int c, int d, int e, int f, int g) => a;
    public int InterfaceTarget(int a, int b, int c, int d, int e, int f, int g) => a;
    int IParameterEligibility.ExplicitTarget(int a, int b, int c, int d, int e, int f, int g) => a;
    [System.Runtime.InteropServices.DllImport("uninvoked-test-fixture")]
    public static extern int NativeTarget(int a, int b, int c, int d, int e, int f, int g);
    public partial int PairedTarget(int a, int b, int c, int d, int e, int f, int g);
    public partial int PairedTarget(int a, int b, int c, int d, int e, int f, int g) => a;
}

public sealed class AmbiguousParameters
{
    public int SameLine(int a, int b, int c, int d, int e, int f, int g) => a; public string SameLine(string a, string b, string c, string d, string e, string f, string g) => a;
    public int Distinct(int a, int b, int c, int d, int e, int f, int g) => a;
    public string Distinct(string a, string b, string c, string d, string e, string f, string g) => a;
}

public sealed class ParameterShapes
{
    public int AllByRef(ref int a, ref int b, ref int c, ref int d, ref int e, ref int f, ref int g) => a;
    public int OneEligible(ref int a, ref int b, ref int c, ref int d, ref int e, ref int f, int g) => g;
    public int TwoEligible(ref int a, ref int b, ref int c, ref int d, ref int e, int f, int g) => f + g;
    public int ParamsOneEligible(ref int a, ref int b, ref int c, ref int d, ref int e, int f, params int[] g) => f;
    public int ParamsTwoEligible(ref int a, ref int b, ref int c, ref int d, int e, int f, params int[] g) => e + f;
}

public static class ParameterShapeExtensions
{
    public static string ReceiverOnly(this string value, ref int a, ref int b, ref int c, ref int d, ref int e, ref int f) => value;
}

public sealed class ParameterFeasibility
{
    private sealed class Hidden { }
    private int InaccessibleTypes(Hidden a, Hidden b, Hidden c, Hidden d, Hidden e, Hidden f, Hidden g) => 1;
    public int GenericOnly<T>(T a, T b, T c, T d, T e, T f, T g) => 1;
    public int MixedGeneric<T>(T a, T b, T c, T d, T e, int f, int g) => f + g;
    public int AssignedOnly(int a, int b, int c, int d, int e, int f, int g)
    {
        a++; b++; c++; d++; e++; f++; g++;
        return a + b + c + d + e + f + g;
    }
    public int MixedAssigned(int a, int b, int c, int d, int e, int f, int g)
    {
        a++; b++; c++; d++; e++;
        return a + b + c + d + e + f + g;
    }
    public string SensitiveMetadata(
        [System.Runtime.CompilerServices.CallerArgumentExpression("b")] string a = "",
        [System.Runtime.CompilerServices.CallerArgumentExpression("c")] string b = "",
        [System.Runtime.CompilerServices.CallerArgumentExpression("d")] string c = "",
        [System.Runtime.CompilerServices.CallerArgumentExpression("e")] string d = "",
        [System.Runtime.CompilerServices.CallerArgumentExpression("f")] string e = "",
        [System.Runtime.CompilerServices.CallerArgumentExpression("g")] string f = "",
        [System.Runtime.CompilerServices.CallerArgumentExpression("a")] string g = "") => a;
    public int DefaultOnly(int a = 0, int b = 0, int c = 0, int d = 0, int e = 0, int f = 0, int g = 0) => a;
    public int CallDefaults() => DefaultOnly();
    public static int DelegateOnly(int a, int b, int c, int d, int e, int f, int g) => a;
    public System.Func<int, int, int, int, int, int, int, int> GetAlias() => DelegateOnly;
    public int ReservedOnly(int clone, int equals, int getHashCode, int toString, int getType, int memberwiseClone, int printMembers) => clone;
}
""", ct).ConfigureAwait(false);
                await scope.LoadAsync(ct).ConfigureAwait(false);
            },
            CancellationToken.None).ConfigureAwait(false);
    }

    private static string GetSuggestionWorkspaceId() =>
        SuggestionWorkspace?.WorkspaceId
        ?? throw new InvalidOperationException("The class-private suggestion workspace was not initialized.");

    private sealed class FixedMetricsService(ComplexityMetricsDto metric) : ICodeMetricsService
    {
        public Task<IReadOnlyList<ComplexityMetricsDto>> GetComplexityMetricsAsync(
            string workspaceId, string? filePath, IReadOnlyList<string>? filePaths, string? projectFilter,
            int? minComplexity, int limit, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ComplexityMetricsDto>>([metric]);
    }
}
