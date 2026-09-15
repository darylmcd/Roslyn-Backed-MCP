using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio;
using RoslynMcp.Host.Stdio.Middleware;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ReferenceResponsePagerTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [TestMethod]
    [DataRow(600, false)]
    [DataRow(3_000, true)]
    public async Task TerminalProjection_UsesConfiguredBudgetAndClassifiesOversizedPages(int previewLength, bool expectedError)
    {
        const int budget = 2_048;
        await using var harness = await InMemoryMcpClientServerHarness.CreateAsync(
            transportName: "reference-page-budget",
            clientCapabilities: new ClientCapabilities(),
            clientHandlers: new McpClientHandlers(),
            disposalFailureContext: "reference-page-budget",
            cancellationToken: CancellationToken.None,
            serverServicesFactory: () => new ServiceCollection()
                .AddSingleton(new ReferenceResponsePager(budget)).BuildServiceProvider());
        var context = new RequestContext<CallToolRequestParams>(
            harness.Server,
            new JsonRpcRequest { Method = RequestMethods.ToolsCall },
            new CallToolRequestParams { Name = "find_references" });
        var item = new LocationDto("reference.cs", 1, 1, 1, 2, null, new string('x', previewLength));
        var json = JsonSerializer.Serialize(new
        {
            count = 4,
            totalCount = 4,
            hasMore = false,
            offset = 0,
            limit = 4,
            summary = false,
            items = Enumerable.Repeat(item, 4).ToArray(),
        }, JsonDefaults.Indented);
        using var metrics = AmbientGateMetrics.BeginRequest();
        var result = await StructuredResultProjector.ExecuteAsync(
            context, "find_references", null, null, Stopwatch.StartNew(),
            () => ValueTask.FromResult(new StructuredDispatchPipeline.DispatchOutcome(
                new CallToolResult { Content = [new TextContentBlock { Text = json }] }, false)));
        Assert.AreEqual(expectedError, result.IsError == true);
        var text = ((TextContentBlock)result.Content[0]).Text;
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        Assert.IsTrue(root.TryGetProperty("_meta", out _));
        if (expectedError)
        {
            Assert.AreEqual("InvalidArgument", root.GetProperty("category").GetString());
            StringAssert.Contains(root.GetProperty("message").GetString(), ReferenceResponsePager.EnvironmentVariableName);
        }
        else
        {
            Assert.IsTrue(Encoding.UTF8.GetByteCount(text) <= budget);
            Assert.IsTrue(root.GetProperty("hasMore").GetBoolean());
            Assert.AreEqual(root.GetProperty("count").GetInt32(), root.GetProperty("nextOffset").GetInt32());
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FindReferences_ByteLimitedPagesEnumerateEveryReferenceOnce(bool summary)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var path = workspace.GetPath("SampleLib", "ReferenceFanOut.cs");
        var source = "namespace Paging; public class Target { } public class Uses {\n"
            + string.Join('\n', Enumerable.Range(0, 150).Select(i => $"public Target Référence{i} = null!;"))
            + "\n}";
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync();
        var expected = await ReferenceService.FindReferencesAsync(
            workspace.WorkspaceId, SymbolLocator.ByMetadataName("Paging.Target"), CancellationToken.None, summary);
        Assert.AreEqual(150, expected.Count);

        const int budget = 4_096;
        var pager = new ReferenceResponsePager(budget);
        var actual = new List<LocationDto>();
        var offset = 0;
        var pageCount = 0;
        do
        {
            var json = await SymbolTools.FindReferences(
                requestContext: null,
                workspaceManager: WorkspaceManager,
                gate: WorkspaceExecutionGate,
                referenceService: ReferenceService,
                workspaceId: workspace.WorkspaceId,
                metadataName: "Paging.Target",
                limit: 200,
                offset: offset,
                summary: summary);
            json = Project(pager, json);
            Assert.IsTrue(Encoding.UTF8.GetByteCount(json) <= budget);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            Assert.IsTrue(root.TryGetProperty("_meta", out _), "The ceiling must include final metadata.");
            var items = root.GetProperty("items").Deserialize<LocationDto[]>(JsonDefaults.Indented)!;
            Assert.IsTrue(items.Length > 0);
            Assert.AreEqual(items.Length, root.GetProperty("count").GetInt32());
            Assert.AreEqual(expected.Count, root.GetProperty("totalCount").GetInt32());
            Assert.AreEqual(offset, root.GetProperty("offset").GetInt32());
            Assert.AreEqual(200, root.GetProperty("limit").GetInt32());
            Assert.AreEqual(summary, root.GetProperty("summary").GetBoolean());
            actual.AddRange(items);
            pageCount++;
            if (!root.GetProperty("hasMore").GetBoolean())
            {
                Assert.AreEqual(JsonValueKind.Null, root.GetProperty("nextOffset").ValueKind);
                break;
            }

            var next = root.GetProperty("nextOffset").GetInt32();
            Assert.AreEqual(offset + items.Length, next);
            offset = next;
            Assert.IsTrue(pageCount <= expected.Count, "Continuation must always make progress.");
        } while (true);

        Assert.IsTrue(pageCount > 1, "The byte ceiling must shorten the requested page.");
        CollectionAssert.AreEqual(expected.ToArray(), actual.ToArray());
    }

    [TestMethod]
    public void Serialize_ExactByteBoundaryAndCallerLimitAreRespected()
    {
        var item = new LocationDto("référence.cs", 1, 1, 1, 2, "成员", new string('é', 200));
        LocationDto[] results = [item, item with { StartLine = 2 }, item with { StartLine = 3 }];
        var full = Serialize(new ReferenceResponsePager(), results, 0, 2, false);
        var exactBytes = Encoding.UTF8.GetByteCount(full);
        var exact = Serialize(new ReferenceResponsePager(exactBytes), results, 0, 2, false);
        Assert.AreEqual(full, exact);
        using var shortened = JsonDocument.Parse(Serialize(new ReferenceResponsePager(exactBytes - 1), results, 0, 2, false));
        Assert.AreEqual(1, shortened.RootElement.GetProperty("count").GetInt32());
        Assert.AreEqual(1, shortened.RootElement.GetProperty("nextOffset").GetInt32());
        using var limited = JsonDocument.Parse(Serialize(new ReferenceResponsePager(), results, 1, 1, false));
        Assert.AreEqual(1, limited.RootElement.GetProperty("count").GetInt32());
        Assert.AreEqual(2, limited.RootElement.GetProperty("nextOffset").GetInt32());
    }

    [TestMethod]
    public void Serialize_EmptyAndPastEndPagesAreTerminalWithoutOffsetOverflow()
    {
        var pager = new ReferenceResponsePager();
        foreach (var offset in new[] { 0, int.MaxValue })
        {
            using var document = JsonDocument.Parse(Serialize(pager, [], offset, 1000, true));
            Assert.AreEqual(0, document.RootElement.GetProperty("count").GetInt32());
            Assert.IsFalse(document.RootElement.GetProperty("hasMore").GetBoolean());
            Assert.AreEqual(JsonValueKind.Null, document.RootElement.GetProperty("nextOffset").ValueKind);
        }
    }

    [TestMethod]
    public void Serialize_OversizedFirstItemRefusesWithoutReturningANonAdvancingPage()
    {
        var item = new LocationDto("reference.cs", 1, 1, 1, 2, null, new string('x', 40_000));
        var error = Assert.ThrowsExactly<PublicArgumentException>(() => Serialize(new ReferenceResponsePager(), [item], 0, 10, false));
        StringAssert.Contains(error.Message, "summary=true");
        StringAssert.Contains(error.Message, ReferenceResponsePager.EnvironmentVariableName);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("invalid")]
    [DataRow("0")]
    [DataRow("1023")]
    [DataRow("2147483648")]
    public void FromEnvironment_InvalidOrMissingValueUsesDefault(string? value) =>
        Assert.AreSame(ReferenceResponsePager.Default, ReferenceResponsePager.FromEnvironment(value));

    [TestMethod]
    public void FromEnvironment_CustomBudgetControlsSerialization()
    {
        var item = new LocationDto("reference.cs", 1, 1, 1, 2, null, new string('x', 2_000));
        Assert.ThrowsExactly<PublicArgumentException>(() => Serialize(ReferenceResponsePager.FromEnvironment("1024"), [item], 0, 10, false));
        using var document = JsonDocument.Parse(Serialize(ReferenceResponsePager.FromEnvironment("4096"), [item], 0, 10, false));
        Assert.AreEqual(1, document.RootElement.GetProperty("count").GetInt32());
    }

    private static string Serialize(
        ReferenceResponsePager pager, IReadOnlyList<LocationDto> results, int offset, int limit, bool summary)
    {
        var items = results.Skip(offset).Take(limit).ToArray();
        var json = JsonSerializer.Serialize(new
        {
            count = items.Length,
            totalCount = results.Count,
            hasMore = (long)offset + items.Length < results.Count,
            offset,
            limit,
            summary,
            items,
        }, JsonDefaults.Indented);
        return Project(pager, json);
    }

    private static string Project(ReferenceResponsePager pager, string json)
    {
        using var metrics = AmbientGateMetrics.BeginRequest();
        AmbientGateMetrics.Current!.GateMode = "rw-lock";
        AmbientGateMetrics.Current.ElapsedMs = long.MaxValue;
        AmbientGateMetrics.Current.StaleAction = "auto-reloaded";
        var result = StructuredResultProjector.InjectMetaIntoContent(
            new CallToolResult { Content = [new TextContentBlock { Text = json }] },
            "find_references",
            responsePager: pager);
        return ((TextContentBlock)result.Content[0]).Text;
    }
}
