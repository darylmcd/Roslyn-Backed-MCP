using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Single owner of the pre-apply file-snapshot policy for every mutation path in the server
/// (<c>file-snapshot-capture-helper-consolidation</c>): given the bytes a file held before
/// the mutation — or the knowledge that it held none — produce the
/// <see cref="FileSnapshotDto"/> <c>revert_last_apply</c> needs.
/// <para>
/// The policy is one line, but it was hand-rolled as an independent ternary at every call
/// site (<see cref="EditService"/> twice, <see cref="EditorConfigService"/>,
/// <see cref="ProjectMutationService"/>, <c>RefactoringService.AddFileSnapshotAsync</c> and
/// <c>RefactoringService.AddProjectFileSnapshotAsync</c>), so a fix to one never reached the
/// others. All of them now delegate here, and no production call site outside this type
/// names <see cref="FileSnapshotDto.FromExistingBytes"/>.
/// </para>
/// <para>
/// Two entry points exist because some callers already read the bytes for a SECOND purpose —
/// <c>AtomicFileWriter.ResolveWriteEncoding</c>, or a three-way missing-file branch — and
/// must not read the file twice: they call <see cref="FromBytesOrFallback"/> with the bytes
/// they already hold, while callers with nothing pre-read call <see cref="CaptureAsync"/>
/// (which itself delegates to <see cref="FromBytesOrFallback"/> on both branches, so the
/// ternary exists exactly once).
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
        if (!File.Exists(filePath))
        {
            return FromBytesOrFallback(filePath, bytes: null, fallbackTextFactory?.Invoke());
        }

        var bytes = await File.ReadAllBytesAsync(filePath, ct).ConfigureAwait(false);
        return FromBytesOrFallback(filePath, bytes, fallbackText: null);
    }
}
