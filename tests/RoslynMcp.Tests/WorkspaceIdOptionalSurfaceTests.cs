using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Tests;

/// <summary>
/// Coverage for the <c>workspace-id-optional-readonly-surface-flip</c> initiative (pilot subset).
/// Asserts, via the reflection-derived <see cref="ToolParameterIndex"/>, that the pilot read-only
/// tools now advertise <c>workspaceId</c> as OPTIONAL (so agents proactively omit it and order-1's
/// read-path middleware resolves it), while write/destructive tools — and the deferred
/// <c>symbol_search</c> — keep it REQUIRED.
///
/// <para>
/// Pilot scope is 3 tools (<c>go_to_definition</c>, <c>find_references</c>, <c>document_symbols</c>)
/// whose <c>workspaceId</c> is followed only by optional parameters, so the flip is a pure in-place
/// default with no parameter reordering and no caller churn. <c>symbol_search</c> is intentionally
/// EXCLUDED from this pilot: a required <c>query</c> parameter follows its <c>workspaceId</c>, so
/// making it optional would force a reorder that breaks ~16 positional call sites — it is folded
/// into the deferred full read-only sweep follow-on alongside the remaining ~45 methods.
/// </para>
/// </summary>
[TestClass]
public sealed class WorkspaceIdOptionalSurfaceTests
{
    private static readonly string[] FlippedPilotTools =
        ["go_to_definition", "find_references", "document_symbols"];

    // Deferred to the follow-on (required query param forces a reorder) + a sample of tools that
    // must KEEP workspaceId required: writers, destructive, and not-yet-swept read-only tools.
    private static readonly string[] StillRequiredTools =
        ["symbol_search", "apply_text_edit", "workspace_close", "symbol_info"];

    [TestMethod]
    public void PilotReadOnlyTools_ExposeWorkspaceIdAsOptional()
    {
        foreach (var tool in FlippedPilotTools)
        {
            var schema = ToolParameterIndex.GetParameter(tool, "workspaceId");
            Assert.IsNotNull(schema, $"{tool} should declare a workspaceId parameter.");
            Assert.AreEqual("string", schema!.Type, $"{tool}.workspaceId should remain a string.");
            Assert.IsFalse(schema.Required,
                $"{tool}.workspaceId must be Required=false after the flip so agents proactively omit it.");
        }
    }

    [TestMethod]
    public void NonPilotTools_KeepWorkspaceIdRequired()
    {
        foreach (var tool in StillRequiredTools)
        {
            var schema = ToolParameterIndex.GetParameter(tool, "workspaceId");
            Assert.IsNotNull(schema, $"{tool} should declare a workspaceId parameter.");
            Assert.IsTrue(schema!.Required,
                $"{tool}.workspaceId must stay Required=true — only the pilot read-only subset is flipped " +
                "in this initiative (symbol_search is deferred; mutating tools are out of scope entirely).");
        }
    }
}
