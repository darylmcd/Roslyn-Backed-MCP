using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Stable diagnostics facade. Query orchestration and source lookup live in focused internal
/// collaborators; this type owns the public contract and detail DTO assembly.
/// </summary>
public sealed class DiagnosticService : IDiagnosticService
{
    private readonly IWorkspaceManager _workspace;
    private readonly DiagnosticQueryService _queryService;
    private readonly DiagnosticDocumentLookup _documentLookup;
    private readonly SupportedFixEnumerationService _supportedFixEnumeration;

    public DiagnosticService(
        IWorkspaceManager workspace,
        ICompilationCache compilationCache,
        ICodeFixProviderRegistry codeFixRegistry,
        ILogger<DiagnosticService>? logger = null,
        IUnexpectedExceptionReporter? exceptionReporter = null)
    {
        _workspace = workspace;
        var effectiveLogger = logger ?? NullLogger<DiagnosticService>.Instance;
        _queryService = new DiagnosticQueryService(workspace, compilationCache);
        _documentLookup = new DiagnosticDocumentLookup(compilationCache);
        _supportedFixEnumeration = new SupportedFixEnumerationService(
            codeFixRegistry,
            effectiveLogger,
            exceptionReporter);
        _workspace.WorkspaceClosed += _queryService.InvalidateWorkspaceCaches;
        _workspace.WorkspaceReloaded += _queryService.InvalidateWorkspaceCaches;
    }

    public bool TryGetCachedWorkspaceDiagnostics(
        string workspaceId,
        out DiagnosticsResultDto? diagnostics) =>
        _queryService.TryGetCachedWorkspaceDiagnostics(workspaceId, out diagnostics);

    public Task<DiagnosticsResultDto> GetDiagnosticsAsync(
        string workspaceId,
        string? projectFilter,
        string? fileFilter,
        string? severityFilter,
        string? diagnosticIdFilter,
        CancellationToken ct) =>
        _queryService.GetDiagnosticsAsync(
            workspaceId,
            new DiagnosticQueryFilters(
                projectFilter,
                fileFilter,
                severityFilter,
                diagnosticIdFilter),
            ct);

    public async Task<DiagnosticDetailsDto?> GetDiagnosticDetailsAsync(
        string workspaceId,
        string diagnosticId,
        string filePath,
        int line,
        int column,
        CancellationToken ct)
    {
        var version = _workspace.GetCurrentVersion(workspaceId);
        var solution = _workspace.GetCurrentSolution(workspaceId);
        _queryService.TryGetCachedDiagnostics(
            workspaceId,
            version,
            out var cachedDiagnostics);

        var lookup = await _documentLookup.FindAsync(
            workspaceId,
            solution,
            new DiagnosticLookupTarget(
                diagnosticId,
                filePath,
                line,
                column),
            cachedDiagnostics,
            ct).ConfigureAwait(false);
        if (lookup.FullScanDiagnostics is not null)
        {
            _queryService.CacheDiagnostics(
                workspaceId,
                version,
                lookup.FullScanDiagnostics);
        }

        return lookup.Diagnostic is null
            ? null
            : await BuildDetailsDtoAsync(
                lookup.Diagnostic,
                solution,
                filePath,
                ct).ConfigureAwait(false);
    }

    private async Task<DiagnosticDetailsDto> BuildDetailsDtoAsync(
        Diagnostic diagnostic,
        Solution solution,
        string filePath,
        CancellationToken ct)
    {
        var fixEnumeration = await _supportedFixEnumeration
            .EnumerateAsync(diagnostic, solution, filePath, ct)
            .ConfigureAwait(false);
        return new DiagnosticDetailsDto(
            Diagnostic: SymbolMapper.ToDiagnosticDto(diagnostic),
            Description: BuildDiagnosticDescription(diagnostic),
            HelpLinkUri: BuildHelpLink(diagnostic.Id),
            SupportedFixes: fixEnumeration.Fixes,
            GuidanceMessage: SupportedFixEnumerationService.GetGuidance(
                diagnostic.Id,
                fixEnumeration),
            FixEnumerationComplete: fixEnumeration.IsComplete,
            FailedProviderCount: fixEnumeration.FailedProviderCount);
    }

    private static string BuildDiagnosticDescription(Diagnostic diagnostic)
    {
        var fromDescription = diagnostic.Descriptor.Description.ToString();
        if (!string.IsNullOrWhiteSpace(fromDescription))
        {
            return fromDescription;
        }

        var fromFormat = diagnostic.Descriptor.MessageFormat.ToString();
        return string.IsNullOrWhiteSpace(fromFormat)
            ? diagnostic.GetMessage()
            : fromFormat;
    }

    private static string BuildHelpLink(string diagnosticId) =>
        $"https://learn.microsoft.com/dotnet/csharp/language-reference/compiler-messages/{diagnosticId.ToLowerInvariant()}";
}
