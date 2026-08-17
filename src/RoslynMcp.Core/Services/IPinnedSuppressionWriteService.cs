using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Applies pragma-suppression writes to a boundary-validated physical target.
/// </summary>
public interface IPinnedSuppressionWriteService
{
    /// <summary>
    /// Inserts a pragma through the workspace document while pinning persistence to
    /// <paramref name="canonicalWritePath"/>.
    /// </summary>
    Task<TextEditResultDto> AddPragmaWarningDisableAsync(
        string workspaceId,
        string filePath,
        int line,
        string diagnosticId,
        string canonicalWritePath,
        CancellationToken ct);

    /// <summary>
    /// Widens a pragma through the workspace document while pinning persistence to
    /// <paramref name="canonicalWritePath"/>.
    /// </summary>
    Task<PragmaWidenResultDto> WidenPragmaScopeAsync(
        string workspaceId,
        string filePath,
        int line,
        string diagnosticId,
        string canonicalWritePath,
        CancellationToken ct);
}
