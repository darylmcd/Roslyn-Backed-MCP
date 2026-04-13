using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
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
}

#pragma warning restore RS2008
#pragma warning restore RS1041
#pragma warning restore RS1038
#pragma warning restore RS1036
