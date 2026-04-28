using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Host.Stdio.Diagnostics;
using RoslynMcp.Host.Stdio.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression for <c>concurrent-mcp-instances-no-tools</c>: when multiple roslynmcp
/// processes start in parallel and the MCP client presents an empty tool list for
/// the N-th instance, operators need a per-process signal to tell whether the
/// fault is server-side (WithToolsFromAssembly returned zero) or client-side
/// (every process registered the expected count).
/// <para>
/// These tests lock in three invariants of <see cref="StartupDiagnostics"/>:
///   (1) a real host.Build() registers the same number of tools/resources/prompts
///       that <see cref="ServerSurfaceCatalog"/> and the <c>[McpServer*]</c>-decorated
///       assembly reflection report;
///   (2) the <c>AllParityOk</c> helper flips to <see langword="false"/> when any
///       category disagrees, so the Error-branch log path actually triggers;
///   (3) <c>server_info</c> echoes the captured snapshot as <c>surface.registered</c>
///       so clients can observe the gap without stderr access.
/// </para>
/// </summary>
[TestClass]
public sealed class StartupDiagnosticsTests
{
    [TestMethod]
    public void BuiltHost_CapturedCounts_MatchCatalogAndReflection()
    {
        using var host = BuildTestHost();

        var report = StartupDiagnostics.Capture(
            host.Services,
            typeof(RoslynMcp.Host.Stdio.McpLoggingProvider).Assembly);

        Assert.IsTrue(report.AllParityOk,
            $"Parity mismatch: tools={report.ToolsRegistered}/{report.ToolsReflected}/{report.ToolsInCatalog}, " +
            $"resources={report.ResourcesRegistered}/{report.ResourcesReflected}/{report.ResourcesInCatalog}, " +
            $"prompts={report.PromptsRegistered}/{report.PromptsReflected}/{report.PromptsInCatalog} " +
            "(registered/reflected/catalog).");

        Assert.IsTrue(report.ToolsRegistered > 0,
            "WithToolsFromAssembly() returned zero tools — this is the exact server-side " +
            "failure shape that concurrent-mcp-instances-no-tools is designed to surface.");

        Assert.AreEqual(ServerSurfaceCatalog.Tools.Count, report.ToolsRegistered);
        Assert.AreEqual(ServerSurfaceCatalog.Resources.Count, report.ResourcesRegistered);
        Assert.AreEqual(ServerSurfaceCatalog.Prompts.Count, report.PromptsRegistered);
    }

    [TestMethod]
    public void MismatchedCounts_FlipParityFalse_ForCategoryAndAggregate()
    {
        var catalogTools = ServerSurfaceCatalog.Tools.Count;
        var catalogResources = ServerSurfaceCatalog.Resources.Count;
        var catalogPrompts = ServerSurfaceCatalog.Prompts.Count;

        var zeroToolsReport = new StartupDiagnostics.SurfaceRegistrationReport(
            ToolsRegistered: 0,
            ToolsReflected: catalogTools,
            ToolsInCatalog: catalogTools,
            ResourcesRegistered: catalogResources,
            ResourcesReflected: catalogResources,
            ResourcesInCatalog: catalogResources,
            PromptsRegistered: catalogPrompts,
            PromptsReflected: catalogPrompts,
            PromptsInCatalog: catalogPrompts);

        Assert.IsFalse(zeroToolsReport.ToolParityOk,
            "ToolParityOk must flag zero-registered even when reflected/catalog agree.");
        Assert.IsTrue(zeroToolsReport.ResourceParityOk);
        Assert.IsTrue(zeroToolsReport.PromptParityOk);
        Assert.IsFalse(zeroToolsReport.AllParityOk,
            "AllParityOk must fail the aggregate as soon as any single category fails — " +
            "otherwise the Error-level log branch never triggers on the concurrent-startup bug.");
    }

