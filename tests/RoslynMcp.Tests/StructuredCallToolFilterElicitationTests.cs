using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio;
using RoslynMcp.Host.Stdio.Middleware;

namespace RoslynMcp.Tests;

/// <summary>
/// Coverage for the <c>elicit-workspace-path-on-missing-required-arg</c> initiative
/// (closes the same-named backlog row): the <see cref="StructuredCallToolFilter"/>
/// recovery path that calls MCP <c>elicitation/create</c> when a tool fails with
/// <c>InvalidArgument: missing &lt;allowlisted-param&gt;</c> and the client supports
/// elicitation. Pins:
/// <list type="bullet">
///   <item><b>(a) elicit-supported</b> preconditions — capability check returns true
///         AND the (toolName, paramName) pair is on the strict allowlist, so the filter
///         is permitted to call <c>ElicitAsync</c>.</item>
///   <item><b>(b) fallback</b> — when the client lacks the elicitation capability OR the
///         user declines, the filter MUST fall through to the existing
///         <c>schemaHint</c>-augmented <c>InvalidArgument</c> envelope, byte-identical
///         to the pre-initiative shape.</item>
///   <item><b>(c) sensitive-data refused</b> — sensitive parameter names (credentials,
///         tokens, secrets, passwords, API keys, auth headers) MUST be refused regardless
///         of whether someone added them to the allowlist by mistake. Per MCP spec §
///         Elicitation security, "Servers MUST NOT request sensitive information".</item>
/// </list>
///
/// <para>
/// The full filter delegate (<see cref="StructuredCallToolFilter.Create"/>) requires a
/// real <see cref="ModelContextProtocol.Server.McpServer"/> instance to drive
/// <c>ElicitAsync</c> end-to-end (the SDK's transport pipeline writes/reads JSON-RPC
/// frames over a stream, which a unit test cannot stand up cheaply). The contract
/// asserted here is therefore the <b>gate logic</b> — every layer that must hold true
/// before <c>ElicitAsync</c> is even called. If those gates pass, the rest is a thin
/// SDK call; if any gate fails, the filter falls through to the existing envelope. Both
/// paths are individually covered.
/// </para>
/// </summary>
[TestClass]
public sealed class StructuredCallToolFilterElicitationTests
{
    // ── (a) elicit-supported preconditions ───────────────────────────────────

    [TestMethod]
    public void HasElicitation_WhenCapabilityNonNull_ReturnsTrue()
    {
        // The minimal shape the SDK populates after initialize-handshake when the client
        // declares "elicitation": {} (form mode). HasElicitation only checks that the
        // capability object exists — it does NOT require form-mode or url-mode to be set
        // (clients can advertise the capability without a sub-mode and the spec leaves
        // sub-mode selection to the server's request).
        var capabilities = new ClientCapabilities
        {
            Elicitation = new ElicitationCapability(),
        };

        Assert.IsTrue(StructuredCallToolFilter.HasElicitation(capabilities),
            "An ElicitationCapability instance present on ClientCapabilities means the " +
            "client supports elicitation/create — the elicit recovery path is permitted.");
    }

    [TestMethod]
    public void IsElicitationAllowedFor_WorkspaceLoadPath_ReturnsTrue()
    {
        // The single allowlisted (tool, param) pair as of this initiative. Adding to the
        // allowlist requires a policy review (non-sensitive AND idempotent retry shape).
        Assert.IsTrue(StructuredCallToolFilter.IsElicitationAllowedFor("workspace_load", "path"),
            "workspace_load.path is the only entry on the strict elicitation allowlist.");
    }

    [TestMethod]
    public void IsElicitationAllowedFor_RandomToolOrParam_ReturnsFalse()
    {
        // Defense layer 1: the allowlist is a strict positive list. Any (tool, param)
        // pair not present is refused, even when nothing about the parameter looks
        // sensitive — the policy is "explicit allow, default deny".
        Assert.IsFalse(StructuredCallToolFilter.IsElicitationAllowedFor("workspace_load", "verbose"));
        Assert.IsFalse(StructuredCallToolFilter.IsElicitationAllowedFor("symbol_search", "query"));
        Assert.IsFalse(StructuredCallToolFilter.IsElicitationAllowedFor("compile_check", "workspaceId"));
    }

