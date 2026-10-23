using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests.Helpers;

/// <summary>Authorizes owned fixture trees and their exact native ancestor editorconfig inputs.</summary>
internal sealed class OwnedTestWorkspacePathPolicy : IWorkspacePathPolicy
{
    // Fixture roots are construction-time policy configuration. Requested paths
    // still receive a fresh physical resolution on every validation.
    private readonly string[] _roots;
    private readonly HashSet<string> _ancestorConfigurations = new(FileSystemPath.Comparer);

    internal OwnedTestWorkspacePathPolicy() : this([TestTempRoot.Current, TestFixtureFileSystem.FindRepositoryRoot()]) { }

    internal OwnedTestWorkspacePathPolicy(IReadOnlyList<string> fixtureRoots)
    {
        _roots = fixtureRoots.Select(root => Path.TrimEndingDirectorySeparator(Path.GetFullPath(root))).ToArray();
        foreach (var root in _roots)
            for (var ancestor = Path.GetDirectoryName(root); ancestor is not null; ancestor = Path.GetDirectoryName(ancestor))
                // The SDK includes every existing ancestor editorconfig as a
                // compiler operand, even above root=true. Configure exact known
                // fixture coordinates; never enroll requested external paths.
                _ancestorConfigurations.Add(Path.Combine(ancestor, ".editorconfig"));
    }

    public Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
    {
        foreach (var path in paths)
        {
            ct.ThrowIfCancellationRequested();
            var physical = PhysicalPathResolver.Resolve(path);
            if (!FileSystemPath.Comparer.Equals(Path.TrimEndingDirectorySeparator(physical),
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)))
                || !(_ancestorConfigurations.Contains(physical)
                    || _roots.Any(root => FileSystemPath.Comparer.Equals(root, physical) || FileSystemPath.IsStrictDescendant(root, physical))))
            {
                var failure = new UnauthorizedAccessException("The physical workspace inventory is outside the owned test fixture roots.");
                failure.Data["RejectedFileName"] = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
                failure.Data["SameAfterDirectoryNormalization"] = FileSystemPath.Comparer.Equals(
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)), Path.TrimEndingDirectorySeparator(physical));
                failure.Data["InsideOwnedRoot"] = _roots.Any(root => FileSystemPath.Comparer.Equals(root, physical)
                    || FileSystemPath.IsStrictDescendant(root, physical));
                if (failure.Data["InsideOwnedRoot"] is true && failure.Data["SameAfterDirectoryNormalization"] is false)
                    WorkspacePathPolicyRefusal.Mark(failure, WorkspacePathPolicyRefusalKind.AliasedPath);
                throw failure;
            }
        }
        return Task.CompletedTask;
    }
}
