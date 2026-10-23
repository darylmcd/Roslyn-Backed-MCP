using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

internal enum NativeCompilerContextAvailability { Certified, InvalidRestoreAssets, DependencyGrantRequired, BuildDependencyGrantRequired, TaskRegistryUnavailable, TaskExecutionUnavailable, AliasedBuildInput, SemanticContextMismatch, AnalyzerImageBindingUnavailable }

/// <summary>Context selection issued only across an owned native load under one frozen input epoch.</summary>
internal sealed partial class NativeCompilerContextCertificate
{
    private static readonly ConditionalWeakTable<Solution, NativeCompilerContextCertificate> Certificates = [];
    private static readonly ConditionalWeakTable<Solution, ProducedReferences> CompilerInputs = [];
    private sealed record ProducedReferences(ImmutableDictionary<ProjectId, ImmutableArray<ProducedReference>> Projects,
        ImmutableArray<CompilerArgumentParseReceipt> Parsing);
    internal sealed record ProducedReference(string TargetPath, string? OutputPath, ImmutableArray<string> Aliases, bool EmbedInteropTypes)
    {
        public override string ToString() => "NativeCompilerReferenceMetadata";
    }
    private abstract record Authority;
    private sealed record CertifiedAuthority(ImmutableArray<PhysicalInputIdentity> Inputs, ImmutableHashSet<string> Mutable,
        ImmutableDictionary<string, ImmutableHashSet<string>> Selected, MsBuildCommandProperties.Redactor Redactor,
        ImmutablePathProvenance Provenance,
        InstalledPackageAuthority Packages, ImmutableArray<SdkExistenceInput> SdkInputs, NativeBuildMembership Membership) : Authority;
    private abstract record LoadEvidence(ImmutableArray<PhysicalInputIdentity> Inputs, ImmutableHashSet<string> CurrentGrants,
        MsBuildCommandProperties.Redactor Redactor, SupplementalPackageReadAuthority SupplementalPackages)
    {
        internal abstract void AssertNativeCurrent(CancellationToken ct);
        internal abstract void AssertSdkCurrent(CancellationToken ct);
        internal abstract bool RequiresCurrentGrant(string path);
    }
    private sealed record EvaluatedLoadEvidence(ImmutableArray<PhysicalInputIdentity> Inputs, ImmutableHashSet<string> CurrentGrants,
        MsBuildCommandProperties.Redactor Redactor, SupplementalPackageReadAuthority SupplementalPackages,
        ImmutableArray<SdkExistenceInput> SdkInputs, NativeBuildMembership Membership, ImmutablePathProvenance Provenance)
        : LoadEvidence(Inputs, CurrentGrants, Redactor, SupplementalPackages)
    {
        internal override void AssertNativeCurrent(CancellationToken ct) => Membership.AssertCurrent(ct);
        internal override void AssertSdkCurrent(CancellationToken ct) => SdkExistenceInput.AssertCurrent(SdkInputs, ct);
        internal override bool RequiresCurrentGrant(string path) => CurrentGrants.Contains(path) || !Provenance.IsImmutable(path);
    }
    // This variant is issued only when native graph discovery actually reports its
    // task registry unavailable. Evaluated fallbacks must retain their graph proof.
    private sealed record RegistryUnavailableLoadEvidence(ImmutableArray<PhysicalInputIdentity> Inputs,
        MsBuildCommandProperties.Redactor Redactor)
        : LoadEvidence(Inputs, Inputs.Select(input => input.FilePath).ToImmutableHashSet(FileSystemPath.Comparer),
            Redactor, SupplementalPackageReadAuthority.None)
    {
        internal override void AssertNativeCurrent(CancellationToken ct) => ct.ThrowIfCancellationRequested();
        internal override void AssertSdkCurrent(CancellationToken ct) => ct.ThrowIfCancellationRequested();
        internal override bool RequiresCurrentGrant(string path) => true;
    }
    private sealed record UnavailableAuthority(NativeCompilerContextAvailability Reason, LoadEvidence Evidence) : Authority;
    // Normalization may only downgrade before publication, under its gate.
    private volatile Authority _authority;
    internal sealed record CompilerContext(ImmutableDictionary<string, string> Globals, string? Framework,
        string ToolsVersion, ImmutableArray<string> Targets)
    {
        public override string ToString() => "NativeSelectedCompilerContext";
    }
    private readonly ImmutableDictionary<string, ImmutableArray<CompilerContext>> _contexts;
    private readonly SemaphoreSlim _normalizationGate = new(1, 1);
    private UnresolvedAnalyzerReferenceStripper.Projection? _issuedProjection;
    private sealed record PreparedProjection(UnresolvedAnalyzerReferenceStripper.Projection Projection,
        ImmutableDictionary<ProjectId, ImmutableArray<ProducedReference>> Produced,
        ImmutableArray<CompilerArgumentParseReceipt> Parsing);
    private PreparedProjection? _preparedProjection;
    internal Solution NativeSolution { get; }
    private NativeCompilerContextCertificate(Solution nativeSolution, Epoch.CertifiedEpoch epoch)
    {
        NativeSolution = nativeSolution;
        _contexts = epoch.Contexts;
        var selected = ImmutableDictionary.CreateBuilder<string, ImmutableHashSet<string>>(FileSystemPath.Comparer);
        foreach (var project in nativeSolution.Projects)
        {
            var key = PhysicalProjectIdentity.Key(project, static path => path);
            var targets = project.ProjectReferences.Select(reference => nativeSolution.GetProject(reference.ProjectId)
                    ?? throw new PublicInvalidOperationException("A native compiler edge has no loaded target context."))
                .Select(target => PhysicalProjectIdentity.Key(target, static path => path)).ToImmutableHashSet(FileSystemPath.Comparer);
            if (selected.TryGetValue(key, out var previous) && !previous.SetEquals(targets))
            {
                _authority = new UnavailableAuthority(NativeCompilerContextAvailability.SemanticContextMismatch,
                    new EvaluatedLoadEvidence(epoch.PublicationInputs, epoch.Mutable, epoch.Redactor,
                        SupplementalPackageReadAuthority.None, epoch.SdkInputs, epoch.Membership, epoch.Provenance));
                return;
            }
            selected[key] = targets;
        }
        _authority = new CertifiedAuthority(epoch.PublicationInputs, epoch.Mutable, selected.ToImmutable(), epoch.Redactor, epoch.Provenance,
            epoch.Packages, epoch.SdkInputs, epoch.Membership);
    }

    private NativeCompilerContextCertificate(Solution nativeSolution, NativeCompilerContextAvailability unavailable, LoadEvidence evidence)
    {
        NativeSolution = nativeSolution; _authority = new UnavailableAuthority(unavailable, evidence);
        _contexts = [];
    }

    internal NativeCompilerContextAvailability Availability => _authority is UnavailableAuthority unavailable
        ? unavailable.Reason : NativeCompilerContextAvailability.Certified;
    private static NativeCompilerContextAvailability RefusalAvailability(Exception failure)
        => WorkspacePathPolicyRefusal.Is(failure, WorkspacePathPolicyRefusalKind.AliasedPath)
            ? NativeCompilerContextAvailability.AliasedBuildInput : NativeCompilerContextAvailability.BuildDependencyGrantRequired;
    internal string RecoveryGuidance => Availability switch
    {
        NativeCompilerContextAvailability.TaskRegistryUnavailable => "The installed MSBuild task-registry metadata cannot be verified. Semantic loading remains available, but compiler-image and physical mutation authority require a supported SDK and a fresh workspace load.",
        NativeCompilerContextAvailability.AnalyzerImageBindingUnavailable => "The native Windows analyzer images cannot be bound to the admitted executable catalog. Semantic loading remains available, but compiler-image and physical mutation authority require a supported analyzer binding profile and a fresh workspace load before any foreign generator or analyzer image is executed. Restore alone cannot establish executable image identity.",
        NativeCompilerContextAvailability.SemanticContextMismatch => "The independently loaded semantic snapshot differs from a native selected compiler context. Semantic loading remains available, but compiler-image and physical mutation authority require matching compiler options, inputs and reference contexts. Load a project with matching explicit evaluation properties or align the reference metadata, then reload.",
        NativeCompilerContextAvailability.TaskExecutionUnavailable => "Native task execution metadata cannot establish the implementation and context that produced a loaded semantic input. Semantic loading remains available; compiler-image and physical mutation authority require a verifiable native task selection and a fresh workspace load.",
        NativeCompilerContextAvailability.DependencyGrantRequired => "The recorded package installation is outside the current dependency input grant. Authorize its exact recorded cache and reload the workspace. Restoring alone does not authorize that cache; do not discard its recorded identity or substitute a default.",
        NativeCompilerContextAvailability.BuildDependencyGrantRequired => "An evaluated build dependency is outside the current input grant. Authorize the actual inherited build-input location through client roots or grants and reload the workspace. This includes MSBuild user-extension configuration when inherited. Restoring alone does not authorize that input; the retained semantic snapshot cannot issue compiler or physical mutation authority.",
        NativeCompilerContextAvailability.AliasedBuildInput => "An evaluated source document uses a filesystem alias. Semantic loading remains available, but compiler-image and physical mutation authority require direct physical input paths and a fresh workspace load.",
        _ => "Restore assets could not establish compiler-context authority. Obtain an explicitly known NuGet package root before repair. If the generated assets file is malformed, remove that exact file first; incremental restore can otherwise leave it unchanged. Regenerate the loaded project or solution's assets with dotnet restore --force --packages <known-root>, then reload the workspace. Do not discard recorded package-root authority or assume a default cache."
    };

