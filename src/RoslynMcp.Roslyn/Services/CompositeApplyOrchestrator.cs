using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Services;

public sealed class CompositeApplyOrchestrator : ICompositeApplyOrchestrator
{
    private readonly IWorkspaceManager _workspace;
    private readonly ICompositePreviewStore _compositePreviewStore;
    private readonly IChangeTracker? _changeTracker;

    public CompositeApplyOrchestrator(
        IWorkspaceManager workspace,
        ICompositePreviewStore compositePreviewStore,
        IChangeTracker? changeTracker = null)
    {
        _workspace = workspace;
        _compositePreviewStore = compositePreviewStore;
        _changeTracker = changeTracker;
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

                await File.WriteAllTextAsync(mutation.FilePath, mutation.UpdatedContent ?? string.Empty, ct).ConfigureAwait(false);
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
            return new ApplyResultDto(false, appliedFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), ex.Message);
        }
    }
}
