using System.Text.Json;
using ModelContextProtocol.Protocol;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Middleware;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

/// <summary>
/// Focused coverage for the <c>workspace-auto-load-on-demand</c> metrics, cancellation boundary,
/// discovery classification, and guided fast-fail envelope. Registered-tool dispatch is exercised
/// by the wire-level workspace recovery suite; these tests keep the pure helper contracts small.
/// </summary>
[TestClass]
public sealed class StructuredCallToolFilterAutoLoadTests
{
    private string _root = null!;

    [TestInitialize]
    public void Init()
    {
        _root = Path.Combine(Path.GetTempPath(), "rmcp-autoload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [TestMethod]
    public void GateMetricsDto_RoundTripsAutoLoadFields()
    {
        var builder = new GateMetricsBuilder { AutoResolution = "auto-loaded", AutoLoadElapsedMs = 123 };
        var dto = builder.ToDto();
        Assert.AreEqual("auto-loaded", dto.AutoResolution);
        Assert.AreEqual(123L, dto.AutoLoadElapsedMs);
    }

    [TestMethod]
    public async Task AwaitRecoveryStageAsync_CancelledWithNominalResult_StopsBeforeContinuation()
    {
        using var cts = new CancellationTokenSource();
        var continued = false;

        async Task<string> CancelAsStageReturnsAsync()
        {
            await Task.Yield();
            cts.Cancel();
            return "nominal-result";
        }

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            _ = await StructuredCallToolFilter.AwaitRecoveryStageAsync(
                CancelAsStageReturnsAsync(),
                cts.Token);
            continued = true;
        });

        Assert.IsFalse(continued,
            "A recovery result delivered with cancellation must not drive load parsing, argument mutation, or original dispatch.");
    }

    [TestMethod]
    public void SuccessEnvelope_WhenAutoLoaded_CarriesAutoResolutionAndElapsedMeta()
    {
        using var scope = AmbientGateMetrics.BeginRequest();
        AmbientGateMetrics.Current!.AutoResolution = "auto-loaded";
        AmbientGateMetrics.Current.AutoLoadElapsedMs = 42;

        var injected = ToolErrorHandler.InjectMetaIfPossible("""{"ok":true}""", "symbol_search");
        var meta = JsonDocument.Parse(injected).RootElement.GetProperty("_meta");

        Assert.AreEqual("auto-loaded", meta.GetProperty("autoResolution").GetString());
        Assert.AreEqual(42L, meta.GetProperty("autoLoadElapsedMs").GetInt64(),
            "On-demand load latency must surface in _meta so profiling can isolate the cold-load cost.");
    }

    [TestMethod]
    public void AmbiguousDiscovery_ProducesFastFailEnvelopeListingCandidates()
    {
        // Two discoverable solutions → the filter's auto-load path records fast-fail and returns a
        // structured InvalidArgument envelope naming the candidates with a workspace_load hint.
        File.WriteAllText(Path.Combine(_root, "Alpha.slnx"), "<Solution />");
        File.WriteAllText(Path.Combine(_root, "Beta.slnx"), "<Solution />");
        var source = Path.Combine(_root, "Class1.cs");
        File.WriteAllText(source, "// source");

        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["filePath"] = JsonSerializer.SerializeToElement(source),
        };
        var discovery = SolutionDiscoveryHelper.TryDiscoverFromFilePath(args);
        Assert.AreEqual(SolutionDiscoveryHelper.DiscoveryStatus.Ambiguous, discovery.Status);

        using var scope = AmbientGateMetrics.BeginRequest();
        AmbientGateMetrics.Current!.AutoResolution = "fast-fail";
        var message =
            $"workspaceId was omitted and no workspace is loaded. {discovery.Candidates.Count} candidate " +
            $"solutions were discovered ({string.Join(", ", discovery.Candidates)}). Call workspace_load(path=…).";
        var result = StructuredCallToolFilter.BuildErrorResult(
            "symbol_search", new ArgumentException(message, "workspaceId"));

        Assert.IsTrue(result.IsError);
        var payload = JsonDocument.Parse(((TextContentBlock)result.Content![0]).Text).RootElement;
        Assert.AreEqual("InvalidArgument", payload.GetProperty("category").GetString());
        Assert.AreEqual("fast-fail", payload.GetProperty("_meta").GetProperty("autoResolution").GetString());
        var publicMessage = payload.GetProperty("message").GetString()!;
        Assert.IsFalse(publicMessage.Contains("Alpha.slnx", StringComparison.Ordinal));
        Assert.IsFalse(publicMessage.Contains("Beta.slnx", StringComparison.Ordinal));
        StringAssert.Contains(publicMessage, "workspace_load");
    }
}
