using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

internal enum RestoreArtifactPhase { BeforeRestore, AfterRestore }

/// <summary>Binds package-derived compiler inputs to the exact selected assets bytes and item origins.</summary>
internal sealed class RestoredCompilerInputAuthority
{
    private sealed record ProjectInputs(RestoredPackageManifest Manifest,
        MsBuildMetadataHelper.CompilerDeclarations Declarations, string? GeneratedConfig,
        ImmutableHashSet<string> NuGetImports, ImmutableArray<PhysicalInputIdentity> PackageImports,
        Func<string, string> MapPath, string NativeProjectPath, CompilerGraphIntent OriginalGraph, CompilerGraphIntent ReviewedGraph,
        ImmutableDictionary<ProjectId, string> NativeProjectIdentities, InstalledPackageAuthority Installed);
    private sealed record CompilerGraphTarget(string Path, string? Output, string Framework);
    private sealed record CompilerGraphIntent(string ProjectPath, CompilerProjectReferences References,
        ImmutableArray<CompilerGraphTarget> Reachable, ImmutableArray<NativeCompilerContextCertificate.ProducedReference> Produced);
    private readonly ImmutableDictionary<string, ProjectInputs> _projects;
    private readonly ImmutableHashSet<string> _mutablePackageInputs;
    private readonly ImmutableArray<MsBuildCommandProperties.Redactor> _redactors;
    private RestoredCompilerInputAuthority(ImmutableDictionary<string, ProjectInputs> projects,
        ImmutableArray<PhysicalInputIdentity> assets, ImmutableArray<PhysicalInputIdentity> packageInputs,
        ImmutableHashSet<string> mutablePackageInputs, ImmutableHashSet<string> outputs,
        ImmutableArray<MsBuildCommandProperties.Redactor> redactors)
    { _projects = projects; Assets = assets; PackageInputs = packageInputs; _mutablePackageInputs = mutablePackageInputs; NuGetGeneratedInputs = outputs; _redactors = redactors; }

    internal ImmutableArray<PhysicalInputIdentity> Assets { get; }
    internal ImmutableArray<PhysicalInputIdentity> PackageInputs { get; }
    internal IReadOnlySet<string> NuGetGeneratedInputs { get; }
    internal IEnumerable<string> FinalGrantPaths => Assets.Select(input => input.FilePath).Concat(_mutablePackageInputs);

    internal CompilerReferenceAuthority ReferenceAuthority()
    {
        try
        {
            return new(_projects.ToDictionary(pair => pair.Key, pair => BuildReferenceEdges(pair.Value.OriginalGraph,
                pair.Value.Manifest.TransitiveProjects, pair.Value.NativeProjectPath, pair.Value.MapPath), FileSystemPath.Comparer));
        }
        catch (Exception failure) { throw SanitizeFailure(failure); }
    }

    internal static async Task<CompilerReferenceAuthority> CaptureReferenceAuthorityAsync(Solution solution,
        IReadOnlyList<ProjectId> owners, Func<string, CancellationToken, Task<byte[]?>> readBytes,
        Func<string, CancellationToken, Task<FileContentDigest?>> readDigest,
        Func<IReadOnlyList<string>, CancellationToken, Task> authorizePaths, CancellationToken ct)
    {
        if (owners.Count == 0) return new(new Dictionary<string, CompilerReferenceAuthority.ProjectEdges>());
        await NativeCompilerContextCertificate.Require(solution).AssertCurrentAsync(solution, authorizePaths, readDigest, ct).ConfigureAwait(false);
        var graph = EvaluatedBuildGraph.Source(solution, ImmutableDictionary<string, byte[]>.Empty, ct);
        var redactor = new MsBuildCommandProperties.Redactor(graph.Nodes.SelectMany(node => MsBuildCommandProperties.Append([], node.Globals)).ToArray());
        try
        {
            var proofs = new Dictionary<string, CompilerReferenceAuthority.ProjectEdges>(FileSystemPath.Comparer);
            foreach (var id in owners.Distinct())
            {
                ct.ThrowIfCancellationRequested();
                var project = solution.GetProject(id) ?? throw new PublicInvalidOperationException("A compiler-reference owner is missing.");
                var node = EvaluatedBuildGraph.Resolve(project, graph);
                ImmutableArray<RestoredProjectLibrary> libraries = [];
                if (node.Inputs.AssetsPath is { } path && await readBytes(path, ct).ConfigureAwait(false) is { } bytes)
                {
                    try
                    {
                        using var document = JsonDocument.Parse(bytes);
                        libraries = RestoredPackageManifest.ReadProjectLibraries(document.RootElement, node.Inputs, ct);
                    }
                    catch (JsonException) { throw new PublicInvalidOperationException("Restore assets are not valid compiler-reference provenance."); }
                }
                proofs.Add(PhysicalProjectIdentity.Key(project, static path => path),
                    BuildReferenceEdges(GraphIntent(node, static path => path, project), libraries, node.ProjectPath, static path => path));
            }
            return new(proofs);
        }
        catch (Exception failure) { throw redactor.Failure(failure); }
    }

    private static CompilerReferenceAuthority.ProjectEdges BuildReferenceEdges(CompilerGraphIntent graph,
        ImmutableArray<RestoredProjectLibrary> libraries, string nativeProjectPath, Func<string, string> map)
    {
        var authored = AuthoredReferenceEdges(graph);
        var implicitEdges = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        if (!graph.References.DisableTransitive && graph.References.Defaults.ReferenceOutputAssembly)
            foreach (var library in libraries)
            {
                var target = ResolveProjectLibrary(library, nativeProjectPath, map, graph);
                var metadata = ProducedMetadata(graph, target, graph.References.Defaults.Aliases);
                implicitEdges.Add(CompilerReferenceAuthority.Key(target.Path, target.Output,
                    metadata.Aliases, metadata.EmbedInteropTypes));
            }
        return new(authored, implicitEdges.ToImmutable());
    }

