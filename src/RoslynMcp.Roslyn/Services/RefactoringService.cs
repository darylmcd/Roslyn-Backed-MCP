using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Simplification;
using Microsoft.Extensions.Logging;
using System.Xml.Linq;

namespace RoslynMcp.Roslyn.Services;

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
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
        if (symbol is null)
            throw new InvalidOperationException("No symbol found for the provided rename target.");

        var newSolution = await Renamer.RenameSymbolAsync(
            solution, symbol, new SymbolRenameOptions(), newName, ct).ConfigureAwait(false);

        var changes = await SolutionDiffHelper.ComputeChangesAsync(solution, newSolution, ct).ConfigureAwait(false);
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
        var solutionChanges = modifiedSolution.GetChanges(currentSolution);
        var hasDocumentSetChanges = solutionChanges.GetProjectChanges()
            .Any(projectChange => projectChange.GetAddedDocuments().Any() || projectChange.GetRemovedDocuments().Any());

        bool success;
        IReadOnlyList<string> appliedFiles;
        if (hasDocumentSetChanges)
        {
            (success, appliedFiles) = await PersistDocumentSetChangesAsync(
                workspaceId,
                currentSolution,
                modifiedSolution,
                solutionChanges,
                ct).ConfigureAwait(false);
        }
        else
        {
            success = _workspace.TryApplyChanges(workspaceId, modifiedSolution);
            appliedFiles = solutionChanges.GetProjectChanges()
                .SelectMany(projectChange => projectChange.GetChangedDocuments())
                .Select(documentId => modifiedSolution.GetDocument(documentId)?.FilePath)
                .Where(filePath => !string.IsNullOrWhiteSpace(filePath))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        _previewStore.Invalidate(previewToken);

        if (!success)
        {
            return new ApplyResultDto(false, [], "Failed to apply changes to the workspace.");
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

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Could not get syntax root for '{filePath}'.");
        var syntaxTree = await document.GetSyntaxTreeAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Could not get syntax tree for '{filePath}'.");
        var compilation = await document.Project.GetCompilationAsync(ct).ConfigureAwait(false)
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

        var organizedDoc = await Formatter.OrganizeImportsAsync(document, ct).ConfigureAwait(false);
        var newSolution = organizedDoc.Project.Solution;

        var changes = await SolutionDiffHelper.ComputeChangesAsync(solution, newSolution, ct).ConfigureAwait(false);
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

        var formattedDoc = await Formatter.FormatAsync(document, cancellationToken: ct).ConfigureAwait(false);
        var newSolution = formattedDoc.Project.Solution;

        var changes = await SolutionDiffHelper.ComputeChangesAsync(solution, newSolution, ct).ConfigureAwait(false);
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

        var syntaxTree = await document.GetSyntaxTreeAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Could not get syntax tree for '{filePath}'.");
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Could not get syntax root for '{filePath}'.");
        var compilation = await document.Project.GetCompilationAsync(ct).ConfigureAwait(false)
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
        var changes = await SolutionDiffHelper.ComputeChangesAsync(solution, newSolution, ct).ConfigureAwait(false);
        var description = $"Apply code fix '{normalizedFixId}' for {diagnosticId} in '{Path.GetFileName(filePath)}'";
        var token = _previewStore.Store(workspaceId, newSolution, _workspace.GetCurrentVersion(workspaceId), description);

        return new RefactoringPreviewDto(token, description, changes, null);
    }

    private async Task<(bool Success, IReadOnlyList<string> AppliedFiles)> PersistDocumentSetChangesAsync(
        string workspaceId,
        Solution currentSolution,
        Solution modifiedSolution,
        SolutionChanges solutionChanges,
        CancellationToken ct)
    {
        var appliedFiles = new List<string>();

        try
        {
            foreach (var projectChange in solutionChanges.GetProjectChanges())
            {
                await PersistProjectReferenceChangesAsync(currentSolution, modifiedSolution, projectChange, appliedFiles, ct).ConfigureAwait(false);

                foreach (var documentId in projectChange.GetAddedDocuments())
                {
                    var document = modifiedSolution.GetDocument(documentId);
                    if (document?.FilePath is null)
                    {
                        continue;
                    }

                    var directory = Path.GetDirectoryName(document.FilePath);
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    var text = (await document.GetTextAsync(ct).ConfigureAwait(false)).ToString();
                    await File.WriteAllTextAsync(document.FilePath, text, ct).ConfigureAwait(false);
                    appliedFiles.Add(document.FilePath);
                }

                foreach (var documentId in projectChange.GetChangedDocuments())
                {
                    var document = modifiedSolution.GetDocument(documentId);
                    if (document?.FilePath is null)
                    {
                        continue;
                    }

                    var text = (await document.GetTextAsync(ct).ConfigureAwait(false)).ToString();
                    await File.WriteAllTextAsync(document.FilePath, text, ct).ConfigureAwait(false);
                    appliedFiles.Add(document.FilePath);
                }

                foreach (var documentId in projectChange.GetRemovedDocuments())
                {
                    var document = currentSolution.GetDocument(documentId);
                    if (document?.FilePath is null)
                    {
                        continue;
                    }

                    if (File.Exists(document.FilePath))
                    {
                        File.Delete(document.FilePath);
                    }

                    appliedFiles.Add(document.FilePath);
                }
            }

            await _workspace.ReloadAsync(workspaceId, ct).ConfigureAwait(false);
            return (true, appliedFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Failed to persist document set changes for workspace {WorkspaceId}", workspaceId);
            return (false, []);
        }
    }

    private static async Task PersistProjectReferenceChangesAsync(
        Solution currentSolution,
        Solution modifiedSolution,
        ProjectChanges projectChange,
        List<string> appliedFiles,
        CancellationToken ct)
    {
        var modifiedProject = modifiedSolution.GetProject(projectChange.ProjectId);
        if (modifiedProject?.FilePath is null || !File.Exists(modifiedProject.FilePath))
        {
            return;
        }

        var addedProjectReferences = projectChange.GetAddedProjectReferences().ToArray();
        var removedProjectReferences = projectChange.GetRemovedProjectReferences().ToArray();
        if (addedProjectReferences.Length == 0 && removedProjectReferences.Length == 0)
        {
            return;
        }

        var originalContent = await File.ReadAllTextAsync(modifiedProject.FilePath, ct).ConfigureAwait(false);
        var document = XDocument.Parse(originalContent, LoadOptions.PreserveWhitespace);
        var projectDirectory = Path.GetDirectoryName(modifiedProject.FilePath)
            ?? throw new InvalidOperationException("Project file path must have a parent directory.");
        var changed = false;

        foreach (var projectReference in addedProjectReferences)
        {
            var referencedProject = modifiedSolution.GetProject(projectReference.ProjectId);
            if (referencedProject?.FilePath is null)
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(projectDirectory, referencedProject.FilePath);
            if (document.Descendants("ProjectReference").Any(element =>
                    string.Equals(NormalizeInclude((string?)element.Attribute("Include")), NormalizeInclude(relativePath), StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            GetOrCreateItemGroup(document, "ProjectReference")
                .Add(new XElement("ProjectReference", new XAttribute("Include", relativePath)));
            changed = true;
        }

        foreach (var projectReference in removedProjectReferences)
        {
            var referencedProject = currentSolution.GetProject(projectReference.ProjectId) ?? modifiedSolution.GetProject(projectReference.ProjectId);
            var targetFileName = Path.GetFileName(referencedProject?.FilePath);
            if (string.IsNullOrWhiteSpace(targetFileName))
            {
                continue;
            }

            var element = document.Descendants("ProjectReference").FirstOrDefault(candidate =>
            {
                var include = (string?)candidate.Attribute("Include");
                return !string.IsNullOrWhiteSpace(include) &&
                       string.Equals(Path.GetFileName(include), targetFileName, StringComparison.OrdinalIgnoreCase);
            });

            if (element is null)
            {
                continue;
            }

            element.Remove();
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        await File.WriteAllTextAsync(modifiedProject.FilePath, document.ToString(SaveOptions.DisableFormatting), ct).ConfigureAwait(false);
        appliedFiles.Add(modifiedProject.FilePath);
    }

    private static XElement GetOrCreateItemGroup(XDocument document, string itemName)
    {
        var existingGroup = document.Root?.Elements("ItemGroup")
            .FirstOrDefault(group => group.Elements(itemName).Any());
        if (existingGroup is not null)
        {
            return existingGroup;
        }

        var itemGroup = new XElement("ItemGroup");
        document.Root?.Add(itemGroup);
        return itemGroup;
    }

    private static string NormalizeInclude(string? include)
    {
        return (include ?? string.Empty).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
    }

    private static string GetDefaultFixId(string diagnosticId) =>
        diagnosticId switch
        {
            "CS8019" => "remove_unused_using",
            _ => string.Empty
        };
}
