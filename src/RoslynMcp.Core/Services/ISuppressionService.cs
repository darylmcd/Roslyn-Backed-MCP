using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Programmatic diagnostic suppression and severity overrides.
/// </summary>
public interface ISuppressionService
{
    /// <summary>
    /// Sets <c>dotnet_diagnostic.&lt;id&gt;.severity</c> in .editorconfig for C# files (same scope as <see cref="IEditorConfigService.SetOptionAsync"/>).
    /// </summary>
    Task<EditorConfigWriteResultDto> SetDiagnosticSeverityAsync(
        string workspaceId, string diagnosticId, string severity, string sourceFilePath, CancellationToken ct);

    /// <summary>
    /// Inserts <c>#pragma warning disable &lt;id&gt;</c> immediately before the given 1-based line.
    /// </summary>
    Task<TextEditResultDto> AddPragmaWarningDisableAsync(
        string workspaceId, string filePath, int line, string diagnosticId, CancellationToken ct);
}