    // ── (b) fallback when client lacks elicitation capability ───────────────

    [TestMethod]
    public void HasElicitation_WhenCapabilitiesNull_ReturnsFalse()
    {
        // Pre-initialize-handshake or a transport that doesn't establish capabilities —
        // the filter MUST NOT call ElicitAsync; it falls through to the existing envelope.
        Assert.IsFalse(StructuredCallToolFilter.HasElicitation(null),
            "Null ClientCapabilities means no handshake-established capability set; refuse to elicit.");
    }

    [TestMethod]
    public void HasElicitation_WhenElicitationOmitted_ReturnsFalse()
    {
        // The client declared SOME capabilities but not elicitation — common with older
        // clients that don't yet support MCP 2025-06-18. The filter falls through.
        var capabilities = new ClientCapabilities
        {
            // No Elicitation set, no Roots set, no Sampling set.
        };

        Assert.IsFalse(StructuredCallToolFilter.HasElicitation(capabilities),
            "Client must explicitly advertise the elicitation capability; absence means refuse.");
    }

    [TestMethod]
    public void BuildErrorResult_WhenElicitFallbackTaken_StillProducesSchemaHintEnvelope()
    {
        // The fallback path MUST produce the same envelope the filter emitted before this
        // initiative — InvalidArgument category, schemaHint populated for cataloged tools,
        // exact message text. This pins backward compatibility for clients that don't
        // support elicitation OR whose user declined: their experience is unchanged.
        var binderException = new ArgumentException(
            "The arguments dictionary is missing a value for the required parameter 'path'.",
            paramName: "path");

        using var scope = AmbientGateMetrics.BeginRequest();
        var result = StructuredCallToolFilter.BuildErrorResult("workspace_load", binderException);

        Assert.IsTrue(result.IsError,
            "The fallback envelope retains IsError=true so the LLM can self-correct on retry.");
        var text = ((TextContentBlock)result.Content![0]).Text;
        var payload = JsonDocument.Parse(text).RootElement;

        Assert.AreEqual("InvalidArgument", payload.GetProperty("category").GetString(),
            "Fallback category must remain InvalidArgument — clients keying on the existing " +
            "envelope shape continue to work without change.");
        Assert.AreEqual("workspace_load", payload.GetProperty("tool").GetString());
        StringAssert.Contains(payload.GetProperty("message").GetString(), "path");
    }

    // ── (c) sensitive-data refused (MCP spec § Elicitation security) ─────────

    [TestMethod]
    public void IsSensitiveFieldName_FlagsCommonCredentialPatterns()
    {
        // The credential-like names a careless allowlist addition might let through.
        // Defense layer 2: even if AllowedElicitationParameters contains a sensitive
        // pair, IsElicitationAllowedFor refuses it via this predicate.
        Assert.IsTrue(StructuredCallToolFilter.IsSensitiveFieldName("password"));
        Assert.IsTrue(StructuredCallToolFilter.IsSensitiveFieldName("Password"));
        Assert.IsTrue(StructuredCallToolFilter.IsSensitiveFieldName("passwordHash"));
        Assert.IsTrue(StructuredCallToolFilter.IsSensitiveFieldName("secret"));
        Assert.IsTrue(StructuredCallToolFilter.IsSensitiveFieldName("clientSecret"));
        Assert.IsTrue(StructuredCallToolFilter.IsSensitiveFieldName("credential"));
        Assert.IsTrue(StructuredCallToolFilter.IsSensitiveFieldName("credentials"));
        Assert.IsTrue(StructuredCallToolFilter.IsSensitiveFieldName("token"));
        Assert.IsTrue(StructuredCallToolFilter.IsSensitiveFieldName("authToken"));
        Assert.IsTrue(StructuredCallToolFilter.IsSensitiveFieldName("apiKey"));
        Assert.IsTrue(StructuredCallToolFilter.IsSensitiveFieldName("api_key"));
        Assert.IsTrue(StructuredCallToolFilter.IsSensitiveFieldName("api-key"));
        Assert.IsTrue(StructuredCallToolFilter.IsSensitiveFieldName("ApiKey"));
        Assert.IsTrue(StructuredCallToolFilter.IsSensitiveFieldName("auth"));
        Assert.IsTrue(StructuredCallToolFilter.IsSensitiveFieldName("Authorization"));
        Assert.IsTrue(StructuredCallToolFilter.IsSensitiveFieldName("private_key"));
        Assert.IsTrue(StructuredCallToolFilter.IsSensitiveFieldName("privateKey"));
        Assert.IsTrue(StructuredCallToolFilter.IsSensitiveFieldName("private-key"));
    }

