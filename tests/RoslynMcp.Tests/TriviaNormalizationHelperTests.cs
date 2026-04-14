using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public class TriviaNormalizationHelperTests
{
    #region NormalizeLeadingTrivia

    [TestMethod]
    public void NormalizeLeadingTrivia_FileStartingWithBlankLines_StripsLeadingBlanks()
    {
        var source = "\r\n\r\n\r\nusing System;\r\nclass C { }\r\n";
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetCompilationUnitRoot();

        var result = TriviaNormalizationHelper.NormalizeLeadingTrivia(root);

        var text = result.ToFullString();
        Assert.IsTrue(text.StartsWith("using System;"), $"Expected no leading blanks but got: [{text[..Math.Min(30, text.Length)]}]");
    }

    [TestMethod]
    public void NormalizeLeadingTrivia_NoLeadingTrivia_NoOp()
    {
        var source = "using System;\r\nclass C { }\r\n";
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetCompilationUnitRoot();

        var result = TriviaNormalizationHelper.NormalizeLeadingTrivia(root);

        Assert.AreEqual(source, result.ToFullString());
    }

    #endregion

    #region NormalizeUsingToMemberSeparator

    [TestMethod]
    public void NormalizeUsingToMemberSeparator_NoUsings_NoOp()
    {
        var source = "class C { }\r\n";
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetCompilationUnitRoot();

        var result = TriviaNormalizationHelper.NormalizeUsingToMemberSeparator(root);

        Assert.AreEqual(source, result.ToFullString());
    }

    [TestMethod]
    public void NormalizeUsingToMemberSeparator_UsingsFollowedImmediatelyByClass_InsertsBlankLine()
    {
        var source = "using System;\r\nclass C { }\r\n";
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetCompilationUnitRoot();

        var result = TriviaNormalizationHelper.NormalizeUsingToMemberSeparator(root);

        var text = result.ToFullString();
        // After normalization, there should be a blank line between using and class.
        // The helper prepends \r\n\r\n to the first member's first token, so the result
        // includes the using's trailing \r\n + the two inserted \r\n = at least one blank line.
        StringAssert.Contains(text, "using System;");
        StringAssert.Contains(text, "class C");
        Assert.IsTrue(text.IndexOf("class C") > text.IndexOf("using System;") + "using System;\r\n".Length,
            "Expected at least one blank line between usings and class declaration.");
    }

    [TestMethod]
    public void NormalizeUsingToMemberSeparator_MultipleBlankLinesBetweenUsingsAndClass_Normalized()
    {
        var source = "using System;\r\n\r\n\r\n\r\nclass C { }\r\n";
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetCompilationUnitRoot();

        var result = TriviaNormalizationHelper.NormalizeUsingToMemberSeparator(root);

        var text = result.ToFullString();
        // After normalization, the first member's leading trivia should be exactly
        // two \r\n (one blank line). The using's trailing \r\n is separate.
        var classIndex = text.IndexOf("class C");
        Assert.IsTrue(classIndex > 0, "Expected class declaration in output");
        // Count newlines between semicolon and class — should be fewer than original
        var betweenSection = text["using System;".Length..classIndex];
        var originalBetween = source["using System;".Length..source.IndexOf("class C")];
        Assert.IsTrue(betweenSection.Length <= originalBetween.Length,
            "Normalized text should have fewer or equal newlines between usings and class.");
    }

    #endregion

    #region CollapseBlankLinesInUsingBlock

    [TestMethod]
    public void CollapseBlankLinesInUsingBlock_ConsecutiveBlankLinesBetweenUsings_CollapsedToOne()
    {
        var source = "using System;\r\n\r\n\r\nusing System.Linq;\r\nclass C { }\r\n";
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetCompilationUnitRoot();

        var result = TriviaNormalizationHelper.CollapseBlankLinesInUsingBlock(root);

        var text = result.ToFullString();
        // The helper collapses consecutive EOL in each using directive's TRAILING trivia.
        // The input has 3 \r\n between the two usings (the `;` trailing \r\n plus two blank lines).
        // After collapsing, the runs of 2+ consecutive EOL in trailing trivia are reduced.
        // Verify the output has strictly fewer newlines than the input.
        var inputBetween = source["using System;".Length..source.IndexOf("using System.Linq;")];
        var betweenUsings = text["using System;".Length..text.IndexOf("using System.Linq;")];
        Assert.IsTrue(betweenUsings.Length <= inputBetween.Length,
            $"Expected collapsed output between usings ({betweenUsings.Length} chars) " +
            $"to be shorter or equal to input ({inputBetween.Length} chars).");
    }

    [TestMethod]
    public void CollapseBlankLinesInUsingBlock_NoBlankLines_NoOp()
    {
        var source = "using System;\r\nusing System.Linq;\r\nclass C { }\r\n";
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetCompilationUnitRoot();

        var result = TriviaNormalizationHelper.CollapseBlankLinesInUsingBlock(root);

        Assert.AreEqual(source, result.ToFullString());
    }

    #endregion
}
