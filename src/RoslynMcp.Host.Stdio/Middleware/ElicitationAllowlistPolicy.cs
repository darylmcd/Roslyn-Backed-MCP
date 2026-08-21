using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Middleware;

/// <summary>
/// The elicitation-allowlist policy layer extracted from <see cref="StructuredCallToolFilter"/>.
/// Owns every decision about <em>whether</em> a missing parameter may be elicited from the user
/// through request-scoped form input — the strict per-arg allowlist, the sensitive-field refusal,
/// and the read-only <c>workspaceId</c> recovery/auto-resolve gates. Holds no transport, dispatch,
/// or metrics concerns; those stay in the request-input adapter, filter, and
/// <see cref="CallMetricsRecorder"/> respectively.
///
/// <para>
/// <see cref="StructuredCallToolFilter"/> and <see cref="StructuredCallElicitationCoordinator"/>
/// consume this policy directly; no test-only forwarding surface mirrors it. <c>SymbolTools</c>
/// consumes the separate <c>ElicitationChoicePrompt</c> contract.
/// </para>
/// </summary>
internal static class ElicitationAllowlistPolicy
{
    internal const string WorkspaceLoadToolName = "workspace_load";
    internal const string WorkspaceIdParameterName = "workspaceId";
    internal const string PathParameterName = "path";

    /// <summary>
    /// Strict allowlist of <c>(toolName, paramName)</c> pairs that may be requested from the user
    /// through a request-scoped form. Anything not on this list is rejected at the input-request
    /// entry point regardless of transport era or any other heuristic — defense layer 1 (per-arg
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
    /// <c>workspace_load.path</c>; <c>workspaceId</c> itself is an opaque, freshly minted session
    /// identifier, not a credential, and is pinned non-sensitive by tests.
    /// </para>
    /// </summary>
    private static readonly HashSet<(string Tool, string Param)> AllowedElicitationParameters =
        new()
        {
            (WorkspaceLoadToolName, PathParameterName),
        };

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
    /// is the non-sensitive string <c>workspaceId</c> parameter, making it eligible for
    /// pre-dispatch path elicitation when no workspace can be resolved. <b>Independent of the
    /// Required flag</b> (mirrors <see cref="IsWorkspaceIdAutoResolveAllowedFor"/>), so recovery
    /// stays live for read-only tools that declare <c>workspaceId</c> as optional
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
    /// path prompt and recover-load-retry flow after discovery cannot resolve a workspace.
    /// Both predicates are <b>independent of the Required flag</b>. This one gates the earlier
    /// zero/one/many-workspace resolution step. Public so tests can pin the policy.
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
