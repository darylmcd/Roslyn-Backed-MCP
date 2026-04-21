using System.Text.Json;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// Runtime dispatch helper for MCP tool shims emitted by <c>McpToolShimGenerator</c>.
/// Exists hand-written (as opposed to inline per-shim bodies) so reviewers can audit
/// the shared lock / serialization path in one place — the generator delegates to
/// these three methods and emits only the per-tool binding lambda.
/// </summary>
/// <remarks>
/// <para>
/// <b>Phase 1.1 status:</b> the helper exists and is unit-tested, but no hand-written
/// shim delegates to it yet. Phase 1.2+ migrates tool groups one at a time; each
/// migrated shim becomes a one-line call into one of the three helpers below.
/// </para>
///
/// <para>
/// <b>API shape choice:</b> the helpers take a <see cref="Func{T, TResult}"/>
/// (<c>Func&lt;CancellationToken, Task&lt;TDto&gt;&gt;</c>) rather than a generic
/// <c>TArgs</c> + <c>Func&lt;string, TArgs, …&gt;</c>. The shim generator captures
/// per-tool parameters via closure, so a <c>TArgs</c> bag would force a wrapper
/// record per tool (~22 tools with different signatures) without reducing boilerplate.
/// The closure shape matches the existing hand-written shim bodies byte-for-byte.
/// </para>
/// </remarks>
internal static class ToolDispatch
{
    /// <summary>
    /// Dispatch body for <c>*_apply</c> tools that receive an opaque preview token
    /// (<see cref="DispatchKind.ApplyByToken"/>). Resolves the workspaceId from the
    /// token via <see cref="IPreviewStore.PeekWorkspaceId"/>, acquires the
    /// per-workspace write gate, invokes <paramref name="serviceCall"/>, and returns
    /// the indented-JSON-serialized DTO.
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
    /// the per-workspace write gate (<see cref="DispatchKind.PreviewWithWorkspaceId"/>).
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
    /// <paramref name="workspaceId"/> and run under the per-workspace read gate
    /// (<see cref="DispatchKind.ReadByWorkspaceId"/>).
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
