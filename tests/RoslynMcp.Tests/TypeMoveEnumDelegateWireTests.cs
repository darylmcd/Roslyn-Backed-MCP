using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// relocation-support-enum-delegate-move-to-file wire evidence (stable tool, release-policy additive
/// input rule), both protocol eras. Base: a thrown InvalidOperation refusal (isError:true, "type-kind
/// ... not supported"). Change: a success preview whose payload keys equal the class case's keys, so
/// the response shape is unchanged. Raw frames print as OBSERVED-WIRE before any assertion, so a run
/// on the old source captures the base refusal frame.
/// </summary>
[TestClass]
public sealed class TypeMoveEnumDelegateWireTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    [DataRow("2025-11-25", "Enum")]
    [DataRow("2026-07-28", "Enum")]
    [DataRow("2025-11-25", "Delegate")]
    [DataRow("2026-07-28", "Delegate")]
    public async Task MoveTypeToFilePreview_EnumOrDelegate_SuccessShapeEqualsClassShapeOnTheWire(string version, string kind)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var path = workspace.GetPath("SampleLib", "WireKindSrc.cs");
        var declaration = kind == "Enum" ? "public enum WireKind { A, B }" : "public delegate int WireKind(int x);";
        await File.WriteAllTextAsync(path,
            "namespace SampleLib;\npublic class WireKindClass {}\n" + declaration + "\n", CancellationToken.None);

        await using var harness = await ProductionParityMcpHarness.CreateAsync(
            "type-move-enum-delegate", version,
            services => services.AddSingleton(new SecurityOptions { SanctionedRoots = [workspace.RootPath] }));
        var loaded = await harness.Client.CallToolAsync("workspace_load",
            new Dictionary<string, object?> { ["path"] = workspace.SolutionPath, ["autoRestore"] = false },
            cancellationToken: CancellationToken.None);
        Assert.IsFalse(loaded.IsError == true, string.Concat(loaded.Content.OfType<TextContentBlock>().Select(c => c.Text)));
        var id = JsonNode.Parse(loaded.Content.OfType<TextContentBlock>().Single().Text)!["workspaceId"]!.GetValue<string>();

        async Task<JsonObject> CallAsync(string tool, string label, Dictionary<string, object?> arguments)
        {
            var prior = harness.RawServerMessages.Count;
            await harness.Client.CallToolAsync(tool, arguments, cancellationToken: CancellationToken.None);
            var frame = harness.RawServerMessages.Skip(prior).Select(s => JsonNode.Parse(s)!.AsObject())
                .Single(node => node.ContainsKey("id") && (node.ContainsKey("result") || node.ContainsKey("error")));
            Console.WriteLine("OBSERVED-WIRE:" + version + ":" + label + ":" + frame.ToJsonString());
            return frame;
        }

        var classFrame = await CallAsync("move_type_to_file_preview", kind + "-control-class", new()
        {
            ["workspaceId"] = id,
            ["sourceFilePath"] = path,
            ["typeName"] = "WireKindClass",
        });
        var classPayload = SuccessPayload(classFrame, version);

        var frame = await CallAsync("move_type_to_file_preview", kind, new()
        {
            ["workspaceId"] = id,
            ["sourceFilePath"] = path,
            ["typeName"] = "WireKind",
        });
        var payload = SuccessPayload(frame, version);
        CollectionAssert.AreEquivalent(
            classPayload.Select(p => p.Key).ToArray(), payload.Select(p => p.Key).ToArray(), frame.ToJsonString());
        Assert.IsNotNull(payload["previewToken"], frame.ToJsonString());
        var diff = string.Join("\n", payload["changes"]!.AsArray().Select(change => change!["unifiedDiff"]!.GetValue<string>()));
        StringAssert.Contains(diff, kind == "Enum" ? "+public enum WireKind" : "+public delegate int WireKind");

        var applyFrame = await CallAsync("move_type_to_file_apply", kind + "-apply", new()
        {
            ["previewToken"] = payload["previewToken"]!.GetValue<string>(),
        });
        var applied = SuccessPayload(applyFrame, version);
        Assert.AreEqual(true, applied["success"]?.GetValue<bool>(), applyFrame.ToJsonString());
        Assert.IsTrue(File.Exists(workspace.GetPath("SampleLib", "WireKind.cs")));
    }

    private static JsonObject SuccessPayload(JsonObject frame, string version)
    {
        Assert.IsNull(frame["error"], frame.ToJsonString());
        var result = frame["result"]!.AsObject();
        Assert.AreNotEqual(true, result["isError"]?.GetValue<bool>(), frame.ToJsonString());
        if (version == "2026-07-28")
            Assert.AreEqual("complete", result["resultType"]!.GetValue<string>());
        else
            Assert.IsFalse(result.ContainsKey("resultType"));
        var payload = JsonNode.Parse(result["content"]!.AsArray().Single()!["text"]!.GetValue<string>())!.AsObject();
        // A refusal carries a non-null "error"; the apply DTO carries an explicit null on success.
        Assert.IsNull(payload["error"], frame.ToJsonString());
        return payload;
    }
}
