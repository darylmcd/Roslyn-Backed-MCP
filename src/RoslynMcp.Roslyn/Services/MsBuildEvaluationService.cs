using System.Collections.Concurrent;
using Microsoft.Build.Evaluation;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using RoslynProject = Microsoft.CodeAnalysis.Project;
using MsBuildProject = Microsoft.Build.Evaluation.Project;

namespace RoslynMcp.Roslyn.Services;

public sealed class MsBuildEvaluationService : IMsBuildEvaluationService
{
    private readonly IWorkspaceManager _workspace;

    /// <summary>
    /// msbuild-evaluation-uncached-perf: caches the loaded MSBuild <see cref="MsBuildProject"/>
    /// (and its owning <see cref="ProjectCollection"/>) keyed by workspace id → (workspace version,
    /// project file path). Without this, every <c>EvaluatePropertyAsync</c>/<c>EvaluateItemsAsync</c>/
    /// <c>GetEvaluatedPropertiesAsync</c> call re-parsed the full MSBuild import graph (SDK targets,
    /// Directory.Build.props, …) from disk even when the project was unchanged — and
    /// <c>get_nuget_dependencies</c> + <c>get_security_status</c> each loop over every project and
    /// duplicated that work. Mirrors the version-keyed, per-workspace invalidation pattern of
    /// <see cref="NuGetDependencyService"/>'s <c>_vulnCache</c>. Invalidated wholesale on
    /// <see cref="IWorkspaceManager.WorkspaceReloaded"/>/<see cref="IWorkspaceManager.WorkspaceClosed"/>.
    /// </summary>
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<(int Version, string FilePath), Lazy<CachedMsBuildProject>>> _cache
        = new(StringComparer.Ordinal);

    public MsBuildEvaluationService(IWorkspaceManager workspace)
    {
        _workspace = workspace;
        // Drop the cached ProjectCollection(s) for a workspace as soon as it reloads (version bump,
        // typically after an on-disk change / restore) or closes, so a subsequent evaluation re-parses
        // the fresh project XML. Mirrors NuGetDependencyService.cs:57-60.
        _workspace.WorkspaceReloaded += InvalidateWorkspace;
        _workspace.WorkspaceClosed += InvalidateWorkspace;
    }

    public Task<MsBuildPropertyEvaluationDto> EvaluatePropertyAsync(
        string workspaceId, string projectName, string propertyName, CancellationToken ct)
    {
        MsBuildInitializer.EnsureInitialized();
        var roslynProj = ResolveRoslynProject(workspaceId, projectName);
        // Keep the cancellation observation OUTSIDE GetOrLoadProject so a cancel on one caller cannot
        // fault/poison the shared Lazy<CachedMsBuildProject> that other concurrent callers await.
        ct.ThrowIfCancellationRequested();
        var msbuildProj = GetOrLoadProject(workspaceId, roslynProj);
        var value = msbuildProj.GetPropertyValue(propertyName);
        return Task.FromResult(new MsBuildPropertyEvaluationDto(
            roslynProj.Name,
            roslynProj.FilePath!,
            propertyName,
            string.IsNullOrEmpty(value) ? null : value));
    }

