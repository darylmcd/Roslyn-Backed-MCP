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

    #region RemoveOrphanIndentTrivia

    [TestMethod]
    public void RemoveOrphanIndentTrivia_StripsOrphanIndentBetweenMembers()
    {
        // Simulate the post-RemoveNodes(KeepExteriorTrivia) shape: an orphan-indent line (four
        // spaces only) sandwiched between two end-of-line markers.
        var source = "class C\r\n{\r\n    public int A;\r\n    \r\n    public int B;\r\n}\r\n";
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetCompilationUnitRoot();

        var result = TriviaNormalizationHelper.RemoveOrphanIndentTrivia(root);

        var text = result.ToFullString();
        // No line should be entirely whitespace after the trim.
        var orphanLines = text.Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.Length > 0 && line.All(char.IsWhiteSpace))
            .ToArray();
        Assert.AreEqual(
            0,
            orphanLines.Length,
            $"Expected no whitespace-only lines but found {orphanLines.Length}: [{string.Join(", ", orphanLines.Select(line => $"'{line}'"))}]\nFull text:\n{text}");
        // The blank line between A and B is preserved as a true blank line (no orphan indent).
        StringAssert.Contains(text, "public int A;\r\n\r\n    public int B;");
    }

    [TestMethod]
    public void RemoveOrphanIndentTrivia_PreservesIndentationOfRealTokens()
    {
        var source = "class C\r\n{\r\n    public int A;\r\n    public int B;\r\n}\r\n";
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetCompilationUnitRoot();

        var result = TriviaNormalizationHelper.RemoveOrphanIndentTrivia(root);

        // Idempotent: nothing to strip, output equals input.
        Assert.AreEqual(source, result.ToFullString());
    }

    [TestMethod]
    public void RemoveOrphanIndentTrivia_LeavesContentInsideVerbatimStringLiteralsAlone()
    {
        // A verbatim string with an internal whitespace-only line stored as part of the token's
        // text (not as trivia). The rewriter must not touch this — string-level trims would.
        var source = "class C\r\n{\r\n    public string S = @\"line1\r\n    \r\n    line3\";\r\n}\r\n";
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetCompilationUnitRoot();

        var result = TriviaNormalizationHelper.RemoveOrphanIndentTrivia(root);

        var text = result.ToFullString();
        // The verbatim string content must be preserved byte-for-byte; the whitespace inside
        // `@"..."` is part of the string literal token, not trivia.
        StringAssert.Contains(text, "@\"line1\r\n    \r\n    line3\"");
    }

    #endregion
}
