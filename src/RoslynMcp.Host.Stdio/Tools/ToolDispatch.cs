using System.Text.Json;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Host.Stdio.Diagnostics;
using RoslynMcp.Host.Stdio.Security;
using RoslynMcp.Roslyn.Contracts;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// Shared runtime dispatch helper for MCP tool shims under
/// <c>src/RoslynMcp.Host.Stdio/Tools/*Tools.cs</c>. Every <c>*_apply</c>, <c>*_preview</c>,
/// and read-only tool method whose body is pure "resolve workspace → gate → service →
/// serialize" delegates inline to the workspace-ID guard or one of the dispatch
/// methods below; 87+ of ~157 stable tool shims currently route through this helper.
/// </summary>
/// <remarks>
/// <para>
/// <b>Dispatch shapes</b> — one helper method per workspace-gating pattern:
/// <list type="bullet">
///   <item><see cref="ApplyByTokenAsync{TDto}(IWorkspaceExecutionGate, IPreviewStore, string, Func{CancellationToken, Task{TDto}}, CancellationToken, PreviewKind)"/>
///     — <c>*_apply</c> tools that receive an opaque preview token; resolves the
///     workspaceId via the store's <c>PeekWorkspaceId</c> peek and runs under the
///     per-workspace write gate. Also has a delegate-peek overload for preview stores
///     that don't derive from <see cref="IPreviewStore"/>
///     (<c>ICompositePreviewStore</c>, <c>IProjectMutationPreviewStore</c>).
///   </item>
///   <item><see cref="PreviewWithWorkspaceIdAsync{TDto}(IWorkspaceExecutionGate, string, Func{CancellationToken, Task{TDto}}, CancellationToken)"/>
///     — <c>*_preview</c> tools that accept an explicit <c>workspaceId</c> and stage
///     changes into the preview store under the per-workspace write gate.
///   </item>
///   <item><see cref="ReadByWorkspaceIdAsync{TDto}(IWorkspaceExecutionGate, string, Func{CancellationToken, Task{TDto}}, CancellationToken)"/>
///     — read-only tools that accept an explicit <c>workspaceId</c> and run under the
///     per-workspace read gate.
///   </item>
///   <item><see cref="ReadByWorkspaceIdWithEvictionRetryAsync{TDto}(IWorkspaceExecutionGate, IWorkspaceManager?, string, Func{string, CancellationToken, Task{TDto}}, CancellationToken, ILoggerFactory?)"/>
///     — eviction-tolerant variant of <see cref="ReadByWorkspaceIdAsync{TDto}(IWorkspaceExecutionGate, string, Func{CancellationToken, Task{TDto}}, CancellationToken)"/>
///     for read-only tools (<c>compile_check</c>) that should transparently rehydrate an
///     evicted workspace from its recorded <c>LoadedPath</c> and retry the read once, instead
///     of surfacing the miss to the caller.
///   </item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why a hand-written helper, not a source generator?</b> The original WS1 plan
/// (phase 1.1) built an <c>McpToolShimGenerator</c> to emit these dispatch bodies
/// from service-interface attributes. The phase-1.2 canary discovered an
/// irreconcilable conflict with <c>ModelContextProtocol.Analyzers.XmlToDescriptionGenerator</c>
/// (shipped in the MCP SDK) — both generators claim the same <c>public static partial</c>
/// declaration slot, causing CS0756. Phases 1.2-1.6 pivoted to inline delegation
/// (each tool body becomes one call into this helper). Phase 1.7 retired the unused
/// generator scaffolding. See <c>CHANGELOG.md</c> Unreleased §Maintenance.
/// </para>
///
/// <para>
/// <b>API shape choice:</b> the helpers take a <see cref="Func{T, TResult}"/>
/// (<c>Func&lt;CancellationToken, Task&lt;TDto&gt;&gt;</c>) rather than a generic
/// <c>TArgs</c> + <c>Func&lt;string, TArgs, …&gt;</c>. Each tool body captures its
/// own parameters via closure, so a <c>TArgs</c> bag would force a wrapper record
/// per tool (~157 tools with different signatures) without reducing boilerplate.
/// </para>
/// </remarks>
internal static class ToolDispatch
{
    /// <summary>
    /// Narrows an optional workspace id after read-path auto-resolution. The middleware
    /// normally fills the value; this guard provides a structured corrective error when no
    /// workspace is loaded or discoverable.
    /// </summary>
    public static string RequireResolvedWorkspaceId(string? workspaceId) =>
        string.IsNullOrEmpty(workspaceId)
            ? throw new ArgumentException(
                "workspaceId was omitted but no workspace is loaded and none could be discovered. " +
                "Call workspace_load(path=…) first, or pass workspaceId explicitly.",
                nameof(workspaceId))
            : workspaceId;

