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

    /// <summary>
    /// Returns the project's evaluated MSBuild properties, optionally restricted by a name filter.
    /// </summary>
    /// <param name="workspaceId">Workspace session identifier.</param>
    /// <param name="projectName">Project name within the workspace.</param>
    /// <param name="propertyNameFilter">
    /// Optional case-insensitive substring filter applied to property names. When <see langword="null"/>
    /// or whitespace, every evaluated property is returned (BUG-008: large payloads — prefer filtering).
    /// </param>
    /// <param name="includedNames">
    /// Optional explicit allowlist of property names to return. Takes precedence over
    /// <paramref name="propertyNameFilter"/> when supplied.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<MsBuildPropertiesDumpDto> GetEvaluatedPropertiesAsync(
        string workspaceId,
        string projectName,
        string? propertyNameFilter,
        IReadOnlyCollection<string>? includedNames,
        CancellationToken ct);
}
