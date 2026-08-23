using System.Collections.Concurrent;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Owns the bounded `ConcurrentQueue&lt;DiagnosticDto&gt;` for a single workspace session and
/// wires the <see cref="MSBuildWorkspace.WorkspaceFailed"/> handler that classifies and enqueues
/// each workspace-failure event. Extracted from <see cref="WorkspaceManager.LoadIntoSessionAsync"/>
/// so the diagnostics-sink concern is independently testable and the manager stays focused on
/// orchestration. Severity normalization at ingress (via
/// <see cref="WorkspaceDiagnosticSeverityClassifier"/>) preserves the contract that
/// <c>workspace_load</c>, <c>workspace_status</c>, <c>project_diagnostics</c>, and the
/// <c>roslyn://workspaces</c> resource all see the same severity.
/// </summary>
internal sealed class WorkspaceDiagnosticsSink
{
    private readonly ConcurrentQueue<DiagnosticDto> _queue = new();
    private readonly int _maxDiagnostics;

    public WorkspaceDiagnosticsSink(int maxDiagnostics)
    {
        _maxDiagnostics = maxDiagnostics;
    }

    public ConcurrentQueue<DiagnosticDto> Queue => _queue;

    public void AttachTo(MSBuildWorkspace workspace, string workspaceId, ILogger logger)
    {
        workspace.RegisterWorkspaceFailedHandler(args =>
        {
            var severity = WorkspaceDiagnosticSeverityClassifier.Classify(
                args.Diagnostic.Kind, args.Diagnostic.Message);
            var dto = new DiagnosticDto(
                Id: $"WORKSPACE_{args.Diagnostic.Kind}".ToUpperInvariant(),
                Message: args.Diagnostic.Message,
                Severity: severity,
                Category: "Workspace",
                FilePath: null,
                StartLine: null,
                StartColumn: null,
                EndLine: null,
                EndColumn: null,
                Location: null);
            _queue.Enqueue(dto);
            while (_queue.Count > _maxDiagnostics)
            {
                _queue.TryDequeue(out _);
            }

            logger.LogWarning(
                "Workspace {WorkspaceId} diagnostic: {Message}",
                workspaceId,
                args.Diagnostic.Message);
        });
    }
}
