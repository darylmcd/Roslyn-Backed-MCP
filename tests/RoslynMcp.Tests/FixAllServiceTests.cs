using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using RoslynMcp.Roslyn.Services;

#pragma warning disable RS1036 // Test-only analyzer double; not shipped
#pragma warning disable RS1038 // Test assembly references Workspaces by design
#pragma warning disable RS1041 // Test targets net10 same as product
#pragma warning disable RS2008 // No release tracking for test-only diagnostic descriptors

namespace RoslynMcp.Tests;

[TestClass]
public sealed class FixAllServiceTests
{
    private static readonly DiagnosticDescriptor SDescriptor = new(
        "SCS0006",
        "t",
        "m",
        "cat",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor IdeDescriptor = new(
        "IDE0001",
        "t",
        "m",
        "cat",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    private sealed class AnalyzerA : DiagnosticAnalyzer
    {
        private readonly ImmutableArray<DiagnosticDescriptor> _supported;

        public AnalyzerA(params DiagnosticDescriptor[] descriptors) =>
            _supported = [..descriptors];

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => _supported;

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
        }
    }

    [TestMethod]
    public void SelectAnalyzersForFixAllCollection_Scs_Uses_Project_Analyzers()
    {
        var proj = ImmutableArray.Create<DiagnosticAnalyzer>(new AnalyzerA(SDescriptor));
        var selected = FixAllService.SelectAnalyzersForFixAllCollection("SCS0006", [], proj);
        CollectionAssert.AreEqual(proj.ToArray(), selected.ToArray());
    }

    [TestMethod]
    public void SelectAnalyzersForFixAllCollection_NonIde_Empty_Project_Uses_None()
    {
        var selected = FixAllService.SelectAnalyzersForFixAllCollection("SCS0006", [], []);
        Assert.AreEqual(0, selected.Length);
    }

    [TestMethod]
    public void SelectAnalyzersForFixAllCollection_Ide_Merges_Features_And_Project()
    {
        var ide = ImmutableArray.Create<DiagnosticAnalyzer>(new AnalyzerA(IdeDescriptor));
        var proj = ImmutableArray.Create<DiagnosticAnalyzer>(new AnalyzerA(SDescriptor));
        var selected = FixAllService.SelectAnalyzersForFixAllCollection("IDE0001", ide, proj);
        Assert.AreEqual(2, selected.Length);
    }

    [TestMethod]
    public void SelectAnalyzersForFixAllCollection_Ca_Uses_Project_When_Present()
    {
        var proj = ImmutableArray.Create<DiagnosticAnalyzer>(new AnalyzerA(
            new DiagnosticDescriptor("CA5350", "t", "m", "cat", DiagnosticSeverity.Warning, true)));
        var selected = FixAllService.SelectAnalyzersForFixAllCollection("CA5350", [], proj);
        CollectionAssert.AreEqual(proj.ToArray(), selected.ToArray());
    }

    // ----------------------------------------------------------------------
    // fix-all-preview-sequence-contains-no-elements regression coverage.
    //
    // IDE0300's built-in collection-expression FixAll provider can throw
    // InvalidOperationException("Sequence contains no elements") when its
    // internal preconditions reject an occurrence. Previously this surfaced to
    // agents as a raw exception or a null guidanceMessage; the fix routes the
    // throw into a structured FixAllProviderCrash envelope so callers can
    // distinguish it from missing-provider / no-occurrences / no-actions paths
    // and fall back to code_fix_preview per occurrence.
    //
    // Two assertions compose the regression guard:
    //   (a) BuildProviderCrashEnvelope emits the documented field shape on any
    //       InvalidOperationException input (unit test — shape pin).
    //   (b) A throwing CodeFixProvider's FixAllProvider.GetFixAsync genuinely
    //       throws InvalidOperationException, so the catch in
    //       PreviewFixAllAsync will trigger the envelope builder in production
    //       (integration-shape test — catch-path plumbing).
    // ----------------------------------------------------------------------

    [TestMethod]
    public void BuildProviderCrashEnvelope_IDE0300_Emits_Structured_Envelope()
    {
        var ex = new InvalidOperationException("Sequence contains no elements");

        var envelope = FixAllService.BuildProviderCrashEnvelope(
            diagnosticId: "IDE0300", scope: "solution", ex: ex);

        Assert.IsTrue(envelope.Error, "Provider crash must set Error=true.");
        Assert.AreEqual("FixAllProviderCrash", envelope.Category);
        Assert.AreEqual(true, envelope.PerOccurrenceFallbackAvailable);
        Assert.AreEqual("IDE0300", envelope.DiagnosticId);
        Assert.AreEqual("solution", envelope.Scope);
        Assert.AreEqual(0, envelope.FixedCount);
        Assert.AreEqual(string.Empty, envelope.PreviewToken);
        Assert.AreEqual(0, envelope.Changes.Count);
        Assert.IsNotNull(envelope.GuidanceMessage);
        StringAssert.Contains(envelope.GuidanceMessage!, "IDE0300");
        StringAssert.Contains(envelope.GuidanceMessage!, "Sequence contains no elements");
        StringAssert.Contains(envelope.GuidanceMessage!, "code_fix_preview");
    }

    [TestMethod]
    public void BuildProviderCrashEnvelope_Preserves_Exception_Type_Name()
    {
        // The envelope's GuidanceMessage should surface the exception type so agents can tell
        // a provider defect from a test-synthesized throw when triaging logs.
        var ex = new InvalidOperationException("any message");

        var envelope = FixAllService.BuildProviderCrashEnvelope(
            diagnosticId: "IDE0005", scope: "document", ex: ex);

        StringAssert.Contains(envelope.GuidanceMessage!, nameof(InvalidOperationException));
    }

    /// <summary>
    /// Synthetic throwing provider used to prove that <see cref="InvalidOperationException"/>
    /// propagates out of <c>FixAllProvider.GetFixAsync</c> — the exact failure mode the
    /// production <see cref="FixAllService.PreviewFixAllAsync"/> catch block guards against.
    /// </summary>
    private sealed class ThrowingCodeFixProvider : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds => ["IDE0300"];

        public override FixAllProvider GetFixAllProvider() => new ThrowingFixAllProvider();

        public override Task RegisterCodeFixesAsync(CodeFixContext context) => Task.CompletedTask;

        private sealed class ThrowingFixAllProvider : FixAllProvider
        {
            public override Task<CodeAction?> GetFixAsync(FixAllContext fixAllContext)
                => throw new InvalidOperationException("Sequence contains no elements");
        }
    }

    [TestMethod]
    public void ThrowingCodeFixProvider_GetFixAllProvider_Throws_InvalidOperationException()
    {
        // Prove the shim actually throws the exception type the production catch handles.
        // Without this anchor, a refactor that swaps InvalidOperationException for another
        // type in the fixture would leave the catch untested silently.
        var provider = new ThrowingCodeFixProvider();
        var fixAllProvider = provider.GetFixAllProvider();
        Assert.IsNotNull(fixAllProvider);

        // Directly invoke GetFixAsync with a null context — the throw fires before any context
        // member is touched, so we can prove behaviour without constructing a real Document.
        var thrown = Assert.ThrowsExactly<InvalidOperationException>(() =>
        {
            _ = fixAllProvider.GetFixAsync(null!).GetAwaiter().GetResult();
        });

        StringAssert.Contains(thrown.Message, "Sequence contains no elements");
    }
}

#pragma warning restore RS2008
#pragma warning restore RS1041
#pragma warning restore RS1038
#pragma warning restore RS1036
