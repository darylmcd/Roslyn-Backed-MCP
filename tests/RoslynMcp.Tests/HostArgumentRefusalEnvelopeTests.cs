using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class HostArgumentRefusalEnvelopeTests : IsolatedWorkspaceTestBase
{
    private const string Sentinel = "PRIVATE-LOCATOR-SENTINEL";
    private static string WorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        WorkspaceId = await GetOrLoadWorkspaceIdAsync(SampleSolutionPath, CancellationToken.None);
    }

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    [DataRow(false, false, false, "filePath", "Provide one of:")]
    [DataRow(true, false, false, "line", "Missing: line, column.")]
    [DataRow(false, true, false, "filePath", "Missing: filePath, column.")]
    [DataRow(false, false, true, "filePath", "Missing: filePath, line.")]
    [DataRow(true, true, false, "column", "Missing: column.")]
    [DataRow(true, false, true, "line", "Missing: line.")]
    [DataRow(false, true, true, "filePath", "Missing: filePath.")]
    public void LocatorRefusal_NamesEveryMissingCoordinate(
        bool path, bool line, bool column, string parameter, string correction)
    {
        var ex = Assert.ThrowsExactly<PublicArgumentException>(() => SymbolLocatorFactory.Create(
            path ? Sentinel : null, line ? 1 : null, column ? 1 : null));
        AssertRefusal(ex, "symbol_info", parameter, correction);
        StringAssert.Contains(ex.PublicMessage, "filePath");
        StringAssert.Contains(ex.PublicMessage, "line");
        StringAssert.Contains(ex.PublicMessage, "column");
        if (!path && !line && !column)
        {
            StringAssert.Contains(ex.PublicMessage, "symbolHandle");
            StringAssert.Contains(ex.PublicMessage, "metadataName");
        }
    }

    [TestMethod]
    public void LocatorPrecedence_PreservesHandleThenMetadataThenSource()
    {
        var handle = SymbolLocatorFactory.Create(Sentinel, 1, 2, "handle", "metadata");
        Assert.AreEqual("handle", handle.SymbolHandle);
        Assert.IsFalse(handle.HasMetadataName);
        Assert.IsFalse(handle.HasSourceLocation);
        var metadata = SymbolLocatorFactory.Create(Sentinel, 1, 2, metadataName: "metadata");
        Assert.AreEqual("metadata", metadata.MetadataName);
        Assert.IsFalse(metadata.HasSourceLocation);
        var source = SymbolLocatorFactory.Create(Sentinel, 1, 2);
        Assert.AreEqual(Sentinel, source.FilePath);
        Assert.AreEqual(1, source.Line);
        Assert.AreEqual(2, source.Column);
    }

    [TestMethod]
    [DataRow("limit", 0, "greater than 0")]
    [DataRow("limit", -1, "greater than 0")]
    [DataRow("offset", -1, "non-negative")]
    [DataRow("failuresLimit", 0, "greater than 0")]
    [DataRow("failuresLimit", -1, "greater than 0")]
    [DataRow("failuresOffset", -1, "non-negative")]
    public async Task ValidationRefusal_PreservesParameterAndBounds(string parameter, int value, string correction)
    {
        var isRun = parameter.StartsWith("failures", StringComparison.Ordinal);
        var ex = await Assert.ThrowsExactlyAsync<PublicArgumentException>(() => isRun
            ? ValidationTools.RunTests(WorkspaceExecutionGate, TestRunnerService, WorkspaceId,
                projectName: Sentinel, filter: Sentinel,
                failuresOffset: parameter == "failuresOffset" ? value : 0,
                failuresLimit: parameter == "failuresLimit" ? value : 25)
            : ValidationTools.DiscoverTests(WorkspaceExecutionGate, TestDiscoveryService, WorkspaceId,
                projectName: Sentinel, nameFilter: Sentinel,
                offset: parameter == "offset" ? value : 0,
                limit: parameter == "limit" ? value : 50));
        AssertRefusal(ex, isRun ? "test_run" : "test_discover", parameter, correction);
    }

    [TestMethod]
    public async Task RealTools_EmitPublicCorrectionsWithoutCallerData()
    {
        await using var harness = await ProductionParityMcpHarness.CreateAsync("host-argument-refusals", "2025-11-25");
        var (workspaceId, filePath, lineCount) = await LoadWireWorkspaceAsync(harness);

        for (var mask = 0; mask < 7; mask++)
        {
            var args = new Dictionary<string, object?> { ["workspaceId"] = workspaceId };
            if ((mask & 1) != 0) args["filePath"] = Sentinel;
            if ((mask & 2) != 0) args["line"] = 1;
            if ((mask & 4) != 0) args["column"] = 1;
            var missing = new List<string>();
            if ((mask & 1) == 0) missing.Add("filePath");
            if ((mask & 2) == 0) missing.Add("line");
            if ((mask & 4) == 0) missing.Add("column");
            await AssertWireRefusalAsync(harness, "symbol_info", args, missing[0],
                mask == 0 ? "Provide one of:" : $"Missing: {string.Join(", ", missing)}.");
        }

        foreach (var (tool, parameter, value, correction) in new[]
        {
            ("test_discover", "limit", 0, "limit must be greater than 0."),
            ("test_discover", "offset", -1, "offset must be non-negative."),
            ("test_run", "failuresLimit", 0, "failuresLimit must be greater than 0."),
            ("test_run", "failuresOffset", -1, "failuresOffset must be non-negative."),
            ("impact_analysis", "declarationsLimit", 0, "declarationsLimit must be >= 1."),
            ("get_source_text", "maxChars", 0, "maxChars must be greater than 0 (got 0)."),
            ("get_source_text", "startLine", 0, "startLine must be >= 1 (got 0)."),
            ("get_source_text", "endLine", 0, "endLine must be >= 1 (got 0)."),
            ("get_source_text", "startLine", lineCount + 1, $"startLine ({lineCount + 1}) is past the end of the file ({lineCount} lines)."),
        })
        {
            await AssertWireRefusalAsync(harness, tool,
                new Dictionary<string, object?>
                {
                    ["workspaceId"] = workspaceId,
                    ["filePath"] = tool == "get_source_text" ? filePath : Sentinel,
                    [parameter] = value,
                    ["metadataName"] = Sentinel,
                    ["symbolHandle"] = Sentinel,
                }, parameter, correction);
        }

        await AssertWireRefusalAsync(harness, "get_source_text",
            new Dictionary<string, object?> { ["workspaceId"] = workspaceId, ["filePath"] = Sentinel, ["startLine"] = 5, ["endLine"] = 2 },
            "startLine", "startLine (5) must be <= endLine (2).");

        foreach (var parameter in new[] { "line", "column" })
        {
            var alias = parameter == "line" ? "startLine" : "startColumn";
            var other = parameter == "line" ? "column" : "line";
            var args = new Dictionary<string, object?> { ["workspaceId"] = workspaceId, ["filePath"] = Sentinel, ["diagnosticId"] = Sentinel, [other] = 1 };
            await AssertWireRefusalAsync(harness, "diagnostic_details", args, parameter,
                $"Missing required parameter '{parameter}' (or its alias '{alias}').");
            args[parameter] = 1;
            args[alias] = 2;
            await AssertWireRefusalAsync(harness, "diagnostic_details", args, parameter,
                $"Conflicting values for '{parameter}' (1) and its alias '{alias}' (2). Supply only one.");
        }
    }

    [TestMethod]
    public async Task SourceTextPastEof_EmitsActualLineCountWithoutPath()
    {
        await using var harness = await ProductionParityMcpHarness.CreateAsync("host-source-eof-refusal", "2025-11-25");
        var (workspaceId, filePath, lineCount) = await LoadWireWorkspaceAsync(harness);
        await AssertWireRefusalAsync(harness, "get_source_text",
            new Dictionary<string, object?> { ["workspaceId"] = workspaceId, ["filePath"] = filePath, ["startLine"] = lineCount + 1 },
            "startLine", $"startLine ({lineCount + 1}) is past the end of the file ({lineCount} lines).");
    }

    private static async Task<(string WorkspaceId, string FilePath, int LineCount)> LoadWireWorkspaceAsync(
        InMemoryMcpClientServerHarness harness)
    {
        var loaded = await harness.Client.CallToolAsync("workspace_load",
            new Dictionary<string, object?> { ["path"] = SampleSolutionPath, ["autoRestore"] = false },
            cancellationToken: CancellationToken.None);
        Assert.IsFalse(loaded.IsError == true);
        var workspaceId = JsonNode.Parse(loaded.Content.OfType<TextContentBlock>().Single().Text)!["workspaceId"]!.GetValue<string>();
        var filePath = WorkspaceManager.GetCurrentSolution(WorkspaceId).Projects.SelectMany(p => p.Documents)
            .First(d => d.Name == "AnimalService.cs").FilePath!;
        var source = await WorkspaceManager.GetSourceTextAsync(WorkspaceId, filePath, CancellationToken.None);
        Assert.IsNotNull(source);
        var lineCount = RoslynMcp.Roslyn.Helpers.SourceTextSlicer.CountLines(source);

        return (workspaceId, filePath, lineCount);
    }

    private static void AssertRefusal(PublicArgumentException ex, string tool, string parameter, string correction)
    {
        Assert.AreEqual(parameter, ex.ParamName);
        var info = ToolErrorHandler.ClassifyError(ex, tool);
        Assert.AreEqual(parameter, info.ParamName);
        Assert.AreEqual(ToolErrorHandler.ErrorCategories.InvalidArgument, info.Category);
        Assert.AreEqual(ex.PublicMessage, info.Message);
        StringAssert.Contains(info.Message, correction);
        Assert.IsFalse(info.Message.Contains(Sentinel, StringComparison.Ordinal));
    }

    private static async Task AssertWireRefusalAsync(InMemoryMcpClientServerHarness harness,
        string tool, Dictionary<string, object?> args, string parameter, string correction)
    {
        var priorCount = harness.RawServerMessages.Count;
        _ = await harness.Client.CallToolAsync(tool, args, cancellationToken: CancellationToken.None);
        var frame = harness.RawServerMessages.Skip(priorCount).Select(s => JsonNode.Parse(s)!.AsObject())
            .Single(node => node.ContainsKey("id") && (node.ContainsKey("result") || node.ContainsKey("error")));
        Assert.IsNull(frame["error"], frame.ToJsonString());
        var result = frame["result"]!.AsObject();
        Assert.AreEqual(true, result["isError"]!.GetValue<bool>());
        var content = result["content"]!.AsArray();
        Assert.HasCount(1, content);
        Assert.AreEqual("text", content[0]!["type"]!.GetValue<string>());
        var payload = JsonNode.Parse(content[0]!["text"]!.GetValue<string>())!.AsObject();
        Assert.AreEqual(true, payload["error"]!.GetValue<bool>());
        Assert.AreEqual("InvalidArgument", payload["category"]!.GetValue<string>());
        Assert.AreEqual(tool, payload["tool"]!.GetValue<string>());
        Assert.AreEqual("ArgumentException", payload["exceptionType"]!.GetValue<string>());
        StringAssert.Contains(payload["message"]!.GetValue<string>(), correction);
        StringAssert.Contains(payload["schemaHint"]!.GetValue<string>(), parameter);
        Assert.IsFalse(payload.ContainsKey("paramName"));
        var raw = frame.ToJsonString();
        Assert.IsFalse(raw.Contains(Sentinel, StringComparison.Ordinal), raw);
        if (args.TryGetValue("filePath", out var path) && path is string submittedPath)
            Assert.IsFalse(raw.Contains(JsonSerializer.Serialize(submittedPath).Trim('"'), StringComparison.Ordinal), raw);
    }
}