    [TestMethod]
    public void LogStartup_WhenAllParityOk_EmitsInformation_NotError()
    {
        var listLogger = new ListLogger();
        var report = new StartupDiagnostics.SurfaceRegistrationReport(
            ToolsRegistered: ServerSurfaceCatalog.Tools.Count,
            ToolsReflected: ServerSurfaceCatalog.Tools.Count,
            ToolsInCatalog: ServerSurfaceCatalog.Tools.Count,
            ResourcesRegistered: ServerSurfaceCatalog.Resources.Count,
            ResourcesReflected: ServerSurfaceCatalog.Resources.Count,
            ResourcesInCatalog: ServerSurfaceCatalog.Resources.Count,
            PromptsRegistered: ServerSurfaceCatalog.Prompts.Count,
            PromptsReflected: ServerSurfaceCatalog.Prompts.Count,
            PromptsInCatalog: ServerSurfaceCatalog.Prompts.Count);

        StartupDiagnostics.LogStartup(listLogger, report, "1.26.0-test");

        Assert.AreEqual(1, listLogger.Entries.Count);
        Assert.AreEqual(LogLevel.Information, listLogger.Entries[0].Level);
        StringAssert.Contains(listLogger.Entries[0].Message, "parity=ok");
        StringAssert.Contains(listLogger.Entries[0].Message, "1.26.0-test");
    }

    [TestMethod]
    public void LogStartup_WhenParityMismatched_EmitsError_WithCountsInMessage()
    {
        var listLogger = new ListLogger();
        var report = new StartupDiagnostics.SurfaceRegistrationReport(
            ToolsRegistered: 0,
            ToolsReflected: 42,
            ToolsInCatalog: 42,
            ResourcesRegistered: 9,
            ResourcesReflected: 9,
            ResourcesInCatalog: 9,
            PromptsRegistered: 20,
            PromptsReflected: 20,
            PromptsInCatalog: 20);

        StartupDiagnostics.LogStartup(listLogger, report, "1.26.0-test");

        Assert.AreEqual(1, listLogger.Entries.Count);
        Assert.AreEqual(LogLevel.Error, listLogger.Entries[0].Level,
            "Zero registered tools is the exact failure shape the diagnostic must flag loudly.");
        StringAssert.Contains(listLogger.Entries[0].Message, "PARITY MISMATCH");
        StringAssert.Contains(listLogger.Entries[0].Message, "concurrent-mcp-instances-no-tools");
    }

    [TestMethod]
    public void ServerInfo_EmitsSurfaceRegistered_WhenSnapshotIsPopulated()
    {
        using var host = BuildTestHost();
        var report = StartupDiagnostics.Capture(host.Services, typeof(RoslynMcp.Host.Stdio.McpLoggingProvider).Assembly);

        var prior = SurfaceRegistrationSnapshot.Value;
        try
        {
            SurfaceRegistrationSnapshot.Value = report;

            var json = ServerTools.GetServerInfo(
                new StubWorkspaceManager(),
                new StubVersionProvider()).GetAwaiter().GetResult();

            using var doc = JsonDocument.Parse(json);
            var surface = doc.RootElement.GetProperty("surface");
            var registered = surface.GetProperty("registered");

            Assert.AreEqual(report.ToolsRegistered, registered.GetProperty("tools").GetInt32());
            Assert.AreEqual(report.ResourcesRegistered, registered.GetProperty("resources").GetInt32());
            Assert.AreEqual(report.PromptsRegistered, registered.GetProperty("prompts").GetInt32());
            Assert.IsTrue(registered.GetProperty("parityOk").GetBoolean());
        }
        finally
        {
            SurfaceRegistrationSnapshot.Value = prior;
        }
    }

