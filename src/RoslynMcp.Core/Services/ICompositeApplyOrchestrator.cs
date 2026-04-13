using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Applies a composite orchestration preview to disk.
/// </summary>
public interface ICompositeApplyOrchestrator
{
    Task<ApplyResultDto> ApplyCompositeAsync(string previewToken, CancellationToken ct);
}
