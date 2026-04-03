using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

public sealed class FileOperationService : IFileOperationService
{
    private readonly IWorkspaceManager _workspace;
    private readonly IPreviewStore _previewStore;
    private readonly ILogger<FileOperationService> _logger;

    public FileOperationService(
        IWorkspaceManager workspace,
        IPreviewStore previewStore,
        ILogger<FileOperationService> logger)
    {
        _workspace = workspace;
        _previewStore = previewStore;
        _logger = logger;
    }

    public async Task<RefactoringPreviewDto> PreviewCreateFileAsync(string workspaceId, CreateFileDto request, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var project = ResolveProject(solution, request.ProjectName);
        var fullPath = Path.GetFullPath(request.FilePath);
        ValidateFilePath(project.FilePath, fullPath);

        if (SymbolResolver.FindDocument(solution, fullPath) is not null || File.Exists(fullPath))
        {
            throw new InvalidOperationException($"A file already exists at '{fullPath}'.");
        }

        var folders = GetFolders(project.FilePath, fullPath);
        var document = project.AddDocument(Path.GetFileName(fullPath), SourceText.From(request.Content), folders, fullPath);
        var newSolution = document.Project.Solution;

        var changes = await SolutionDiffHelper.ComputeChangesAsync(solution, newSolution, ct).ConfigureAwait(false);
        var description = $"Create file '{Path.GetFileName(fullPath)}' in project '{project.Name}'";
        var token = _previewStore.Store(workspaceId, newSolution, _workspace.GetCurrentVersion(workspaceId), description);

        _logger.LogInformation("Prepared create-file preview for {FilePath} in workspace {WorkspaceId}", fullPath, workspaceId);
        return new RefactoringPreviewDto(token, description, changes, null);
    }

    public async Task<RefactoringPreviewDto> PreviewDeleteFileAsync(string workspaceId, DeleteFileDto request, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var fullPath = Path.GetFullPath(request.FilePath);
        var document = SymbolResolver.FindDocument(solution, fullPath)
            ?? throw new InvalidOperationException($"Document not found: {request.FilePath}");

        var newSolution = solution.RemoveDocument(document.Id);
        var changes = await SolutionDiffHelper.ComputeChangesAsync(solution, newSolution, ct).ConfigureAwait(false);
        var description = $"Delete file '{Path.GetFileName(fullPath)}'";
        var token = _previewStore.Store(workspaceId, newSolution, _workspace.GetCurrentVersion(workspaceId), description);

        _logger.LogInformation("Prepared delete-file preview for {FilePath} in workspace {WorkspaceId}", fullPath, workspaceId);
        return new RefactoringPreviewDto(token, description, changes, null);
    }

    public async Task<RefactoringPreviewDto> PreviewMoveFileAsync(string workspaceId, MoveFileDto request, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var sourcePath = Path.GetFullPath(request.SourceFilePath);
        var destinationPath = Path.GetFullPath(request.DestinationFilePath);
        var sourceDocument = SymbolResolver.FindDocument(solution, sourcePath)
            ?? throw new InvalidOperationException($"Document not found: {request.SourceFilePath}");

        if (string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Source and destination paths must be different.");
        }

        if (SymbolResolver.FindDocument(solution, destinationPath) is not null || File.Exists(destinationPath))
        {
            throw new InvalidOperationException($"A file already exists at '{destinationPath}'.");
        }

        var destinationProject = ResolveDestinationProject(solution, sourceDocument.Project, request.DestinationProjectName);
        ValidateFilePath(destinationProject.FilePath, destinationPath);

        var sourceText = await sourceDocument.GetTextAsync(ct).ConfigureAwait(false);
        var updatedText = sourceText;
        var warnings = new List<string>();
        if (request.UpdateNamespace)
        {
            var namespaceResult = await TryUpdateNamespaceAsync(sourceDocument, destinationProject, destinationPath, ct).ConfigureAwait(false);
            if (namespaceResult.Text is not null)
            {
                updatedText = namespaceResult.Text;
            }

            if (!string.IsNullOrWhiteSpace(namespaceResult.Warning))
            {
                warnings.Add(namespaceResult.Warning);
            }
        }

        var folders = GetFolders(destinationProject.FilePath, destinationPath);
        var createdDocument = destinationProject.AddDocument(Path.GetFileName(destinationPath), updatedText, folders, destinationPath);
        var newSolution = createdDocument.Project.Solution.RemoveDocument(sourceDocument.Id);

        var changes = await SolutionDiffHelper.ComputeChangesAsync(solution, newSolution, ct).ConfigureAwait(false);
        var description = $"Move file '{Path.GetFileName(sourcePath)}' to '{destinationPath}'";
        var token = _previewStore.Store(workspaceId, newSolution, _workspace.GetCurrentVersion(workspaceId), description);

        _logger.LogInformation("Prepared move-file preview from {SourcePath} to {DestinationPath} in workspace {WorkspaceId}", sourcePath, destinationPath, workspaceId);
        return new RefactoringPreviewDto(token, description, changes, warnings.Count > 0 ? warnings : null);
    }

