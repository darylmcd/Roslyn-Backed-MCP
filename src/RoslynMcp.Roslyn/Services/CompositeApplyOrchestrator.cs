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
    private readonly SourceTransitionWriter _transitionAsync;

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
        _transitionAsync = static (path, expected, desired, operation, authority, token) => operation.TransitionSourceAsync(path, expected, desired, PhysicalSourceTransitionMode.FreshReplacement, authority, null, token);
    }

    internal CompositeApplyOrchestrator(IWorkspaceManager workspace, ICompositePreviewStore store,
        IChangeTracker? tracker, ILogger<CompositeApplyOrchestrator>? logger,
        IUnexpectedExceptionReporter? reporter, IUndoService? undo,
        SourceTransitionWriter transitionAsync)
        : this(workspace, store, tracker, logger, reporter, undo)
    {
        _transitionAsync = transitionAsync;
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

        IReadOnlyList<FileSnapshotDto> files = [];
        IPreparedUndoOperation? operation = null;
        PhysicalMutationRecovery? recovery = null;
        IWorkspaceMutationLease? lease = null;
        IPhysicalRestorationAuthority? desiredGraph = null;
        Exception? primaryFailure = null;
        var phaseCompleted = false;
        var recoveryOwner = _workspace as IWorkspaceRecoveryAuthority
            ?? throw InvalidOperationErrors.Internal("The workspace does not support physical recovery authority.");
        var completedMutations = 0;
        try
        {
            await recoveryOwner.AuthorizePathsAsync(workspaceId, mutations.Select(mutation => mutation.FilePath).ToArray(), ct).ConfigureAwait(false);
            foreach (var mutation in mutations) WorkspaceManager.EnsurePhysicalPath(mutation.FilePath);
            mutations = mutations.Select(mutation => mutation with { FilePath = Path.GetFullPath(mutation.FilePath) }).ToArray();
            files = await CaptureUndoFilesAsync(workspaceId, mutations, ct).ConfigureAwait(false);
            recovery = await recoveryOwner.CaptureRecoveryAsync(workspaceId, $"Composite operation ({mutations.Count} mutations)", files, ct).ConfigureAwait(false);
            operation = _undoService is null ? null : await _undoService.PrepareBeforeApplyAsync(recovery, ct).ConfigureAwait(false);
            var intendedFiles = files.ToDictionary(file => file.FilePath, FileSystemPath.Comparer);
            var transitions = new List<(FileSnapshotDto Before, FileSnapshotDto Intended)>();
            foreach (var mutation in mutations)
            {
                var before = intendedFiles[mutation.FilePath];
                var intended = mutation.DeleteFile ? new FileSnapshotDto(mutation.FilePath, null)
                    : FileSnapshotDto.FromExistingBytes(mutation.FilePath, SolutionFileMutationPlanner.Encode(mutation.UpdatedContent ?? string.Empty,
                        SourceFileEncoding.FromBytes(SnapshotBytes(before))));
                transitions.Add((before, intended));
                intendedFiles[mutation.FilePath] = intended;
            }
            // Native operations carry a captured layout. Prepare their desired graph before
            // any source-parent namespace can be introduced by physical publication.
            lease = await recoveryOwner.BeginMutationAsync(recovery, ct).ConfigureAwait(false);
            if (recovery.CapturedLayout is not null)
                desiredGraph = await recoveryOwner.StageRestorationAsync(recovery, intendedFiles.Values.ToArray(),
                    recovery.CapturedLayout, ct).ConfigureAwait(false);
            foreach (var (before, intended) in transitions)
            {
                await PhysicalMutationFileGuard.AssertMatchesAsync(before.FilePath, SnapshotBytes(before),
                    (path, token) => ReadAuthorizedBytesAsync(workspaceId, path, token), ct).ConfigureAwait(false);
                lease.BeforeWrite(intended);
                await ApplyMutationAsync(workspaceId, before, intended, recovery, ct).ConfigureAwait(false);
                completedMutations++;
            }
            var remaining = await RemainingWrittenPathsAsync(workspaceId, files, ct).ConfigureAwait(false);
            if (remaining.Count == 0)
            {
                await lease.AbortAsync(CancellationToken.None).ConfigureAwait(false);
                _compositePreviewStore.Invalidate(previewToken);
                return new ApplyResultDto(true, [], null);
            }
            if (desiredGraph is not null)
                await recoveryOwner.CommitRestorationAsync(recovery, desiredGraph.Expected, retainedRecovery: false, ct).ConfigureAwait(false);
            else
                await recoveryOwner.CommitMutationAsync(recovery, expected: null, ct).ConfigureAwait(false);
            foreach (var intended in intendedFiles.Values)
                await PhysicalMutationFileGuard.AssertMatchesAsync(intended.FilePath, SnapshotBytes(intended),
                    (path, token) => ReadAuthorizedBytesAsync(workspaceId, path, token), ct).ConfigureAwait(false);
            lease.Complete(intendedFiles.Values.ToArray());
            lease.Dispose();
            phaseCompleted = true;
            if (operation is not null) await operation.PublishAsync(ct).ConfigureAwait(false);
            _compositePreviewStore.Invalidate(previewToken);
            var distinct = remaining.ToArray();
            _changeTracker?.RecordChange(workspaceId, recovery.Description,
                distinct, "apply_composite_preview");
            return new ApplyResultDto(true, distinct, null);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
        {
            primaryFailure = failure;
            // A partial composite deliberately retains its writes. Publish only that operation's
            // recovery target; a zero-effect failure leaves the previous undo target untouched.
            var remaining = await RemainingWrittenPathsAsync(workspaceId, files, CancellationToken.None).ConfigureAwait(false);
            if (recovery is not null && (remaining.Count > 0 || recovery.HasRetainedSourceTransitions || recovery.HasUnverifiedSourceNamespaces))
            {
                if (operation is not null) operation.RetainRecovery();
                else recoveryOwner.RetainRecovery(recovery);
            }
            else if (!phaseCompleted && lease is not null) await lease.AbortAsync(CancellationToken.None).ConfigureAwait(false);
            if (failure is OperationCanceledException or IPublicMessageException) throw;
            return ProjectFailure(failure, remaining, completedMutations, mutations.Count);
        }
        catch (Exception failure) { primaryFailure = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Composite physical ownership cleanup failed.", primaryFailure, lease, desiredGraph); }
    }

    private async Task<IReadOnlyList<FileSnapshotDto>> CaptureUndoFilesAsync(
        string workspaceId, IReadOnlyList<CompositeFileMutation> mutations, CancellationToken ct)
    {
        var seen = new HashSet<string>(FileSystemPath.Comparer);
        var files = new List<FileSnapshotDto>();
        foreach (var mutation in mutations)
        {
            var path = Path.GetFullPath(mutation.FilePath);
            if (!seen.Add(path)) continue;
            var bytes = await ReadAuthorizedBytesAsync(workspaceId, path, ct).ConfigureAwait(false);
            files.Add(bytes is null ? new FileSnapshotDto(path, null) : FileSnapshotDto.FromExistingBytes(path, bytes));
        }
        return files;
    }

    private async Task<IReadOnlyList<string>> RemainingWrittenPathsAsync(
        string workspaceId, IReadOnlyList<FileSnapshotDto> files, CancellationToken ct)
    {
        var remaining = new List<string>();
        foreach (var original in files)
        {
            var path = original.FilePath;
            try
            {
                await (_workspace as IWorkspaceRecoveryAuthority
                    ?? throw InvalidOperationErrors.Internal("The workspace does not support physical recovery authority.")).AuthorizePathsAsync(workspaceId, [path], ct).ConfigureAwait(false);
                var bytes = await FileSnapshotCapture.ReadIfPresentAsync(path, PhysicalInputReader.ReadAllBytesAsync, ct).ConfigureAwait(false);
                var changed = original.OriginalText is null ? bytes is not null
                    : bytes is null || original.OriginalBytes.IsDefault
                    || !bytes.AsSpan().SequenceEqual(original.OriginalBytes.AsSpan());
                if (changed) remaining.Add(path);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                var detail = UnexpectedExceptionReporting.Report(_exceptionReporter, failure, UnexpectedExceptionCategory.CompositeApply).Public;
                _logger?.LogWarning("Could not inspect a composite target; retaining its inventory entry. correlationId={CorrelationId}", detail.CorrelationId);
                remaining.Add(path);
            }
        }
        return remaining;
    }

    private async Task<byte[]?> ReadAuthorizedBytesAsync(string workspaceId, string path, CancellationToken ct)
    {
        await (_workspace as IWorkspaceRecoveryAuthority
            ?? throw InvalidOperationErrors.Internal("The workspace does not support physical recovery authority."))
            .AuthorizePathsAsync(workspaceId, [path], ct).ConfigureAwait(false);
        return await FileSnapshotCapture.ReadIfPresentAsync(path, PhysicalInputReader.ReadAllBytesAsync, ct).ConfigureAwait(false);
    }

    private static byte[]? SnapshotBytes(FileSnapshotDto snapshot) => snapshot.OriginalText is null ? null
        : !snapshot.OriginalBytes.IsDefault ? snapshot.OriginalBytes.ToArray()
        : throw InvalidOperationErrors.Internal("Composite physical ownership requires exact captured bytes.");

    private Task ApplyMutationAsync(string workspaceId, FileSnapshotDto before, FileSnapshotDto intended,
        PhysicalMutationRecovery operation, CancellationToken ct)
    {
        var authority = _workspace as IWorkspaceRecoveryAuthority
            ?? throw InvalidOperationErrors.Internal("The workspace does not support physical recovery authority.");
        return _transitionAsync(intended.FilePath, SnapshotBytes(before), SnapshotBytes(intended), operation,
            authority, ct);
    }
    private ApplyResultDto ProjectFailure(
        Exception failure,
        IReadOnlyCollection<string> appliedFiles,
        int completedMutations,
        int mutationCount)
    {
        // Completed mutations are not rolled back; the token remains valid for inspection/re-preview.
        var applied = appliedFiles.Distinct(FileSystemPath.Comparer).ToList();
        var detail = UnexpectedExceptionReporting.Report(
            _exceptionReporter,
            failure,
            UnexpectedExceptionCategory.CompositeApply).Public;
        var failingTarget = completedMutations < mutationCount ? $"mutation[{completedMutations}]" : "workspace completion";
        _logger?.LogWarning(
            "Composite apply failed after {AppliedCount} of {TotalCount} mutation(s); failing target: {FailingTarget}. " +
            "Prior writes are left in place (no rollback); correlationId={CorrelationId}.",
            completedMutations,
            mutationCount,
            failingTarget,
            detail.CorrelationId);
        var message = appliedFiles.Count > 0
            ? $"Partial composite apply: {applied.Count} file(s) were written before the failure at {failingTarget}. " +
              $"Inspect appliedFiles, resolve the filesystem failure, and re-preview. correlationId={detail.CorrelationId}"
            : "Composite apply failed with no remaining recorded file writes. Resolve the failure and re-preview if needed. " +
              $"correlationId={detail.CorrelationId}";
        return new ApplyResultDto(false, applied, message);
    }
}