    public override string ToString() => $"NativeCompilerContextCertificate {{ Availability = {Availability} }}";

    internal static bool TryGet(Solution solution, out NativeCompilerContextCertificate? certificate)
        => Certificates.TryGetValue(solution, out certificate);

    internal static NativeCompilerContextCertificate Require(Solution solution)
    {
        if (!Certificates.TryGetValue(solution, out NativeCompilerContextCertificate? certificate))
            throw new PublicInvalidOperationException("Compiler context authority requires an independently loaded native solution; recreate the preview from the loaded workspace.");
        _ = certificate.RequireCertified();
        return certificate;
    }

    private CertifiedAuthority RequireCertified() => _authority is CertifiedAuthority certified ? certified
        : throw new PublicInvalidOperationException(RecoveryGuidance);

    internal InstalledPackageAuthority InstalledPackages => RequireCertified().Packages;

    internal static bool IsVerifiedPackageDocument(string path, IReadOnlySet<string> verifiedFiles, IReadOnlyList<string> authoredRoots)
        => verifiedFiles.Contains(path) && !authoredRoots.Any(root => FileSystemPath.Comparer.Equals(root, path)
            || FileSystemPath.IsStrictDescendant(root, path));

    private void BindNormalized(Solution normalized, ImmutableDictionary<ProjectId, ImmutableArray<ProducedReference>> produced,
        ImmutableArray<CompilerArgumentParseReceipt> parsing)
    {
        // Only the owned normalizer calls this after projecting analyzer policy and the
        // upstream omitted metadata bit. No caller-supplied preview enters issuance.
        AssertNormalizationSelection(NativeSolution, normalized);
        NativeCompilerContextCertificate bound = Certificates.GetValue(normalized, _ => this);
        if (!ReferenceEquals(bound, this)) throw InvalidOperationErrors.Internal("Native compiler normalization has conflicting authority.");
        CompilerInputs.Add(normalized, new(produced, parsing));
    }

    internal static void AssertNormalizationSelection(Solution original, Solution normalized)
    {
        if (!original.ProjectIds.SequenceEqual(normalized.ProjectIds))
            throw InvalidOperationErrors.Internal("Native compiler normalization changed project membership.");
        foreach (Project project in original.Projects)
        {
            Project result = normalized.GetProject(project.Id)
                ?? throw InvalidOperationErrors.Internal("Native compiler normalization lost a project.");
            if (project.FilePath != result.FilePath || project.OutputFilePath != result.OutputFilePath
                || project.ProjectReferences.Count() != result.ProjectReferences.Count()
                || !project.ProjectReferences.Zip(result.ProjectReferences).All(pair => pair.First.ProjectId == pair.Second.ProjectId
                    && pair.First.Aliases.SequenceEqual(pair.Second.Aliases, StringComparer.Ordinal)))
            {
                throw InvalidOperationErrors.Internal("Native compiler normalization changed context selection.");
            }
        }
    }

    internal ImmutableArray<ProducedReference> ReferenceInputs(Project owner)
    {
        if (!ReferenceEquals(Require(owner.Solution), this) || !CompilerInputs.TryGetValue(owner.Solution, out ProducedReferences? produced)
            || !produced.Projects.TryGetValue(owner.Id, out ImmutableArray<ProducedReference> references))
        {
            throw new PublicInvalidOperationException("The compiler owner has no independently observed task-input metadata.");
        }

        return references;
    }

