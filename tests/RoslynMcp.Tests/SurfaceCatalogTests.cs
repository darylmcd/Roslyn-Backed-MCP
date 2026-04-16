using System.Reflection;
using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Host.Stdio.Services;
using RoslynMcp.Host.Stdio.Tools;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Server;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class SurfaceCatalogTests
{
    [TestMethod]
    public void ServerSurfaceCatalog_CoversAllRegisteredToolsResourcesAndPrompts()
    {
        var assembly = typeof(ServerTools).Assembly;

        CollectionAssert.AreEquivalent(
            GetRegisteredNames<McpServerToolAttribute>(assembly),
            ServerSurfaceCatalog.Tools.Select(entry => entry.Name).ToArray());

        CollectionAssert.AreEquivalent(
            GetRegisteredNames<McpServerResourceAttribute>(assembly),
            ServerSurfaceCatalog.Resources.Select(entry => entry.Name).ToArray());

        CollectionAssert.AreEquivalent(
            GetRegisteredNames<McpServerPromptAttribute>(assembly),
            ServerSurfaceCatalog.Prompts.Select(entry => entry.Name).ToArray());
    }

    // dr-9-11-payload-exceeds-mcp-tool-result-cap: the default `server_catalog` resource must
    // return a cap-safe summary (no Tools / Prompts lists inline). The paginated siblings
    // serve those lists in slices.
    [TestMethod]
    public void ServerCatalogSummary_ReplacesToolsAndPromptsWithPaginationPointers()
    {
        var summary = ServerSurfaceCatalog.CreateSummaryDocument();
        Assert.AreEqual(ServerSurfaceCatalog.Tools.Count, summary.ToolCount);
        Assert.AreEqual(ServerSurfaceCatalog.Prompts.Count, summary.PromptCount);
        Assert.AreEqual("roslyn://server/catalog/tools/{offset}/{limit}", summary.ToolsResourceTemplate);
        Assert.AreEqual("roslyn://server/catalog/prompts/{offset}/{limit}", summary.PromptsResourceTemplate);
        CollectionAssert.AreEquivalent(
            ServerSurfaceCatalog.Resources.Select(e => e.Name).ToArray(),
            summary.Resources.Select(e => e.Name).ToArray());
    }

    [TestMethod]
    public void PageEntries_HonoursOffsetAndLimit_SurfacesPaginationMetadata()
    {
        var page = ServerSurfaceCatalog.PageEntries(ServerSurfaceCatalog.Tools, offset: 0, limit: 10, resourceName: "test");
        Assert.AreEqual("test", page.ResourceName);
        Assert.AreEqual(0, page.Offset);
        Assert.AreEqual(10, page.Limit);
        Assert.AreEqual(10, page.ReturnedCount);
        Assert.AreEqual(ServerSurfaceCatalog.Tools.Count, page.TotalCount);
        Assert.IsTrue(page.HasMore);
        Assert.AreEqual(10, page.Entries.Count);
    }

    [TestMethod]
    public void PageEntries_ClampsOffsetPastTotal_EmptyPageNoHasMore()
    {
        var total = ServerSurfaceCatalog.Tools.Count;
        var page = ServerSurfaceCatalog.PageEntries(ServerSurfaceCatalog.Tools, offset: total + 100, limit: 10, resourceName: "test");
        Assert.AreEqual(total, page.Offset, "Offset must clamp to total, not past it.");
        Assert.AreEqual(0, page.ReturnedCount);
        Assert.AreEqual(total, page.TotalCount);
        Assert.IsFalse(page.HasMore);
    }

    [TestMethod]
    public void PageEntries_ClampsLimitToCeiling()
    {
        var page = ServerSurfaceCatalog.PageEntries(ServerSurfaceCatalog.Tools, offset: 0, limit: 99999, resourceName: "test");
        Assert.AreEqual(200, page.Limit, "Limit must clamp to the 200 ceiling.");
    }

    [TestMethod]
    public void McpToolMetadata_RequiredOnEveryTool_MatchesCatalogEntry()
    {
        // Item 2 (v1.18): every [McpServerTool] method MUST carry [McpToolMetadata]. The
        // attribute's category/tier/readOnly/destructive/summary must match the
        // hand-maintained ServerSurfaceCatalog.Tools entry. Together these two checks make
        // it structurally impossible for a new tool to ship with stale or missing catalog
        // metadata: the name-parity test catches missing catalog rows, and this test
        // catches missing attributes plus any field-level drift.
        var assembly = typeof(ServerTools).Assembly;
        var catalogByName = ServerSurfaceCatalog.Tools.ToDictionary(
            entry => entry.Name, entry => entry, StringComparer.Ordinal);

        var drift = new List<string>();
        var missingAttribute = new List<string>();
        foreach (var method in assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)))
        {
            var serverTool = method.GetCustomAttribute<McpServerToolAttribute>();
            if (serverTool is null) continue;

            var metadata = method.GetCustomAttribute<McpToolMetadataAttribute>();
            if (metadata is null)
            {
                missingAttribute.Add($"{serverTool.Name}: declared on {method.DeclaringType?.FullName}.{method.Name} but lacks [McpToolMetadata]");
                continue;
            }

            if (!catalogByName.TryGetValue(serverTool.Name!, out var entry))
            {
                drift.Add($"{serverTool.Name}: catalog entry missing");
                continue;
            }

            if (!string.Equals(entry.Category, metadata.Category, StringComparison.Ordinal))
                drift.Add($"{serverTool.Name}: Category mismatch (attr='{metadata.Category}', catalog='{entry.Category}')");
            if (!string.Equals(entry.SupportTier, metadata.SupportTier, StringComparison.Ordinal))
                drift.Add($"{serverTool.Name}: SupportTier mismatch (attr='{metadata.SupportTier}', catalog='{entry.SupportTier}')");
            if (entry.ReadOnly != metadata.ReadOnly)
                drift.Add($"{serverTool.Name}: ReadOnly mismatch (attr={metadata.ReadOnly}, catalog={entry.ReadOnly})");
            if (entry.Destructive != metadata.Destructive)
                drift.Add($"{serverTool.Name}: Destructive mismatch (attr={metadata.Destructive}, catalog={entry.Destructive})");
            if (!string.Equals(entry.Summary, metadata.Summary, StringComparison.Ordinal))
                drift.Add($"{serverTool.Name}: Summary mismatch");
        }

        Assert.AreEqual(0, missingAttribute.Count,
            "Tools missing [McpToolMetadata]:\n  " + string.Join("\n  ", missingAttribute));
        Assert.AreEqual(0, drift.Count,
            "McpToolMetadata drift detected:\n  " + string.Join("\n  ", drift));
    }

    [TestMethod]
    public async Task ServerInfo_IncludesSurfaceSupportSummary()
    {
        var json = await ServerTools.GetServerInfo(new FakeWorkspaceManager(), new NuGetVersionChecker(new HttpClient()));
        using var doc = JsonDocument.Parse(json);

        Assert.IsTrue(doc.RootElement.TryGetProperty("surface", out var surface));
        Assert.IsTrue(surface.TryGetProperty("tools", out var tools));
        Assert.IsTrue(tools.TryGetProperty("stable", out var stableTools));
        Assert.IsTrue(stableTools.GetInt32() > 0);
        Assert.IsTrue(tools.TryGetProperty("experimental", out _));
    }

    private static string[] GetRegisteredNames<TAttribute>(Assembly assembly)
        where TAttribute : Attribute
    {
        return assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Select(method => method.GetCustomAttribute<TAttribute>())
            .OfType<TAttribute>()
            .Select(GetName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    private static string GetName<TAttribute>(TAttribute attribute)
        where TAttribute : Attribute
    {
        return attribute switch
        {
            McpServerToolAttribute tool => tool.Name ?? throw new InvalidOperationException("Tool name is required."),
            McpServerResourceAttribute resource => resource.Name ?? throw new InvalidOperationException("Resource name is required."),
            McpServerPromptAttribute prompt => prompt.Name ?? throw new InvalidOperationException("Prompt name is required."),
            _ => throw new InvalidOperationException($"Unsupported attribute type: {typeof(TAttribute).Name}")
        };
    }

    private sealed class FakeWorkspaceManager : IWorkspaceManager
    {
        public event Action<string>? WorkspaceClosed { add { } remove { } }
        public event Action<string>? WorkspaceReloaded { add { } remove { } }

        public Task<WorkspaceStatusDto> LoadAsync(string path, CancellationToken ct) => throw new NotSupportedException();
        public Task<WorkspaceStatusDto> ReloadAsync(string workspaceId, CancellationToken ct) => throw new NotSupportedException();
        public bool ContainsWorkspace(string workspaceId) => false;
        public bool IsStale(string workspaceId) => false;
        public bool Close(string workspaceId) => throw new NotSupportedException();
        public IReadOnlyList<WorkspaceStatusDto> ListWorkspaces() => [];
        public WorkspaceStatusDto GetStatus(string workspaceId) => throw new NotSupportedException();
        public Task<WorkspaceStatusDto> GetStatusAsync(string workspaceId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ProjectGraphDto GetProjectGraph(string workspaceId) => throw new NotSupportedException();
        public Task<IReadOnlyList<GeneratedDocumentDto>> GetSourceGeneratedDocumentsAsync(string workspaceId, string? projectName, CancellationToken ct) => throw new NotSupportedException();
        public Task<string?> GetSourceTextAsync(string workspaceId, string filePath, CancellationToken ct) => throw new NotSupportedException();
        public int GetCurrentVersion(string workspaceId) => throw new NotSupportedException();
        public void RestoreVersion(string workspaceId, int version) => throw new NotSupportedException();
        public Solution GetCurrentSolution(string workspaceId) => throw new NotSupportedException();
        public bool TryApplyChanges(string workspaceId, Solution newSolution) => throw new NotSupportedException();
        public Project? GetProject(string workspaceId, string projectNameOrPath) => null;
    }
}
