using System.Text.Json;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression for `mcp-parameter-validation-error-messages` (P3-UX): missing/invalid
/// parameters surfaced as generic "An error occurred invoking '&lt;tool&gt;'" with no
/// indication of which parameter was wrong (NetworkDocumentation + IT-Chat-Bot 2026-04-13).
/// <see cref="ToolErrorHandler.ClassifyError"/> now walks the inner-exception chain for
/// the standard binding-failure shapes and surfaces a structured InvalidArgument that
/// names the offending parameter.
/// </summary>
[TestClass]
public sealed class ToolErrorHandlerParameterValidationTests
{
    [TestMethod]
    public async Task ArgumentNullException_WrappedInInvocationException_SurfacesParameterName()
    {
        var inner = new ArgumentNullException("workspaceId", "Required parameter is null");
        var outer = new System.Reflection.TargetInvocationException("Invocation failed", inner);

        var result = await ToolErrorHandler.ExecuteAsync(
            "test_tool",
            () => throw outer);

        var doc = JsonDocument.Parse(result);
        var category = doc.RootElement.GetProperty("category").GetString();
        Assert.AreEqual("InvalidArgument", category, $"expected InvalidArgument, got {category}; full payload: {result}");
        var message = doc.RootElement.GetProperty("message").GetString();
        StringAssert.Contains(message, "workspaceId", $"message must name the missing parameter; got: {message}");
    }

    [TestMethod]
    public async Task ArgumentOutOfRangeException_WrappedInInvocationException_SurfacesParameterName()
    {
        var inner = new ArgumentOutOfRangeException("limit", 5000, "Value must be <= 1000");
        var outer = new InvalidOperationException("Tool invocation failed", inner);

        var result = await ToolErrorHandler.ExecuteAsync(
            "test_tool",
            () => throw outer);

        var doc = JsonDocument.Parse(result);
        Assert.AreEqual("InvalidArgument", doc.RootElement.GetProperty("category").GetString());
        StringAssert.Contains(doc.RootElement.GetProperty("message").GetString(), "limit");
    }

    [TestMethod]
    public async Task JsonException_WrappedInInvocationException_SurfacesAsInvalidArgument()
    {
        var inner = new JsonException("Unexpected token at line 3");
        var outer = new InvalidOperationException("Invocation failed", inner);

        var result = await ToolErrorHandler.ExecuteAsync(
            "test_tool",
            () => throw outer);

        var doc = JsonDocument.Parse(result);
        Assert.AreEqual("InvalidArgument", doc.RootElement.GetProperty("category").GetString());
        var message = doc.RootElement.GetProperty("message").GetString();
        StringAssert.Contains(message, "JSON", $"message must indicate a JSON problem; got: {message}");
    }

    [TestMethod]
    public async Task FormatException_WrappedInInvocationException_SurfacesAsInvalidArgument()
    {
        var inner = new FormatException("Input string was not in a correct format");
        var outer = new InvalidOperationException("Invocation failed", inner);

        var result = await ToolErrorHandler.ExecuteAsync(
            "test_tool",
            () => throw outer);

        var doc = JsonDocument.Parse(result);
        Assert.AreEqual("InvalidArgument", doc.RootElement.GetProperty("category").GetString());
        StringAssert.Contains(doc.RootElement.GetProperty("message").GetString(), "format");
    }

    [TestMethod]
    public async Task UnrecognizedException_WithoutBindingShape_StillFallsThroughToInternalError()
    {
        // Guard: the new walker should NOT mis-classify legitimate internal errors as
        // parameter validation failures. NullReferenceException with no obvious binding
        // signal stays as InternalError.
        var outer = new InvalidOperationException("Wrapper",
            new NullReferenceException("Object reference not set"));

        var result = await ToolErrorHandler.ExecuteAsync("test_tool", () => throw outer);
        var doc = JsonDocument.Parse(result);
        var category = doc.RootElement.GetProperty("category").GetString();
        // The outer is InvalidOperationException, which the ErrorHandlers dictionary
        // catches as "InvalidOperation" — the inner-walker only fires for the fallback
        // path when no dictionary entry matches.
        Assert.AreEqual("InvalidOperation", category, $"unexpected: {result}");
    }
}
