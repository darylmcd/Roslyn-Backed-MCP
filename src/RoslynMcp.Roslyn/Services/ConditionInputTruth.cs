using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Binds native predicate truth to an already-authorized physical identity, without reading or evaluating anything.</summary>
internal static class ConditionInputTruth
{
    internal static bool LogicalExists(string queryPath, PhysicalInputKind kind) =>
        kind == PhysicalInputKind.Directory || kind == PhysicalInputKind.File && !Path.EndsInDirectorySeparator(queryPath);

    internal static void AssertObserved(BuildPathRoles.ConditionProbe probe, PhysicalInputKind kind)
    {
        if (!FileSystemPath.Comparer.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(probe.QueryPath)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(probe.Path))))
            throw new PublicInvalidOperationException("A native condition query does not match its captured physical input.");
        if (probe.ObservedExists is { } observed && observed != LogicalExists(probe.QueryPath, kind))
            throw new PublicInvalidOperationException("A condition input changed after native evaluation; reload the workspace before preparing physical execution.");
    }

    internal static void AssertObserved(IEnumerable<BuildPathRoles.ConditionProbe> probes, IReadOnlyList<PhysicalInputIdentity> identities)
    {
        var captured = PhysicalInputIdentity.MergeConsistent(identities.Select(input => input with
        { FilePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(input.FilePath)) }))
            .ToDictionary(input => input.FilePath, FileSystemPath.Comparer);
        foreach (var probe in probes)
        {
            if (!captured.TryGetValue(Path.TrimEndingDirectorySeparator(Path.GetFullPath(probe.Path)), out var identity))
                throw new PublicInvalidOperationException("A native condition input has no authorized physical identity.");
            AssertObserved(probe, identity.Kind);
        }
    }
}
