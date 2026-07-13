using ModelContextProtocol.Protocol;
using RoslynMcp.Host.Stdio.Middleware;

namespace RoslynMcp.Tests;

/// <summary>
/// Coverage for <see cref="ElicitationAllowlistPolicy"/> — the elicitation-allowlist policy layer
/// extracted from <see cref="StructuredCallToolFilter"/> in the
/// <c>structuredcalltoolfilter-god-class-decompose</c> initiative. These assertions exercise the
/// policy members <b>directly</b> (not through the filter's thin delegates), pinning the
/// decomposed unit on its own so the policy contract is guarded even if the filter's forwarding
/// surface changes. The pre-existing <c>StructuredCallToolFilterElicitationTests</c> continue to
/// assert the same guarantees through the filter delegates, proving the delegates are
/// behavior-preserving.
/// </summary>
[TestClass]
public sealed class ElicitationAllowlistPolicyTests
{
    // ── HasElicitation ────────────────────────────────────────────────────────

    [TestMethod]
    public void HasElicitation_WhenCapabilityNonNull_ReturnsTrue()
    {
        var capabilities = new ClientCapabilities
        {
            Elicitation = new ElicitationCapability(),
        };

        Assert.IsTrue(ElicitationAllowlistPolicy.HasElicitation(capabilities),
            "An ElicitationCapability instance present on ClientCapabilities means the client " +
            "supports elicitation/create — the elicit recovery path is permitted.");
    }

    [TestMethod]
    public void HasElicitation_WhenCapabilitiesNull_ReturnsFalse()
    {
        Assert.IsFalse(ElicitationAllowlistPolicy.HasElicitation(null),
            "Null ClientCapabilities means no handshake-established capability set; refuse to elicit.");
    }

    [TestMethod]
    public void HasElicitation_WhenElicitationOmitted_ReturnsFalse()
    {
        var capabilities = new ClientCapabilities();

        Assert.IsFalse(ElicitationAllowlistPolicy.HasElicitation(capabilities),
            "Client must explicitly advertise the elicitation capability; absence means refuse.");
    }

    // ── IsSensitiveFieldName ──────────────────────────────────────────────────

    [TestMethod]
    public void IsSensitiveFieldName_FlagsCommonCredentialPatterns()
    {
        Assert.IsTrue(ElicitationAllowlistPolicy.IsSensitiveFieldName("password"));
        Assert.IsTrue(ElicitationAllowlistPolicy.IsSensitiveFieldName("Password"));
        Assert.IsTrue(ElicitationAllowlistPolicy.IsSensitiveFieldName("passwordHash"));
        Assert.IsTrue(ElicitationAllowlistPolicy.IsSensitiveFieldName("secret"));
        Assert.IsTrue(ElicitationAllowlistPolicy.IsSensitiveFieldName("clientSecret"));
        Assert.IsTrue(ElicitationAllowlistPolicy.IsSensitiveFieldName("credential"));
        Assert.IsTrue(ElicitationAllowlistPolicy.IsSensitiveFieldName("credentials"));
        Assert.IsTrue(ElicitationAllowlistPolicy.IsSensitiveFieldName("token"));
        Assert.IsTrue(ElicitationAllowlistPolicy.IsSensitiveFieldName("authToken"));
        Assert.IsTrue(ElicitationAllowlistPolicy.IsSensitiveFieldName("apiKey"));
        Assert.IsTrue(ElicitationAllowlistPolicy.IsSensitiveFieldName("api_key"));
        Assert.IsTrue(ElicitationAllowlistPolicy.IsSensitiveFieldName("api-key"));
        Assert.IsTrue(ElicitationAllowlistPolicy.IsSensitiveFieldName("ApiKey"));
        Assert.IsTrue(ElicitationAllowlistPolicy.IsSensitiveFieldName("auth"));
        Assert.IsTrue(ElicitationAllowlistPolicy.IsSensitiveFieldName("Authorization"));
        Assert.IsTrue(ElicitationAllowlistPolicy.IsSensitiveFieldName("private_key"));
        Assert.IsTrue(ElicitationAllowlistPolicy.IsSensitiveFieldName("privateKey"));
        Assert.IsTrue(ElicitationAllowlistPolicy.IsSensitiveFieldName("private-key"));
    }

