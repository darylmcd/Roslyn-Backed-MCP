using System.Text;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

public sealed class CompositeApplyOrchestrator : ICompositeApplyOrchestrator
{
    private readonly IWorkspaceManager _workspace;
    private readonly ICompositePreviewStore _compositePreviewStore;
    private readonly IChangeTracker? _changeTracker;
    private readonly ILogger<CompositeApplyOrchestrator>? _logger;

    public CompositeApplyOrchestrator(
        IWorkspaceManager workspace,
        ICompositePreviewStore compositePreviewStore,
        IChangeTracker? changeTracker = null,
        ILogger<CompositeApplyOrchestrator>? logger = null)
    {
        _workspace = workspace;
        _compositePreviewStore = compositePreviewStore;
        _changeTracker = changeTracker;
        _logger = logger;
    }

    public async Task<ApplyResultDto> ApplyCompositeAsync(string previewToken, CancellationToken ct)
    {
        var entry = _compositePreviewStore.Retrieve(previewToken);
        if (entry is null)
        {
            return new ApplyResultDto(false, [], "Preview token is invalid, expired, or stale because the workspace changed since the preview was generated. Please create a new preview.");
        }

        var (workspaceId, _, _, mutations) = entry.Value;
        // preview-token-cross-coupling-bundle (BREAKING): version-equality check removed.
        // Composite previews hold a per-token list of absolute-path `CompositeFileMutation`
        // records (write text / delete file). A sibling `*_apply` that mutated unrelated
        // files does not invalidate these records; the mutations replay cleanly below. If
        // two previews happen to target the same file, last-apply wins by design. If the
        // workspace was reloaded or closed, the composite store's lifecycle hook has
        // already dropped the entry and Retrieve above returned null.

        // symbol-refactor-preview-empty-appliedfiles-on-success (gh #750, BREAKING):
        // Pre-fix, an empty `mutations` list silently returned `{success: true, appliedFiles: []}`
        // because the foreach below never executed. That looked like "apply succeeded" while
        // doing nothing. Now we surface the no-op explicitly so callers can distinguish a
        // genuine no-op preview from a successful apply. `ReloadAsync` and `Invalidate` are
        // skipped so the token remains valid for caller inspection.
        if (mutations.Count == 0)
        {
            return new ApplyResultDto(
                false,
                [],
                "Preview token yielded no file mutations — the composite preview produced no file-level changes. Re-issue the preview with a different operation set.");
        }

        var appliedFiles = new List<string>();

        try
        {
            foreach (var mutation in mutations)
            {
                if (mutation.DeleteFile)
                {
                    if (File.Exists(mutation.FilePath))
                    {
                        File.Delete(mutation.FilePath);
                    }

                    appliedFiles.Add(mutation.FilePath);
                    continue;
                }

                var directory = Path.GetDirectoryName(mutation.FilePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await AtomicFileWriter.WriteAllTextAsync(mutation.FilePath, mutation.UpdatedContent ?? string.Empty, ct, _logger).ConfigureAwait(false);
                appliedFiles.Add(mutation.FilePath);
            }

            await _workspace.ReloadAsync(workspaceId, ct).ConfigureAwait(false);
            _compositePreviewStore.Invalidate(previewToken);
            var distinctFiles = appliedFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            _changeTracker?.RecordChange(workspaceId, $"Composite operation ({distinctFiles.Count} files)", distinctFiles, "apply_composite_preview");
            return new ApplyResultDto(true, distinctFiles, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // A mutation failed mid-loop. Prior mutations already hit disk — this orchestrator does
            // not roll them back (see plan Risk 3: true rollback would duplicate UndoService's
            // pre-image capture). Instead we log a warning naming applied-vs-total plus the failing
            // file, and clearly mark the returned result as a partial apply so a caller can tell a
            // genuine partial state apart from a clean no-op failure. Each completed mutation adds
            // exactly one entry to appliedFiles, so appliedFiles.Count is the index of the failing
            // mutation. The preview token is intentionally left valid (Invalidate is not called).
            var applied = appliedFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var failingFile = appliedFiles.Count < mutations.Count
                ? mutations[appliedFiles.Count].FilePath
                : "(unknown)";
            _logger?.LogWarning(
                ex,
                "Composite apply failed after {AppliedCount} of {TotalCount} mutation(s); failing file: {FailingFile}. Prior writes are left in place (no rollback).",
                appliedFiles.Count,
                mutations.Count,
                failingFile);
            var message = appliedFiles.Count > 0
                ? $"Partial composite apply: {applied.Count} file(s) were written before the failure at {failingFile}. {ex.Message}"
                : ex.Message;
            return new ApplyResultDto(false, applied, message);
        }
    }
}

/// <summary>
/// Shared atomic file-write primitive: writes <paramref name="content"/> to a same-directory
/// <c>.tmp</c> sibling of <paramref name="path"/>, then <see cref="File.Move(string, string, bool)"/>
/// over the target. This mirrors the proven pattern in <c>WorkspaceCacheStore</c> and
/// <c>PersistentCompositeStorage</c> and prevents a truncated/corrupt target file if the process
/// crashes or the disk fills mid-write. The temp file is always <paramref name="path"/> + ".tmp",
/// so it lives on the same directory/volume as the target — a precondition for <c>File.Move</c>
/// being atomic (a cross-volume move degrades to a non-atomic copy+delete). On any failure the
/// orphaned <c>.tmp</c> is best-effort deleted so a failed write leaves no artifact, then the
/// original exception is re-thrown for the caller's catch filter. If an optional
/// <paramref name="logger"/> is supplied, a failure to delete the orphaned <c>.tmp</c> is logged
/// at Warning level (the primary write failure is still re-thrown either way) so a stray artifact
/// left on disk leaves an observability trail instead of being silently discarded.
/// <para>
/// Text writes accept an optional <see cref="Encoding"/> so a mutated file keeps its original
/// BOM/encoding (<c>mutation-write-paths-drop-original-encoding</c>). Callers resolve it through
/// <see cref="ResolveWriteEncoding(byte[])"/> (from the pre-mutation on-disk bytes) or
/// <see cref="ResolveWriteEncoding(Encoding)"/> (from a Roslyn <c>SourceText.Encoding</c>).
/// Omitting it keeps the historic UTF-8-no-BOM behavior.
/// </para>
/// </summary>
internal static class AtomicFileWriter
{
    /// <summary>
    /// UTF-8 without a byte-order mark — the encoding .NET's <c>Encoding</c>-less
    /// <see cref="File.WriteAllTextAsync(string, string?, CancellationToken)"/> overload uses.
    /// Also the correct default for a brand-new file and for any source whose original encoding
    /// could not be determined, so omitting <c>encoding</c> preserves the pre-existing behavior
    /// byte-for-byte. Deliberately NOT <see cref="Encoding.UTF8"/>: that static instance emits a
    /// BOM, so using it would ADD a preamble to every no-BOM file.
    /// </summary>
    internal static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Atomically writes <paramref name="content"/> to <paramref name="path"/>.
    /// </summary>
    /// <param name="encoding">
    /// Optional. When supplied, the temp file is written with this encoding so a source file's
    /// original BOM/encoding survives the round-trip (<c>mutation-write-paths-drop-original-encoding</c>).
    /// When <see langword="null"/> the encoding-less BCL overload is used, i.e. UTF-8 with no BOM.
    /// Declared AFTER <paramref name="logger"/> on purpose: callers that pass a logger positionally
    /// must keep binding it to <paramref name="logger"/>.
    /// </param>
    public static async Task WriteAllTextAsync(
        string path,
        string content,
        CancellationToken ct,
        ILogger? logger = null,
        Encoding? encoding = null)
        => await WriteAtomicAsync(
            path,
            tmp => encoding is null
                ? File.WriteAllTextAsync(tmp, content, ct)
                : File.WriteAllTextAsync(tmp, content, encoding, ct),
            logger).ConfigureAwait(false);

    public static async Task WriteAllBytesAsync(string path, byte[] content, CancellationToken ct, ILogger? logger = null)
        => await WriteAtomicAsync(
            path,
            tmp => File.WriteAllBytesAsync(tmp, content, ct),
            logger).ConfigureAwait(false);

    /// <summary>
    /// Resolves the <see cref="Encoding"/> a file should be REWRITTEN with, given the bytes it
    /// held before the mutation. Returns <see cref="Utf8NoBom"/> when <paramref name="existingBytes"/>
    /// is <see langword="null"/> (the file is being created) or when the original carried no
    /// preamble. Otherwise returns the BOM-derived encoding so the preamble is re-emitted verbatim.
    /// </summary>
    /// <remarks>
    /// The <c>HasPreamble</c> gate is load-bearing. <see cref="CsprojSemanticEquality.CreateSnapshot"/>
    /// reports <see cref="Encoding.UTF8"/> (BOM-emitting) for a plain no-BOM UTF-8 file, because that
    /// is the <see cref="StreamReader"/> fallback when no byte-order mark is detected. Writing with
    /// that instance would add a BOM to every no-BOM file, so the no-preamble case must fall back to
    /// <see cref="Utf8NoBom"/> instead of trusting the detected encoding.
    /// Encoding detection is BOM-based only: a BOM-less UTF-16/codepage file is indistinguishable
    /// from UTF-8 here and is rewritten as UTF-8-no-BOM, matching the pre-fix behavior.
    /// </remarks>
    internal static Encoding ResolveWriteEncoding(byte[]? existingBytes)
    {
        if (existingBytes is null || existingBytes.Length == 0)
        {
            return Utf8NoBom;
        }

        var snapshot = CsprojSemanticEquality.CreateSnapshot(existingBytes);
        return snapshot.HasPreamble ? snapshot.TextEncoding : Utf8NoBom;
    }

    /// <summary>
    /// Resolves the <see cref="Encoding"/> to write with from an encoding Roslyn already attached
    /// to a <c>SourceText</c> (<c>SourceText.Encoding</c>), which is <see langword="null"/> for text
    /// synthesized in memory.
    /// </summary>
    /// <remarks>
    /// A UTF-8 encoding that emits no preamble is normalized to the shared <see cref="Utf8NoBom"/>
    /// instance rather than passed through: Roslyn's own no-BOM UTF-8 instance is constructed with
    /// <c>throwOnInvalidBytes: true</c>, so writing through it would turn an unpaired surrogate into
    /// a mid-write <see cref="EncoderFallbackException"/> where the pre-fix path silently emitted
    /// U+FFFD. Non-UTF-8 and BOM-carrying encodings are preserved as-is.
    /// </remarks>
    internal static Encoding ResolveWriteEncoding(Encoding? sourceEncoding)
    {
        if (sourceEncoding is null)
        {
            return Utf8NoBom;
        }

        return sourceEncoding.CodePage == Utf8NoBom.CodePage && sourceEncoding.GetPreamble().Length == 0
            ? Utf8NoBom
            : sourceEncoding;
    }

    private static async Task WriteAtomicAsync(
        string path,
        Func<string, Task> writeTempAsync,
        ILogger? logger)
    {
        var tmp = path + ".tmp";
        try
        {
            await writeTempAsync(tmp).ConfigureAwait(false);
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            TryDeleteTemp(tmp, path, logger);
            throw;
        }
    }

    private static void TryDeleteTemp(string tmp, string path, ILogger? logger)
    {
        try
        {
            if (File.Exists(tmp))
            {
                File.Delete(tmp);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup — a stray .tmp is non-fatal and must not mask the original failure.
            // The primary write exception is still re-thrown by the caller; this only records that the
            // orphaned temp artifact could not be removed so it is not left on disk silently.
            logger?.LogWarning(
                ex,
                "AtomicFileWriter: failed to delete orphaned temp file {TempPath} after a failed write to {TargetPath}; a stray .tmp artifact may remain on disk.",
                tmp,
                path);
        }
    }
}