    private static ImmutableHashSet<string> AuthoredReferenceEdges(CompilerGraphIntent graph)
    {
        var authored = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (var item in graph.References.Declared.Where(item => item.Metadata.ReferenceOutputAssembly))
        {
            var targets = item.Targets.ToArray();
            if (targets.Length != 1)
                throw new PublicInvalidOperationException("An authored compiler reference has no unique evaluated output context.");
            var metadata = ProducedMetadata(graph, new(targets[0].Path, targets[0].Output, targets[0].Framework), item.Metadata.Aliases);
            authored.Add(CompilerReferenceAuthority.Key(targets[0].Path, targets[0].Output, metadata.Aliases, metadata.EmbedInteropTypes));
        }
        return authored.ToImmutable();
    }

    private static NativeCompilerContextCertificate.ProducedReference ProducedMetadata(CompilerGraphIntent graph,
        CompilerGraphTarget target, ImmutableArray<string> declaredAliases)
    {
        var matches = graph.Produced.Where(input => FileSystemPath.Comparer.Equals(input.TargetPath, target.Path)
            && FileSystemPath.Comparer.Equals(input.OutputPath, target.Output)).ToArray();
        if (matches.Length > 1)
            matches = matches.Where(input => input.Aliases.DefaultIfEmpty("global").OrderBy(alias => alias, StringComparer.Ordinal)
                .SequenceEqual(declaredAliases.DefaultIfEmpty("global").OrderBy(alias => alias, StringComparer.Ordinal), StringComparer.Ordinal)).ToArray();
        if (matches.Length != 1)
            throw new PhysicalGraphMismatchException("A certified compiler target has no unique actual task-input metadata.");
        return matches[0];
    }

    private static CompilerGraphTarget ResolveProjectLibrary(RestoredProjectLibrary library, string nativeProjectPath,
        Func<string, string> map, CompilerGraphIntent graph)
    {
        var path = map(Path.GetFullPath(library.ProjectPath, Path.GetDirectoryName(nativeProjectPath)!));
        var candidates = graph.Reachable.Where(target => FileSystemPath.Comparer.Equals(target.Path, path)
            && (library.TargetFramework.Length == 0 || RestoredPackageManifest.SameTargetFramework(library.TargetFramework, target.Framework))).ToArray();
        if (candidates.Length != 1)
            throw new PhysicalGraphMismatchException("A restored project library does not identify one reviewed reachable compiler context.");
        return candidates[0];
    }

    internal static async Task<RestoredCompilerInputAuthority> CaptureAsync(string workspaceId, Solution native,
        Func<string, string> mapToReviewed, IWorkspacePathPolicy policy, RestoreArtifactPhase phase, CancellationToken ct)
    {
        await NativeCompilerContextCertificate.Require(native).AssertCurrentAsync(native, workspaceId, policy, ct).ConfigureAwait(false);
        var graph = EvaluatedBuildGraph.Source(native, ct);
        var redactor = new MsBuildCommandProperties.Redactor(graph.Nodes.SelectMany(node => MsBuildCommandProperties.Append([], node.Globals)).ToArray());
        try { return await CaptureCoreAsync(workspaceId, native, mapToReviewed, policy, phase, graph, redactor, ct).ConfigureAwait(false); }
        catch (Exception failure) { throw redactor.Failure(failure); }
    }

