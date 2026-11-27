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

    [TestMethod]
    [DataRow("2025-11-25", "empty")]
    [DataRow("2026-07-28", "empty")]
    [DataRow("2025-11-25", "prior")]
    [DataRow("2026-07-28", "prior")]
    [DataRow("2025-11-25", "mixed")]
    [DataRow("2026-07-28", "mixed")]
    public async Task FailedBatchUndoRetainsOnlyRealEffectsOnTheWire(string version, string scenario)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var priorPath = workspace.GetPath("SampleLib", "Dog.cs");
        var editedPath = workspace.GetPath("SampleLib", "Cat.cs");
        var rejectedPath = workspace.GetPath("SampleLib", "IAnimal.cs");
        var priorBytes = await File.ReadAllBytesAsync(priorPath);
        var editedBytes = await File.ReadAllBytesAsync(editedPath);
        var rejectedBytes = await File.ReadAllBytesAsync(rejectedPath);
        await using var harness = await ProductionParityMcpHarness.CreateAsync("failed-apply-undo", version,
            services => services.AddSingleton(new SecurityOptions { SanctionedRoots = [workspace.RootPath] }));

        async Task<JsonObject> CallAsync(string name, Dictionary<string, object?> args)
        {
            var before = harness.RawServerMessages.Count;
            await harness.Client.CallToolAsync(name, args, cancellationToken: CancellationToken.None);
            var frame = harness.RawServerMessages.Skip(before).Select(s => JsonNode.Parse(s)!.AsObject())
                .Single(n => n.ContainsKey("id") && (n.ContainsKey("result") || n.ContainsKey("error")));
            var raw = frame.ToJsonString();
            Console.WriteLine($"OBSERVED-RESPONSE era={version} scenario={scenario} name={name} frame={raw}");
            Assert.IsNull(frame["error"], raw);
            var result = frame["result"]!.AsObject();
            Assert.AreNotEqual(true, result["isError"]?.GetValue<bool>(), raw);
            return JsonNode.Parse(result["content"]![0]!["text"]!.GetValue<string>())!.AsObject();
        }

        var load = await CallAsync("workspace_load", new() { ["path"] = workspace.SolutionPath, ["autoRestore"] = false });
        var id = load["workspaceId"]!.GetValue<string>();
        var workspaceArgs = new Dictionary<string, object?> { ["workspaceId"] = id };
        var emptyUndo = await CallAsync("revert_last_apply", workspaceArgs);
        Assert.IsFalse(emptyUndo["reverted"]!.GetValue<bool>());
        if (scenario == "prior")
        {
            var a = await CallAsync("apply_text_edit", new(workspaceArgs)
            {
                ["filePath"] = priorPath,
                ["edits"] = new[] { Insert("// prior A\n") },
            });
            Assert.IsTrue(a["success"]!.GetValue<bool>());
            CollectionAssert.AreNotEqual(priorBytes, await File.ReadAllBytesAsync(priorPath));
        }

        var mixed = scenario == "mixed";
        var b = await CallAsync("apply_multi_file_edit", new(workspaceArgs)
        {
            ["fileEdits"] = new[]
            {
                new { filePath = editedPath, edits = new[] { Insert(mixed ? "// mixed B\n" : "}\n") } },
                new { filePath = rejectedPath, edits = new[] { Insert("}\n") } },
            },
        });
        Assert.IsFalse(b["success"]!.GetValue<bool>());
        Assert.AreEqual(mixed ? 1 : 0, b["filesModified"]!.GetValue<int>());
        CollectionAssert.AreEqual(rejectedBytes, await File.ReadAllBytesAsync(rejectedPath));
        if (mixed)
            CollectionAssert.AreNotEqual(editedBytes, await File.ReadAllBytesAsync(editedPath));
        else
            CollectionAssert.AreEqual(editedBytes, await File.ReadAllBytesAsync(editedPath));

        var changes = (await CallAsync("workspace_changes", workspaceArgs))["changes"]!.AsArray();
        Assert.HasCount(scenario == "empty" ? 0 : 1, changes);
        if (mixed)
        {
            CollectionAssert.AreEqual(new[] { editedPath },
                changes[0]!["affectedFiles"]!.AsArray().Select(path => path!.GetValue<string>()).ToArray());
            var result = await CallAsync("revert_apply_by_sequence", new(workspaceArgs)
            {
                ["sequenceNumber"] = changes[0]!["sequenceNumber"]!.GetValue<int>(),
            });
            Assert.IsTrue(result["reverted"]!.GetValue<bool>());
            CollectionAssert.AreEqual(editedBytes, await File.ReadAllBytesAsync(editedPath));
            CollectionAssert.AreEqual(rejectedBytes, await File.ReadAllBytesAsync(rejectedPath));
            return;
        }

        var last = await CallAsync("revert_last_apply", workspaceArgs);
        last.Remove("_meta");
        emptyUndo.Remove("_meta");
        if (scenario == "empty")
        {
            Assert.IsTrue(JsonNode.DeepEquals(emptyUndo, last), last.ToJsonString());
        }
        else
        {
            CollectionAssert.AreEquivalent(new[] { "reverted", "revertedOperation", "appliedAtUtc" },
                last.Select(field => field.Key).ToArray());
            Assert.IsTrue(last["reverted"]!.GetValue<bool>());
            Assert.AreEqual("Apply text edit to Dog.cs", last["revertedOperation"]!.GetValue<string>());
            CollectionAssert.AreEqual(priorBytes, await File.ReadAllBytesAsync(priorPath));
        }

        var phantom = await CallAsync("revert_apply_by_sequence", new(workspaceArgs)
        {
            ["sequenceNumber"] = scenario == "empty" ? 1 : changes[0]!["sequenceNumber"]!.GetValue<int>() + 1,
        });
        Assert.IsFalse(phantom["reverted"]!.GetValue<bool>());
        Assert.AreEqual("unknown-sequence", phantom["reason"]!.GetValue<string>());
    }

    private static object Insert(string text) =>
        new { startLine = 1, startColumn = 1, endLine = 1, endColumn = 1, newText = text };

}
