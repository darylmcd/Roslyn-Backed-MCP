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
/// server-info-update-available-next-major-nullable (5.0): raw-wire pins for the
/// <c>server_info.update</c> block across both protocol eras. <c>updateAvailable</c> is
/// <c>true</c> when a known registry version is strictly newer than the running build,
/// <c>false</c> only after a <c>succeeded</c> check found none, and <c>null</c> (unknown) while the
/// check is <c>neverChecked</c>, <c>pending</c>, <c>failed</c> or <c>timedOut</c> without a known
/// newer version. <c>checkStatus</c> (with <c>lastCheckedAt</c>) reports the check outcome.
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
    /// The <c>update</c> subtree of the <c>server_info</c> output schema as 5.0 advertises it:
    /// the 4.x shape with <c>updateAvailable</c> widened to <c>["boolean","null"]</c>.
    /// </summary>
    private const string _updateSchema = """
        {
          "type": ["object", "null"],
          "properties": {
            "current": { "type": "string" },
            "latest": { "type": ["string", "null"] },
            "updateAvailable": { "type": ["boolean", "null"] },
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
        new("never checked", null, VersionCheckStatus.NeverChecked, null, "neverChecked", ExpectedAvailable: null),
        new("first check pending", null, VersionCheckStatus.Pending, null, "pending", ExpectedAvailable: null),
        // A refresh after cache expiry reports pending while GetLatestVersion still returns the
        // previously fetched value; v4.2.1 reports that known newer version.
        new("refresh pending with known newer version", _newerThanAnyBuild, VersionCheckStatus.Pending, null, "pending", ExpectedAvailable: true),
        new("succeeded, no newer version", _olderThanAnyBuild, VersionCheckStatus.Succeeded, _completedAt, "succeeded", ExpectedAvailable: false),
        new("succeeded, newer version", _newerThanAnyBuild, VersionCheckStatus.Succeeded, _completedAt, "succeeded", ExpectedAvailable: true),
        new("failed", null, VersionCheckStatus.Failed, _completedAt, "failed", ExpectedAvailable: null),
        new("timed out", null, VersionCheckStatus.TimedOut, _completedAt, "timedOut", ExpectedAvailable: null),
    ];

    [TestMethod]
    public async Task ServerInfo_UpdateBlockOutputSchema_AdvertisesNullableUpdateAvailableAcrossProtocolEras()
    {
        var expected = JsonNode.Parse(_updateSchema)!;

        foreach (var protocolVersion in new[] { _legacyProtocolVersion, (string?)null })
        {
            await using var harness = await CreateHarnessAsync(new FixedLatestVersionProvider(null, VersionCheckStatus.NeverChecked, null), protocolVersion);

            var listed = await harness.Client.ListToolsAsync(new ListToolsRequestParams(), CancellationToken.None);
            var serverInfo = listed.Tools.Single(static tool => tool.Name == "server_info");
            Assert.IsNotNull(serverInfo.OutputSchema, "server_info must advertise an output schema.");
            var update = JsonNode.Parse(serverInfo.OutputSchema.Value.GetRawText())!["properties"]!["update"];

            Assert.IsTrue(
                JsonNode.DeepEquals(expected, update),
                $"[{protocolVersion ?? _modernProtocolVersion}] server_info.update output schema drifted from the 5.0 contract. " +
                $"Actual: {update?.ToJsonString()}");
        }

        var catalogUpdate = ServerSurfaceCatalog.Tools
            .Single(static tool => tool.Name == "server_info")
            .OutputSchema!["properties"]!["update"];
        Assert.IsTrue(
            JsonNode.DeepEquals(expected, catalogUpdate),
            $"roslyn://server/catalog server_info.update schema drifted from the 5.0 contract. Actual: {catalogUpdate?.ToJsonString()}");
    }

    [TestMethod]
    public async Task ServerInfo_UpdateAvailableIsNullWhileUnknown_AcrossProtocolEras()
    {
        foreach (var protocolVersion in new[] { _legacyProtocolVersion, (string?)null })
        {
            var provider = new FixedLatestVersionProvider(null, VersionCheckStatus.NeverChecked, null);
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
                    updateCase.ExpectedAvailable switch { true => JsonValueKind.True, false => JsonValueKind.False, null => JsonValueKind.Null },
                    updateAvailable.ValueKind,
                    $"{label}: updateAvailable must be true for a known newer version, false only after a succeeded check, null while availability is unknown. Update: {update.GetRawText()}");
                Assert.AreEqual(updateCase.ExpectedStatus, update.GetProperty("checkStatus").GetString(),
                    $"{label}: checkStatus must report the check outcome. Update: {update.GetRawText()}");

                if (updateCase.ExpectedAvailable is true)
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
        bool? ExpectedAvailable);
}
