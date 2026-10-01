using System.Text.Json;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ArgumentErrorsTests
{
    [TestMethod]
    public void Redacted_ReturnsExactArgumentExceptionWithParamNameAndInner()
    {
        var inner = new InvalidOperationException("inner");

        var ex = ArgumentErrors.Redacted("widgetName", "server detail", inner);

        Assert.AreEqual(typeof(ArgumentException), ex.GetType());
        Assert.AreEqual("widgetName", ex.ParamName);
        StringAssert.Contains(ex.Message, "server detail");
        Assert.AreSame(inner, ex.InnerException);
    }

    [TestMethod]
    public void Redacted_WithoutInner_LeavesInnerNull()
    {
        var ex = ArgumentErrors.Redacted("widgetName", "server detail");

        Assert.IsNull(ex.InnerException);
    }

    [TestMethod]
    public void Redacted_NullParameterName_Throws()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => ArgumentErrors.Redacted(null!, "detail"));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void Redacted_BlankParameterName_Throws(string parameterName)
    {
        Assert.ThrowsExactly<ArgumentException>(() => ArgumentErrors.Redacted(parameterName, "detail"));
    }

    [TestMethod]
    public void Redacted_ClassifiedEnvelope_HidesServerDetailAndNamesParameter()
    {
        const string secretDetail = "C:\\secret\\server\\path\\caller-free-text.txt rejected";
        var ex = ArgumentErrors.Redacted("widgetName", secretDetail);

        var json = ToolErrorHandler.ClassifyAndFormat(ex, "some_tool");

        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual("InvalidArgument", doc.RootElement.GetProperty("category").GetString());
        Assert.AreEqual("ArgumentException", doc.RootElement.GetProperty("exceptionType").GetString());
        StringAssert.Contains(doc.RootElement.GetProperty("message").GetString(), "Parameter 'widgetName' is invalid");
        Assert.IsFalse(json.Contains("secret", StringComparison.Ordinal), $"Envelope leaked server detail: {json}");
    }
}
