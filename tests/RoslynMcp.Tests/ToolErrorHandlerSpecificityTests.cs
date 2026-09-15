using System.Text.Json;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ToolErrorHandlerSpecificityTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NearestHandler_UsesInheritanceRatherThanRegistrationOrder(bool reverse)
    {
        KeyValuePair<Type, string>[] registrations =
        [
            new(typeof(Exception), "fallback"),
            new(typeof(ArgumentException), "argument"),
            new(typeof(ArgumentNullException), "required"),
        ];
        var handlers = (reverse ? registrations.Reverse() : registrations)
            .ToDictionary(entry => entry.Key, entry => entry.Value);

        Assert.AreEqual("required", ToolErrorHandler.FindNearestHandler(new ArgumentNullException(), handlers));
        Assert.AreEqual("required", ToolErrorHandler.FindNearestHandler(new DerivedNullException(), handlers));
        Assert.AreEqual("argument", ToolErrorHandler.FindNearestHandler(new ArgumentOutOfRangeException(), handlers));
        Assert.AreEqual("fallback", ToolErrorHandler.FindNearestHandler(new InvalidOperationException(), handlers));
        Assert.IsNull(ToolErrorHandler.FindNearestHandler(new Exception(), new Dictionary<Type, string>()));
    }

    [TestMethod]
    public void DerivedBindingException_PreservesRequiredParameterRecovery()
    {
        using var json = JsonDocument.Parse(ToolErrorHandler.ClassifyAndFormat(new DerivedNullException(), "test_run"));
        var envelope = json.RootElement;
        Assert.AreEqual("InvalidArgument", envelope.GetProperty("category").GetString());
        StringAssert.Contains(envelope.GetProperty("message").GetString(), "missing or null");
    }

    [TestMethod]
    public void SpecificExecutionFailures_PreservePublicCategories()
    {
        (Exception Error, string Category)[] cases =
        [
            (new PreviewTokenStaleException("opaque", "private detail"), "PreviewTokenStale"),
            (new WorkspaceEvictedException("opaque", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "private-path", "private detail"), "WorkspaceEvicted"),
            (new KeyNotFoundException("private detail"), "NotFound"),
            (new InvalidOperationException("private detail"), "InvalidOperation"),
        ];
        foreach (var (error, category) in cases)
        {
            using var json = JsonDocument.Parse(ToolErrorHandler.ClassifyAndFormat(error, "test_run"));
            Assert.AreEqual(category, json.RootElement.GetProperty("category").GetString());
            Assert.IsFalse(json.RootElement.GetRawText().Contains("private", StringComparison.Ordinal));
        }
    }

    private sealed class DerivedNullException : ArgumentNullException;
}