    public Task<MsBuildItemEvaluationDto> EvaluateItemsAsync(
        string workspaceId, string projectName, string itemType, CancellationToken ct)
    {
        MsBuildInitializer.EnsureInitialized();
        var roslynProj = ResolveRoslynProject(workspaceId, projectName);
        ct.ThrowIfCancellationRequested();
        var msbuildProj = GetOrLoadProject(workspaceId, roslynProj);
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

    public Task<MsBuildPropertiesDumpDto> GetEvaluatedPropertiesAsync(
        string workspaceId,
        string projectName,
        string? propertyNameFilter,
        IReadOnlyCollection<string>? includedNames,
        CancellationToken ct)
    {
        MsBuildInitializer.EnsureInitialized();
        var roslynProj = ResolveRoslynProject(workspaceId, projectName);
        ct.ThrowIfCancellationRequested();
        var msbuildProj = GetOrLoadProject(workspaceId, roslynProj);

        // Filter at evaluation time so we never serialize 60KB+ of internal MSBuild
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

    /// <summary>
    /// Returns a loaded MSBuild <see cref="MsBuildProject"/> for the given project, reusing a cached
    /// instance when the workspace version and project file path are unchanged. The
    /// <see cref="Lazy{T}"/> with <see cref="LazyThreadSafetyMode.ExecutionAndPublication"/> guarantees
    /// <c>LoadProject</c> runs exactly once per key even under concurrent callers. Ownership of the
    /// created <see cref="ProjectCollection"/> transfers to the cache; callers must NOT unload it.
    /// </summary>
    private MsBuildProject GetOrLoadProject(string workspaceId, RoslynProject roslynProj)
    {
        var version = _workspace.GetCurrentVersion(workspaceId);
        var bucket = _cache.GetOrAdd(
            workspaceId,
            static _ => new ConcurrentDictionary<(int Version, string FilePath), Lazy<CachedMsBuildProject>>());

        var filePath = roslynProj.FilePath!;
        var entry = bucket.GetOrAdd(
            (version, filePath),
            static key => new Lazy<CachedMsBuildProject>(
                () =>
                {
                    var collection = new ProjectCollection();
                    return new CachedMsBuildProject(collection, collection.LoadProject(key.FilePath));
                },
                LazyThreadSafetyMode.ExecutionAndPublication));

        return entry.Value.Project;
    }

    private void InvalidateWorkspace(string workspaceId)
    {
        if (!_cache.TryRemove(workspaceId, out var bucket))
        {
            return;
        }

        foreach (var lazy in bucket.Values)
        {
            // Only dispose entries whose factory actually ran — touching an uncreated Lazy would
            // force a needless LoadProject just to dispose it.
            if (lazy.IsValueCreated)
            {
                lazy.Value.Dispose();
            }
        }
    }

    /// <summary>
    /// Holds a loaded MSBuild <see cref="MsBuildProject"/> together with its owning
    /// <see cref="ProjectCollection"/>. Disposal unloads and disposes the collection so the cached
    /// import graph is released on workspace reload/close.
    /// </summary>
    private sealed class CachedMsBuildProject(ProjectCollection collection, MsBuildProject project) : IDisposable
    {
        public MsBuildProject Project { get; } = project;

        public void Dispose()
        {
            collection.UnloadAllProjects();
            collection.Dispose();
        }
    }

    private RoslynProject ResolveRoslynProject(string workspaceId, string projectName)
    {
        if (string.IsNullOrWhiteSpace(projectName))
        {
            // msbuild-tools-bad-argument-message: a missing/whitespace project name is the most
            // common MCP client mistake (firewall audit, 2026-04-08). Surface the required
            // parameter name and an invocation example instead of a generic invocation error.
            throw new ArgumentException(
                "The 'project' parameter is required. Pass the project name or absolute .csproj path, " +
                "e.g. { \"workspaceId\": \"<id>\", \"project\": \"MyApp.Core\", \"propertyName\": \"TargetFramework\" }. " +
                "Use workspace_status to list loaded projects.",
                nameof(projectName));
        }

        var solution = _workspace.GetCurrentSolution(workspaceId);
        var proj = ProjectFilterHelper.FilterProjects(solution, projectName).FirstOrDefault();
        if (proj is null)
        {
            // Surface the list of loaded project names so the caller can immediately retry with
            // the correct identifier instead of opening workspace_status.
            var loadedProjects = solution.Projects.Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
            var loadedList = loadedProjects.Count > 0
                ? string.Join(", ", loadedProjects)
                : "(none — the workspace has no projects loaded)";

            throw new InvalidOperationException(
                $"Project '{projectName}' not found in workspace '{workspaceId}'. " +
                $"Loaded projects ({loadedProjects.Count}): {loadedList}. " +
                "Pass a matching 'project' value (project name or absolute .csproj path).");
        }

        if (string.IsNullOrEmpty(proj.FilePath))
        {
            throw new InvalidOperationException(
                $"Project '{projectName}' has no file path. " +
                "The workspace solution may contain a virtual/shim project that cannot be evaluated by MSBuild.");
        }

        return proj;
    }
}
