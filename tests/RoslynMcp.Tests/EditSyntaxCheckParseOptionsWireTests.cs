using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// edit-syntax-check-project-parse-options: raw-wire contract for the stable <c>apply_text_edit</c>
/// on both protocol eras. A project-context refusal must be an ordinary result frame carrying the
/// existing <c>TextEditResultDto</c> fields (never the tool-error envelope), and a plain edit must
/// keep the exact success shape. Every frame is echoed as <c>OBSERVED-WIRE</c> for before/after capture.
/// </summary>
[TestClass]
public sealed class EditSyntaxCheckParseOptionsWireTests : IsolatedWorkspaceTestBase
{
    private const string Define = "<DefineConstants>$(DefineConstants);PROJECT_DEFINE</DefineConstants>";

    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    [DataRow("2025-11-25")]
    [DataRow("2026-07-28")]
    public async Task ApplyTextEdit_ActiveSymbolMalformedEdit_IsSuccessFalseDtoNotAToolError(string version)
    {
        var (result, raw, onDisk, original) = await CallAsync(
            version, "#if PROJECT_DEFINE\npublic class ParseTarget { }\n#endif\n", Define,
            new { startLine = 2, startColumn = 1, endLine = 2, endColumn = 1, newText = "}\n" });

        Assert.AreNotEqual(true, result.Frame["isError"]?.GetValue<bool>(), raw);
        Assert.IsFalse(raw.Contains("\"category\"", StringComparison.Ordinal), raw);
        var payload = result.Payload;
        Assert.AreEqual(false, payload["success"]?.GetValue<bool>(), raw);
        Assert.AreEqual(0, payload["editsApplied"]?.GetValue<int>(), raw);
        Assert.IsFalse(payload.ContainsKey("verification") && payload["verification"] is not null, raw);
        var errors = payload["syntaxErrors"]!.AsArray();
        Assert.IsNotEmpty(errors, raw);
        CollectionAssert.AreEquivalent(new[] { "line", "column", "message" },
            errors[0]!.AsObject().Select(p => p.Key).ToArray(), raw);
        Assert.AreEqual(2, errors[0]!["line"]!.GetValue<int>(), raw);
        AssertCommonShape(payload, version, result.Frame, raw);
        Assert.AreEqual(original, onDisk, "A refused edit must not touch the file.");
    }

    [TestMethod]
    [DataRow("2025-11-25")]
    [DataRow("2026-07-28")]
    public async Task ApplyTextEdit_ErrorDirectiveDeactivatedByProjectSymbol_IsAnOrdinarySuccess(string version)
    {
        var (result, raw, onDisk, _) = await CallAsync(
            version, "#if !PROJECT_DEFINE\n#error unsupported\n#endif\npublic class ParseTarget { }\n", Define,
            new { startLine = 4, startColumn = 1, endLine = 4, endColumn = 1, newText = "// benign\n" });

        Assert.AreNotEqual(true, result.Frame["isError"]?.GetValue<bool>(), raw);
        Assert.AreEqual(true, result.Payload["success"]?.GetValue<bool>(), raw);
        Assert.AreEqual(1, result.Payload["editsApplied"]?.GetValue<int>(), raw);
        AssertCommonShape(result.Payload, version, result.Frame, raw);
        StringAssert.Contains(onDisk, "// benign");
    }

    [TestMethod]
    [DataRow("2025-11-25")]
    [DataRow("2026-07-28")]
    public async Task ApplyTextEdit_PlainEditWithoutProjectOptions_KeepsTheSingleContextShape(string version)
    {
        var (result, raw, onDisk, _) = await CallAsync(
            version, "public class ParseTarget { }\n", projectProperties: null,
            new { startLine = 1, startColumn = 1, endLine = 1, endColumn = 1, newText = "// plain\n" });

        Assert.AreNotEqual(true, result.Frame["isError"]?.GetValue<bool>(), raw);
        Assert.AreEqual(true, result.Payload["success"]?.GetValue<bool>(), raw);
        CollectionAssert.AreEquivalent(new[] { "success", "filePath", "editsApplied", "changes", "syntaxErrors", "verification", "_meta" },
            result.Payload.Select(p => p.Key).ToArray(), raw);
        AssertCommonShape(result.Payload, version, result.Frame, raw);
        StringAssert.StartsWith(onDisk, "// plain\n");
    }

