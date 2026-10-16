using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

internal sealed record FixAllTargetRequest(
    FixAllScope Scope,
    string? FilePath,
    string? ProjectName);

internal sealed record FixAllTarget(Document Document, Project Project);

internal static class FixAllTargetResolver
{
    internal static FixAllTargetRequest ParseAndValidate(
        string scope,
        string? filePath,
        string? projectName)
    {
        var parsedScope = scope.ToLowerInvariant() switch
        {
            "document" => FixAllScope.Document,
            "project" => FixAllScope.Project,
            "solution" => FixAllScope.Solution,
            _ => throw new PublicArgumentException(
                "scope must be 'document', 'project', or 'solution'.",
                nameof(scope)),
        };

        if (parsedScope == FixAllScope.Document && string.IsNullOrWhiteSpace(filePath))
        {
            throw new PublicArgumentException(
                "filePath is required when scope is 'document'.",
                nameof(filePath));
        }

        if (parsedScope == FixAllScope.Project && string.IsNullOrWhiteSpace(projectName))
        {
            throw new PublicArgumentException(
                "projectName is required when scope is 'project'. Use workspace_status with verbose=true to list loaded projects.",
                nameof(projectName));
        }

        return new FixAllTargetRequest(parsedScope, filePath, projectName);
    }

    internal static FixAllTarget Resolve(Solution solution, FixAllTargetRequest request)
    {
        if (request.Scope == FixAllScope.Document)
        {
            var document = SymbolResolver.FindDocument(solution, request.FilePath!)
                ?? throw new FileNotFoundException($"Document not found: {request.FilePath}");
            return new FixAllTarget(document, document.Project);
        }

        if (request.Scope == FixAllScope.Project)
        {
            var project = ProjectFilterHelper.FilterProjects(solution, request.ProjectName).FirstOrDefault()
                ?? throw WorkspaceProjectResolver.NotFound(solution.Projects.Select(candidate => candidate.Name));
            var document = project.Documents.FirstOrDefault()
                ?? throw new InvalidOperationException("Project has no documents.");
            return new FixAllTarget(document, project);
        }

        var solutionProject = solution.Projects.FirstOrDefault()
            ?? throw new InvalidOperationException("Solution has no projects.");
        var solutionDocument = solutionProject.Documents.FirstOrDefault()
            ?? throw new InvalidOperationException("Solution has no documents.");
        return new FixAllTarget(solutionDocument, solutionProject);
    }
}
