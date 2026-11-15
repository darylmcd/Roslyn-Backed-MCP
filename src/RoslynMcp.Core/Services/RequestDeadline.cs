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
                if (scope.IsDisposed) continue;

                min = Earlier(min, scope.Budget.Remaining);
                min = Earlier(min, scope.ExternalBudget?.Invoke());
            }

            return min;
        }
    }

    /// <summary>
    /// Opens a deadline scope that expires <paramref name="timeout"/> from now on
    /// <paramref name="timeProvider"/>'s clock. Dispose the returned scope to restore the parent.
    /// </summary>
    public static IRequestDeadlineScope Begin(TimeProvider timeProvider, TimeSpan timeout)
        => Begin(timeProvider, timeout, null);

    internal static Scope Begin(TimeProvider timeProvider, TimeSpan timeout, Func<TimeSpan?>? externalBudget)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        var scope = new Scope(_current.Value, new Budget(timeProvider, timeout), externalBudget);
        _current.Value = scope;
        return scope;
    }

    private static TimeSpan? Earlier(TimeSpan? current, TimeSpan? candidate) =>
        candidate is not null && (current is null || candidate < current) ? candidate : current;

    internal sealed class Scope(Scope? parent, Budget budget, Func<TimeSpan?>? externalBudget) : IRequestDeadlineScope
    {
        private volatile bool _disposed;

        internal Scope? Parent { get; } = parent;
        internal bool IsDisposed => _disposed;
        internal Budget Budget { get; } = budget;
        internal Func<TimeSpan?>? ExternalBudget { get; } = externalBudget;

        public void Reset(TimeSpan timeout) => Budget.Reset(timeout);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _current.Value = Parent;
        }
    }

    internal sealed class Budget(TimeProvider timeProvider, TimeSpan timeout)
    {
        private long _deadlineUtcTicks = (timeProvider.GetUtcNow() + timeout).UtcTicks;

        internal TimeSpan Remaining => TimeSpan.FromTicks(Math.Max(0,
            Volatile.Read(ref _deadlineUtcTicks) - timeProvider.GetUtcNow().UtcTicks));

        internal void Reset(TimeSpan timeout) =>
            Volatile.Write(ref _deadlineUtcTicks, (timeProvider.GetUtcNow() + timeout).UtcTicks);
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
