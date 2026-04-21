namespace RoslynMcp.Core.Attributes;

/// <summary>
/// Marks a service-interface method as the target of a generated MCP tool shim.
/// The <c>McpToolShimGenerator</c> (in <c>analyzers/McpToolShimGenerator/</c>)
/// reads this attribute plus the sibling <c>[McpServerTool]</c> / <c>[McpToolMetadata]</c>
/// attributes and emits a dispatcher under
/// <c>obj/Generated/RoslynMcp.Host.Stdio/McpToolShimGenerator/&lt;ToolGroup&gt;.g.cs</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Phase 1.1 status:</b> attribute + <see cref="DispatchKind"/> exist but no service
/// interface has adopted them yet. Phase 1.2+ lifts <c>[McpServerTool]</c> from the
/// hand-written <c>Tools/*.cs</c> method to the service-interface method, adds this
/// attribute with a <see cref="DispatchKind"/>, and deletes the hand-written shim.
/// </para>
/// <para>
/// See <c>ai_docs/plans/20260421T123658Z_post-audit-followups.md</c> § "Workstream 1 —
/// phase 1.1" for the full rollout plan across 7 PRs.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class GeneratedDispatchAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GeneratedDispatchAttribute"/> class.
    /// </summary>
    /// <param name="kind">The dispatch pattern the generator should emit for this method.</param>
    public GeneratedDispatchAttribute(DispatchKind kind)
    {
        Kind = kind;
    }

    /// <summary>
    /// Gets the dispatch pattern the generator emits for this method — one of
    /// <see cref="DispatchKind.ApplyByToken"/>, <see cref="DispatchKind.PreviewWithWorkspaceId"/>,
    /// or <see cref="DispatchKind.ReadByWorkspaceId"/>.
    /// </summary>
    public DispatchKind Kind { get; }
}

/// <summary>
/// Classifies an MCP tool method by its dispatch shape so the generator can emit the
/// correct shim body. Each kind corresponds to one hand-written helper on
/// <c>RoslynMcp.Host.Stdio.Tools.ToolDispatch</c>.
/// </summary>
public enum DispatchKind
{
    /// <summary>
    /// <c>*_apply</c> tools — receive a preview token, resolve
    /// <c>workspaceId</c> via <c>IPreviewStore.PeekWorkspaceId</c>, run under the
    /// per-workspace write-gate. Covers the 12-method <c>Apply*</c> cluster.
    /// </summary>
    ApplyByToken = 0,

    /// <summary>
    /// <c>*_preview</c> tools that take an explicit <c>workspaceId</c> parameter and
    /// stage changes into <c>IPreviewStore</c>. Run under the per-workspace write-gate.
    /// Covers the 10-method <c>Preview*</c> cluster.
    /// </summary>
    PreviewWithWorkspaceId = 1,

    /// <summary>
    /// Read-only tools that take an explicit <c>workspaceId</c> parameter and run
    /// under the per-workspace read-gate. Covers the smaller read-side clusters
    /// (e.g. <c>GetEditorConfigOptions</c>, <c>GetSecurityDiagnostics</c>).
    /// </summary>
    ReadByWorkspaceId = 2,
}
