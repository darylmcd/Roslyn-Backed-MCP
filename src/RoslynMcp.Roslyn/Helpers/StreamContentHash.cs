using System.Buffers;
using System.Security.Cryptography;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>Hashes a caller-owned stream through EOF without allocating a large buffer per capture.</summary>
internal static class StreamContentHash
{
    internal static async Task<byte[]> ComputeSha256Async(Stream stream, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        const int initialCapacity = 64 * 1024;
        const int expandedCapacity = 256 * 1024;
        var capacity = initialCapacity;
        var observed = 0;
        byte[]? buffer = ArrayPool<byte>.Shared.Rent(capacity);
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, capacity), ct).ConfigureAwait(false)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                if (capacity == expandedCapacity) continue;
                observed += read;
                if (observed < expandedCapacity) continue;
                // Base growth on actual reads, without querying length or assuming full reads.
                // Return the old buffer first so each capture owns only one pooled buffer.
                var previous = buffer;
                buffer = null;
                ClearAndReturn(previous);
                buffer = ArrayPool<byte>.Shared.Rent(expandedCapacity);
                capacity = expandedCapacity;
            }
            return hash.GetHashAndReset();
        }
        finally
        {
            // Clear even on a read failure that may have partially populated the buffer.
            if (buffer is not null) ClearAndReturn(buffer);
        }
    }

    private static void ClearAndReturn(byte[] buffer)
    {
        CryptographicOperations.ZeroMemory(buffer);
        ArrayPool<byte>.Shared.Return(buffer);
    }
}