    private static async Task<RestoredCompilerInputAuthority> CaptureCoreAsync(string workspaceId, Solution native,
        Func<string, string> mapToReviewed, IWorkspacePathPolicy policy, RestoreArtifactPhase phase,
        EvaluatedBuildGraph.Graph graph, MsBuildCommandProperties.Redactor redactor, CancellationToken ct)
    {
        var projects = ImmutableDictionary.CreateBuilder<string, ProjectInputs>(FileSystemPath.Comparer);
        var identities = ImmutableArray.CreateBuilder<PhysicalInputIdentity>();
        var packageIdentities = ImmutableArray.CreateBuilder<PhysicalInputIdentity>();
        var mutablePackageInputs = ImmutableHashSet.CreateBuilder<string>(FileSystemPath.Comparer);
        var outputs = ImmutableHashSet.CreateBuilder<string>(FileSystemPath.Comparer);
        var nativeIdentities = native.Projects.ToImmutableDictionary(candidate => candidate.Id,
            candidate => PhysicalProjectIdentity.Key(candidate, mapToReviewed));
        foreach (var project in native.Projects)
        {
            ct.ThrowIfCancellationRequested();
            var node = EvaluatedBuildGraph.Resolve(project, graph);
            var declarations = MsBuildMetadataHelper.EvaluateCompilerDeclarations(node.ProjectPath, node.Globals);
            var manifest = RestoredPackageManifest.Empty;
            IReadOnlyList<string> packageRoots = [];
            var installedProof = InstalledPackageAuthority.Merge([]);
            var imports = ImmutableHashSet.CreateBuilder<string>(FileSystemPath.Comparer);
            if (node.Inputs.AssetsPath is { } assets)
            {
                var generated = new List<string> { assets };
                if (node.Inputs.ExtensionsPath is { } extensions)
                {
                    generated.Add(Path.Combine(extensions, Path.GetFileName(node.ProjectPath) + ".nuget.g.props"));
                    generated.Add(Path.Combine(extensions, Path.GetFileName(node.ProjectPath) + ".nuget.g.targets"));
                    generated.Add(Path.Combine(extensions, Path.GetFileName(node.ProjectPath) + ".nuget.dgspec.json"));
                    imports.UnionWith(generated.Where(path => path.EndsWith(".props", StringComparison.OrdinalIgnoreCase)
                        || path.EndsWith(".targets", StringComparison.OrdinalIgnoreCase)));
                }
                await policy.ValidateAsync(workspaceId, generated, ct).ConfigureAwait(false);
                byte[]? bytes = await FileSnapshotCapture.ReadIfPresentAsync(assets, PhysicalInputReader.ReadAllBytesAsync, ct).ConfigureAwait(false);
                identities.Add(new(assets, bytes is not null, bytes is null ? [] : [.. SHA256.HashData(bytes)]));
                if (bytes is not null)
                {
                    try
                    {
                        using var document = JsonDocument.Parse(bytes);
                        if (document.RootElement.ValueKind != JsonValueKind.Object)
                            throw new PublicInvalidOperationException("Restore assets have no object-shaped compiler provenance.");
                        var installed = await BuildInputProvenance.ReadPackagesAsync(assets, bytes, graph.Nodes.Select(item => Path.GetDirectoryName(item.ProjectPath)!).ToArray(), (paths, token) => policy.ValidateAsync(workspaceId, paths, token), ct).ConfigureAwait(false);
                        installedProof = installed;
                        packageRoots = installed.Files;
                        packageIdentities.AddRange(installed.Inputs);
                        mutablePackageInputs.UnionWith(installed.RequiresCurrentGrant);
                        var compileTarget = ProjectMetadataParser.TryGetAssetsTarget(document.RootElement, node.Inputs, false, out _);
                        var runtimeTarget = ProjectMetadataParser.TryGetAssetsTarget(document.RootElement, node.Inputs, true, out _);
                        var complete = compileTarget && runtimeTarget;
                        if (!complete && phase == RestoreArtifactPhase.AfterRestore)
                            throw ArtifactFailure("SelectedTargetMissing", native.ProjectIds.Count, node.Inputs.Packages.Count, true,
                                compileTarget, runtimeTarget);
                        if (complete || phase == RestoreArtifactPhase.AfterRestore)
                            manifest = RestoredPackageManifest.Read(document.RootElement, node.Inputs,
                                declarations.ContentPreprocessorDirectory, project.Language, installed, ct);
                    }
                    catch (JsonException) { throw new PublicInvalidOperationException("Restore assets are not valid compiler-input provenance."); }
                }
                else if (phase == RestoreArtifactPhase.AfterRestore && node.Inputs.Packages.Count > 0)
                    throw ArtifactFailure("AssetsMissing", native.ProjectIds.Count, node.Inputs.Packages.Count, false, false, false);
                foreach (var path in generated) outputs.Add(mapToReviewed(path));
                foreach (var path in generated.Where(path => !FileSystemPath.Comparer.Equals(path, assets)))
                    identities.Add(await PhysicalInputIdentity.CaptureAsync(path, ct).ConfigureAwait(false));
            }
            else if (phase == RestoreArtifactPhase.AfterRestore && node.Inputs.Packages.Count > 0)
                throw ArtifactFailure("AssetsPathUnavailable", native.ProjectIds.Count, node.Inputs.Packages.Count, false, false, false);
            var packageImports = declarations.Imports.Where(manifest.PackageFiles.Contains).Distinct(FileSystemPath.Comparer).ToArray();
            var generatedConfig = EvaluatedProjectGraph.GeneratedEditorConfigPath(node.ProjectPath, node.Globals);
            var consumedPackages = project.MetadataReferences.Select(reference => reference.Display).OfType<string>().Where(manifest.Compile.Contains)
                .Concat(project.AnalyzerReferences.Select(reference => reference.FullPath).OfType<string>().Where(manifest.AnalyzerCandidates.Contains))
                .Concat(SolutionFileMutationPlanner.AllDocuments(project).Select(document => document.FilePath).OfType<string>()
                    .Where(path => manifest.PackageFiles.Contains(path) || manifest.ContentActions.ContainsKey(path)
                        || FileSystemPath.Comparer.Equals(path, generatedConfig)))
                .Concat(packageImports).Distinct(FileSystemPath.Comparer).ToArray();
            if (project.CompilationOptions?.CryptoKeyFile is { Length: > 0 } keyFile)
                consumedPackages = [.. consumedPackages, Path.GetFullPath(keyFile, Path.GetDirectoryName(node.ProjectPath)!)];
            // Approved, hash-bound assets confer only exact immutable artifact authority.
            // Authored project identities retain mutable-root policy even if a package cache overlaps them.
            var authored = native.Projects.Select(item => item.FilePath).Append(native.FilePath).OfType<string>()
                .Concat(declarations.Items.Where(item => !packageImports.Contains(item.DefiningProject, FileSystemPath.Comparer)
                    && !imports.Contains(item.DefiningProject)).Select(item => item.Path))
                .ToHashSet(FileSystemPath.Comparer);
            var authoredRoots = native.Projects.Select(item => item.FilePath).OfType<string>()
                .Select(path => Path.GetDirectoryName(mapToReviewed(path))!).Distinct(FileSystemPath.Comparer).ToArray();
            var reviewedPackageRoots = packageRoots.Select(mapToReviewed).Distinct(FileSystemPath.Comparer).ToArray();
            var reviewedPackageProvenance = new ImmutablePathProvenance([], reviewedPackageRoots, authoredRoots);
            var mutable = consumedPackages.Where(path => authored.Contains(path)
                || !(manifest.PackageFiles.Contains(path) || manifest.Compile.Contains(path)
                    || manifest.AnalyzerCandidates.Contains(path) || packageImports.Contains(path, FileSystemPath.Comparer))
                || !reviewedPackageProvenance.IsImmutable(mapToReviewed(path))).ToArray();
            mutablePackageInputs.UnionWith(mutable);
            await policy.ValidateAsync(workspaceId, mutable, ct).ConfigureAwait(false);
            var importIdentities = ImmutableArray.CreateBuilder<PhysicalInputIdentity>();
            foreach (var path in consumedPackages)
            {
                var identity = await PhysicalInputIdentity.CaptureAsync(path, ct).ConfigureAwait(false);
                packageIdentities.Add(identity);
                if (packageImports.Contains(path, FileSystemPath.Comparer)) importIdentities.Add(identity);
            }
            var graphIntent = GraphIntent(node, mapToReviewed, project);
            projects.Add(PhysicalProjectIdentity.Key(project, mapToReviewed), new(manifest, declarations,
                generatedConfig, imports.ToImmutable(), importIdentities.ToImmutable(), mapToReviewed, node.ProjectPath,
                graphIntent, graphIntent, nativeIdentities, installedProof));
        }
        var result = new RestoredCompilerInputAuthority(projects.ToImmutable(), Coalesce(identities), Coalesce(packageIdentities),
            mutablePackageInputs.ToImmutable(), outputs.ToImmutable(), [redactor]);
        await result.AssertCurrentAsync(workspaceId, policy, ct).ConfigureAwait(false);
        return result;
    }

