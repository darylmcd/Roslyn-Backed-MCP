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

        public Task<WorkspaceStatusDto> LoadAsync(string path, CancellationToken ct) => throw new NotSupportedException();
        public Task<WorkspaceStatusDto> ReloadAsync(string workspaceId, CancellationToken ct) => throw new NotSupportedException();
        public bool ContainsWorkspace(string workspaceId) => false;
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
