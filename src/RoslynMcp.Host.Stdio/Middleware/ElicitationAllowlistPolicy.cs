using ModelContextProtocol.Protocol;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Middleware;

/// <summary>
/// The elicitation-allowlist policy layer extracted from <see cref="StructuredCallToolFilter"/>.
/// Owns every decision about <em>whether</em> a missing parameter may be elicited from the user
/// via MCP <c>elicitation/create</c> — the strict per-arg allowlist, the sensitive-field refusal,
/// and the read-only <c>workspaceId</c> recovery/auto-resolve gates. Holds no dispatch or metrics
/// concerns; those stay in the filter and <see cref="CallMetricsRecorder"/> respectively.
///
/// <para>
/// <see cref="StructuredCallToolFilter"/> keeps thin public delegates that forward to these
/// members, so its historical static call surface (consumed by <c>SymbolTools</c> and the existing
/// filter test suites) is preserved byte-for-byte.
/// </para>
/// </summary>
internal static class ElicitationAllowlistPolicy
{
    private const string WorkspaceLoadToolName = "workspace_load";
    private const string WorkspaceIdParameterName = "workspaceId";
    private const string PathParameterName = "path";

    /// <summary>
    /// Strict allowlist of <c>(toolName, paramName)</c> pairs that may be elicited from the
    /// user via <c>elicitation/create</c>. Anything not on this list is rejected at the
    /// elicitation entry point regardless of any other heuristic — defense layer 1 (per-arg
    /// allowlist) and defense layer 2 (<see cref="IsSensitiveFieldName"/>) are both checked
    /// before any elicit request is built.
    ///
    /// <para>
    /// Adding to this list requires explicit policy review: the parameter must be
    /// non-sensitive, naturally bounded (a path, an id, a select-from-N), and the recovery
    /// shape (one-shot retry with the elicited value patched in) must be safe for the tool's
    /// idempotency semantics. <c>workspaceId</c> parameters (Required or optional) for read-only,
    /// non-destructive tools are handled by
    /// <see cref="IsWorkspaceIdRecoveryAllowedFor"/> because the concrete elicited field is
    /// <c>workspace_load.path</c>; <c>workspaceId</c> itself is a path-derived session token,
    /// not a credential, and is pinned non-sensitive by tests.
    /// </para>
    /// </summary>
    private static readonly HashSet<(string Tool, string Param)> AllowedElicitationParameters =
        new()
        {
            (WorkspaceLoadToolName, PathParameterName),
        };

    /// <summary>
    /// Capability-check helper: returns <see langword="true"/> when the connected client
    /// declares the <c>elicitation</c> capability per MCP 2025-06-18 § Client Capabilities.
    /// Public so initiative #9 (<c>elicit-disambiguation-on-multi-symbol-resolve</c>) can
    /// reuse the same predicate without copy-pasting the null-coalescing dance.
    /// </summary>
    /// <param name="capabilities">
    /// The <see cref="McpServer.ClientCapabilities"/> snapshot, typically obtained as
    /// <c>context.Server.ClientCapabilities</c> inside a request filter or tool method.
    /// May be <see langword="null"/> on the server's pre-initialize path.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when both <paramref name="capabilities"/> and
    /// <c>capabilities.Elicitation</c> are non-null. Zero-allocation and side-effect-free.
    /// </returns>
    public static bool HasElicitation(ClientCapabilities? capabilities) =>
        capabilities?.Elicitation is not null;

