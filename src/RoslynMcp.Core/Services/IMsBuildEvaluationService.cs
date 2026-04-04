using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Evaluates MSBuild properties and items for projects in a loaded workspace using Microsoft.Build.Evaluation.
/// </summary>
public interface IMsBuildEvaluationService
{
    Task<MsBuildPropertyEvaluationDto> EvaluatePropertyAsync(
        string workspaceId, string projectName, string propertyName, CancellationToken ct);

    Task<MsBuildItemEvaluationDto> EvaluateItemsAsync(
        string workspaceId, string projectName, string itemType, CancellationToken ct);

    Task<MsBuildPropertiesDumpDto> GetEvaluatedPropertiesAsync(
        string workspaceId, string projectName, CancellationToken ct);
}
