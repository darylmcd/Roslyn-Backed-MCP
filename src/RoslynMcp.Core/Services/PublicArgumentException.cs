namespace RoslynMcp.Core.Services;

/// <summary>
/// Carries a bounded, server-authored argument diagnostic through the shared error boundary.
/// Unlike arbitrary <see cref="ArgumentException.Message"/> values, <see cref="PublicMessage"/>
/// is deliberately safe to return verbatim to the caller.
/// </summary>
/// <remarks>
/// <b>Publication policy:</b> the message may echo integers, bounds, lists of valid values and
/// server-owned identifiers. It must not echo absolute paths, caller-supplied free text, or the
/// message of a lower-layer exception. When in doubt, use <see cref="ArgumentErrors.Redacted"/>
/// and let the boundary's redaction apply.
/// <para>
/// Lives in <c>RoslynMcp.Core</c> so <c>RoslynMcp.Roslyn</c> throw sites can use it, like
/// <see cref="PublicInvalidOperationException"/> and <see cref="PreviewTokenStaleException"/>.
/// </para>
/// </remarks>
public sealed class PublicArgumentException : ArgumentException, IPublicMessageException
{
    public PublicArgumentException(string publicMessage, string parameterName)
        : this(publicMessage, parameterName, null)
    {
    }

    /// <summary>Retains lower-layer detail for server diagnostics without publishing it.</summary>
    public PublicArgumentException(string publicMessage, string parameterName, Exception? innerException)
        : base(publicMessage, parameterName, innerException)
    {
        PublicMessage = publicMessage;
        WireExceptionType = nameof(ArgumentException);
    }

    private PublicArgumentException(string publicMessage, Exception innerException)
        : this(publicMessage, "parametersJson", innerException)
    {
        WireExceptionType = "PromptParameterBindingException";
    }

    /// <summary>Preserves the released identity of prompt JSON parser and type binding errors.</summary>
    public static PublicArgumentException FromPromptParameterBindingFailure(
        string publicMessage, Exception innerException) => new(publicMessage, innerException);

    /// <summary>The trusted released exception identity emitted by the shared host boundary.</summary>
    public string WireExceptionType { get; }

    public string PublicMessage { get; }
}
