using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// Direct unit coverage for <see cref="ProjectRelativePathValidation"/>: hostile folder
/// segments (rooted, traversal, separators, empty, invalid chars) must refuse, benign
/// segments must pass, and <c>EnsureDescendantOfRoot</c> must catch escapes that segment
/// validation alone cannot — including the sibling-prefix trap (<c>C:/Proj</c> vs
/// <c>C:/Proj2</c>) that a naive <c>StartsWith</c> containment check admits.
/// </summary>
[TestClass]
public class ProjectRelativePathValidationTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "PrpvTestRoot");

    [TestMethod]
    public void Simple_Segments_Do_Not_Throw()
        => ProjectRelativePathValidation.ValidateFolderSegments(["Models", "Requests"], "dtoFolders");

    [TestMethod]
    public void Empty_Segment_Throws()
        => Assert.ThrowsExactly<InvalidOperationException>(
            () => ProjectRelativePathValidation.ValidateFolderSegments(["Models", ""], "dtoFolders"));

    [TestMethod]
    public void Whitespace_Segment_Throws()
        => Assert.ThrowsExactly<InvalidOperationException>(
            () => ProjectRelativePathValidation.ValidateFolderSegments(["   "], "dtoFolders"));

    [TestMethod]
    public void Single_Dot_Segment_Throws()
        => Assert.ThrowsExactly<InvalidOperationException>(
            () => ProjectRelativePathValidation.ValidateFolderSegments(["."], "dtoFolders"));

    [TestMethod]
    public void Traversal_Segment_Throws()
    {
        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => ProjectRelativePathValidation.ValidateFolderSegments([".."], "dtoFolders"));
        StringAssert.Contains(ex.Message, "..");
        StringAssert.Contains(ex.Message, "dtoFolders");
    }

    [TestMethod]
    public void ForwardSlash_Segment_Throws()
        => Assert.ThrowsExactly<InvalidOperationException>(
            () => ProjectRelativePathValidation.ValidateFolderSegments(["Models/Requests"], "dtoFolders"));

    [TestMethod]
    public void BackSlash_Segment_Throws()
        => Assert.ThrowsExactly<InvalidOperationException>(
            () => ProjectRelativePathValidation.ValidateFolderSegments(["Models\\Requests"], "dtoFolders"));

    [TestMethod]
    public void Rooted_Segment_Throws()
    {
        // "/abs" reads as rooted on Windows and Unix alike — but it also carries a
        // separator, so exercise the rooted arm with a drive-relative Windows form too.
        Assert.ThrowsExactly<InvalidOperationException>(
            () => ProjectRelativePathValidation.ValidateFolderSegments(["/abs"], "dtoFolders"));
        if (OperatingSystem.IsWindows())
            Assert.ThrowsExactly<InvalidOperationException>(
                () => ProjectRelativePathValidation.ValidateFolderSegments(["C:Temp"], "dtoFolders"));
    }

    [TestMethod]
    public void Nul_Char_Segment_Throws()
        => Assert.ThrowsExactly<InvalidOperationException>(
            () => ProjectRelativePathValidation.ValidateFolderSegments(["Mod\0els"], "dtoFolders"));

    [TestMethod]
    public void Descendant_Path_Returns_Canonical_Path()
    {
        var candidate = Path.Combine(Root, "Models", "Requests", "Dto.cs");
        var result = ProjectRelativePathValidation.EnsureDescendantOfRoot(Root, candidate, "dtoFolders");
        Assert.AreEqual(Path.GetFullPath(candidate), result);
    }

    [TestMethod]
    public void Root_Itself_Throws()
        => Assert.ThrowsExactly<InvalidOperationException>(
            () => ProjectRelativePathValidation.EnsureDescendantOfRoot(Root, Root, "dtoFolders"));

    [TestMethod]
    public void Parent_Escape_Throws()
    {
        var candidate = Path.Combine(Root, "Models", "..", "..", "Escaped.cs");
        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => ProjectRelativePathValidation.EnsureDescendantOfRoot(Root, candidate, "dtoFolders"));
        StringAssert.Contains(ex.Message, "outside");
    }

    [TestMethod]
    public void Sibling_Prefix_Directory_Throws()
    {
        // The StartsWith(root) trap: "<root>2/file.cs" string-prefixes "<root>" but is a
        // sibling directory, not a descendant.
        var candidate = Path.Combine(Root + "2", "file.cs");
        Assert.ThrowsExactly<InvalidOperationException>(
            () => ProjectRelativePathValidation.EnsureDescendantOfRoot(Root, candidate, "dtoFolders"));
    }

    [TestMethod]
    public void Segment_Starting_With_Dots_Is_Not_Mistaken_For_Traversal()
    {
        // A directory literally named "..foo" is inside the root; the first-segment
        // traversal check must not refuse it via a loose StartsWith("..").
        var candidate = Path.Combine(Root, "..foo", "Dto.cs");
        var result = ProjectRelativePathValidation.EnsureDescendantOfRoot(Root, candidate, "dtoFolders");
        Assert.AreEqual(Path.GetFullPath(candidate), result);
    }
}