    internal RestoredCompilerInputAuthority MergeAddedProjects(RestoredCompilerInputAuthority desiredStageCertificate)
    {
        var added = desiredStageCertificate._projects.Where(pair => !_projects.ContainsKey(pair.Key)).ToArray();
        // A disposed stage supplies parsed evidence, never a live staged-file epoch.
        // Retain only independently observed inputs whose physical identity remains external;
        // an existing CURRENT grant for a custom cache remains required on every later read.
        var detachedPackageInputs = added.SelectMany(pair => pair.Value.Installed.Inputs
            .Where(input => FileSystemPath.Comparer.Equals(input.FilePath, pair.Value.MapPath(input.FilePath)))).ToArray();
        var retained = detachedPackageInputs.Select(input => input.FilePath).ToHashSet(FileSystemPath.Comparer);
        var detachedMutable = added.SelectMany(pair => pair.Value.Installed.RequiresCurrentGrant).Where(retained.Contains);
        var reviewed = _projects.ToBuilder();
        foreach (var (key, value) in desiredStageCertificate._projects)
            reviewed[key] = reviewed.TryGetValue(key, out var before) ? before with { ReviewedGraph = value.ReviewedGraph } : value;
        return new(reviewed.ToImmutable(), Assets, Coalesce(PackageInputs.Concat(detachedPackageInputs)),
            _mutablePackageInputs.Union(detachedMutable),
            NuGetGeneratedInputs.Concat(desiredStageCertificate.NuGetGeneratedInputs).ToImmutableHashSet(FileSystemPath.Comparer),
            _redactors.AddRange(desiredStageCertificate._redactors));
    }

    internal Solution WithPreRestoreCompilerReferences(Solution reviewed, RestoredCompilerInputAuthority desiredStageCertificate)
    {
        var result = reviewed;
        foreach (var owner in reviewed.Projects)
        {
            var key = PhysicalProjectIdentity.Key(owner, static path => path);
            if (!_projects.TryGetValue(key, out var before)) continue; // New contexts retain strict snapshot comparison.
            if (!desiredStageCertificate._projects.TryGetValue(key, out var desired))
                throw new PhysicalGraphMismatchException("A reviewed compiler owner has no staged evaluation context.");
            var oldEdges = BuildReferenceEdges(before.OriginalGraph, before.Manifest.TransitiveProjects, before.NativeProjectPath, before.MapPath);
            var references = owner.ProjectReferences.Where(reference => !WasOriginalImplicit(reviewed, reference, before, oldEdges)).ToList();
            var policy = desired.OriginalGraph.References;
            if (!policy.DisableTransitive && policy.Defaults.ReferenceOutputAssembly)
                foreach (var library in before.Manifest.TransitiveProjects)
                {
                    // Before restore the copied assets are the independently captured old
                    // producer epoch. Policy can suppress/relabel its edges; only successful
                    // restore may replace its library set with newly certified assets.
                    var target = ResolvePreRestoreProjectLibrary(library, desired, before);
                    var targetProject = ReviewedProject(reviewed, target, before);
                    var metadata = ProducedMetadata(desired.OriginalGraph, target, policy.Defaults.Aliases);
                    var edge = new ProjectReference(targetProject.Id, metadata.Aliases, metadata.EmbedInteropTypes);
                    if (!references.Contains(edge)) references.Add(edge);
                }
            result = result.WithProjectReferences(owner.Id, references);
        }
        return result;
    }

    private static CompilerGraphTarget ResolvePreRestoreProjectLibrary(RestoredProjectLibrary library,
        ProjectInputs desired, ProjectInputs? original)
    {
        var path = desired.MapPath(Path.GetFullPath(library.ProjectPath, Path.GetDirectoryName(desired.NativeProjectPath)!));
        if (desired.OriginalGraph.Reachable.Any(target => FileSystemPath.Comparer.Equals(target.Path, path)))
            return ResolveProjectLibrary(library, desired.NativeProjectPath, desired.MapPath, desired.OriginalGraph);
        // Copied old assets may still produce an edge whose authored route was removed.
        // Only that exact old manifest and independently certified context can supply
        // its missing reachability; callers also require the desired native task input.
        if (original is null || !original.Manifest.TransitiveProjects.Contains(library))
            throw new PhysicalGraphMismatchException("A pre-restore compiler edge has no certified original context.");
        var previous = ResolveProjectLibrary(library, original.NativeProjectPath, original.MapPath, original.OriginalGraph);
        if (!FileSystemPath.Comparer.Equals(previous.Path, path))
            throw new PhysicalGraphMismatchException("A pre-restore compiler edge changed its selecting project path.");
        return previous;
    }

    internal Solution WithoutAbsentAssetsImplicitReferences(Solution reviewed, IReadOnlySet<string> missingSelectingAssets,
        RestoredCompilerInputAuthority originalAuthority)
    {
        var result = reviewed;
        foreach (var owner in reviewed.Projects)
        {
            var key = PhysicalProjectIdentity.Key(owner, static path => path);
            if (!_projects.TryGetValue(key, out var proof))
                throw new PhysicalGraphMismatchException("An initial compiler owner has no staged assets provenance.");
            // This prediction belongs only to the executable fork's frozen missing-assets
            // epoch. The old-assets preflight and restored prediction keep their own graphs.
            if (!proof.Installed.AssetInputs.Any(input => missingSelectingAssets.Contains(proof.MapPath(input.FilePath)))) continue;
            originalAuthority._projects.TryGetValue(key, out var original);
            var authored = AuthoredReferenceEdges(proof.OriginalGraph);
            var implicitEdges = new HashSet<string>(StringComparer.Ordinal);
            var policy = proof.OriginalGraph.References;
            if (!policy.DisableTransitive && policy.Defaults.ReferenceOutputAssembly)
                foreach (var library in proof.Manifest.TransitiveProjects)
                {
                    var target = ResolvePreRestoreProjectLibrary(library, proof, original);
                    var metadata = ProducedMetadata(proof.OriginalGraph, target, policy.Defaults.Aliases);
                    implicitEdges.Add(CompilerReferenceAuthority.Key(target.Path, target.Output, metadata.Aliases, metadata.EmbedInteropTypes));
                }
            result = result.WithProjectReferences(owner.Id,
                owner.ProjectReferences.Where(reference =>
                {
                    var referenceKey = CompilerReferenceAuthority.Key(reviewed, reference);
                    return !implicitEdges.Contains(referenceKey) || authored.Contains(referenceKey);
                }));
        }
        return result;
    }

