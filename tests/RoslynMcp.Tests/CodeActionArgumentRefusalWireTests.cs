using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class CodeActionArgumentRefusalWireTests : IsolatedWorkspaceTestBase
{
    private const string PathSentinel = "PRIVATE-CODEACTION-PATH";
    private const string SourceSentinel = "PRIVATE-CODEACTION-SOURCE";
    private const string TitleSentinel = "PRIVATE-CODEACTION-TITLE";
    private const string TokenSentinel = "PRIVATE-CODEACTION-TOKEN";

    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    [DataRow("2025-11-25", 0, -1)]
    [DataRow("2025-11-25", 0, 0)]
    [DataRow("2025-11-25", 1, -1)]
    [DataRow("2025-11-25", 1, 1)]
    [DataRow("2026-07-28", 0, -1)]
    [DataRow("2026-07-28", 0, 0)]
    [DataRow("2026-07-28", 1, -1)]
    [DataRow("2026-07-28", 1, 1)]
    public async Task InvalidIndex_RealServicePreservesPublicCorrectionAndWireIdentity(
        string version, int availableCount, int actionIndex)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var path = workspace.GetPath("SampleLib", PathSentinel + ".cs");
        var source = $"// {SourceSentinel}\nnamespace SampleLib; public class CodeActionProbe {{ }}\n";
        await File.WriteAllTextAsync(path, source);
        await using var harness = await ProductionParityMcpHarness.CreateAsync(
            "code-action-index-refusals", version, services =>
            {
                services.AddSingleton(new SecurityOptions { SanctionedRoots = [workspace.RootPath] });
                services.AddSingleton<ICodeActionService>(provider => new CodeActionService(
                    provider.GetRequiredService<IWorkspaceManager>(),
                    provider.GetRequiredService<IPreviewStore>(),
                    provider.GetRequiredService<ILogger<CodeActionService>>(),
                    () => new FeatureProviderLoadResult<CodeFixProvider>([], []),
                    () => new FeatureProviderLoadResult<CodeRefactoringProvider>(
                        availableCount == 0 ? [] : [new FixedRefactoringProvider()], []),
                    provider.GetRequiredService<IUnexpectedExceptionReporter>()));
            });
        var loaded = await harness.Client.CallToolAsync("workspace_load",
            new Dictionary<string, object?> { ["path"] = workspace.SolutionPath, ["autoRestore"] = false },
            cancellationToken: CancellationToken.None);
        Assert.IsFalse(loaded.IsError == true);
        var id = JsonNode.Parse(loaded.Content.OfType<TextContentBlock>().Single().Text)!["workspaceId"]!.GetValue<string>();
        var arguments = new Dictionary<string, object?>
        {
            ["workspaceId"] = id,
            ["filePath"] = path,
            ["startLine"] = 2,
            ["startColumn"] = 1,
            ["actionIndex"] = actionIndex,
            ["previewToken"] = TokenSentinel,
        };
        var prior = harness.RawServerMessages.Count;
        await harness.Client.CallToolAsync("preview_code_action", arguments, cancellationToken: CancellationToken.None);
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
        Assert.AreEqual("text", content[0]!["type"]?.GetValue<string>(), raw);
        var payload = JsonNode.Parse(content[0]!["text"]!.GetValue<string>())!.AsObject();
        Assert.AreEqual(true, payload["error"]?.GetValue<bool>(), raw);
        Assert.AreEqual("InvalidArgument", payload["category"]?.GetValue<string>(), raw);
        Assert.AreEqual("ArgumentException", payload["exceptionType"]?.GetValue<string>(), raw);
        Assert.AreEqual("preview_code_action", payload["tool"]?.GetValue<string>(), raw);
        Assert.AreEqual($"actionIndex {actionIndex} is out of range. Available actions: {availableCount}.",
            payload["message"]?.GetValue<string>(), raw);
        StringAssert.StartsWith(payload["schemaHint"]!.GetValue<string>(), "preview_code_action(actionIndex:");
        Assert.IsFalse(payload.ContainsKey("paramName"), raw);
        Assert.IsFalse(payload.ContainsKey("correlationId"), raw);
        foreach (var sentinel in new[]
        {
            PathSentinel, SourceSentinel, TitleSentinel, TokenSentinel,
            JsonSerializer.Serialize(workspace.RootPath).Trim('"'),
            "PublicArgumentException", "stackTrace",
        })
            Assert.IsFalse(raw.Contains(sentinel, StringComparison.Ordinal), raw);

        if (availableCount == 1)
        {
            arguments["actionIndex"] = 0;
            var valid = await harness.Client.CallToolAsync("preview_code_action", arguments, cancellationToken: CancellationToken.None);
            Assert.IsFalse(valid.IsError == true);
            var preview = JsonNode.Parse(valid.Content.OfType<TextContentBlock>().Single().Text)!.AsObject();
            Assert.IsFalse(string.IsNullOrWhiteSpace(preview["previewToken"]?.GetValue<string>()));
            Assert.AreEqual("Code action: " + TitleSentinel, preview["description"]?.GetValue<string>());
            Assert.HasCount(1, preview["changes"]!.AsArray());
            StringAssert.Contains(preview["changes"]!.ToJsonString(), "CodeActionPreviewed");
        }
        Assert.AreEqual(source, await File.ReadAllTextAsync(path), "Refusals and previews must not change source files.");
    }

    private sealed class FixedRefactoringProvider : CodeRefactoringProvider
    {
        public override Task ComputeRefactoringsAsync(CodeRefactoringContext context)
        {
            context.RegisterRefactoring(CodeAction.Create(TitleSentinel, async ct =>
            {
                var source = await context.Document.GetTextAsync(ct);
                return context.Document.WithText(SourceText.From(source + "// CodeActionPreviewed\n"));
            }, equivalenceKey: "code-action-refusal-valid-preview"));
            return Task.CompletedTask;
        }
    }
}
