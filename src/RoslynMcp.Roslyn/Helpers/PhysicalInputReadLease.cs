namespace RoslynMcp.Roslyn.Helpers;

/// <summary>Owns a validated read stream and its Unix advisory read lock.</summary>
internal sealed class PhysicalInputReadLease(FileStream stream, bool unixReadLock) : IDisposable, IAsyncDisposable
{
    private FileStream? _owned = stream;
    internal FileStream Stream => _owned ?? throw new ObjectDisposedException(nameof(PhysicalInputReadLease));

    public void Dispose()
    {
        var input = Interlocked.Exchange(ref _owned, null);
        if (input is null) return;
        try { if (unixReadLock) PhysicalDirectoryScope.UnlockInputReadUnix(input.SafeFileHandle); }
        finally { input.Dispose(); }
    }

    public async ValueTask DisposeAsync()
    {
        var input = Interlocked.Exchange(ref _owned, null);
        if (input is null) return;
        try { if (unixReadLock) PhysicalDirectoryScope.UnlockInputReadUnix(input.SafeFileHandle); }
        finally { await input.DisposeAsync().ConfigureAwait(false); }
    }
}
