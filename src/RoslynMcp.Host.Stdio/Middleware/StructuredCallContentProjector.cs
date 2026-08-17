using ModelContextProtocol.Protocol;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Host.Stdio.Middleware;

/// <summary>
/// The structured-content / <c>_meta</c> projection layer extracted from
/// <see cref="StructuredCallToolFilter"/>. Owns injection of the gate-metrics snapshot as a
/// top-level <c>_meta</c> property on the response's first text block. Structured-result
/// ownership stays with the tool producer; the SDK transports the explicit envelope.
/// Holds no error-classification, dispatch, or allowlist concerns; those stay in the filter,
/// <see cref="StructuredCallElicitationCoordinator"/>, and <see cref="ElicitationAllowlistPolicy"/>.
///
/// <para>
/// <see cref="StructuredCallToolFilter"/> keeps one thin delegate to this method so dispatch and
/// projection ownership remain separate.
/// </para>
/// </summary>
internal static class StructuredCallContentProjector
{
    /// <summary>
    /// Injects the gate-metrics snapshot as a top-level <c>_meta</c> property on the first
    /// text content block when the tool's JSON response is object-rooted. Arrays,
    /// primitives, and non-text content are returned unchanged — preserving the
    /// historical contract that e.g. <c>source_generated_documents</c>'s bare-array
    /// response shape remains stable across the filter migration. Exposed <c>internal</c>
    /// so tests can assert meta-injection behavior directly.
    ///
    /// <para>
    /// Producer-owned <see cref="CallToolResult.StructuredContent"/> is authoritative and is
    /// never created or replaced here. Schema-declaring tools must construct both protocol
    /// channels before this decorator runs; this class only augments object-rooted text.
    /// </para>
    /// </summary>
    internal static CallToolResult InjectMetaIntoContent(CallToolResult result, string toolName)
    {
        if (result.Content is null || result.Content.Count == 0)
        {
            return result;
        }

        if (result.Content[0] is not TextContentBlock text || string.IsNullOrEmpty(text.Text))
        {
            return result;
        }

        var injected = ToolErrorHandler.InjectMetaIfPossible(text.Text, toolName);
        var textChanged = !(ReferenceEquals(injected, text.Text) || injected == text.Text);

        if (!textChanged)
        {
            // Nothing to project: preserve the complete response envelope and every content
            // block by returning the producer's exact result instance.
            return result;
        }

        text.Text = injected;
        return result;
    }
}
