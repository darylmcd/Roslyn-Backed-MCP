namespace RoslynMcp.Host.Stdio.Diagnostics;

/// <summary>
/// Owns the correlation identifier for the currently-dispatched MCP message.
/// Reading <see cref="Current"/> never creates state; callers outside a request see
/// <see langword="null"/>.
/// </summary>
internal static class RequestCorrelationContext
{
    private static readonly AsyncLocal<CorrelationState?> s_current = new();

    public static string? Current => s_current.Value?.CorrelationId;

    public static IDisposable Begin()
    {
        var prior = s_current.Value;
        var current = new CorrelationState(Guid.NewGuid().ToString("N"));
        s_current.Value = current;
        return new CorrelationScope(prior, current);
    }

    private sealed record CorrelationState(string CorrelationId);

    private sealed class CorrelationScope(
        CorrelationState? prior,
        CorrelationState current) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (ReferenceEquals(s_current.Value, current))
            {
                s_current.Value = prior;
            }
        }
    }
}