    private static Microsoft.CodeAnalysis.Project ResolveProject(Microsoft.CodeAnalysis.Solution solution, string projectName)
    {
        return solution.Projects.FirstOrDefault(project =>
                   string.Equals(project.Name, projectName, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(project.FilePath, projectName, StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidOperationException($"Project not found: {projectName}");
    }

    private static Microsoft.CodeAnalysis.Project ResolveDestinationProject(
        Solution solution,
        Microsoft.CodeAnalysis.Project sourceProject,
        string? destinationProjectName)
    {
        if (string.IsNullOrWhiteSpace(destinationProjectName))
        {
            return sourceProject;
        }

        return ResolveProject(solution, destinationProjectName);
    }

    private static void ValidateFilePath(string? projectFilePath, string filePath)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath))
        {
            throw new InvalidOperationException("The target project does not have a file path on disk.");
        }

        var projectDirectory = Path.GetDirectoryName(projectFilePath)
            ?? throw new InvalidOperationException($"Project directory could not be resolved for '{projectFilePath}'.");
        if (!filePath.StartsWith(projectDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Path '{filePath}' must be under the target project directory '{projectDirectory}'.");
        }
    }

    private static IReadOnlyList<string> GetFolders(string? projectFilePath, string filePath)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath))
        {
            return [];
        }

        var projectDirectory = Path.GetDirectoryName(projectFilePath);
        var fileDirectory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(projectDirectory) || string.IsNullOrWhiteSpace(fileDirectory))
        {
            return [];
        }

        var relativeDirectory = Path.GetRelativePath(projectDirectory, fileDirectory);
        if (string.Equals(relativeDirectory, ".", StringComparison.Ordinal))
        {
            return [];
        }

        return relativeDirectory
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Where(folder => !string.IsNullOrWhiteSpace(folder) && folder != ".")
            .ToArray();
    }

    private static async Task<(SourceText? Text, string? Warning)> TryUpdateNamespaceAsync(
        Document document,
        Microsoft.CodeAnalysis.Project destinationProject,
        string destinationPath,
        CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is not CompilationUnitSyntax compilationUnit)
        {
            return (null, "The document syntax tree could not be loaded, so the namespace was not updated.");
        }

        var targetNamespace = ComputeTargetNamespace(destinationProject, destinationPath);
        if (string.IsNullOrWhiteSpace(targetNamespace))
        {
            return (null, "The destination namespace could not be inferred, so the namespace was not updated.");
        }

        if (compilationUnit.Members.FirstOrDefault() is FileScopedNamespaceDeclarationSyntax fileScopedNamespace)
        {
            var updatedRoot = compilationUnit.ReplaceNode(
                fileScopedNamespace,
                fileScopedNamespace.WithName(SyntaxFactory.ParseName(targetNamespace)));
            return (updatedRoot.GetText(), "Namespace references outside the moved file are not automatically rewritten.");
        }

        if (compilationUnit.Members.FirstOrDefault() is NamespaceDeclarationSyntax namespaceDeclaration)
        {
            var updatedRoot = compilationUnit.ReplaceNode(
                namespaceDeclaration,
                namespaceDeclaration.WithName(SyntaxFactory.ParseName(targetNamespace)));
            return (updatedRoot.GetText(), "Namespace references outside the moved file are not automatically rewritten.");
        }

        return (null, "The file does not declare a namespace, so only the file path will change.");
    }

    private static string ComputeTargetNamespace(Microsoft.CodeAnalysis.Project project, string destinationPath)
    {
        var baseNamespace = !string.IsNullOrWhiteSpace(project.DefaultNamespace)
            ? project.DefaultNamespace
            : project.Name;

        if (string.IsNullOrWhiteSpace(project.FilePath))
        {
            return baseNamespace;
        }

        var folders = GetFolders(project.FilePath, destinationPath);
        if (folders.Count == 0)
        {
            return baseNamespace;
        }

        var suffix = string.Join('.', folders.Select(folder => folder.Replace(' ', '_')));
        return $"{baseNamespace}.{suffix}";
    }
}
