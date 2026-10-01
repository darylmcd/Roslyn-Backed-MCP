namespace RoslynMcp.Core.Services;

/// <summary>
/// Carries a bounded, server-authored argument diagnostic through the shared error boundary.
/// Unlike arbitrary <see cref="ArgumentException.Message"/> values, <see cref="PublicMessage"/>
/// is deliberately safe to return verbatim to the caller.
/// </summary>
/// <remarks>
/// <b>Publication policy:</b> the message may echo integers, bounds, lists of valid values and
/// server-owned identifiers. It must not echo absolute paths, caller-supplied free text, or the
/// message of a lower-layer exception. When in doubt, throw a plain <see cref="ArgumentException"/>
/// and let the boundary's redaction apply.
/// <para>
/// Lives in <c>RoslynMcp.Core</c> so <c>RoslynMcp.Roslyn</c> throw sites can use it, like
/// <see cref="PublicInvalidOperationException"/> and <see cref="PreviewTokenStaleException"/>.
/// </para>
/// </remarks>
public sealed class PublicArgumentException : ArgumentException, IPublicMessageException
{
    public PublicArgumentException(string publicMessage, string parameterName)
        : base(publicMessage, parameterName)
    {
        PublicMessage = publicMessage;
    }

    public string PublicMessage { get; }
}
