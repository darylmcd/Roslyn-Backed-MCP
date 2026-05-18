using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression tests for <c>symbol-relationships-builtin-type-unbounded-enumeration</c> (gh #757):
/// when the caret lands on a builtin-type token like <c>void</c> and the caller passes
/// <c>preferDeclaringMember=false</c>, the legacy code path resolved the token to
/// <c>System.Void</c> and then called <c>FindReferencesAsync</c> on the special type — which
/// enumerated every reference to the type solution-wide (measured 57+ KB on an 11-project /
/// 759-document solution). The fix detects post-promotion builtin types and returns an empty
/// envelope with a <c>Hint</c> field instead of enumerating the unbounded reference set.
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class SymbolRelationshipsBuiltinTypeSuppressionTests : SharedWorkspaceTestBase
{
    private static string _workspaceId = string.Empty;
    private static string _animalServicePath = string.Empty;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        _workspaceId = await LoadSharedSampleWorkspaceAsync();
        var sampleSolutionRoot = Path.GetDirectoryName(SampleSolutionPath)!;
        _animalServicePath = Path.Combine(sampleSolutionRoot, "SampleLib", "AnimalService.cs");
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        DisposeServices();
    }

    /// <summary>
    /// Cursor on <c>void</c> token in <c>AnimalService.MakeThemSpeak</c> (line 16, col 12) with
    /// <c>preferDeclaringMember=false</c>: the resolved symbol is <c>System.Void</c>
    /// (<c>SpecialType.System_Void</c>). Pre-fix: <c>FindReferencesAsync(System.Void)</c> enumerates
    /// every void-returning method reference solution-wide. Post-fix: short-circuit returns an
    /// empty envelope with a non-null <c>Hint</c> explaining the suppression.
    /// </summary>
    [TestMethod]
    public async Task BuiltinVoidToken_PreferDeclaringMemberFalse_SuppressesReferencesAndReturnsHint()
    {
        var locator = SymbolLocator.BySource(_animalServicePath, line: 16, column: 12);

        var result = await SymbolRelationshipService.GetSymbolRelationshipsAsync(
            _workspaceId,
            locator,
            preferDeclaringMember: false,
            CancellationToken.None);

        Assert.IsNotNull(result, "Resolver should still produce an envelope, not return null.");
        Assert.AreEqual(0, result.References.Count,
            "References list MUST be suppressed when the cursor resolves to a builtin type with preferDeclaringMember=false — the pre-fix code returned 57+ KB of unbounded references.");
        Assert.AreEqual(0, result.Implementations.Count, "Implementations should also be empty in the suppression envelope.");
        Assert.AreEqual(0, result.BaseMembers.Count, "BaseMembers should also be empty in the suppression envelope.");
        Assert.AreEqual(0, result.Overrides.Count, "Overrides should also be empty in the suppression envelope.");
        Assert.AreEqual(0, result.Definitions.Count, "Definitions should also be empty — System.Void has no in-source location.");
        Assert.IsNotNull(result.Hint, "Hint MUST be populated so the caller can recover by setting preferDeclaringMember=true.");
        Assert.IsTrue(result.Hint!.Contains("builtin", StringComparison.OrdinalIgnoreCase),
            $"Hint should explain the builtin-type suppression. Got: {result.Hint}");
        Assert.IsTrue(result.Hint.Contains("preferDeclaringMember", StringComparison.Ordinal),
            $"Hint should point the caller at preferDeclaringMember=true as the recovery path. Got: {result.Hint}");
    }

    /// <summary>
    /// Regression guard: when <c>preferDeclaringMember=true</c> (the default), the cursor on the
    /// <c>void</c> token auto-promotes to the enclosing <c>MakeThemSpeak</c> method and the
    /// normal relationship enumeration runs. The suppression hint MUST NOT fire on the
    /// promoted path — the promoted symbol is a method (not a builtin type), so this verifies the
    /// guard's <c>!preferDeclaringMember</c> condition.
    /// </summary>
    [TestMethod]
    public async Task BuiltinVoidToken_PreferDeclaringMemberTrue_PromotesToMethodWithoutHint()
    {
        var locator = SymbolLocator.BySource(_animalServicePath, line: 16, column: 12);

        var result = await SymbolRelationshipService.GetSymbolRelationshipsAsync(
            _workspaceId,
            locator,
            preferDeclaringMember: true,
            CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual("MakeThemSpeak", result.Symbol.Name,
            "preferDeclaringMember=true should promote the void token to the enclosing method.");
        Assert.IsNull(result.Hint,
            "The suppression hint MUST NOT fire on the promoted-method path — the guard checks !preferDeclaringMember first.");
        Assert.IsTrue(result.Definitions.Count > 0,
            "The promoted method should have its declaration as a definition location.");
    }
}
