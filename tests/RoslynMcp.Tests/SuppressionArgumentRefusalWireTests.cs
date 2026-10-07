using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class SuppressionArgumentRefusalWireTests : IsolatedWorkspaceTestBase
{
    private const string Sentinel = "PRIVATE-SUPPRESSION-SENTINEL";

    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    [DataRow("2025-11-25", "diagnosticId")]
    [DataRow("2026-07-28", "diagnosticId")]
    [DataRow("2025-11-25", "line")]
    [DataRow("2026-07-28", "line")]
    [DataRow("2025-11-25", "severity")]
    [DataRow("2026-07-28", "severity")]
    [DataRow("2025-11-25", "filePath")]
    [DataRow("2026-07-28", "filePath")]
    public async Task ActualServices_PublicRefusals_PreserveWireAndNeverMutate(string version, string parameter)
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var root = Path.GetDirectoryName(workspace.SolutionPath)!;
        await using var harness = await ProductionParityMcpHarness.CreateAsync(
            "suppression-refusals", version,
            services => services.AddSingleton(new SecurityOptions { SanctionedRoots = [root] }));
        var loaded = await harness.Client.CallToolAsync("workspace_load",
            new Dictionary<string, object?> { ["path"] = workspace.SolutionPath, ["autoRestore"] = false },
            cancellationToken: CancellationToken.None);
        Assert.IsFalse(loaded.IsError == true);
        var id = JsonNode.Parse(loaded.Content.OfType<TextContentBlock>().Single().Text)!["workspaceId"]!.GetValue<string>();
        var path = workspace.GetPath("SampleLib", "AnimalService.cs");
        var before = Snapshot(root);
        foreach (var item in Cases().Where(item => item.Parameter == parameter))
        {
            var arguments = new Dictionary<string, object?>
            {
                ["workspaceId"] = id,
                ["filePath"] = path,
                ["diagnosticId"] = Sentinel,
                ["line"] = 1,
                ["severity"] = "warning",
            };
            arguments[item.Parameter] = item.Value;
            var prior = harness.RawServerMessages.Count;
            await harness.Client.CallToolAsync(item.Tool, arguments, cancellationToken: CancellationToken.None);
            var frame = harness.RawServerMessages.Skip(prior).Select(raw => JsonNode.Parse(raw)!.AsObject())
                .Single(node => node.ContainsKey("id") && (node.ContainsKey("result") || node.ContainsKey("error")));
            var raw = frame.ToJsonString();
            Assert.IsNull(frame["error"], raw);
            var result = frame["result"]!.AsObject();
            Assert.AreEqual(true, result["isError"]?.GetValue<bool>(), raw);
            if (version == "2026-07-28")
                Assert.AreEqual("complete", result["resultType"]?.GetValue<string>(), raw);
            else
                Assert.IsFalse(result.ContainsKey("resultType"), raw);
            var content = result["content"]!.AsArray();
            Assert.HasCount(1, content);
            Assert.AreEqual("text", content[0]!["type"]!.GetValue<string>());
            var payload = JsonNode.Parse(content[0]!["text"]!.GetValue<string>())!.AsObject();
            Assert.AreEqual(true, payload["error"]?.GetValue<bool>(), raw);
            Assert.AreEqual("InvalidArgument", payload["category"]?.GetValue<string>(), raw);
            Assert.AreEqual(item.Tool, payload["tool"]?.GetValue<string>(), raw);
            Assert.AreEqual(item.Identity, payload["exceptionType"]?.GetValue<string>(), raw);
            StringAssert.StartsWith(payload["schemaHint"]!.GetValue<string>(), item.Tool + "(" + item.Parameter + ":");
            StringAssert.Contains(payload["message"]!.GetValue<string>(), item.Correction);
            Assert.IsFalse(raw.Contains(Sentinel, StringComparison.Ordinal), raw);
            Assert.IsFalse(raw.Contains(JsonSerializer.Serialize(root).Trim('"'), StringComparison.Ordinal), raw);
            Assert.IsFalse(raw.Contains("PublicArgument", StringComparison.Ordinal), raw);
            Assert.IsFalse(raw.Contains("stackTrace", StringComparison.Ordinal), raw);
            var after = Snapshot(root);
            CollectionAssert.AreEquivalent(before.Keys.ToArray(), after.Keys.ToArray());
            foreach (var file in before.Keys)
                CollectionAssert.AreEqual(before[file], after[file], item.Tool + ": " + file);
        }
    }

    private static Dictionary<string, byte[]> Snapshot(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(root, path), File.ReadAllBytes);

    private sealed record WireCase(string Tool, string Parameter, object? Value, string Identity, string Correction);

    private static IEnumerable<WireCase> Cases()
    {
        foreach (var tool in new[] { "set_diagnostic_severity", "add_pragma_suppression", "verify_pragma_suppresses", "pragma_scope_widen" })
            yield return new(tool, "diagnosticId", " ", "ArgumentException", "Diagnostic id is required.");
        foreach (var tool in new[] { "add_pragma_suppression", "verify_pragma_suppresses", "pragma_scope_widen" })
            foreach (var line in new[] { 0, -1 })
                yield return new(tool, "line", line, "ArgumentOutOfRangeException", "Line must be 1-based and positive.");
        yield return new("verify_pragma_suppresses", "filePath", " ", "ArgumentException", "File path is required.");
        foreach (var severity in new string?[] { null, "", " ", Sentinel, "default" })
            yield return new("set_diagnostic_severity", "severity", severity, "ArgumentException", "error, warning, suggestion, silent, or none");
    }
}
