using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// rebase-changed-document-silent-drop wire evidence (release-policy Rule 1, ADR 0043), both
/// protocol eras. Base: apply of a reviewed edit whose document a sibling <c>delete_file_apply</c>
/// removed returned <c>success:true</c> with no applied files. Change: <c>success:false</c> carrying the
/// existing invalid-preview <c>error</c>, same payload keys as a successful apply. Raw frames are
/// printed as OBSERVED-WIRE (before the assertions, so a run on the base commit still emits them).
/// </summary>
[TestClass]
public sealed class RebaseDroppedIntentWireTests : IsolatedWorkspaceTestBase
{
    private const string _misformatted = "namespace SampleLib\n{\n    public class {0}\n    {\n  public   int   Value  =>  1 ;\n    }\n}\n";

    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    [DataRow("2025-11-25")]
    [DataRow("2026-07-28")]
    public async Task ApplyOfEditWhoseDocumentWasDeletedBySibling_IsRefusedInTheExistingDtoFields(string version)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var doomedPath = workspace.GetPath("SampleLib", "WireDoomed.cs");
        var controlPath = workspace.GetPath("SampleLib", "WireControl.cs");
        await File.WriteAllTextAsync(doomedPath, _misformatted.Replace("{0}", "WireDoomed"), CancellationToken.None);
        await File.WriteAllTextAsync(controlPath, _misformatted.Replace("{0}", "WireControl"), CancellationToken.None);

        await using var harness = await ProductionParityMcpHarness.CreateAsync(
            "rebase-dropped-intent", version,
            services =>
            {
                services.AddSingleton(new SecurityOptions { SanctionedRoots = [workspace.RootPath] });
                // ROSLYNMCP_ON_STALE=off: under the default auto-reload the sibling delete's file-watcher
                // stale flag reloads the workspace at the next call, the reload bump exceeds
                // PreviewStore.DefaultMaxVersionSpan and the token is refused as stale before the rebase
                // ever runs (same DTO, same text). Off keeps the token live so the rebase path is the one tested.
                services.AddSingleton(new ExecutionGateOptions { OnStale = StalenessPolicy.Off });
            });
        var loaded = await harness.Client.CallToolAsync("workspace_load",
            new Dictionary<string, object?> { ["path"] = workspace.SolutionPath, ["autoRestore"] = false },
            cancellationToken: CancellationToken.None);
        var id = JsonNode.Parse(loaded.Content.OfType<TextContentBlock>().Single().Text)!["workspaceId"]!.GetValue<string>();

        var doomedToken = (await CallPayloadAsync(harness, "format_document_preview",
            new() { ["workspaceId"] = id, ["filePath"] = doomedPath }, version, emit: false))["previewToken"]!.GetValue<string>();
        var deleteToken = (await CallPayloadAsync(harness, "delete_file_preview",
            new() { ["workspaceId"] = id, ["filePath"] = doomedPath }, version, emit: false))["previewToken"]!.GetValue<string>();
        var deleted = await CallPayloadAsync(harness, "delete_file_apply",
            new() { ["previewToken"] = deleteToken }, version, emit: false);
        Assert.AreEqual(true, deleted["success"]?.GetValue<bool>(), deleted.ToJsonString());

        // The doomed apply must come straight after the sibling delete: any further preview triggers
        // an auto-reload that pushes the doomed token past PreviewStore.DefaultMaxVersionSpan and
        // turns the frame into PreviewTokenStale instead of the dropped-edit result under test.
        var refusal = await CallPayloadAsync(harness, "format_document_apply",
            new() { ["previewToken"] = doomedToken }, version, emit: true, label: "dropped-edit");

        // Control (shape baseline) minted afterwards, so it is inside its own version span.
        var controlToken = (await CallPayloadAsync(harness, "format_document_preview",
            new() { ["workspaceId"] = id, ["filePath"] = controlPath }, version, emit: false))["previewToken"]!.GetValue<string>();
        var control = await CallPayloadAsync(harness, "format_document_apply",
            new() { ["previewToken"] = controlToken }, version, emit: true, label: "control-success");

        Assert.AreEqual(true, control["success"]?.GetValue<bool>(), control.ToJsonString());
        Assert.AreEqual(false, refusal["success"]?.GetValue<bool>(),
            "Base returned success:true with the reviewed edit dropped: " + refusal.ToJsonString());
        Assert.AreEqual(0, refusal["appliedFiles"]!.AsArray().Count);
        StringAssert.Contains(refusal["error"]!.GetValue<string>(), "Preview token is invalid, expired, or stale");
        var extraKeys = refusal.Select(p => p.Key).Except(control.Select(p => p.Key)).ToArray();
        Assert.IsTrue(extraKeys.All(key => key == "error"),
            "The refusal may add only the existing error field to the successful apply shape: " + string.Join(",", extraKeys));
        Assert.IsFalse(File.Exists(doomedPath));
    }

    private static async Task<JsonObject> CallPayloadAsync(
        InMemoryMcpClientServerHarness harness, string tool, Dictionary<string, object?> args, string version, bool emit, string label = "")
    {
        var prior = harness.RawServerMessages.Count;
        await harness.Client.CallToolAsync(tool, args, cancellationToken: CancellationToken.None);
        var frame = harness.RawServerMessages.Skip(prior).Select(s => JsonNode.Parse(s)!.AsObject())
            .Single(node => node.ContainsKey("id") && (node.ContainsKey("result") || node.ContainsKey("error")));
        if (emit)
        {
            Console.WriteLine("OBSERVED-WIRE:" + version + ":" + label + ":" + frame.ToJsonString());
        }

        Assert.IsNull(frame["error"], frame.ToJsonString());
        var result = frame["result"]!.AsObject();
        Assert.AreNotEqual(true, result["isError"]?.GetValue<bool>(), frame.ToJsonString());
        if (version == "2026-07-28")
            Assert.AreEqual("complete", result["resultType"]!.GetValue<string>());
        else
            Assert.IsFalse(result.ContainsKey("resultType"));
        return JsonNode.Parse(result["content"]!.AsArray().Single()!["text"]!.GetValue<string>())!.AsObject();
    }
}
