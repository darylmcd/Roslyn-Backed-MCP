using Company.RoslynMcp.Core.Models;
using Company.RoslynMcp.Core.Services;
using Company.RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Simplification;
using Microsoft.Extensions.Logging;

namespace Company.RoslynMcp.Roslyn.Services;

public sealed class RefactoringService : IRefactoringService
{
    private readonly IWorkspaceManager _workspace;
    private readonly IPreviewStore _previewStore;
    private readonly ILogger<RefactoringService> _logger;

    public RefactoringService(IWorkspaceManager workspace, IPreviewStore previewStore, ILogger<RefactoringService> logger)
    {
        _workspace = workspace;
        _previewStore = previewStore;
        _logger = logger;
    }

    public async Task<RefactoringPreviewDto> PreviewRenameAsync(
        string filePath, int line, int column, string newName, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution();
        var symbol = await SymbolResolver.ResolveAtPositionAsync(solution, filePath, line, column, ct);
        if (symbol is null)
            throw new InvalidOperationException($"No symbol found at {filePath}:{line}:{column}");

        var newSolution = await Renamer.RenameSymbolAsync(
            solution, symbol, new SymbolRenameOptions(), newName, ct);

        var changes = await ComputeChangesAsync(solution, newSolution, ct);
        var description = $"Rename '{symbol.Name}' to '{newName}'";
        var token = _previewStore.Store(newSolution, _workspace.CurrentVersion, description);

        return new RefactoringPreviewDto(token, description, changes, null);
    }

    public async Task<ApplyResultDto> ApplyRefactoringAsync(string previewToken, CancellationToken ct)
    {
        var entry = _previewStore.Retrieve(previewToken, _workspace.CurrentVersion);
        if (entry is null)
        {
            return new ApplyResultDto(
                false, [],
                "Preview token is invalid or expired. The workspace may have changed since the preview was generated. Please create a new preview.");
        }

        var (modifiedSolution, description) = entry.Value;

        var currentSolution = _workspace.GetCurrentSolution();
        var changedDocs = modifiedSolution.GetChanges(currentSolution)
            .GetProjectChanges()
            .SelectMany(pc => pc.GetChangedDocuments())
            .ToList();

        var success = _workspace.TryApplyChanges(modifiedSolution);
        _previewStore.Invalidate(previewToken);

        if (!success)
        {
            return new ApplyResultDto(false, [], "Failed to apply changes to the workspace.");
        }

        var appliedFiles = new List<string>();
        foreach (var docId in changedDocs)
        {
            var doc = modifiedSolution.GetDocument(docId);
            if (doc?.FilePath is not null)
                appliedFiles.Add(doc.FilePath);
        }

        _logger.LogInformation("Applied refactoring '{Description}' to {Count} file(s)", description, appliedFiles.Count);
        return new ApplyResultDto(true, appliedFiles, null);
    }

    public async Task<RefactoringPreviewDto> PreviewOrganizeUsingsAsync(string filePath, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution();
        var document = SymbolResolver.FindDocument(solution, filePath);
        if (document is null)
            throw new InvalidOperationException($"Document not found: {filePath}");

        var organizedDoc = await Formatter.OrganizeImportsAsync(document, ct);
        var newSolution = organizedDoc.Project.Solution;

        var changes = await ComputeChangesAsync(solution, newSolution, ct);
        var description = $"Organize usings in '{Path.GetFileName(filePath)}'";
        var token = _previewStore.Store(newSolution, _workspace.CurrentVersion, description);

        return new RefactoringPreviewDto(token, description, changes, null);
    }

    public async Task<RefactoringPreviewDto> PreviewFormatDocumentAsync(string filePath, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution();
        var document = SymbolResolver.FindDocument(solution, filePath);
        if (document is null)
            throw new InvalidOperationException($"Document not found: {filePath}");

        var formattedDoc = await Formatter.FormatAsync(document, cancellationToken: ct);
        var newSolution = formattedDoc.Project.Solution;

        var changes = await ComputeChangesAsync(solution, newSolution, ct);
        var description = $"Format document '{Path.GetFileName(filePath)}'";
        var token = _previewStore.Store(newSolution, _workspace.CurrentVersion, description);

        return new RefactoringPreviewDto(token, description, changes, null);
    }

    private static async Task<IReadOnlyList<FileChangeDto>> ComputeChangesAsync(
        Solution oldSolution, Solution newSolution, CancellationToken ct)
    {
        var changes = new List<FileChangeDto>();
        var solutionChanges = newSolution.GetChanges(oldSolution);

        foreach (var projectChange in solutionChanges.GetProjectChanges())
        {
            foreach (var docId in projectChange.GetChangedDocuments())
            {
                var oldDoc = oldSolution.GetDocument(docId);
                var newDoc = newSolution.GetDocument(docId);
                if (oldDoc is null || newDoc is null) continue;

                var oldText = (await oldDoc.GetTextAsync(ct)).ToString();
                var newText = (await newDoc.GetTextAsync(ct)).ToString();

                if (oldText == newText) continue;

                var filePath = oldDoc.FilePath ?? oldDoc.Name;
                var diff = DiffGenerator.GenerateUnifiedDiff(oldText, newText, filePath);
                changes.Add(new FileChangeDto(filePath, diff));
            }
        }

        return changes;
    }
}
