using System.Text.Json;
using ModelContextProtocol.Protocol;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Middleware;
using RoslynMcp.Host.Stdio.ProtocolCompatibility;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

/// <summary>
/// Coverage for the <c>workspace-id-omitted-single-resolve</c> initiative: the
/// <see cref="StructuredCallToolFilter"/> pre-dispatch path that resolves an omitted/empty
/// <c>workspaceId</c> on read-only, non-destructive tools before the SDK binder runs. Pins:
/// <list type="bullet">
///   <item><b>eligibility</b> — <see cref="ElicitationAllowlistPolicy.IsWorkspaceIdAutoResolveAllowedFor"/>
///         fires for read-only workspace-scoped tools <i>regardless of the schema Required flag</i>,
///         which is the property that keeps the feature alive after a tool flips
///         <c>workspaceId</c> to optional (the surface-flip initiative). Its sibling
///         <see cref="ElicitationAllowlistPolicy.IsWorkspaceIdRecoveryAllowedFor"/> is now also
///         Required-independent; it differs only in covering the path prompt and
///         recover-load-retry flow rather than the earlier auto-resolution path.</item>
///   <item><b>classification</b> — omit + 1 loaded resolves to that workspace; omit + ≥2 loaded
///         fast-fails listing the candidates; omit + 0 loaded is left for on-demand discovery;
///         an explicit id passes through untouched.</item>
///   <item><b>observability</b> — the <c>_meta.autoResolution</c> field round-trips through the
///         metrics snapshot and onto the error envelope.</item>
/// </list>
/// The full <see cref="StructuredCallToolFilter.Create"/> delegate needs a live MCP transport to
/// drive end-to-end, so (as with the elicitation tests) the contract asserted here is the gate
/// logic + classification that the pre-dispatch branch is a thin wrapper over.
/// </summary>
[TestClass]
public sealed class StructuredCallToolFilterResolutionTests
{
    [TestMethod]
    public void NormalizeSourceLocationArguments_CharacterOnly_ConvertsToOneBasedColumn()
    {
        var args = Arguments(("filePath", "C:/repo/Test.cs"), ("line", 4), ("character", 8));

        var normalized = LspSourceLocationArgumentNormalizer.Normalize("symbol_info", args)!;

        Assert.IsFalse(normalized.ContainsKey("character"));
        Assert.AreEqual(9, normalized["column"].GetInt32());
    }

    [TestMethod]
    public void NormalizeSourceLocationArguments_ExistingColumn_IsUnchanged()
    {
        var args = Arguments(("column", 9));

        Assert.AreSame(
            args,
            LspSourceLocationArgumentNormalizer.Normalize("symbol_info", args));
    }

    [TestMethod]
    public void NormalizeSourceLocationArguments_AgreeingAliases_KeepCanonicalColumn()
    {
        var args = Arguments(("column", 9), ("character", 8));

        var normalized = LspSourceLocationArgumentNormalizer.Normalize("symbol_info", args)!;

        Assert.AreEqual(9, normalized["column"].GetInt32());
        Assert.IsFalse(normalized.ContainsKey("character"));
    }

    [TestMethod]
    public void NormalizeSourceLocationArguments_ConflictingAliases_FailDeterministically()
    {
        var args = Arguments(("column", 9), ("character", 9));

        var error = Assert.ThrowsExactly<PublicArgumentException>(() =>
            LspSourceLocationArgumentNormalizer.Normalize("symbol_info", args));

        Assert.AreEqual("character", error.ParamName);
        StringAssert.Contains(error.Message, "column must equal character + 1 (10)");
    }

    [TestMethod]
    public void NormalizeSourceLocationArguments_NonSourceLocationTool_LeavesCharacterForBinder()
    {
        var args = Arguments(("character", 8));

        Assert.AreSame(
            args,
            LspSourceLocationArgumentNormalizer.Normalize("workspace_list", args));
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(int.MaxValue)]
    public void NormalizeSourceLocationArguments_InvalidCharacter_FailsClosed(int character)
    {
        var error = Assert.ThrowsExactly<PublicArgumentException>(() =>
            LspSourceLocationArgumentNormalizer.Normalize(
                "symbol_info",
                Arguments(("character", character))));

        Assert.AreEqual("character", error.ParamName);
    }

