using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Item 7 implementation. Walks the supplied operation list sequentially. Each operation
/// produces a delta against the accumulating Solution; the final snapshot is stored once via
/// <see cref="IPreviewStore"/> so a single token covers the whole batch.
///
/// <para>
/// Operations are atomic at preview time: the first op that fails aborts the entire preview
/// (no partial token is issued). Order matters — later ops see the rewritten text from earlier
/// ops, which is documented in the operation kind table on <see cref="ISymbolRefactorService"/>.
/// </para>
/// </summary>
public sealed class SymbolRefactorService : ISymbolRefactorService
{
    private const int MaxOperations = 25;
    private const int MaxFilesAffected = 500;

    private readonly IWorkspaceManager _workspace;
    private readonly IPreviewStore _previewStore;
    private readonly IRefactoringService _refactoringService;
    private readonly IEditService _editService;
    private readonly IRestructureService _restructureService;

    public SymbolRefactorService(
        IWorkspaceManager workspace,
        IPreviewStore previewStore,
        IRefactoringService refactoringService,
        IEditService editService,
        IRestructureService restructureService)
    {
        _workspace = workspace;
        _previewStore = previewStore;
        _refactoringService = refactoringService;
        _editService = editService;
        _restructureService = restructureService;
    }

    public async Task<RefactoringPreviewDto> PreviewAsync(
        string workspaceId, IReadOnlyList<SymbolRefactorOperation> operations, CancellationToken ct)
    {
        if (operations is null || operations.Count == 0)
            throw new InvalidOperationException("symbol_refactor_preview requires at least one operation.");
        if (operations.Count > MaxOperations)
            throw new InvalidOperationException(
                $"symbol_refactor_preview accepts at most {MaxOperations} operations per call.");

        var aggregatedDiffs = new Dictionary<string, FileChangeDto>(StringComparer.OrdinalIgnoreCase);
        var descriptions = new List<string>();

        for (var i = 0; i < operations.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var op = operations[i];
            try
            {
                var stepPreview = await ExecuteOperationAsync(workspaceId, op, ct).ConfigureAwait(false);
                descriptions.Add($"[{i + 1}/{operations.Count}] {op.Kind}: {stepPreview.Description}");

                foreach (var change in stepPreview.Changes)
                {
                    aggregatedDiffs[change.FilePath] = change;
                }

                // The previous step persisted its own preview token; we don't need it after the
                // text changes have flowed into the workspace. Apply the step so the next op
                // sees the rewritten state.
                await _refactoringService.ApplyRefactoringAsync(stepPreview.PreviewToken, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InvalidOperationException(
                    $"symbol_refactor_preview operation #{i + 1} ({op.Kind}) failed: {ex.Message}", ex);
            }

            if (aggregatedDiffs.Count > MaxFilesAffected)
            {
                throw new InvalidOperationException(
                    $"symbol_refactor_preview exceeded the {MaxFilesAffected}-file diff cap. Split the operation list.");
            }
        }

        // After applying every step the workspace now reflects the final state. Snapshot the
        // current solution and store it as the composite preview token. (Note: each sub-op
        // already wrote to disk via apply — symbol_refactor_apply will be a no-op; this hands
        // the user a unified diff while leaving the workspace in the post-refactor state.)
        var finalSolution = _workspace.GetCurrentSolution(workspaceId);
        var description = "Composite refactor:\n  " + string.Join("\n  ", descriptions);
        var token = _previewStore.Store(workspaceId, finalSolution, _workspace.GetCurrentVersion(workspaceId), description);

        return new RefactoringPreviewDto(
            PreviewToken: token,
            Description: description,
            Changes: aggregatedDiffs.Values.ToArray(),
            Warnings: null);
    }

    private async Task<RefactoringPreviewDto> ExecuteOperationAsync(
        string workspaceId, SymbolRefactorOperation op, CancellationToken ct)
    {
        return op.Kind?.ToLowerInvariant() switch
        {
            "rename" => await ExecuteRenameAsync(workspaceId, op, ct).ConfigureAwait(false),
            "edit" => await ExecuteEditAsync(workspaceId, op, ct).ConfigureAwait(false),
            "restructure" => await ExecuteRestructureAsync(workspaceId, op, ct).ConfigureAwait(false),
            _ => throw new ArgumentException(
                $"Unsupported operation kind '{op.Kind}'. Valid: rename, edit, restructure."),
        };
    }

    private Task<RefactoringPreviewDto> ExecuteRenameAsync(string workspaceId, SymbolRefactorOperation op, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(op.NewName))
            throw new ArgumentException("kind='rename' requires NewName.");
        var locator = new SymbolLocator(op.FilePath, op.Line, op.Column, op.SymbolHandle, op.MetadataName);
        locator.Validate();
        return _refactoringService.PreviewRenameAsync(workspaceId, locator, op.NewName, ct);
    }

    private Task<RefactoringPreviewDto> ExecuteEditAsync(string workspaceId, SymbolRefactorOperation op, CancellationToken ct)
    {
        if (op.FileEdits is null || op.FileEdits.Count == 0)
            throw new ArgumentException("kind='edit' requires FileEdits.");
        return _editService.PreviewMultiFileTextEditsAsync(workspaceId, op.FileEdits, ct, skipSyntaxCheck: false);
    }

    private Task<RefactoringPreviewDto> ExecuteRestructureAsync(string workspaceId, SymbolRefactorOperation op, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(op.Pattern) || op.Goal is null)
            throw new ArgumentException("kind='restructure' requires Pattern and Goal.");
        return _restructureService.PreviewRestructureAsync(
            workspaceId, op.Pattern, op.Goal,
            new RestructureScope(op.ScopeFilePath, op.ScopeProjectName), ct);
    }
}
