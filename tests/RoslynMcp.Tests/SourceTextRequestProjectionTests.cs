using System.Text.Json;
using RoslynMcp.Host.Stdio;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

/// <summary>
/// Behaviour-preservation gate for the <c>get_source_text</c> validation + projection helper
/// extracted out of <c>WorkspaceTools.GetSourceText</c>. Asserts the exact exception paramNames,
/// clamp semantics, truncation marker literal, and camelCase wire keys the tool has always emitted.
/// </summary>
[TestClass]
public sealed class SourceTextRequestProjectionTests
{
    private const string _truncationMarkerFormat =
        "\n[TRUNCATED at {0} characters \u2014 re-request a narrower line range to see the rest]";

    [TestMethod]
    public void ValidateRequest_NonPositiveMaxChars_ThrowsNamingMaxChars()
    {
        var ex = Assert.ThrowsExactly<ArgumentException>(
            () => SourceTextRequestProjection.ValidateRequest(0, null, null));

        Assert.AreEqual("maxChars", ex.ParamName);
        StringAssert.StartsWith(ex.Message, "maxChars must be greater than 0 (got 0).");
    }

    [TestMethod]
    public void ValidateRequest_ZeroStartLine_ThrowsNamingStartLine()
    {
        var ex = Assert.ThrowsExactly<ArgumentException>(
            () => SourceTextRequestProjection.ValidateRequest(100, 0, null));

        Assert.AreEqual("startLine", ex.ParamName);
        StringAssert.StartsWith(ex.Message, "startLine must be >= 1 (got 0).");
    }

    [TestMethod]
    public void ValidateRequest_ZeroEndLine_ThrowsNamingEndLine()
    {
        var ex = Assert.ThrowsExactly<ArgumentException>(
            () => SourceTextRequestProjection.ValidateRequest(100, null, 0));

        Assert.AreEqual("endLine", ex.ParamName);
        StringAssert.StartsWith(ex.Message, "endLine must be >= 1 (got 0).");
    }

    [TestMethod]
    public void ValidateRequest_InvertedRange_ThrowsNamingStartLine()
    {
        // The historical paramName for the inverted-range throw is startLine, not endLine.
        var ex = Assert.ThrowsExactly<ArgumentException>(
            () => SourceTextRequestProjection.ValidateRequest(100, 5, 2));

        Assert.AreEqual("startLine", ex.ParamName);
        StringAssert.StartsWith(ex.Message, "startLine (5) must be <= endLine (2).");
    }

    [TestMethod]
    [DataRow(1, null)]
    [DataRow(1, 1)]
    [DataRow(2, 10)]
    public void ValidateRequest_ValidBounds_DoesNotThrow(int startLine, int? endLine)
        => SourceTextRequestProjection.ValidateRequest(65536, startLine, endLine);

    [TestMethod]
    public void Project_StartLinePastEndOfFile_ThrowsNamingStartLine()
    {
        var ex = Assert.ThrowsExactly<ArgumentException>(
            () => SourceTextRequestProjection.Project("C:/repo/A.cs", "a\nb\nc", 9, null, 65536));

        Assert.AreEqual("startLine", ex.ParamName);
        StringAssert.StartsWith(ex.Message, "startLine (9) is past the end of the file (3 lines).");
    }

    [TestMethod]
    public void Project_EndLineBeyondFile_ClampsToTotalLineCount()
    {
        var projection = SourceTextRequestProjection.Project("C:/repo/A.cs", "a\nb\nc", 2, 1000, 65536);

        Assert.AreEqual(3, projection.TotalLineCount);
        Assert.AreEqual(2, projection.RequestedStartLine);
        Assert.AreEqual(1000, projection.RequestedEndLine);
        Assert.AreEqual(2, projection.ReturnedStartLine);
        Assert.AreEqual(3, projection.ReturnedEndLine);
        Assert.AreEqual("b\nc", projection.Text);
        Assert.IsFalse(projection.Truncated);
    }

    [TestMethod]
    public void Project_EmptyText_CountsOneLineAndReturnsEmptySlice()
    {
        var projection = SourceTextRequestProjection.Project("C:/repo/Empty.cs", string.Empty, null, null, 65536);

        Assert.AreEqual(1, projection.TotalLineCount);
        Assert.AreEqual(1, projection.RequestedStartLine);
        Assert.AreEqual(1, projection.RequestedEndLine);
        Assert.AreEqual(1, projection.ReturnedEndLine);
        Assert.AreEqual(string.Empty, projection.Text);
        Assert.IsFalse(projection.Truncated);
    }

    [TestMethod]
    public void Project_TrailingNewline_DoesNotDoubleCountFinalLine()
    {
        var projection = SourceTextRequestProjection.Project("C:/repo/A.cs", "a\nb\n", null, null, 65536);

        Assert.AreEqual(2, projection.TotalLineCount);
        Assert.AreEqual(2, projection.ReturnedEndLine);
        Assert.AreEqual("a\nb\n", projection.Text);
    }

    [TestMethod]
    public void Project_SliceExactlyAtCap_IsNotTruncated()
    {
        var text = new string('x', 32);

        var projection = SourceTextRequestProjection.Project("C:/repo/A.cs", text, null, null, 32);

        Assert.IsFalse(projection.Truncated);
        Assert.AreEqual(32, projection.Text.Length);
        Assert.AreEqual(text, projection.Text);
        StringAssert.DoesNotMatch(projection.Text, new System.Text.RegularExpressions.Regex(@"\[TRUNCATED"));
    }

    [TestMethod]
    public void Project_SliceOverCap_TruncatesWithVerbatimMarker()
    {
        var text = new string('x', 40);
        const int maxChars = 32;

        var projection = SourceTextRequestProjection.Project("C:/repo/A.cs", text, null, null, maxChars);

        Assert.IsTrue(projection.Truncated);
        var expectedMarker = string.Format(
            System.Globalization.CultureInfo.InvariantCulture, _truncationMarkerFormat, maxChars);
        Assert.AreEqual(new string('x', maxChars) + expectedMarker, projection.Text);
        StringAssert.EndsWith(projection.Text, expectedMarker);
    }

    [TestMethod]
    public void Project_SerializedWithJsonDefaults_EmitsHistoricalCamelCaseKeys()
    {
        var projection = SourceTextRequestProjection.Project("C:/repo/A.cs", "a\nb\nc", 1, 2, 65536);

        var json = JsonSerializer.Serialize(projection, JsonDefaults.Indented);
        using var document = JsonDocument.Parse(json);
        var keys = document.RootElement.EnumerateObject().Select(p => p.Name).ToArray();

        CollectionAssert.AreEqual(
            new[]
            {
                "filePath",
                "totalLineCount",
                "requestedStartLine",
                "requestedEndLine",
                "returnedStartLine",
                "returnedEndLine",
                "truncated",
                "text",
            },
            keys);
        Assert.AreEqual("C:/repo/A.cs", document.RootElement.GetProperty("filePath").GetString());
        Assert.AreEqual("a\nb\n", document.RootElement.GetProperty("text").GetString());
    }
}
