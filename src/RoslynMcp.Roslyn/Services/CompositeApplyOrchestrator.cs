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

    public CompositeApplyOrchestrator(
        IWorkspaceManager workspace,
        ICompositePreviewStore compositePreviewStore,
        IChangeTracker? changeTracker = null,
        ILogger<CompositeApplyOrchestrator>? logger = null,
        IUnexpectedExceptionReporter? exceptionReporter = null)
    {
        _workspace = workspace;
        _compositePreviewStore = compositePreviewStore;
        _changeTracker = changeTracker;
        _logger = logger;
        _exceptionReporter = exceptionReporter;
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
            logger,
            exceptionReporter).ConfigureAwait(false);

    private static async Task WriteAtomicAsync(
        string path,
        Func<string, Task> writeTempAsync,
        ILogger? logger,
        IUnexpectedExceptionReporter? exceptionReporter)
    {
        var tmp = path + ".tmp";
        try
        {
            await writeTempAsync(tmp).ConfigureAwait(false);
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            TryDeleteTemp(tmp, path, logger, exceptionReporter);
            throw;
        }
    }

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
            // Best-effort cleanup — a stray .tmp is non-fatal and must not mask the original failure.
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
                    "{CleanupCategory}: failed to delete orphaned temp file {TargetFile}.tmp after a failed write; a stray .tmp artifact may remain on disk. correlationId={CorrelationId} exceptionTypes={ExceptionTypes} stackFrameCount={StackFrameCount}",
                    TempCleanupCategory,
                    Path.GetFileName(path),
                    diagnostic.CorrelationId,
                    string.Join(" -> ", diagnostic.ExceptionTypes),
                    diagnostic.StackFrameCount);
            }
        }
    }
}
