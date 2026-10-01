namespace RoslynMcp.Core.Services;

/// <summary>
/// Marks an exception whose <see cref="PublicMessage"/> is a deliberately authored,
/// safe-to-return-verbatim diagnostic. The host's shared error boundary returns it to the caller
/// instead of a generic fallback, and reports the BCL base type name (for example
/// <c>ArgumentException</c> or <c>InvalidOperationException</c>) as the envelope
/// <c>exceptionType</c> so the implementing class name never reaches the wire.
/// </summary>
/// <remarks>
/// Implemented by <see cref="PublicArgumentException"/> and
/// <see cref="PublicInvalidOperationException"/>. Implementers must derive directly from the BCL
/// exception whose name the envelope reports.
/// </remarks>
public interface IPublicMessageException
{
    /// <summary>The caller-safe message; see <see cref="PublicArgumentException"/> for the publication policy.</summary>
    string PublicMessage { get; }
}