    /// <summary>
    /// Defense-in-depth predicate: returns <see langword="true"/> when the parameter name
    /// suggests credential / secret / token / password / API-key / authorization material.
    /// The primary defense is the strict <see cref="AllowedElicitationParameters"/>
    /// allowlist; this helper exists so tests can pin the policy and so any future allowlist
    /// addition is double-checked before being merged. Per MCP spec § Elicitation security,
    /// "Servers MUST NOT request sensitive information" via <c>elicitation/create</c>.
    /// </summary>
    /// <param name="paramName">Parameter name (case-insensitive comparison).</param>
    /// <returns>
    /// <see langword="true"/> when the name matches a sensitive-data pattern. Empty/null
    /// names return <see langword="false"/> — the allowlist owns the positive permission
    /// decision; this helper only owns the "do not even consider" decision.
    /// </returns>
    public static bool IsSensitiveFieldName(string? paramName)
    {
        if (string.IsNullOrEmpty(paramName)) return false;
        // Use Contains-based matching so common variants ("apiKey", "api_key", "ApiKey",
        // "authToken", "passwordHash", etc.) all classify as sensitive without
        // enumerating every casing.
        return paramName.Contains("password", StringComparison.OrdinalIgnoreCase)
            || paramName.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || paramName.Contains("credential", StringComparison.OrdinalIgnoreCase)
            || paramName.Contains("token", StringComparison.OrdinalIgnoreCase)
            || paramName.Contains("apikey", StringComparison.OrdinalIgnoreCase)
            || paramName.Contains("api_key", StringComparison.OrdinalIgnoreCase)
            || paramName.Contains("api-key", StringComparison.OrdinalIgnoreCase)
            || paramName.Equals("auth", StringComparison.OrdinalIgnoreCase)
            || paramName.Equals("authorization", StringComparison.OrdinalIgnoreCase)
            || paramName.Contains("private_key", StringComparison.OrdinalIgnoreCase)
            || paramName.Contains("privatekey", StringComparison.OrdinalIgnoreCase)
            || paramName.Contains("private-key", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="toolName"/> + <paramref name="paramName"/>
    /// is on the strict elicitation allowlist AND the parameter name is not flagged sensitive.
    /// Both checks must pass — an entry that ends up sensitive (because someone added
    /// <c>workspace_load.token</c> to the allowlist by mistake, say) still gets refused.
    /// Public so tests can pin the allowlist policy.
    /// </summary>
    public static bool IsElicitationAllowedFor(string? toolName, string? paramName)
    {
        if (string.IsNullOrEmpty(toolName) || string.IsNullOrEmpty(paramName)) return false;
        if (IsSensitiveFieldName(paramName)) return false;
        return AllowedElicitationParameters.Contains((toolName, paramName))
               || IsWorkspaceIdRecoveryAllowedFor(toolName, paramName);
    }

    /// <summary>
    /// workspace-id-omitted-residual-recovery-coherence: returns <see langword="true"/> when
    /// <paramref name="toolName"/> is a read-only, non-destructive tool and <paramref name="paramName"/>
    /// is the non-sensitive string <c>workspaceId</c> parameter, making it eligible for the
    /// exception-path elicitation recovery. <b>Independent of the Required flag</b> (mirrors
    /// <see cref="IsWorkspaceIdAutoResolveAllowedFor"/>): the recovery only fires from the
    /// exception-catch block, and the binder never throws for a missing <em>optional</em> arg, so a
    /// relaxed gate cannot fire spuriously — but keeping it Required-independent ensures recovery
    /// stays live for read-only tools that flip <c>workspaceId</c> to optional
    /// (e.g. <c>go_to_definition</c>, <c>find_references</c>, <c>document_symbols</c>).
    /// </summary>
    public static bool IsWorkspaceIdRecoveryAllowedFor(string toolName, string paramName)
    {
        if (!string.Equals(paramName, WorkspaceIdParameterName, StringComparison.Ordinal))
        {
            return false;
        }

        if (IsSensitiveFieldName(paramName))
        {
            return false;
        }

        var schema = ToolParameterIndex.GetParameter(toolName, paramName);

        return schema is { Type: "string" }
               && ServerSurfaceCatalog.TryGetTool(toolName, out var tool)
               && tool is { ReadOnly: true, Destructive: false };
    }

    /// <summary>
    /// workspace-id-omitted-single-resolve: returns <see langword="true"/> when
    /// <paramref name="toolName"/> is a read-only, non-destructive tool that declares a string
    /// <c>workspaceId</c> parameter, making it eligible for pre-dispatch auto-resolution.
    /// Distinct from <see cref="IsWorkspaceIdRecoveryAllowedFor"/>: that predicate gates the
    /// exception-path elicitation recovery (it fires when a tool-call surfaces a missing
    /// <c>workspaceId</c>); both predicates are now <b>independent of the Required flag</b> so
    /// they keep working after a read-only tool flips <c>workspaceId</c> to optional. This one
    /// gates pre-dispatch auto-resolution rather than the exception-path recovery. Public so
    /// tests can pin the policy.
    /// </summary>
    public static bool IsWorkspaceIdAutoResolveAllowedFor(string? toolName)
    {
        if (string.IsNullOrEmpty(toolName))
        {
            return false;
        }

        var schema = ToolParameterIndex.GetParameter(toolName, WorkspaceIdParameterName);
        if (schema is not { Type: "string" })
        {
            return false;
        }

        return ServerSurfaceCatalog.TryGetTool(toolName, out var tool)
               && tool is { ReadOnly: true, Destructive: false };
    }
}