    private static bool WasOriginalImplicit(Solution reviewed, ProjectReference reference, ProjectInputs before,
        CompilerReferenceAuthority.ProjectEdges oldEdges)
    {
        var target = reviewed.GetProject(reference.ProjectId)
            ?? throw new PhysicalGraphMismatchException("A reviewed compiler reference has no target context.");
        var key = before.NativeProjectIdentities.TryGetValue(reference.ProjectId, out var originalIdentity)
            ? originalIdentity : PhysicalProjectIdentity.Key(target, static path => path);
        key += SolutionFileMutationPlanner.ReferenceMetadataKey(reference.Aliases, reference.EmbedInteropTypes);
        return oldEdges.Implicit.Contains(key) && !oldEdges.Authored.Contains(key);
    }

    private static Project ReviewedProject(Solution reviewed, CompilerGraphTarget target, ProjectInputs before)
    {
        var key = PhysicalProjectIdentity.Key(target.Path, target.Output, static path => path);
        var originalIds = before.NativeProjectIdentities.Where(pair => FileSystemPath.Comparer.Equals(pair.Value, key)).Select(pair => pair.Key).ToArray();
        var candidates = originalIds.Select(reviewed.GetProject).OfType<Project>().ToArray();
        if (candidates.Length == 0)
            candidates = reviewed.Projects.Where(project => FileSystemPath.Comparer.Equals(PhysicalProjectIdentity.Key(project, static path => path), key)).ToArray();
        if (candidates.Length != 1)
            throw new PhysicalGraphMismatchException("A certified compiler edge has no unique reviewed project context.");
        return candidates[0];
    }

    internal async Task AssertCurrentAsync(string workspaceId, IWorkspacePathPolicy policy, CancellationToken ct)
    {
        try
        {
            await policy.ValidateAsync(workspaceId, Assets.Select(input => input.FilePath).Concat(_mutablePackageInputs).ToArray(), ct).ConfigureAwait(false);
            await PhysicalInputIdentity.AssertCurrentAsync(Assets, ct).ConfigureAwait(false);
            await PhysicalInputIdentity.AssertCurrentAsync(PackageInputs, ct).ConfigureAwait(false);
        }
        catch (Exception failure) { throw SanitizeFailure(failure); }
    }

    internal Exception SanitizeFailure(Exception failure)
    {
        foreach (var redactor in _redactors) failure = redactor.Failure(failure);
        return failure;
    }

    /// <summary>Called only after a successful restore. Every other frozen file remains exact.</summary>
    internal async Task<ImmutableArray<PhysicalInputIdentity>> AdvanceGeneratedOutputEpochAsync(string workspaceId,
        IReadOnlyList<PhysicalInputIdentity> previous, Func<string, string> actualToReviewed,
        IReadOnlySet<string> protectedReviewedWritePaths, IWorkspacePathPolicy policy, CancellationToken ct)
    {
        try
        {
            return await AdvanceGeneratedOutputEpochCoreAsync(workspaceId, previous, actualToReviewed,
            protectedReviewedWritePaths, policy, ct).ConfigureAwait(false);
        }
        catch (Exception failure) { throw SanitizeFailure(failure); }
    }

    private async Task<ImmutableArray<PhysicalInputIdentity>> AdvanceGeneratedOutputEpochCoreAsync(string workspaceId,
        IReadOnlyList<PhysicalInputIdentity> previous, Func<string, string> actualToReviewed,
        IReadOnlySet<string> protectedReviewedWritePaths, IWorkspacePathPolicy policy, CancellationToken ct)
    {
        var produced = Assets.ToDictionary(input => input.FilePath, FileSystemPath.Comparer);
        var retained = previous.Where(input => !NuGetGeneratedInputs.Contains(actualToReviewed(input.FilePath))).ToArray();
        if (Assets.Any(input => protectedReviewedWritePaths.Contains(actualToReviewed(input.FilePath))))
            throw new PublicInvalidOperationException("A restore output overlaps a reviewed physical write; its authored bytes cannot be replaced by restore.");
        await policy.ValidateAsync(workspaceId, retained.Select(input => input.FilePath).ToArray(), ct).ConfigureAwait(false);
        await PhysicalInputIdentity.AssertCurrentAsync(retained, ct).ConfigureAwait(false);
        await AssertCurrentAsync(workspaceId, policy, ct).ConfigureAwait(false);
        return [.. retained, .. produced.Values];
    }

    internal static Task<RestoredCompilerInputAuthority> RefreshAfterSuccessfulRestoreAsync(string workspaceId,
        PhysicalGraphCandidate native, Func<string, string> mapToReviewed, IWorkspacePathPolicy policy,
        bool finalInvocation, CancellationToken ct) => CaptureAsync(workspaceId, native.Solution, mapToReviewed,
            policy, finalInvocation ? RestoreArtifactPhase.AfterRestore : RestoreArtifactPhase.BeforeRestore, ct);

