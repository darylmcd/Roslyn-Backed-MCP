using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Elicitation;

/// <summary>
/// The shared elicitation-choice contract, extracted to its own namespace so
/// <c>RoslynMcp.Host.Stdio.Middleware</c> and <c>RoslynMcp.Host.Stdio.Tools</c> can both depend on
/// it instead of on each other. Owns the two members that were the sole reason
/// <c>Tools</c> imported <c>Middleware</c>: the client-capability predicate
/// (<see cref="HasElicitation"/>) and the select-from-N picker
/// (<see cref="TryElicitChoiceAsync"/>).
///
/// <para>
/// <b>Layering invariant:</b> this type must never import <c>RoslynMcp.Host.Stdio.Middleware</c>,
/// <c>RoslynMcp.Host.Stdio.Tools</c>, or any other <c>RoslynMcp.*</c> namespace — only the MCP SDK
/// and BCL. Any such <c>using</c> re-creates the namespace cycle this type exists to break, and is
/// caught by
/// <c>ExpandedSurfaceIntegrationTests_RepoSolutionAnalysis.No_Circular_Namespace_Dependency_Between_Middleware_And_Tools</c>.
/// </para>
///
/// <para>
/// <c>ElicitationAllowlistPolicy.HasElicitation</c> and
/// <c>StructuredCallElicitationCoordinator.TryElicitChoiceAsync</c> are retained as thin delegates
/// forwarding here, so the historical Middleware-internal static call surface (consumed by
/// <c>StructuredCallToolFilter</c> and the existing elicitation test suites) is preserved
/// byte-for-byte.
/// </para>
/// </summary>
internal static class ElicitationChoicePrompt
{
    /// <summary>
    /// Capability-check helper: returns <see langword="true"/> when the connected client
    /// declares the <c>elicitation</c> capability per MCP 2025-06-18 § Client Capabilities.
    /// Canonical home for the predicate — both the Middleware allowlist policy and the
    /// <c>Tools</c> symbol-disambiguation path call it (directly or via a delegate) rather than
    /// copy-pasting the null-coalescing dance.
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
    /// elicit-disambiguation-on-multi-symbol-resolve: shared select-from-N elicitation
    /// helper. Builds an enum-shaped <c>elicitation/create</c> request whose options carry
    /// short candidate labels and stable string keys, calls the SDK, and returns the chosen
    /// key (or <see langword="null"/> when the user declined / the client lacks elicitation /
    /// the request itself failed). The caller uses the returned key to map back to the
    /// original candidate (e.g. an <c>ISymbol</c>) and re-runs the tool call against that
    /// chosen candidate.
    /// </summary>
    /// <param name="server">The connected <see cref="McpServer"/>; <see langword="null"/> short-circuits to null.</param>
    /// <param name="paramName">
    /// Name of the schema field carrying the picked option — also the dictionary key the
    /// SDK populates in <see cref="ElicitResult.Content"/>. Conventionally <c>"choice"</c>.
    /// </param>
    /// <param name="title">Short title shown above the option list.</param>
    /// <param name="description">Longer description (one or two sentences) explaining why the user is being asked.</param>
    /// <param name="options">
    /// The select-from-N options. <c>Key</c> is the stable identifier returned to the caller,
    /// <c>Label</c> is the human-readable text shown in the picker.
    /// </param>
    /// <param name="cancellationToken">Cancellation token (request-scoped).</param>
    /// <returns>The chosen <c>Key</c> on accept; <see langword="null"/> on decline / cancel / unsupported / error.</returns>
    public static async Task<string?> TryElicitChoiceAsync(
        McpServer? server,
        string paramName,
        string title,
        string description,
        IReadOnlyList<(string Key, string Label)> options,
        CancellationToken cancellationToken)
    {
        if (server is null) return null;
        if (!HasElicitation(server.ClientCapabilities)) return null;
        if (string.IsNullOrEmpty(paramName) || options is null || options.Count == 0) return null;

        var oneOf = new List<ElicitRequestParams.EnumSchemaOption>(options.Count);
        for (var i = 0; i < options.Count; i++)
        {
            var (key, label) = options[i];
            if (string.IsNullOrEmpty(key)) continue;
            oneOf.Add(new ElicitRequestParams.EnumSchemaOption
            {
                Const = key,
                Title = string.IsNullOrEmpty(label) ? key : label,
            });
        }
        if (oneOf.Count == 0) return null;

        var request = new ElicitRequestParams
        {
            Message = description,
            RequestedSchema = new ElicitRequestParams.RequestSchema
            {
                Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                {
                    [paramName] = new ElicitRequestParams.TitledSingleSelectEnumSchema
                    {
                        Title = title,
                        Description = description,
                        OneOf = oneOf,
                    },
                },
                Required = [paramName],
            },
        };

        ElicitResult result;
        try
        {
            result = await server.ElicitAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            // Transport gone / capability mismatch. The disambiguation path falls through
            // to the additive list response — never masks a real failure. Named explicitly
            // (rather than a broad `catch` or an `is not OperationCanceledException` filter)
            // so cancellation propagates and any genuinely unexpected exception type is not
            // silently absorbed either (elicitation-trychoice-cancellation-swallow).
            LogElicitFailure(server, ex);
            return null;
        }
        catch (McpException ex)
        {
            // Client-side error response to the elicitation request. Same fallback as above.
            LogElicitFailure(server, ex);
            return null;
        }

        if (!result.IsAccepted || result.Content is null) return null;
        if (!result.Content.TryGetValue(paramName, out var chosen)) return null;
        var chosenKey = chosen.ValueKind == JsonValueKind.String ? chosen.GetString() : null;
        return string.IsNullOrEmpty(chosenKey) ? null : chosenKey;
    }

    /// <summary>
    /// Logs the swallowed <see cref="InvalidOperationException"/> / <see cref="McpException"/>
    /// at Debug so the additive-list fallback is discoverable during troubleshooting instead of
    /// disappearing silently (elicitation-trychoice-cancellation-swallow). Resolves the logger
    /// from <see cref="McpServer.Services"/> — the same DI-lookup pattern
    /// <c>StructuredCallToolFilter</c> uses — and no-ops when no <see cref="ILoggerFactory"/> is
    /// registered (e.g. in unit tests that construct a server without a full DI container).
    /// </summary>
    private static void LogElicitFailure(McpServer server, Exception ex) =>
        server.Services?.GetService<ILoggerFactory>()?
            .CreateLogger("RoslynMcp.Elicitation.ElicitationChoicePrompt")
            .LogDebug(
                ex,
                "TryElicitChoiceAsync: elicitation request failed with {ExceptionType}; falling back to the additive list response.",
                ex.GetType().Name);
}
