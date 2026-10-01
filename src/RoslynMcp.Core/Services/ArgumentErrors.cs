namespace RoslynMcp.Core.Services;

/// <summary>
/// Factory for caller-argument errors whose server-side detail must never reach the client.
/// </summary>
/// <remarks>
/// Policy (P/R/I): prefer a path-free, caller-actionable public message. Use <see cref="Redacted"/>
/// only when the useful detail is caller free text or a server path; the detail then stays in
/// <see cref="Exception.Message"/> for server logs while the Host collapses the client envelope to a
/// generic "Parameter '&lt;name&gt;' is invalid" message. Internal invariants are not caller errors and
/// must not use this factory.
/// </remarks>
public static class ArgumentErrors
{
    /// <summary>
    /// Creates an exact <see cref="ArgumentException"/> with <see cref="ArgumentException.ParamName"/>
    /// always set. <paramref name="serverDetail"/> is kept only in <see cref="Exception.Message"/>.
    /// </summary>
    /// <param name="parameterName">The offending parameter; must be non-blank.</param>
    /// <param name="serverDetail">Server-side diagnostic detail; never shown to the client.</param>
    /// <param name="inner">Optional underlying exception, preserved for server logs.</param>
    /// <exception cref="ArgumentException"><paramref name="parameterName"/> is null, empty, or whitespace.</exception>
    public static ArgumentException Redacted(string parameterName, string serverDetail, Exception? inner = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);
        return new ArgumentException(serverDetail, parameterName, inner);
    }
}