    internal async Task<Solution> WithRefreshedInputsAsync(string workspaceId, IWorkspacePathPolicy policy,
        Solution expectedReviewed, PhysicalGraphCandidate actualCandidate,
        RestoredCompilerInputAuthority refreshed, Func<string, string> reviewedToActual,
        Func<string, string> actualToReviewed, IReadOnlySet<string> protectedReviewedWritePaths,
        IReadOnlySet<string> pathProperties, CancellationToken ct)
    {
        try
        {
            return await WithRefreshedInputsCoreAsync(workspaceId, policy, expectedReviewed, actualCandidate,
            refreshed, reviewedToActual, actualToReviewed, protectedReviewedWritePaths, pathProperties, ct).ConfigureAwait(false);
        }
        catch (Exception failure) { throw refreshed.SanitizeFailure(SanitizeFailure(failure)); }
    }

    private async Task<Solution> WithRefreshedInputsCoreAsync(string workspaceId, IWorkspacePathPolicy policy,
        Solution expectedReviewed, PhysicalGraphCandidate actualCandidate,
        RestoredCompilerInputAuthority refreshed, Func<string, string> reviewedToActual,
        Func<string, string> actualToReviewed, IReadOnlySet<string> protectedReviewedWritePaths,
        IReadOnlySet<string> pathProperties, CancellationToken ct)
    {
        var actualReloaded = actualCandidate.Solution;
        var result = expectedReviewed;
        foreach (var expected in expectedReviewed.Projects)
        {
            ct.ThrowIfCancellationRequested();
            var key = PhysicalProjectIdentity.Key(expected, static path => path);
            var actual = actualReloaded.Projects.SingleOrDefault(project => FileSystemPath.Comparer.Equals(
                PhysicalProjectIdentity.Key(project, actualToReviewed), key))
                ?? throw new PhysicalGraphMismatchException("The restored graph has no matching compiler-input context.");
            if (!_projects.TryGetValue(key, out var before) || !refreshed._projects.TryGetValue(key, out var after))
                throw new PhysicalGraphMismatchException("A restored compiler context has no pre/post assets certification.");
            bool IsDerived(ProjectInputs proof, string kind, string path)
            {
                var origin = proof.Declarations.Items.Where(item => item.Kind == kind && (FileSystemPath.Comparer.Equals(item.Path, path)
                    || kind == "Reference" && string.Equals(Path.GetFileNameWithoutExtension(item.Include.Split(',')[0]),
                        Path.GetFileNameWithoutExtension(path), StringComparison.OrdinalIgnoreCase))).ToArray();
                // Authored explicit declarations retain exact identity even when their path is a package asset.
                bool PackageOrigin(string definition) => proof.PackageImports.Any(input => FileSystemPath.Comparer.Equals(input.FilePath, definition));
                if (origin.Any(item => !PackageOrigin(item.DefiningProject)
                    && !proof.NuGetImports.Contains(item.DefiningProject))) return false;
                return ManifestDerived(proof, kind, path, origin.Any(item => PackageOrigin(item.DefiningProject)));
            }
            bool ManifestDerived(ProjectInputs proof, string kind, string path, bool packageImport)
                => kind switch
                {
                    "Reference" => proof.Manifest.Compile.Contains(path),
                    "Analyzer" => proof.Manifest.AnalyzerCandidates.Contains(path)
                        || packageImport && proof.Manifest.PackageFiles.Contains(path),
                    _ => proof.Manifest.ContentActions.TryGetValue(path, out var action) && action.Equals(kind, StringComparison.OrdinalIgnoreCase)
                        || packageImport && proof.Manifest.PackageFiles.Contains(path)
                };
            bool Before(string kind, string path) => IsDerived(before, kind, reviewedToActual(path))
                || IsDerivedMapped(before, kind, path);
            bool IsDerivedMapped(ProjectInputs proof, string kind, string reviewedPath)
            {
                IEnumerable<string> candidates = kind switch
                {
                    "Reference" => proof.Manifest.Compile,
                    "Analyzer" => proof.Manifest.AnalyzerCandidates,
                    _ => proof.Manifest.ContentActions.Keys.Concat(proof.Manifest.PackageFiles)
                };
                return candidates.Any(path => FileSystemPath.Comparer.Equals(proof.MapPath(path), reviewedPath) && IsDerived(proof, kind, path));
            }
            HashSet<DocumentId> DerivedDocuments(ProjectInputs proof, Project project, Func<string, string> map)
            {
                var derived = new HashSet<DocumentId>();
                foreach (var group in SolutionFileMutationPlanner.AllDocuments(project).Where(document => document.FilePath is not null)
                    .GroupBy(document => Kind(document) + "|" + document.FilePath, FileSystemPath.Comparer))
                {
                    var documents = group.ToArray();
                    var kind = Kind(documents[0]);
                    var path = documents[0].FilePath!;
                    var origins = proof.Declarations.Items.Where(item => item.Kind == kind
                        && FileSystemPath.Comparer.Equals(map(item.Path), path)).ToArray();
                    bool PackageOrigin(MsBuildMetadataHelper.CompilerDeclaration item) => proof.NuGetImports.Contains(item.DefiningProject)
                        || proof.PackageImports.Any(input => FileSystemPath.Comparer.Equals(input.FilePath, item.DefiningProject));
                    var packageOrigins = origins.Where(PackageOrigin).ToArray();
                    if (packageOrigins.Length > 0 && packageOrigins.Length < origins.Length)
                    {
                        // Two native item origins can share one physical file. Its frozen
                        // text is common, but only package-origin occurrences may refresh.
                        if (origins.Length != documents.Length)
                            throw new PhysicalGraphMismatchException("Mixed compiler document origins have no exact native occurrence correspondence.");
                        for (var index = 0; index < origins.Length; index++)
                            if (PackageOrigin(origins[index]) && ManifestDerived(proof, kind, origins[index].Path,
                                proof.PackageImports.Any(input => FileSystemPath.Comparer.Equals(input.FilePath, origins[index].DefiningProject))))
                                derived.Add(documents[index].Id);
                    }
                    else if (origins.Any(item => IsDerived(proof, kind, item.Path))
                        || origins.Length == 0 && IsDerivedMapped(proof, kind, path))
                        derived.UnionWith(documents.Select(document => document.Id));
                }
                return derived;
            }
            var retainedMetadata = expected.MetadataReferences.Where(reference => reference.Display is not { } path || !Before("Reference", path));
            var derivedMetadata = actual.MetadataReferences.Where(reference => reference.Display is { } path && IsDerived(after, "Reference", path));
            result = result.WithProjectMetadataReferences(expected.Id, retainedMetadata.Concat(derivedMetadata));
            var retainedAnalyzers = expected.AnalyzerReferences.Where(reference => reference.FullPath is not { } path || !Before("Analyzer", path));
            var derivedAnalyzers = actual.AnalyzerReferences.Where(reference => reference.FullPath is { } path && IsDerived(after, "Analyzer", path));
            result = result.WithProjectAnalyzerReferences(expected.Id, retainedAnalyzers.Concat(derivedAnalyzers));
            var refreshedReferences = RefreshedProjectReferences(expectedReviewed, expected, before, after).ToArray();
            var predictedEdges = refreshedReferences.Select(reference => CompilerReferenceAuthority.Key(expectedReviewed, reference)).ToHashSet(StringComparer.Ordinal);
            var nativeEdges = actual.ProjectReferences.Select(reference =>
            {
                var target = actualReloaded.GetProject(reference.ProjectId)
                    ?? throw new PhysicalGraphMismatchException("A native compiler reference has no target context.");
                return PhysicalProjectIdentity.Key(target, actualToReviewed)
                    + SolutionFileMutationPlanner.ReferenceMetadataKey(reference.Aliases, reference.EmbedInteropTypes);
            }).ToHashSet(StringComparer.Ordinal);
            if (!predictedEdges.SetEquals(nativeEdges))
            {
                var lineage = "OriginalDeclared:" + before.OriginalGraph.References.Declared.Length
                    + ",OriginalImplicit:" + before.Manifest.TransitiveProjects.Length
                    + ",ReviewedDeclared:" + before.ReviewedGraph.References.Declared.Length
                    + ",RestoredImplicit:" + after.Manifest.TransitiveProjects.Length
                    + ",Predicted:" + predictedEdges.Count + ",Native:" + nativeEdges.Count
                    + ",Missing:" + predictedEdges.Except(nativeEdges).Count() + ",Extra:" + nativeEdges.Except(predictedEdges).Count();
                var failure = new PhysicalGraphMismatchException("Restored compiler-reference lineage differs from the independently certified prediction (" + lineage + ").");
                failure.Data["CompilerReferenceLineage"] = lineage;
                throw failure;
            }
            result = result.WithProjectReferences(expected.Id, refreshedReferences);
            if (!SameInputs(before.PackageImports, after.PackageImports))
            {
                // Options come from an owned native candidate under the frozen authored/global
                // receipt; direct snapshot option edits were rejected before physical replay.
                var parseOptions = actual.ParseOptions ?? throw new PublicInvalidOperationException(
                    "The restored native project has no supported parse configuration.");
                var compilationOptions = actual.CompilationOptions ?? throw new PublicInvalidOperationException(
                    "The restored native project has no supported compiler configuration.");
                if (!string.IsNullOrEmpty(compilationOptions.CryptoKeyFile))
                    compilationOptions = compilationOptions.WithCryptoKeyFile(actualToReviewed(compilationOptions.CryptoKeyFile));
                result = result.WithProjectParseOptions(expected.Id, parseOptions)
                    .WithProjectCompilationOptions(expected.Id, compilationOptions);
            }
            var beforeDerivedDocuments = DerivedDocuments(before, expected, before.MapPath);
            var afterDerivedDocuments = DerivedDocuments(after, actual, static path => path);
            foreach (var document in SolutionFileMutationPlanner.AllDocuments(expected))
            {
                if (document.FilePath is not { } path || protectedReviewedWritePaths.Contains(path)) continue;
                if (!beforeDerivedDocuments.Contains(document.Id) && !FileSystemPath.Comparer.Equals(path, before.GeneratedConfig is { } config ? before.MapPath(config) : null)) continue;
                result = Remove(result, document);
            }
            foreach (var document in SolutionFileMutationPlanner.AllDocuments(actual))
            {
                if (document.FilePath is not { } path) continue;
                var reviewed = actualToReviewed(path);
                if (protectedReviewedWritePaths.Contains(reviewed) || (!afterDerivedDocuments.Contains(document.Id)
                    && !FileSystemPath.Comparer.Equals(path, after.GeneratedConfig))) continue;
                var id = DocumentId.CreateNewId(expected.Id);
                if (!after.Manifest.PackageFiles.Contains(path) || refreshed._mutablePackageInputs.Contains(path))
                    await policy.ValidateAsync(workspaceId, [path], ct).ConfigureAwait(false);
                var input = refreshed.PackageInputs.SingleOrDefault(input => FileSystemPath.Comparer.Equals(input.FilePath, path))
                    ?? throw new PhysicalGraphMismatchException("A refreshed compiler document has no frozen physical input identity.");
                await PhysicalInputIdentity.AssertCurrentAsync([input], ct).ConfigureAwait(false);
                var text = await document.GetTextAsync(ct).ConfigureAwait(false);
                await PhysicalInputIdentity.AssertCurrentAsync([input], ct).ConfigureAwait(false);
                if (FileSystemPath.Comparer.Equals(path, after.GeneratedConfig))
                    text = SourceText.From(SolutionFileMutationPlanner.RelocateGeneratedConfig(text.ToString(), actualToReviewed, pathProperties),
                        text.Encoding, text.ChecksumAlgorithm);
                result = Kind(document) switch
                {
                    "Compile" => result.AddDocument(id, document.Name, text, document.Folders, reviewed),
                    "AdditionalFiles" => result.AddAdditionalDocument(id, document.Name, text, document.Folders, reviewed),
                    _ => result.AddAnalyzerConfigDocument(id, document.Name, text, document.Folders, reviewed)
                };
                if (document is Document sourceDocument)
                    result = result.WithDocumentSourceCodeKind(id, sourceDocument.SourceCodeKind);
            }
        }
        return result;
    }

