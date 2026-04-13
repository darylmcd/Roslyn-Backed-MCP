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
}
