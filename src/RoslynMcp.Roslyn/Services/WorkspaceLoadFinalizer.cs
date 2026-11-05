using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// The read-side state a freshly opened workspace is published with: per-project status, the
/// physical document-path index, and the restore/build readiness flags.
/// </summary>
internal sealed record WorkspaceLoadProjection(
    ImmutableArray<ProjectStatusDto> ProjectStatuses,
    ImmutableHashSet<string> DocumentPaths,
    bool RestoreRequired,
    bool BuildRequired);

/// <summary>
/// Post-open finalization of a loaded workspace (workspace-manager-load-finalization-collaborator-
/// extraction): refuses link-bearing project/document paths, then projects the opened solution
/// into the status, document-path-index and readiness state the manager publishes on the session.
/// Extracted from <see cref="WorkspaceManager"/>'s <c>LoadIntoSessionAsync</c>.
/// <para>
/// Pure with respect to the session: it reads the workspace and diagnostics it is handed and
/// returns a value, never touching <c>WorkspaceSession</c>. The manager keeps the load lock, the
/// cache write and the atomic old/new swap, so a throw here leaves the prior session untouched
/// and the manager disposes the new workspace on its failure path.
/// </para>
/// </summary>
/// <remarks>
/// Not sealed so a failure-path test double can derive and override the virtual surface,
/// mirroring <see cref="WorkspaceSessionLoader"/> and <see cref="RestoreStalenessDetector"/>.
/// </remarks>
internal class WorkspaceLoadFinalizer(RestoreStalenessDetector restoreStalenessDetector)
{
    /// <summary>
    /// Validates the opened workspace's paths are physical, then builds its published projection.
    /// Validation runs first so a link-bearing workspace is refused before any projection work.
    /// </summary>
    /// <exception cref="InvalidOperationException">A project or in-project document path has a filesystem-link component.</exception>
    public virtual WorkspaceLoadProjection BuildLoadProjection(
        MSBuildWorkspace workspace,
        ConcurrentQueue<DiagnosticDto> diagnostics,
        ILogger logger)
    {
        var solution = workspace.CurrentSolution;
        EnsureWorkspacePathsArePhysical(solution);

        var projectStatuses = BuildProjectStatuses(solution, logger);
        var documentPaths = BuildDocumentPathIndex(solution);
        var restoreRequired = restoreStalenessDetector.DetectRestoreRequired(projectStatuses, workspace.Properties, logger) ||
                              RestoreStalenessDetector.HasRestoreRequiredWorkspaceDiagnostics(diagnostics);
        // restore-required-vs-build-conflation: an unresolved-analyzer warning is a missing
        // BUILD output, distinct from a missing NuGet package. Track it separately so the
        // summary hint, readiness verdict, and autoRestore gate route to `dotnet build`
        // rather than a no-op `dotnet restore`.
        var buildRequired = WorkspaceDiagnosticClassifier.HasBuildRequired(diagnostics);

        return new WorkspaceLoadProjection(projectStatuses, documentPaths, restoreRequired, buildRequired);
    }

    /// <summary>
    /// Physical document-path index shared by load finalization and
    /// <see cref="WorkspaceManager.TryApplyChanges"/>, so both publish the same identity.
    /// </summary>
    internal static ImmutableHashSet<string> BuildDocumentPathIndex(Solution solution) =>
        solution.Projects
            .SelectMany(project => project.Documents)
            .Select(document => document.FilePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => PhysicalPathResolver.Resolve(path!))
            .ToImmutableHashSet(FileSystemPath.Comparer);

    private static void EnsureWorkspacePathsArePhysical(Solution solution)
    {
        foreach (var project in solution.Projects)
        {
            WorkspaceManager.EnsurePhysicalPath(project.FilePath);
            var projectDirectory = Path.GetDirectoryName(project.FilePath);
            foreach (var document in project.Documents
                         .Concat<TextDocument>(project.AdditionalDocuments)
                         .Concat(project.AnalyzerConfigDocuments))
            {
                if (string.IsNullOrWhiteSpace(document.FilePath)) continue;

                var physicalPath = PhysicalPathResolver.Resolve(document.FilePath);
                // MSBuild imports some source documents from outside the project (for example,
                // SDK files in the NuGet cache). Those paths are read-only in this workspace:
                // TryApplyChanges rejects edits to any document whose path is not physical.
                if (IsPathUnderDirectory(physicalPath, projectDirectory))
                {
                    WorkspaceManager.EnsurePhysicalPath(document.FilePath);
                }
            }
        }
    }

    private static bool IsPathUnderDirectory(string path, string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return false;
        var prefix = directory.EndsWith(Path.DirectorySeparatorChar)
            ? directory
            : directory + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, FileSystemPath.Comparison);
    }

    private static ImmutableArray<ProjectStatusDto> BuildProjectStatuses(Solution solution, ILogger logger)
    {
        return solution.Projects.Select(project =>
        {
            var projectDoc = ProjectMetadataParser.LoadProjectDocument(project.FilePath, logger);
            return new ProjectStatusDto(
                // project-graph-missing-metadata-fields: never emit an empty Name / FilePath
                // into the status DTO. ResolveProjectName falls back through AssemblyName →
                // filename stem → "unknown"; ResolveProjectPath falls back to "unknown".
                Name: ProjectMetadataParser.ResolveProjectName(project),
                FilePath: ProjectMetadataParser.ResolveProjectPath(project),
                DocumentCount: project.Documents.Count(),
                ProjectReferences: project.ProjectReferences
                    .Select(reference =>
                    {
                        var referenced = solution.GetProject(reference.ProjectId);
                        return referenced is null
                            ? "unknown"
                            : ProjectMetadataParser.ResolveProjectName(referenced);
                    })
                    .ToList(),
                TargetFrameworks: ProjectMetadataParser.GetTargetFrameworks(project, projectDoc, logger),
                IsTestProject: ProjectMetadataParser.IsTestProject(projectDoc),
                AssemblyName: ProjectMetadataParser.GetAssemblyName(project),
                OutputType: ProjectMetadataParser.GetOutputType(project, projectDoc, logger));
        }).ToImmutableArray();
    }
}
