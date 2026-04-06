using Microsoft.Build.Evaluation;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using RoslynProject = Microsoft.CodeAnalysis.Project;

namespace RoslynMcp.Roslyn.Services;

public sealed class MsBuildEvaluationService : IMsBuildEvaluationService
{
    private readonly IWorkspaceManager _workspace;
    public MsBuildEvaluationService(IWorkspaceManager workspace)
    {
        _workspace = workspace;
    }

    public Task<MsBuildPropertyEvaluationDto> EvaluatePropertyAsync(
        string workspaceId, string projectName, string propertyName, CancellationToken ct)
    {
        MsBuildInitializer.EnsureInitialized();
        var roslynProj = ResolveRoslynProject(workspaceId, projectName);
        using var collection = new ProjectCollection();
        try
        {
            ct.ThrowIfCancellationRequested();
            var msbuildProj = collection.LoadProject(roslynProj.FilePath!);
            var value = msbuildProj.GetPropertyValue(propertyName);
            return Task.FromResult(new MsBuildPropertyEvaluationDto(
                roslynProj.Name,
                roslynProj.FilePath!,
                propertyName,
                string.IsNullOrEmpty(value) ? null : value));
        }
        finally
        {
            collection.UnloadAllProjects();
        }
    }

    public Task<MsBuildItemEvaluationDto> EvaluateItemsAsync(
        string workspaceId, string projectName, string itemType, CancellationToken ct)
    {
        MsBuildInitializer.EnsureInitialized();
        var roslynProj = ResolveRoslynProject(workspaceId, projectName);
        using var collection = new ProjectCollection();
        try
        {
            var msbuildProj = collection.LoadProject(roslynProj.FilePath!);
            var items = new List<MsBuildItemInstanceDto>();
            foreach (var item in msbuildProj.GetItems(itemType))
            {
                ct.ThrowIfCancellationRequested();
                var meta = item.Metadata.ToDictionary(m => m.Name, m => m.EvaluatedValue, StringComparer.OrdinalIgnoreCase);
                items.Add(new MsBuildItemInstanceDto(item.EvaluatedInclude, meta));
            }

            return Task.FromResult(new MsBuildItemEvaluationDto(
                roslynProj.Name,
                roslynProj.FilePath!,
                itemType,
                items));
        }
        finally
        {
            collection.UnloadAllProjects();
        }
    }

    public Task<MsBuildPropertiesDumpDto> GetEvaluatedPropertiesAsync(
        string workspaceId,
        string projectName,
        string? propertyNameFilter,
        IReadOnlyCollection<string>? includedNames,
        CancellationToken ct)
    {
        MsBuildInitializer.EnsureInitialized();
        var roslynProj = ResolveRoslynProject(workspaceId, projectName);
        using var collection = new ProjectCollection();
        try
        {
            var msbuildProj = collection.LoadProject(roslynProj.FilePath!);

            // BUG-008: Filter at evaluation time so we never serialize 60KB+ of internal MSBuild
            // properties when the caller only needs a handful. The explicit allowlist takes
            // precedence; falling back to a substring filter mirrors get_diagnostics behavior.
            HashSet<string>? allowlist = null;
            if (includedNames is not null && includedNames.Count > 0)
            {
                allowlist = new HashSet<string>(includedNames, StringComparer.OrdinalIgnoreCase);
            }

            var hasSubstringFilter = !string.IsNullOrWhiteSpace(propertyNameFilter);
            var totalCount = 0;
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in msbuildProj.Properties)
            {
                ct.ThrowIfCancellationRequested();
                totalCount++;

                if (allowlist is not null)
                {
                    if (!allowlist.Contains(prop.Name)) continue;
                }
                else if (hasSubstringFilter &&
                         !prop.Name.Contains(propertyNameFilter!, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                dict[prop.Name] = prop.EvaluatedValue;
            }

            var appliedFilter = allowlist is not null
                ? $"includedNames=[{string.Join(",", allowlist.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))}]"
                : hasSubstringFilter
                    ? $"propertyNameFilter='{propertyNameFilter}'"
                    : null;

            return Task.FromResult(new MsBuildPropertiesDumpDto(
                roslynProj.Name,
                roslynProj.FilePath!,
                dict,
                TotalCount: totalCount,
                ReturnedCount: dict.Count,
                AppliedFilter: appliedFilter));
        }
        finally
        {
            collection.UnloadAllProjects();
        }
    }

    private RoslynProject ResolveRoslynProject(string workspaceId, string projectName)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var proj = ProjectFilterHelper.FilterProjects(solution, projectName).FirstOrDefault();
        if (proj is null)
        {
            throw new InvalidOperationException($"Project '{projectName}' not found in workspace.");
        }

        if (string.IsNullOrEmpty(proj.FilePath))
        {
            throw new InvalidOperationException($"Project '{projectName}' has no file path.");
        }

        return proj;
    }
}
