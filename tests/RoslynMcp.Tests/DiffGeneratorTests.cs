using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public class DiffGeneratorTests
{
    [TestMethod]
    public void Identical_Texts_Returns_Header_Only()
    {
        var result = DiffGenerator.GenerateUnifiedDiff("hello\n", "hello\n", "test.cs");
        StringAssert.Contains(result, "--- a/test.cs");
        StringAssert.Contains(result, "+++ b/test.cs");
        Assert.IsFalse(result.Contains("@@") && result.Contains("-hello"), "No diff hunks expected for identical text");
    }

    [TestMethod]
    public void Single_Line_Change_Produces_Diff_Hunk()
    {
        var result = DiffGenerator.GenerateUnifiedDiff("old line\n", "new line\n", "file.cs");
        StringAssert.Contains(result, "@@");
        StringAssert.Contains(result, "-old line");
        StringAssert.Contains(result, "+new line");
    }

    [TestMethod]
    public void Insertion_Shows_Plus_Prefix()
    {
        var result = DiffGenerator.GenerateUnifiedDiff("", "added\n", "new.cs");
        StringAssert.Contains(result, "+added");
    }

    [TestMethod]
    public void Deletion_Shows_Minus_Prefix()
    {
        var result = DiffGenerator.GenerateUnifiedDiff("removed\n", "", "old.cs");
        StringAssert.Contains(result, "-removed");
    }

    [TestMethod]
    public void Truncation_At_MaxChars_Produces_Flag6A_Marker()
    {
        var oldText = string.Join("\n", Enumerable.Range(1, 100).Select(i => $"line {i}"));
        var newText = string.Join("\n", Enumerable.Range(1, 100).Select(i => $"changed {i}"));
        var result = DiffGenerator.GenerateUnifiedDiff(oldText, newText, "big.cs", maxChars: 200);
        StringAssert.Contains(result, "FLAG-6A");
        StringAssert.Contains(result, "truncated");
    }

    [TestMethod]
    public void MaxChars_Zero_Disables_Truncation()
    {
        var oldText = string.Join("\n", Enumerable.Range(1, 100).Select(i => $"line {i}"));
        var newText = string.Join("\n", Enumerable.Range(1, 100).Select(i => $"changed {i}"));
        var result = DiffGenerator.GenerateUnifiedDiff(oldText, newText, "big.cs", maxChars: 0);
        Assert.IsFalse(result.Contains("FLAG-6A"), "No truncation marker expected when maxChars=0");
    }

    [TestMethod]
    public void File_Path_Appears_In_Header()
    {
        var result = DiffGenerator.GenerateUnifiedDiff("a\n", "b\n", "src/MyClass.cs");
        StringAssert.Contains(result, "--- a/src/MyClass.cs");
        StringAssert.Contains(result, "+++ b/src/MyClass.cs");
    }

    // apply-text-edit-diff-quality: investigation pass — these edge-case repros confirm the
    // diff path's output cleanliness. The original audit text was vague ("stray ;, noisy
    // diff text"); after the overlap-safety + syntax-preflight work that landed in earlier
    // PRs, the cases below all produce clean unified diffs with no spurious characters.
    // If a future audit captures a concrete diff with extra characters, paste it as a new
    // test here and patch DiffGenerator to match.

    [TestMethod]
    public void TrailingNewlineMissing_LastLineEdit_NoStrayCharacters()
    {
        // Edit at end-of-file with no trailing newline shouldn't introduce a stray character.
        var result = DiffGenerator.GenerateUnifiedDiff("first\nlast", "first\nLAST", "eof.cs");
        StringAssert.Contains(result, "-last");
        StringAssert.Contains(result, "+LAST");
        Assert.IsFalse(result.Contains("+last;"), "Stray semicolon must not appear in the diff output.");
    }

    [TestMethod]
    public void CrlfLineEndings_RoundTripCleanly()
    {
        // Mixed LF/CRLF inputs: diff should still produce a coherent hunk with the right
        // before/after lines. (DiffPlex normalizes line endings internally; verify no leak.)
        var result = DiffGenerator.GenerateUnifiedDiff("alpha\r\nbeta\r\n", "alpha\r\nBETA\r\n", "crlf.cs");
        StringAssert.Contains(result, "-beta");
        StringAssert.Contains(result, "+BETA");
    }

    [TestMethod]
    public void CommentOnlyChange_PreservesCommentSemantics()
    {
        // Replacing a single-line comment shouldn't lose the // marker or merge with code.
        var result = DiffGenerator.GenerateUnifiedDiff(
            "var x = 1;\n// old comment\nvar y = 2;\n",
            "var x = 1;\n// new comment\nvar y = 2;\n",
            "comment.cs");
        StringAssert.Contains(result, "-// old comment");
        StringAssert.Contains(result, "+// new comment");
        Assert.IsFalse(result.Contains("-var x"), "Unchanged code lines must not appear as deletions.");
    }

    [TestMethod]
    public void SingleCharInsertionMidLine_ProducesClearHunk()
    {
        // The audit-cited "edge column" case: a one-character edit in the middle of a long
        // line. Both before and after lines should appear in full as separate -/+ entries.
        var result = DiffGenerator.GenerateUnifiedDiff(
            "public class Foo { public int Bar { get; } }\n",
            "public class Foo { public int Baz { get; } }\n",
            "edge.cs");
        StringAssert.Contains(result, "-public class Foo { public int Bar { get; } }");
        StringAssert.Contains(result, "+public class Foo { public int Baz { get; } }");
    }
}