    // ── eligibility (Required-independent) ──────────────────────────────────

    [TestMethod]
    public void IsWorkspaceIdAutoResolveAllowedFor_RequiredReadOnlyTool_ReturnsTrue()
    {
        Assert.IsTrue(ElicitationAllowlistPolicy.IsWorkspaceIdAutoResolveAllowedFor("symbol_search"));
        Assert.IsTrue(ElicitationAllowlistPolicy.IsWorkspaceIdAutoResolveAllowedFor("find_references"));
        Assert.IsTrue(ElicitationAllowlistPolicy.IsWorkspaceIdAutoResolveAllowedFor("compile_check"));
    }

    [TestMethod]
    public void IsWorkspaceIdAutoResolveAllowedFor_OptionalReadOnlyTool_ReturnsTrue()
    {
        // workspace_readiness_report declares `string? workspaceId = null` (Required:false) at HEAD.
        // Auto-resolution MUST still apply — this is the exact post-surface-flip shape, and the
        // whole point of decoupling auto-resolution from the Required flag.
        Assert.IsTrue(
            ElicitationAllowlistPolicy.IsWorkspaceIdAutoResolveAllowedFor("workspace_readiness_report"),
            "Auto-resolution must fire for read-only tools whose workspaceId is optional; " +
            "otherwise the feature dies the moment a tool flips workspaceId to a default.");

        // Coherence witness: the path-recovery predicate is also Required-independent
        // (decoupled to match auto-resolve), so it fires for the same optional read-only tool. The
        // two predicates differ in WHICH path they gate (pre-dispatch auto-resolution vs the
        // prompt-and-retry recovery), not in Required-gating.
        Assert.IsTrue(
            ElicitationAllowlistPolicy.IsWorkspaceIdRecoveryAllowedFor("workspace_readiness_report", "workspaceId"),
            "The path-recovery predicate is Required-independent and must fire for an " +
            "optional-workspaceId read-only tool, mirroring the auto-resolve predicate.");
    }

    [TestMethod]
    public void IsWorkspaceIdAutoResolveAllowedFor_WriteOrDestructiveTool_ReturnsFalse()
    {
        Assert.IsFalse(ElicitationAllowlistPolicy.IsWorkspaceIdAutoResolveAllowedFor("apply_text_edit"),
            "Mutating tools must never have workspaceId auto-resolved at the chokepoint.");
        Assert.IsFalse(ElicitationAllowlistPolicy.IsWorkspaceIdAutoResolveAllowedFor("revert_last_apply"),
            "Destructive tools must never have workspaceId auto-resolved.");
        Assert.IsFalse(ElicitationAllowlistPolicy.IsWorkspaceIdAutoResolveAllowedFor("workspace_close"),
            "Destructive workspace-lifecycle tools must never auto-resolve workspaceId.");
    }

    [TestMethod]
    public void IsWorkspaceIdAutoResolveAllowedFor_UnknownOrEmpty_ReturnsFalse()
    {
        Assert.IsFalse(ElicitationAllowlistPolicy.IsWorkspaceIdAutoResolveAllowedFor("not_a_real_tool"));
        Assert.IsFalse(ElicitationAllowlistPolicy.IsWorkspaceIdAutoResolveAllowedFor(""));
        Assert.IsFalse(ElicitationAllowlistPolicy.IsWorkspaceIdAutoResolveAllowedFor(null));
    }

    [TestMethod]
    public void IsWorkspaceIdRecoveryAllowedFor_UnknownTool_ReturnsFalse()
    {
        // solutiondiscoveryhelper-hotpath-perf: the recovery predicate now resolves the tool via the
        // O(1) ServerSurfaceCatalog.TryGetTool index instead of a linear FirstOrDefault scan. An
        // unknown tool name must still short-circuit to false (TryGetTool miss ⇒ no eligible tool),
        // preserving the pre-switch behavior.
        Assert.IsFalse(
            ElicitationAllowlistPolicy.IsWorkspaceIdRecoveryAllowedFor("not_a_real_tool", "workspaceId"));
    }

    // ── classification ──────────────────────────────────────────────────────

