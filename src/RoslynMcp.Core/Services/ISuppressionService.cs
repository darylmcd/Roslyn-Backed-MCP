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

    /// <summary>
    /// Checks whether the <c>#pragma warning disable/restore</c> pair for the given diagnostic id
    /// actually covers the specified 1-based line. Used to detect "cosmetic pragma" bugs where the
    /// pragma pair wraps the wrong span — e.g. pair wraps line 68 but the diagnostic actually fires
    /// at line 78 (IT-Chat-Bot RetrievalExecutor CA2025 regression).
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="filePath">Absolute path to the C# source file to inspect.</param>
    /// <param name="line">1-based line number that should be covered by the pragma pair (typically the diagnostic fire site).</param>
    /// <param name="diagnosticId">The diagnostic id whose suppression to check (e.g. <c>CA2025</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<PragmaVerifyResultDto> VerifyPragmaSuppressesAsync(
        string workspaceId, string filePath, int line, string diagnosticId, CancellationToken ct);

    /// <summary>
    /// Extends the matching <c>#pragma warning restore &lt;id&gt;</c> past the given 1-based line,
    /// so the pair covers a previously-uncovered fire site. Refuses the edit when relocating the
    /// restore would cross a <c>#region</c>/<c>#endregion</c> boundary or nest into another
    /// <c>#pragma warning disable &lt;id&gt;</c> for the same id (both would silently change the
    /// effective scope of other suppressions).
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="filePath">Absolute path to the C# source file to modify.</param>
    /// <param name="line">1-based line that must be covered after the widen (typically the diagnostic fire site).</param>
    /// <param name="diagnosticId">The diagnostic id whose <c>restore</c> is being moved.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<PragmaWidenResultDto> WidenPragmaScopeAsync(
        string workspaceId, string filePath, int line, string diagnosticId, CancellationToken ct);
}
