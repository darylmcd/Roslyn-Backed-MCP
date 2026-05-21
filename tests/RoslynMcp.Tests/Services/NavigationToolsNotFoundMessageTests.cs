using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests.Services;

/// <summary>
/// Regression coverage for `navigation-tools-misnamed-locator-error` (P4 — UX). Pre-fix the four
/// navigation/analysis tools (<c>callers_callees</c>, <c>impact_analysis</c>, <c>find_consumers</c>,
/// <c>symbol_relationships</c>) all threw the verbatim string
/// <c>"No symbol found at the specified location"</c> when the resolver returned null — even when
/// the caller had supplied <c>metadataName</c> or <c>symbolHandle</c> and never passed a location
/// at all. Post-fix every site routes through <see cref="SymbolLocatorFactory.FormatSymbolNotFoundMessage"/>
/// so the error names the locator field that was actually supplied. Mirrors the shape of
/// <see cref="SymbolInfoNotFoundMessageTests"/> — one test per (tool x locator-mode) cell that
/// exercises a not-found path and asserts the locator field appears in the message and the legacy
/// "at the specified location" wording is gone.
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class NavigationToolsNotFoundMessageTests : SharedWorkspaceTestBase
{
    private static string WorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        WorkspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);
    }

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    /// <summary>
    /// <c>callers_callees</c> with a <c>metadataName</c>-only locator that does not resolve.
    /// Pre-fix the message claimed the symbol was missing "at the specified location" — there was
    /// no location supplied. Post-fix the message must echo the metadata name.
    /// </summary>
    [TestMethod]
    public async Task CallersCallees_MetadataNameOnly_NotFound_NamesTheMetadataName()
    {
        const string bogusName = "SampleLib.DefinitelyDoesNotExist__Bogus";

        var ex = await Assert.ThrowsExceptionAsync<KeyNotFoundException>(async () =>
            await AnalysisTools.GetCallersCallees(
                WorkspaceExecutionGate,
                SymbolRelationshipService,
                WorkspaceId,
                metadataName: bogusName,
                ct: CancellationToken.None));

        StringAssert.Contains(ex.Message, "metadata name",
            $"Error must name the locator field that was supplied. Got: {ex.Message}");
        StringAssert.Contains(ex.Message, bogusName,
            $"Error must echo the offending metadata name so the caller can correlate. Got: {ex.Message}");
        Assert.IsFalse(
            ex.Message.Contains("at the specified location", StringComparison.Ordinal),
            $"Error must NOT use the legacy \"at the specified location\" wording. Got: {ex.Message}");
    }

    /// <summary>
    /// <c>impact_analysis</c> with a <c>metadataName</c>-only locator that does not resolve. Same
    /// motivator as the callers_callees case — agents resolved a fully-qualified name and got a
    /// message about a location they never supplied.
    /// </summary>
    [TestMethod]
    public async Task ImpactAnalysis_MetadataNameOnly_NotFound_NamesTheMetadataName()
    {
        const string bogusName = "SampleLib.DefinitelyDoesNotExist__Bogus";

        var ex = await Assert.ThrowsExceptionAsync<SymbolNotFoundException>(async () =>
            await AnalysisTools.AnalyzeImpact(
                WorkspaceExecutionGate,
                MutationAnalysisService,
                WorkspaceId,
                metadataName: bogusName,
                ct: CancellationToken.None));

        StringAssert.Contains(ex.Message, "metadata name",
            $"Error must name the locator field that was supplied. Got: {ex.Message}");
        StringAssert.Contains(ex.Message, bogusName,
            $"Error must echo the offending metadata name so the caller can correlate. Got: {ex.Message}");
        Assert.IsFalse(
            ex.Message.Contains("at the specified location", StringComparison.Ordinal),
            $"Error must NOT use the legacy \"at the specified location\" wording. Got: {ex.Message}");
    }

    /// <summary>
    /// <c>find_consumers</c> with a <c>metadataName</c>-only locator that does not resolve. Covers
    /// the consumer-analysis throw site previously sharing the misleading "at the specified
    /// location" wording. (A symbolHandle-only variant for the same tool is not exercised here:
    /// well-formed-but-missing handles are rejected upstream by the handle resolver with its own
    /// targeted error message before reaching the not-found branch — that path stays informative
    /// without going through <see cref="SymbolLocatorFactory.FormatSymbolNotFoundMessage"/>.)
    /// </summary>
    [TestMethod]
    public async Task FindConsumers_MetadataNameOnly_NotFound_NamesTheMetadataName()
    {
        const string bogusName = "SampleLib.DefinitelyDoesNotExist__Bogus";

        var ex = await Assert.ThrowsExceptionAsync<SymbolNotFoundException>(async () =>
            await ConsumerAnalysisTools.FindConsumers(
                WorkspaceExecutionGate,
                ConsumerAnalysisService,
                WorkspaceId,
                metadataName: bogusName,
                ct: CancellationToken.None));

        StringAssert.Contains(ex.Message, "metadata name",
            $"Error must name the locator field that was supplied. Got: {ex.Message}");
        StringAssert.Contains(ex.Message, bogusName,
            $"Error must echo the offending metadata name so the caller can correlate. Got: {ex.Message}");
        Assert.IsFalse(
            ex.Message.Contains("at the specified location", StringComparison.Ordinal),
            $"Error must NOT use the legacy \"at the specified location\" wording. Got: {ex.Message}");
    }

    /// <summary>
    /// <c>symbol_relationships</c> with a source-location-only locator pointing at a file that's
    /// not in the loaded workspace. Pre-fix the message was "No symbol found at the specified
    /// location" — directionally correct but vague. Post-fix the message must include the
    /// file:line:col literal so the caller can verify their input round-tripped correctly.
    /// </summary>
    [TestMethod]
    public async Task SymbolRelationships_SourceLocationOnly_NotFound_NamesTheFileLineCol()
    {
        var bogusFile = Path.Combine(Path.GetTempPath(), "definitely_not_in_workspace.cs");

        var ex = await Assert.ThrowsExceptionAsync<KeyNotFoundException>(async () =>
            await SymbolTools.GetSymbolRelationships(
                WorkspaceExecutionGate,
                SymbolRelationshipService,
                WorkspaceId,
                filePath: bogusFile,
                line: 1,
                column: 1,
                ct: CancellationToken.None));

        StringAssert.Contains(ex.Message, bogusFile,
            $"Error must echo the file path that was supplied. Got: {ex.Message}");
        StringAssert.Contains(ex.Message, "1:1",
            $"Error must echo the line:column position that was supplied. Got: {ex.Message}");
        Assert.IsFalse(
            ex.Message.Contains("the specified location", StringComparison.Ordinal),
            $"Error must NOT use the legacy \"the specified location\" wording. Got: {ex.Message}");
    }

}
