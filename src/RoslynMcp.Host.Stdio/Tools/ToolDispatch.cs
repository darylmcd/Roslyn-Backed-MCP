using System.Text.Json;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// Shared runtime dispatch helper for MCP tool shims under
/// <c>src/RoslynMcp.Host.Stdio/Tools/*Tools.cs</c>. Every <c>*_apply</c>, <c>*_preview</c>,
/// and read-only tool method whose body is pure "resolve workspace → gate → service →
/// serialize" delegates inline to one of the three methods below; 87+ of ~157 stable
/// tool shims currently route through this helper.
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
    /// <exception cref="KeyNotFoundException">
    /// Thrown when <paramref name="previewToken"/> is not present in
    /// <paramref name="previewStore"/> (expired, invalidated, or never stored). The
    /// exception message matches the format used by the existing hand-written
    /// shims so existing error-envelope tests remain valid when shims migrate to
    /// this helper.
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
    /// <exception cref="KeyNotFoundException">
    /// Thrown when <paramref name="peekWorkspaceId"/> returns <see langword="null"/>. The
    /// exception message matches the format used by the existing hand-written shims so
    /// existing error-envelope tests remain valid when shims migrate to this helper.
    /// </exception>
    public static Task<string> ApplyByTokenAsync<TDto>(
        IWorkspaceExecutionGate gate,
        Func<string, string?> peekWorkspaceId,
        string previewToken,
        Func<CancellationToken, Task<TDto>> serviceCall,
        CancellationToken ct)
    {
        var wsId = peekWorkspaceId(previewToken)
            ?? throw new KeyNotFoundException($"Preview token '{previewToken}' not found or expired.");
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
}
