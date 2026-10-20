using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// change_signature_preview and format_range_preview caller refusals must reach the wire as
/// authored, path-free corrections that keep the released category and BCL exceptionType.
/// </summary>
[TestClass]
public sealed class RefactoringRefusalPublicationWireTests : IsolatedWorkspaceTestBase
{
    private const string Sentinel = "PRIVATE-REFUSAL-SENTINEL";

    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    [DataRow("2025-11-25")]
    [DataRow("2026-07-28")]
    public async Task SignatureAndFormatRangeRefusals_PreserveDualEraWireContract(string version)
    {
        await using var harness = await ProductionParityMcpHarness.CreateAsync("refactoring-refusal-publication", version);
        var loaded = await harness.Client.CallToolAsync("workspace_load",
            new Dictionary<string, object?> { ["path"] = SampleSolutionPath, ["autoRestore"] = false },
            cancellationToken: CancellationToken.None);
        Assert.IsFalse(loaded.IsError == true);
        var id = JsonNode.Parse(loaded.Content.OfType<TextContentBlock>().Single().Text)!["workspaceId"]!.GetValue<string>();
        var root = Path.GetDirectoryName(SampleSolutionPath)!;
        var path = Path.Combine(root, "SampleLib", "RefactoringProbe.cs");

        var cases = new List<WireCase>();
        void Signature(string parameter, string correction, Dictionary<string, object?> arguments,
            string category = "InvalidArgument", string exceptionType = "ArgumentException")
        {
            arguments.TryAdd("filePath", path);
            arguments.TryAdd("line", 11);
            arguments.TryAdd("column", 16);
            arguments["workspaceId"] = id;
            cases.Add(new WireCase("change_signature_preview", arguments, parameter, correction, category, exceptionType));
        }
        void Range(int startLine, int startColumn, int endLine, int endColumn, string parameter, string correction)
            => cases.Add(new WireCase("format_range_preview", new()
            {
                ["workspaceId"] = id,
                ["filePath"] = path,
                ["startLine"] = startLine,
                ["startColumn"] = startColumn,
                ["endLine"] = endLine,
                ["endColumn"] = endColumn,
            }, parameter, correction, "InvalidArgument", "ArgumentException"));

        Signature("op", "'op' is not supported", new() { ["op"] = Sentinel });
        Signature("name", "requires parameter 'name'", new() { ["op"] = "add", ["parameterType"] = Sentinel });
        Signature("parameterType", "requires parameter 'parameterType'", new() { ["op"] = "add", ["name"] = Sentinel });
        Signature("position", "0..2", new() { ["op"] = "add", ["name"] = Sentinel, ["parameterType"] = "int", ["position"] = 99 });
        Signature("position", "requires 'position'", new() { ["op"] = "remove", ["name"] = Sentinel, ["position"] = 99 });
        Signature("name", "does not match a parameter", new() { ["op"] = "rename", ["name"] = Sentinel, ["newName"] = "x" });
        Signature("newName", "requires parameter 'newName'", new() { ["op"] = "rename", ["name"] = "a" });
        Signature("newOrder", "requires parameter 'newOrder'", new() { ["op"] = "reorder" });
        Signature("newOrder", "exactly once", new() { ["op"] = "reorder", ["newOrder"] = Sentinel });
        Signature("newOrder", "entry 2", new() { ["op"] = "reorder", ["newOrder"] = "a," + Sentinel });
        Signature("newOrder", "more than once", new() { ["op"] = "reorder", ["newOrder"] = "a,a" });
        Signature("newOrder", "identity permutation", new() { ["op"] = "reorder", ["newOrder"] = "a,b" });
        Signature("metadataName", "parenthesized", new()
        {
            ["op"] = "remove",
            ["filePath"] = null,
            ["line"] = null,
            ["column"] = null,
            ["metadataName"] = "SampleLib.RefactoringProbe.ComputeAndPrint(" + Sentinel + ")",
        });
        Signature("op", "resolved a NamedType symbol",
            new() { ["op"] = "remove", ["name"] = Sentinel, ["line"] = 8, ["column"] = 14 },
            "InvalidOperation", "InvalidOperationException");

        Range(0, 1, 17, 6, "startLine", "'startLine' must be >= 1");
        Range(11, 1, 0, 6, "endLine", "'endLine' must be >= 1");
        Range(11, 0, 17, 6, "startColumn", "'startColumn' must be >= 1");
        Range(11, 1, 17, 0, "endColumn", "'endColumn' must be >= 1");
        Range(99999, 1, 99999, 1, "startLine", "'startLine' (99999) is past the end of the file");
        Range(11, 1, 99999, 1, "endLine", "'endLine' (99999) is past the end of the file");
        Range(17, 1, 11, 6, "endLine", "'endLine' (11) must be >= startLine (17)");
        Range(11, 9, 11, 3, "startColumn", "'startColumn' (9) must be <= endColumn (3)");

        var rootJson = JsonSerializer.Serialize(root).Trim('"');
        foreach (var item in cases)
        {
            var prior = harness.RawServerMessages.Count;
            await harness.Client.CallToolAsync(item.Tool, item.Arguments, cancellationToken: CancellationToken.None);
            var frame = harness.RawServerMessages.Skip(prior).Select(s => JsonNode.Parse(s)!.AsObject())
                .Single(node => node.ContainsKey("id") && (node.ContainsKey("result") || node.ContainsKey("error")));
            var label = version + ":" + item.Tool + ":" + item.Parameter + ":" + item.Correction;

            Assert.IsNull(frame["error"], label + " " + frame.ToJsonString());
            var result = frame["result"]!.AsObject();
            Assert.AreEqual(true, result["isError"]!.GetValue<bool>(), label + " " + frame.ToJsonString());
            if (version == "2026-07-28")
                Assert.AreEqual("complete", result["resultType"]!.GetValue<string>());
            else
                Assert.IsFalse(result.ContainsKey("resultType"));
            var content = result["content"]!.AsArray();
            Assert.HasCount(1, content);
            var payload = JsonNode.Parse(content[0]!["text"]!.GetValue<string>())!.AsObject();
            Assert.AreEqual(item.Category, payload["category"]!.GetValue<string>(), label + " " + frame.ToJsonString());
            Assert.AreEqual(item.Tool, payload["tool"]!.GetValue<string>());
            Assert.AreEqual(item.ExceptionType, payload["exceptionType"]!.GetValue<string>(), label);
            StringAssert.Contains(payload["message"]!.GetValue<string>(), item.Correction, label + " " + frame.ToJsonString());
            if (item.Category == "InvalidArgument")
                StringAssert.StartsWith(payload["schemaHint"]!.GetValue<string>(), item.Tool + "(" + item.Parameter + ":", label);
            Assert.IsFalse(payload.ContainsKey("paramName"), label);
            Assert.IsFalse(payload.ContainsKey("correlationId"), label);
            var raw = frame.ToJsonString();
            Assert.IsFalse(raw.Contains(Sentinel, StringComparison.Ordinal), label + " " + raw);
            Assert.IsFalse(raw.Contains(rootJson, StringComparison.Ordinal), label + " " + raw);
        }
    }

    private sealed record WireCase(
        string Tool, Dictionary<string, object?> Arguments, string Parameter, string Correction,
        string Category, string ExceptionType);
}
