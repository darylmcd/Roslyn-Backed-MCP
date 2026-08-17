using System.Diagnostics;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Projects unexpected exceptions into separate public and server-diagnostic shapes.
/// Expected validation and domain failures must continue to use their dedicated contracts.
/// </summary>
public static class PublicExceptionDetailPolicy
{
    private const string ClientCategory = "InternalError";
    private const string ClientSummary = "An unexpected internal error occurred.";
    private const string ClientRemediation =
        "Retry once; if the failure repeats, report the correlation ID to the server operator.";
    private const string UnavailableCorrelationId = "unavailable";

    /// <summary>
    /// Creates deterministic, secret-safe projections for an unexpected exception.
    /// Exception messages, data, source paths, and raw stack text are never copied.
    /// </summary>
    public static UnexpectedExceptionDetails ProjectUnexpected(Exception exception, string? correlationId)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var safeCorrelationId = NormalizeCorrelationId(correlationId);
        var exceptionTypes = new List<string>();
        var current = exception;
        while (current is not null)
        {
            exceptionTypes.Add(current.GetType().FullName ?? current.GetType().Name);
            current = current.InnerException;
        }

        var stackFrameCount = new StackTrace(exception, fNeedFileInfo: false).FrameCount;
        return new UnexpectedExceptionDetails(
            new PublicUnexpectedExceptionDetail(
                ClientCategory,
                ClientSummary,
                ClientRemediation,
                safeCorrelationId),
            new ServerUnexpectedExceptionDiagnostic(
                safeCorrelationId,
                exceptionTypes.AsReadOnly(),
                stackFrameCount));
    }

    private static string NormalizeCorrelationId(string? correlationId)
    {
        if (string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > 64)
        {
            return UnavailableCorrelationId;
        }

        foreach (var character in correlationId)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_')
            {
                return UnavailableCorrelationId;
            }
        }

        return correlationId;
    }
}

/// <summary>Secret-safe client projection for an unexpected exception.</summary>
public sealed record PublicUnexpectedExceptionDetail(
    string Category,
    string Summary,
    string Remediation,
    string CorrelationId);

/// <summary>
/// Server-only diagnostic structure for an unexpected exception. It deliberately retains only
/// exception type topology and stack depth; raw messages, paths, data, and stack text are excluded.
/// </summary>
public sealed record ServerUnexpectedExceptionDiagnostic(
    string CorrelationId,
    IReadOnlyList<string> ExceptionTypes,
    int StackFrameCount);

/// <summary>Paired public and server projections derived from one unexpected exception.</summary>
public sealed record UnexpectedExceptionDetails(
    PublicUnexpectedExceptionDetail Public,
    ServerUnexpectedExceptionDiagnostic Server);
