using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Freezes authored restore inputs and advances only successful native output epochs.</summary>
internal sealed record RestoreInputAuthority(IReadOnlyList<PhysicalInputIdentity> Inputs, InstalledPackageAuthority Packages,
        IReadOnlyList<EvaluatedBuildGraph.Node> Nodes, IReadOnlySet<string> Generated,
        ImmutableArray<SdkExistenceInput> SdkConditions, ImmutableArray<BuildPathRoles.ConditionProbe> Conditions,
        IReadOnlyList<string> MutablePaths, Func<IReadOnlyList<string>, CancellationToken, Task> Authorize)
{
    public override string ToString() => "RestoreInputAuthority";
    private IReadOnlyList<PhysicalInputIdentity> _outputs = [];
    private readonly HashSet<string> _successfulOutputs = new(FileSystemPath.Comparer);
    internal IReadOnlySet<string> TransitionedOutputPaths => _outputs.Select(input => input.FilePath).ToHashSet(FileSystemPath.Comparer);

    internal async Task AssertCurrentAsync(CancellationToken ct)
    {
        var inputs = this;
        await inputs.Authorize(inputs.MutablePaths, ct).ConfigureAwait(false);
        await inputs.Packages.AssertPackageFilesCurrentAsync((paths, token) => inputs.Authorize( paths, token), ct).ConfigureAwait(false);
        await inputs.Authorize( inputs.MutablePaths, ct).ConfigureAwait(false);
        await PhysicalInputIdentity.AssertCurrentAsync(inputs.Inputs, ct).ConfigureAwait(false);
        await inputs.Authorize(inputs._outputs.Select(input => input.FilePath).ToArray(), ct).ConfigureAwait(false);
        await PhysicalInputIdentity.AssertCurrentAsync(inputs._outputs, ct).ConfigureAwait(false);
        SdkExistenceInput.AssertCurrent(inputs.SdkConditions, ct);
        ConditionInputTruth.AssertObserved(inputs.Conditions, inputs.Inputs);
        EvaluatedBuildGraph.Graph current = EvaluatedBuildGraph.FromEntries(inputs.Nodes.Select(node => new EvaluatedBuildGraph.Entry(node.ProjectPath, node.Globals)),
            ImmutableDictionary<string, byte[]>.Empty, ct);
        var assets = current.Nodes.Select(node => node.Inputs.AssetsPath).OfType<string>().Distinct(FileSystemPath.Comparer).ToArray();
        InstalledPackageAuthority packages = await BuildInputProvenance.ReadPackagesAsync(assets,
            current.Nodes.Select(node => Path.GetDirectoryName(node.ProjectPath)!).ToArray(),
            (paths, token) => inputs.Authorize( paths, token), ct).ConfigureAwait(false);
        inputs.AssertMembership(current, packages, ct);
        // Native graph reconstruction and selecting-assets reads are part of this
        // observation. Close their window against the original generated epoch as
        // well as the newly reconstructed package proof before authorizing effects.
        await inputs.Packages.AssertPackageFilesCurrentAsync(inputs.Authorize, ct).ConfigureAwait(false);
        await packages.AssertCurrentAsync(inputs.Authorize, ct).ConfigureAwait(false);
        await inputs.Authorize(inputs.MutablePaths, ct).ConfigureAwait(false);
        await PhysicalInputIdentity.AssertCurrentAsync(inputs.Inputs, ct).ConfigureAwait(false);
        await inputs.Authorize(inputs._outputs.Select(input => input.FilePath).ToArray(), ct).ConfigureAwait(false);
        await PhysicalInputIdentity.AssertCurrentAsync(inputs._outputs, ct).ConfigureAwait(false);
        SdkExistenceInput.AssertCurrent(inputs.SdkConditions, ct);
        _currentPackages = packages.RetainPackageFilesFrom(Packages);
    }

    private InstalledPackageAuthority? _currentPackages;
    private InstalledPackageAuthority CurrentPackages => _currentPackages
        ?? throw new PublicInvalidOperationException("Restore inputs have no completed current observation.");
    internal Task AuthorizeFinalAsync(IEnumerable<string> additionalPaths, CancellationToken ct) =>
        Authorize(MutablePaths.Concat(_outputs.Select(input => input.FilePath))
            .Concat(CurrentPackages.RequiresCurrentGrant).Concat(additionalPaths)
            .Distinct(FileSystemPath.Comparer).ToArray(), ct);

    // All policy callbacks precede this native observation. It may discover new
    // matching inputs, but cannot bless them or replace the retained byte epoch.
    internal void AssertNativeMembershipCurrent(CancellationToken ct)
    {
        var current = EvaluatedBuildGraph.FromEntries(Nodes.Select(node => new EvaluatedBuildGraph.Entry(node.ProjectPath, node.Globals)),
            ImmutableDictionary<string, byte[]>.Empty, ct);
        AssertMembership(current, CurrentPackages, ct);
    }

    internal async Task AssertPhysicalCurrentAsync(IEnumerable<PhysicalInputIdentity> additionalInputs, CancellationToken ct)
    {
        var identities = PhysicalInputIdentity.MergeConsistent(Inputs.Concat(_outputs)
            .Concat(CurrentPackages.Inputs).Concat(additionalInputs));
        await PhysicalInputCaptureBatch.AssertCurrentAsync(identities, ct).ConfigureAwait(false);
        SdkExistenceInput.AssertCurrent(SdkConditions, ct);
        ConditionInputTruth.AssertObserved(Conditions, identities);
    }

    private void AssertMembership(EvaluatedBuildGraph.Graph current, InstalledPackageAuthority packages, CancellationToken ct)
    {
        var inputs = this;
        var protectedPaths = inputs.Inputs.Select(input => input.FilePath).ToHashSet(FileSystemPath.Comparer);
        var admitted = inputs._successfulOutputs.Concat(packages.Files).Concat(inputs.Packages.Files)
            .Where(path => !protectedPaths.Contains(path)).ToHashSet(FileSystemPath.Comparer);
        string Key(EvaluatedBuildGraph.Node node) => node.ProjectPath + "|" + NativeTaskExecutionIdentity.EscapedContextIdentity(node.Globals, node.TaskRegistrations.ToolsVersion, ct);
        var expectedNodes = inputs.Nodes.GroupBy(Key, StringComparer.Ordinal).ToDictionary(group => group.Key, group =>
        {
            EvaluatedBuildGraph.Node first = group.First();
            if (group.Any(node => !node.FileItems.SequenceEqual(first.FileItems) || node.EvaluatedItems != first.EvaluatedItems || node.EvaluatedProperties != first.EvaluatedProperties))
                throw new PublicInvalidOperationException("Selected restore closures disagree about their captured input membership.");
            return first;
        }, StringComparer.Ordinal);
        var currentNodes = current.Nodes.ToDictionary(Key, StringComparer.Ordinal);
        if (!expectedNodes.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(currentNodes.Keys))
            throw new PublicInvalidOperationException("Authored project membership changed during restore; reload before preparing tests.");
        foreach ((string? key, EvaluatedBuildGraph.Node? expected) in expectedNodes)
        {
            ct.ThrowIfCancellationRequested();
            EvaluatedBuildGraph.Node actual = currentNodes[key];
            NativeBuildMembership.FileItem[] expectedItems = [.. expected.FileItems.Where(item => !admitted.Contains(item.Path))];
            NativeBuildMembership.FileItem[] actualItems = [.. actual.FileItems.Where(item => !admitted.Contains(item.Path))];
            IEnumerable<string> Paths(EvaluatedBuildGraph.Node node) => node.BuildInputs.Definitions.Concat(node.PathEvidence.InputFiles)
                .Concat(node.DocumentFiles).Concat(node.ArtifactFiles).Where(path => !admitted.Contains(path));
            if (!expectedItems.SequenceEqual(actualItems) || !Paths(expected).ToHashSet(FileSystemPath.Comparer).SetEquals(Paths(actual)))
                throw new PublicInvalidOperationException("Authored input membership changed during restore; reload before preparing tests.");
            var immutable = new ImmutablePathProvenance(expected.BuildInputs.ImmutableRoots, [],
                inputs.Nodes.Select(node => Path.GetDirectoryName(node.ProjectPath)!));
            bool ProtectedCondition(BuildPathRoles.ConditionProbe probe) => !admitted.Contains(probe.Origin)
                && !IsRestoreConditionTransition(probe, admitted, immutable);
            if (!expected.PathEvidence.ConditionProbes.Where(ProtectedCondition).ToHashSet()
                .SetEquals(actual.PathEvidence.ConditionProbes.Where(ProtectedCondition)))
            {
                throw new PublicInvalidOperationException("Authored condition membership changed during restore; reload before preparing tests.");
            }
        }
    }

    private static bool IsRestoreConditionTransition(BuildPathRoles.ConditionProbe probe, IReadOnlySet<string> outputs,
        ImmutablePathProvenance provenance)
    {
        if (outputs.Contains(probe.Path)) return true;
        // Only SDK/package-authored existence checks may observe a producer-created
        // parent directory. This grants no authority to enumerate its descendants.
        if (!provenance.IsImmutable(probe.Origin)) return false;
        return outputs.Any(output =>
        {
            for (var directory = Path.GetDirectoryName(output); directory is not null; directory = Path.GetDirectoryName(directory))
                if (FileSystemPath.Comparer.Equals(Path.TrimEndingDirectorySeparator(probe.Path), directory)) return true;
            return false;
        });
    }
    internal static async Task<RestoreInputAuthority> CaptureAsync(
        IReadOnlyList<EvaluatedBuildGraph.Node> nodes, IReadOnlyList<EvaluatedBuildGraph.Node> producers, Solution solution,
        Func<IReadOnlyList<string>, CancellationToken, Task> authorize, CancellationToken ct)
    {
        var assets = nodes.Select(node => node.Inputs.AssetsPath).OfType<string>().Distinct(FileSystemPath.Comparer).ToArray();
        await authorize( assets, ct).ConfigureAwait(false);
        InstalledPackageAuthority packages = await BuildInputProvenance.ReadPackagesAsync(assets, nodes.Select(node => Path.GetDirectoryName(node.ProjectPath)!).ToArray(), (paths, token) => authorize( paths, token), ct).ConfigureAwait(false);

        var authored = nodes.Select(node => Path.GetDirectoryName(node.ProjectPath)!).Distinct(FileSystemPath.Comparer).ToArray();
        var provenance = new ImmutablePathProvenance(nodes.SelectMany(node => node.BuildInputs.ImmutableRoots), packages.Files, authored);
        var generated = producers.Where(node => node.Inputs.ExtensionsPath is not null).SelectMany(node => new[]
        {
            Path.Combine(node.Inputs.ExtensionsPath!, Path.GetFileName(node.ProjectPath) + ".nuget.g.props"),
            Path.Combine(node.Inputs.ExtensionsPath!, Path.GetFileName(node.ProjectPath) + ".nuget.g.targets"),
            Path.Combine(node.Inputs.ExtensionsPath!, Path.GetFileName(node.ProjectPath) + ".nuget.dgspec.json")
        }).Concat(producers.Select(node => node.Inputs.AssetsPath).OfType<string>()).ToHashSet(FileSystemPath.Comparer);
        BuildPathRoles.ConditionProbe[] probes = [.. nodes.SelectMany(node => node.PathEvidence.ConditionProbes).Distinct()];
        ImmutableArray<SdkExistenceInput> sdkConditions = SdkExistenceInput.Capture(probes.Where(SdkExistenceInput.Supports), ct);
        var conditions = probes.Where(probe => !SdkExistenceInput.Supports(probe)
            && !IsRestoreConditionTransition(probe, generated, provenance)).ToImmutableArray();
        var definitions = nodes.SelectMany(node => node.BuildInputs.Definitions).Concat(nodes.Select(node => node.ProjectPath))
            .Concat(nodes.SelectMany(node => node.PathEvidence.InputFiles)).Where(path => !generated.Contains(path))
            .Concat(nodes.SelectMany(node => node.DocumentFiles))
            .Concat(nodes.SelectMany(node => node.ArtifactFiles))
            .Concat(solution.Projects.SelectMany(SolutionFileMutationPlanner.AllDocuments).Select(document => document.FilePath).OfType<string>())
            .Concat(conditions.Select(probe => probe.Path))
            .Where(path => !provenance.IsImmutable(path))
            .Distinct(FileSystemPath.Comparer).ToArray();
        var immutableConditions = conditions.Select(probe => probe.Path).Where(provenance.IsImmutable)
            .Distinct(FileSystemPath.Comparer).ToArray();
        // Package membership does not replace current grants for custom caches.
        // Carry only package paths that this protected-input snapshot actually reads.
        var mutable = definitions.Concat(immutableConditions.Intersect(packages.RequiresCurrentGrant, FileSystemPath.Comparer))
            .Distinct(FileSystemPath.Comparer).ToArray();
        await authorize( definitions, ct).ConfigureAwait(false);
        await authorize( mutable, ct).ConfigureAwait(false);
        var result = new List<PhysicalInputIdentity>();
        foreach (var path in definitions) result.Add(await PhysicalInputIdentity.CaptureAsync(path, ct).ConfigureAwait(false));
        // Immutable condition targets still need exact truth identities, but never
        // grant arbitrary roots: the existing provenance controls their reads.
        foreach (var path in immutableConditions)
            result.Add(await PhysicalInputIdentity.CaptureAsync(path, ct).ConfigureAwait(false));
        ConditionInputTruth.AssertObserved(conditions, result);
        SdkExistenceInput.AssertCurrent(sdkConditions, ct);
        var authority = new RestoreInputAuthority(result, packages, nodes, generated, sdkConditions, conditions, mutable, authorize);
        var outputPaths = generated.Except(result.Select(input => input.FilePath), FileSystemPath.Comparer).ToArray();
        await authorize(outputPaths, ct).ConfigureAwait(false);
        var outputInputs = new List<PhysicalInputIdentity>();
        foreach (var path in outputPaths) outputInputs.Add(await PhysicalInputIdentity.CaptureAsync(path, ct).ConfigureAwait(false));
        authority._outputs = outputInputs;
        return authority;
    }


    internal string[] OutputsFor(RestoreInvocationPlanner.Invocation invocation) => Nodes
        .Where(node => !node.IsOuterBuild && FileSystemPath.Comparer.Equals(node.ProjectPath, invocation.TargetPath)
            && invocation.Context is { } context
            && FileSystemPath.Comparer.Equals(node.Inputs.AssetsPath, context.AssetsPath))
        .SelectMany(GeneratedPaths).Distinct(FileSystemPath.Comparer).ToArray();

    internal async Task AuthorizeInvocationAsync(RestoreInvocationPlanner.Invocation invocation, CancellationToken ct)
    {
        if (OutputsFor(invocation).Intersect(Inputs.Select(input => input.FilePath), FileSystemPath.Comparer).Any())
            throw new PublicInvalidOperationException("A restore output overlaps a protected authored input.");
        await AssertCurrentAsync(ct).ConfigureAwait(false);
    }

    internal async Task AcceptSuccessfulAsync(RestoreInvocationPlanner.Invocation invocation, CancellationToken ct)
    {
        var outputs = OutputsFor(invocation).ToHashSet(FileSystemPath.Comparer);
        var epoch = await RestoreOutputEpoch.CaptureAfterSuccessfulRestoreAsync(_outputs, outputs,
            Inputs.Select(input => input.FilePath).ToHashSet(FileSystemPath.Comparer), Authorize, ct).ConfigureAwait(false);
        _outputs = epoch.Inputs;
        _successfulOutputs.UnionWith(outputs);
        await AssertCurrentAsync(ct).ConfigureAwait(false);
    }

    internal static IEnumerable<string> GeneratedPaths(EvaluatedBuildGraph.Node node)
    {
        if (node.Inputs.AssetsPath is { } assets) yield return assets;
        if (node.Inputs.ExtensionsPath is not { } extensions) yield break;
        foreach (var suffix in new[] { ".nuget.g.props", ".nuget.g.targets", ".nuget.dgspec.json" })
            yield return Path.Combine(extensions, Path.GetFileName(node.ProjectPath) + suffix);
    }
}
