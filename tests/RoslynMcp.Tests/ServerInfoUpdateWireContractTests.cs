using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Host.Stdio.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// server-info-update-unknown-not-false (4.x additive form): raw-wire pins for the stable
/// <c>server_info.update</c> block across both protocol eras. Within the 4.x line
/// <c>updateAvailable</c> stays the v4.2.1 non-nullable boolean — <c>true</c> only when a known
/// registry version is strictly newer than the running build, otherwise <c>false</c> — and
/// <c>checkStatus</c> (with <c>lastCheckedAt</c>) is the authority that distinguishes "up to
/// date" (<c>succeeded</c>) from "unknown" (<c>neverChecked</c>, <c>pending</c>, <c>failed</c>,
/// <c>timedOut</c>). The advertised output schema for the block must stay identical to v4.2.1.
/// </summary>
[TestClass]
public sealed class ServerInfoUpdateWireContractTests
{
    private const string _legacyProtocolVersion = "2025-11-25";
    private const string _modernProtocolVersion = "2026-07-28";
    private const string _newerThanAnyBuild = "999.0.0";
    private const string _olderThanAnyBuild = "0.0.1";
    private const string _updateCommand = "dotnet tool update -g Darylmcd.RoslynMcp";

    /// <summary>
    /// The <c>update</c> subtree of the <c>server_info</c> output schema exactly as the v4.2.1
    /// release advertises it (captured from a running 4.2.1 build's
    /// <c>roslyn://server/catalog/tools/0/5</c> entry). A 4.x build must not change a byte of it:
    /// widening <c>updateAvailable</c> to <c>["boolean","null"]</c> is the 5.0 contract change.
    /// </summary>
    private const string _v421UpdateSchema = """
        {
          "type": ["object", "null"],
          "properties": {
            "current": { "type": "string" },
            "latest": { "type": ["string", "null"] },
            "updateAvailable": { "type": "boolean" },
            "command": { "type": ["string", "null"] },
            "checkStatus": { "type": "string" },
            "lastCheckedAt": { "type": ["string", "null"] }
          },
          "required": ["current", "latest", "updateAvailable", "command", "checkStatus", "lastCheckedAt"]
        }
        """;

