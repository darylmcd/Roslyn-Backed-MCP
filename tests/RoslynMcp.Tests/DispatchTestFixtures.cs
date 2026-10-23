using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Tests;

// Linked and direct-token gates stay distinct: cancellation-forwarding tests require the
// former, while route-only fixtures deliberately forward the caller's exact token.
internal sealed class RecordingDispatchGate : IWorkspaceExecutionGate
{
    public int ReadCallCount { get; private set; }
    public int WriteCallCount { get; private set; }
    public string? LastReadWorkspaceId { get; private set; }
    public string? LastWriteWorkspaceId { get; private set; }

    public async Task<T> RunReadAsync<T>(string workspaceId, Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        ReadCallCount++;
        LastReadWorkspaceId = workspaceId;
        // Pass a fresh CTS-linked token so the PassesCancellationTokenToServiceCall test
        // can distinguish "helper forwarded a token" from "helper dropped to default".
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        return await action(linked.Token).ConfigureAwait(false);
    }

    public async Task<T> RunWriteAsync<T>(
        string workspaceId,
        Func<CancellationToken, Task<T>> action,
        CancellationToken ct,
        bool applyStalenessPolicy = true)
    {
        _ = applyStalenessPolicy;
        WriteCallCount++;
        LastWriteWorkspaceId = workspaceId;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        return await action(linked.Token).ConfigureAwait(false);
    }

    public Task<T> RunLoadGateAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
        => throw new NotSupportedException("ToolDispatch must not route through RunLoadGateAsync.");

    public void RemoveGate(string workspaceId)
        => throw new NotSupportedException("ToolDispatch must not call RemoveGate.");
}

internal sealed class ForwardingDispatchGate : IWorkspaceExecutionGate
{
    public async Task<T> RunReadAsync<T>(string workspaceId, Func<CancellationToken, Task<T>> action, CancellationToken ct)
        => await action(ct).ConfigureAwait(false);

    public async Task<T> RunWriteAsync<T>(
        string workspaceId,
        Func<CancellationToken, Task<T>> action,
        CancellationToken ct,
        bool applyStalenessPolicy = true)
    {
        _ = applyStalenessPolicy;
        return await action(ct).ConfigureAwait(false);
    }

    public Task<T> RunLoadGateAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
        => throw new NotSupportedException("Token apply must not route through RunLoadGateAsync.");

    public void RemoveGate(string workspaceId)
        => throw new NotSupportedException("Token apply must not call RemoveGate.");
}

internal sealed class DispatchPreviewStore : IPreviewStore
{
    private readonly string _token;
    private readonly string _workspaceId;

    public DispatchPreviewStore(string token, string workspaceId)
    {
        _token = token;
        _workspaceId = workspaceId;
    }

    /// <summary>
    /// preview-apply-token-write-path-toctou: write set the fake reports for its token, or
    /// <see langword="null"/> (the fake's explicit default)
    /// for "unknown".
    /// </summary>
    public IReadOnlyList<string>? ChangedPaths { get; init; }

    /// <summary>
    /// preview-token-apply-route-provenance: producer family the fake reports for its token.
    /// Defaults to <see cref="PreviewKind.Unspecified"/>, matching both the interface default
    /// and an untagged producer.
    /// </summary>
    public PreviewKind Kind { get; init; } = PreviewKind.Unspecified;

    public string? PeekWorkspaceId(string token) => token == _token ? _workspaceId : null;

    public IReadOnlyList<string>? PeekChangedPaths(string token) => token == _token ? ChangedPaths : null;

    public PreviewKind PeekKind(string token) => token == _token ? Kind : PreviewKind.Unspecified;

    public string Store(string workspaceId, Solution originalSolution, Solution modifiedSolution, int workspaceVersion,
        string description, IReadOnlyList<FileChangeDto> changes, PreviewKind kind)
        => throw new NotSupportedException();

    public (string WorkspaceId, Solution OriginalSolution, Solution ModifiedSolution, int WorkspaceVersion, string Description, bool DiffTruncated)? Retrieve(string token)
        => throw new NotSupportedException();

    public void Invalidate(string token) => throw new NotSupportedException();

    public void InvalidateAll(string? workspaceId = null) => throw new NotSupportedException();

    public void InvalidateOnVersionBump(string workspaceId, int newWorkspaceVersion) => throw new NotSupportedException();
}

internal class RecordingApplyRefactoringService : IRefactoringService
{
    private readonly string _appliedFile;

    public RecordingApplyRefactoringService(string appliedFile) => _appliedFile = appliedFile;

    public int ApplyCallCount { get; private set; }

    public virtual Task<ApplyResultDto> ApplyRefactoringAsync(string previewToken, string toolName, CancellationToken ct)
    {
        ApplyCallCount++;
        return Task.FromResult(new ApplyResultDto(true, new[] { _appliedFile }, null));
    }

    public virtual Task<RefactoringApplyOperationResult> ApplyRefactoringWithUndoAsync(string previewToken, string toolName, bool force, CancellationToken ct) =>
        throw new NotSupportedException("This apply-routing fixture does not supply operation-owned compensation.");

    public virtual Task<ApplyResultDto> ApplyRefactoringAsync(string previewToken, string toolName, bool force, CancellationToken ct)
        => ApplyRefactoringAsync(previewToken, toolName, ct);

    public Task<RefactoringPreviewDto> PreviewRenameAsync(string workspaceId, SymbolLocator locator, string newName, CancellationToken ct)
        => throw new NotSupportedException();

    public Task<RefactoringPreviewDto> PreviewRenameAsync(string workspaceId, SymbolLocator locator, string newName, bool summary, CancellationToken ct)
        => throw new NotSupportedException();

    public Task<RefactoringPreviewDto> PreviewOrganizeUsingsAsync(string workspaceId, string filePath, CancellationToken ct)
        => throw new NotSupportedException();

    public Task<RefactoringPreviewDto> PreviewFormatDocumentAsync(string workspaceId, string filePath, CancellationToken ct)
        => throw new NotSupportedException();

    public Task<RefactoringPreviewDto> PreviewFormatRangeAsync(string workspaceId, string filePath, int startLine, int startColumn, int endLine, int endColumn, CancellationToken ct)
        => throw new NotSupportedException();

    public Task<RefactoringPreviewDto> PreviewCodeFixAsync(string workspaceId, string diagnosticId, string filePath, int line, int column, string? fixId, CancellationToken ct)
        => throw new NotSupportedException();
}
