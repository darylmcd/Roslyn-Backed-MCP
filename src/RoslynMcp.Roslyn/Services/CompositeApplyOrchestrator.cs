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
    private readonly IUnexpectedExceptionReporter? _exceptionReporter;
    private readonly IUndoService? _undoService;

    public CompositeApplyOrchestrator(
        IWorkspaceManager workspace,
        ICompositePreviewStore compositePreviewStore,
        IChangeTracker? changeTracker = null,
        ILogger<CompositeApplyOrchestrator>? logger = null,
        IUnexpectedExceptionReporter? exceptionReporter = null,
        IUndoService? undoService = null)
    {
        _workspace = workspace;
        _compositePreviewStore = compositePreviewStore;
        _changeTracker = changeTracker;
        _logger = logger;
        _exceptionReporter = exceptionReporter;
        _undoService = undoService;
    }

    public async Task<ApplyResultDto> ApplyCompositeAsync(string previewToken, CancellationToken ct)
    {
        var entry = _compositePreviewStore.Retrieve(previewToken);
        if (entry is null)
        {
            return new ApplyResultDto(false, [], "Preview token is invalid, expired, or stale because the workspace changed since the preview was generated. Please create a new preview.");
        }

        var (workspaceId, _, _, mutations) = entry.Value;
        // Lifecycle invalidation, not unrelated workspace-version changes, governs token validity.
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
            // Validate the complete set before capturing undo or writing any file. Imported
            // documents may be readable through an alias but must not become write targets.
            foreach (var mutation in mutations)
            {
                WorkspaceManager.EnsurePhysicalPath(mutation.FilePath);
            }

            await CaptureUndoSnapshotAsync(workspaceId, mutations, ct).ConfigureAwait(false);
            await ApplyMutationsAsync(mutations, appliedFiles, ct).ConfigureAwait(false);
            return await CompleteApplyAsync(
                previewToken,
                workspaceId,
                appliedFiles,
                ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return ProjectFailure(ex, appliedFiles, mutations.Count);
        }
    }

    /// <summary>
    /// apply-composite-no-undo-capture: registers ONE pre-apply snapshot covering every file the
    /// composite touches, before the first write, so <c>revert_last_apply</c> restores this
    /// composite instead of the previous apply. The snapshot is committed to revert history when
    /// <see cref="CompleteApplyAsync"/> records the change. Files the composite creates snapshot
    /// as absent (revert deletes them); files it deletes snapshot their bytes (revert recreates them).
    /// </summary>
    private async Task CaptureUndoSnapshotAsync(
        string workspaceId,
        IReadOnlyList<CompositeFileMutation> mutations,
        CancellationToken ct)
    {
        if (_undoService is null)
        {
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fileSnapshots = new List<FileSnapshotDto>(mutations.Count);
        foreach (var mutation in mutations)
        {
            // First occurrence wins: a later mutation of the same path must not re-snapshot state
            // that an earlier mutation in this composite would already have changed.
            var normalizedPath = Path.GetFullPath(mutation.FilePath);
            if (seen.Add(normalizedPath))
            {
                fileSnapshots.Add(await FileSnapshotCapture.CaptureAsync(normalizedPath, fallbackTextFactory: null, ct)
                    .ConfigureAwait(false));
            }
        }

        _undoService.CaptureBeforeApply(
            workspaceId,
            $"Composite operation ({fileSnapshots.Count} files)",
            preApplySolution: null,
            fileSnapshots);
    }

    private async Task ApplyMutationsAsync(
        IReadOnlyList<CompositeFileMutation> mutations,
        ICollection<string> appliedFiles,
        CancellationToken ct)
    {
        foreach (var mutation in mutations)
        {
            await ApplyMutationAsync(mutation, ct).ConfigureAwait(false);
            appliedFiles.Add(mutation.FilePath);
        }
    }

    private async Task ApplyMutationAsync(CompositeFileMutation mutation, CancellationToken ct)
    {
        if (mutation.DeleteFile)
        {
            if (File.Exists(mutation.FilePath))
            {
                File.Delete(mutation.FilePath);
            }

            return;
        }

        var directory = Path.GetDirectoryName(mutation.FilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Existing source files retain their on-disk encoding; new files remain UTF-8 without BOM.
        var preApplyBytes = File.Exists(mutation.FilePath)
            ? await File.ReadAllBytesAsync(mutation.FilePath, ct).ConfigureAwait(false)
            : null;
        await AtomicFileWriter.WriteAllTextAsync(
            mutation.FilePath,
            mutation.UpdatedContent ?? string.Empty,
            ct,
            _logger,
            encoding: SourceFileEncoding.FromBytes(preApplyBytes),
            exceptionReporter: _exceptionReporter).ConfigureAwait(false);
    }

    private async Task<ApplyResultDto> CompleteApplyAsync(
        string previewToken,
        string workspaceId,
        IReadOnlyCollection<string> appliedFiles,
        CancellationToken ct)
    {
        await _workspace.ReloadAsync(workspaceId, ct).ConfigureAwait(false);
        _compositePreviewStore.Invalidate(previewToken);
        var distinctFiles = appliedFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        _changeTracker?.RecordChange(
            workspaceId,
            $"Composite operation ({distinctFiles.Count} files)",
            distinctFiles,
            "apply_composite_preview");
        return new ApplyResultDto(true, distinctFiles, null);
    }

    private ApplyResultDto ProjectFailure(
        Exception failure,
        IReadOnlyCollection<string> appliedFiles,
        int mutationCount)
    {
        // Completed mutations are not rolled back; the token remains valid for inspection/re-preview.
        var applied = appliedFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var detail = UnexpectedExceptionReporting.Report(
            _exceptionReporter,
            failure,
            UnexpectedExceptionCategory.CompositeApply).Public;
        var failingTarget = $"mutation[{appliedFiles.Count}]";
        _logger?.LogWarning(
            "Composite apply failed after {AppliedCount} of {TotalCount} mutation(s); failing target: {FailingTarget}. " +
            "Prior writes are left in place (no rollback); correlationId={CorrelationId}.",
            appliedFiles.Count,
            mutationCount,
            failingTarget,
            detail.CorrelationId);
        var message = appliedFiles.Count > 0
            ? $"Partial composite apply: {applied.Count} file(s) were written before the failure at {failingTarget}. " +
              $"Inspect appliedFiles, resolve the filesystem failure, and re-preview. correlationId={detail.CorrelationId}"
            : "Composite apply failed before any files were written. Resolve the filesystem failure and retry with the same token. " +
              $"correlationId={detail.CorrelationId}";
        return new ApplyResultDto(false, applied, message);
    }
}

/// <summary>
/// Writes through a same-directory temporary sibling before atomically replacing the target.
/// Cleanup failures are secret-safe warnings and never mask the primary write failure.
/// </summary>
internal static class AtomicFileWriter
{
    /// <summary>
    /// Stable category token for secret-safe temp-cleanup diagnostics.
    /// </summary>
    private const string TempCleanupCategory = "CompositeApplyTempCleanup";

    /// <summary>
    /// Atomically writes <paramref name="content"/> to <paramref name="path"/>.
    /// </summary>
    /// <param name="encoding">Existing source encoding, or null for UTF-8 without BOM.</param>
    public static async Task WriteAllTextAsync(
        string path,
        string content,
        CancellationToken ct,
        ILogger? logger = null,
        Encoding? encoding = null,
        IUnexpectedExceptionReporter? exceptionReporter = null)
        => await WriteAtomicAsync(
            path,
            tmp => encoding is null
                ? File.WriteAllTextAsync(tmp, content, ct)
                : File.WriteAllTextAsync(tmp, content, encoding, ct),
            ct,
            logger,
            exceptionReporter).ConfigureAwait(false);

    public static async Task WriteAllBytesAsync(
        string path,
        byte[] content,
        CancellationToken ct,
        ILogger? logger = null,
        IUnexpectedExceptionReporter? exceptionReporter = null)
        => await WriteAtomicAsync(
            path,
            tmp => File.WriteAllBytesAsync(tmp, content, ct),
            ct,
            logger,
            exceptionReporter).ConfigureAwait(false);

    internal static Task WriteAtomicAsync(
        string path,
        Func<string, Task> writeTempAsync,
        CancellationToken ct,
        ILogger? logger,
        IUnexpectedExceptionReporter? exceptionReporter)
    {
        ct.ThrowIfCancellationRequested();
        return WriteAtomicAtTempPathAsync(
            path, ReserveUniqueTempPath(path), writeTempAsync, ct, logger, exceptionReporter);
    }

    private static string ReserveUniqueTempPath(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))
            ?? throw new InvalidOperationException("The target has no containing directory.");
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var candidate = Path.Combine(directory, ".rmcp-" + Path.GetRandomFileName() + ".tmp");
            try
            {
                using var reservation = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                return candidate;
            }
            catch (IOException) when (File.Exists(candidate))
            {
                // The create-new operation is atomic; a collision gets another name.
            }
        }

        throw new IOException("Could not reserve a unique atomic-write temporary file.");
    }

    // The explicit path is also used by fault-injection tests for temp cleanup.
    internal static async Task WriteAtomicAtTempPathAsync(
        string path,
        string tmp,
        Func<string, Task> writeTempAsync,
        CancellationToken ct,
        ILogger? logger,
        IUnexpectedExceptionReporter? exceptionReporter)
    {
        try
        {
            await writeTempAsync(tmp).ConfigureAwait(false);
            await ReplaceAfterTransientReaderAsync(tmp, path, ct).ConfigureAwait(false);
        }
        catch
        {
            TryDeleteTemp(tmp, path, logger, exceptionReporter);
            throw;
        }
    }

    private static async Task ReplaceAfterTransientReaderAsync(
        string tmp,
        string path,
        CancellationToken ct)
    {
        const int maxAttempts = 10;
        for (var attempt = 1; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                File.Move(tmp, path, overwrite: true);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts &&
                                       IsRetryableWindowsReplacementFailure(ex))
            {
                await Task.Delay(Math.Min(25 << (attempt - 1), 400), ct).ConfigureAwait(false);
            }
        }
    }

    private static bool IsRetryableWindowsReplacementFailure(Exception exception)
    {
        if (!OperatingSystem.IsWindows()) return false;
        if (exception is IOException io && IsSharingViolation(io)) return true;
        // On Windows, File.Move(overwrite: true) reports access denied for a held
        // destination even when the reader shares writes and deletes. The retry is
        // bounded; a real ACL denial still surfaces after the last attempt.
        return exception is UnauthorizedAccessException { HResult: unchecked((int)0x80070005) };
    }

    private static bool IsSharingViolation(IOException exception) =>
        exception.HResult is unchecked((int)0x80070020) or unchecked((int)0x80070021);

    private static void TryDeleteTemp(
        string tmp,
        string path,
        ILogger? logger,
        IUnexpectedExceptionReporter? exceptionReporter)
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
            // Best-effort cleanup — a stray temp is non-fatal and must not mask the original failure.
            // The primary write exception is still re-thrown by the caller; this only records that the
            // orphaned temp artifact could not be removed so it is not left on disk silently.
            // The caught exception and the absolute temp/target paths are deliberately NOT logged
            // (atomic-file-cleanup-error-detail-redaction): only the stable cleanup category, the
            // target's file name, and the shared secret-safe projection reach the sinks.
            if (logger is not null)
            {
                var diagnostic = UnexpectedExceptionReporting.Report(
                    exceptionReporter,
                    ex,
                    UnexpectedExceptionCategory.CompositeApply).Server;
                logger.LogWarning(
                    "{CleanupCategory}: failed to delete an orphaned temp file for {TargetFile} after a failed write; a temp artifact may remain on disk. correlationId={CorrelationId} exceptionTypes={ExceptionTypes} stackFrameCount={StackFrameCount}",
                    TempCleanupCategory,
                    Path.GetFileName(path),
                    diagnostic.CorrelationId,
                    string.Join(" -> ", diagnostic.ExceptionTypes),
                    diagnostic.StackFrameCount);
            }
        }
    }
}