    [TestMethod]
    public void ClassifyWorkspaceIdResolution_ExplicitId_ReturnsExplicit()
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["workspaceId"] = JsonSerializer.SerializeToElement("ws-1"),
        };

        var result = StructuredCallToolFilter.ClassifyWorkspaceIdResolution(
            args, new[] { Summary("ws-1"), Summary("ws-2") }, out var resolvedId, out var failMessage);

        Assert.AreEqual(StructuredCallToolFilter.WorkspaceIdAutoResolution.Explicit, result);
        Assert.IsNull(resolvedId);
        Assert.IsNull(failMessage);
    }

    [TestMethod]
    public void ClassifyWorkspaceIdResolution_OmittedWithOneLoaded_ResolvesToSingle()
    {
        var result = StructuredCallToolFilter.ClassifyWorkspaceIdResolution(
            arguments: null, new[] { Summary("ws-only") }, out var resolvedId, out var failMessage);

        Assert.AreEqual(StructuredCallToolFilter.WorkspaceIdAutoResolution.SingleWorkspace, result);
        Assert.AreEqual("ws-only", resolvedId);
        Assert.IsNull(failMessage);
    }

    [TestMethod]
    public void ClassifyWorkspaceIdResolution_BlankIdWithOneLoaded_ResolvesToSingle()
    {
        // A whitespace-only id counts as omitted — same as the workspace tools' own resolver.
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["workspaceId"] = JsonSerializer.SerializeToElement("   "),
        };

        var result = StructuredCallToolFilter.ClassifyWorkspaceIdResolution(
            args, new[] { Summary("ws-only") }, out var resolvedId, out _);

        Assert.AreEqual(StructuredCallToolFilter.WorkspaceIdAutoResolution.SingleWorkspace, result);
        Assert.AreEqual("ws-only", resolvedId);
    }

    [TestMethod]
    public void ClassifyWorkspaceIdResolution_OmittedWithTwoLoaded_FastFailsNamingBoth()
    {
        var result = StructuredCallToolFilter.ClassifyWorkspaceIdResolution(
            arguments: null,
            new[] { Summary("ws-alpha"), Summary("ws-beta") },
            out var resolvedId,
            out var failMessage);

        Assert.AreEqual(StructuredCallToolFilter.WorkspaceIdAutoResolution.FastFail, result);
        Assert.IsNull(resolvedId);
        Assert.IsNotNull(failMessage);
        StringAssert.Contains(failMessage, "ws-alpha");
        StringAssert.Contains(failMessage, "ws-beta");
    }

    [TestMethod]
    public void ClassifyWorkspaceIdResolution_UniqueFileOwner_ResolvesOwner()
    {
        var result = StructuredCallToolFilter.ClassifyWorkspaceIdResolution(
            arguments: null,
            new[] { Summary("ws-alpha", "C:/repos/a/A.sln"), Summary("ws-beta", "C:/repos/b/B.sln") },
            new[] { "ws-beta" },
            out var resolvedId,
            out var failMessage);

        Assert.AreEqual(StructuredCallToolFilter.WorkspaceIdAutoResolution.FilePathWorkspace, result);
        Assert.AreEqual("ws-beta", resolvedId);
        Assert.IsNull(failMessage);
    }

    [TestMethod]
    public void ClassifyWorkspaceIdResolution_AmbiguousOwners_ListsOnlyOwnersInPathOrder()
    {
        var result = StructuredCallToolFilter.ClassifyWorkspaceIdResolution(
            arguments: null,
            new[]
            {
                Summary("ws-z", "C:/repos/z/Z.sln"),
                Summary("ws-b", "C:/repos/b/B.sln"),
                Summary("ws-a", "C:/repos/a/A.sln"),
            },
            new[] { "ws-z", "ws-a" },
            out _,
            out var failMessage);

        Assert.AreEqual(StructuredCallToolFilter.WorkspaceIdAutoResolution.FastFail, result);
        Assert.IsNotNull(failMessage);
        Assert.IsFalse(failMessage.Contains("ws-b", StringComparison.Ordinal), failMessage);
        Assert.IsTrue(
            failMessage.IndexOf("ws-a", StringComparison.Ordinal) <
            failMessage.IndexOf("ws-z", StringComparison.Ordinal),
            failMessage);
        StringAssert.Contains(failMessage, "loadedPath");
    }

    [TestMethod]
    public void ClassifyWorkspaceIdResolution_OmittedWithZeroLoaded_NotApplicable()
    {
        var result = StructuredCallToolFilter.ClassifyWorkspaceIdResolution(
            arguments: null, Array.Empty<WorkspaceStatusSummaryDto>(), out var resolvedId, out var failMessage);

        Assert.AreEqual(StructuredCallToolFilter.WorkspaceIdAutoResolution.NotApplicable, result);
        Assert.IsNull(resolvedId);
        Assert.IsNull(failMessage);
    }

    [TestMethod]
    public void RequestStateCodec_ValidWorkspaceId_RoundTripsCanonicalParameter()
    {
        var workspaceId = Guid.NewGuid().ToString("N");
        var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            [ElicitationAllowlistPolicy.WorkspaceIdParameterName] =
                JsonSerializer.SerializeToElement(workspaceId),
        };

        var state = RequestStateCodec.CaptureWorkspaceId(
            arguments,
            ElicitationAllowlistPolicy.WorkspaceIdParameterName);

        Assert.IsNotNull(state);
        Assert.IsTrue(RequestStateCodec.TryRestoreWorkspaceId(state, out var restored));
        Assert.AreEqual(workspaceId, restored);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("roslynmcp.workspace.v1:not-a-guid")]
    [DataRow("roslynmcp.workspace.v1:gggggggggggggggggggggggggggggggg")]
    [DataRow("roslynmcp.workspace.v2:0123456789abcdef0123456789abcdef")]
    [DataRow("roslynmcp.workspace.v1:0123456789abcdef0123456789abcdef-extra")]
    public void RequestStateCodec_MalformedOrUnknownState_FailsClosed(string? state)
    {
        Assert.IsFalse(RequestStateCodec.TryRestoreWorkspaceId(state, out var restored));
        Assert.AreEqual(string.Empty, restored);
    }

    // ── observability (_meta.autoResolution) ────────────────────────────────

    [TestMethod]
    public void GateMetricsDto_RoundTripsAutoResolution()
    {
        var builder = new GateMetricsBuilder { AutoResolution = "single-workspace" };
        Assert.AreEqual("single-workspace", builder.ToDto().AutoResolution);
    }

    [TestMethod]
    public void BuildErrorResult_WhenFastFailRecorded_EnvelopeMetaCarriesAutoResolution()
    {
        using var scope = AmbientGateMetrics.BeginRequest();
        AmbientGateMetrics.Current!.AutoResolution = "fast-fail";

        var result = StructuredCallToolFilter.BuildErrorResult(
            "symbol_search",
            new ArgumentException("workspaceId was omitted but 2 workspaces are loaded.", "workspaceId"));

        Assert.IsTrue(result.IsError);
        var text = ((TextContentBlock)result.Content![0]).Text;
        var payload = JsonDocument.Parse(text).RootElement;
        Assert.AreEqual("InvalidArgument", payload.GetProperty("category").GetString());
        Assert.IsTrue(payload.TryGetProperty("_meta", out var meta), $"Expected _meta. Actual: {payload}");
        Assert.AreEqual("fast-fail", meta.GetProperty("autoResolution").GetString(),
            "The fast-fail envelope must surface autoResolution so callers can distinguish a " +
            "structured 'pick a workspace' error from an ordinary InvalidArgument.");
    }

    private static WorkspaceStatusSummaryDto Summary(string workspaceId, string? loadedPath = null) => new(
        WorkspaceId: workspaceId,
        LoadedPath: loadedPath,
        WorkspaceVersion: 0,
        SnapshotToken: string.Empty,
        LoadedAtUtc: default,
        ProjectCount: 0,
        DocumentCount: 0,
        IsLoaded: true,
        IsStale: false,
        WorkspaceDiagnosticCount: 0,
        WorkspaceErrorCount: 0,
        WorkspaceWarningCount: 0,
        IsReady: true,
        AnalyzersReady: true,
        RestoreRequired: false,
        BuildRequired: false,
        RestoreHint: null,
        SolutionFileName: null);

    private static Dictionary<string, JsonElement> Arguments(params (string Name, object Value)[] values) =>
        values.ToDictionary(
            value => value.Name,
            value => JsonSerializer.SerializeToElement(value.Value),
            StringComparer.Ordinal);
}
