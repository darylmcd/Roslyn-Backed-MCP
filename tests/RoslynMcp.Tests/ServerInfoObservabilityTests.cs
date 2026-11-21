using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Host.Stdio.Runtime;
using RoslynMcp.Host.Stdio.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// <c>logging-default-sink-disabled-stderr-swallowed</c>: <c>server_info.observability</c> tells an agent
/// where the log sink writes. Every test owns its <see cref="ServerProcessMetadata"/>, so this parallel
/// class shares no mutable state with other classes that probe <c>server_info</c>.
/// </summary>
[TestClass]
public sealed class ServerInfoObservabilityTests
{
    [TestMethod]
    public async Task ServerInfo_UnpublishedObservability_IsNullMeaningUnknown()
    {
        using var document = await ReadServerInfoAsync(new ServerProcessMetadata());

        Assert.AreEqual(
            JsonValueKind.Null,
            document.RootElement.GetProperty("observability").ValueKind,
            "An unbooted host is 'unknown', never a fabricated sink.");
    }

    [TestMethod]
    public async Task ServerInfo_PublishedFileSink_ReportsSinkLogFileAndLogDirectory()
    {
        var metadata = new ServerProcessMetadata();
        metadata.PublishObservability(new ObservabilityDto("file", "/state/roslyn-mcp/logs/roslyn-mcp-42.jsonl", "/state/roslyn-mcp/logs"));

        using var document = await ReadServerInfoAsync(metadata);

        var observability = document.RootElement.GetProperty("observability");
        Assert.AreEqual("file", observability.GetProperty("sink").GetString());
        Assert.AreEqual("/state/roslyn-mcp/logs/roslyn-mcp-42.jsonl", observability.GetProperty("logFile").GetString());
        Assert.AreEqual("/state/roslyn-mcp/logs", observability.GetProperty("logDirectory").GetString());
    }

    [TestMethod]
    public async Task ServerInfo_PublishedDisabledSink_ReportsNullPaths()
    {
        var metadata = new ServerProcessMetadata();
        metadata.PublishObservability(new ObservabilityDto("disabled", null, null));

        using var document = await ReadServerInfoAsync(metadata);

        var observability = document.RootElement.GetProperty("observability");
        Assert.AreEqual("disabled", observability.GetProperty("sink").GetString());
        Assert.AreEqual(JsonValueKind.Null, observability.GetProperty("logFile").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, observability.GetProperty("logDirectory").ValueKind);
    }

    private static async Task<JsonDocument> ReadServerInfoAsync(ServerProcessMetadata metadata)
    {
        var result = await ServerTools.GetServerInfo(
            new FailClosedWorkspaceManagerStub(),
            new FixedLatestVersionProvider(null, VersionCheckStatus.NeverChecked, null),
            metadata);
        return JsonDocument.Parse(result.TextPayload());
    }
}