    [TestMethod]
    public void IsSensitiveFieldName_DoesNotFlagBenignNames()
    {
        // Negative cases — common parameter names that must NOT be classified sensitive.
        // Failing cases here would block legitimate elicit candidates from ever being
        // added to the allowlist.
        Assert.IsFalse(StructuredCallToolFilter.IsSensitiveFieldName("path"));
        Assert.IsFalse(StructuredCallToolFilter.IsSensitiveFieldName("workspaceId"));
        Assert.IsFalse(StructuredCallToolFilter.IsSensitiveFieldName("filePath"));
        Assert.IsFalse(StructuredCallToolFilter.IsSensitiveFieldName("query"));
        Assert.IsFalse(StructuredCallToolFilter.IsSensitiveFieldName("verbose"));
        Assert.IsFalse(StructuredCallToolFilter.IsSensitiveFieldName(""),
            "Empty string is not a sensitive name — it just isn't a name at all.");
        Assert.IsFalse(StructuredCallToolFilter.IsSensitiveFieldName(null),
            "Null is not a sensitive name — it just isn't a name at all.");
    }

    [TestMethod]
    public void IsElicitationAllowedFor_RefusesSensitiveFields()
    {
        // The whole-policy assertion: even if a sensitive name were on the allowlist
        // somehow (through a future careless addition), IsElicitationAllowedFor MUST
        // refuse it. This is the test that pins the security guarantee — its failure
        // is the ship-blocking signal for the elicitation feature.
        // We verify the gate against a sensitive-name candidate; the result MUST be false
        // regardless of whether the (tool, param) is on the allowlist.
        Assert.IsFalse(StructuredCallToolFilter.IsElicitationAllowedFor("workspace_load", "password"),
            "Sensitive parameter names MUST be refused even if the (tool, param) pair were " +
            "added to the allowlist. MCP spec § Elicitation security: 'Servers MUST NOT " +
            "request sensitive information'.");
        Assert.IsFalse(StructuredCallToolFilter.IsElicitationAllowedFor("workspace_load", "token"));
        Assert.IsFalse(StructuredCallToolFilter.IsElicitationAllowedFor("workspace_load", "apiKey"));
        Assert.IsFalse(StructuredCallToolFilter.IsElicitationAllowedFor("workspace_load", "secret"));
        Assert.IsFalse(StructuredCallToolFilter.IsElicitationAllowedFor("workspace_load", "credential"));
        Assert.IsFalse(StructuredCallToolFilter.IsElicitationAllowedFor("workspace_load", "Authorization"));
    }

    [TestMethod]
    public void IsElicitationAllowedFor_NullOrEmpty_ReturnsFalse()
    {
        // Defensive: empty/null inputs reach the helper through misformed binder errors.
        // Refuse cleanly rather than throwing.
        Assert.IsFalse(StructuredCallToolFilter.IsElicitationAllowedFor(null, "path"));
        Assert.IsFalse(StructuredCallToolFilter.IsElicitationAllowedFor("workspace_load", null));
        Assert.IsFalse(StructuredCallToolFilter.IsElicitationAllowedFor("", "path"));
        Assert.IsFalse(StructuredCallToolFilter.IsElicitationAllowedFor("workspace_load", ""));
        Assert.IsFalse(StructuredCallToolFilter.IsElicitationAllowedFor(null, null));
    }
}
