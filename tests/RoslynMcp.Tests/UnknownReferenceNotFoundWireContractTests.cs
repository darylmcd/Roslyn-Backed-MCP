using System.Text.Json;
using System.Text.Json.Nodes;
using RoslynMcp.Roslyn;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// project-name-not-found-category-next-major (5.0): raw-wire pins that an unknown
/// <c>projectName</c> (<c>build_project</c>, <c>test_run</c>) and an unresolvable
/// <c>symbolHandle</c> (<c>rename_preview</c>) report the <c>NotFound</c> category, not
/// <c>InvalidOperation</c>, in both protocol eras. The messages are caller-actionable and carry
/// neither the submitted value nor <c>workspace_reload</c> advice, which cannot fix a name typo.
/// </summary>
[TestClass]
public sealed class UnknownReferenceNotFoundWireContractTests
{
    private const string _legacyProtocolVersion = "2025-11-25";
    private const string _modernProtocolVersion = "2026-07-28";
    private const string _submittedProjectName = "NoSuchProjectForNotFoundWireTest";
    // Well-formed (base64 JSON payload) but resolves to no symbol: a malformed handle is an
    // InvalidArgument, an unresolvable one is NotFound.
    private static readonly string _fabricatedHandle = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
        """{"MetadataName":"NoSuch.TypeForNotFoundWireTest","DisplayName":"NoSuch.TypeForNotFoundWireTest"}"""));

    [TestMethod]
    [DataRow(null)]
    [DataRow(_legacyProtocolVersion)]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task UnknownProjectNameAndSymbolHandle_ReportNotFound_AcrossProtocolEras(string? requestedProtocol)
    {
        MsBuildInitializer.EnsureInitialized();
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var sampleSolution = TestFixtureFileSystem.FindFixturePath(
            repositoryRoot,
            "SampleSolution",
            "SampleSolution.slnx",
            "SampleSolution.sln");

        await using var harness = await ProductionParityMcpHarness.CreateAsync(
            $"unknown-reference-wire-{requestedProtocol ?? _modernProtocolVersion}",
            requestedProtocol);
        var label = $"[{requestedProtocol ?? _modernProtocolVersion}]";

        var load = await harness.Client.CallToolAsync(
            "workspace_load",
            Args(("path", sampleSolution), ("prewarm", false), ("autoRestore", false)),
            cancellationToken: CancellationToken.None);
        Assert.IsFalse(load.IsError is true, $"{label} workspace_load failed: {load.TextPayload()}");
        using var loadDocument = JsonDocument.Parse(load.TextPayload());
        var workspaceId = loadDocument.RootElement.GetProperty("workspaceId").GetString()!;

        foreach (var (tool, arguments) in new (string, Dictionary<string, object?>)[]
        {
            ("build_project", Args(("workspaceId", workspaceId), ("projectName", _submittedProjectName))),
            ("test_run", Args(("workspaceId", workspaceId), ("projectName", _submittedProjectName))),
            ("rename_preview", Args(("workspaceId", workspaceId), ("symbolHandle", _fabricatedHandle), ("newName", "Renamed"))),
        })
        {
            var result = await harness.Client.CallToolAsync(tool, arguments, cancellationToken: CancellationToken.None);
            Assert.IsTrue(result.IsError is true, $"{label} {tool}: expected a tool error, got: {result.TextPayload()}");
            var text = result.TextPayload();
            var envelope = Assert.IsInstanceOfType<JsonObject>(JsonNode.Parse(text));

            Assert.AreEqual("NotFound", envelope["category"]?.GetValue<string>(), $"{label} {tool}: {text}");
            Assert.AreEqual("KeyNotFoundException", envelope["exceptionType"]?.GetValue<string>(), $"{label} {tool}: {text}");
            Assert.IsFalse(envelope.ContainsKey("reason"), $"{label} {tool}: only a workspace miss carries a reason. {text}");
            var message = envelope["message"]!.GetValue<string>();
            Assert.IsFalse(message.Contains("workspace_reload", StringComparison.Ordinal),
                $"{label} {tool}: reloading cannot fix an unknown name. Message: {message}");
            Assert.IsFalse(text.Contains(_submittedProjectName, StringComparison.Ordinal) ||
                           text.Contains(_fabricatedHandle, StringComparison.Ordinal),
                $"{label} {tool}: the envelope must not echo the submitted value. {text}");
            if (tool != "rename_preview")
            {
                StringAssert.Contains(message, "workspace_status", $"{label} {tool}: {message}");
            }
        }
    }

    private static Dictionary<string, object?> Args(params (string Name, object? Value)[] values) =>
        values.ToDictionary(static value => value.Name, static value => value.Value, StringComparer.Ordinal);
}
