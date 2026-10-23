namespace RoslynMcp.Tests.Helpers;

internal sealed class ConcurrencyTracker
{
    private readonly object _sync = new();
    private int _current;
    private int _maximum;

    public int MaxConcurrent
    {
        get { lock (_sync) return _maximum; }
    }

    public void Enter()
    {
        lock (_sync)
        {
            _current++;
            _maximum = Math.Max(_maximum, _current);
        }
    }

    public void Leave()
    {
        lock (_sync)
        {
            if (_current == 0)
                throw new InvalidOperationException("Cannot leave without an active entry.");
            _current--;
        }
    }
}
