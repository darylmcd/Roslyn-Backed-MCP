namespace RoslynMcp.Core.Services;

/// <summary>
/// Ambient (AsyncLocal) publication of the enclosing request's gate deadline. The workspace
/// execution gate opens a scope when it arms its per-request timeout; code running inside the
/// gated action (for example the <c>workspace_load</c> auto-restore) reads <see cref="Remaining"/>
/// to size its own budget so it cannot consume the whole request deadline and starve the work
/// that follows it. Companion to <see cref="AmbientGateMetrics"/>.
/// </summary>
/// <remarks>
/// Nested scopes (a write gate opened inside the load gate for <c>workspace_reload</c>) never
/// extend the effective deadline: <see cref="Remaining"/> is the minimum across the whole chain.
/// </remarks>
public static class RequestDeadline
{
    private static readonly AsyncLocal<Scope?> _current = new();

    /// <summary>
    /// Time left before the earliest enclosing request deadline, or <see langword="null"/> outside
    /// any scope (non-gated callers). Never negative.
    /// </summary>
    public static TimeSpan? Remaining
    {
        get
        {
            TimeSpan? min = null;
            for (var scope = _current.Value; scope is not null; scope = scope.Parent)
            {
                var remaining = scope.DeadlineUtc - scope.TimeProvider.GetUtcNow();
                if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
                if (min is null || remaining < min) min = remaining;
            }

            return min;
        }
    }

    /// <summary>
    /// Opens a deadline scope that expires <paramref name="timeout"/> from now on
    /// <paramref name="timeProvider"/>'s clock. Dispose the returned scope to restore the parent.
    /// </summary>
    public static IRequestDeadlineScope Begin(TimeProvider timeProvider, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        var scope = new Scope(_current.Value, timeProvider, timeProvider.GetUtcNow() + timeout);
        _current.Value = scope;
        return scope;
    }

    private sealed class Scope : IRequestDeadlineScope
    {
        private bool _disposed;

        public Scope(Scope? parent, TimeProvider timeProvider, DateTimeOffset deadlineUtc)
        {
            Parent = parent;
            TimeProvider = timeProvider;
            DeadlineUtc = deadlineUtc;
        }

        public Scope? Parent { get; }

        public TimeProvider TimeProvider { get; }

        public DateTimeOffset DeadlineUtc { get; private set; }

        public void Reset(TimeSpan timeout) => DeadlineUtc = TimeProvider.GetUtcNow() + timeout;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _current.Value = Parent;
        }
    }
}

/// <summary>A scope opened by <see cref="RequestDeadline.Begin"/>.</summary>
public interface IRequestDeadlineScope : IDisposable
{
    /// <summary>
    /// Re-arms this scope's own deadline to <paramref name="timeout"/> from now, mirroring a gate
    /// that re-arms its timeout source (for example after an auto-reload). Enclosing scopes keep
    /// their deadlines, so the effective <see cref="RequestDeadline.Remaining"/> still honors them.
    /// </summary>
    void Reset(TimeSpan timeout);
}