    /// <summary>
    /// Dispatch body for <c>*_apply</c> tools that receive an opaque preview token.
    /// Resolves the workspaceId from the token via
    /// <see cref="IPreviewStore.PeekWorkspaceId"/>, acquires the per-workspace write
    /// gate, revalidates the preview's write set against the sanctioned-root boundary
    /// (<see cref="RevalidateChangedPathsAsync"/> — preview-apply-token-write-path-toctou),
    /// invokes <paramref name="serviceCall"/>, and returns the indented-JSON-serialized DTO.
    /// The delegate-peek overload below performs NO such revalidation because its stores
    /// (<c>ICompositePreviewStore</c>, <c>IProjectMutationPreviewStore</c>) expose only a
    /// token-to-workspace lookup and no write-set contract.
    /// </summary>
    /// <typeparam name="TDto">The DTO type returned by the underlying service call.</typeparam>
    /// <param name="gate">The workspace execution gate.</param>
    /// <param name="previewStore">The preview store holding the token → workspaceId mapping.</param>
    /// <param name="previewToken">The opaque token returned by the paired <c>*_preview</c> tool.</param>
    /// <param name="serviceCall">
    /// A closure the generator emits that invokes the service method with the
    /// <paramref name="previewToken"/> and the gate-provided <see cref="CancellationToken"/>.
    /// Kept as <c>Func&lt;CancellationToken, Task&lt;TDto&gt;&gt;</c> rather than
    /// <c>Func&lt;string, CancellationToken, Task&lt;TDto&gt;&gt;</c> so the generator
    /// can emit a parameter-less lambda identical to the existing hand-written body.
    /// </param>
    /// <param name="ct">The caller's cancellation token.</param>
    /// <param name="expectedKind">
    /// preview-token-apply-route-provenance: the producer family this route accepts. Required so a
    /// call site cannot silently become permissive by dropping its route binding.
    /// </param>
    /// <param name="invokedRoute">
    /// The public MCP route the caller invoked. Required because a producer kind may be shared by
    /// several routes, so it cannot be derived from <paramref name="expectedKind"/>.
    /// </param>
    /// <returns>
    /// The DTO serialized with <see cref="JsonDefaults.Indented"/>.
    /// </returns>
    /// <exception cref="PreviewTokenStaleException">
    /// Thrown when <paramref name="previewToken"/> is not present in
    /// <paramref name="previewStore"/> (expired, invalidated by an auto-reload version
    /// bump via <c>InvalidateOnVersionBump</c>, or never stored). Distinct from a bare
    /// <see cref="KeyNotFoundException"/> so <c>ToolErrorHandler</c> can surface the
    /// dedicated <c>PreviewTokenStale</c> category — callers re-issue the paired
    /// <c>*_preview</c> against the post-reload workspace rather than mistaking the
    /// failure for "workspace not found" or "symbol not found."
    /// </exception>
    public static Task<string> ApplyByTokenAsync<TDto>(
        IWorkspaceExecutionGate gate,
        IPreviewStore previewStore,
        string previewToken,
        Func<CancellationToken, Task<TDto>> serviceCall,
        CancellationToken ct,
        PreviewKind expectedKind,
        string invokedRoute)
    {
        var wsId = RequirePreviewWorkspaceId(previewStore.PeekWorkspaceId, previewToken);
        return gate.RunWriteAsync(wsId, async c =>
        {
            // preview-token-apply-route-provenance: refuse a token minted by a different producer
            // family BEFORE any mutation — and before the boundary revalidation below, so a
            // wrong-route redemption never reaches RevalidateChangedPathsAsync or TryApplyChanges.
            RequireCompatibleProducer(
                previewStore,
                previewToken,
                expectedKind,
                invokedRoute);

            // preview-apply-token-write-path-toctou: revalidate the preview's write set against
            // the sanctioned-root boundary AT REDEMPTION TIME, under the same write gate that
            // holds until the persistence write. The only boundary check before this ran in the
            // paired *_preview call — up to a full preview-TTL earlier — leaving the whole TTL as
            // a validate-to-write window for a symlink/junction swap.
            await RevalidateChangedPathsAsync(previewStore, previewToken, wsId, c).ConfigureAwait(false);
            var result = await serviceCall(c).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, JsonDefaults.Indented);
        }, ct);
    }

    /// <summary>
    /// preview-token-apply-route-provenance: binds a redeeming <c>*_apply</c> route to the producer
    /// family that minted the token, using the non-consuming
    /// <see cref="IPreviewStore.PeekKind(string)"/> peek. Fails OPEN on unknown provenance and
    /// CLOSED only on a present-and-incompatible one — mirroring the
    /// <see cref="IPreviewStore.PeekChangedPaths(string)"/> "null means unknown, skip, never
    /// verified" convention:
    /// <list type="bullet">
    ///   <item><paramref name="expectedKind"/> is <see cref="PreviewKind.Unspecified"/> — an
    ///     explicit permissive binding. In-tree apply shims pass a concrete kind; this arm remains
    ///     for direct callers and compatibility with out-of-tree hosts.</item>
    ///   <item>The stored kind is <see cref="PreviewKind.Unspecified"/> — an untagged producer or an
    ///     out-of-tree store that does not track provenance. Permissive by the enum's own documented
    ///     contract; every apply route MUST accept it.</item>
    ///   <item>Both are concrete and differ — throw before mutation.</item>
    /// </list>
    /// The guard does not consume or TTL-refresh the entry.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the token's recorded producer is concrete and incompatible with the route. The
    /// message names BOTH the actual producer's <c>*_preview</c> tool and the <c>*_apply</c> route
    /// the token should be redeemed through, so the caller can correct the call without guessing.
    /// Surfaces through <c>ToolErrorHandler</c>'s generic envelope.
    /// </exception>
    internal static void RequireCompatibleProducer(
        IPreviewStore previewStore,
        string previewToken,
        PreviewKind expectedKind,
        string invokedRoute)
    {
        if (expectedKind == PreviewKind.Unspecified)
        {
            return;
        }

        var actualKind = previewStore.PeekKind(previewToken);
        if (actualKind == PreviewKind.Unspecified || actualKind == expectedKind)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Preview token '{previewToken}' was produced by `{PreviewToolFor(actualKind)}`; " +
            $"redeem it via `{ApplyRouteFor(actualKind)}` (or the generic `apply_with_verify`), " +
            $"not `{invokedRoute}`, which only accepts " +
            $"`{PreviewToolFor(expectedKind)}` tokens. No workspace changes were made.");
    }

    /// <summary>
    /// preview-token-route-map-centralization: THE single kind → <c>*_preview</c> tool-name map.
    /// The apply-route half is deliberately absent — <see cref="ApplyRouteFor"/> derives it by
    /// looking the preview-tool name up in <c>ServerSurfaceCatalog.PreviewApplyRoutes</c>,
    /// which the catalog already maintains for the whole preview surface. Keeping only the
    /// kind → preview-tool hop here means a route rename is edited in exactly one place.
    /// <see cref="PreviewKind.Unspecified"/> is intentionally NOT a key: it means "no provenance
    /// claim" and has no producer tool.
    /// </summary>
    private static readonly IReadOnlyDictionary<PreviewKind, string> _previewToolsByKind =
        new Dictionary<PreviewKind, string>
        {
            [PreviewKind.SymbolRename] = "rename_preview",
            [PreviewKind.FormatDocument] = "format_document_preview",
            [PreviewKind.FormatRange] = "format_range_preview",
            [PreviewKind.OrganizeUsings] = "organize_usings_preview",
            [PreviewKind.CodeFix] = "code_fix_preview",
            [PreviewKind.MultiFileEdit] = "preview_multi_file_edit",
            [PreviewKind.FileCreate] = "create_file_preview",
            [PreviewKind.FileDelete] = "delete_file_preview",
            [PreviewKind.FileMove] = "move_file_preview",
            [PreviewKind.CodeAction] = "preview_code_action",
            [PreviewKind.FixAll] = "fix_all_preview",
            [PreviewKind.ExtractMethod] = "extract_method_preview",
            [PreviewKind.ExtractInterface] = "extract_interface_preview",
            [PreviewKind.ExtractType] = "extract_type_preview",
            [PreviewKind.MoveTypeToFile] = "move_type_to_file_preview",
            // preview-token-route-binding-bulk-family: `replace_invocation_preview` shares this
            // kind — it redeems through the same `bulk_replace_type_apply` route — so the map names
            // the route-eponymous producer. See PreviewKind.BulkReplaceType for why the two
            // producers deliberately collapse onto one member.
            [PreviewKind.BulkReplaceType] = "bulk_replace_type_preview",
            [PreviewKind.RemoveDeadCode] = "remove_dead_code_preview",
        };

    /// <summary>
    /// preview-token-apply-route-provenance: the <c>*_preview</c> tool name that mints each
    /// <see cref="PreviewKind"/>, for the actionable half of the mismatch message.
    /// <para>
    /// preview-token-route-map-centralization: fails LOUD on an unmapped kind rather than
    /// returning a plausible-looking catch-all. Only ever reached with a concrete kind —
    /// <see cref="RequireCompatibleProducer"/> returns early on
    /// <see cref="PreviewKind.Unspecified"/> for both the route's and the token's kind, so the
    /// permissive contract never touches this map.
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="kind"/> has no entry — a new <see cref="PreviewKind"/> member
    /// was added without its companion map entry.
    /// </exception>
    internal static string PreviewToolFor(PreviewKind kind) =>
        _previewToolsByKind.TryGetValue(kind, out var previewTool)
            ? previewTool
            : throw UnmappedPreviewKind(kind, "ToolDispatch._previewToolsByKind (kind → *_preview tool)");

    /// <summary>
    /// preview-token-apply-route-provenance: the named <c>*_apply</c> route bound to each
    /// <see cref="PreviewKind"/>, for the recovery half of the mismatch message.
    /// <para>
    /// preview-token-route-map-centralization: derived, not re-listed — the kind resolves to its
    /// <c>*_preview</c> tool via <see cref="PreviewToolFor"/>, and the tool resolves to its apply
    /// route via <see cref="ServerSurfaceCatalog.TryGetCompatibleApplyRoute"/>. Same-assembly
    /// internal reach; no visibility change was needed.
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="kind"/> is unmapped in either hop.
    /// </exception>
    internal static string ApplyRouteFor(PreviewKind kind)
    {
        var previewTool = PreviewToolFor(kind);
        return ServerSurfaceCatalog.TryGetCompatibleApplyRoute(previewTool, out var applyRoute)
            ? applyRoute
            : throw UnmappedPreviewKind(
                kind,
                $"ServerSurfaceCatalog.PreviewApplyRoutes (no entry for `{previewTool}`)");
    }

    /// <summary>
    /// preview-token-route-map-centralization: the fail-loud arm shared by
    /// <see cref="PreviewToolFor"/> and <see cref="ApplyRouteFor"/>. Names the offending member
    /// and BOTH map sites so the fix is mechanical.
    /// </summary>
    private static InvalidOperationException UnmappedPreviewKind(PreviewKind kind, string missingSite) =>
        new($"PreviewKind.{kind} has no entry in {missingSite}. Every concrete PreviewKind must be " +
            "mapped in BOTH ToolDispatch._previewToolsByKind (kind → *_preview tool) and " +
            "ServerSurfaceCatalog.PreviewApplyRoutes (*_preview tool → *_apply route); add the " +
            "missing entry rather than letting the preview-token provenance guard report a route " +
            "the caller cannot use.");

    /// <summary>
    /// preview-apply-token-write-path-toctou: runs every path in the preview's write set through
    /// <see cref="ClientRootPathValidator.ValidatePathAgainstRootsAsync"/> against the host-bound
    /// <see cref="SecurityOptionsSnapshot"/>. Skips silently — documented as "unknown", never
    /// "verified" — when either the snapshot is <see langword="null"/> (unbooted host, i.e. unit
    /// tests; the production host always populates it at startup) or the store cannot enumerate
    /// the write set (<see cref="IPreviewStore.PeekChangedPaths"/> returned
    /// <see langword="null"/>). Throws <see cref="ArgumentException"/> when any target falls
    /// outside the configured boundary, refusing the apply before the service call runs.
    /// <para>
    /// The boundary re-derived here must be the SAME one that admitted the workspace, never a
    /// narrower one: a workspace loaded with <c>workspace_load(expandSanctionedRoots: true)</c>
    /// legitimately holds documents under the widened parent root, so
    /// <see cref="RootExpansionGrantRegistry"/> replays that load-time grant. The widening still
    /// takes effect only when the operator-owned <see cref="RoslynMcp.Roslyn.Services.SecurityOptions.AllowRootExpansion"/>
    /// is set — an out-of-boundary path is refused on an expansion-loaded workspace exactly as on
    /// any other. The current request's <see cref="ModelContextProtocol.Server.McpServer"/> is
    /// supplied from <see cref="RequestMcpServerContext"/> so legacy MCP Roots remain an
    /// additional narrowing boundary during redemption, matching preview-time validation.
    /// </para>
    /// </summary>
    internal static async Task RevalidateChangedPathsAsync(
        IPreviewStore previewStore,
        string previewToken,
        string workspaceId,
        CancellationToken ct)
    {
        var securityOptions = SecurityOptionsSnapshot.Value;
        if (securityOptions is null)
        {
            return;
        }

        var changedPaths = previewStore.PeekChangedPaths(previewToken);
        if (changedPaths is null)
        {
            return;
        }

        var expandSanctionedRoots = RootExpansionGrantRegistry.IsGranted(workspaceId);
        foreach (var path in changedPaths)
        {
            await ClientRootPathValidator.ValidatePathAgainstRootsAsync(
                RequestMcpServerContext.Current,
                path,
                ct,
                securityOptions: securityOptions,
                expandSanctionedRoots: expandSanctionedRoots).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Overload for <c>*_apply</c> tools that use a non-<see cref="IPreviewStore"/> preview
    /// store (e.g. <c>ICompositePreviewStore</c>, <c>IProjectMutationPreviewStore</c>) whose
    /// only requirement from the dispatch helper is a token → workspaceId peek. Accepts the
    /// peek as a <see cref="Func{T, TResult}"/> delegate so the helper stays decoupled from
    /// the concrete store interface — phase-1.6 widening to cover
    /// <c>ApplyCompositePreview</c> and <c>ApplyProjectMutation</c>, which otherwise match
    /// the same 7-line dispatch pattern but can't consume the primary overload because their
    /// store interfaces don't derive from <see cref="IPreviewStore"/>.
    /// </summary>
    /// <typeparam name="TDto">The DTO type returned by the underlying service call.</typeparam>
    /// <param name="gate">The workspace execution gate.</param>
    /// <param name="peekWorkspaceId">
    /// A closure that returns the workspaceId for a given preview token without consuming
    /// the entry, or <see langword="null"/> if the token is expired or not found. Typically
    /// a method group reference to the store's <c>PeekWorkspaceId</c>.
    /// </param>
    /// <param name="previewToken">The opaque token returned by the paired <c>*_preview</c> tool.</param>
    /// <param name="serviceCall">
    /// A closure that invokes the service method with the <paramref name="previewToken"/>
    /// and the gate-provided <see cref="CancellationToken"/>. Kept as
    /// <c>Func&lt;CancellationToken, Task&lt;TDto&gt;&gt;</c> for the same reason as the
    /// primary <see cref="ApplyByTokenAsync{TDto}(IWorkspaceExecutionGate, IPreviewStore, string, Func{CancellationToken, Task{TDto}}, CancellationToken, PreviewKind)"/>
    /// overload: closure capture matches the existing hand-written shim bodies byte-for-byte.
    /// </param>
    /// <param name="ct">The caller's cancellation token.</param>
    /// <returns>The DTO serialized with <see cref="JsonDefaults.Indented"/>.</returns>
    /// <exception cref="PreviewTokenStaleException">
    /// Thrown when <paramref name="peekWorkspaceId"/> returns <see langword="null"/>
    /// (token expired, invalidated by an auto-reload version bump, or never stored).
    /// Distinct from a bare <see cref="KeyNotFoundException"/> so <c>ToolErrorHandler</c>
    /// can surface the dedicated <c>PreviewTokenStale</c> category and direct callers to
    /// re-issue the paired <c>*_preview</c>.
    /// </exception>
    public static Task<string> ApplyByTokenAsync<TDto>(
        IWorkspaceExecutionGate gate,
        Func<string, string?> peekWorkspaceId,
        string previewToken,
        Func<CancellationToken, Task<TDto>> serviceCall,
        CancellationToken ct)
    {
        var wsId = RequirePreviewWorkspaceId(peekWorkspaceId, previewToken);
        return gate.RunWriteAsync(wsId, async c =>
        {
            var result = await serviceCall(c).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, JsonDefaults.Indented);
        }, ct);
    }

    private static string RequirePreviewWorkspaceId(
        Func<string, string?> peekWorkspaceId,
        string previewToken) =>
        peekWorkspaceId(previewToken)
        ?? throw new PreviewTokenStaleException(
            previewToken,
            $"Preview token '{previewToken}' has expired or was invalidated: the workspace was reloaded after the preview was created, dropping the stored solution snapshot. Re-issue the paired *_preview call against the current workspace.");

    /// <summary>
    /// Dispatch body for <c>*_preview</c> tools that take an explicit
    /// <paramref name="workspaceId"/> and stage changes into the preview store under
    /// the per-workspace write gate.
    /// </summary>
    /// <typeparam name="TDto">The DTO type returned by the underlying service call.</typeparam>
    /// <param name="gate">The workspace execution gate.</param>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="serviceCall">
    /// A closure the generator emits that invokes the service method with the
    /// tool's parameters (captured via closure) and the gate-provided
    /// <see cref="CancellationToken"/>.
    /// </param>
    /// <param name="ct">The caller's cancellation token.</param>
    /// <returns>The DTO serialized with <see cref="JsonDefaults.Indented"/>.</returns>
    public static Task<string> PreviewWithWorkspaceIdAsync<TDto>(
        IWorkspaceExecutionGate gate,
        string workspaceId,
        Func<CancellationToken, Task<TDto>> serviceCall,
        CancellationToken ct)
        => gate.RunWriteAsync(workspaceId, async c =>
        {
            var result = await serviceCall(c).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, JsonDefaults.Indented);
        }, ct);

    /// <summary>
    /// Dispatch body for read-only tools that take an explicit
    /// <paramref name="workspaceId"/> and run under the per-workspace read gate.
    /// </summary>
    /// <typeparam name="TDto">The DTO type returned by the underlying service call.</typeparam>
    /// <param name="gate">The workspace execution gate.</param>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="serviceCall">
    /// A closure the generator emits that invokes the service method with the
    /// tool's parameters (captured via closure) and the gate-provided
    /// <see cref="CancellationToken"/>.
    /// </param>
    /// <param name="ct">The caller's cancellation token.</param>
    /// <returns>The DTO serialized with <see cref="JsonDefaults.Indented"/>.</returns>
    public static Task<string> ReadByWorkspaceIdAsync<TDto>(
        IWorkspaceExecutionGate gate,
        string workspaceId,
        Func<CancellationToken, Task<TDto>> serviceCall,
        CancellationToken ct)
        => gate.RunReadAsync(workspaceId, async c =>
        {
            var result = await serviceCall(c).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, JsonDefaults.Indented);
        }, ct);

    /// <summary>
    /// Eviction-tolerant variant of
    /// <see cref="ReadByWorkspaceIdAsync{TDto}(IWorkspaceExecutionGate, string, Func{CancellationToken, Task{TDto}}, CancellationToken)"/>.
    /// Runs the read once; when the workspace lookup misses because the session was evicted,
    /// rehydrates it from the evicted session's recorded <c>LoadedPath</c> and retries the read
    /// exactly once against the fresh <c>workspaceId</c>. Any other failure — and any second
    /// failure — propagates unchanged so the caller's existing error envelope is preserved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>workspace-eviction-no-auto-retry-on-tool-call</c>. Two distinct miss shapes reach this
    /// helper and both must recover:
    /// </para>
    /// <list type="number">
    ///   <item><description><b>Evicted before the call started</b> (the common case) —
    ///     <c>WorkspaceExecutionGate.RunPerWorkspaceAsync</c> runs a cheap
    ///     <c>ContainsWorkspace</c> precheck that only consults the live session dictionary and
    ///     throws a bare <see cref="KeyNotFoundException"/>, never reaching
    ///     <c>WorkspaceManager.GetRequiredSession</c>'s richer classification. The helper
    ///     therefore re-probes the manager (<see cref="TryReclassifyAsEvicted"/>) to recover the
    ///     <see cref="WorkspaceEvictedException"/> the gate short-circuited.</description></item>
    ///   <item><description><b>Evicted mid-call</b> (the narrow race) — the deeper
    ///     <c>GetRequiredSession</c> lookup inside the service body throws
    ///     <see cref="WorkspaceEvictedException"/> directly; it is used as-is.</description></item>
    /// </list>
    /// <para>
    /// <paramref name="workspaceManager"/> is optional so direct (non-DI) callers keep the exact
    /// pre-existing behaviour: with no manager the helper degrades to
    /// <see cref="ReadByWorkspaceIdAsync{TDto}(IWorkspaceExecutionGate, string, Func{CancellationToken, Task{TDto}}, CancellationToken)"/>
    /// and never retries.
    /// </para>
    /// </remarks>
    /// <typeparam name="TDto">The DTO type returned by the underlying service call.</typeparam>
    /// <param name="gate">The workspace execution gate.</param>
    /// <param name="workspaceManager">
    /// The workspace manager used to reclassify the miss and rehydrate the evicted session, or
    /// <see langword="null"/> to disable the retry entirely.
    /// </param>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="serviceCall">
    /// A closure that invokes the service method for a given workspace id. Unlike the
    /// non-retrying helpers this takes the id as a parameter rather than capturing it, because
    /// the retry leg must run against the post-reload id.
    /// </param>
    /// <param name="ct">The caller's cancellation token.</param>
    /// <param name="loggerFactory">
    /// Optional DI-bound logger factory used to log a swallowed reload failure during the
    /// eviction-retry attempt, or <see langword="null"/> to skip logging (direct/test callers).
    /// </param>
    /// <returns>The DTO serialized with <see cref="JsonDefaults.Indented"/>.</returns>
    public static async Task<string> ReadByWorkspaceIdWithEvictionRetryAsync<TDto>(
        IWorkspaceExecutionGate gate,
        IWorkspaceManager? workspaceManager,
        string workspaceId,
        Func<string, CancellationToken, Task<TDto>> serviceCall,
        CancellationToken ct,
        ILoggerFactory? loggerFactory = null)
    {
        try
        {
            return await ReadByWorkspaceIdAsync(gate, workspaceId, c => serviceCall(workspaceId, c), ct)
                .ConfigureAwait(false);
        }
        catch (KeyNotFoundException ex) when (workspaceManager is not null)
        {
            var reloadedId = await TryReloadEvictedWorkspaceForRetryAsync(workspaceManager, workspaceId, ex, ct, loggerFactory)
                .ConfigureAwait(false);
            if (reloadedId is null)
            {
                // Not an eviction, or not recoverable — rethrow so the original structured
                // envelope reaches the caller unchanged (no silent retry loop).
                throw;
            }

            return await ReadByWorkspaceIdAsync(gate, reloadedId, c => serviceCall(reloadedId, c), ct)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Re-probes <paramref name="workspaceManager"/> for <paramref name="workspaceId"/> purely to
    /// recover the eviction classification that a cheap <c>ContainsWorkspace</c> precheck
    /// discarded. Returns the <see cref="WorkspaceEvictedException"/> when the manager holds an
    /// eviction record (or a host-recycle signal) for the id, or <see langword="null"/> when the
    /// miss is a genuine never-loaded/typo'd id — or when the workspace turns out to be live
    /// again (a concurrent reload won the race), in which case there is nothing to rehydrate.
    /// </summary>
    /// <remarks>
    /// Uses <see cref="IWorkspaceManager.GetCurrentVersion"/> rather than
    /// <see cref="IWorkspaceManager.GetStatus"/> as the probe: both route through
    /// <c>GetRequiredSession</c> (the sole source of the eviction classification), but
    /// <c>GetCurrentVersion</c> returns a single <see langword="int"/> instead of building a full
    /// status DTO with per-project rollups. The probe only ever runs on an already-failing call,
    /// never on the hot success path.
    /// </remarks>
    internal static WorkspaceEvictedException? TryReclassifyAsEvicted(
        IWorkspaceManager workspaceManager,
        string workspaceId)
    {
        try
        {
            _ = workspaceManager.GetCurrentVersion(workspaceId);
            return null;
        }
        catch (WorkspaceEvictedException evicted)
        {
            return evicted;
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Rehydrates an evicted workspace from its recorded <c>LoadedPath</c> and returns the fresh
    /// workspace id, or <see langword="null"/> when the failure is not a recoverable eviction (no
    /// eviction record, no recorded path — e.g. a cross-process recycle — or the reload itself
    /// failed). Callers rethrow the original failure on <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why <see cref="EvictPolicy.Lru"/> and not <see cref="EvictPolicy.Strict"/>:</b> the
    /// evidenced failure is LRU eviction under <c>MaxConcurrentWorkspaces</c> pressure — the
    /// evicted session's slot was freed and immediately consumed by the load that triggered the
    /// eviction, so the cap is still saturated at retry time. A <see cref="EvictPolicy.Strict"/>
    /// reload would throw <see cref="InvalidOperationException"/> ("already tracking N
    /// workspaces") for the primary case this helper exists to fix, recovering nothing and
    /// replacing the caller's eviction envelope with a confusing cap-reached one.
    /// <see cref="EvictPolicy.Lru"/> makes room by evicting the current least-recently-used
    /// session. This is exactly the recovery a caller would perform by hand
    /// (<c>workspace_load(path, evictPolicy: "lru")</c>), just without the round trip.
    /// </para>
    /// <para>
    /// <b>What LRU eviction protects (<c>lru-eviction-gate-layer-execution</c>):</b> candidate
    /// SELECTION skips only sessions holding their <c>LoadLock</c> — acquired exclusively while a
    /// <c>workspace_load</c>/reload is in flight — so selection alone is not a safety boundary.
    /// Eviction EXECUTION supplies it: <c>WorkspaceManager</c> closes the chosen candidate under
    /// <c>IWorkspaceExecutionGate.RunWriteAsync</c> (reached through a lazily-resolved gate
    /// reference, which is what keeps the back-edge from being a constructor-level DI cycle), the
    /// same per-workspace writer-lock nesting <c>workspace_close</c> and <c>workspace_reload</c>
    /// use. An in-flight gated reader or writer therefore drains BEFORE its workspace is removed
    /// and disposed, instead of being yanked out from under mid-operation. What remains possible
    /// is the ordinary between-calls case: a caller idle between two tool calls can find its
    /// workspace evicted and observes <see cref="WorkspaceEvictedException"/> (a
    /// <see cref="KeyNotFoundException"/>) on the next lookup — which is exactly what this helper
    /// recovers from. Pinned by
    /// <c>WorkspaceCapLruEvictionTests.LruEviction_WhileGatedReadInFlight_BlocksUntilReaderDrains</c>.
    /// </para>
    /// <para>
    /// A failed reload is not additionally surfaced by this helper: the caller rethrows the
    /// original failure it was given, unchanged. That original failure is only richly
    /// diagnostic — carrying the id, the recorded path, and a copy-pasteable
    /// <c>workspace_load</c> recovery hint — when it arrived here already as a
    /// <see cref="WorkspaceEvictedException"/> (the mid-call case). On the more common
    /// gate-precheck miss the caller instead rethrows the bare <see cref="KeyNotFoundException"/>
    /// the gate raised, which carries no eviction context at all — on that path a failed reload
    /// previously left no trace anywhere. This helper now logs the reload failure itself
    /// (the evicted workspace id, the recorded <c>LoadedPath</c>, and the reload exception) via
    /// the optional <paramref name="loggerFactory"/>, so the failure is no longer silent on
    /// either path.
    /// </para>
    /// </remarks>
    /// <param name="loggerFactory">
    /// Optional DI-bound logger factory used to log a reload failure, or
    /// <see langword="null"/> to skip logging (direct/test callers).
    /// </param>
    internal static async Task<string?> TryReloadEvictedWorkspaceForRetryAsync(
        IWorkspaceManager workspaceManager,
        string workspaceId,
        KeyNotFoundException failure,
        CancellationToken ct,
        ILoggerFactory? loggerFactory = null)
    {
        var evicted = failure as WorkspaceEvictedException
            ?? TryReclassifyAsEvicted(workspaceManager, workspaceId);

        if (evicted?.LoadedPath is not { Length: > 0 } loadedPath)
        {
            return null;
        }

        try
        {
            var reloaded = await workspaceManager.LoadAsync(loadedPath, EvictPolicy.Lru, ct)
                .ConfigureAwait(false);
            return reloaded.WorkspaceId;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            CreateLogger(loggerFactory)?.LogWarning(
                ex,
                "Failed to rehydrate evicted workspace {WorkspaceId} from LoadedPath \"{LoadedPath}\": {ExceptionType}: {ExceptionMessage}",
                workspaceId,
                loadedPath,
                ex.GetType().Name,
                ex.Message);
            return null;
        }
    }

    private static ILogger? CreateLogger(ILoggerFactory? loggerFactory) =>
        loggerFactory?.CreateLogger(typeof(ToolDispatch).FullName ?? nameof(ToolDispatch));
}
