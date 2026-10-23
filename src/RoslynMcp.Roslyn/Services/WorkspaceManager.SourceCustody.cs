using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

public sealed partial class WorkspaceManager
{
    private const string SourceCustodyParentName = ".roslynmcp-custody";

    async Task<PhysicalSourceCustody> IWorkspaceRecoveryAuthority.AcquireSourceCustodyAsync(
        PhysicalMutationRecovery operation, string sourcePath, PhysicalDirectoryScope sourceParent,
        string leafName, string temporaryName, CancellationToken ct)
    {
        sourcePath = Path.GetFullPath(sourcePath);
        PhysicalDirectoryScope.ValidateName(leafName);
        PhysicalDirectoryScope.ValidateName(temporaryName);
        if (!operation.Files.Any(file => FileSystemPath.Comparer.Equals(file.FilePath, sourcePath))
            || !FileSystemPath.Comparer.Equals(Path.GetDirectoryName(sourcePath), sourceParent.Path))
            throw new WorkspaceMutationOwnershipException("Source custody is outside the captured operation.");
        var allowedSourcePaths = new[] { sourcePath, sourceParent.Path, Path.Combine(sourceParent.Path, temporaryName) }
            .ToHashSet(FileSystemPath.Comparer);
        AssertOperationOwned();
        var originalLayout = operation.CapturedLayout
            ?? throw InvalidOperationErrors.Internal("Source custody requires the captured native graph layout.");
        PhysicalRestorationAuthority[] desired;
        lock (_mutationLock) desired = RequireMutation(operation).RestorationAuthorities.ToArray();
        var observedLayouts = desired.Select(authority => authority.Layout).Prepend(originalLayout).ToArray();
        var sourceRoots = observedLayouts.SelectMany(layout => layout.ContentRoots.Concat(layout.AuthoredRoots)
                .Concat(layout.BuildNodes.SelectMany(node => node.PathEvidence.TraversalRoots)))
            .Distinct(FileSystemPath.Comparer).ToArray();

        foreach (var anchorPath in SourceCustodyAnchors(sourceParent.Path))
        {
            ct.ThrowIfCancellationRequested();
            PhysicalDirectoryScope? existing = null;
            PhysicalDirectoryScope? parent = null;
            PhysicalDirectoryScope? anchor = null;
            Exception? primary = null;
            try
            {
                anchor = PhysicalDirectoryScope.OpenRouteIfPresent(anchorPath);
                if (anchor is null || !sourceParent.SameFilesystem(anchor)) continue;
                var heldAnchor = anchor;
                heldAnchor.AssertCurrentPath();
                var parentPath = Path.Combine(anchorPath, SourceCustodyParentName);
                var childPath = Path.Combine(parentPath, leafName);
                // A mounted existing service parent may differ from its lexical anchor.
                existing = PhysicalDirectoryScope.OpenRouteIfPresent(parentPath);
                existing?.AssertCurrentPath();
                if (existing is not null && !sourceParent.SameFilesystem(existing)) continue;
                try
                {
                    await AssertAdmissionAsync([], ct).ConfigureAwait(false);
                    // Administrative placement is not an exemption from native abort
                    // proof. Record possible parent creation without deletion authority.
                    if (existing is null) operation.RecordSourceNamespace(parentPath);
                    parent = await PhysicalGraphDirectory.OpenParentAsync(anchorPath, [SourceCustodyParentName],
                        leafName, AssertAdmissionAsync, ct).ConfigureAwait(false);
                }
                catch (CustodyObservationConflictException) { continue; }
                sourceParent.AssertCurrentPath();
                parent.AssertCurrentPath();
                if (!sourceParent.SameFilesystem(parent))
                    throw new IOException("The admitted custody namespace changed its source filesystem.");
                var releasingExisting = existing;
                existing = null;
                var releasingAnchor = anchor;
                anchor = null;
                OwnedResourceCleanup.Dispose("Source custody admission witnesses failed to release.", null, releasingExisting, releasingAnchor);
                var result = new PhysicalSourceCustody(sourceParent, parent, leafName, AssertOperationOwned, AssertAdmissionOwned, AuthorizeSourceAsync);
                parent = null;
                return result;

                async Task AssertAdmissionAsync(IReadOnlyList<string> _, CancellationToken token)
                {
                    AssertAdmissionOwned();
                    sourceParent.AssertCurrentPath();
                    heldAnchor.AssertCurrentPath();
                    existing?.AssertCurrentPath();
                    foreach (var authority in desired) authority.AssertCurrent(token);
                    await AssertSnapshotDisjointAsync(operation.PreApplySolution, static path => path).ConfigureAwait(false);
                    foreach (var authority in desired)
                        await AssertSnapshotDisjointAsync(authority.Expected.Solution, authority.Expected.MapPath).ConfigureAwait(false);
                    foreach (var probe in observedLayouts.SelectMany(layout => layout.BuildNodes).SelectMany(node => node.PathEvidence.ConditionProbes))
                    {
                        var changedParent = existing is null && FileSystemPath.Comparer.Equals(probe.Path, parentPath);
                        var changedChild = FileSystemPath.Comparer.Equals(probe.Path, childPath)
                            || FileSystemPath.IsStrictDescendant(childPath, probe.Path);
                        if (changedParent || changedChild) throw new CustodyObservationConflictException();
                    }
                    foreach (var authority in desired) authority.AssertCurrent(token);
                    existing?.AssertCurrentPath();
                    heldAnchor.AssertCurrentPath();
                    sourceParent.AssertCurrentPath();
                    AssertAdmissionOwned();

                    async Task AssertSnapshotDisjointAsync(Microsoft.CodeAnalysis.Solution snapshot, Func<string, string> map)
                    {
                        var certificate = NativeCompilerContextCertificate.Require(snapshot);
                        if (!await certificate.IsMappedTemporaryPathDisjointAsync(snapshot, operation.WorkspaceId, _pathPolicy,
                            parentPath, map, sourceRoots, token).ConfigureAwait(false)
                            || !await certificate.IsMappedTemporaryPathDisjointAsync(snapshot, operation.WorkspaceId, _pathPolicy,
                                childPath, map, sourceRoots, token).ConfigureAwait(false))
                            throw new CustodyObservationConflictException();
                    }
                }
            }
            catch (Exception failure) { primary = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Source custody parent admission cleanup failed.", primary, parent, existing, anchor); }
        }
        throw new PublicInvalidOperationException("Source mutation requires an unobserved administrative custody namespace on the source filesystem.");

        void AssertOperationOwned()
        {
            lock (_mutationLock)
            {
                RequireMutation(operation);
                if ((!_activeMutations.TryGetValue(operation.WorkspaceId, out var active) || !ReferenceEquals(active, operation))
                    && (!_recoveries.TryGetValue(operation.WorkspaceId, out var pending) || !ReferenceEquals(pending, operation)))
                    throw new WorkspaceMutationOwnershipException("The source custody receipt no longer belongs to an active or pending recovery.");
            }
        }

        void AssertAdmissionOwned()
        {
            AssertOperationOwned();
            lock (_mutationLock)
                if (!RequireMutation(operation).RestorationAuthorities.SetEquals(desired))
                    throw new WorkspaceMutationOwnershipException("The independently staged custody observations changed during admission.");
            foreach (var authority in desired) authority.AssertCurrent(CancellationToken.None);
        }

        async Task AuthorizeSourceAsync(IReadOnlyList<string> paths, CancellationToken token)
        {
            AssertOperationOwned();
            if (paths.Any(path => !allowedSourcePaths.Contains(path)))
                throw new WorkspaceMutationOwnershipException("A custody receipt cannot authorize another source transition.");
            await ((IWorkspacePathAuthority)this).AuthorizePathsAsync(operation.WorkspaceId, paths, token).ConfigureAwait(false);
            AssertOperationOwned();
        }
    }

    internal static IEnumerable<string> SourceCustodyAnchors(string sourceParent)
    {
        var seen = new HashSet<string>(FileSystemPath.Comparer);
        var temporary = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        if (seen.Add(temporary)) yield return temporary;
        for (var ancestor = Path.GetDirectoryName(sourceParent); ancestor is not null; ancestor = Path.GetDirectoryName(ancestor))
            if (seen.Add(ancestor)) yield return ancestor;
    }

    private sealed class CustodyObservationConflictException : Exception { }
}
