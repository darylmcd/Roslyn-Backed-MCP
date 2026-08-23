namespace RoslynMcp.Core.Services;

/// <summary>
/// Marks an <see cref="InvalidOperationException"/> whose <see cref="Exception.Message"/> is
/// already a deliberately-authored, safe-to-return-verbatim domain refusal — analogous to
/// <c>RoslynMcp.Host.Stdio.Tools.PublicArgumentException</c>, but for the
/// <see cref="InvalidOperationException"/> shape.
/// </summary>
/// <remarks>
/// <b>Why this exists:</b> <c>RoslynMcp.Host.Stdio.Tools.ToolErrorHandler</c>'s generic
/// <see cref="InvalidOperationException"/> handler replaces the message with a fixed fallback
/// ("Check the tool contract and retry.") for anything it doesn't recognize — a defensive
/// default, since most <see cref="InvalidOperationException"/> messages are not written with
/// public disclosure in mind. That default silently discarded actionable, already-safe
/// remediation text from domain refusals such as
/// <c>RoslynMcp.Roslyn.Services.TestRunnerService</c>'s global.json/restore-failure guidance and
/// <c>RoslynMcp.Roslyn.Helpers.TreeNodeFilterTranslator</c>'s filter-translation refusals — an
/// agent calling <c>test_run</c> and hitting one of these got no more information than "check the
/// tool contract," even though the throw site had already composed a precise, safe explanation.
/// <para>
/// This type lives in <c>RoslynMcp.Core</c> (not alongside <c>PublicArgumentException</c> in
/// <c>RoslynMcp.Host.Stdio</c>) because the throw sites are in <c>RoslynMcp.Roslyn</c>, which
/// cannot reference the host project — the same reason <see cref="PreviewTokenStaleException"/>
/// and <see cref="WorkspaceEvictedException"/> live here.
/// </para>
/// <para>
/// Use this only when the message was written FOR the caller: it must not embed absolute paths,
/// stack traces, or any value that could carry secrets. When in doubt, throw the plain
/// <see cref="InvalidOperationException"/> and let the generic fallback apply.
/// </para>
/// </remarks>
public sealed class PublicInvalidOperationException : InvalidOperationException
{
    public PublicInvalidOperationException(string publicMessage, Exception? inner = null)
        : base(publicMessage, inner)
    {
        PublicMessage = publicMessage;
    }

    public string PublicMessage { get; }
}
