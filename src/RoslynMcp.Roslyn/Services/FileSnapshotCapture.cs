using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Captures pre-apply snapshots while distinguishing known absence from read failures.
/// <para>
/// Callers that already own exact bytes may construct a byte snapshot directly. Callers
/// needing a physical read use the shared missing-only reader after live authorization.
/// </para>
/// <para>
/// <see cref="FromBytesOrFallback"/> applies the byte-versus-text policy without rereading;
/// <see cref="CaptureAsync"/> reads once and delegates to that policy.
/// </para>
/// </summary>
internal static class FileSnapshotCapture
{
    /// <summary>
    /// Builds a snapshot from bytes already read off disk, or from a text fallback when the
    /// file did not exist.
    /// </summary>
    /// <param name="filePath">Absolute path of the file about to be mutated.</param>
    /// <param name="bytes">
    /// The exact pre-mutation on-disk bytes, or <see langword="null"/> when the file did not
    /// exist. An existing zero-length file yields an empty array, NOT <see langword="null"/>,
    /// and correctly takes the bytes path.
    /// </param>
    /// <param name="fallbackText">
    /// Text recorded as <c>OriginalText</c> when <paramref name="bytes"/> is
    /// <see langword="null"/>. Pass <see langword="null"/> when the caller has no meaningful
    /// pre-mutation text for a file that did not exist.
    /// </param>
    /// <remarks>
    /// The null check is load-bearing and must stay OUTSIDE the
    /// <see cref="FileSnapshotDto.FromExistingBytes"/> call: its parameter is a
    /// <see cref="ReadOnlySpan{T}"/>, so a null <c>byte[]</c> converts implicitly to an EMPTY
    /// span and would silently yield an empty-bytes snapshot instead of the fallback.
    /// </remarks>
    internal static FileSnapshotDto FromBytesOrFallback(string filePath, byte[]? bytes, string? fallbackText)
        => bytes is not null
            ? FileSnapshotDto.FromExistingBytes(filePath, bytes)
            : new FileSnapshotDto(filePath, fallbackText);

    /// <summary>
    /// Reads the pre-mutation bytes off disk (when the file exists) and builds the snapshot,
    /// for callers that do not already hold them.
    /// </summary>
    /// <param name="filePath">Absolute path of the file about to be mutated.</param>
    /// <param name="fallbackTextFactory">
    /// Invoked ONLY when the file does not exist, to produce the <c>OriginalText</c> fallback.
    /// Deliberately lazy: the callers on the edit-apply path hold a Roslyn <c>SourceText</c>
    /// whose <c>ToString()</c> materializes the whole file, and the file-exists case (the
    /// common one) must not pay for a string it discards.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    internal static async Task<FileSnapshotDto> CaptureAsync(
        string filePath,
        Func<string?>? fallbackTextFactory,
        CancellationToken ct)
    {
        var bytes = await ReadIfPresentAsync(filePath, PhysicalInputReader.ReadAllBytesAsync, ct).ConfigureAwait(false);
        return FromBytesOrFallback(filePath, bytes, bytes is null ? fallbackTextFactory?.Invoke() : null);
    }

    /// <summary>Distinguishes known absence from unreadable physical input for all transaction readers.</summary>
    internal static async Task<T?> ReadIfPresentAsync<T>(string path,
        Func<string, CancellationToken, Task<T>> read, CancellationToken ct) where T : class
    {
        ct.ThrowIfCancellationRequested();
        try { return await read(path, ct).ConfigureAwait(false); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }
}
