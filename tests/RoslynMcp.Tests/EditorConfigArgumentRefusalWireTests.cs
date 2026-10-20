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
public sealed class EditorConfigArgumentRefusalWireTests : IsolatedWorkspaceTestBase
{
    private const string Sentinel = "PRIVATE-EDITORCONFIG-SENTINEL";

    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    [DataRow("2025-11-25", "key")]
    [DataRow("2026-07-28", "key")]
    [DataRow("2025-11-25", "value")]
    [DataRow("2026-07-28", "value")]
    public async Task SetEditorConfigOption_NonAssignmentInput_PreservesWireAndNeverMutates(string version, string parameter)
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var root = Path.GetDirectoryName(workspace.SolutionPath)!;
        await File.WriteAllTextAsync(Path.Combine(root, ".editorconfig"), "[*.cs]\nindent_size = 4\n");
        await using var harness = await ProductionParityMcpHarness.CreateAsync(
            "editorconfig-refusals", version,
            services => services.AddSingleton(new SecurityOptions { SanctionedRoots = [root] }));
        var loaded = await harness.Client.CallToolAsync("workspace_load",
            new Dictionary<string, object?> { ["path"] = workspace.SolutionPath, ["autoRestore"] = false },
            cancellationToken: CancellationToken.None);
        Assert.IsFalse(loaded.IsError == true);
        var id = JsonNode.Parse(loaded.Content.OfType<TextContentBlock>().Single().Text)!["workspaceId"]!.GetValue<string>();
        var before = Snapshot(root);

        var arguments = new Dictionary<string, object?>
        {
            ["workspaceId"] = id,
            ["filePath"] = workspace.GetPath("SampleLib", "Dog.cs"),
            ["key"] = parameter == "key" ? Sentinel + "\n[*.vb]\nx = y" : "my_key",
            ["value"] = parameter == "value" ? Sentinel + "\nindent_size = 2" : "v",
        };
        var prior = harness.RawServerMessages.Count;
        await harness.Client.CallToolAsync("set_editorconfig_option", arguments, cancellationToken: CancellationToken.None);
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
        var payload = JsonNode.Parse(content[0]!["text"]!.GetValue<string>())!.AsObject();
        Assert.AreEqual("InvalidArgument", payload["category"]?.GetValue<string>(), raw);
        Assert.AreEqual("set_editorconfig_option", payload["tool"]?.GetValue<string>(), raw);
        Assert.AreEqual("ArgumentException", payload["exceptionType"]?.GetValue<string>(), raw);
        StringAssert.StartsWith(payload["schemaHint"]!.GetValue<string>(), "set_editorconfig_option(" + parameter + ":");
        StringAssert.Contains(payload["message"]!.GetValue<string>(), "single line");
        Assert.IsFalse(raw.Contains(Sentinel, StringComparison.Ordinal), raw);
        Assert.IsFalse(raw.Contains(JsonSerializer.Serialize(root).Trim('"'), StringComparison.Ordinal), raw);
        Assert.IsFalse(raw.Contains("PublicArgument", StringComparison.Ordinal), raw);
        Assert.IsFalse(raw.Contains("stackTrace", StringComparison.Ordinal), raw);
        var after = Snapshot(root);
        CollectionAssert.AreEquivalent(before.Keys.ToArray(), after.Keys.ToArray());
        foreach (var file in before.Keys)
            CollectionAssert.AreEqual(before[file], after[file], file);
    }

    private static Dictionary<string, byte[]> Snapshot(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(root, path), File.ReadAllBytes);
}
