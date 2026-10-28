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
            (new WorkspaceNotFoundException("opaque", "private detail"), "WorkspaceNotFound"),
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
    public void WorkspaceMiss_ReportsWorkspaceNotFoundCategoryAndExceptionType()
    {
        using var json = JsonDocument.Parse(
            ToolErrorHandler.ClassifyAndFormat(new WorkspaceNotFoundException("opaque", "private detail"), "compile_check"));
        var envelope = json.RootElement;

        Assert.AreEqual("WorkspaceNotFound", envelope.GetProperty("category").GetString());
        Assert.AreEqual("WorkspaceNotFound", envelope.GetProperty("reason").GetString());
        Assert.AreEqual("WorkspaceNotFoundException", envelope.GetProperty("exceptionType").GetString(),
            "5.0 reports the exception type name for a workspace miss.");
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
    public async Task UnknownWorkspaceId_AfterAutoReload_ReportsWorkspaceNotFoundEnvelope()
    {
        // 4.x classified a workspace miss that raced an in-call auto-reload as
        // WorkspaceReloadedDuringCall with exceptionType KeyNotFoundException. 5.0 returns the same
        // WorkspaceNotFound envelope as the plain miss (workspace-not-found-next-major-category-promotion).
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
        var envelope = doc.RootElement;
        Assert.AreEqual("WorkspaceNotFound", envelope.GetProperty("category").GetString(),
            $"A workspace miss must not be reported as a reload race. Payload: {result}");
        Assert.AreEqual("WorkspaceNotFoundException", envelope.GetProperty("exceptionType").GetString(),
            $"exceptionType names the exception on the reload-race path too. Payload: {result}");
        Assert.AreEqual("WorkspaceNotFound", envelope.GetProperty("reason").GetString(),
            $"The workspace reason must survive an in-call auto-reload. Payload: {result}");
        var message = envelope.GetProperty("message").GetString();
        StringAssert.Contains(message, "workspace_load",
            $"A workspace miss must name the recovery tool, not only symbol re-resolution. Payload: {result}");
        Assert.IsFalse(envelope.GetRawText().Contains("private", StringComparison.Ordinal), result);
    }

    [TestMethod]
    public async Task SymbolMiss_AfterAutoReload_StaysReloadRaceWithoutWorkspaceReason()
    {
        var result = await ToolExecutionTestHarness.RunAsync(
            "get_source_text",
            () =>
            {
                if (AmbientGateMetrics.Current is { } m)
                {
                    m.StaleAction = "auto-reloaded";
                }

                throw new KeyNotFoundException("Symbol handle is stale: private detail");
            });

        using var doc = JsonDocument.Parse(result);
        var envelope = doc.RootElement;
        Assert.AreEqual("WorkspaceReloadedDuringCall", envelope.GetProperty("category").GetString(), result);
        Assert.IsFalse(envelope.TryGetProperty("reason", out _),
            $"Only a workspace miss carries the reason discriminator. Payload: {result}");
        StringAssert.Contains(envelope.GetProperty("message").GetString(), "Re-resolve the symbol", result);
    }

    private sealed class DerivedNullException : ArgumentNullException;
}
