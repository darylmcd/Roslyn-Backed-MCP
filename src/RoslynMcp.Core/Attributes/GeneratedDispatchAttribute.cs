namespace RoslynMcp.Core.Attributes;

/// <summary>
/// Marks a partial method declaration on a <c>*Tools</c> class under
/// <c>src/RoslynMcp.Host.Stdio/Tools/</c> as the anchor for a generator-emitted MCP
/// tool shim. The <c>McpToolShimGenerator</c> (in <c>analyzers/McpToolShimGenerator/</c>)
/// reads this attribute and emits the matching <c>partial</c> implementation under
/// <c>obj/Generated/RoslynMcp.Host.Stdio/McpToolShimGenerator/&lt;ToolClass&gt;.g.cs</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Anchor shape (phase 1.2+).</b> The attribute lives on the Tools class partial
/// method declaration — NOT on the backing service-interface method. Lifting to the
/// service was rejected because multiple tools share a service method (e.g.
/// <c>IRefactoringService.ApplyRefactoringAsync</c> backs every <c>*_apply</c> tool);
/// attribute-lifting would collide. The Tools class stays the source of truth for
/// the MCP-facing surface; the generator is mechanical.
/// </para>
/// <para>
/// See <c>ai_docs/plans/20260421T123658Z_post-audit-followups.md</c> § "Workstream 1 —
/// phase 1.2 pilot criteria" for the design rationale.
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

    /// <summary>
    /// The service interface type whose member backs the tool shim. The emitter looks
    /// up <see cref="Method"/> on this type and generates a closure that invokes it.
    /// Kept as <see cref="Type"/>? (not a string metadata name) so typos produce
    /// compile errors on the <c>typeof(...)</c> argument rather than silent runtime
    /// misses.
    /// </summary>
    public Type? Service { get; init; }

    /// <summary>
    /// The name of the service-interface method to delegate to. Kept as
    /// <see cref="string"/>? (attribute args are restricted to compile-time constants)
    /// and passed via <c>nameof(I...Service.XxxAsync)</c> at the call site so a rename
    /// of the backing method propagates.
    /// </summary>
    public string? Method { get; init; }
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
