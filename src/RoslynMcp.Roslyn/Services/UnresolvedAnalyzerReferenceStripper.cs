using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Helpers;
using System.Collections.Concurrent;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Removes <see cref="UnresolvedAnalyzerReference"/> entries from every project in a loaded
/// workspace. These entries are produced when an analyzer file referenced by the project cannot
/// be located (typical for analyzer projects targeting netstandard2.0 whose build output is not
/// yet on disk). Leaving them in place causes Roslyn-internal switches over AnalyzerReference
/// subtypes to throw <c>InvalidOperationException("Unexpected value 'UnresolvedAnalyzerReference'")</c>
/// from SymbolFinder, Compilation.GetDiagnostics, and similar APIs. Each strip emits a
/// <c>WORKSPACE_UNRESOLVED_ANALYZER</c> warning (severity Warning, not Error) so callers can
/// still discover that something was filtered. Extracted from
/// <see cref="WorkspaceManager.LoadIntoSessionAsync"/> — workspace-manager-decompose-restore-and-
/// analyzer-subsystems — as a zero-external-caller, purely code-moved collaborator.
/// </summary>
/// <remarks>
/// Not sealed so a future test double can derive and override the virtual surface, mirroring
/// <see cref="WorkspaceSessionLoader"/>'s pattern.
/// </remarks>
internal class UnresolvedAnalyzerReferenceStripper
{
    public virtual async Task StripAsync(
        string workspaceId,
        MSBuildWorkspace workspace,
        ConcurrentQueue<DiagnosticDto> diagnosticsQueue,
        int maxDiagnosticsPerWorkspace,
        ILogger logger,
        CancellationToken ct)
    {
        var originalSolution = workspace.CurrentSolution;
        var solution = originalSolution;
        var strippedCount = 0;
        var newDiagnostics = new List<DiagnosticDto>();

        foreach (var project in originalSolution.Projects)
        {
            var unresolved = project.AnalyzerReferences
                .OfType<UnresolvedAnalyzerReference>()
                .ToList();

            if (unresolved.Count == 0) continue;

            foreach (var reference in unresolved)
            {
                solution = solution.RemoveAnalyzerReference(project.Id, reference);
                strippedCount++;

                var displayName = reference.Display ?? reference.FullPath ?? "<unknown>";
                newDiagnostics.Add(new DiagnosticDto(
                    Id: "WORKSPACE_UNRESOLVED_ANALYZER",
                    Message: $"Unresolved analyzer reference removed from project '{project.Name}': {displayName}. " +
                             "This typically indicates a missing analyzer build output (netstandard2.0 analyzer project) " +
                             "or an unresolved package path. Run `dotnet build` on the analyzer project, then `workspace_reload`.",
                    Severity: WorkspaceDiagnosticSeverityClassifier.Classify(WorkspaceDiagnosticKind.Warning, ""),
                    Category: "Workspace",
                    FilePath: project.FilePath,
                    StartLine: null,
                    StartColumn: null,
                    EndLine: null,
                    EndColumn: null));
            }
        }

        if (strippedCount == 0) return;

        // csproj-reserialization-msbuildworkspace (P2) — create-file-apply-csproj-side-effect-all-projects.
        //
        // MSBuildWorkspace.TryApplyChanges reserializes csprojs that had an analyzer-reference
        // removed: MSBuild reprojects the project file to emit the updated AnalyzerReference
        // itemgroup, and in doing so adds a UTF-8 BOM, flips LF→CRLF, collapses blank lines, and
        // strips the trailing newline. The semantics are identical but the on-disk bytes drift,
        // which shows up as noise in git diff — exactly the pattern every recent subagent on the
        // 2026-04-16 backlog-sweep observed on samples/GeneratedDocumentSolution/ConsumerLib/
        // ConsumerLib.csproj during verify-release.ps1 runs (that csproj has an
        // `OutputItemType="Analyzer"` project reference to ConsumerLib.Generators, which registers
        // as UnresolvedAnalyzerReference on first load before the generator output is built).
        //
        // Snapshot ALL csproj bytes before TryApplyChanges; restore any whose post-apply XML is
        // semantically equivalent to the snapshot (trivia-only diff). Csprojs with a legitimate
        // semantic change (should not occur here since we only strip analyzer refs, but defensive)
        // keep their new bytes.
        var csprojSnapshots = await CsprojSemanticEquality.SnapshotProjectsAsync(
            originalSolution.Projects
                .Select(project => project.FilePath)
                .Where(path => !string.IsNullOrWhiteSpace(path))!,
            logger,
            ct).ConfigureAwait(false);

        if (!workspace.TryApplyChanges(solution))
        {
            // Should be impossible — analyzer reference removal is supported by every workspace
            // implementation. Log and leave the unresolved entries in place; the per-service
            // guards in CompilationCache/FixAllService were removed as part of this change so
            // surface the failure loudly.
            logger.LogWarning(
                "Workspace {WorkspaceId}: TryApplyChanges failed when stripping {Count} UnresolvedAnalyzerReference entries; downstream tools may still crash on them.",
                workspaceId, strippedCount);
            return;
        }

        // Restore trivia-only drift introduced by TryApplyChanges's csproj reserialization.
        await CsprojSemanticEquality.RestoreTriviaOnlyDriftAsync(
            csprojSnapshots,
            skipPaths: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            logger,
            operationTag: "csproj-reserialization-msbuildworkspace/strip-unresolved-analyzer",
            ct).ConfigureAwait(false);

        foreach (var dto in newDiagnostics)
        {
            diagnosticsQueue.Enqueue(dto);
            while (diagnosticsQueue.Count > maxDiagnosticsPerWorkspace)
            {
                diagnosticsQueue.TryDequeue(out _);
            }
        }

        logger.LogInformation(
            "Workspace {WorkspaceId}: stripped {Count} UnresolvedAnalyzerReference entries to prevent downstream crashes.",
            workspaceId, strippedCount);
    }
}
