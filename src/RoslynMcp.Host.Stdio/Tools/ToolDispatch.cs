using System.Text.Json;
using RoslynMcp.Core.Services;
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
///   <item><see cref="ApplyByTokenAsync{TDto}(IWorkspaceExecutionGate, IPreviewStore, string, Func{CancellationToken, Task{TDto}}, CancellationToken)"/>
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
    /// gate, invokes <paramref name="serviceCall"/>, and returns the indented-JSON-
    /// serialized DTO.
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
        CancellationToken ct)
        => ApplyByTokenAsync(gate, previewStore.PeekWorkspaceId, previewToken, serviceCall, ct);

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
    /// primary <see cref="ApplyByTokenAsync{TDto}(IWorkspaceExecutionGate, IPreviewStore, string, Func{CancellationToken, Task{TDto}}, CancellationToken)"/>
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
        var wsId = peekWorkspaceId(previewToken)
            ?? throw new PreviewTokenStaleException(
                previewToken,
                $"Preview token '{previewToken}' has expired or was invalidated: the workspace was reloaded after the preview was created, dropping the stored solution snapshot. Re-issue the paired *_preview call against the current workspace.");
        return gate.RunWriteAsync(wsId, async c =>
        {
            var result = await serviceCall(c).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, JsonDefaults.Indented);
        }, ct);
    }

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
    /// <returns>The DTO serialized with <see cref="JsonDefaults.Indented"/>.</returns>
    public static async Task<string> ReadByWorkspaceIdWithEvictionRetryAsync<TDto>(
        IWorkspaceExecutionGate gate,
        IWorkspaceManager? workspaceManager,
        string workspaceId,
        Func<string, CancellationToken, Task<TDto>> serviceCall,
        CancellationToken ct)
    {
        try
        {
            return await ReadByWorkspaceIdAsync(gate, workspaceId, c => serviceCall(workspaceId, c), ct)
                .ConfigureAwait(false);
        }
        catch (KeyNotFoundException ex) when (workspaceManager is not null)
        {
            var reloadedId = await TryReloadEvictedWorkspaceForRetryAsync(workspaceManager, workspaceId, ex, ct)
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
    /// <b>What the LRU scan does and does not protect
    /// (<c>lru-eviction-concurrent-reader-safety-overstated</c>):</b> <c>WorkspaceManager</c>'s
    /// eviction scan skips only sessions holding their <c>LoadLock</c>, which is acquired
    /// exclusively while a <c>workspace_load</c>/reload is in flight. It does <b>not</b> consult
    /// <c>WorkspaceExecutionGate</c>'s per-workspace reader/writer lock, so a workspace with
    /// in-flight reads or writes can still be evicted and disposed mid-operation; that caller
    /// observes <see cref="WorkspaceEvictedException"/> (a <see cref="KeyNotFoundException"/>) on
    /// its next <c>WorkspaceManager</c> lookup. Earlier revisions of these remarks claimed an
    /// in-flight workspace is never yanked out from under a concurrent caller — that guarantee
    /// holds for the load path only, not for gated readers/writers. The gap is documented rather
    /// than closed because <c>WorkspaceExecutionGate</c> already depends on
    /// <c>IWorkspaceManager</c>, so gating eviction from inside <c>WorkspaceManager</c> would be a
    /// DI cycle. Pinned by
    /// <c>WorkspaceCapLruEvictionTests.LruEviction_WhileGatedReadInFlight_EvictsAnyway_AndReaderObservesEviction</c>.
    /// </para>
    /// <para>
    /// A failed reload is deliberately not surfaced: the original eviction exception is richer
    /// (it carries the id, the recorded path, and a copy-pasteable <c>workspace_load</c> recovery
    /// hint), and the caller rethrows it, so no diagnostic is lost.
    /// </para>
    /// </remarks>
    internal static async Task<string?> TryReloadEvictedWorkspaceForRetryAsync(
        IWorkspaceManager workspaceManager,
        string workspaceId,
        KeyNotFoundException failure,
        CancellationToken ct)
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
        catch (Exception)
        {
            return null;
        }
    }
}