    [TestMethod]
    public void IsSensitiveFieldName_DoesNotFlagBenignNames()
    {
        Assert.IsFalse(ElicitationAllowlistPolicy.IsSensitiveFieldName("path"));
        Assert.IsFalse(ElicitationAllowlistPolicy.IsSensitiveFieldName("workspaceId"));
        Assert.IsFalse(ElicitationAllowlistPolicy.IsSensitiveFieldName("filePath"));
        Assert.IsFalse(ElicitationAllowlistPolicy.IsSensitiveFieldName("query"));
        Assert.IsFalse(ElicitationAllowlistPolicy.IsSensitiveFieldName("verbose"));
        Assert.IsFalse(ElicitationAllowlistPolicy.IsSensitiveFieldName(""),
            "Empty string is not a sensitive name — it just isn't a name at all.");
        Assert.IsFalse(ElicitationAllowlistPolicy.IsSensitiveFieldName(null),
            "Null is not a sensitive name — it just isn't a name at all.");
    }

    // ── IsElicitationAllowedFor ───────────────────────────────────────────────

    [TestMethod]
    public void IsElicitationAllowedFor_WorkspaceLoadPath_ReturnsTrue()
    {
        Assert.IsTrue(ElicitationAllowlistPolicy.IsElicitationAllowedFor("workspace_load", "path"),
            "workspace_load.path must stay on the strict elicitation allowlist.");
    }

    [TestMethod]
    public void IsElicitationAllowedFor_RequiredWorkspaceId_ReturnsTrue()
    {
        Assert.IsFalse(ElicitationAllowlistPolicy.IsSensitiveFieldName("workspaceId"),
            "Security review: workspaceId is a non-secret session token derived from the loaded path.");
        Assert.IsTrue(ElicitationAllowlistPolicy.IsElicitationAllowedFor("workspace_status", "workspaceId"));
        Assert.IsTrue(ElicitationAllowlistPolicy.IsElicitationAllowedFor("compile_check", "workspaceId"));
    }

    [TestMethod]
    public void IsElicitationAllowedFor_WriteOrDestructiveWorkspaceId_ReturnsFalse()
    {
        Assert.IsFalse(ElicitationAllowlistPolicy.IsElicitationAllowedFor("apply_text_edit", "workspaceId"),
            "Missing workspaceId recovery must not auto-load-and-retry direct edit tools.");
        Assert.IsFalse(ElicitationAllowlistPolicy.IsElicitationAllowedFor("revert_last_apply", "workspaceId"),
            "Missing workspaceId recovery must not auto-load-and-retry destructive undo tools.");
        Assert.IsFalse(ElicitationAllowlistPolicy.IsElicitationAllowedFor("workspace_close", "workspaceId"),
            "Missing workspaceId recovery must not auto-load-and-retry destructive workspace lifecycle tools.");
    }

    [TestMethod]
    public void IsElicitationAllowedFor_RandomToolOrParam_ReturnsFalse()
    {
        Assert.IsFalse(ElicitationAllowlistPolicy.IsElicitationAllowedFor("workspace_load", "verbose"));
        Assert.IsFalse(ElicitationAllowlistPolicy.IsElicitationAllowedFor("symbol_search", "query"));
        Assert.IsFalse(ElicitationAllowlistPolicy.IsElicitationAllowedFor("unknown_tool", "workspaceId"));
    }

    [TestMethod]
    public void IsElicitationAllowedFor_RefusesSensitiveFields()
    {
        // Even if a sensitive name were on the allowlist, IsElicitationAllowedFor MUST refuse it.
        // MCP spec § Elicitation security: "Servers MUST NOT request sensitive information".
        Assert.IsFalse(ElicitationAllowlistPolicy.IsElicitationAllowedFor("workspace_load", "password"));
        Assert.IsFalse(ElicitationAllowlistPolicy.IsElicitationAllowedFor("workspace_load", "token"));
        Assert.IsFalse(ElicitationAllowlistPolicy.IsElicitationAllowedFor("workspace_load", "apiKey"));
        Assert.IsFalse(ElicitationAllowlistPolicy.IsElicitationAllowedFor("workspace_load", "secret"));
        Assert.IsFalse(ElicitationAllowlistPolicy.IsElicitationAllowedFor("workspace_load", "credential"));
        Assert.IsFalse(ElicitationAllowlistPolicy.IsElicitationAllowedFor("workspace_load", "Authorization"));
    }