    private static readonly DateTime _completedAt = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);

    private static readonly UpdateCase[] _cases =
    [
        new("never checked", null, VersionCheckStatus.NeverChecked, null, "neverChecked", ExpectedAvailable: false),
        new("first check pending", null, VersionCheckStatus.Pending, null, "pending", ExpectedAvailable: false),
        // A refresh after cache expiry reports pending while GetLatestVersion still returns the
        // previously fetched value; v4.2.1 reports that known newer version.
        new("refresh pending with known newer version", _newerThanAnyBuild, VersionCheckStatus.Pending, null, "pending", ExpectedAvailable: true),
        new("succeeded, no newer version", _olderThanAnyBuild, VersionCheckStatus.Succeeded, _completedAt, "succeeded", ExpectedAvailable: false),
        new("succeeded, newer version", _newerThanAnyBuild, VersionCheckStatus.Succeeded, _completedAt, "succeeded", ExpectedAvailable: true),
        new("failed", null, VersionCheckStatus.Failed, _completedAt, "failed", ExpectedAvailable: false),
        new("timed out", null, VersionCheckStatus.TimedOut, _completedAt, "timedOut", ExpectedAvailable: false),
    ];

    [TestMethod]
    public async Task ServerInfo_UpdateBlockOutputSchema_IsByteIdenticalToV421AcrossProtocolEras()
    {
        var expected = JsonNode.Parse(_v421UpdateSchema)!;

        foreach (var protocolVersion in new[] { _legacyProtocolVersion, (string?)null })
        {
            await using var harness = await CreateHarnessAsync(new MutableVersionProvider(), protocolVersion);

            var listed = await harness.Client.ListToolsAsync(new ListToolsRequestParams(), CancellationToken.None);
            var serverInfo = listed.Tools.Single(static tool => tool.Name == "server_info");
            Assert.IsNotNull(serverInfo.OutputSchema, "server_info must advertise an output schema.");
            var update = JsonNode.Parse(serverInfo.OutputSchema.Value.GetRawText())!["properties"]!["update"];

            Assert.IsTrue(
                JsonNode.DeepEquals(expected, update),
                $"[{protocolVersion ?? _modernProtocolVersion}] server_info.update output schema drifted from v4.2.1. " +
                $"Actual: {update?.ToJsonString()}");
        }

        var catalogUpdate = ServerSurfaceCatalog.Tools
            .Single(static tool => tool.Name == "server_info")
            .OutputSchema!["properties"]!["update"];
        Assert.IsTrue(
            JsonNode.DeepEquals(expected, catalogUpdate),
            $"roslyn://server/catalog server_info.update schema drifted from v4.2.1. Actual: {catalogUpdate?.ToJsonString()}");
    }

    [TestMethod]
    public async Task ServerInfo_UpdateAvailableIsBooleanAndCheckStatusCarriesUnknown_AcrossProtocolEras()
    {
        foreach (var protocolVersion in new[] { _legacyProtocolVersion, (string?)null })
        {
            var provider = new MutableVersionProvider();
            await using var harness = await CreateHarnessAsync(provider, protocolVersion);
            var listed = await harness.Client.ListToolsAsync(new ListToolsRequestParams(), CancellationToken.None);
            var schema = JsonNode.Parse(listed.Tools.Single(static tool => tool.Name == "server_info").OutputSchema!.Value.GetRawText())!;

            foreach (var updateCase in _cases)
            {
                var label = $"[{protocolVersion ?? _modernProtocolVersion}] {updateCase.Label}";
                provider.Latest = updateCase.Latest;
                provider.LastCheckStatus = updateCase.Status;
                provider.LastCheckedAt = updateCase.LastCheckedAt;

                var before = harness.RawServerMessages.Count;
                var result = await harness.Client.CallToolAsync("server_info", cancellationToken: CancellationToken.None);
                Assert.IsFalse(result.IsError is true, $"{label}: server_info failed: {result.TextPayload()}");

                var structured = FindSingleNewResult(harness.RawServerMessages, before, label).GetProperty("structuredContent");
                Assert.IsTrue(
                    GeneratedJsonSchemaMatcher.Matches(structured, schema),
                    $"{label}: structuredContent does not satisfy the advertised schema: {structured.GetRawText()}");

                var update = structured.GetProperty("update");
                var updateAvailable = update.GetProperty("updateAvailable");
                Assert.AreEqual(
                    updateCase.ExpectedAvailable ? JsonValueKind.True : JsonValueKind.False,
                    updateAvailable.ValueKind,
                    $"{label}: updateAvailable must be a JSON boolean (never null) within 4.x. Update: {update.GetRawText()}");
                Assert.AreEqual(updateCase.ExpectedStatus, update.GetProperty("checkStatus").GetString(),
                    $"{label}: checkStatus must report the check outcome. Update: {update.GetRawText()}");

                if (updateCase.ExpectedAvailable)
                {
                    Assert.AreEqual(updateCase.Latest, update.GetProperty("latest").GetString(), label);
                    Assert.AreEqual(_updateCommand, update.GetProperty("command").GetString(), label);
                }
                else
                {
                    Assert.AreEqual(JsonValueKind.Null, update.GetProperty("latest").ValueKind, label);
                    Assert.AreEqual(JsonValueKind.Null, update.GetProperty("command").ValueKind, label);
                }

                if (updateCase.LastCheckedAt is { } completedAt)
                {
                    Assert.AreEqual(completedAt.ToString("O"), update.GetProperty("lastCheckedAt").GetString(), label);
                }
                else
                {
                    Assert.AreEqual(JsonValueKind.Null, update.GetProperty("lastCheckedAt").ValueKind, label);
                }
            }
        }
    }

    private static JsonElement FindSingleNewResult(IReadOnlyList<string> rawMessages, int skip, string label)
    {
        var results = new List<JsonElement>();
        foreach (var rawMessage in rawMessages.Skip(skip))
        {
            using var document = JsonDocument.Parse(rawMessage);
            if (document.RootElement.TryGetProperty("result", out var result))
            {
                results.Add(result.Clone());
            }
        }

        Assert.HasCount(1, results, $"{label}: expected one raw response result.");
        return results[0];
    }

    /// <summary>
    /// Full-host harness whose NuGet-backed checker is replaced, so every check state is
    /// deterministic and offline.
    /// </summary>
    private static Task<InMemoryMcpClientServerHarness> CreateHarnessAsync(
        ILatestVersionProvider versionProvider,
        string? protocolVersion) =>
        ProductionParityMcpHarness.CreateAsync(
            $"server-info-update-wire-{protocolVersion ?? _modernProtocolVersion}",
            protocolVersion,
            services => services.AddSingleton(versionProvider));

    private sealed record UpdateCase(
        string Label,
        string? Latest,
        VersionCheckStatus Status,
        DateTime? LastCheckedAt,
        string ExpectedStatus,
        bool ExpectedAvailable);

    private sealed class MutableVersionProvider : ILatestVersionProvider
    {
        public string? Latest { get; set; }

        public VersionCheckStatus LastCheckStatus { get; set; } = VersionCheckStatus.NeverChecked;

        public DateTime? LastCheckedAt { get; set; }

        public string? GetLatestVersion() => Latest;
    }
}
