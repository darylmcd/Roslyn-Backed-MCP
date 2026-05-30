using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public class UsingDirectiveSynthesizerTests
{
    // using-directive-synthesis-dedup: the synthesis cluster (BuildUsingDirectives +
    // PreserveSpecialAndRequiredSourceUsings + AddMissingRequiredUsingDirectives +
    // SortUsingDirectives + helpers) was previously duplicated verbatim in
    // InterfaceExtractionService and CrossProjectRefactoringService and drifted independently.
    // Both entry points now delegate to UsingDirectiveSynthesizer, so testing the helper once
    // covers both — the "both produce identical using lists for the same input" guarantee is
    // structural (shared code), and the existing InterfaceExtraction / CrossProjectRefactoring
    // integration tests still exercise the end-to-end emit paths.

    private static SyntaxList<UsingDirectiveSyntax> ParseUsings(string source)
        => SyntaxFactory.ParseCompilationUnit(source).Usings;

    [TestMethod]
    public void BuildUsingDirectives_DropsUnrequiredPlain_KeepsRequired_AddsMissing()
    {
        var sourceUsings = ParseUsings("using Foo.Bar;\nusing System.Text;\n");
        var required = new[] { "System.Text", "System.Collections.Generic" };

        var result = UsingDirectiveSynthesizer.BuildUsingDirectives(sourceUsings, required);
        var names = result.Select(u => u.Name!.ToString()).ToList();

        // Plain source using whose namespace is NOT required is dropped.
        CollectionAssert.DoesNotContain(names, "Foo.Bar");
        // Required namespace already present in source is kept.
        CollectionAssert.Contains(names, "System.Text");
        // Required namespace absent from source is synthesized.
        CollectionAssert.Contains(names, "System.Collections.Generic");
        Assert.AreEqual(2, names.Count);
    }

    [TestMethod]
    public void BuildUsingDirectives_PreservesSpecials_SortsSystemFirst()
    {
        var sourceUsings = ParseUsings(
            "using Zeta.Lib;\nusing System.Text;\nusing static System.Math;\nusing Alias = Some.Thing;\n");
        var required = new[] { "System.Text", "Zeta.Lib" };

        var result = UsingDirectiveSynthesizer.BuildUsingDirectives(sourceUsings, required);

        // Plain usings first — System.* alphabetically ahead of other plains.
        Assert.AreEqual("System.Text", result[0].Name!.ToString());
        Assert.AreEqual("Zeta.Lib", result[1].Name!.ToString());
        // Aliases / static / global usings cannot be re-synthesized from symbols, so they are
        // always preserved, ordered after the plain usings in their original order.
        Assert.IsTrue(
            result[2].StaticKeyword.IsKind(SyntaxKind.StaticKeyword),
            "static using should be preserved after the plain usings");
        Assert.IsNotNull(result[3].Alias, "alias using should be preserved");
        Assert.AreEqual(4, result.Count);
    }
}
