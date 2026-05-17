using RoslynMcp.Core.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression suite for gh #775: <c>find_unused_symbols</c> reported test-bridge accessor
/// methods (private members named <c>*ForTest</c>, <c>*ForTesting</c>, <c>*_ForTest</c>,
/// <c>*Internal</c>) as <c>confidence=high</c> false positives. The suffix guard lives
/// in <see cref="RoslynMcp.Roslyn.Services.UnusedCodeAnalyzer"/>'s
/// <c>IsConventionInvokedMember</c> and is gated by
/// <see cref="UnusedSymbolsAnalysisOptions.ExcludeConventionInvoked"/> — opt-out re-surfaces
/// the matches. The negative control <c>BuildSelectStatement</c> (no suffix) must still
/// surface under the default filter, proving the guard is exact-suffix and bounded.
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class UnusedSymbolsTestBridgeExclusionTests : SharedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        InitializeServices();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        DisposeServices();
    }

    [TestMethod]
    public async Task FindUnusedSymbols_Default_ExcludesForTestSuffix()
    {
        var workspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);

        var unusedSymbols = await UnusedCodeAnalyzer.FindUnusedSymbolsAsync(
            workspaceId,
            new UnusedSymbolsAnalysisOptions { ProjectFilter = "SampleLib", IncludePublic = false, Limit = 500 },
            CancellationToken.None);

        var unusedNames = unusedSymbols.Select(s => s.SymbolName).ToHashSet(StringComparer.Ordinal);

        Assert.IsFalse(unusedNames.Contains("BuildSelectCommandTextForTest"),
            "*ForTest suffix should be excluded under default excludeConventionInvoked — test-bridge accessors are invoked from InternalsVisibleTo test assemblies.");
    }

    [TestMethod]
    public async Task FindUnusedSymbols_Default_ExcludesForTestingSuffix()
    {
        var workspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);

        var unusedSymbols = await UnusedCodeAnalyzer.FindUnusedSymbolsAsync(
            workspaceId,
            new UnusedSymbolsAnalysisOptions { ProjectFilter = "SampleLib", IncludePublic = false, Limit = 500 },
            CancellationToken.None);

        var unusedNames = unusedSymbols.Select(s => s.SymbolName).ToHashSet(StringComparer.Ordinal);

        Assert.IsFalse(unusedNames.Contains("GetCacheForTesting"),
            "*ForTesting suffix should be excluded under default excludeConventionInvoked.");
    }

    [TestMethod]
    public async Task FindUnusedSymbols_Default_ExcludesInternalSuffix()
    {
        var workspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);

        var unusedSymbols = await UnusedCodeAnalyzer.FindUnusedSymbolsAsync(
            workspaceId,
            new UnusedSymbolsAnalysisOptions { ProjectFilter = "SampleLib", IncludePublic = false, Limit = 500 },
            CancellationToken.None);

        var unusedNames = unusedSymbols.Select(s => s.SymbolName).ToHashSet(StringComparer.Ordinal);

        Assert.IsFalse(unusedNames.Contains("ResetStateInternal"),
            "*Internal suffix should be excluded under default excludeConventionInvoked.");
    }

    [TestMethod]
    public async Task FindUnusedSymbols_Default_ExcludesUnderscoreForTestSuffix()
    {
        var workspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);

        var unusedSymbols = await UnusedCodeAnalyzer.FindUnusedSymbolsAsync(
            workspaceId,
            new UnusedSymbolsAnalysisOptions { ProjectFilter = "SampleLib", IncludePublic = false, Limit = 500 },
            CancellationToken.None);

        var unusedNames = unusedSymbols.Select(s => s.SymbolName).ToHashSet(StringComparer.Ordinal);

        Assert.IsFalse(unusedNames.Contains("BuildCache_ForTest"),
            "*_ForTest underscore-separator variant should be excluded under default excludeConventionInvoked.");
    }

    [TestMethod]
    public async Task FindUnusedSymbols_Default_StillReportsNonSuffixedPrivate()
    {
        // Negative control: a plain unused private with no suffix match must still be
        // reported even with the convention filter on. Confirms the suffix guard is
        // exact-suffix and does not over-exclude generic privates.
        var workspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);

        var unusedSymbols = await UnusedCodeAnalyzer.FindUnusedSymbolsAsync(
            workspaceId,
            new UnusedSymbolsAnalysisOptions { ProjectFilter = "SampleLib", IncludePublic = false, Limit = 500 },
            CancellationToken.None);

        var unusedNames = unusedSymbols.Select(s => s.SymbolName).ToHashSet(StringComparer.Ordinal);

        Assert.IsTrue(unusedNames.Contains("BuildSelectStatement"),
            "Plain private with no test-bridge suffix must still be reported — the guard must not over-exclude.");
    }

    [TestMethod]
    public async Task FindUnusedSymbols_ExcludeConventionInvokedFalse_ReturnsTestBridgeMethods()
    {
        // Opt-out regression: with the filter explicitly off, every suffix-matched
        // test-bridge member must reappear. Catches accidental hardening where the
        // suffix guard runs outside the ExcludeConventionInvoked gate.
        var workspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);

        var unusedSymbols = await UnusedCodeAnalyzer.FindUnusedSymbolsAsync(
            workspaceId,
            new UnusedSymbolsAnalysisOptions
            {
                ProjectFilter = "SampleLib",
                IncludePublic = false,
                Limit = 500,
                ExcludeConventionInvoked = false,
            },
            CancellationToken.None);

        var unusedNames = unusedSymbols.Select(s => s.SymbolName).ToHashSet(StringComparer.Ordinal);

        Assert.IsTrue(unusedNames.Contains("BuildSelectCommandTextForTest"),
            "Opt-out — *ForTest test-bridge member should surface with ExcludeConventionInvoked=false.");
        Assert.IsTrue(unusedNames.Contains("GetCacheForTesting"),
            "Opt-out — *ForTesting test-bridge member should surface with ExcludeConventionInvoked=false.");
        Assert.IsTrue(unusedNames.Contains("ResetStateInternal"),
            "Opt-out — *Internal test-bridge member should surface with ExcludeConventionInvoked=false.");
        Assert.IsTrue(unusedNames.Contains("BuildCache_ForTest"),
            "Opt-out — *_ForTest test-bridge member should surface with ExcludeConventionInvoked=false.");
    }
}
