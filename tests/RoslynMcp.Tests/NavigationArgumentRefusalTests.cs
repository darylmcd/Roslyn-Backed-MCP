using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using ModelContextProtocol.Protocol;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NavigationArgumentRefusalTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    [DataRow("")]
    [DataRow("class A {}")]
    [DataRow("class A {}\nclass B {}")]
    [DataRow("class A {}\r\nclass B {}")]
    [DataRow("class A {}\n")]
    public async Task Resolver_RejectsInvalidCoordinatesWithoutCrossLineAliasing(string source)
    {
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("Positions", LanguageNames.CSharp);
        var document = project.AddDocument("Position.cs", SourceText.From(source), filePath: "Position.cs");
        var text = await document.GetTextAsync();
        foreach (var strict in new[] { false, true })
        {
            foreach (var line in new[] { 0, -1, int.MinValue, int.MaxValue, text.Lines.Count + 1 })
                await AssertPositionRefusal(() => SymbolResolver.ResolveAtPositionAsync(
                    document.Project.Solution, "Position.cs", line, 1, CancellationToken.None, strict), "line", $"valid range: 1..{text.Lines.Count}");
            foreach (var column in new[] { 0, -1, int.MinValue, int.MaxValue, text.Lines[0].Span.Length + 2 })
                await AssertPositionRefusal(() => SymbolResolver.ResolveAtPositionAsync(
                    document.Project.Solution, "Position.cs", 1, column, CancellationToken.None, strict), "column", $"valid range: 1..{text.Lines[0].Span.Length + 1}");
            await SymbolResolver.ResolveAtPositionAsync(document.Project.Solution, "Position.cs",
                text.Lines.Count, text.Lines[^1].Span.Length + 1, CancellationToken.None, strict);
        }
        await AssertPositionRefusal(() => SymbolResolver.TryResolveEnclosingMemberAsync(
            document.Project.Solution, "Position.cs", 1, int.MaxValue, CancellationToken.None), "column", $"valid range: 1..{text.Lines[0].Span.Length + 1}");
        await AssertPositionRefusal(() => SymbolResolver.TryResolveEnclosingMemberAsync(
            document.Project.Solution, "Position.cs", 0, 1, CancellationToken.None), "line", $"valid range: 1..{text.Lines.Count}");
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("x")]
    [DataRow("x\nnext")]
    [DataRow("x\r\nnext")]
    [DataRow("x\n")]
    public void SourcePosition_ValidatesStrictProbeAndClampedModes(string source)
    {
        var text = SourceText.From(source);
        for (var line = 1; line <= text.Lines.Count; line++)
        {
            var sourceLine = text.Lines[line - 1];
            var strictMax = sourceLine.Span.Length + 1;
            Assert.AreEqual(sourceLine.Start, SourcePosition.StrictCaret(text, line, 1));
            Assert.AreEqual(sourceLine.End, SourcePosition.StrictCaret(text, line, strictMax));
            var probeMax = sourceLine.SpanIncludingLineBreak.Length > sourceLine.Span.Length
                ? sourceLine.SpanIncludingLineBreak.Length : strictMax;
            for (var column = 1; column <= probeMax; column++)
                Assert.AreEqual(sourceLine.Start + column - 1, SourcePosition.ProbeTrivia(text, line, column));
            Assert.AreEqual(sourceLine.End, SourcePosition.ClampedCodeActionCaret(text, line, int.MaxValue));
            foreach (var column in new[] { 0, -1, int.MinValue, int.MaxValue })
            {
                var strictFailure = Assert.ThrowsExactly<PublicArgumentException>(() => SourcePosition.StrictCaret(text, line, column));
                StringAssert.Contains(strictFailure.PublicMessage, $"valid range: 1..{strictMax}");
                var probeFailure = Assert.ThrowsExactly<PublicArgumentException>(() => SourcePosition.ProbeTrivia(text, line, column));
                StringAssert.Contains(probeFailure.PublicMessage, $"valid range: 1..{probeMax}");
                if (column < 1)
                    Assert.ThrowsExactly<PublicArgumentException>(() => SourcePosition.ClampedCodeActionCaret(text, line, column));
            }
            Assert.ThrowsExactly<PublicArgumentException>(() => SourcePosition.StrictCaret(text, line, strictMax + 1));
            Assert.ThrowsExactly<PublicArgumentException>(() => SourcePosition.ProbeTrivia(text, line, probeMax + 1));
        }
        foreach (var line in new[] { 0, -1, int.MinValue, int.MaxValue, text.Lines.Count + 1 })
        {
            Assert.ThrowsExactly<PublicArgumentException>(() => SourcePosition.StrictCaret(text, line, 1));
            Assert.ThrowsExactly<PublicArgumentException>(() => SourcePosition.ProbeTrivia(text, line, 1));
            Assert.ThrowsExactly<PublicArgumentException>(() => SourcePosition.ClampedCodeActionCaret(text, line, 1));
        }
    }

    private static async Task AssertPositionRefusal(Func<Task<ISymbol?>> action, string parameter, string correction)
    {
        var exception = await Assert.ThrowsExactlyAsync<PublicArgumentException>(action);
        Assert.AreEqual(parameter, exception.ParamName);
        StringAssert.Contains(exception.PublicMessage, correction);
        Assert.IsFalse(exception.PublicMessage.Contains("Position.cs", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("2025-11-25")]
    [DataRow("2026-07-28")]
    public async Task NavigationRefusals_PreserveSafeDualEraWireContract(string version)
    {
        await using var harness = await ProductionParityMcpHarness.CreateAsync("navigation-refusals", version);
        var loaded = await harness.Client.CallToolAsync("workspace_load",
            new Dictionary<string, object?> { ["path"] = SampleSolutionPath, ["autoRestore"] = false },
            cancellationToken: CancellationToken.None);
        Assert.IsFalse(loaded.IsError == true);
        var id = JsonNode.Parse(loaded.Content.OfType<TextContentBlock>().Single().Text)!["workspaceId"]!.GetValue<string>();
        var root = Path.GetDirectoryName(SampleSolutionPath)!;
        var path = Path.Combine(root, "SampleLib", "RefactoringProbe.cs");
        var cases = new List<(string Tool, string Parameter, string Correction, Dictionary<string, object?> Args)>();
        void Add(string tool, string parameter, string correction, Dictionary<string, object?> args)
        {
            args["workspaceId"] = id;
            if (correction == "valid range: 1..")
            {
                var text = SourceText.From(File.ReadAllText(path));
                var firstLine = text.Lines[0];
                var upper = parameter.EndsWith("Line", StringComparison.Ordinal) || parameter == "line"
                    ? text.Lines.Count
                    : tool == "probe_position" ? firstLine.SpanIncludingLineBreak.Length : firstLine.Span.Length + 1;
                correction += upper;
            }
            cases.Add((tool, parameter, correction, args));
        }
        Add("go_to_definition", "line", "valid range: 1..", new() { ["filePath"] = path, ["line"] = 99999, ["column"] = 1 });
        Add("go_to_definition", "column", "Source location is incomplete", new() { ["filePath"] = path, ["line"] = 1 });
        foreach (var tool in new[] { "enclosing_symbol", "probe_position", "get_completions", "get_operations" })
        {
            Add(tool, "line", "valid range: 1..", new() { ["filePath"] = path, ["line"] = 0, ["column"] = 1 });
            Add(tool, "column", "valid range: 1..", new() { ["filePath"] = path, ["line"] = 1, ["column"] = int.MaxValue });
        }
        Add("get_completions", "maxItems", "greater than 0", new() { ["filePath"] = path, ["line"] = 1, ["column"] = 1, ["maxItems"] = 0, ["filterText"] = "PRIVATE-NAVIGATION" });
        foreach (var tool in new[] { "get_code_actions", "preview_code_action" })
        {
            Add(tool, "startLine", tool == "get_code_actions" ? "startLine must be >= 1" : "valid range: 1..", new() { ["filePath"] = path, ["startLine"] = 0, ["startColumn"] = 1, ["actionIndex"] = 0 });
            Add(tool, "startColumn", tool == "get_code_actions" ? "startColumn must be >= 1" : "valid range: 1..", new() { ["filePath"] = path, ["startLine"] = 1, ["startColumn"] = 0, ["actionIndex"] = 0 });
            Add(tool, "endColumn", "provided together", new() { ["filePath"] = path, ["startLine"] = 1, ["startColumn"] = 1, ["endLine"] = 1, ["actionIndex"] = 0 });
            Add(tool, "endLine", "provided together", new() { ["filePath"] = path, ["startLine"] = 1, ["startColumn"] = 1, ["endColumn"] = 1, ["actionIndex"] = 0 });
            Add(tool, "endColumn", "valid range: 1..", new() { ["filePath"] = path, ["startLine"] = 1, ["startColumn"] = 1, ["endLine"] = 1, ["endColumn"] = int.MaxValue, ["actionIndex"] = 0 });
            Add(tool, "endLine", "valid range: 1..", new() { ["filePath"] = path, ["startLine"] = 1, ["startColumn"] = 1, ["endLine"] = 99999, ["endColumn"] = 1, ["actionIndex"] = 0 });
            Add(tool, "startColumn", "valid range: 1..", new() { ["filePath"] = path, ["startLine"] = 1, ["startColumn"] = int.MaxValue, ["endLine"] = 1, ["endColumn"] = 1, ["actionIndex"] = 0 });
            Add(tool, "endLine", "must not precede", new() { ["filePath"] = path, ["startLine"] = 2, ["startColumn"] = 1, ["endLine"] = 1, ["endColumn"] = 1, ["actionIndex"] = 0 });
            Add(tool, "endColumn", "must not precede", new() { ["filePath"] = path, ["startLine"] = 1, ["startColumn"] = 2, ["endLine"] = 1, ["endColumn"] = 1, ["actionIndex"] = 0 });
        }
        foreach (var item in cases)
        {
            var prior = harness.RawServerMessages.Count;
            await harness.Client.CallToolAsync(item.Tool, item.Args, cancellationToken: CancellationToken.None);
            var frame = harness.RawServerMessages.Skip(prior).Select(s => JsonNode.Parse(s)!.AsObject())
                .Single(node => node.ContainsKey("id") && (node.ContainsKey("result") || node.ContainsKey("error")));
            Assert.IsNull(frame["error"], frame.ToJsonString());
            var result = frame["result"]!.AsObject();
            Assert.AreEqual(true, result["isError"]!.GetValue<bool>(), frame.ToJsonString());
            if (version == "2026-07-28")
                Assert.AreEqual("complete", result["resultType"]!.GetValue<string>());
            else
                Assert.IsFalse(result.ContainsKey("resultType"));
            var content = result["content"]!.AsArray();
            Assert.HasCount(1, content);
            var payload = JsonNode.Parse(content[0]!["text"]!.GetValue<string>())!.AsObject();
            Assert.AreEqual("InvalidArgument", payload["category"]!.GetValue<string>());
            Assert.AreEqual("ArgumentException", payload["exceptionType"]!.GetValue<string>());
            StringAssert.StartsWith(payload["schemaHint"]!.GetValue<string>(), item.Tool + "(" + item.Parameter + ":");
            StringAssert.Contains(payload["message"]!.GetValue<string>(), item.Correction);
            var raw = frame.ToJsonString();
            Assert.IsFalse(raw.Contains("PRIVATE-NAVIGATION", StringComparison.Ordinal));
            Assert.IsFalse(raw.Contains(JsonSerializer.Serialize(root).Trim('"'), StringComparison.Ordinal));
        }
    }
}
