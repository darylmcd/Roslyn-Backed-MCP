using System.Collections.Immutable;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>An exact SDK-origin existence query; grants neither content reads nor descendant access.</summary>
internal sealed class SdkExistenceInput
{
    internal string Origin { get; }
    internal string Path { get; }
    private string QueryPath { get; }
    private bool ObservedExists { get; }
    private bool Exists { get; }
    private bool IsDirectory { get; }
    private SdkExistenceInput(string origin, string path, string queryPath, bool observedExists, bool exists, bool directory)
        => (Origin, Path, QueryPath, ObservedExists, Exists, IsDirectory) = (origin, path, queryPath, observedExists, exists, directory);
    public override string ToString() => "RegisteredSdkExistenceInput";
    internal bool EquivalentTo(SdkExistenceInput other) => Origin == other.Origin && Path == other.Path
        && QueryPath == other.QueryPath && ObservedExists == other.ObservedExists && Exists == other.Exists && IsDirectory == other.IsDirectory;

    internal static bool Supports(BuildPathRoles.ConditionProbe probe)
    {
        if (probe.ObservedExists is null) return false;
        var sdk = BuildInputProvenance.RegisteredSdkDirectory();
        var parent = System.IO.Path.GetDirectoryName(sdk);
        if (parent is null || !System.IO.Path.GetFileName(parent).Equals("sdk", StringComparison.OrdinalIgnoreCase)) return false;
        var installation = System.IO.Path.GetDirectoryName(parent);
        if (installation is null) return false;
        var origin = System.IO.Path.Combine(sdk, "Sdks", "Microsoft.NET.Sdk", "targets", "Microsoft.NET.NuGetOfflineCache.targets");
        // These two native Exists candidates come from the registered installation,
        // never from project-controlled NetCoreRoot or intermediate SDK properties.
        var candidates = new[] { System.IO.Path.Combine(parent, "NuGetFallbackFolder"), System.IO.Path.Combine(installation, "library-packs") };
        return FileSystemPath.Comparer.Equals(System.IO.Path.GetFullPath(probe.Origin), origin)
            && candidates.Contains(System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(probe.Path)), FileSystemPath.Comparer);
    }

    internal static ImmutableArray<SdkExistenceInput> Capture(IEnumerable<BuildPathRoles.ConditionProbe> probes, CancellationToken ct)
    {
        var result = ImmutableArray.CreateBuilder<SdkExistenceInput>();
        foreach (var probe in probes.Distinct())
        {
            ct.ThrowIfCancellationRequested();
            if (probe.ObservedExists is not { } observed || !Supports(probe)) throw new PublicInvalidOperationException("The existence query has no independent registered SDK authority.");
            var identity = Inspect(probe.Path);
            ConditionInputTruth.AssertObserved(probe, Kind(identity));
            result.Add(new(probe.Origin, probe.Path, probe.QueryPath, observed, identity.Exists, identity.Directory));
        }
        return result.ToImmutable();
    }

    internal static void AssertCurrent(IEnumerable<SdkExistenceInput> inputs, CancellationToken ct)
    {
        foreach (var input in inputs)
        {
            ct.ThrowIfCancellationRequested();
            var probe = new BuildPathRoles.ConditionProbe(input.Origin, input.Path, input.QueryPath, input.ObservedExists);
            if (!Supports(probe)) throw new PublicInvalidOperationException("The registered SDK existence authority changed.");
            var current = Inspect(input.Path);
            ConditionInputTruth.AssertObserved(probe, Kind(current));
            if (input.Exists != current.Exists || input.IsDirectory != current.Directory)
                throw new PublicInvalidOperationException("A registered SDK existence input changed after preparation; reload the workspace.");
        }
    }

    private static PhysicalInputKind Kind((bool Exists, bool Directory) identity) => !identity.Exists
        ? PhysicalInputKind.Missing : identity.Directory ? PhysicalInputKind.Directory : PhysicalInputKind.File;

    private static (bool Exists, bool Directory) Inspect(string path)
    {
        WorkspaceManager.EnsurePhysicalPath(path);
        try { return (true, (File.GetAttributes(path) & FileAttributes.Directory) != 0); }
        catch (FileNotFoundException) { return (false, false); }
        catch (DirectoryNotFoundException) { return (false, false); }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        { throw new PublicInvalidOperationException("The registered SDK existence input could not be inspected safely."); }
    }
}
