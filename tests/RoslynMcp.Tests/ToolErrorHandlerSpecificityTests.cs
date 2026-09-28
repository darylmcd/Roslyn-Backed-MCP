using System.Text.Json;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Tests.Helpers;

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
            (new WorkspaceNotFoundException("opaque", "private detail"), "NotFound"),
            (new KeyNotFoundException("Metadata name not found: private detail"), "NotFound"),
            (new KeyNotFoundException("Symbol handle is stale: private detail"), "NotFound"),
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

    [TestMethod]
    public void WorkspaceMiss_KeepsV4NotFoundWireValues_AndAddsWorkspaceReason()
    {
        using var json = JsonDocument.Parse(
            ToolErrorHandler.ClassifyAndFormat(new WorkspaceNotFoundException("opaque", "private detail"), "compile_check"));
        var envelope = json.RootElement;

        Assert.AreEqual("NotFound", envelope.GetProperty("category").GetString());
        Assert.AreEqual("WorkspaceNotFound", envelope.GetProperty("reason").GetString());
        Assert.AreEqual("KeyNotFoundException", envelope.GetProperty("exceptionType").GetString(),
            "exceptionType keeps its 4.x wire value for a workspace miss.");
        StringAssert.Contains(envelope.GetProperty("message").GetString(), "workspace_load");
        Assert.IsFalse(envelope.GetRawText().Contains("private", StringComparison.Ordinal));
    }

    [TestMethod]
    public void NonWorkspaceMisses_CarryNoReason()
    {
        Exception[] misses =
        [
            new KeyNotFoundException("Metadata name not found: private detail"),
            new KeyNotFoundException("Document not found: private detail"),
            new SymbolNotFoundException("private detail", []),
            new WorkspaceEvictedException("opaque", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "private-path", "private detail"),
        ];
        foreach (var miss in misses)
        {
            using var json = JsonDocument.Parse(ToolErrorHandler.ClassifyAndFormat(miss, "test_run"));
            Assert.IsFalse(json.RootElement.TryGetProperty("reason", out _),
                $"{miss.GetType().Name} must not carry the workspace reason: {json.RootElement.GetRawText()}");
        }
    }

    [TestMethod]
    public async Task UnknownWorkspaceId_AfterAutoReload_StaysNotFoundWithWorkspaceReason()
    {
        var result = await ToolExecutionTestHarness.RunAsync(
            "get_source_text",
            () =>
            {
                if (AmbientGateMetrics.Current is { } m)
                {
                    m.StaleAction = "auto-reloaded";
                }

                throw new WorkspaceNotFoundException("opaque", "private detail");
            });

        using var doc = JsonDocument.Parse(result);
        Assert.AreEqual("NotFound", doc.RootElement.GetProperty("category").GetString(),
            $"An unknown workspaceId must not be relabeled WorkspaceReloadedDuringCall. Payload: {result}");
        Assert.AreEqual("WorkspaceNotFound", doc.RootElement.GetProperty("reason").GetString(),
            $"The workspace reason must survive an in-call auto-reload. Payload: {result}");
    }

    private sealed class DerivedNullException : ArgumentNullException;
}