    [TestMethod]
    public void ServerInfo_OmitsSurfaceRegistered_WhenSnapshotIsUnpopulated()
    {
        var prior = SurfaceRegistrationSnapshot.Value;
        try
        {
            SurfaceRegistrationSnapshot.Value = null;

            var json = ServerTools.GetServerInfo(
                new StubWorkspaceManager(),
                new StubVersionProvider()).GetAwaiter().GetResult();

            using var doc = JsonDocument.Parse(json);
            var surface = doc.RootElement.GetProperty("surface");
            var registered = surface.GetProperty("registered");

            Assert.AreEqual(JsonValueKind.Null, registered.ValueKind,
                "surface.registered must be null (not absent) when unit-test paths skip host boot " +
                "— tools-that-don't-boot-the-host see no snapshot, and we don't want the field's " +
                "schema to disappear.");
        }
        finally
        {
            SurfaceRegistrationSnapshot.Value = prior;
        }
    }

    private static IHost BuildTestHost()
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();

        // Mirror the production composition root via the shared
        // AddRoslynMcpHostServices extension so production and test DI graphs
        // cannot drift (di-graph-triple-registration-cleanup).
        builder.Services.AddRoslynMcpHostServices(
            new WorkspaceManagerOptions(),
            new ValidationServiceOptions(),
            new PreviewStoreOptions(),
            new ExecutionGateOptions(),
            new SecurityOptions(),
            new ScriptingServiceOptions());

        // WithTools/Resources/PromptsFromAssembly register each attributed method as
        // a singleton McpServerTool/Resource/Prompt in DI. No transport needed for
        // StartupDiagnostics.Capture — it counts DI registrations directly. Must
        // pass the Host.Stdio assembly explicitly, otherwise the parameterless
        // overload uses Assembly.GetCallingAssembly() = RoslynMcp.Tests, which
        // carries zero attributed methods and masks the registration count.
        var hostAssembly = typeof(RoslynMcp.Host.Stdio.McpLoggingProvider).Assembly;
        builder.Services
            .AddMcpServer()
            .WithToolsFromAssembly(hostAssembly)
            .WithResourcesFromAssembly(hostAssembly)
            .WithPromptsFromAssembly(hostAssembly);

        return builder.Build();
    }

    private sealed class ListLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }

    private sealed class StubWorkspaceManager : IWorkspaceManager
    {
        public event Action<string>? WorkspaceClosed { add { } remove { } }
        public event Action<string>? WorkspaceReloaded { add { } remove { } }

        public Task<WorkspaceStatusDto> LoadAsync(string path, CancellationToken ct) => throw new NotSupportedException();
        public Task<WorkspaceStatusDto> ReloadAsync(string workspaceId, CancellationToken ct) => throw new NotSupportedException();
        public bool ContainsWorkspace(string workspaceId) => false;
        public bool IsStale(string workspaceId) => false;
        public bool Close(string workspaceId) => throw new NotSupportedException();
        public IReadOnlyList<WorkspaceStatusDto> ListWorkspaces() => Array.Empty<WorkspaceStatusDto>();
        public WorkspaceStatusDto GetStatus(string workspaceId) => throw new NotSupportedException();
        public Task<WorkspaceStatusDto> GetStatusAsync(string workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ProjectGraphDto GetProjectGraph(string workspaceId) => throw new NotSupportedException();
        public Task<IReadOnlyList<GeneratedDocumentDto>> GetSourceGeneratedDocumentsAsync(string workspaceId, string? projectName, CancellationToken ct) => throw new NotSupportedException();
        public Task<string?> GetSourceTextAsync(string workspaceId, string filePath, CancellationToken ct) => throw new NotSupportedException();
        public int GetCurrentVersion(string workspaceId) => throw new NotSupportedException();
        public void RestoreVersion(string workspaceId, int version) => throw new NotSupportedException();
        public Solution GetCurrentSolution(string workspaceId) => throw new NotSupportedException();
        public bool TryApplyChanges(string workspaceId, Solution newSolution) => throw new NotSupportedException();
        public Project? GetProject(string workspaceId, string projectNameOrPath) => null;
    }

    private sealed class StubVersionProvider : ILatestVersionProvider
    {
        public string? GetLatestVersion() => null;
    }
}
