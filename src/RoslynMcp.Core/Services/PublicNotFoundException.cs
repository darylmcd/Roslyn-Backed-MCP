namespace RoslynMcp.Core.Services;

/// <summary>
/// Marks a <see cref="KeyNotFoundException"/> whose <see cref="Exception.Message"/> is a
/// deliberately authored, safe-to-return-verbatim "the thing you named does not exist" refusal —
/// analogous to <see cref="PublicInvalidOperationException"/> for the not-found shape. The host
/// reports it as the <c>NotFound</c> category (and <c>KeyNotFoundException</c> as the envelope
/// <c>exceptionType</c>) with <see cref="PublicMessage"/> as the message.
/// </summary>
/// <remarks>
/// Use it for a caller-supplied identifier that resolves to nothing (an unknown project name, an
/// unresolvable symbol handle). The message must follow the publication policy of
/// <see cref="PublicArgumentException"/>: no absolute paths and no echo of caller free text. Do not
/// use it for a workspace id (<see cref="WorkspaceNotFoundException"/>) or an evicted workspace
/// (<see cref="WorkspaceEvictedException"/>). Message text must not contain the document-miss
/// markers the execution gate keys its post-reload retry on.
/// </remarks>
public sealed class PublicNotFoundException : KeyNotFoundException, IPublicMessageException
{
    public PublicNotFoundException(string publicMessage, Exception? inner = null)
        : base(publicMessage, inner)
    {
        PublicMessage = publicMessage;
    }

    public string PublicMessage { get; }
}
