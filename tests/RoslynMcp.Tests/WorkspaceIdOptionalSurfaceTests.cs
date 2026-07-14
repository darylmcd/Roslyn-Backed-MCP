using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

/// <summary>
/// Coverage for the <c>workspace-id-optional-readonly-surface-flip</c> initiative (pilot subset).
/// Asserts, via the reflection-derived <see cref="ToolParameterIndex"/>, that the pilot read-only
/// tools now advertise <c>workspaceId</c> as OPTIONAL (so agents proactively omit it and order-1's
/// read-path middleware resolves it), while write/destructive tools — and the deferred
/// <c>symbol_search</c> — keep it REQUIRED.
///
/// <para>
/// The pilot flipped 3 tools (<c>go_to_definition</c>, <c>find_references</c>, <c>document_symbols</c>);
/// the bounded full-sweep sub-batch adds <c>compile_check</c> — each has a <c>workspaceId</c> followed
/// only by optional parameters, so the flip is a pure in-place default with no parameter reordering
/// and no caller churn. <c>symbol_search</c> is intentionally EXCLUDED: a required <c>query</c>
/// parameter follows its <c>workspaceId</c>, so making it optional would force a reorder that breaks
/// ~16 positional call sites — it is folded into the deferred full read-only sweep follow-on
/// alongside the remaining unflipped read-only methods.
/// </para>
/// </summary>
[TestClass]
public sealed class WorkspaceIdOptionalSurfaceTests
{
    private static readonly string[] FlippedPilotTools =
        ["go_to_definition", "find_references", "document_symbols", "compile_check"];

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
    public void FlippedTool_WithNullWorkspaceId_ThrowsGuidedInvalidArgument()
    {
        // The residual case the middleware cannot resolve (omitted id + zero workspaces loaded +
        // nothing discoverable) reaches the tool body with workspaceId == null. The body guard
        // must throw a structured ArgumentException (the filter classifies it to InvalidArgument)
        // pointing the caller at workspace_load — not NRE on the downstream gate call. The guard
        // runs before any service is touched, so null DI args are safe here.
        var ex = Assert.ThrowsExactly<ArgumentException>(() =>
            SymbolTools.GetDocumentSymbols(server: null!, gate: null!, symbolSearchService: null!, workspaceId: null));

        Assert.AreEqual("workspaceId", ex.ParamName);
        StringAssert.Contains(ex.Message, "workspace_load",
            "The guard must steer the caller to workspace_load rather than fail opaquely.");
    }

    [TestMethod]
    public void CompileCheck_WithNullWorkspaceId_ThrowsGuidedInvalidArgument()
    {
        // compile_check's body guard mirrors the symbol-tool guard above. The pre-gate severity +
        // pagination validations pass for the defaults (severity=null, offset=0, limit=50), so the
        // null-workspaceId case reaches RequireResolvedWorkspaceId before any service or gate is
        // touched — null DI args are safe here.
        var ex = Assert.ThrowsExactly<ArgumentException>(() =>
            CompileCheckTools.CompileCheck(gate: null!, compileCheckService: null!, workspaceId: null));

        Assert.AreEqual("workspaceId", ex.ParamName);
        StringAssert.Contains(ex.Message, "workspace_load",
            "The guard must steer the caller to workspace_load rather than fail opaquely.");
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
