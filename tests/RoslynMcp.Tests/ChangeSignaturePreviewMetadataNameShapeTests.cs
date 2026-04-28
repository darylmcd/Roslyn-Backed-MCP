using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression coverage for `change-signature-preview-metadataname-shape-error-actionability` (P3).
/// Pre-fix, calling `change_signature_preview` with a parenthesized metadata name like
/// <c>SampleLib.AnimalService.GetAllAnimals(string)</c> failed with the misleading
/// <c>"requires a method symbol; resolved null"</c> error. The cause was actually shape
/// mismatch (parens are not part of a metadata name) but the message named "method symbol
/// resolution" — pointing the agent at the wrong cure. Post-fix the service rejects the
/// parenthesized shape up-front with an actionable message that names the supported
/// alternatives (bare method name + position-disambiguator, or `symbolHandle` from
/// `symbol_search`).
/// </summary>
[TestClass]
public sealed class ChangeSignaturePreviewMetadataNameShapeTests : TestBase
{
    private static ChangeSignatureService _changeSignatureService = null!;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        InitializeServices();
        _changeSignatureService = new ChangeSignatureService(
            WorkspaceManager,
            PreviewStore,
            RefactoringService);
    }

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    /// <summary>
    /// Parenthesized metadata names — the shape an agent gets when copy/pasting a Roslyn
    /// <c>ToDisplayString()</c> output — must produce an actionable rejection that names
    /// the shape mismatch and points at the supported alternatives. Pre-fix the error read
    /// "requires a method symbol; resolved null", which incorrectly described the cause as
    /// resolution failure rather than as shape mismatch. The new message must contain
    /// "parenthesized" (so the agent learns the shape is wrong) and "symbolHandle" (the
    /// supported alternative).
    /// </summary>
    [TestMethod]
    public async Task ChangeSignaturePreview_MetadataNameWithParens_RejectsWithActionableError()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        var workspaceId = loadResult.WorkspaceId;

        try
        {
            // Parenthesized metadata name — exactly the shape an agent that pastes a
            // Roslyn `ToDisplayString()` output naturally produces. Pre-fix this throws
            // `InvalidOperationException("requires a method symbol; resolved null")` —
            // misleading. Post-fix it throws `ArgumentException` whose message names the
            // shape mismatch and the supported alternatives.
            var locator = SymbolLocator.ByMetadataName("SampleLib.AnimalService.GetAllAnimals(string)");
            var request = new ChangeSignatureRequest(
                Op: "remove",
                Name: null,
                ParameterType: null,
                Position: 0,
                NewName: null,
                DefaultValue: null);

            var ex = await Assert.ThrowsExceptionAsync<ArgumentException>(async () =>
                await _changeSignatureService.PreviewChangeSignatureAsync(
                    workspaceId, locator, request, CancellationToken.None));

            // Error must name the shape mismatch (the actual cause) so the agent knows what
            // to fix.
            StringAssert.Contains(ex.Message, "parenthesized",
                $"error must call out the parenthesized signature as the cause; got: {ex.Message}");

            // Error must point at the supported alternative.
            StringAssert.Contains(ex.Message, "symbolHandle",
                $"error must name `symbolHandle` as the alternative; got: {ex.Message}");

            // Error must echo back the offending input so the agent can correlate with
            // its own request payload.
            StringAssert.Contains(ex.Message, "SampleLib.AnimalService.GetAllAnimals(string)",
                $"error must echo the offending metadata name input; got: {ex.Message}");

            // Error must NOT use the misleading pre-fix wording.
            Assert.IsFalse(ex.Message.Contains("requires a method symbol", StringComparison.Ordinal),
                $"error must NOT use the pre-fix 'requires a method symbol' wording; got: {ex.Message}");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
        }
    }

    /// <summary>
    /// Negative-case complement: the supported bare-method-name shape must continue to
    /// resolve cleanly. This exercises the rejection branch's negation — if the new
    /// pre-check accidentally rejected non-parenthesized inputs the supported path would
    /// regress. SampleLib.AnimalService.GetAllAnimals is a real method on the sample
    /// solution; locating it by bare name and removing parameter 0 must succeed. (The
    /// method is parameterless in the sample, so `op=remove Position=0` itself raises
    /// a different error — but that error must NOT be the new shape-mismatch error.)
    /// </summary>
    [TestMethod]
    public async Task ChangeSignaturePreview_BareMetadataName_DoesNotHitShapeMismatchPath()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        var workspaceId = loadResult.WorkspaceId;

        try
        {
            // Bare fully-qualified method name — the supported shape. The resolver must
            // accept this and proceed past the new shape-rejection branch. The downstream
            // service may still surface a different error (e.g. method has no parameters,
            // or position out of range), but that error must NOT contain the new
            // shape-mismatch wording.
            var locator = SymbolLocator.ByMetadataName("SampleLib.AnimalService.GetAllAnimals");
            var request = new ChangeSignatureRequest(
                Op: "remove",
                Name: null,
                ParameterType: null,
                Position: 99, // genuinely out of range — exercises the post-resolution path
                NewName: null,
                DefaultValue: null);

            // The bare name resolves and the pipeline proceeds past the parenthesis check.
            // The downstream behavior may succeed or fail, but it must NOT raise the new
            // shape-mismatch error.
            try
            {
                await _changeSignatureService.PreviewChangeSignatureAsync(
                    workspaceId, locator, request, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // Whatever the downstream raises, it must NOT be the shape-mismatch error.
                Assert.IsFalse(ex.Message.Contains("parenthesized", StringComparison.Ordinal),
                    $"bare name must not be flagged as parenthesized; got: {ex.Message}");
            }
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
        }
    }
}
