namespace SampleLib;

/// <summary>Test-only types for backlog / audit scenarios (semantic search, unused-symbol confidence).
/// </summary>
public enum BacklogAuditEnum
{
    NeverReferencedValue = 0
}

public sealed class BacklogDisposableSample : IDisposable
{
    public void Dispose() { }
}

public static class BacklogAsyncSample
{
    public static async System.Threading.Tasks.Task<bool> ReturnsBoolAsync()
    {
        await System.Threading.Tasks.Task.Yield();
        return true;
    }
}