    internal async Task<UnresolvedAnalyzerReferenceStripper.Projection> NormalizeAsync(string workspaceId,
        IWorkspacePathPolicy policy, CancellationToken ct)
    {
        await _normalizationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var issued = _issuedProjection
                ?? throw InvalidOperationErrors.Internal("The native compiler projection has not completed owned publication.");
            if (Availability == NativeCompilerContextAvailability.Certified)
                await AssertCurrentAsync(issued.Solution, workspaceId, policy, ct).ConfigureAwait(false);
            else
                await AssertLoadCurrentAsync(issued.Solution, workspaceId, policy,
                    ((UnavailableAuthority)_authority).Evidence.SupplementalPackages, null, ct).ConfigureAwait(false);
            return issued;
        }
        finally { _normalizationGate.Release(); }
    }

    internal async Task PrepareProjectionAsync(string workspaceId, IWorkspacePathPolicy policy, CancellationToken ct)
    {
        await _normalizationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_issuedProjection is not null || _preparedProjection is not null)
                throw InvalidOperationErrors.Internal("The native compiler projection was already prepared or published.");
            _preparedProjection = await NormalizeCoreAsync(workspaceId, policy, ct).ConfigureAwait(false);
        }
        finally { _normalizationGate.Release(); }
    }

    private async Task<PreparedProjection> NormalizeCoreAsync(string workspaceId,
        IWorkspacePathPolicy policy, CancellationToken ct)
    {
        UnresolvedAnalyzerReferenceStripper.Projection projection = UnresolvedAnalyzerReferenceStripper.Project(NativeSolution, ct);
        if (Availability != NativeCompilerContextAvailability.Certified)
        {
            return new(projection, [], []);
        }
        CertifiedAuthority authority = RequireCertified();
        ImmutableArray<CompilerArgumentParseReceipt>.Builder parsingReceipts = ImmutableArray.CreateBuilder<CompilerArgumentParseReceipt>();
        try
        {
            Solution result = projection.Solution;
            var producerEntryVerified = false;
            ImmutableDictionary<ProjectId, ImmutableArray<ProducedReference>>.Builder produced = ImmutableDictionary.CreateBuilder<ProjectId, ImmutableArray<ProducedReference>>();
            foreach (Project project in NativeSolution.Projects)
            {
                ct.ThrowIfCancellationRequested();
                var ownerIdentity = PhysicalProjectIdentity.Key(project, static value => value);
                if (!_contexts.TryGetValue(ownerIdentity, out var contexts) || contexts.IsDefaultOrEmpty)
                    throw new NativeSemanticContextMismatchException(NativeSemanticContextDifference.Context, []);
                foreach (var context in contexts)
                {
                    if (!producerEntryVerified)
                    {
                        await AssertLoadEpochAsync(workspaceId, policy, [], ct).ConfigureAwait(false);
                        producerEntryVerified = true;
                    }
                    CompilerArgumentInputContext parsingContext = CreateParsingContext(workspaceId, project, policy);
                    NativeCompilerTaskInputs.Capture captured;
                    try { captured = await NativeCompilerTaskInputs.ReadAsync(project, context.Globals, context.Framework, context.ToolsVersion, context.Targets, parsingContext, ct).ConfigureAwait(false); }
                    catch (NativeTaskExecutionUnavailableException)
                    {
                        return await PreserveSemanticAsync(NativeCompilerContextAvailability.TaskExecutionUnavailable).ConfigureAwait(false);
                    }
                    parsingReceipts.Add(captured.Parsing);
                    await AssertLoadEpochAsync(workspaceId, policy, [], ct).ConfigureAwait(false);
                    var directory = Path.GetDirectoryName(project.FilePath)
                        ?? throw new PublicInvalidOperationException("The native compiler owner has no physical directory.");
                    var remaining = captured.References.ToList();
                    var references = new List<ProjectReference>();
                    ImmutableArray<ProducedReference>.Builder inputs = ImmutableArray.CreateBuilder<ProducedReference>();
                    foreach (ProjectReference reference in project.ProjectReferences)
                    {
                        Project target = NativeSolution.GetProject(reference.ProjectId)
                            ?? throw new PublicInvalidOperationException("The native compiler edge has no loaded target.");
                        NativeCompilerTaskInputs.Reference[] matches = [.. remaining.Where(input => NativeCompilerTaskInputs.Matches(input, target, reference.Aliases, directory))];
                        if (matches.Length != 1)
                            throw new NativeSemanticContextMismatchException(NativeSemanticContextDifference.References, []);
                        remaining.Remove(matches[0]);
                        references.Add(new(reference.ProjectId, reference.Aliases, matches[0].EmbedInteropTypes));
                        inputs.Add(new(target.FilePath ?? throw new PublicInvalidOperationException("The compiler target has no physical identity."),
                            target.OutputFilePath, reference.Aliases, matches[0].EmbedInteropTypes));
                    }
                    // Every remaining compiler reference must already be a native metadata input.
                    // The producer cannot add an edge or launder a changed assembly reference.
                    IOrderedEnumerable<NativeCompilerTaskInputs.ReferenceIdentity> nativeMetadata = project.MetadataReferences.Select(reference => reference is PortableExecutableReference { FilePath: { } path }
                        ? NativeCompilerTaskInputs.ReferenceKey(path, reference.Properties.Aliases, reference.Properties.EmbedInteropTypes, reference.Properties.Kind, directory)
                        : throw new PublicInvalidOperationException("The native compiler metadata input has no physical identity."))
                        .OrderBy(value => value, NativeCompilerTaskInputs.ReferenceIdentity.Comparer);
                    IOrderedEnumerable<NativeCompilerTaskInputs.ReferenceIdentity> actualMetadata = remaining.Select(input => NativeCompilerTaskInputs.ReferenceKey(input.Path, input.Aliases,
                        input.EmbedInteropTypes, input.Kind, directory)).OrderBy(value => value, NativeCompilerTaskInputs.ReferenceIdentity.Comparer);
                    if (!nativeMetadata.SequenceEqual(actualMetadata, NativeCompilerTaskInputs.ReferenceIdentity.Comparer))
                        throw new NativeSemanticContextMismatchException(NativeSemanticContextDifference.References, []);
                    var currentInputs = inputs.ToImmutable();
                    if (produced.TryGetValue(project.Id, out var previous))
                    {
                        if (!previous.SequenceEqual(currentInputs)
                            || !result.GetProject(project.Id)!.ProjectReferences.SequenceEqual(references))
                            throw new NativeSemanticContextMismatchException(NativeSemanticContextDifference.References, []);
                    }
                    else
                    {
                        if (!references.SequenceEqual(project.ProjectReferences)) result = result.WithProjectReferences(project.Id, references);
                        produced.Add(project.Id, currentInputs);
                    }
                }
            }
            await AssertPublicationInputsCurrentAsync().ConfigureAwait(false);
            return new(projection with { Solution = result }, produced.ToImmutable(), parsingReceipts.ToImmutable());
        }
        catch (NativeSemanticContextMismatchException mismatch)
        {
            parsingReceipts.AddRange(mismatch.Parsing);
            return await PreserveSemanticAsync(NativeCompilerContextAvailability.SemanticContextMismatch).ConfigureAwait(false);
        }
        catch (Exception failure) { throw authority.Redactor.Failure(failure); }

        async Task<PreparedProjection> PreserveSemanticAsync(NativeCompilerContextAvailability reason)
        {
            try
            {
                // Recheck authority before classifying divergence. A revoked input,
                // producer failure or IO error must retain its own redacted failure.
                await AssertPublicationInputsCurrentAsync().ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                _authority = new UnavailableAuthority(reason, CaptureLoadEvidence());
                return new(projection, [], parsingReceipts.ToImmutable());
            }
            catch (Exception failure) { throw authority.Redactor.Failure(failure); }
        }

        Task AssertPublicationInputsCurrentAsync()
            => AssertProjectionInputsCurrentAsync(parsingReceipts.ToImmutable(), workspaceId, policy, ct);
    }

    private async Task AssertProjectionInputsCurrentAsync(ImmutableArray<CompilerArgumentParseReceipt> parsing,
        string workspaceId, IWorkspacePathPolicy policy, CancellationToken ct)
    {
        var authority = RequireCertified();
        try
        {
            await AssertFinalClosureAsync(parsing, [],
                (paths, token) => policy.ValidateAsync(workspaceId, paths, token), ct).ConfigureAwait(false);
        }
        catch (Exception failure) { throw authority.Redactor.Failure(failure); }
    }

    internal ImmutableArray<CompilerProjectReferenceTarget> Select(Project owner, CompilerProjectReferenceDeclaration declaration)
    {
        if (!declaration.Metadata.ReferenceOutputAssembly) return declaration.Targets;
        if (!RequireCertified().Selected.TryGetValue(PhysicalProjectIdentity.Key(owner, static path => path), out ImmutableHashSet<string>? selected))
            throw new PublicInvalidOperationException("A compiler owner has no independently loaded native context.");
        var targets = declaration.Targets.Where(target => selected.Contains(
            PhysicalProjectIdentity.Key(target.Path, target.Output, static path => path))).ToImmutableArray();
        if (targets.Length != 1)
            throw new PublicInvalidOperationException("An authored compiler reference has no unique independently loaded direct context.");
        return targets;
    }

    internal async Task AssertCurrentAsync(Solution solution, Func<IReadOnlyList<string>, CancellationToken, Task> authorizePaths,
        Func<string, CancellationToken, Task<FileContentDigest?>> readDigest, CancellationToken ct)
    {
        CertifiedAuthority authority = RequireCertified();
        try
        {
            var (Authorize, Digest) = BindInputReader(authorizePaths, readDigest, ReadPhysicalDigestAsync);
            SdkExistenceInput.AssertCurrent(authority.SdkInputs, ct);
            await CompilerArgumentParseReceipt.AssertInputsAsync(authority.Inputs, [], [], Authorize, Digest, ct).ConfigureAwait(false);
            if (_analyzerInputs is { } analyzers)
                await analyzers.AssertCurrentAsync(Authorize, Digest, ct).ConfigureAwait(false);
            foreach (CompilerArgumentParseReceipt receipt in RequireParsingReceipts(solution))
            {
                await receipt.AssertCurrentAsync(Authorize, Digest, ct).ConfigureAwait(false);
            }
            await CompilerArgumentParseReceipt.AssertInputsAsync(authority.Inputs, [], [], Authorize, Digest, ct).ConfigureAwait(false);
            await AssertFinalClosureAsync(RequireParsingReceipts(solution), [], authorizePaths, ct).ConfigureAwait(false);
        }
        catch (Exception failure) { throw authority.Redactor.Failure(failure); }
    }

    internal async Task AssertCurrentAsync(Solution solution, string workspaceId, IWorkspacePathPolicy policy, CancellationToken ct)
    {
        var authority = RequireCertified();
        try
        {
            await AssertFinalClosureAsync(RequireParsingReceipts(solution), [],
                (paths, token) => policy.ValidateAsync(workspaceId, paths, token), ct).ConfigureAwait(false);
        }
        catch (Exception failure) { throw authority.Redactor.Failure(failure); }
    }

    private ImmutableArray<CompilerArgumentParseReceipt> RequireParsingReceipts(Solution solution)
    {
        _ = RequireCertified();
        return IssuedParsingReceipts(solution);
    }

    private ImmutableArray<CompilerArgumentParseReceipt> IssuedParsingReceipts(Solution solution)
        => Certificates.TryGetValue(solution, out var issued) && ReferenceEquals(issued, this)
            && CompilerInputs.TryGetValue(solution, out ProducedReferences? produced) ? produced.Parsing
            : throw new PublicInvalidOperationException("Load freshness requires the exact independently normalized native snapshot.");

    private LoadEvidence CaptureLoadEvidence() => _authority switch
    {
        CertifiedAuthority authority => new EvaluatedLoadEvidence(authority.Inputs, authority.Mutable, authority.Redactor,
            SupplementalPackageReadAuthority.None, authority.SdkInputs, authority.Membership, authority.Provenance),
        UnavailableAuthority unavailable => unavailable.Evidence,
        _ => throw InvalidOperationErrors.Internal("Native load evidence is missing.")
    };

    internal Task AssertLoadCurrentAsync(Solution solution, string workspaceId, IWorkspacePathPolicy policy,
        SupplementalPackageReadAuthority supplementalPackages, PhysicalInputClosure? additionalInputs, CancellationToken ct)
        => AssertLoadClosureAsync(IssuedParsingReceipts(solution), workspaceId, policy, supplementalPackages, [], additionalInputs, ct);

    private async Task AssertLoadClosureAsync(ImmutableArray<CompilerArgumentParseReceipt> parsing, string workspaceId,
        IWorkspacePathPolicy policy, SupplementalPackageReadAuthority supplementalPackages,
        IReadOnlyList<string> additionalCurrentGrants, PhysicalInputClosure? additionalInputs, CancellationToken ct)
    {
        var evidence = CaptureLoadEvidence();
        try
        {
            var hidden = parsing.SelectMany(receipt => receipt.CurrentGrantPaths)
                .Concat(_analyzerInputs?.CurrentGrantPaths ?? []);
            var grants = evidence.CurrentGrants.Concat(hidden.Where(evidence.RequiresCurrentGrant))
                .Concat(additionalCurrentGrants).Concat(additionalInputs?.CurrentGrants ?? []).Distinct(FileSystemPath.Comparer).ToArray();
            await supplementalPackages.AuthorizeFinalAsync(workspaceId, policy, grants, ct).ConfigureAwait(false);
            await AssertPhysicalLoadClosureAsync(evidence, parsing, ct).ConfigureAwait(false);
            if (additionalInputs is not null) await additionalInputs.AssertPhysicalCurrentAsync(ct).ConfigureAwait(false);
            await supplementalPackages.AssertPhysicalCurrentAsync(ct).ConfigureAwait(false);
        }
        catch (Exception failure) { throw evidence.Redactor.Failure(failure); }
    }

    private async Task AssertPhysicalLoadClosureAsync(LoadEvidence evidence,
        ImmutableArray<CompilerArgumentParseReceipt> parsing, CancellationToken ct)
    {
        // Trusted native evaluation can itself observe/effect the filesystem, so
        // the hidden byte/directory proofs must follow it without more callbacks.
        evidence.AssertSdkCurrent(ct);
        evidence.AssertNativeCurrent(ct);
        if (_analyzerInputs is { } analyzers)
            await analyzers.AssertPhysicalCurrentAsync(ct).ConfigureAwait(false);
        foreach (var receipt in parsing)
            await receipt.AssertPhysicalCurrentAsync(ImmutableHashSet<string>.Empty, ct).ConfigureAwait(false);
        await PhysicalInputCaptureBatch.AssertCurrentAsync(evidence.Inputs, ct).ConfigureAwait(false);
        evidence.AssertSdkCurrent(ct);
    }

    // Restore supplies its exact transitioned output identities; a policy-grant set
    // never identifies mutable bytes. Hidden parser/analyzer roles remain immutable.
    internal IReadOnlyList<string> FinalRestoreGrantPaths(Solution solution)
    {
        var authority = RequireCertified();
        return authority.Inputs.Select(input => input.FilePath)
            .Concat(RequireParsingReceipts(solution).SelectMany(receipt => receipt.CurrentGrantPaths))
            .Concat(_analyzerInputs?.CurrentGrantPaths ?? [])
            .Where(path => authority.Mutable.Contains(path) || !authority.Provenance.IsImmutable(path))
            .Distinct(FileSystemPath.Comparer).ToArray();
    }

    internal async Task AssertRestorePhysicalCurrentAsync(Solution solution, IReadOnlySet<string> transitionedOutputs, CancellationToken ct)
    {
        var authority = RequireCertified();
        try
        {
            SdkExistenceInput.AssertCurrent(authority.SdkInputs, ct);
            if (_analyzerInputs is { } analyzers)
                await analyzers.AssertPhysicalCurrentAsync(ct).ConfigureAwait(false);
            foreach (var receipt in RequireParsingReceipts(solution))
                await receipt.AssertPhysicalCurrentAsync(ImmutableHashSet<string>.Empty, ct).ConfigureAwait(false);
            await PhysicalInputCaptureBatch.AssertCurrentAsync(authority.Inputs
                .Where(input => !transitionedOutputs.Contains(input.FilePath)).ToImmutableArray(), ct).ConfigureAwait(false);
            SdkExistenceInput.AssertCurrent(authority.SdkInputs, ct);
        }
        catch (Exception failure) { throw authority.Redactor.Failure(failure); }
    }

    internal Task AssertTemporaryPathDisjointAsync(Solution solution, string workspaceId,
        IWorkspacePathPolicy policy, string temporaryPath, IEnumerable<string> additionalNativeTraversalRoots, CancellationToken ct)
    {
        var authority = RequireCertified();
        var receipts = RequireParsingReceipts(solution);
        var (authorize, _, _) = BindInputAuthority((paths, token) => policy.ValidateAsync(workspaceId, paths, token),
            ReadPhysicalAsync, ReadPhysicalDigestAsync);
        return authority.Membership.AssertTemporaryPathDisjointAsync(temporaryPath,
            receipts.SelectMany(receipt => receipt.RecursiveTraversalRoots).Concat(additionalNativeTraversalRoots), authorize, ct);
    }

    internal Task<bool> IsMappedTemporaryPathDisjointAsync(Solution solution, string workspaceId,
        IWorkspacePathPolicy policy, string temporaryPath, Func<string, string> mapToSource,
        IEnumerable<string> additionalSourceTraversalRoots, CancellationToken ct)
    {
        var authority = RequireCertified();
        var roots = authority.Membership.RecordedTraversalRoots
            .Concat(RequireParsingReceipts(solution).SelectMany(receipt => receipt.RecursiveTraversalRoots))
            .Select(mapToSource).Concat(additionalSourceTraversalRoots);
        var (authorize, _, _) = BindInputAuthority((paths, token) => policy.ValidateAsync(workspaceId, paths, token),
            ReadPhysicalAsync, ReadPhysicalDigestAsync);
        return NativeBuildMembership.IsDisjointFromTraversalRootsAsync(temporaryPath, roots, authorize, ct);
    }

    private CompilerArgumentInputContext CreateParsingContext(string workspaceId, Project project, IWorkspacePathPolicy policy)
    {
        (Func<IReadOnlyList<string>, CancellationToken, Task> Authorize, Func<string, CancellationToken, Task<byte[]?>> Read, Func<string, CancellationToken, Task<FileContentDigest?>> Digest) = BindInputAuthority((paths, token) => policy.ValidateAsync(workspaceId, paths, token), ReadPhysicalAsync, ReadPhysicalDigestAsync);
        return CompilerArgumentInputContext.CreateOwnedPhysical(Path.GetDirectoryName(project.FilePath) ?? throw new PublicInvalidOperationException("The native compiler owner has no physical directory."), Authorize, Read, Digest, CompilerArgumentInputContext.CreateStagePath,
            (stage, token) => RequireCertified().Membership.AssertTemporaryPathDisjointAsync(stage, [], Authorize, token));
    }

    internal async Task AuthorizeCurrentInputsAsync(Solution solution, string workspaceId, IWorkspacePathPolicy policy,
        IReadOnlyList<string> paths, CancellationToken ct)
    {
        if (!Certificates.TryGetValue(solution, out var issued) || !ReferenceEquals(issued, this))
            throw new PublicInvalidOperationException("Current input authority requires the exact independently loaded native snapshot.");
        if (Availability != NativeCompilerContextAvailability.Certified)
        {
            await policy.ValidateAsync(workspaceId, paths, ct).ConfigureAwait(false);
            await AssertLoadClosureAsync(IssuedParsingReceipts(solution), workspaceId, policy,
                ((UnavailableAuthority)_authority).Evidence.SupplementalPackages, paths, null, ct).ConfigureAwait(false);
            return;
        }
        var captured = RequireCertified().Inputs.Select(input => input.FilePath)
            .Concat(solution.Projects.SelectMany(project => project.MetadataReferences)
                .OfType<PortableExecutableReference>().Select(reference => reference.FilePath).OfType<string>())
            .ToHashSet(FileSystemPath.Comparer);
        var selected = paths.Where(captured.Contains).ToArray();
        var uncaptured = paths.Where(path => !captured.Contains(path)).ToArray();
        // Untrusted cache coordinates cannot acquire intrinsic authority by being
        // beneath a remembered SDK/package directory.
        await policy.ValidateAsync(workspaceId, uncaptured, ct).ConfigureAwait(false);
        await AssertCurrentAsync(solution, workspaceId, policy, ct).ConfigureAwait(false);
        var (authorize, _, _) = BindInputAuthority((inputs, token) => policy.ValidateAsync(workspaceId, inputs, token),
            ReadPhysicalAsync, ReadPhysicalDigestAsync);
        await authorize(selected, ct).ConfigureAwait(false);
        // Selected-input authorization is itself a callback after AssertCurrent.
        // Unknown cache coordinates retain explicit grants even beneath SDK roots.
        await AssertFinalClosureAsync(RequireParsingReceipts(solution), uncaptured,
            (inputs, token) => policy.ValidateAsync(workspaceId, inputs, token), ct).ConfigureAwait(false);
    }

    private (Func<IReadOnlyList<string>, CancellationToken, Task> Authorize, Func<string, CancellationToken, Task<byte[]?>> Read, Func<string, CancellationToken, Task<FileContentDigest?>> Digest)
        BindInputAuthority(Func<IReadOnlyList<string>, CancellationToken, Task> authorizePaths, Func<string, CancellationToken, Task<byte[]?>> readBytes,
            Func<string, CancellationToken, Task<FileContentDigest?>> readDigest)
    {
        var bytes = BindInputReader(authorizePaths, readBytes, ReadPhysicalAsync);
        var digest = BindInputReader(authorizePaths, readDigest, ReadPhysicalDigestAsync);
        return (bytes.Authorize, bytes.Read, digest.Read);
    }

    private (Func<IReadOnlyList<string>, CancellationToken, Task> Authorize, Func<string, CancellationToken, Task<T?>> Read)
        BindInputReader<T>(Func<IReadOnlyList<string>, CancellationToken, Task> authorizePaths,
            Func<string, CancellationToken, Task<T?>> suppliedRead, Func<string, CancellationToken, Task<T?>> physicalRead) where T : class
    {
        CertifiedAuthority authority = RequireCertified();
        async Task Authorize(IReadOnlyList<string> paths, CancellationToken ct)
        {
            await authorizePaths(paths.Where(path => authority.Mutable.Contains(path) || !authority.Provenance.IsImmutable(path)).ToArray(), ct).ConfigureAwait(false);
            foreach (var path in paths) WorkspaceManager.EnsurePhysicalPath(path);
        }
        async Task<T?> Read(string path, CancellationToken ct)
        {
            await Authorize([path], ct).ConfigureAwait(false);
            return await (authority.Mutable.Contains(path) || !authority.Provenance.IsImmutable(path)
                ? suppliedRead : physicalRead)(path, ct).ConfigureAwait(false);
        }
        return (Authorize, Read);
    }

    private static Task<byte[]?> ReadPhysicalAsync(string path, CancellationToken ct)
        => FileSnapshotCapture.ReadIfPresentAsync(path, PhysicalInputReader.ReadAllBytesAsync, ct);

    private static Task<FileContentDigest?> ReadPhysicalDigestAsync(string path, CancellationToken ct)
        => FileSnapshotCapture.ReadIfPresentAsync(path, PhysicalInputReader.ReadDigestAsync, ct);

    private async Task AssertFinalClosureAsync(ImmutableArray<CompilerArgumentParseReceipt> parsing,
        IReadOnlyList<string> additionalCurrentGrants,
        Func<IReadOnlyList<string>, CancellationToken, Task> authorizePaths, CancellationToken ct)
    {
        var authority = RequireCertified();
        var paths = authority.Mutable.Concat(parsing.SelectMany(receipt => receipt.CurrentGrantPaths)
                .Concat(_analyzerInputs?.CurrentGrantPaths ?? [])
                .Where(path => authority.Mutable.Contains(path) || !authority.Provenance.IsImmutable(path)))
            .Concat(additionalCurrentGrants).Distinct(FileSystemPath.Comparer).ToArray();
        await authorizePaths(paths, ct).ConfigureAwait(false);
        // No policy or supplied-reader callbacks may run after this complete
        // grant batch. Native evaluation precedes the final byte/directory proof.
        await AssertPhysicalLoadClosureAsync(CaptureLoadEvidence(), parsing, ct).ConfigureAwait(false);
    }

    private async Task AssertLoadEpochAsync(string workspaceId, IWorkspacePathPolicy policy,
        ImmutableArray<CompilerArgumentParseReceipt> parsing, CancellationToken ct)
    {
        CertifiedAuthority authority = RequireCertified();
        try
        {
            await policy.ValidateAsync(workspaceId, authority.Mutable.ToArray(), ct).ConfigureAwait(false);
            SdkExistenceInput.AssertCurrent(authority.SdkInputs, ct);
            await PhysicalInputCaptureBatch.AssertCurrentAsync(authority.Inputs, ct).ConfigureAwait(false);
            if (_analyzerInputs is { } analyzers)
            {
                var (Authorize, Digest) = BindInputReader((paths, token) => policy.ValidateAsync(workspaceId, paths, token), ReadPhysicalDigestAsync, ReadPhysicalDigestAsync);
                await analyzers.AssertCurrentAsync(Authorize, Digest, ct).ConfigureAwait(false);
            }
            await policy.ValidateAsync(workspaceId, authority.Mutable.ToArray(), ct).ConfigureAwait(false);
            await PhysicalInputCaptureBatch.AssertCurrentAsync(authority.Inputs, ct).ConfigureAwait(false);
            await AssertFinalClosureAsync(parsing, [],
                (paths, token) => policy.ValidateAsync(workspaceId, paths, token), ct).ConfigureAwait(false);
        }
        catch (Exception failure) { throw authority.Redactor.Failure(failure); }
    }

    internal abstract class Epoch
    {
        internal abstract Task PrepareOutputsAsync(string workspaceId, IWorkspacePathPolicy policy, CancellationToken ct);
        internal abstract Task AssertPreparationCurrentAsync(string workspaceId, IWorkspacePathPolicy policy, CancellationToken ct);
        internal abstract Epoch ReconcileRecaptured(Epoch recaptured, CancellationToken ct);
        internal virtual bool IsEquivalentAdmission(Epoch other, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return false;
        }

        internal abstract Task<NativeCompilerContextCertificate> CompleteAsync(string workspaceId, Solution native,
            IWorkspacePathPolicy policy, CancellationToken ct);

        internal static async Task<Epoch> CaptureAsync(string workspaceId, string path,
            IReadOnlyDictionary<string, string> globals, IWorkspacePathPolicy policy, SupplementalPackageReadAuthority supplementalPackages, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            EvaluatedBuildGraph.Graph graph;
            try { graph = EvaluatedBuildGraph.FromRoots([path], globals, ImmutableDictionary<string, byte[]>.Empty, ct); }
            catch (NativeTaskRegistryUnavailableException)
            {
                return await RegistryUnavailableAsync(workspaceId, path, globals, policy, ct).ConfigureAwait(false);
            }
            var membership = NativeBuildMembership.Capture(graph, ct);
            var sdkInputs = SdkExistenceInput.Capture(graph.Nodes.SelectMany(node => node.PathEvidence.ConditionProbes)
                .Where(SdkExistenceInput.Supports), ct);
            var redactor = new MsBuildCommandProperties.Redactor(graph.Nodes.SelectMany(node => MsBuildCommandProperties.Append([], node.Globals)).ToArray());
            try
            {
                var assets = graph.Nodes.Select(node => node.Inputs.AssetsPath).OfType<string>().Distinct(FileSystemPath.Comparer).ToArray();
                var source = graph.Nodes.Select(node => node.ProjectPath).Append(path).Concat(NativeOutputPreparation.RestoreInputs(graph)).Distinct(FileSystemPath.Comparer).ToArray();
                await policy.ValidateAsync(workspaceId, source, ct).ConfigureAwait(false);
                var inputs = new List<PhysicalInputIdentity>();
                foreach (var input in source) inputs.Add(await PhysicalInputIdentity.CaptureAsync(input, ct).ConfigureAwait(false));
                var authored = graph.Nodes.Select(node => Path.GetDirectoryName(node.ProjectPath)!).Distinct(FileSystemPath.Comparer).ToArray();
                UnavailableEpoch Unavailable(ImmutableArray<PhysicalInputIdentity> safeInputs, NativeCompilerContextAvailability reason,
                    SupplementalPackageReadAuthority supplemental) => new(new EvaluatedLoadEvidence(safeInputs,
                        safeInputs.Select(input => input.FilePath).ToImmutableHashSet(FileSystemPath.Comparer), redactor, supplemental,
                        sdkInputs, membership, new ImmutablePathProvenance(graph.Nodes.SelectMany(node => node.BuildInputs.ImmutableRoots), [], authored)), reason);
                var configurationInputs = new List<PhysicalInputIdentity>();
                foreach (var directory in authored.Append(Path.GetDirectoryName(path)!).Distinct(FileSystemPath.Comparer))
                {
                    PreparedDotnetTestContext configuration = await DotnetTestModeResolver.ResolveAsync(workspaceId, directory, policy, ct).ConfigureAwait(false);
                    configurationInputs.AddRange(configuration.Candidates.Select(candidate => new PhysicalInputIdentity(candidate.FilePath, candidate.Exists, candidate.ContentHash)));
                }
                inputs.AddRange(configurationInputs);
                ImmutableArray<PhysicalInputIdentity> mandatory = PhysicalInputIdentity.MergeConsistent(inputs);
                async Task<(ImmutableArray<PhysicalInputIdentity> Inputs, NativeCompilerContextAvailability Availability)> MandatoryDocumentsAsync(IEnumerable<string> verifiedPackageFiles)
                {
                    var provenance = new ImmutablePathProvenance(graph.Nodes.SelectMany(node => node.BuildInputs.ImmutableRoots), [], authored);
                    var verifiedFiles = verifiedPackageFiles.ToHashSet(FileSystemPath.Comparer);
                    var documents = graph.Nodes.SelectMany(node => node.DocumentFiles)
                        .Where(input => !provenance.IsImmutable(input)
                            && !IsVerifiedPackageDocument(input, verifiedFiles, authored)).Distinct(FileSystemPath.Comparer).ToArray();
                    NativeCompilerContextAvailability availability = NativeCompilerContextAvailability.Certified;
                    var authorized = new List<string>();
                    try
                    {
                        await policy.ValidateAsync(workspaceId, documents, ct).ConfigureAwait(false);
                        authorized.AddRange(documents);
                    }
                    catch (Exception failure) when (WorkspacePathPolicyRefusal.Is(failure, WorkspacePathPolicyRefusalKind.AliasedPath))
                    {
                        availability = NativeCompilerContextAvailability.AliasedBuildInput;
                        // Preserve independently authorized authored identities even when
                        // another document can only participate in native semantic loading.
                        foreach (var document in documents)
                        {
                            try { await policy.ValidateAsync(workspaceId, [document], ct).ConfigureAwait(false); }
                            catch (Exception denied) when (WorkspacePathPolicyRefusal.Is(denied, WorkspacePathPolicyRefusalKind.AliasedPath))
                            { continue; }
                            authorized.Add(document);
                        }
                    }
                    var identities = new List<PhysicalInputIdentity>(mandatory);
                    foreach (var document in authorized)
                    {
                        try { await policy.ValidateAsync(workspaceId, [document], ct).ConfigureAwait(false); }
                        catch (Exception denied) when (WorkspacePathPolicyRefusal.Is(denied, WorkspacePathPolicyRefusalKind.AliasedPath))
                        {
                            availability = NativeCompilerContextAvailability.AliasedBuildInput;
                            continue;
                        }
                        var physical = PhysicalPathResolver.Resolve(document);
                        if (!FileSystemPath.Comparer.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(document)),
                            Path.TrimEndingDirectorySeparator(physical)))
                        {
                            if (availability == NativeCompilerContextAvailability.Certified)
                                availability = NativeCompilerContextAvailability.AliasedBuildInput;
                            continue;
                        }
                        identities.Add(await PhysicalInputIdentity.CaptureAsync(document, ct).ConfigureAwait(false));
                    }
                    return (PhysicalInputIdentity.MergeConsistent(identities), availability);
                }
                InstalledPackageAuthority packages;
                try { packages = await BuildInputProvenance.ReadPackagesAsync(assets, graph.Nodes.Select(node => Path.GetDirectoryName(node.ProjectPath)!).ToArray(), (paths, token) => policy.ValidateAsync(workspaceId, paths, token), ct).ConfigureAwait(false); }
                catch (PublicInvalidOperationException)
                {
                    // Invalid assets permit degraded semantic loading, never physical authority.
                    // Native evaluation remains trusted; our unknown external inputs are not read.
                    (ImmutableArray<PhysicalInputIdentity> Inputs, NativeCompilerContextAvailability Availability) documents = await MandatoryDocumentsAsync([]).ConfigureAwait(false);
                    return Unavailable(documents.Inputs, documents.Availability == NativeCompilerContextAvailability.Certified
                        ? NativeCompilerContextAvailability.InvalidRestoreAssets : documents.Availability, SupplementalPackageReadAuthority.None);
                }
                catch (Exception failure) when (WorkspacePathPolicyRefusal.IsDenied(failure)
                    && failure.Data[InstalledPackageAuthority.DependencyGrantRequiredKey] is true)
                {
                    (ImmutableArray<PhysicalInputIdentity> Inputs, NativeCompilerContextAvailability Availability) documents = await MandatoryDocumentsAsync([]).ConfigureAwait(false);
                    return Unavailable(documents.Inputs, documents.Availability == NativeCompilerContextAvailability.Certified
                        ? WorkspacePathPolicyRefusal.Is(failure, WorkspacePathPolicyRefusalKind.AliasedPath)
                            ? NativeCompilerContextAvailability.AliasedBuildInput : NativeCompilerContextAvailability.DependencyGrantRequired
                        : documents.Availability, SupplementalPackageReadAuthority.None);
                }
                if (packages.AssetInputs.Any(input => !input.Exists)
                    && (graph.Nodes.Any(node => node.Inputs.Packages.Count > 0) || !supplementalPackages.Files.IsEmpty))
                {
                    await supplementalPackages.AssertCurrentAsync(ct).ConfigureAwait(false);
                    (ImmutableArray<PhysicalInputIdentity> Inputs, NativeCompilerContextAvailability Availability) unavailableInputs = await MandatoryDocumentsAsync(packages.Files.Concat(supplementalPackages.Files)).ConfigureAwait(false);
                    await supplementalPackages.AssertCurrentAsync(ct).ConfigureAwait(false);
                    return Unavailable(unavailableInputs.Inputs, unavailableInputs.Availability == NativeCompilerContextAvailability.Certified
                        ? NativeCompilerContextAvailability.InvalidRestoreAssets : unavailableInputs.Availability, supplementalPackages);
                }
                (ImmutableArray<PhysicalInputIdentity> Inputs, NativeCompilerContextAvailability Availability) documentInputs = await MandatoryDocumentsAsync(packages.Files).ConfigureAwait(false);
                mandatory = documentInputs.Inputs;
                if (documentInputs.Availability != NativeCompilerContextAvailability.Certified)
                    return Unavailable(mandatory, documentInputs.Availability, SupplementalPackageReadAuthority.None);
                inputs.AddRange(mandatory);
                inputs.AddRange(packages.Inputs);
                var provenance = new ImmutablePathProvenance(graph.Nodes.SelectMany(node => node.BuildInputs.ImmutableRoots), packages.Files, authored);
                var physical = graph.Nodes.SelectMany(node => node.BuildInputs.Definitions.Concat(node.DocumentFiles).Concat(node.ArtifactFiles).Concat(node.PathEvidence.InputFiles).Concat(node.PathEvidence.OutputFiles)
                        .Concat(node.PathEvidence.ConditionProbes.Where(probe => !SdkExistenceInput.Supports(probe)).Select(probe => probe.Path)))
                    .Append(path).Concat(assets).Distinct(FileSystemPath.Comparer).ToArray();
                var mutable = physical.Where(input => !provenance.IsImmutable(input))
                    .ToHashSet(FileSystemPath.Comparer);
                mutable.UnionWith(packages.RequiresCurrentGrant);
                mutable.UnionWith(configurationInputs.Select(input => input.FilePath));
                await policy.ValidateAsync(workspaceId, mandatory.Select(input => input.FilePath).ToArray(), ct).ConfigureAwait(false);
                var dependencyPaths = mutable.Except(mandatory.Select(input => input.FilePath), FileSystemPath.Comparer).ToArray();
                try
                {
                    await policy.ValidateAsync(workspaceId, dependencyPaths, ct).ConfigureAwait(false);
                    // Dependency authorization may revoke an earlier mandatory grant.
                    // Refresh the complete read closure immediately before its capture.
                    await policy.ValidateAsync(workspaceId, mutable.ToArray(), ct).ConfigureAwait(false);
                }
                catch (Exception failure) when (WorkspacePathPolicyRefusal.IsDenied(failure))
                {
                    // Only dependency denial degrades. A revoked mandatory input must
                    // still propagate its current refusal before any further capture.
                    await policy.ValidateAsync(workspaceId, mandatory.Select(input => input.FilePath).ToArray(), ct).ConfigureAwait(false);
                    // Native evaluation/loading remains trusted and useful for semantic reads.
                    // Our denied dependency is never explicitly probed or hashed, and this
                    // unavailable snapshot can issue no compiler/physical mutation certificate.
                    return Unavailable(mandatory, RefusalAvailability(failure), SupplementalPackageReadAuthority.None);
                }
                inputs.AddRange(await PhysicalInputCaptureBatch.CaptureAsync([.. physical], ct).ConfigureAwait(false));
                ImmutableArray<PhysicalInputIdentity> captured = PhysicalInputIdentity.MergeConsistent(inputs);
                ConditionInputTruth.AssertObserved(graph.Nodes.SelectMany(node => node.PathEvidence.ConditionProbes)
                    .Where(probe => !SdkExistenceInput.Supports(probe)), captured);
                var epoch = new CertifiedEpoch(captured, mutable.ToImmutableHashSet(FileSystemPath.Comparer),
                    EvaluatedExecutionIdentity.Capture(graph, static value => value, BuildPathRoles.PathProperties, ct),
                    redactor, graph, provenance, packages, sdkInputs,
                    mandatory.Concat(packages.Inputs).Select(input => input.FilePath).ToImmutableHashSet(FileSystemPath.Comparer), membership);
                await policy.ValidateAsync(workspaceId, epoch.Mutable.ToArray(), ct).ConfigureAwait(false);
                await PhysicalInputCaptureBatch.AssertCurrentAsync(epoch.Inputs, ct).ConfigureAwait(false);
                SdkExistenceInput.AssertCurrent(epoch.SdkInputs, ct);
                return epoch;
            }
            catch (Exception failure) { throw redactor.Failure(failure); }
        }

        internal static async Task<Epoch> RegistryUnavailableAsync(string workspaceId, string path,
            IReadOnlyDictionary<string, string> globals, IWorkspacePathPolicy policy, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var redactor = new MsBuildCommandProperties.Redactor(MsBuildCommandProperties.Append([], globals));
            try
            {
                var entry = Path.GetFullPath(path);
                await policy.ValidateAsync(workspaceId, [entry], ct).ConfigureAwait(false);
                PhysicalInputIdentity identity = await PhysicalInputIdentity.CaptureAsync(entry, ct).ConfigureAwait(false);
                PreparedDotnetTestContext configuration = await DotnetTestModeResolver.ResolveAsync(workspaceId, Path.GetDirectoryName(entry)!, policy, ct).ConfigureAwait(false);
                ImmutableArray<PhysicalInputIdentity> inputs = PhysicalInputIdentity.MergeConsistent(new[] { identity }.Concat(configuration.Candidates
                    .Select(candidate => new PhysicalInputIdentity(candidate.FilePath, candidate.Exists, candidate.ContentHash))));
                ct.ThrowIfCancellationRequested();
                return new UnavailableEpoch(new RegistryUnavailableLoadEvidence(inputs, redactor), NativeCompilerContextAvailability.TaskRegistryUnavailable);
            }
            catch (Exception failure) { throw redactor.Failure(failure); }
        }

        internal sealed class CertifiedEpoch(ImmutableArray<PhysicalInputIdentity> inputs, ImmutableHashSet<string> mutable,
            EvaluatedExecutionIdentity execution, MsBuildCommandProperties.Redactor redactor,
            EvaluatedBuildGraph.Graph graph,
            ImmutablePathProvenance provenance,
            InstalledPackageAuthority packages, ImmutableArray<SdkExistenceInput> sdkInputs, ImmutableHashSet<string> protectedInputs,
            NativeBuildMembership membership) : Epoch
        {
            internal ImmutableArray<PhysicalInputIdentity> Inputs { get; } = inputs;
            // Private preparation freezes all output slots. Published compiler authority
            // freezes actual input roles; producing an otherwise unused output is not source drift.
            internal ImmutableArray<PhysicalInputIdentity> PublicationInputs { get; } = SelectPublicationInputs(inputs, graph, protectedInputs);

            private static ImmutableArray<PhysicalInputIdentity> SelectPublicationInputs(ImmutableArray<PhysicalInputIdentity> inputs,
                EvaluatedBuildGraph.Graph graph, ImmutableHashSet<string> protectedInputs)
            {
                var inputRoles = NativeOutputPreparation.ProtectedInputPaths(graph, protectedInputs);
                inputRoles.UnionWith(graph.Nodes.SelectMany(node => node.PathEvidence.ConditionProbes.Select(probe => probe.Path)));
                var outputOnly = graph.Nodes.SelectMany(node => node.PathEvidence.OutputFiles)
                    .Where(path => !inputRoles.Contains(path)).ToHashSet(FileSystemPath.Comparer);
                return inputs.Where(input => !outputOnly.Contains(input.FilePath)).ToImmutableArray();
            }

            internal ImmutableHashSet<string> Mutable { get; } = mutable;
            internal MsBuildCommandProperties.Redactor Redactor { get; } = redactor;
            internal ImmutablePathProvenance Provenance { get; } = provenance;
            internal InstalledPackageAuthority Packages { get; } = packages;
            internal ImmutableArray<SdkExistenceInput> SdkInputs { get; } = sdkInputs;
            internal NativeBuildMembership Membership { get; } = membership;
            internal ImmutableDictionary<string, ImmutableArray<CompilerContext>> Contexts { get; } = graph.Nodes.Where(node => !node.IsOuterBuild)
                .GroupBy(node => PhysicalProjectIdentity.Key(node.ProjectPath, node.TargetPath, static value => value), FileSystemPath.Comparer)
                .ToImmutableDictionary(group => group.Key, group => group.Select(node => new CompilerContext(node.Globals, node.TargetFramework,
                    node.TaskRegistrations.ToolsVersion, node.CompilerReferences.NativeCompileTargets)).ToImmutableArray(), FileSystemPath.Comparer);
            private ImmutableArray<PhysicalInputIdentity> _preparedOutputs = [];
            private ImmutableArray<PhysicalInputIdentity> _producerAssemblies = [];
            private readonly HashSet<string> _unverifiedOutputs = new(FileSystemPath.Comparer);
            private bool _compilerIdentityUnavailable;
            internal override bool IsEquivalentAdmission(Epoch other, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                if (other is not CertifiedEpoch candidate || !Membership.EquivalentTo(candidate.Membership)
                    || !Mutable.SetEquals(candidate.Mutable)
                    || !Packages.Files.ToHashSet(FileSystemPath.Comparer).SetEquals(candidate.Packages.Files)
                    || !Packages.RequiresCurrentGrant.ToHashSet(FileSystemPath.Comparer).SetEquals(candidate.Packages.RequiresCurrentGrant)
                    || SdkInputs.Length != candidate.SdkInputs.Length
                    || SdkInputs.Any(input => !candidate.SdkInputs.Any(input.EquivalentTo)))
                {
                    return false;
                }

                PhysicalInputIdentity[] before = [.. Inputs.OrderBy(input => input.FilePath, FileSystemPath.Comparer)];
                PhysicalInputIdentity[] after = [.. candidate.Inputs.OrderBy(input => input.FilePath, FileSystemPath.Comparer)];
                if (before.Length != after.Length) return false;
                for (var index = 0; index < before.Length; index++)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!FileSystemPath.Comparer.Equals(before[index].FilePath, after[index].FilePath)
                        || before[index].Kind != after[index].Kind
                        || !before[index].ContentHash.AsSpan().SequenceEqual(after[index].ContentHash.AsSpan()))
                    {
                        return false;
                    }
                }
                return true;
            }
            internal override async Task AssertPreparationCurrentAsync(string workspaceId, IWorkspacePathPolicy policy, CancellationToken ct)
            {
                await NativeOutputPreparation.AssertInputsAsync(workspaceId, graph, Inputs, protectedInputs, Mutable, policy, ct).ConfigureAwait(false);
                SdkExistenceInput.AssertCurrent(SdkInputs, ct);
                await policy.ValidateAsync(workspaceId, _preparedOutputs.Select(input => input.FilePath).ToArray(), ct).ConfigureAwait(false);
                await policy.ValidateAsync(workspaceId, PreparationGrantPaths, ct).ConfigureAwait(false);
                await AssertPreparationPhysicalCurrentAsync(ct).ConfigureAwait(false);
            }

            internal string[] PreparationGrantPaths => Mutable.Concat(_preparedOutputs.Select(input => input.FilePath))
                .Distinct(FileSystemPath.Comparer).ToArray();

            internal async Task AssertPreparationPhysicalCurrentAsync(CancellationToken ct)
            {
                SdkExistenceInput.AssertCurrent(SdkInputs, ct);
                await NativeOutputPreparation.AssertPhysicalInputsAsync(graph, Inputs, protectedInputs, Mutable, ct).ConfigureAwait(false);
                await PhysicalInputCaptureBatch.AssertCurrentAsync(_preparedOutputs, ct).ConfigureAwait(false);
                await PhysicalInputIdentity.AssertCurrentAsync(_producerAssemblies, ct).ConfigureAwait(false);
                SdkExistenceInput.AssertCurrent(SdkInputs, ct);
            }

            internal override Epoch ReconcileRecaptured(Epoch recaptured, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                if (recaptured is not CertifiedEpoch final) throw new PublicInvalidOperationException("Native preparation lost its captured dependency authority before final loading.");
                var original = Inputs.Select(input => input.FilePath).ToHashSet(FileSystemPath.Comparer);
                var declared = graph.Nodes.SelectMany(node => node.PathEvidence.OutputFiles.Concat(node.PathEvidence.Directories)).ToHashSet(FileSystemPath.Comparer);
                var dependsOnUnverifiedOutput = false;
                foreach (PhysicalInputIdentity input in final.Inputs)
                {
                    ct.ThrowIfCancellationRequested();
                    if (original.Contains(input.FilePath) || declared.Contains(input.FilePath) || NativeOutputPreparation.IsDeclaredOutputDirectory(graph, input.FilePath)
                        || _preparedOutputs.Any(output => FileSystemPath.Comparer.Equals(output.FilePath, input.FilePath)
                            && output.Kind == input.Kind && output.ContentHash.AsSpan().SequenceEqual(input.ContentHash.AsSpan())))
                    {
                        continue;
                    }

                    if (input.Kind == PhysicalInputKind.File && _unverifiedOutputs.Contains(input.FilePath)
                        && final.IsObservedSemanticInput(input.FilePath))
                    {
                        dependsOnUnverifiedOutput = true;
                        continue;
                    }
                    throw new PublicInvalidOperationException("Native preparation introduced an undeclared physical input before final loading.");
                }
                ct.ThrowIfCancellationRequested();
                return dependsOnUnverifiedOutput || _compilerIdentityUnavailable ? new PreparationUnavailableEpoch(this, final) : final;
            }

            private bool IsObservedSemanticInput(string path) => !graph.Nodes.Any(node => node.BuildInputs.Definitions.Contains(path, FileSystemPath.Comparer))
                && !NativeOutputPreparation.RestoreInputs(graph).Contains(path, FileSystemPath.Comparer)
                && graph.Nodes.Any(node => node.DocumentFiles.Concat(node.ArtifactFiles).Concat(node.PathEvidence.InputFiles)
                    .Contains(path, FileSystemPath.Comparer));

            private async Task AssertPhysicalSnapshotCurrentAsync(string workspaceId, IWorkspacePathPolicy policy, CancellationToken ct)
            {
                await policy.ValidateAsync(workspaceId, Mutable.ToArray(), ct).ConfigureAwait(false);
                await PhysicalInputCaptureBatch.AssertCurrentAsync(Inputs, ct).ConfigureAwait(false);
                SdkExistenceInput.AssertCurrent(SdkInputs, ct);
                ct.ThrowIfCancellationRequested();
            }

            internal async Task AssertSnapshotCurrentAsync(string workspaceId, IWorkspacePathPolicy policy, CancellationToken ct)
            {
                await AssertPhysicalSnapshotCurrentAsync(workspaceId, policy, ct).ConfigureAwait(false);
                Membership.AssertCurrent(ct);
                await AssertPhysicalSnapshotCurrentAsync(workspaceId, policy, ct).ConfigureAwait(false);
            }

            private async Task AssertExecutionSnapshotCurrentAsync(string workspaceId, Solution native,
                IWorkspacePathPolicy policy, CancellationToken ct)
            {
                await AssertPhysicalSnapshotCurrentAsync(workspaceId, policy, ct).ConfigureAwait(false);
                // ADR 0029 trusts native evaluation to discover inputs. These two native
                // graph checks grant no explicit image/file read authority; membership
                // and fresh physical proof must both pass before admitting images.
                execution.AssertEquivalent(native, ct);
                ct.ThrowIfCancellationRequested();
                Membership.AssertCurrent(ct);
                await AssertPhysicalSnapshotCurrentAsync(workspaceId, policy, ct).ConfigureAwait(false);
            }
            internal override async Task PrepareOutputsAsync(string workspaceId, IWorkspacePathPolicy policy, CancellationToken ct)
            {
                try
                {
                    await NativeOutputPreparation.AssertInputsAsync(workspaceId, graph, Inputs, protectedInputs, Mutable, policy, ct).ConfigureAwait(false);
                    SdkExistenceInput.AssertCurrent(SdkInputs, ct);
                    foreach (EvaluatedBuildGraph.Node? node in graph.Nodes.Where(node => !node.IsOuterBuild))
                    {
                        ct.ThrowIfCancellationRequested();
                        ImmutableDictionary<string, PhysicalInputIdentity> assemblies = await NativeOutputPreparation.CaptureWriterAssembliesAsync(node, ct).ConfigureAwait(false);
                        _producerAssemblies = PhysicalInputIdentity.MergeConsistent(_producerAssemblies.Concat(assemblies.Values));
                        await AssertPreparationCurrentAsync(workspaceId, policy, ct).ConfigureAwait(false);
                        var (preparation, _) = await NativeCompilerTaskInputs.PrepareOutputsAsync(node.ProjectPath, node.Globals,
                            node.TargetFramework, node.TaskRegistrations.ToolsVersion, node.CompilerReferences.NativeCompileTargets, ct).ConfigureAwait(false);
                        ImmutableArray<NativeOutputProducerObserver.Receipt> receipts = preparation.Writers;
                        _compilerIdentityUnavailable |= preparation.CompilerIdentityUnavailable;
                        var scope = new NativeWriterOutputScope(graph, node, protectedInputs, ct);
                        foreach (NativeOutputProducerObserver.Receipt receipt in receipts)
                        {
                            ct.ThrowIfCancellationRequested();
                            if (scope.TryGetPath(receipt, out var output) && !scope.HasVerifiedImplementation(receipt, assemblies))
                                _unverifiedOutputs.Add(output);
                        }
                        ImmutableArray<PhysicalInputIdentity> produced = await NativeOutputPreparation.AdmitWriterOutputsAsync(workspaceId, graph, node, protectedInputs,
                            assemblies, receipts, policy, ct).ConfigureAwait(false);
                        _preparedOutputs = PhysicalInputIdentity.MergeConsistent(_preparedOutputs.Concat(produced));
                        await AssertPreparationCurrentAsync(workspaceId, policy, ct).ConfigureAwait(false);
                    }
                }
                catch (Exception failure) { throw Redactor.Failure(failure); }
            }
            internal override async Task<NativeCompilerContextCertificate> CompleteAsync(string workspaceId, Solution native,
                IWorkspacePathPolicy policy, CancellationToken ct)
            {
                try
                {
                    await AssertExecutionSnapshotCurrentAsync(workspaceId, native, policy, ct).ConfigureAwait(false);
                    var certificate = new NativeCompilerContextCertificate(native, this);
                    if (certificate.Availability == NativeCompilerContextAvailability.Certified)
                        await certificate.CaptureAdmittedImagesAsync(workspaceId, policy, ct).ConfigureAwait(false);
                    await AssertSnapshotCurrentAsync(workspaceId, policy, ct).ConfigureAwait(false);
                    return certificate;
                }
                catch (Exception failure) { throw Redactor.Failure(failure); }
            }
        }

        private sealed class PreparationUnavailableEpoch(CertifiedEpoch original, CertifiedEpoch final) : Epoch
        {
            internal override async Task AssertPreparationCurrentAsync(string workspaceId, IWorkspacePathPolicy policy, CancellationToken ct)
            {
                await original.AssertPreparationCurrentAsync(workspaceId, policy, ct).ConfigureAwait(false);
                await final.AssertSnapshotCurrentAsync(workspaceId, policy, ct).ConfigureAwait(false);
                await policy.ValidateAsync(workspaceId, original.PreparationGrantPaths.Concat(final.Mutable)
                    .Distinct(FileSystemPath.Comparer).ToArray(), ct).ConfigureAwait(false);
                final.Membership.AssertCurrent(ct);
                await original.AssertPreparationPhysicalCurrentAsync(ct).ConfigureAwait(false);
                await PhysicalInputCaptureBatch.AssertCurrentAsync(final.Inputs, ct).ConfigureAwait(false);
                SdkExistenceInput.AssertCurrent(final.SdkInputs, ct);
            }

            internal override Epoch ReconcileRecaptured(Epoch recaptured, CancellationToken ct) => original.ReconcileRecaptured(recaptured, ct);

            internal override Task PrepareOutputsAsync(string workspaceId, IWorkspacePathPolicy policy, CancellationToken ct)
                => AssertPreparationCurrentAsync(workspaceId, policy, ct);

            internal override async Task<NativeCompilerContextCertificate> CompleteAsync(string workspaceId, Solution native,
                IWorkspacePathPolicy policy, CancellationToken ct)
            {
                try
                {
                    await AssertPreparationCurrentAsync(workspaceId, policy, ct).ConfigureAwait(false);
                    return new(native, NativeCompilerContextAvailability.TaskExecutionUnavailable,
                        new EvaluatedLoadEvidence(final.PublicationInputs, final.Mutable, final.Redactor,
                            SupplementalPackageReadAuthority.None, final.SdkInputs, final.Membership, final.Provenance));
                }
                catch (Exception failure) { throw original.Redactor.Failure(failure); }
            }
        }

        private sealed class UnavailableEpoch(LoadEvidence evidence, NativeCompilerContextAvailability reason) : Epoch
        {
            internal override async Task AssertPreparationCurrentAsync(string workspaceId, IWorkspacePathPolicy policy, CancellationToken ct)
            {
                await evidence.SupplementalPackages.AssertCurrentAsync(ct).ConfigureAwait(false);
                await policy.ValidateAsync(workspaceId, evidence.CurrentGrants.ToArray(), ct).ConfigureAwait(false);
                await PhysicalInputCaptureBatch.AssertCurrentAsync(evidence.Inputs, ct).ConfigureAwait(false);
                await evidence.SupplementalPackages.AssertCurrentAsync(ct).ConfigureAwait(false);
                await evidence.SupplementalPackages.AuthorizeFinalAsync(workspaceId, policy,
                    evidence.CurrentGrants.ToArray(), ct).ConfigureAwait(false);
                await evidence.SupplementalPackages.AssertPhysicalCurrentAsync(ct).ConfigureAwait(false);
                await PhysicalInputCaptureBatch.AssertCurrentAsync(evidence.Inputs, ct).ConfigureAwait(false);
            }

            internal override Epoch ReconcileRecaptured(Epoch recaptured, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                // Degraded loading still binds original mandatory authored inputs;
                // the final epoch independently determines available capabilities.
                return recaptured;
            }
            internal override Task PrepareOutputsAsync(string workspaceId, IWorkspacePathPolicy policy, CancellationToken ct)
                => AssertPreparationCurrentAsync(workspaceId, policy, ct);
            internal override async Task<NativeCompilerContextCertificate> CompleteAsync(string workspaceId, Solution native,
                IWorkspacePathPolicy policy, CancellationToken ct)
            {
                try
                {
                    await AssertPreparationCurrentAsync(workspaceId, policy, ct).ConfigureAwait(false);
                    // The final owned epoch retains native graph/SDK freshness even
                    // when it cannot certify compiler inputs. Preparation above is
                    // deliberately separate from this final membership proof.
                    evidence.AssertNativeCurrent(ct);
                    await PhysicalInputCaptureBatch.AssertCurrentAsync(evidence.Inputs, ct).ConfigureAwait(false);
                    evidence.AssertSdkCurrent(ct);
                    return new(native, reason, evidence);
                }
                catch (Exception failure) { throw evidence.Redactor.Failure(failure); }
            }
        }
    }
}
