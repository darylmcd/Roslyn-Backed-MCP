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

    private static bool IsGlobal(UsingDirectiveSyntax u) => u.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword);

    // CS8915: a global using must precede every non-global using in a file.
    private static void AssertGlobalsPrecedeOrdinary(SyntaxList<UsingDirectiveSyntax> result)
    {
        var firstOrdinary = result.ToList().FindIndex(u => !IsGlobal(u));
        if (firstOrdinary < 0)
        {
            return;
        }

        for (var i = firstOrdinary; i < result.Count; i++)
        {
            Assert.IsFalse(IsGlobal(result[i]), $"global using at index {i} follows an ordinary using (CS8915): {result[i]}");
        }
    }

    [TestMethod]
    public void BuildUsingDirectives_PlacesGlobalUsingsBeforeOrdinary()
    {
        var sourceUsings = ParseUsings(
            "global using System.Globalization;\nusing Zeta.Lib;\nusing System.Text;\n");
        var required = new[] { "System.Text", "Zeta.Lib" };

        var result = UsingDirectiveSynthesizer.BuildUsingDirectives(sourceUsings, required);

        AssertGlobalsPrecedeOrdinary(result);
        CollectionAssert.AreEqual(
            new[] { "System.Globalization", "System.Text", "Zeta.Lib" },
            result.Select(u => u.Name!.ToString()).ToArray());
        Assert.IsTrue(IsGlobal(result[0]));
    }

    [TestMethod]
    public void BuildUsingDirectives_GlobalAliasAndStatic_PreservedFirstInSourceOrder_LocalSpecialsLast()
    {
        var sourceUsings = ParseUsings(
            "using static System.Console;\nglobal using GA = Some.Thing;\nusing LA = Other.Thing;\n"
            + "global using static System.Math;\nusing Zeta.Lib;\n");
        var required = new[] { "Zeta.Lib" };

        var result = UsingDirectiveSynthesizer.BuildUsingDirectives(sourceUsings, required);

        AssertGlobalsPrecedeOrdinary(result);
        Assert.AreEqual(5, result.Count);
        // Globals first, in source order, with alias/static semantics intact.
        Assert.IsTrue(IsGlobal(result[0]));
        Assert.AreEqual("GA", result[0].Alias!.Name.Identifier.Text);
        Assert.IsTrue(IsGlobal(result[1]));
        Assert.IsTrue(result[1].StaticKeyword.IsKind(SyntaxKind.StaticKeyword));
        Assert.AreEqual("System.Math", result[1].Name!.ToString());
        // Then plain usings, then non-global alias/static in source order.
        Assert.AreEqual("Zeta.Lib", result[2].Name!.ToString());
        Assert.IsTrue(result[3].StaticKeyword.IsKind(SyntaxKind.StaticKeyword));
        Assert.IsFalse(IsGlobal(result[3]));
        Assert.AreEqual("LA", result[4].Alias!.Name.Identifier.Text);
        Assert.IsFalse(IsGlobal(result[4]));
    }

    [TestMethod]
    public void BuildUsingDirectives_GlobalOnlySource_SynthesizedPlainFollowsGlobal()
    {
        var sourceUsings = ParseUsings("global using System.Globalization;\n");
        var required = new[] { "System.Linq" };

        var result = UsingDirectiveSynthesizer.BuildUsingDirectives(sourceUsings, required);

        AssertGlobalsPrecedeOrdinary(result);
        Assert.AreEqual(2, result.Count);
        Assert.IsTrue(IsGlobal(result[0]));
        Assert.AreEqual("System.Linq", result[1].Name!.ToString());
        Assert.IsFalse(IsGlobal(result[1]));
    }

    [TestMethod]
    public void BuildUsingDirectives_NoGlobalSource_OrderUnchanged()
    {
        var sourceUsings = ParseUsings("using Zeta.Lib;\nusing static System.Math;\nusing System.Text;\n");
        var required = new[] { "System.Text", "Zeta.Lib" };

        var result = UsingDirectiveSynthesizer.BuildUsingDirectives(sourceUsings, required);

        CollectionAssert.AreEqual(
            new[] { "System.Text", "Zeta.Lib", "System.Math" },
            result.Select(u => u.Name!.ToString()).ToArray());
    }
}