    private static CompilerGraphIntent GraphIntent(EvaluatedBuildGraph.Node root, Func<string, string> map, Project owner)
    {
        var seen = new HashSet<EvaluatedBuildGraph.Node>();
        void Visit(EvaluatedBuildGraph.Node node)
        {
            foreach (var dependency in node.Dependencies)
                if (seen.Add(dependency)) Visit(dependency);
        }
        Visit(root);
        var references = root.CompilerReferences;
        var certificate = NativeCompilerContextCertificate.Require(owner.Solution);
        return new(map(root.ProjectPath), references with
        {
            Declared = references.Declared.Select(item => item with
            {
                Path = map(item.Path),
                Targets = certificate.Select(owner, item).Select(target => target with
                {
                    Path = map(target.Path),
                    Output = target.Output is { } output ? map(output) : null
                }).ToImmutableArray()
            }).ToImmutableArray()
        }, seen.Where(node => !node.IsOuterBuild).Select(node => new CompilerGraphTarget(map(node.ProjectPath),
            node.TargetPath is { } output ? map(output) : null, node.Inputs.TargetFramework)).ToImmutableArray(),
            certificate.ReferenceInputs(owner).Select(input => input with
            {
                TargetPath = map(input.TargetPath),
                OutputPath = input.OutputPath is { } output ? map(output) : null
            }).ToImmutableArray());
    }

