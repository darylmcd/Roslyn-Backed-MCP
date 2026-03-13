using Company.RoslynMcp.Core.Models;
using Company.RoslynMcp.Core.Services;
using Company.RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
        string workspaceId, SymbolLocator locator, string newName, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct);
        if (symbol is null)
            throw new InvalidOperationException("No symbol found for the provided rename target.");

        var newSolution = await Renamer.RenameSymbolAsync(
            solution, symbol, new SymbolRenameOptions(), newName, ct);

        var changes = await ComputeChangesAsync(solution, newSolution, ct);
        var description = $"Rename '{symbol.Name}' to '{newName}'";
        var token = _previewStore.Store(workspaceId, newSolution, _workspace.GetCurrentVersion(workspaceId), description);

        return new RefactoringPreviewDto(token, description, changes, null);
    }

    public async Task<ApplyResultDto> ApplyRefactoringAsync(string previewToken, CancellationToken ct)
    {
        var entry = _previewStore.Retrieve(previewToken);
        if (entry is null)
        {
            return new ApplyResultDto(
                false, [],
                "Preview token is invalid, expired, or stale because the workspace changed since the preview was generated. Please create a new preview.");
        }

        var (workspaceId, modifiedSolution, workspaceVersion, description) = entry.Value;
        if (_workspace.GetCurrentVersion(workspaceId) != workspaceVersion)
        {
            _previewStore.Invalidate(previewToken);
            return new ApplyResultDto(
                false,
                [],
                "Preview token is stale because the target workspace changed. Please create a new preview.");
        }

        var currentSolution = _workspace.GetCurrentSolution(workspaceId);
        var changedDocs = modifiedSolution.GetChanges(currentSolution)
            .GetProjectChanges()
            .SelectMany(pc => pc.GetChangedDocuments())
            .ToList();

        var success = _workspace.TryApplyChanges(workspaceId, modifiedSolution);
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

    public async Task<RefactoringPreviewDto> PreviewOrganizeUsingsAsync(string workspaceId, string filePath, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var document = SymbolResolver.FindDocument(solution, filePath);
        if (document is null)
            throw new InvalidOperationException($"Document not found: {filePath}");

        var root = await document.GetSyntaxRootAsync(ct)
            ?? throw new InvalidOperationException($"Could not get syntax root for '{filePath}'.");
        var syntaxTree = await document.GetSyntaxTreeAsync(ct)
            ?? throw new InvalidOperationException($"Could not get syntax tree for '{filePath}'.");
        var compilation = await document.Project.GetCompilationAsync(ct)
            ?? throw new InvalidOperationException($"Could not compile project for '{filePath}'.");

        var unnecessaryUsings = compilation.GetDiagnostics(ct)
            .Where(diagnostic => diagnostic.Id == "CS8019" && diagnostic.Location.SourceTree == syntaxTree)
            .Select(diagnostic => root.FindNode(diagnostic.Location.SourceSpan))
            .OfType<UsingDirectiveSyntax>()
            .Distinct()
            .ToList();

        if (unnecessaryUsings.Count > 0)
        {
            root = root.RemoveNodes(unnecessaryUsings, SyntaxRemoveOptions.KeepExteriorTrivia)
                ?? root;
            document = document.WithSyntaxRoot(root);
        }

        var organizedDoc = await Formatter.OrganizeImportsAsync(document, ct);
        var newSolution = organizedDoc.Project.Solution;

        var changes = await ComputeChangesAsync(solution, newSolution, ct);
        var description = $"Organize usings in '{Path.GetFileName(filePath)}'";
        var token = _previewStore.Store(workspaceId, newSolution, _workspace.GetCurrentVersion(workspaceId), description);

        return new RefactoringPreviewDto(token, description, changes, null);
    }

    public async Task<RefactoringPreviewDto> PreviewFormatDocumentAsync(string workspaceId, string filePath, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var document = SymbolResolver.FindDocument(solution, filePath);
        if (document is null)
            throw new InvalidOperationException($"Document not found: {filePath}");

        var formattedDoc = await Formatter.FormatAsync(document, cancellationToken: ct);
        var newSolution = formattedDoc.Project.Solution;

        var changes = await ComputeChangesAsync(solution, newSolution, ct);
        var description = $"Format document '{Path.GetFileName(filePath)}'";
        var token = _previewStore.Store(workspaceId, newSolution, _workspace.GetCurrentVersion(workspaceId), description);

        return new RefactoringPreviewDto(token, description, changes, null);
    }

    public async Task<RefactoringPreviewDto> PreviewCodeFixAsync(
        string workspaceId,
        string diagnosticId,
        string filePath,
        int line,
        int column,
        string? fixId,
        CancellationToken ct)
    {
        var normalizedFixId = string.IsNullOrWhiteSpace(fixId) ? GetDefaultFixId(diagnosticId) : fixId;
        if (!string.Equals(diagnosticId, "CS8019", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(normalizedFixId, "remove_unused_using", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Diagnostic '{diagnosticId}' does not have a supported curated code fix.");
        }

        var solution = _workspace.GetCurrentSolution(workspaceId);
        var document = SymbolResolver.FindDocument(solution, filePath);
        if (document is null)
        {
            throw new InvalidOperationException($"Document not found: {filePath}");
        }

        var syntaxTree = await document.GetSyntaxTreeAsync(ct)
            ?? throw new InvalidOperationException($"Could not get syntax tree for '{filePath}'.");
        var root = await document.GetSyntaxRootAsync(ct)
            ?? throw new InvalidOperationException($"Could not get syntax root for '{filePath}'.");
        var compilation = await document.Project.GetCompilationAsync(ct)
            ?? throw new InvalidOperationException($"Could not compile project for '{filePath}'.");

        var diagnostic = compilation.GetDiagnostics(ct)
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Id, diagnosticId, StringComparison.OrdinalIgnoreCase) &&
                candidate.Location.IsInSource &&
                candidate.Location.SourceTree == syntaxTree &&
                candidate.Location.GetLineSpan().StartLinePosition.Line + 1 == line &&
                candidate.Location.GetLineSpan().StartLinePosition.Character + 1 == column);

        if (diagnostic is null)
        {
            throw new InvalidOperationException(
                $"Diagnostic '{diagnosticId}' was not found at {filePath}:{line}:{column}.");
        }

        var usingDirective = root.FindNode(diagnostic.Location.SourceSpan).FirstAncestorOrSelf<UsingDirectiveSyntax>()
            ?? root.FindNode(diagnostic.Location.SourceSpan) as UsingDirectiveSyntax;
        if (usingDirective is null)
        {
            throw new InvalidOperationException("The unused using directive could not be resolved.");
        }

        var newRoot = root.RemoveNode(usingDirective, SyntaxRemoveOptions.KeepExteriorTrivia)
            ?? throw new InvalidOperationException("Failed to remove the unused using directive.");
        var newSolution = document.WithSyntaxRoot(newRoot).Project.Solution;
        var changes = await ComputeChangesAsync(solution, newSolution, ct);
        var description = $"Apply code fix '{normalizedFixId}' for {diagnosticId} in '{Path.GetFileName(filePath)}'";
        var token = _previewStore.Store(workspaceId, newSolution, _workspace.GetCurrentVersion(workspaceId), description);

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

    private static string GetDefaultFixId(string diagnosticId) =>
        diagnosticId switch
        {
            "CS8019" => "remove_unused_using",
            _ => string.Empty
        };
}
