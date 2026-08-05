using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;

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
/// </summary>
internal static class AtomicFileWriter
{
    public static async Task WriteAllTextAsync(string path, string content, CancellationToken ct, ILogger? logger = null)
        => await WriteAtomicAsync(
            path,
            tmp => File.WriteAllTextAsync(tmp, content, ct),
            logger).ConfigureAwait(false);

    public static async Task WriteAllBytesAsync(string path, byte[] content, CancellationToken ct, ILogger? logger = null)
        => await WriteAtomicAsync(
            path,
            tmp => File.WriteAllBytesAsync(tmp, content, ct),
            logger).ConfigureAwait(false);

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
