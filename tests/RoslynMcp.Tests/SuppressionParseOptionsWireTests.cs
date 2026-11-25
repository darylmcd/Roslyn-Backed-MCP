using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class SuppressionParseOptionsWireTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    [DataRow("2025-11-25")]
    [DataRow("2026-07-28")]
    public async Task ProjectSymbols_CorrectValuesWithinExistingWireDtos(string version)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var sources = new[]
        {
            "#if !PROJECT_DEFINE\n#pragma warning disable CS0168\n#endif\nclass AddTarget { }\n",
            "#if PROJECT_DEFINE\n#pragma warning disable CS0168\n#else\n#pragma warning disable CS0168\n#endif\nclass VerifyTarget { }\n#if PROJECT_DEFINE\n#pragma warning restore CS0168\n#else\n#pragma warning restore CS0168\n#endif\n",
            "#if !PROJECT_DEFINE\n#pragma warning disable CS0168\n#pragma warning restore CS0168\n#endif\nclass WidenTarget { }\n",
        };
        var names = new[] { "AddTarget.cs", "VerifyTarget.cs", "WidenTarget.cs" };
        for (var i = 0; i < names.Length; i++)
            await File.WriteAllTextAsync(workspace.GetPath("SampleLib", names[i]), sources[i]);
        var project = workspace.GetPath("SampleLib", "SampleLib.csproj");
        var xml = await File.ReadAllTextAsync(project);
        await File.WriteAllTextAsync(project, xml.Replace("</PropertyGroup>",
            "<DefineConstants>$(DefineConstants);PROJECT_DEFINE</DefineConstants></PropertyGroup>", StringComparison.Ordinal));
        await using var harness = await ProductionParityMcpHarness.CreateAsync("pragma-parse-options", version,
            services => services.AddSingleton(new SecurityOptions { SanctionedRoots = [workspace.RootPath] }));
        var loaded = await harness.Client.CallToolAsync("workspace_load",
            new Dictionary<string, object?> { ["path"] = workspace.SolutionPath, ["autoRestore"] = false },
            cancellationToken: CancellationToken.None);
        Assert.IsFalse(loaded.IsError == true);
        var id = JsonNode.Parse(loaded.Content.OfType<TextContentBlock>().Single().Text)!["workspaceId"]!.GetValue<string>();
        var tools = new[] { "add_pragma_suppression", "verify_pragma_suppresses", "pragma_scope_widen" };
        var lines = new[] { 4, 6, 5 };
        var payloads = new List<JsonObject>();
        for (var i = 0; i < tools.Length; i++)
        {
            var arguments = new Dictionary<string, object?>
            {
                ["workspaceId"] = id,
                ["filePath"] = workspace.GetPath("SampleLib", names[i]),
                ["diagnosticId"] = "CS0168",
                ["line"] = lines[i],
            };
            var prior = harness.RawServerMessages.Count;
            await harness.Client.CallToolAsync(tools[i], arguments, cancellationToken: CancellationToken.None);
            var frame = harness.RawServerMessages.Skip(prior).Select(raw => JsonNode.Parse(raw)!.AsObject())
                .Single(node => node.ContainsKey("id") && (node.ContainsKey("result") || node.ContainsKey("error")));
            var raw = frame.ToJsonString();
            Console.WriteLine($"OBSERVED-WIRE era={version} tool={tools[i]} request={JsonSerializer.Serialize(arguments)} response={raw}");
            Assert.IsNull(frame["error"], raw);
            var result = frame["result"]!.AsObject();
            Assert.AreNotEqual(true, result["isError"]?.GetValue<bool>(), raw);
            if (version == "2026-07-28") Assert.AreEqual("complete", result["resultType"]?.GetValue<string>(), raw);
            else Assert.IsFalse(result.ContainsKey("resultType"), raw);
            var payload = JsonNode.Parse(result["content"]![0]!["text"]!.GetValue<string>())!.AsObject();
            Assert.IsFalse(payload.ContainsKey("category"), raw);
            payloads.Add(payload);
        }
        CollectionAssert.AreEquivalent(new[] { "success", "filePath", "editsApplied", "changes", "syntaxErrors", "verification", "_meta" }, payloads[0].Select(p => p.Key).ToArray());
        CollectionAssert.AreEquivalent(new[] { "suppresses", "filePath", "line", "diagnosticId", "disableLine", "restoreLine", "reason", "diagnosticFiresAtLine", "diagnosticFiresAtLineUnavailableReason", "_meta" }, payloads[1].Select(p => p.Key).ToArray());
        CollectionAssert.AreEquivalent(new[] { "success", "filePath", "targetLine", "diagnosticId", "disableLine", "originalRestoreLine", "newRestoreLine", "alreadyCovered", "reason", "_meta" }, payloads[2].Select(p => p.Key).ToArray());
        Assert.AreEqual(true, payloads[0]["success"]!.GetValue<bool>());
        Assert.AreEqual(1, payloads[0]["editsApplied"]!.GetValue<int>());
        CollectionAssert.AreEqual(System.Text.Encoding.UTF8.GetBytes(sources[0].Replace("class AddTarget", "#pragma warning disable CS0168\nclass AddTarget", StringComparison.Ordinal)),
            await File.ReadAllBytesAsync(workspace.GetPath("SampleLib", names[0])));
        Assert.AreEqual(true, payloads[1]["suppresses"]!.GetValue<bool>());
        Assert.AreEqual(2, payloads[1]["disableLine"]!.GetValue<int>());
        Assert.AreEqual(8, payloads[1]["restoreLine"]!.GetValue<int>());
        Assert.AreEqual("Line 6 lies inside the '#pragma warning disable CS0168' span (disable 2 → restore 8).", payloads[1]["reason"]!.GetValue<string>());
        CollectionAssert.AreEqual(System.Text.Encoding.UTF8.GetBytes(sources[1]), await File.ReadAllBytesAsync(workspace.GetPath("SampleLib", names[1])));
        Assert.AreEqual(false, payloads[2]["success"]!.GetValue<bool>());
        Assert.IsNull(payloads[2]["disableLine"]);
        Assert.IsNull(payloads[2]["originalRestoreLine"]);
        Assert.IsNull(payloads[2]["newRestoreLine"]);
        Assert.AreEqual(false, payloads[2]["alreadyCovered"]!.GetValue<bool>());
        Assert.AreEqual("No '#pragma warning disable CS0168' found at or before line 5; nothing to widen.", payloads[2]["reason"]!.GetValue<string>());
        CollectionAssert.AreEqual(System.Text.Encoding.UTF8.GetBytes(sources[2]), await File.ReadAllBytesAsync(workspace.GetPath("SampleLib", names[2])));
    }
}
