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
        var fullPath = ResolveFilePath(request.FilePath, "filePath");
        ValidateFilePath(project.FilePath, fullPath, "filePath");

        if (SymbolResolver.FindDocument(solution, fullPath) is not null || File.Exists(fullPath))
        {
            throw new PublicInvalidOperationException("A file already exists at the requested destination. Choose a new filePath or edit the existing file.");
        }

        // FLAG-10B: Some MCP clients pass literal "\\n" / "\\r" / "\\t" sequences in the content
        // parameter (JSON-escape passed as-is rather than decoded to actual control characters).
        // The server transparently decodes these standard escape sequences so create_file_preview
        // always emits the multi-line file the caller intended. If the content already contains
        // real newlines, decoding is skipped to preserve any intentional literal backslash-n.
        var normalizedContent = NormalizeContentEscapes(request.Content);

        var folders = GetFolders(project.FilePath, fullPath);
        var document = project.AddDocument(Path.GetFileName(fullPath), SourceText.From(normalizedContent), folders, fullPath);
        var newSolution = document.Project.Solution;

        var changes = await SolutionDiffHelper.ComputeChangesAsync(solution, newSolution, ct).ConfigureAwait(false);
        var description = $"Create file '{Path.GetFileName(fullPath)}' in project '{project.Name}'";
        var token = _previewStore.Store(
            workspaceId,
            newSolution,
            _workspace.GetCurrentVersion(workspaceId),
            description,
            changes,
            // preview-token-apply-route-provenance: record this preview's producer family so
            // the paired *_apply route can refuse a token minted by a different family.
            kind: PreviewKind.FileCreate);

        _logger.LogInformation("Prepared create-file preview for {FilePath} in workspace {WorkspaceId}", fullPath, workspaceId);
        return new RefactoringPreviewDto(token, description, changes, null);
    }

    /// <summary>
    /// FLAG-10B: Decode standard escape sequences (<c>\n</c>, <c>\r</c>, <c>\t</c>, <c>\\</c>) when
    /// the content has none of the actual control characters they represent. This handles MCP
    /// clients that pass JSON-quoted strings containing literal backslash-n sequences (the JSON
    /// parser may have already passed them through unchanged) without losing intentional literal
    /// backslash-n in legitimately decoded multi-line content.
    /// </summary>
    internal static string NormalizeContentEscapes(string content)
    {
        if (string.IsNullOrEmpty(content)) return content;
        // If the content already contains real newlines, trust the caller and skip decoding.
        if (content.Contains('\n') || content.Contains('\r')) return content;
        // If there are no backslash-escape sequences either, nothing to do.
        if (!content.Contains('\\')) return content;

        var sb = new System.Text.StringBuilder(content.Length);
        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];
            if (c == '\\' && i + 1 < content.Length)
            {
                var next = content[i + 1];
                switch (next)
                {
                    case 'n': sb.Append('\n'); i++; continue;
                    case 'r': sb.Append('\r'); i++; continue;
                    case 't': sb.Append('\t'); i++; continue;
                    case '\\': sb.Append('\\'); i++; continue;
                    case '"': sb.Append('"'); i++; continue;
                    default: sb.Append(c); continue;
                }
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    public async Task<RefactoringPreviewDto> PreviewDeleteFileAsync(string workspaceId, DeleteFileDto request, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var fullPath = ResolveFilePath(request.FilePath, "filePath");
        var document = SymbolResolver.FindDocument(solution, fullPath)
            ?? throw new PublicInvalidOperationException("The requested file is not in the loaded workspace. Verify filePath names a loaded document, then reload the workspace if needed.");

        var newSolution = solution.RemoveDocument(document.Id);
        var changes = await SolutionDiffHelper.ComputeChangesAsync(solution, newSolution, ct).ConfigureAwait(false);
        var description = $"Delete file '{Path.GetFileName(fullPath)}'";
        var token = _previewStore.Store(
            workspaceId,
            newSolution,
            _workspace.GetCurrentVersion(workspaceId),
            description,
            changes,
            // preview-token-apply-route-provenance: record this preview's producer family so
            // the paired *_apply route can refuse a token minted by a different family.
            kind: PreviewKind.FileDelete);

        _logger.LogInformation("Prepared delete-file preview for {FilePath} in workspace {WorkspaceId}", fullPath, workspaceId);
        return new RefactoringPreviewDto(token, description, changes, null);
    }

    public async Task<RefactoringPreviewDto> PreviewMoveFileAsync(string workspaceId, MoveFileDto request, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var sourcePath = ResolveFilePath(request.SourceFilePath, "sourceFilePath");
        var destinationPath = ResolveFilePath(request.TargetFilePath, "targetFilePath");
        var sourceDocument = SymbolResolver.FindDocument(solution, sourcePath)
            ?? throw new PublicInvalidOperationException("The source file is not in the loaded workspace. Verify sourceFilePath names a loaded document, then reload the workspace if needed.");

        if (string.Equals(sourcePath, destinationPath, FileSystemPath.Comparison))
        {
            throw new PublicArgumentException("Source and destination paths must be different. Choose a different targetFilePath.", "targetFilePath");
        }

        if (SymbolResolver.FindDocument(solution, destinationPath) is not null || File.Exists(destinationPath))
        {
            throw new PublicInvalidOperationException("A file already exists at the requested destination. Choose a new targetFilePath or edit the existing file.");
        }

        var destinationProject = ResolveDestinationProject(solution, sourceDocument.Project, request.DestinationProjectName);
        ValidateFilePath(destinationProject.FilePath, destinationPath, "targetFilePath");

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
        var token = _previewStore.Store(
            workspaceId,
            newSolution,
            _workspace.GetCurrentVersion(workspaceId),
            description,
            changes,
            // preview-token-apply-route-provenance: record this preview's producer family so
            // the paired *_apply route can refuse a token minted by a different family.
            kind: PreviewKind.FileMove);

        _logger.LogInformation("Prepared move-file preview from {SourcePath} to {DestinationPath} in workspace {WorkspaceId}", sourcePath, destinationPath, workspaceId);
        return new RefactoringPreviewDto(token, description, changes, warnings.Count > 0 ? warnings : null);
    }

    private static Microsoft.CodeAnalysis.Project ResolveProject(Microsoft.CodeAnalysis.Solution solution, string projectName)
    {
        return solution.Projects.FirstOrDefault(project =>
                   string.Equals(project.Name, projectName, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(project.FilePath, projectName, StringComparison.OrdinalIgnoreCase))
               ?? throw new PublicInvalidOperationException("The requested project is not loaded. Use workspace_status to list project names, then retry with a loaded project.");
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


    private static string ResolveFilePath(string filePath, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrEmpty(Path.GetFileName(filePath)) ||
            Path.GetFileName(filePath) is "." or "..")
        {
            throw new PublicArgumentException(
                "The file path must include a non-empty file name. Supply a file path without a trailing directory separator.", parameterName);
        }

        if (filePath.Contains('\0'))
        {
            throw new PublicArgumentException(
                "The file path contains an invalid character. Supply a file path without null characters.", parameterName);
        }

        // Validate original segments before GetFullPath trims Windows trailing-dot/space aliases
        // or collapses directory references that could hide an invalid name.
        var rootLength = Path.GetPathRoot(filePath)?.Length ?? 0;
        var segments = filePath[rootLength..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
        if (segments.Any(IsInvalidFileSegment))
        {
            throw new PublicArgumentException(
                "The file path contains an unsupported file or directory name. Use names valid on the operating system, without trailing dots or spaces on Windows.", parameterName);
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(filePath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw ArgumentErrors.Redacted(parameterName, "The supplied file path could not be resolved.", ex);
        }

        return fullPath;
    }

    private static bool IsInvalidFileSegment(string segment)
    {
        if (segment is "." or "..") return false;
        if (segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return true;
        return OperatingSystem.IsWindows() && (segment.EndsWith(' ') || segment.EndsWith('.'));
    }

    private static void ValidateFilePath(string? projectFilePath, string filePath, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath))
        {
            throw new PublicInvalidOperationException("The target project does not have a file path on disk. Load a project saved on disk before creating or moving files.");
        }

        var projectDirectory = Path.GetDirectoryName(projectFilePath)
            ?? throw new PublicInvalidOperationException("The target project directory could not be resolved. Reload a project saved on disk before creating or moving files.");
        if (!FileSystemPath.IsStrictDescendant(projectDirectory, filePath))
        {
            throw new PublicArgumentException(
                "The file path must name a file inside the target project directory. Choose a descendant file path.", parameterName);
        }
    }

    // Item #1 — the folders-resolution helper moved to ProjectMetadataParser.ComputeDocumentFolders
    // so TypeMoveService, TypeExtractionService, InterfaceExtractionService, and
    // CrossProjectRefactoringService can share it (they all previously omitted folders and
    // produced the NetworkDocumentation §9.2 "rogue project-root file" shape on apply).
    private static IReadOnlyList<string> GetFolders(string? projectFilePath, string filePath)
        => ProjectMetadataParser.ComputeDocumentFolders(projectFilePath, filePath);

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
