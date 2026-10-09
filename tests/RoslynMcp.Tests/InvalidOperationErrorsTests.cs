using System.Text.Json;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class InvalidOperationErrorsTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Internal_PreservesExactIdentityAndServerDetailsWithoutDisclosure(bool withInner)
    {
        const string detail = "SECRET-SENTINEL server path C:/private/server.txt";
        Exception? inner = withInner ? new Exception("SECRET-SENTINEL inner") : null;
        var exception = InvalidOperationErrors.Internal(detail, inner);
        Assert.AreEqual(typeof(InvalidOperationException), exception.GetType());
        Assert.AreEqual(detail, exception.Message);
        Assert.AreSame(inner, exception.InnerException);
        Assert.IsFalse(exception is IPublicMessageException);
        using var document = JsonDocument.Parse(ToolErrorHandler.ClassifyAndFormat(exception, "some_tool"));
        Assert.AreEqual("InvalidOperation", document.RootElement.GetProperty("category").GetString());
        Assert.AreEqual("InvalidOperationException", document.RootElement.GetProperty("exceptionType").GetString());
        StringAssert.Contains(document.RootElement.GetProperty("message").GetString(), "Check the tool contract");
        Assert.IsFalse(document.RootElement.ToString().Contains("SECRET-SENTINEL", StringComparison.Ordinal));
    }

    [TestMethod]
    public void PublicRefusal_StillReturnsAuthoredGuidanceAndHidesInner()
    {
        var exception = new PublicInvalidOperationException("Choose a valid operation.", new Exception("SECRET-SENTINEL"));
        using var document = JsonDocument.Parse(ToolErrorHandler.ClassifyAndFormat(exception, "some_tool"));
        Assert.AreEqual("Choose a valid operation.", document.RootElement.GetProperty("message").GetString());
        Assert.IsFalse(document.RootElement.ToString().Contains("SECRET-SENTINEL", StringComparison.Ordinal));
    }
}
