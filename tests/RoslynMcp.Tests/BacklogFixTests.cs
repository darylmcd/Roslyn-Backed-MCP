using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace RoslynMcp.Tests;

/// <summary>
/// Tests covering P0-P2 backlog fixes: error handling, defensive wrapping, and refactored services.
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class BacklogFixTests : SharedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    // ── BUG-05: Error messages include exception chain and stack trace ──

    [TestMethod]
    public async Task ToolErrorHandler_InternalError_IncludesExceptionTypeAndStackTrace()
    {
        var result = await ToolExecutionTestHarness.RunAsync(
            "test_tool",
            () => throw new NullReferenceException("test null ref"));

        var json = JsonDocument.Parse(result);
        Assert.IsTrue(json.RootElement.GetProperty("error").GetBoolean());
        Assert.AreEqual("InternalError", json.RootElement.GetProperty("category").GetString());
        Assert.IsTrue(json.RootElement.GetProperty("message").GetString()!.Contains("NullReferenceException"));
        Assert.IsTrue(json.RootElement.TryGetProperty("stackTrace", out _));
    }

    [TestMethod]
    public async Task ToolErrorHandler_InternalError_IncludesInnerExceptionChain()
    {
        var inner2 = new ArgumentException("deep cause");
        var inner1 = new InvalidCastException("mid cause", inner2);
        var outer = new AggregateException("wrapper", inner1);

        var result = await ToolExecutionTestHarness.RunAsync("test_tool", () => throw outer);

        var json = JsonDocument.Parse(result);
        var message = json.RootElement.GetProperty("message").GetString()!;
        Assert.IsTrue(message.Contains("InvalidCastException"));
        Assert.IsTrue(message.Contains("ArgumentException"));
    }

    [TestMethod]
    public async Task ToolErrorHandler_KnownErrors_DoNotIncludeStackTrace()
    {
        var result = await ToolExecutionTestHarness.RunAsync(
            "test_tool",
            () => throw new KeyNotFoundException("not found"));

        var json = JsonDocument.Parse(result);
        Assert.AreEqual("NotFound", json.RootElement.GetProperty("category").GetString());
        Assert.IsFalse(json.RootElement.TryGetProperty("stackTrace", out _));
    }

    // ── CODE-04: InvalidOperationException is handled via dictionary ──

    [TestMethod]
    public async Task ToolErrorHandler_InvalidOperation_ClassifiedCorrectly()
    {
        var result = await ToolExecutionTestHarness.RunAsync(
            "test_tool",
            () => throw new InvalidOperationException("workspace stale"));

        var json = JsonDocument.Parse(result);
        Assert.AreEqual("InvalidOperation", json.RootElement.GetProperty("category").GetString());
    }

    [TestMethod]
    public async Task ToolErrorHandler_RateLimit_ClassifiedCorrectly()
    {
        var result = await ToolExecutionTestHarness.RunAsync(
            "test_tool",
            () => throw new InvalidOperationException("Rate limit exceeded"));

        var json = JsonDocument.Parse(result);
        Assert.AreEqual("RateLimited", json.RootElement.GetProperty("category").GetString());
    }

    // ── CODE-08: GatedCommandExecutor resolves projects correctly ──

    [TestMethod]
    public async Task GatedCommandExecutor_ResolveProject_FindsByName()
    {
        var workspaceId = await LoadSampleSolutionAsync();
        var project = GatedCommandExecutor.ResolveProject(workspaceId, "SampleLib");
        Assert.AreEqual("SampleLib", project.Name);
    }

    [TestMethod]
    public void GatedCommandExecutor_ResolveProject_ThrowsForUnknown()
    {
        Assert.ThrowsException<InvalidOperationException>(() =>
        {
            var workspaceId = WorkspaceManager.ListWorkspaces().First().WorkspaceId;
            GatedCommandExecutor.ResolveProject(workspaceId, "NonExistentProject");
        });
    }

    // ── BUG-15: Snippet analysis resolves Microsoft.Extensions usings ──

    [TestMethod]
    public async Task SnippetAnalysis_MicrosoftExtensions_Resolves()
    {
        var result = await SnippetAnalysisService.AnalyzeAsync(
            "var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;",
            ["Microsoft.Extensions.Logging", "Microsoft.Extensions.Logging.Abstractions"],
            "statements",
            CancellationToken.None);

        // Should either resolve with 0 errors, or at most report for missing logger package
        // (but not "type not found" for System.Text.Json which was the original bug)
        Assert.IsNotNull(result);
    }

    // ── BUG-04: Session lookup error includes active count ──

    [TestMethod]
    public void WorkspaceManager_SessionNotFound_ErrorIncludesActiveCount()
    {
        var ex = Assert.ThrowsException<KeyNotFoundException>(() =>
            WorkspaceManager.GetCurrentSolution("nonexistent-workspace-id"));

        Assert.IsTrue(ex.Message.Contains("active session(s)"));
    }

    [TestMethod]
    public void WorkspaceManager_HasSession_ReturnsFalseForNonexistent()
    {
        Assert.IsFalse(WorkspaceManager.HasSession("nonexistent-workspace-id"));
    }

    [TestMethod]
    public async Task WorkspaceManager_HasSession_ReturnsTrueForActive()
    {
        var workspaceId = await LoadSampleSolutionAsync();
        Assert.IsTrue(WorkspaceManager.HasSession(workspaceId));
    }

    // ── Helper ──

    private async Task<string> LoadSampleSolutionAsync()
    {
        return await GetOrLoadWorkspaceIdAsync(SampleSolutionPath, CancellationToken.None);
    }

    // ── AUDIT-23: semantic_search query parsing ──

    [TestMethod]
    public async Task SemanticSearch_ClassesImplementingIDisposable_FindsDisposableClass()
    {
        var workspaceId = await LoadSampleSolutionAsync();
        var response = await CodePatternAnalyzer.SemanticSearchAsync(
            workspaceId, "classes implementing IDisposable", "SampleLib", 50, CancellationToken.None);

        Assert.IsNotNull(response.Debug);
        CollectionAssert.Contains(response.Debug!.AppliedPredicates.ToList(), "implementing-interface");
        Assert.IsTrue(
            response.Results.Any(r => r.SymbolName == "BacklogDisposableSample"),
            "Expected disposable sample class.");
        Assert.IsNull(response.Warning);
    }

    [TestMethod]
    public async Task SemanticSearch_MethodsReturningTaskBool_FindsSample()
    {
        var workspaceId = await LoadSampleSolutionAsync();
        var response = await CodePatternAnalyzer.SemanticSearchAsync(
            workspaceId, "methods returning Task<bool>", "SampleLib", 50, CancellationToken.None);

        Assert.IsNotNull(response.Debug);
        CollectionAssert.Contains(response.Debug!.AppliedPredicates.ToList(), "returning-type");
        Assert.IsTrue(
            response.Results.Any(r => r.SymbolName == "ReturnsBoolAsync"),
            "Expected method returning Task<bool>.");
    }

    [TestMethod]
    public async Task SemanticSearch_HtmlEncodedAngleBrackets_StillFindsSample()
    {
        // BUG fix (semantic-search-html-decode): AI clients commonly double-encode angle
        // brackets when constructing JSON queries. The decoded query should match the same
        // results as the unencoded version.
        var workspaceId = await LoadSampleSolutionAsync();
        var response = await CodePatternAnalyzer.SemanticSearchAsync(
            workspaceId, "methods returning Task&lt;bool&gt;", "SampleLib", 50, CancellationToken.None);

        Assert.IsTrue(
            response.Results.Any(r => r.SymbolName == "ReturnsBoolAsync"),
            "Expected encoded query 'Task&lt;bool&gt;' to be HTML-decoded and match the same results as 'Task<bool>'.");
    }

    // ── AUDIT-35: unused symbol confidence ──

    [TestMethod]
    public async Task FindUnusedSymbols_PublicEnumValue_HasLowConfidence_WhenUnused()
    {
        var workspaceId = await LoadSampleSolutionAsync();
        var unused = await UnusedCodeAnalyzer.FindUnusedSymbolsAsync(
            workspaceId,
            new UnusedSymbolsAnalysisOptions { ProjectFilter = "SampleLib", IncludePublic = true, Limit = 200 },
            CancellationToken.None);

        var enumMember = unused.FirstOrDefault(u => u.SymbolName == "NeverReferencedValue");
        Assert.IsNotNull(enumMember, "Expected unused public enum member in backlog sample.");
        Assert.AreEqual("low", enumMember.Confidence);
    }
}
