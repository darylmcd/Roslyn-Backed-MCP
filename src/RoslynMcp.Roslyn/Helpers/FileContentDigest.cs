using System.Collections.Immutable;
using System.Security.Cryptography;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>Exact content digest, separate from content bytes and from permission to read them.</summary>
internal sealed class FileContentDigest
{
    internal ImmutableArray<byte> Hash { get; }
    private FileContentDigest(ImmutableArray<byte> hash) => Hash = hash;
    internal static FileContentDigest FromHash(ReadOnlySpan<byte> hash)
    {
        if (hash.Length != SHA256.HashSizeInBytes) throw new ArgumentException("A content digest must be exactly SHA256 sized.", nameof(hash));
        return new([.. hash]);
    }
    internal static FileContentDigest? FromBytes(byte[]? bytes) => bytes is null ? null : FromHash(SHA256.HashData(bytes));

    // Explicit adapter for virtual/injected content providers. Never chooses a physical reader.
    internal static async Task<FileContentDigest?> FromReaderAsync(string path,
        Func<string, CancellationToken, Task<byte[]?>> readBytes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var bytes = await readBytes(path, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        var digest = FromBytes(bytes);
        ct.ThrowIfCancellationRequested();
        return digest;
    }
}
