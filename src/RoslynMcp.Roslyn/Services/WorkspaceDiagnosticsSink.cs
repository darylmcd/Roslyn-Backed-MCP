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
    private readonly object _sync = new();
    private (WorkspaceDiagnosticsSink Sink, string WorkspaceId, ILogger Logger)? _forward;

    public WorkspaceDiagnosticsSink(int maxDiagnostics)
    {
        _maxDiagnostics = maxDiagnostics;
    }

    public ConcurrentQueue<DiagnosticDto> Queue => _queue;

    internal WorkspaceDiagnosticsSink CreateCandidate() => new(_maxDiagnostics);

    internal void ForwardTo(WorkspaceDiagnosticsSink target, string workspaceId, ILogger logger)
    {
        lock (_sync)
        {
            if (_forward is not null || ReferenceEquals(this, target))
                throw new InvalidOperationException("Candidate diagnostics already have an owner.");
            while (_queue.TryDequeue(out var diagnostic)) target.Report(diagnostic, workspaceId, logger);
            _forward = (target, workspaceId, logger);
        }
    }

    private void Report(DiagnosticDto diagnostic, string workspaceId, ILogger logger)
    {
        lock (_sync)
        {
            if (_forward is { } forward)
            {
                forward.Sink.Report(diagnostic, forward.WorkspaceId, forward.Logger);
                return;
            }
            _queue.Enqueue(diagnostic);
            while (_queue.Count > _maxDiagnostics) _queue.TryDequeue(out _);
            logger.LogWarning("Workspace {WorkspaceId} diagnostic: {Message}", workspaceId, diagnostic.Message);
        }
    }

    public void AttachTo(MSBuildWorkspace workspace, string workspaceId, ILogger logger)
    {
        var redactor = new MsBuildCommandProperties.Redactor(MsBuildCommandProperties.Append([], workspace.Properties));
        workspace.RegisterWorkspaceFailedHandler(args =>
        {
            var severity = WorkspaceDiagnosticSeverityClassifier.Classify(
                args.Diagnostic.Kind, args.Diagnostic.Message);
            var message = redactor.Text(args.Diagnostic.Message);
            var dto = new DiagnosticDto(
                Id: $"WORKSPACE_{args.Diagnostic.Kind}".ToUpperInvariant(),
                Message: message,
                Severity: severity,
                Category: "Workspace",
                FilePath: null,
                StartLine: null,
                StartColumn: null,
                EndLine: null,
                EndColumn: null,
                Location: null);
            Report(dto, workspaceId, logger);
        });
    }
}
