using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Host.Stdio.Resources;
using RoslynMcp.Host.Stdio.Services;
using RoslynMcp.Host.Stdio.Tools;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Server;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class SurfaceCatalogTests
{
    [TestMethod]
    public void ServerSurfaceCatalog_CoversAllRegisteredToolsResourcesAndPrompts()
    {
        var assembly = typeof(ServerTools).Assembly;

        CollectionAssert.AreEquivalent(
            GetRegisteredNames<McpServerToolAttribute>(assembly),
            ServerSurfaceCatalog.Tools.Select(entry => entry.Name).ToArray());

        CollectionAssert.AreEquivalent(
            GetRegisteredNames<McpServerResourceAttribute>(assembly),
            ServerSurfaceCatalog.Resources.Select(entry => entry.Name).ToArray());

        CollectionAssert.AreEquivalent(
            GetRegisteredNames<McpServerPromptAttribute>(assembly),
            ServerSurfaceCatalog.Prompts.Select(entry => entry.Name).ToArray());
    }

    // dr-9-11-payload-exceeds-mcp-tool-result-cap: the default `server_catalog` resource must
    // return a cap-safe summary (no Tools / Prompts lists inline). The paginated siblings
    // serve those lists in slices.
    [TestMethod]
    public void ServerCatalogSummary_ReplacesToolsAndPromptsWithPaginationPointers()
    {
        var summary = ServerSurfaceCatalog.CreateSummaryDocument();
        Assert.AreEqual(ServerSurfaceCatalog.Tools.Count, summary.ToolCount);
        Assert.AreEqual(ServerSurfaceCatalog.Prompts.Count, summary.PromptCount);
        Assert.AreEqual("roslyn://server/catalog/tools/{offset}/{limit}", summary.ToolsResourceTemplate);
        Assert.AreEqual("roslyn://server/catalog/prompts/{offset}/{limit}", summary.PromptsResourceTemplate);
        CollectionAssert.AreEquivalent(
            ServerSurfaceCatalog.Resources.Select(e => e.Name).ToArray(),
            summary.Resources.Select(e => e.Name).ToArray());
    }

    [TestMethod]
    public void PageEntries_HonoursOffsetAndLimit_SurfacesPaginationMetadata()
    {
        var page = ServerSurfaceCatalog.PageEntries(ServerSurfaceCatalog.Tools, offset: 0, limit: 10, resourceName: "test");
        Assert.AreEqual("test", page.ResourceName);
        Assert.AreEqual(0, page.Offset);
        Assert.AreEqual(10, page.Limit);
        Assert.AreEqual(10, page.ReturnedCount);
        Assert.AreEqual(ServerSurfaceCatalog.Tools.Count, page.TotalCount);
        Assert.IsTrue(page.HasMore);
        Assert.AreEqual(10, page.Entries.Count);
    }

    [TestMethod]
    public void PageEntries_ClampsOffsetPastTotal_EmptyPageNoHasMore()
    {
        var total = ServerSurfaceCatalog.Tools.Count;
        var page = ServerSurfaceCatalog.PageEntries(ServerSurfaceCatalog.Tools, offset: total + 100, limit: 10, resourceName: "test");
        Assert.AreEqual(total, page.Offset, "Offset must clamp to total, not past it.");
        Assert.AreEqual(0, page.ReturnedCount);
        Assert.AreEqual(total, page.TotalCount);
        Assert.IsFalse(page.HasMore);
    }

    [TestMethod]
    public void PageEntries_ClampsLimitToCeiling()
    {
        var page = ServerSurfaceCatalog.PageEntries(ServerSurfaceCatalog.Tools, offset: 0, limit: 99999, resourceName: "test");
        Assert.AreEqual(200, page.Limit, "Limit must clamp to the 200 ceiling.");
    }

    [TestMethod]
    public void BuildVersionDiff_ClassifiesSyntheticCatalogChanges()
    {
        var prior = new CatalogSnapshotDocument(
            Version: "v1",
            CatalogVersion: "cat-1",
            SourceKind: "test",
            SourceRef: "prior",
            SourceCommit: "prior",
            Tools:
            [
                ToolEntry("removed_tool", "stable", summary: "removed"),
                ToolEntry("promoted_tool", "experimental", summary: "same"),
                ToolEntry("changed_tool", "stable", summary: "old", outputSchema: """{"type":"object","properties":{"old":{"type":"string"}}}""")
            ],
            Resources: [],
            Prompts:
            [
                PromptEntry("changed_prompt", "experimental", [new("workspaceId", "string", true, null, "Old description")])
            ]);

        var current = new CatalogSnapshotDocument(
            Version: "v2",
            CatalogVersion: "cat-2",
            SourceKind: "test",
            SourceRef: "current",
            SourceCommit: "current",
            Tools:
            [
                ToolEntry("added_tool", "stable", summary: "added"),
                ToolEntry("promoted_tool", "stable", summary: "same"),
                ToolEntry("changed_tool", "stable", summary: "new", outputSchema: """{"type":"object","properties":{"new":{"type":"string"}}}""")
            ],
            Resources: [],
            Prompts:
            [
                PromptEntry("changed_prompt", "experimental", [new("workspaceId", "string", true, null, "New description")])
            ]);

        var diff = ServerSurfaceCatalog.BuildVersionDiff(prior, current);

        CollectionAssert.AreEqual(new[] { "added_tool" }, diff.Tools.Added.Select(entry => entry.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "removed_tool" }, diff.Tools.Removed.Select(entry => entry.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "promoted_tool" }, diff.Tools.Promoted.Select(entry => entry.Name).ToArray());

        var changedTool = diff.Tools.Changed.Single(entry => entry.Name == "changed_tool");
        CollectionAssert.AreEquivalent(
            new[] { "summary", "outputSchema" },
            changedTool.ChangedFields.Select(field => field.Field).ToArray());

        var changedPrompt = diff.Prompts.Changed.Single(entry => entry.Name == "changed_prompt");
        CollectionAssert.AreEqual(
            new[] { "parameters" },
            changedPrompt.ChangedFields.Select(field => field.Field).ToArray());

        Assert.AreEqual(1, diff.Summary.Added);
        Assert.AreEqual(1, diff.Summary.Removed);
        Assert.AreEqual(1, diff.Summary.Promoted);
        Assert.AreEqual(2, diff.Summary.Changed);
    }

    [TestMethod]
    public void CreateVersionDiff_V231ToCurrent_UsesStoredReleaseSnapshot()
    {
        var diff = ServerSurfaceCatalog.CreateVersionDiff("v2.3.1", "latest");

        Assert.AreEqual("2.3.1", diff.FromVersion);
        Assert.AreEqual(ServerSurfaceCatalog.CurrentReleaseVersion, diff.ToVersion);
        Assert.AreEqual("git-tag", diff.SnapshotSources.From.SourceKind);
        Assert.AreEqual("v2.3.1", diff.SnapshotSources.From.SourceRef);
        Assert.AreEqual("e147875", diff.SnapshotSources.From.SourceCommit);

        CollectionAssert.AreEquivalent(
            new[] { "workspace_readiness_report", "workspace_support_bundle" },
            diff.Tools.Added.Select(entry => entry.Name).ToArray());
        CollectionAssert.Contains(diff.Resources.Added.Select(entry => entry.Name).ToArray(), "server_catalog_version_diff");

        // The full changed-tool set: get_completions (summary) and server_info (outputSchema)
        // pre-date this catalog version; the three workspace-status-family tools below changed
        // their outputSchema because restore-required-vs-build-conflation added `buildRequired`
        // to WorkspaceStatusSummaryDto, which every workspace-status tool serializes.
        CollectionAssert.AreEquivalent(
            new[] { "get_completions", "server_info", "workspace_status", "workspace_health", "workspace_list" },
            diff.Tools.Changed.Select(entry => entry.Name).ToArray());

        var changedTool = diff.Tools.Changed.Single(entry => entry.Name == "get_completions");
        CollectionAssert.AreEqual(new[] { "summary" }, changedTool.ChangedFields.Select(field => field.Field).ToArray());

        var changedServerInfo = diff.Tools.Changed.Single(entry => entry.Name == "server_info");
        CollectionAssert.AreEqual(new[] { "outputSchema" }, changedServerInfo.ChangedFields.Select(field => field.Field).ToArray());

        foreach (var workspaceToolName in new[] { "workspace_status", "workspace_health", "workspace_list" })
        {
            var changedWorkspaceTool = diff.Tools.Changed.Single(entry => entry.Name == workspaceToolName);
            CollectionAssert.AreEqual(
                new[] { "outputSchema" },
                changedWorkspaceTool.ChangedFields.Select(field => field.Field).ToArray(),
                $"{workspaceToolName} changed only its outputSchema (buildRequired added to WorkspaceStatusSummaryDto).");
        }

        Assert.AreEqual(3, diff.Summary.Added);
        Assert.AreEqual(0, diff.Summary.Removed);
        Assert.AreEqual(0, diff.Summary.Promoted);
        Assert.AreEqual(5, diff.Summary.Changed);
    }

    [TestMethod]
    public void ServerCatalogVersionDiffResource_PublishesCamelCasePayload()
    {
        var json = ServerResources.GetServerCatalogVersionDiff("2.3.1", "current");
        using var document = JsonDocument.Parse(json);

        Assert.IsTrue(document.RootElement.TryGetProperty("fromVersion", out var fromVersion));
        Assert.AreEqual("2.3.1", fromVersion.GetString());
        Assert.IsTrue(document.RootElement.TryGetProperty("summary", out var summary));
        Assert.AreEqual(3, summary.GetProperty("added").GetInt32());
        Assert.IsTrue(document.RootElement.TryGetProperty("tools", out var tools));
        Assert.IsTrue(tools.TryGetProperty("changed", out var changed));
        CollectionAssert.AreEquivalent(
            new[] { "get_completions", "server_info", "workspace_status", "workspace_health", "workspace_list" },
            changed.EnumerateArray().Select(entry => entry.GetProperty("name").GetString()).ToArray());
    }

    [TestMethod]
    public void CreateVersionDiff_UnsupportedPair_ThrowsClearError()
    {
        var ex = Assert.ThrowsExactly<ArgumentException>(
            () => ServerSurfaceCatalog.CreateVersionDiff("v2.3.0", "current"));

        StringAssert.Contains(ex.Message, "Unsupported catalog diff 'v2.3.0' -> 'current'");
        StringAssert.Contains(ex.Message, "v2.3.1");
        StringAssert.Contains(ex.Message, "latest");
    }

    [TestMethod]
    public void ServerCatalogVersionDiffResource_UnsupportedPair_ReturnsInvalidArgumentEnvelope()
    {
        var json = ServerResources.GetServerCatalogVersionDiff("v2.3.0", "current");
        using var document = JsonDocument.Parse(json);

        Assert.IsTrue(document.RootElement.GetProperty("error").GetBoolean());
        Assert.AreEqual("InvalidArgument", document.RootElement.GetProperty("category").GetString());
        Assert.AreEqual(
            "roslyn://server/catalog-diff/{fromVersion}/{toVersion}",
            document.RootElement.GetProperty("tool").GetString());
        StringAssert.Contains(document.RootElement.GetProperty("message").GetString() ?? string.Empty, "Unsupported catalog diff");
        StringAssert.Contains(document.RootElement.GetProperty("message").GetString() ?? string.Empty, "v2.3.1");
    }

    [TestMethod]
    public void McpToolMetadata_RequiredOnEveryTool_MatchesCatalogEntry()
    {
        // Item 2 (v1.18): every [McpServerTool] method MUST carry [McpToolMetadata]. The
        // attribute's category/tier/readOnly/destructive/summary must match the
        // hand-maintained ServerSurfaceCatalog.Tools entry. Together these two checks make
        // it structurally impossible for a new tool to ship with stale or missing catalog
        // metadata: the name-parity test catches missing catalog rows, and this test
        // catches missing attributes plus any field-level drift.
        var assembly = typeof(ServerTools).Assembly;
        var catalogByName = ServerSurfaceCatalog.Tools.ToDictionary(
            entry => entry.Name, entry => entry, StringComparer.Ordinal);

        var drift = new List<string>();
        var missingAttribute = new List<string>();
        foreach (var method in assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)))
        {
            var serverTool = method.GetCustomAttribute<McpServerToolAttribute>();
            if (serverTool is null) continue;

            var metadata = method.GetCustomAttribute<McpToolMetadataAttribute>();
            if (metadata is null)
            {
                missingAttribute.Add($"{serverTool.Name}: declared on {method.DeclaringType?.FullName}.{method.Name} but lacks [McpToolMetadata]");
                continue;
            }

            if (!catalogByName.TryGetValue(serverTool.Name!, out var entry))
            {
                drift.Add($"{serverTool.Name}: catalog entry missing");
                continue;
            }

            if (!string.Equals(entry.Category, metadata.Category, StringComparison.Ordinal))
                drift.Add($"{serverTool.Name}: Category mismatch (attr='{metadata.Category}', catalog='{entry.Category}')");
            if (!string.Equals(entry.SupportTier, metadata.SupportTier, StringComparison.Ordinal))
                drift.Add($"{serverTool.Name}: SupportTier mismatch (attr='{metadata.SupportTier}', catalog='{entry.SupportTier}')");
            if (entry.ReadOnly != metadata.ReadOnly)
                drift.Add($"{serverTool.Name}: ReadOnly mismatch (attr={metadata.ReadOnly}, catalog={entry.ReadOnly})");
            if (entry.Destructive != metadata.Destructive)
                drift.Add($"{serverTool.Name}: Destructive mismatch (attr={metadata.Destructive}, catalog={entry.Destructive})");
            if (!string.Equals(entry.Summary, metadata.Summary, StringComparison.Ordinal))
                drift.Add($"{serverTool.Name}: Summary mismatch");
        }

        Assert.AreEqual(0, missingAttribute.Count,
            "Tools missing [McpToolMetadata]:\n  " + string.Join("\n  ", missingAttribute));
        Assert.AreEqual(0, drift.Count,
            "McpToolMetadata drift detected:\n  " + string.Join("\n  ", drift));
    }

    [TestMethod]
    public void GetCompletions_DescriptionAndCatalogGuideMemberAccessPositioning()
    {
        var method = typeof(SymbolTools).GetMethod(
            nameof(SymbolTools.GetCompletions),
            BindingFlags.Public | BindingFlags.Static);
        Assert.IsNotNull(method, "SymbolTools.GetCompletions must exist.");

        var description = method.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()?.Description ?? string.Empty;
        StringAssert.Contains(description, "member/token position");
        StringAssert.Contains(description, "not the literal dot column");
        StringAssert.Contains(description, "probe_position");

        var entry = ServerSurfaceCatalog.Tools.SingleOrDefault(t => t.Name == "get_completions");
        Assert.IsNotNull(entry, "get_completions must be present in the tool catalog.");
        StringAssert.Contains(entry.Summary, "member/token position");
        StringAssert.Contains(entry.Summary, "not the literal dot column");
        StringAssert.Contains(entry.Summary, "probe_position");
    }

    [TestMethod]
    public async Task ServerInfo_IncludesSurfaceSupportSummary()
    {
        var json = await ServerTools.GetServerInfo(
            new FakeWorkspaceManager(),
            new NuGetVersionChecker(new TestHttpClientFactory()));
        using var doc = JsonDocument.Parse(json);

        Assert.IsTrue(doc.RootElement.TryGetProperty("surface", out var surface));
        Assert.IsTrue(surface.TryGetProperty("tools", out var tools));
        Assert.IsTrue(tools.TryGetProperty("stable", out var stableTools));
        Assert.IsTrue(stableTools.GetInt32() > 0);
        Assert.IsTrue(tools.TryGetProperty("experimental", out _));
    }

    [TestMethod]
    public async Task ServerInfo_IncludesResourceServerNameHints()
    {
        var json = await ServerTools.GetServerInfo(
            new FakeWorkspaceManager(),
            new NuGetVersionChecker(new TestHttpClientFactory()));
        using var doc = JsonDocument.Parse(json);

        Assert.IsTrue(doc.RootElement.TryGetProperty("resourceServerNames", out var hints));
        Assert.AreEqual("roslyn", hints.GetProperty("canonical").GetString());

        var aliases = hints.GetProperty("aliases")
            .EnumerateArray()
            .Select(alias => alias.GetString())
            .ToArray();
        CollectionAssert.Contains(aliases, "roslyn");
        CollectionAssert.Contains(aliases, "plugin:roslyn-mcp:roslyn");
        CollectionAssert.Contains(aliases, "plugin_roslyn-mcp_roslyn");

        var guidance = hints.GetProperty("probeGuidance").GetString() ?? string.Empty;
        StringAssert.Contains(guidance, "do not synthesize underscore variants");
    }

    // get-prompt-text-publish-parameter-schema: catalog prompt entries must publish a
    // parameters[] array per prompt so callers can build parametersJson for get_prompt_text
    // without a 2-roundtrip learn-then-invoke loop. The 3-param `debug_test_failure` (1 required
    // + 2 optional) is the representative shape from the plan: it covers required-parameter
    // serialization, optional-parameter default-value serialization (string? defaulting to null),
    // and the DI-service / CancellationToken exclusion (ITestRunnerService and `ct` must NOT
    // appear).
    [TestMethod]
    public void Prompts_PublishParameterSchema_OnCatalogEntry()
    {
        var promptEntries = ServerSurfaceCatalog.Prompts;
        // Every prompt must carry a non-null Parameters list — the empty-prompt case is allowed
        // (some prompts take no user-facing args) but null is reserved for tools / resources.
        foreach (var prompt in promptEntries)
        {
            Assert.IsNotNull(prompt.Parameters,
                $"Prompt '{prompt.Name}' must publish a parameters[] array (got null).");
        }

        var debug = promptEntries.Single(p => p.Name == "debug_test_failure");
        Assert.IsNotNull(debug.Parameters);
        Assert.AreEqual(3, debug.Parameters!.Count,
            "debug_test_failure exposes 3 user-facing params: workspaceId, projectName, filter "
            + "(ITestRunnerService and CancellationToken are DI-resolved and must be excluded).");

        var workspaceId = debug.Parameters.Single(p => p.Name == "workspaceId");
        Assert.AreEqual("string", workspaceId.Type);
        Assert.IsTrue(workspaceId.Required, "workspaceId has no default and must be Required=true.");
        Assert.IsNull(workspaceId.DefaultValue);
        Assert.AreEqual("The workspace session identifier", workspaceId.Description);

        var projectName = debug.Parameters.Single(p => p.Name == "projectName");
        Assert.AreEqual("string", projectName.Type, "Nullable reference type collapses to bare 'string' in the schema.");
        Assert.IsFalse(projectName.Required, "projectName has a `= null` default and must be Required=false.");
        Assert.IsNull(projectName.DefaultValue, "DefaultValue=null mirrors the C# `= null` default.");
        Assert.AreEqual("Optional: specific test project name", projectName.Description);

        var filter = debug.Parameters.Single(p => p.Name == "filter");
        Assert.IsFalse(filter.Required);
        Assert.IsNull(filter.DefaultValue);
    }

    [TestMethod]
    public void Prompts_ExcludeServicesAndCancellationToken_FromParameterSchema()
    {
        // Guard against schema regression: every prompt parameter list must be free of
        // CancellationToken and Microsoft.Extensions.* / interface-typed DI services. The
        // catalog publishes only the values an agent must supply via parametersJson.
        foreach (var prompt in ServerSurfaceCatalog.Prompts)
        {
            Assert.IsNotNull(prompt.Parameters);
            foreach (var p in prompt.Parameters!)
            {
                Assert.AreNotEqual("CancellationToken", p.Type,
                    $"Prompt '{prompt.Name}' parameter '{p.Name}' must not surface CancellationToken.");

                // Heuristic: a leading 'I' followed by an uppercase letter strongly suggests an
                // interface-typed DI service slipped through (e.g. 'IDiagnosticService'). The
                // PromptParameterIndex.IsServiceType filter must drop those before publish.
                var looksLikeInterface = p.Type.Length > 1
                    && p.Type[0] == 'I'
                    && char.IsUpper(p.Type[1]);
                Assert.IsFalse(looksLikeInterface,
                    $"Prompt '{prompt.Name}' parameter '{p.Name}' looks like an interface type ('{p.Type}') — DI services must be filtered out before publishing.");
            }
        }
    }

    [TestMethod]
    public void Prompts_PublishedAsJson_UsesCamelCaseSchemaFields()
    {
        // End-to-end: the camelCase serializer config that ServerResources uses must produce
        // exactly the field names callers will read — `parameters` (not `Parameters`),
        // `defaultValue` (not `DefaultValue`), etc. A regression to PascalCase would silently
        // break consumers parsing by JSON-key.
        var page = ServerSurfaceCatalog.PageEntries(
            ServerSurfaceCatalog.Prompts, offset: 0, limit: 200, resourceName: "test");
        var json = JsonSerializer.Serialize(page, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        using var doc = JsonDocument.Parse(json);
        var entries = doc.RootElement.GetProperty("entries");
        var debug = entries.EnumerateArray().Single(e => e.GetProperty("name").GetString() == "debug_test_failure");

        Assert.IsTrue(debug.TryGetProperty("parameters", out var parameters),
            "Catalog prompt entry must publish 'parameters' (camelCase) field.");
        Assert.AreEqual(JsonValueKind.Array, parameters.ValueKind);
        Assert.AreEqual(3, parameters.GetArrayLength());

        var workspaceIdParam = parameters.EnumerateArray().Single(p => p.GetProperty("name").GetString() == "workspaceId");
        Assert.AreEqual("string", workspaceIdParam.GetProperty("type").GetString());
        Assert.IsTrue(workspaceIdParam.GetProperty("required").GetBoolean());
        Assert.IsTrue(workspaceIdParam.TryGetProperty("defaultValue", out var dv) && dv.ValueKind == JsonValueKind.Null,
            "Required parameter must serialize defaultValue as JSON null.");
        Assert.AreEqual("The workspace session identifier",
            workspaceIdParam.GetProperty("description").GetString());
    }

    [TestMethod]
    public void Prompts_OptionalNullableValueType_FormatsAsCSharpKeywordWithQuestionMark()
    {
        // refactor_and_validate has `int? endLine = null` and `int? endColumn = null` —
        // the schema must report Required=false and DefaultValue=null with type label "int?".
        // Picking this prompt (alongside debug_test_failure) gives coverage of nullable
        // value-type parameters distinct from nullable reference-type parameters.
        var refactor = ServerSurfaceCatalog.Prompts.SingleOrDefault(p => p.Name == "refactor_and_validate");
        Assert.IsNotNull(refactor);
        Assert.IsNotNull(refactor!.Parameters);

        var endLine = refactor.Parameters!.Single(p => p.Name == "endLine");
        Assert.AreEqual("int?", endLine.Type, "Nullable<int> formats as 'int?' for readability.");
        Assert.IsFalse(endLine.Required);
        Assert.IsNull(endLine.DefaultValue);
    }

    [TestMethod]
    public void AllArrayTypedToolParameters_DescriptionContainsNativeJsonArrayPhrase()
    {
        // filepaths-array-vs-stringified-tool-description-clarification: the 9 in-scope
        // string[]-typed parameters must each carry "native JSON array" in their [Description]
        // so LLM clients do not mis-encode array values as stringified JSON.
        var assembly = typeof(ServerTools).Assembly;

        var inScopePairs = new HashSet<(string toolName, string paramName)>(
        [
            ("analyze_snippet", "usings"),
            ("evaluate_csharp", "imports"),
            ("workspace_warm", "projects"),
            ("validate_workspace", "changedFilePaths"),
            ("get_complexity_metrics", "filePaths"),
            ("get_msbuild_properties", "includedNames"),
            ("extract_interface_preview", "memberNames"),
            ("scaffold_type_preview", "interfaces"),
            ("parameter_object_preview", "dtoFolders"),
        ]);

        var failures = new List<string>();
        var foundCount = 0;

        foreach (var method in assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)))
        {
            var serverTool = method.GetCustomAttribute<McpServerToolAttribute>();
            if (serverTool is null) continue;

            var toolName = serverTool.Name ?? string.Empty;
            foreach (var param in method.GetParameters())
            {
                if (!inScopePairs.Contains((toolName, param.Name ?? string.Empty))) continue;

                foundCount++;
                var description = param.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()?.Description ?? string.Empty;
                if (!description.Contains("native JSON array", StringComparison.Ordinal))
                    failures.Add($"({toolName}, {param.Name}): [Description] does not contain 'native JSON array'. Got: \"{description}\"");
            }
        }

        Assert.IsTrue(foundCount > 0,
            "No in-scope (toolName, paramName) pairs found — assembly may have failed to load or tool names changed.");
        Assert.AreEqual(0, failures.Count,
            "Array-typed parameters missing 'native JSON array' in [Description]:\n  " + string.Join("\n  ", failures));
    }

    private static string[] GetRegisteredNames<TAttribute>(Assembly assembly)
        where TAttribute : Attribute
    {
        return assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Select(method => method.GetCustomAttribute<TAttribute>())
            .OfType<TAttribute>()
            .Select(GetName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    private static SurfaceEntry ToolEntry(
        string name,
        string supportTier,
        string summary,
        string? outputSchema = null) =>
        new(
            "tool",
            name,
            "test",
            supportTier,
            ReadOnly: true,
            Destructive: false,
            summary,
            UriTemplate: null,
            Parameters: null,
            OutputSchema: outputSchema is null ? null : JsonNode.Parse(outputSchema));

    private static SurfaceEntry PromptEntry(
        string name,
        string supportTier,
        IReadOnlyList<PromptParameterEntry> parameters) =>
        new(
            "prompt",
            name,
            "test",
            supportTier,
            ReadOnly: true,
            Destructive: false,
            "prompt",
            UriTemplate: null,
            Parameters: parameters,
            OutputSchema: null);

    private static string GetName<TAttribute>(TAttribute attribute)
        where TAttribute : Attribute
    {
        return attribute switch
        {
            McpServerToolAttribute tool => tool.Name ?? throw new InvalidOperationException("Tool name is required."),
            McpServerResourceAttribute resource => resource.Name ?? throw new InvalidOperationException("Resource name is required."),
            McpServerPromptAttribute prompt => prompt.Name ?? throw new InvalidOperationException("Prompt name is required."),
            _ => throw new InvalidOperationException($"Unsupported attribute type: {typeof(TAttribute).Name}")
        };
    }

    private sealed class FakeWorkspaceManager : IWorkspaceManager
    {
        public event Action<string>? WorkspaceClosed { add { } remove { } }
        public event Action<string>? WorkspaceReloaded { add { } remove { } }

        public Task<WorkspaceStatusDto> LoadAsync(string path, EvictPolicy evictPolicy, CancellationToken ct) => throw new NotSupportedException();
        public Task<WorkspaceStatusDto> ReloadAsync(string workspaceId, CancellationToken ct) => throw new NotSupportedException();
        public bool ContainsWorkspace(string workspaceId) => false;
        public bool IsStale(string workspaceId) => false;
        public bool Close(string workspaceId) => throw new NotSupportedException();
        public IReadOnlyList<WorkspaceStatusDto> ListWorkspaces() => [];
        public WorkspaceStatusDto GetStatus(string workspaceId) => throw new NotSupportedException();
        public Task<WorkspaceStatusDto> GetStatusAsync(string workspaceId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ProjectGraphDto GetProjectGraph(string workspaceId) => throw new NotSupportedException();
        public Task<IReadOnlyList<GeneratedDocumentDto>> GetSourceGeneratedDocumentsAsync(string workspaceId, string? projectName, CancellationToken ct) => throw new NotSupportedException();
        public Task<string?> GetSourceTextAsync(string workspaceId, string filePath, CancellationToken ct) => throw new NotSupportedException();
        public int GetCurrentVersion(string workspaceId) => throw new NotSupportedException();
        public void RestoreVersion(string workspaceId, int version) => throw new NotSupportedException();
        public Solution GetCurrentSolution(string workspaceId) => throw new NotSupportedException();
        public bool TryApplyChanges(string workspaceId, Solution newSolution) => throw new NotSupportedException();
        public Project? GetProject(string workspaceId, string projectNameOrPath) => null;
    }
}
