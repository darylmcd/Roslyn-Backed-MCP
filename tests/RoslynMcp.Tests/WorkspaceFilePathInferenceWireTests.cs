using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Middleware;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class WorkspaceFilePathInferenceWireTests
{
    private const string ToolName = "symbol_info";
    private const string FilePath = "C:/repos/b/Source.cs";

    [TestMethod]
    public async Task OmittedWorkspaceId_UsesUniqueDocumentOwnerBeforeBinding()
    {
        var manager = new IndexedWorkspaceManager(
            [Status("ws-alpha", "C:/repos/a/A.sln"), Status("ws-beta", "C:/repos/b/B.sln")],
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [FilePath] = ["ws-beta"],
            });
        await using var harness = await CreateHarnessAsync(manager);

        var result = await harness.Client.CallToolAsync(
            ToolName,
            new Dictionary<string, object?>
            {
                ["filePath"] = FilePath,
                ["line"] = 1,
                ["column"] = 1,
            },
            cancellationToken: CancellationToken.None);

        Assert.IsFalse(result.IsError is true);
        var payload = ParseTextContent(result);
        Assert.AreEqual("ws-beta", payload.GetProperty("workspaceId").GetString());
        Assert.AreEqual(FilePath, payload.GetProperty("filePath").GetString());
    }

    [TestMethod]
    public async Task OmittedWorkspaceId_WithAmbiguousOwners_ReturnsBoundedPathRichError()
    {
        var manager = new IndexedWorkspaceManager(
            [Status("ws-zeta", "C:/repos/z/Z.sln"), Status("ws-alpha", "C:/repos/a/A.sln")],
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [FilePath] = ["ws-zeta", "ws-alpha"],
            });
        await using var harness = await CreateHarnessAsync(manager);

        var result = await harness.Client.CallToolAsync(
            ToolName,
            new Dictionary<string, object?>
            {
                ["filePath"] = FilePath,
                ["line"] = 1,
                ["column"] = 1,
            },
            cancellationToken: CancellationToken.None);

        Assert.IsTrue(result.IsError is true);
        var error = ParseTextContent(result);
        Assert.AreEqual("InvalidArgument", error.GetProperty("category").GetString());
        var message = error.GetProperty("message").GetString()!;
        StringAssert.Contains(message, "ws-alpha");
        StringAssert.Contains(message, "C:/repos/a/A.sln");
        StringAssert.Contains(message, "ws-zeta");
        StringAssert.Contains(message, "C:/repos/z/Z.sln");
        Assert.IsTrue(
            message.IndexOf("ws-alpha", StringComparison.Ordinal) <
            message.IndexOf("ws-zeta", StringComparison.Ordinal),
            message);
        Assert.IsTrue(message.Length <= 4096, "Ambiguity text must stay bounded for callers.");
    }

    private static JsonElement ParseTextContent(CallToolResult result)
    {
        using var document = JsonDocument.Parse(((TextContentBlock)result.Content![0]).Text);
        return document.RootElement.Clone();
    }

    private static WorkspaceStatusDto Status(string id, string path) => new(
        WorkspaceId: id,
        LoadedPath: path,
        WorkspaceVersion: 1,
        SnapshotToken: string.Empty,
        LoadedAtUtc: DateTimeOffset.UnixEpoch,
        ProjectCount: 1,
        DocumentCount: 1,
        Projects: [],
        IsLoaded: true,
        IsStale: false,
        WorkspaceDiagnostics: []);

    private static async Task<InMemoryMcpClientServerHarness> CreateHarnessAsync(
        IWorkspaceManager workspaceManager)
    {
        var services = new ServiceCollection();
        services.AddSingleton(workspaceManager);
        services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation
                {
                    Name = "workspace-file-inference-wire-test",
                    Version = "1.0.0",
                };
            })
            .WithTools<SyntheticLocationTools>()
            .WithRequestFilters(static filters =>
                filters.AddCallToolFilter(StructuredCallToolFilter.Create));
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;

        return await InMemoryMcpClientServerHarness.CreateAsync(
            transportName: "workspace-file-inference-wire",
            clientCapabilities: new ClientCapabilities(),
            clientHandlers: new McpClientHandlers(),
            disposalFailureContext: "workspace-file-inference-wire",
            cancellationToken: CancellationToken.None,
            serverOptions: options,
            serverServicesFactory: () => provider);
    }

    [McpServerToolType]
    private sealed class SyntheticLocationTools
    {
        [McpServerTool(Name = ToolName)]
        public static string Locate(string workspaceId, string filePath, int line, int? column = null) =>
            JsonSerializer.Serialize(new { workspaceId, filePath, line, column });
    }

    private sealed class IndexedWorkspaceManager(
        WorkspaceStatusDto[] workspaces,
        IReadOnlyDictionary<string, string[]> owners) : IWorkspaceManager
    {
        public event Action<string>? WorkspaceClosed { add { } remove { } }
        public event Action<string>? WorkspaceReloaded { add { } remove { } }
        public IReadOnlyList<WorkspaceStatusDto> ListWorkspaces() => workspaces;
        public IReadOnlyList<string> FindWorkspaceIdsContainingFile(string filePath) =>
            owners.TryGetValue(filePath, out var matches) ? matches : [];
        public Task<WorkspaceStatusDto> LoadAsync(string path, EvictPolicy evictPolicy, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<WorkspaceStatusDto> ReloadAsync(string workspaceId, CancellationToken ct) =>
            throw new NotSupportedException();
        public bool ContainsWorkspace(string workspaceId) =>
            workspaces.Any(workspace => workspace.WorkspaceId == workspaceId);
        public bool IsStale(string workspaceId) => false;
        public bool Close(string workspaceId) => throw new NotSupportedException();
        public WorkspaceStatusDto GetStatus(string workspaceId) =>
            workspaces.Single(workspace => workspace.WorkspaceId == workspaceId);
        public Task<WorkspaceStatusDto> GetStatusAsync(
            string workspaceId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(GetStatus(workspaceId));
        public ProjectGraphDto GetProjectGraph(string workspaceId) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<GeneratedDocumentDto>> GetSourceGeneratedDocumentsAsync(
            string workspaceId,
            string? projectName,
            CancellationToken ct) => throw new NotSupportedException();
        public Task<string?> GetSourceTextAsync(
            string workspaceId,
            string filePath,
            CancellationToken ct) => throw new NotSupportedException();
        public int GetCurrentVersion(string workspaceId) => GetStatus(workspaceId).WorkspaceVersion;
        public Microsoft.CodeAnalysis.Solution GetCurrentSolution(string workspaceId) =>
            throw new NotSupportedException();
        public Microsoft.CodeAnalysis.Project? GetProject(string workspaceId, string projectNameOrPath) =>
            throw new NotSupportedException();
        public bool TryApplyChanges(string workspaceId, Microsoft.CodeAnalysis.Solution newSolution) =>
            throw new NotSupportedException();
        public void RestoreVersion(string workspaceId, int version) => throw new NotSupportedException();
    }
}
