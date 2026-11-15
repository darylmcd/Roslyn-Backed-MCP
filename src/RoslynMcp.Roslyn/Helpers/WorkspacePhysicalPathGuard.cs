using Microsoft.CodeAnalysis;

namespace RoslynMcp.Roslyn.Helpers;

internal static class WorkspacePhysicalPathGuard
{
    internal static void EnsureChangedDocumentPathsArePhysical(Solution current, Solution next)
    {
        foreach (var project in next.Projects)
        {
            EnsurePhysicalPath(project.FilePath);
        }

        var changes = next.GetChanges(current);
        foreach (var project in changes.GetAddedProjects().Concat(changes.GetRemovedProjects()))
        {
            foreach (var document in project.Documents
                         .Concat<TextDocument>(project.AdditionalDocuments)
                         .Concat(project.AnalyzerConfigDocuments))
            {
                EnsurePhysicalPath(document.FilePath);
            }
        }

        foreach (var change in changes.GetProjectChanges())
        {
            foreach (var id in change.GetChangedDocuments()
                         .Concat(change.GetAddedDocuments())
                         .Concat(change.GetRemovedDocuments()))
            {
                EnsurePhysicalPath(current.GetDocument(id)?.FilePath);
                EnsurePhysicalPath(next.GetDocument(id)?.FilePath);
            }

            foreach (var id in change.GetChangedAdditionalDocuments()
                         .Concat(change.GetAddedAdditionalDocuments())
                         .Concat(change.GetRemovedAdditionalDocuments()))
            {
                EnsurePhysicalPath(current.GetAdditionalDocument(id)?.FilePath);
                EnsurePhysicalPath(next.GetAdditionalDocument(id)?.FilePath);
            }

            foreach (var id in change.GetChangedAnalyzerConfigDocuments()
                         .Concat(change.GetAddedAnalyzerConfigDocuments())
                         .Concat(change.GetRemovedAnalyzerConfigDocuments()))
            {
                EnsurePhysicalPath(current.GetAnalyzerConfigDocument(id)?.FilePath);
                EnsurePhysicalPath(next.GetAnalyzerConfigDocument(id)?.FilePath);
            }
        }
    }

    internal static void EnsurePhysicalPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        var physicalPath = PhysicalPathResolver.Resolve(path);
        if (FileSystemPath.Comparer.Equals(path, physicalPath)) return;

        throw new InvalidOperationException(
            "Workspace project or modified document path has a filesystem-link component. " +
            "Load and edit through the physical path.");
    }
}
