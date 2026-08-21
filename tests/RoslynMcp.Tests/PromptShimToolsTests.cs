using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

// prompt-shim-parameter-binding-complexity-extraction: the parameter binder
// (BuildParameterValues + its ParseParametersDocument / EnsureRequiredParametersPresent /
// ResolveParameterValue / DeserializeParameterValue helpers) was previously only exercised
// on its error paths by the four GetPromptText_* tests in PromptSmokeTests.cs. This file adds
// the missing success-path regression: it drives dead_code_audit end-to-end through
// GetPromptText/BuildParameterValues with the real shared workspace fixture, a live
// CancellationToken, a DI-resolved service parameter (IUnusedCodeAnalyzer), a JSON-supplied
// value (workspaceId), and an omitted parameter falling through to its default (projectName),
// asserting the rendered message text. This pins the CT → DI service → supplied-JSON → default
// precedence chain alongside the value-free binding-error regressions below.
[DoNotParallelize]
[TestClass]
public sealed class PromptShimToolsTests : SharedWorkspaceTestBase
{
    private static string WorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        WorkspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);
    }

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task GetPromptText_DeadCodeAudit_BindsServiceJsonAndDefault_RendersText()
    {
        // dead_code_audit's first parameter is the DI service IUnusedCodeAnalyzer, so the
        // ServiceProvider must register it — an empty ServiceCollection would throw at
        // GetRequiredService before the assertion runs.
        using var services = new ServiceCollection()
            .AddSingleton<IUnusedCodeAnalyzer>(UnusedCodeAnalyzer)
            .BuildServiceProvider();

        // workspaceId is supplied via JSON; projectName is omitted so the binder falls through
        // to the parameter's default (null); the CancellationToken and the service are resolved
        // by the binder, not by parametersJson.
        var json = await PromptShimTools.GetPromptText(
            services,
            promptName: "dead_code_audit",
            parametersJson: $"{{\"workspaceId\":\"{WorkspaceId}\"}}",
            CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.AreEqual("dead_code_audit", root.GetProperty("promptName").GetString());
        Assert.AreEqual(2, root.GetProperty("parameterCount").GetInt32(),
            "dead_code_audit exposes exactly two user-facing parameters (workspaceId, projectName); "
            + "the service and CancellationToken must not be surfaced in the published schema.");

        var messages = root.GetProperty("messages");
        Assert.AreEqual(1, messages.GetArrayLength(),
            "dead_code_audit renders a single prompt message.");

        var text = messages[0].GetProperty("text").GetString() ?? string.Empty;
        StringAssert.Contains(text, "dead code audit",
            "Rendered dead_code_audit message must contain the audit body text.");
        StringAssert.Contains(text, "(entire workspace)",
            "Omitting projectName must fall through to the null default, which the template renders "
            + "as '(entire workspace)' — this pins the default-value binder branch.");
    }

    [TestMethod]
    [DataRow("document")]
    [DataRow("property")]
    public async Task GetPromptText_BindingFailure_PublishesOnlyStableCorrection(string failureKind)
    {
        const string secretSentinel =
            "SECRET-SENTINEL-private-payload-C:/private/source.cs";
        var parametersJson = failureKind == "document"
            ? $"{{\"secret\":\"{secretSentinel}\""
            : $"{{\"taskCategory\":{{\"secret\":\"{secretSentinel}\"}}}}";
        using var services = new ServiceCollection().BuildServiceProvider();

        var json = await ToolExecutionTestHarness.RunAsync(
            "get_prompt_text",
            () => PromptShimTools.GetPromptText(
                services,
                promptName: "discover_capabilities",
                parametersJson,
                CancellationToken.None));

        using var document = JsonDocument.Parse(json);
        Assert.AreEqual("InvalidArgument", document.RootElement.GetProperty("category").GetString());
        var message = document.RootElement.GetProperty("message").GetString() ?? string.Empty;
        StringAssert.Contains(message, "Example:", StringComparison.Ordinal);
        if (failureKind == "document")
        {
            StringAssert.Contains(message, "parametersJson", StringComparison.Ordinal);
            StringAssert.Contains(message, "JSON object", StringComparison.Ordinal);
        }
        else
        {
            StringAssert.Contains(message, "taskCategory", StringComparison.Ordinal);
            StringAssert.Contains(message, "String", StringComparison.Ordinal);
        }

        Assert.IsFalse(json.Contains(secretSentinel, StringComparison.Ordinal));
        Assert.IsFalse(json.Contains(nameof(JsonException), StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("private/source.cs", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task GetPromptText_NonObjectJson_ReturnsStructuredInvalidArgument()
    {
        using var services = new ServiceCollection().BuildServiceProvider();

        var json = await ToolExecutionTestHarness.RunAsync(
            "get_prompt_text",
            () => PromptShimTools.GetPromptText(
                services,
                promptName: "discover_capabilities",
                parametersJson: "[1, 2, 3]",
                CancellationToken.None));

        using var document = JsonDocument.Parse(json);
        Assert.AreEqual("InvalidArgument", document.RootElement.GetProperty("category").GetString());
        StringAssert.Contains(
            document.RootElement.GetProperty("message").GetString(),
            "must be a JSON object",
            StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task GetPromptText_MissingRequiredParameters_HidesInternalParameterInventory()
    {
        using var services = new ServiceCollection()
            .AddSingleton<IDiagnosticService>(DiagnosticService)
            .AddSingleton<IWorkspaceManager>(WorkspaceManager)
            .BuildServiceProvider();

        var json = await ToolExecutionTestHarness.RunAsync(
            "get_prompt_text",
            () => PromptShimTools.GetPromptText(
                services,
                promptName: "explain_error",
                parametersJson: "{}",
                CancellationToken.None));

        using var document = JsonDocument.Parse(json);
        Assert.AreEqual("InvalidArgument", document.RootElement.GetProperty("category").GetString());
        var message = document.RootElement.GetProperty("message").GetString() ?? string.Empty;
        StringAssert.Contains(message, "required prompt parameter", StringComparison.Ordinal);
        Assert.IsFalse(message.Contains("workspaceId", StringComparison.Ordinal));
        Assert.IsFalse(message.Contains("diagnosticId", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ResolvePromptMethod_RepeatedLookups_BuildReflectionIndexOnce()
    {
        var first = PromptShimTools.ResolvePromptMethod("dead_code_audit");
        var second = PromptShimTools.ResolvePromptMethod("dead_code_audit");

        Assert.IsNotNull(first.Method);
        Assert.AreSame(first.Method, second.Method);
        Assert.AreEqual(
            1,
            PromptShimTools.PromptIndexBuildCount,
            "Prompt reflection metadata must be indexed once and reused across calls.");
    }
}
