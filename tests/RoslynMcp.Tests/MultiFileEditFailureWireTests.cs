using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class MultiFileEditFailureWireTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    [DataRow("2025-11-25", false)]
    [DataRow("2026-07-28", false)]
    [DataRow("2025-11-25", true)]
    [DataRow("2026-07-28", true)]
    public async Task ApplyMultiFileEdit_OrdinaryResultPreservesWireShapeAndBatchRecovery(string version, bool allGood)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var paths = new[] { workspace.GetPath("SampleLib", "WireA.cs"), workspace.GetPath("SampleLib", "WireB.cs") };
        var originals = new[] { "public class WireA {}\n", "public class WireB {}\n" };
        for (var i = 0; i < paths.Length; i++) await File.WriteAllTextAsync(paths[i], originals[i]);
        var requests = new ConcurrentQueue<string>();
        await using var harness = await ProductionParityMcpHarness.CreateAsync("multi-file-failure", version, services =>
        {
            services.AddSingleton(new SecurityOptions { SanctionedRoots = [workspace.RootPath] });
            services.AddMcpServer().WithMessageFilters(filters => filters.AddIncomingFilter(next => async (context, ct) =>
            {
                // Full frames as received by the server, rather than an arguments-only pseudo-request.
                var frame = JsonSerializer.Serialize(context.JsonRpcMessage, McpJsonUtilities.DefaultOptions);
                requests.Enqueue(frame);
                Console.WriteLine($"OBSERVED-REQUEST era={version} frame={frame}");
                await next(context, ct);
            }));
        });
        var loaded = await harness.Client.CallToolAsync("workspace_load", new Dictionary<string, object?>
        {
            ["path"] = workspace.SolutionPath,
            ["autoRestore"] = false,
        }, cancellationToken: CancellationToken.None);
        Assert.AreNotEqual(true, loaded.IsError);
        var id = JsonNode.Parse(loaded.Content.OfType<TextContentBlock>().Single().Text)!["workspaceId"]!.GetValue<string>();
        var insertions = new[] { "// valid\n", allGood ? "// valid\n" : "}\n" };
        var prior = harness.RawServerMessages.Count;
        await harness.Client.CallToolAsync("apply_multi_file_edit", new Dictionary<string, object?>
        {
            ["workspaceId"] = id,
            ["fileEdits"] = paths.Select((path, i) => new
            {
                filePath = path,
                edits = new[] { new { startLine = 1, startColumn = 1, endLine = 1, endColumn = 1, newText = insertions[i] } },
            }).ToArray(),
        }, cancellationToken: CancellationToken.None);
        var frame = harness.RawServerMessages.Skip(prior).Select(s => JsonNode.Parse(s)!.AsObject())
            .Single(n => n.ContainsKey("id") && (n.ContainsKey("result") || n.ContainsKey("error")));
        var raw = frame.ToJsonString();
        Console.WriteLine($"OBSERVED-RESPONSE era={version} allGood={allGood} frame={raw}");
        Assert.IsNull(frame["error"], raw);
        var result = frame["result"]!.AsObject();
        Assert.AreNotEqual(true, result["isError"]?.GetValue<bool>(), raw);
        var payload = JsonNode.Parse(result["content"]![0]!["text"]!.GetValue<string>())!.AsObject();
        try
        {
            CollectionAssert.AreEquivalent(new[] { "success", "filesModified", "files", "verification", "_meta" }, payload.Select(p => p.Key).ToArray(), raw);
            Assert.IsNull(payload["verification"], raw);
            Assert.AreEqual("rw-lock", payload["_meta"]!["gateMode"]!.GetValue<string>(), raw);
            Assert.IsFalse(payload.ContainsKey("category") || payload.ContainsKey("message"), raw);
            var files = payload["files"]!.AsArray();
            Assert.HasCount(2, files);
            for (var i = 0; i < paths.Length; i++)
            {
                CollectionAssert.AreEquivalent(new[] { "filePath", "editsApplied", "diff" }, files[i]!.AsObject().Select(p => p.Key).ToArray(), raw);
                Assert.AreEqual(paths[i], files[i]!["filePath"]!.GetValue<string>());
                Assert.AreEqual(i == 0 || allGood ? 1 : 0, files[i]!["editsApplied"]!.GetValue<int>());
                if (i == 0 || allGood) Assert.IsNotNull(files[i]!["diff"]?.GetValue<string>());
                else Assert.IsNull(files[i]!["diff"]);
                Assert.AreEqual((i == 0 || allGood ? insertions[i] : "") + originals[i], await File.ReadAllTextAsync(paths[i]));
            }
            var observed = requests.Select(s => JsonNode.Parse(s)!.AsObject()).ToArray();
            if (version == "2026-07-28")
            {
                Assert.IsTrue(observed.Any(r => r["method"]?.GetValue<string>() == "server/discover"));
                Assert.IsFalse(observed.Any(r => r["method"]?.GetValue<string>() == "initialize"));
                foreach (var request in observed.Where(r => r["method"]?.GetValue<string>() == "tools/call"))
                {
                    var meta = request["params"]!["_meta"]!.AsObject();
                    Assert.AreEqual(version, meta["io.modelcontextprotocol/protocolVersion"]!.GetValue<string>());
                    Assert.IsNotNull(meta["io.modelcontextprotocol/clientInfo"]);
                    Assert.IsNotNull(meta["io.modelcontextprotocol/clientCapabilities"]);
                }
                Assert.AreEqual("complete", result["resultType"]!.GetValue<string>(), raw);
                Assert.IsNotNull(result["_meta"]!["io.modelcontextprotocol/serverInfo"], raw);
            }
            else
            {
                Assert.IsTrue(observed.Any(r => r["method"]?.GetValue<string>() == "initialize" && r["params"]!["protocolVersion"]!.GetValue<string>() == version));
                Assert.IsTrue(observed.Any(r => r["method"]?.GetValue<string>() == "notifications/initialized"));
                Assert.IsFalse(result.ContainsKey("resultType"), raw);
            }
        }
        finally
        {
            var reverted = await harness.Client.CallToolAsync("revert_last_apply", new Dictionary<string, object?> { ["workspaceId"] = id }, cancellationToken: CancellationToken.None);
            Assert.AreNotEqual(true, reverted.IsError);
            Assert.IsTrue(JsonNode.Parse(reverted.Content.OfType<TextContentBlock>().Single().Text)!["reverted"]!.GetValue<bool>());
            for (var i = 0; i < paths.Length; i++) Assert.AreEqual(originals[i], await File.ReadAllTextAsync(paths[i]));
            foreach (var response in harness.RawServerMessages) Console.WriteLine($"OBSERVED-SERVER era={version} frame={response}");
        }
        Assert.AreEqual(allGood, payload["success"]!.GetValue<bool>(), raw);
        Assert.AreEqual(allGood ? 2 : 1, payload["filesModified"]!.GetValue<int>(), raw);
    }
}
