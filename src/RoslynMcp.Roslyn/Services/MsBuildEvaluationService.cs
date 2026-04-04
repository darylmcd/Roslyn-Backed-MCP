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
        string workspaceId, string projectName, CancellationToken ct)
    {
        MsBuildInitializer.EnsureInitialized();
        var roslynProj = ResolveRoslynProject(workspaceId, projectName);
        using var collection = new ProjectCollection();
        try
        {
            var msbuildProj = collection.LoadProject(roslynProj.FilePath!);
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in msbuildProj.Properties)
            {
                ct.ThrowIfCancellationRequested();
                dict[prop.Name] = prop.EvaluatedValue;
            }

            return Task.FromResult(new MsBuildPropertiesDumpDto(
                roslynProj.Name,
                roslynProj.FilePath!,
                dict));
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