    [TestMethod]
    [DataRow("2025-11-25")]
    [DataRow("2026-07-28")]
    public async Task ApplyTextEdit_PlainMalformedEdit_StillRefusesWithTheSameShape(string version)
    {
        // Single-context output must be unchanged: the default-options refusal keeps its message and position.
        var (result, raw, _, _) = await CallAsync(
            version, "public class ParseTarget { }\n", projectProperties: null,
            new { startLine = 1, startColumn = 1, endLine = 1, endColumn = 1, newText = "}\n" });

        Assert.AreEqual(false, result.Payload["success"]?.GetValue<bool>(), raw);
        var errors = result.Payload["syntaxErrors"]!.AsArray();
        Assert.HasCount(1, errors, raw);
        Assert.AreEqual(1, errors[0]!["line"]!.GetValue<int>(), raw);
        StringAssert.Contains(errors[0]!["message"]!.GetValue<string>(), "Type or namespace definition, or end-of-file expected");
    }

    private static void AssertCommonShape(JsonObject payload, string version, JsonObject frame, string raw)
    {
        Assert.IsNull(frame["error"], raw);
        Assert.IsTrue(payload.ContainsKey("filePath"), raw);
        Assert.IsTrue(payload.ContainsKey("changes"), raw);
        if (version == "2026-07-28")
            Assert.AreEqual("complete", frame["result"]!["resultType"]?.GetValue<string>(), raw);
        else
            Assert.IsFalse(frame["result"]!.AsObject().ContainsKey("resultType"), raw);
    }

    private sealed record WireResult(JsonObject Frame, JsonObject Payload);

    private static async Task<(WireResult Result, string Raw, string OnDisk, string Original)> CallAsync(
        string version, string source, string? projectProperties, object edit)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var target = workspace.GetPath("SampleLib", "ParseTarget.cs");
        await File.WriteAllTextAsync(target, source);
        if (projectProperties is not null)
        {
            var projectPath = workspace.GetPath("SampleLib", "SampleLib.csproj");
            var xml = await File.ReadAllTextAsync(projectPath);
            await File.WriteAllTextAsync(projectPath,
                xml.Replace("</PropertyGroup>", projectProperties + "</PropertyGroup>", StringComparison.Ordinal));
        }

        var root = Path.GetDirectoryName(workspace.SolutionPath)!;
        await using var harness = await ProductionParityMcpHarness.CreateAsync(
            "edit-parse-options", version,
            services => services.AddSingleton(new SecurityOptions { SanctionedRoots = [root] }));
        var loaded = await harness.Client.CallToolAsync("workspace_load",
            new Dictionary<string, object?> { ["path"] = workspace.SolutionPath, ["autoRestore"] = false },
            cancellationToken: CancellationToken.None);
        Assert.IsFalse(loaded.IsError == true);
        var id = JsonNode.Parse(loaded.Content.OfType<TextContentBlock>().Single().Text)!["workspaceId"]!.GetValue<string>();

        var arguments = new Dictionary<string, object?>
        {
            ["workspaceId"] = id,
            ["filePath"] = target,
            ["edits"] = new[] { edit },
        };
        var prior = harness.RawServerMessages.Count;
        await harness.Client.CallToolAsync("apply_text_edit", arguments, cancellationToken: CancellationToken.None);
        var frame = harness.RawServerMessages.Skip(prior).Select(raw => JsonNode.Parse(raw)!.AsObject())
            .Single(node => node.ContainsKey("id") && (node.ContainsKey("result") || node.ContainsKey("error")));
        var rawFrame = frame.ToJsonString();
        Console.WriteLine($"OBSERVED-WIRE era={version} request={JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(arguments))!.ToJsonString()} response={rawFrame}");
        var content = frame["result"]!["content"]!.AsArray();
        var payload = JsonNode.Parse(content[0]!["text"]!.GetValue<string>())!.AsObject();
        return (new WireResult(frame, payload), rawFrame, await File.ReadAllTextAsync(target), source);
    }
}