    [TestMethod]
    public void IsElicitationAllowedFor_NullOrEmpty_ReturnsFalse()
    {
        Assert.IsFalse(ElicitationAllowlistPolicy.IsElicitationAllowedFor(null, "path"));
        Assert.IsFalse(ElicitationAllowlistPolicy.IsElicitationAllowedFor("workspace_load", null));
        Assert.IsFalse(ElicitationAllowlistPolicy.IsElicitationAllowedFor("", "path"));
        Assert.IsFalse(ElicitationAllowlistPolicy.IsElicitationAllowedFor("workspace_load", ""));
        Assert.IsFalse(ElicitationAllowlistPolicy.IsElicitationAllowedFor(null, null));
    }

    // ── IsWorkspaceIdRecoveryAllowedFor ───────────────────────────────────────

    [TestMethod]
    public void IsWorkspaceIdRecoveryAllowedFor_OptionalWorkspaceIdReadOnlyTool_ReturnsTrue()
    {
        // The recovery gate is decoupled from the Required flag, so read-only tools that flipped
        // workspaceId to Required:false keep a live exception-path elicitation recovery.
        Assert.IsTrue(ElicitationAllowlistPolicy.IsWorkspaceIdRecoveryAllowedFor("go_to_definition", "workspaceId"),
            "Recovery must stay eligible for go_to_definition after workspaceId flipped to optional.");
        Assert.IsTrue(ElicitationAllowlistPolicy.IsWorkspaceIdRecoveryAllowedFor("find_references", "workspaceId"));
        Assert.IsTrue(ElicitationAllowlistPolicy.IsWorkspaceIdRecoveryAllowedFor("document_symbols", "workspaceId"));
    }

    [TestMethod]
    public void IsWorkspaceIdRecoveryAllowedFor_NonWorkspaceIdParam_ReturnsFalse()
    {
        Assert.IsFalse(ElicitationAllowlistPolicy.IsWorkspaceIdRecoveryAllowedFor("go_to_definition", "path"),
            "Only the workspaceId parameter is eligible for the workspaceId recovery gate.");
        Assert.IsFalse(ElicitationAllowlistPolicy.IsWorkspaceIdRecoveryAllowedFor("find_references", "query"));
    }

    [TestMethod]
    public void IsWorkspaceIdRecoveryAllowedFor_DestructiveTool_ReturnsFalse()
    {
        Assert.IsFalse(ElicitationAllowlistPolicy.IsWorkspaceIdRecoveryAllowedFor("apply_text_edit", "workspaceId"),
            "Recovery must not auto-load-and-retry a destructive tool.");
    }

    // ── IsWorkspaceIdAutoResolveAllowedFor ────────────────────────────────────

    [TestMethod]
    public void IsWorkspaceIdAutoResolveAllowedFor_ReadOnlyWorkspaceScopedTool_ReturnsTrue()
    {
        Assert.IsTrue(ElicitationAllowlistPolicy.IsWorkspaceIdAutoResolveAllowedFor("go_to_definition"),
            "Read-only, non-destructive tools declaring a string workspaceId are auto-resolve eligible.");
        Assert.IsTrue(ElicitationAllowlistPolicy.IsWorkspaceIdAutoResolveAllowedFor("find_references"));
        Assert.IsTrue(ElicitationAllowlistPolicy.IsWorkspaceIdAutoResolveAllowedFor("document_symbols"));
    }

    [TestMethod]
    public void IsWorkspaceIdAutoResolveAllowedFor_DestructiveTool_ReturnsFalse()
    {
        Assert.IsFalse(ElicitationAllowlistPolicy.IsWorkspaceIdAutoResolveAllowedFor("apply_text_edit"),
            "Destructive edit tools must not have workspaceId auto-resolved pre-dispatch.");
    }

    [TestMethod]
    public void IsWorkspaceIdAutoResolveAllowedFor_NullOrEmpty_ReturnsFalse()
    {
        Assert.IsFalse(ElicitationAllowlistPolicy.IsWorkspaceIdAutoResolveAllowedFor(null));
        Assert.IsFalse(ElicitationAllowlistPolicy.IsWorkspaceIdAutoResolveAllowedFor(""));
    }
}
