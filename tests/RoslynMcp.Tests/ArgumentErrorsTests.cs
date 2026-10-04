using System.Text.Json;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ArgumentErrorsTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PublicArgument_WithInner_PreservesTrustedIdentityAndHidesInner(bool bindingFailure)
    {
        const string secret = "SECRET-SENTINEL-private-inner";
        var inner = new JsonException(secret);
        var exception = bindingFailure
            ? PublicArgumentException.FromPromptParameterBindingFailure("Use the schema.", inner)
            : new PublicArgumentException("Use the schema.", "value", inner);
        var identity = bindingFailure ? "PromptParameterBindingException" : "ArgumentException";
        Assert.AreSame(inner, exception.InnerException);
        Assert.AreEqual(bindingFailure ? "parametersJson" : "value", exception.ParamName);
        Assert.AreEqual(identity, exception.WireExceptionType);
        foreach (var wrapped in new Exception[] { exception,
            new System.Reflection.TargetInvocationException(exception) })
        {
            var json = ToolErrorHandler.ClassifyAndFormat(wrapped, "get_prompt_text");
            using var document = JsonDocument.Parse(json);
            Assert.AreEqual(wrapped is System.Reflection.TargetInvocationException
                ? nameof(System.Reflection.TargetInvocationException) : identity,
                document.RootElement.GetProperty("exceptionType").GetString());
            Assert.AreEqual("Use the schema.", document.RootElement.GetProperty("message").GetString());
            Assert.IsFalse(json.Contains(secret, StringComparison.Ordinal));
        }

        var overrideInfo = new ToolErrorHandler.ErrorInfo(ToolErrorHandler.ErrorCategories.InvalidArgument,
            "Use the schema.", WireExceptionType: "ReleasedOverride");
        using var overrideDocument = JsonDocument.Parse(
            ToolErrorHandler.FormatErrorResponse(overrideInfo, "get_prompt_text", exception));
        Assert.AreEqual("ReleasedOverride",
            overrideDocument.RootElement.GetProperty("exceptionType").GetString());
    }

    [TestMethod]
    public void PublicArgument_OrdinaryConstructor_PreservesArgumentIdentity()
    {
        var exception = new PublicArgumentException("Choose a valid value.", "value");
        Assert.AreEqual("ArgumentException", exception.WireExceptionType);
        Assert.IsNull(exception.InnerException);
    }

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
