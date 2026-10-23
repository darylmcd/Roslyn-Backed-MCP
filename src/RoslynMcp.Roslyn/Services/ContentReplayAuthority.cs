using System.Collections.Immutable;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Admits only byte-identical immutable project items in one private replay coordinate.</summary>
internal sealed class ContentReplayAuthority
{
    private readonly ImmutableArray<PhysicalInputIdentity> _originals;
    private readonly ImmutableArray<PhysicalInputIdentity> _copies;
    private readonly InstalledPackageAuthority _packages;
    private readonly ImmutableDictionary<string, string> _coordinates;

    private ContentReplayAuthority(ImmutableArray<PhysicalInputIdentity> originals,
        ImmutableArray<PhysicalInputIdentity> copies, InstalledPackageAuthority packages)
    {
        _originals = originals;
        _copies = copies;
        _packages = packages;
        _coordinates = originals.Select((input, index) => KeyValuePair.Create(input.FilePath, copies[index].FilePath))
            .ToImmutableDictionary(FileSystemPath.Comparer);
    }

    internal static async Task<ContentReplayAuthority> CaptureAsync(string workspaceId, PhysicalGraphLayout layout,
        string destination, IWorkspacePathPolicy policy, CancellationToken ct)
    {
        await layout.Packages.AssertCurrentAsync((paths, token) => policy.ValidateAsync(workspaceId, paths, token), ct).ConfigureAwait(false);
        SdkExistenceInput.AssertCurrent(layout.SdkInputs, ct);
        var originals = ImmutableArray.CreateBuilder<PhysicalInputIdentity>();
        var copies = ImmutableArray.CreateBuilder<PhysicalInputIdentity>();
        var packageInputs = layout.Packages.Inputs.ToDictionary(input => input.FilePath, FileSystemPath.Comparer);
        var sdkProvenance = new ImmutablePathProvenance(layout.ImmutableRoots, [], layout.AuthoredRoots);
        foreach (var path in layout.ContentReplayFiles)
        {
            if (!layout.IsImmutablePath(path))
                throw new PublicInvalidOperationException("A content replay input no longer has immutable provenance.");
            var target = layout.MapTo(destination, path);
            await policy.ValidateAsync(workspaceId, layout.Packages.PathsRequiringCurrentGrant([path, target]), ct).ConfigureAwait(false);
            if (!packageInputs.ContainsKey(path) && !sdkProvenance.IsImmutable(path))
                throw new PublicInvalidOperationException("An immutable content replay input has no retained package or SDK proof.");
            var input = packageInputs.TryGetValue(path, out var admitted) ? admitted
                : await PhysicalInputIdentity.CaptureAsync(path, ct).ConfigureAwait(false);
            if (input.Kind != PhysicalInputKind.File)
                throw new PublicInvalidOperationException("An immutable content replay input is not an admitted file.");
            await PhysicalInputIdentity.AssertCurrentAsync([input], ct).ConfigureAwait(false);
            originals.Add(input);
            copies.Add(input with { FilePath = target });
        }
        return new(originals.ToImmutable(), copies.ToImmutable(), layout.Packages);
    }

    internal ImmutableArray<PhysicalInputIdentity> FinalInputs =>
        PhysicalInputIdentity.MergeConsistent(_originals.Concat(_copies).Concat(_packages.Inputs));
    internal string[] FinalGrantPaths => _packages.PathsRequiringCurrentGrant(FinalInputs.Select(input => input.FilePath));

    internal string MapActual(string path) => _coordinates.TryGetValue(path, out var target) ? target : path;

    internal async Task AssertCurrentAsync(string workspaceId, IWorkspacePathPolicy policy, CancellationToken ct)
    {
        await _packages.AssertPackageFilesCurrentAsync((paths, token) => policy.ValidateAsync(workspaceId, paths, token), ct).ConfigureAwait(false);
        await policy.ValidateAsync(workspaceId, _packages.PathsRequiringCurrentGrant(_originals.Concat(_copies)
            .Select(input => input.FilePath)), ct).ConfigureAwait(false);
        await PhysicalInputIdentity.AssertCurrentAsync(_originals, ct).ConfigureAwait(false);
        await PhysicalInputIdentity.AssertCurrentAsync(_copies, ct).ConfigureAwait(false);
    }
}