    private static IEnumerable<ProjectReference> RefreshedProjectReferences(Solution reviewed, Project expected,
        ProjectInputs before, ProjectInputs after)
    {
        static bool MetadataEqual(CompilerProjectReferenceMetadata left, CompilerProjectReferenceMetadata right) =>
            left.ReferenceOutputAssembly == right.ReferenceOutputAssembly && left.EmbedInteropTypes == right.EmbedInteropTypes
            && left.Aliases.SequenceEqual(right.Aliases, StringComparer.Ordinal);
        var intent = before.ReviewedGraph;
        if (intent.References.DisableTransitive != after.OriginalGraph.References.DisableTransitive
            || !MetadataEqual(intent.References.Defaults, after.OriginalGraph.References.Defaults))
            throw new PhysicalGraphMismatchException("Restored compiler project-reference policy differs from the reviewed evaluation.");
        CompilerGraphTarget Resolve(RestoredProjectLibrary library, ProjectInputs proof, CompilerGraphIntent graph)
            => ResolveProjectLibrary(library, proof.NativeProjectPath, proof.MapPath, graph);
        var oldEdges = BuildReferenceEdges(before.OriginalGraph, before.Manifest.TransitiveProjects, before.NativeProjectPath, before.MapPath);
        var result = expected.ProjectReferences.Where(reference => !WasOriginalImplicit(reviewed, reference, before, oldEdges)).ToList();
        if (intent.References.DisableTransitive || !intent.References.Defaults.ReferenceOutputAssembly) return result;
        foreach (var library in after.Manifest.TransitiveProjects)
        {
            var target = Resolve(library, after, intent);
            var candidates = reviewed.Projects.Where(project => FileSystemPath.Comparer.Equals(project.FilePath, target.Path)
                && FileSystemPath.Comparer.Equals(project.OutputFilePath, target.Output)).ToArray();
            if (candidates.Length != 1)
                throw new PhysicalGraphMismatchException("A certified compiler reference has no unique reviewed project/output identity.");
            var metadata = ProducedMetadata(after.OriginalGraph, target, intent.References.Defaults.Aliases);
            var reference = new ProjectReference(candidates[0].Id, metadata.Aliases, metadata.EmbedInteropTypes);
            if (!result.Contains(reference)) result.Add(reference);
        }
        return result;
    }

    private static bool SameInputs(ImmutableArray<PhysicalInputIdentity> left, ImmutableArray<PhysicalInputIdentity> right)
    {
        if (left.Length != right.Length) return false;
        var byPath = right.ToDictionary(item => item.FilePath, FileSystemPath.Comparer);
        return left.All(item => byPath.TryGetValue(item.FilePath, out var match) && item.Exists == match.Exists
            && item.ContentHash.AsSpan().SequenceEqual(match.ContentHash.AsSpan()));
    }

    private static ImmutableArray<PhysicalInputIdentity> Coalesce(IEnumerable<PhysicalInputIdentity> inputs)
    {
        var result = new Dictionary<string, PhysicalInputIdentity>(FileSystemPath.Comparer);
        foreach (var input in inputs)
        {
            if (result.TryGetValue(input.FilePath, out var prior) && (input.Exists != prior.Exists
                || !input.ContentHash.AsSpan().SequenceEqual(prior.ContentHash.AsSpan())))
                throw new PublicInvalidOperationException("Restore artifacts changed while their compiler authority was captured; retry against stable inputs.");
            result[input.FilePath] = input;
        }
        return [.. result.Values];
    }

    private static PublicInvalidOperationException ArtifactFailure(string kind, int contexts, int packages,
        bool exists, bool compileTarget, bool runtimeTarget)
    {
        var failure = new PublicInvalidOperationException("Successful restore did not establish complete active compiler-input assets.");
        failure.Data["RestoreArtifactFailureKind"] = kind;
        failure.Data["RestoreNativeContextCount"] = contexts;
        failure.Data["RestorePackageExpectationCount"] = packages;
        failure.Data["RestoreAssetsExist"] = exists;
        failure.Data["RestoreCompileTargetPresent"] = compileTarget;
        failure.Data["RestoreRuntimeTargetPresent"] = runtimeTarget;
        return failure;
    }

    private static string Kind(TextDocument document) => document is Document ? "Compile"
        : document.Project.GetAdditionalDocument(document.Id) is not null ? "AdditionalFiles" : "EditorConfigFiles";
    private static Solution Remove(Solution solution, TextDocument document) => Kind(document) switch
    {
        "Compile" => solution.RemoveDocument(document.Id),
        "AdditionalFiles" => solution.RemoveAdditionalDocument(document.Id),
        _ => solution.RemoveAnalyzerConfigDocument(document.Id)
    };
}
