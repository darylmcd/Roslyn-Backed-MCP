using System.Text.Json;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

/// <summary>
/// Pins the <see cref="IPublicMessageException"/> contract: public-message types implement the
/// marker, the shared error boundary returns their message verbatim, and the envelope
/// <c>exceptionType</c> is the BCL base name rather than the implementing class name.
/// </summary>
[TestClass]
public sealed class PublicMessageExceptionTests
{
    [TestMethod]
    public void PublicArgumentOutOfRangeException_PreservesDirectBclIdentity()
    {
        Assert.AreSame(typeof(ArgumentOutOfRangeException), typeof(PublicArgumentOutOfRangeException).BaseType);
        var ex = new PublicArgumentOutOfRangeException("Choose a line before EOF.", "lineRange");
        Assert.IsInstanceOfType<IPublicMessageException>(ex);
        Assert.AreEqual("lineRange", ex.ParamName);
        using var doc = JsonDocument.Parse(ToolErrorHandler.ClassifyAndFormat(ex, "resource"));
        Assert.AreEqual("InvalidArgument", doc.RootElement.GetProperty("category").GetString());
        Assert.AreEqual("Choose a line before EOF.", doc.RootElement.GetProperty("message").GetString());
        Assert.AreEqual(nameof(ArgumentOutOfRangeException), doc.RootElement.GetProperty("exceptionType").GetString());
    }

    [TestMethod]
    public void PublicArgumentException_ImplementsMarker_AndDerivesFromArgumentException()
    {
        var ex = new PublicArgumentException("Choose a valid mode.", "mode");

        Assert.IsInstanceOfType<IPublicMessageException>(ex);
        Assert.IsInstanceOfType<ArgumentException>(ex);
        Assert.AreEqual("Choose a valid mode.", ex.PublicMessage);
        Assert.AreEqual("mode", ex.ParamName);
        Assert.AreSame(typeof(ArgumentException), typeof(PublicArgumentException).BaseType);
    }

    [TestMethod]
    public void PublicInvalidOperationException_ImplementsMarker_AndDerivesFromInvalidOperationException()
    {
        var ex = new PublicInvalidOperationException("State must change first.");

        Assert.IsInstanceOfType<IPublicMessageException>(ex);
        Assert.AreEqual("State must change first.", ex.PublicMessage);
        Assert.AreSame(typeof(InvalidOperationException), typeof(PublicInvalidOperationException).BaseType);
    }

    [TestMethod]
    public void ClassifyAndFormat_PublicArgumentException_VerbatimMessageAndBclExceptionType()
    {
        var json = ToolErrorHandler.ClassifyAndFormat(
            new PublicArgumentException("Parameter 'mode' must be one of: a, b.", "mode"), "some_tool");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.AreEqual("InvalidArgument", root.GetProperty("category").GetString());
        Assert.AreEqual("Parameter 'mode' must be one of: a, b.", root.GetProperty("message").GetString());
        Assert.AreEqual(nameof(ArgumentException), root.GetProperty("exceptionType").GetString());
    }

    [TestMethod]
    public void ClassifyAndFormat_PublicInvalidOperationException_VerbatimMessageAndBclExceptionType()
    {
        var json = ToolErrorHandler.ClassifyAndFormat(
            new PublicInvalidOperationException("Run restore, then retry."), "some_tool");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.AreEqual("InvalidOperation", root.GetProperty("category").GetString());
        Assert.AreEqual("Run restore, then retry.", root.GetProperty("message").GetString());
        Assert.AreEqual(nameof(InvalidOperationException), root.GetProperty("exceptionType").GetString());
    }

    [TestMethod]
    public void FormatErrorResponse_WireExceptionTypeOverride_WinsOverBaseTypeNormalization()
    {
        var info = new ToolErrorHandler.ErrorInfo(
            ToolErrorHandler.ErrorCategories.InvalidArgument,
            "Safe message.",
            WireExceptionType: nameof(KeyNotFoundException));

        var json = ToolErrorHandler.FormatErrorResponse(
            info, "some_tool", new PublicArgumentException("Safe message.", "mode"));
        using var doc = JsonDocument.Parse(json);

        Assert.AreEqual(nameof(KeyNotFoundException), doc.RootElement.GetProperty("exceptionType").GetString());
    }

    [TestMethod]
    public void ClassifyAndFormat_PlainArgumentException_StillReportsThrownTypeName()
    {
        var json = ToolErrorHandler.ClassifyAndFormat(new ArgumentOutOfRangeException("count"), "some_tool");
        using var doc = JsonDocument.Parse(json);

        Assert.AreEqual(nameof(ArgumentOutOfRangeException), doc.RootElement.GetProperty("exceptionType").GetString());
    }
}
