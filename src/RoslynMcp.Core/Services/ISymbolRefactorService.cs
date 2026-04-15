using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Item 7 (v1.18, <c>agent-symbol-refactor-unified-preview</c>): chains heterogeneous refactor
/// operations (rename + multi-file edits + structural rewrites) into a single composite preview
/// token. Operations run sequentially against an accumulating Solution snapshot so later ops
/// see the text produced by earlier ops.
/// </summary>
public interface ISymbolRefactorService
{
    Task<RefactoringPreviewDto> PreviewAsync(
        string workspaceId,
        IReadOnlyList<SymbolRefactorOperation> operations,
        CancellationToken ct);
}

/// <summary>
/// One step in a composite refactor. <see cref="Kind"/> selects the shape of the remaining
/// fields:
/// <list type="bullet">
///   <item><description><c>rename</c> — supply <see cref="SymbolHandle"/> (or <see cref="FilePath"/> + <see cref="Line"/> + <see cref="Column"/>) and <see cref="NewName"/>.</description></item>
///   <item><description><c>edit</c> — supply <see cref="FileEdits"/> for direct multi-file text edits.</description></item>
///   <item><description><c>restructure</c> — supply <see cref="Pattern"/> + <see cref="Goal"/> (and optional <see cref="ScopeFilePath"/> / <see cref="ScopeProjectName"/>).</description></item>
/// </list>
/// </summary>
public sealed record SymbolRefactorOperation(
    string Kind,
    string? SymbolHandle = null,
    string? FilePath = null,
    int? Line = null,
    int? Column = null,
    string? MetadataName = null,
    string? NewName = null,
    IReadOnlyList<FileEditsDto>? FileEdits = null,
    string? Pattern = null,
    string? Goal = null,
    string? ScopeFilePath = null,
    string? ScopeProjectName = null);
