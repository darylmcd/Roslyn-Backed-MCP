using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

public sealed partial class WorkspaceManager
{
    Task<IPhysicalRestorationAuthority> IWorkspaceRecoveryAuthority.StageRestorationAsync(
        PhysicalMutationRecovery operation, IReadOnlyList<FileSnapshotDto> desiredFiles, PhysicalGraphLayout? priorLayout, CancellationToken ct)
        => StagePhysicalRestorationCoreAsync(operation, desiredFiles, priorLayout, null, ct);

    Task<IPhysicalRestorationAuthority> IWorkspaceRecoveryAuthority.StageMutationAsync(
        PhysicalMutationRecovery operation, IReadOnlyList<FileSnapshotDto> desiredFiles, CancellationToken ct)
    {
        lock (_mutationLock) AssertMutationIntents(operation, desiredFiles);
        return StagePhysicalRestorationCoreAsync(operation, desiredFiles, operation.CapturedLayout,
            NativeSourceAdmission.ForOperation(operation), ct);
    }

    private void AssertMutationIntents(PhysicalMutationRecovery operation, IReadOnlyList<FileSnapshotDto> desiredFiles)
    {
        RequireActive(operation);
        var state = RequireMutation(operation);
        var paths = desiredFiles.Select(file => file.FilePath).ToHashSet(FileSystemPath.Comparer);
        if (paths.Count != desiredFiles.Count || !paths.SetEquals(operation.Files.Select(file => file.FilePath))
            || desiredFiles.Any(file => !state.WriteIntents.TryGetValue(file.FilePath, out var intent) || !SameSnapshot(intent, file)))
            throw new WorkspaceMutationOwnershipException("Mutation verification requires exactly the operation's registered desired states.");
    }

    private async Task<IPhysicalRestorationAuthority> StagePhysicalRestorationCoreAsync(
        PhysicalMutationRecovery operation, IReadOnlyList<FileSnapshotDto> desiredFiles, PhysicalGraphLayout? priorLayout,
        NativeSourceAdmission? sourceAdmission, CancellationToken ct)
    {
        lock (_mutationLock)
        {
            RequireMutation(operation);
            if (!_recoveries.TryGetValue(operation.WorkspaceId, out var pending) || !ReferenceEquals(pending, operation))
                RequireCurrentOwnership(operation);
            if (operation.HasRetainedSourceTransitions)
                throw new WorkspaceMutationOwnershipException("Source transition custody must be resolved before staging the physical workspace.");
        }
        var session = GetRequiredSession(operation.WorkspaceId);
        var loadedPath = session.LoadedPath ?? throw InvalidOperationErrors.Internal("A physical recovery has no loaded path.");
        var capturedLayout = priorLayout ?? operation.CapturedLayout
            ?? throw InvalidOperationErrors.Internal("A native physical recovery has no captured graph layout.");
        await _pathPolicy.ValidateAsync(operation.WorkspaceId, capturedLayout.Packages.PathsRequiringCurrentGrant(capturedLayout.ContentRoots.Concat(capturedLayout.InputFiles)
            .Concat(CurrentPhysicalPaths(operation.WorkspaceId).Where(path => !capturedLayout.IsImmutablePath(path)))
            .Concat(desiredFiles.Select(file => file.FilePath))
            .Distinct(FileSystemPath.Comparer)), ct).ConfigureAwait(false);
        var globals = session.Workspace?.Properties.ToDictionary(pair => pair.Key, pair => pair.Value)
            ?? throw InvalidOperationErrors.Internal("A native physical recovery has no evaluation properties.");
        var actual = await PhysicalGraphCandidate.LoadAsync(operation.WorkspaceId, loadedPath, globals,
            _pathPolicy, _sessionLoader, _logger, sourceAdmission, ct).ConfigureAwait(false);
        PhysicalGraphStage? stage = null;
        try
        {
            var layout = PhysicalGraphLayout.Union(await PhysicalGraphLayout.CreateAsync(operation.WorkspaceId, loadedPath, actual.Solution, _pathPolicy, ct).ConfigureAwait(false), capturedLayout);
            stage = await PhysicalGraphStage.CreateAsync(operation.WorkspaceId, loadedPath, actual.Solution, layout,
                desiredFiles, globals, _sessionLoader, _pathPolicy,
                ((IWorkspaceEvaluationLoader)this).CaptureStageInvocationAuthority(operation.WorkspaceId,
                    session.Capture()?.Solution ?? throw new WorkspaceMutationOwnershipException("The recovery source closed before staging.")),
                _logger, ct).ConfigureAwait(false);
            stage.EnterReadAuthority();
            var preservesIdentity = true;
            try
            {
                await SolutionFileMutationPlanner.AssertEquivalentAsync(stage.Expected.Solution, operation.PreApplySolution,
                    stage.Expected.MapPath, static path => path, verifyDocuments: true, stage.Expected.PathProperties, ct).ConfigureAwait(false);
            }
            catch (PhysicalGraphMismatchException) { preservesIdentity = false; }
            var owned = desiredFiles.Select(file => file.FilePath).ToHashSet(FileSystemPath.Comparer);
            var unowned = operation.CapturedGraphFiles.Where(file => !owned.Contains(file.FilePath)).ToArray();
            await _pathPolicy.ValidateAsync(operation.WorkspaceId, capturedLayout.Packages.PathsRequiringCurrentGrant(unowned.Select(file => file.FilePath)), ct).ConfigureAwait(false);
            foreach (var captured in unowned)
            {
                var current = await PhysicalInputIdentity.CaptureAsync(captured.FilePath, ct).ConfigureAwait(false);
                if (captured.Exists != current.Exists || !captured.ContentHash.AsSpan().SequenceEqual(current.ContentHash.AsSpan()))
                    preservesIdentity = false;
            }
            var restoration = new PhysicalRestorationAuthority(this, operation, stage, actual, preservesIdentity,
                sourceAdmission is not null, desiredFiles.Select(CanonicalSnapshot).ToImmutableArray());
            lock (_mutationLock) RequireMutation(operation).RestorationAuthorities.Add(restoration);
            return restoration;
        }
        catch (Exception failure)
        {
            OwnedResourceCleanup.Dispose("Staged recovery validation and cleanup failed.", failure, stage, actual);
            throw;
        }
    }

    async Task IWorkspaceRecoveryAuthority.VerifyMutationNamespacesAsync(PhysicalMutationRecovery operation,
        IPhysicalRestorationAuthority restoration, MutationNamespaceTarget target, CancellationToken ct)
    {
        if (restoration is not PhysicalRestorationAuthority issued)
            throw new WorkspaceMutationOwnershipException("Mutation verification requires an exact manager-issued restoration.");
        issued.AssertMutationCurrent(this, operation, ct);
        if (operation.HasRetainedSourceTransitions)
            throw new WorkspaceMutationOwnershipException("Retained source transition custody must be resolved before namespace verification.");
        if (target == MutationNamespaceTarget.CapturedSource)
        {
            await issued.Source.AssertSourceCurrentAsync(operation, _pathPolicy, [], ct).ConfigureAwait(false);
            issued.AssertMutationCurrent(this, operation, ct);
            issued.MarkCapturedSourceVerified();
        }
        else if (target == MutationNamespaceTarget.Desired)
        {
            var session = GetRequiredSession(operation.WorkspaceId);
            var path = session.LoadedPath ?? throw InvalidOperationErrors.Internal("Mutation verification has no loaded path.");
            var globals = session.Workspace?.Properties.ToDictionary(pair => pair.Key, pair => pair.Value)
                ?? throw InvalidOperationErrors.Internal("Mutation verification has no native evaluation properties.");
            PhysicalGraphCandidate? candidate = null;
            Exception? failure = null;
            try
            {
                candidate = await PhysicalGraphCandidate.LoadAsync(operation.WorkspaceId, path, globals,
                    _pathPolicy, _sessionLoader, _logger, null, ct).ConfigureAwait(false);
                var expected = issued.Expected;
                await SolutionFileMutationPlanner.AssertEquivalentAsync(expected.Solution, candidate.Solution,
                    expected.MapPath, static input => input, verifyDocuments: true, expected.PathProperties, ct).ConfigureAwait(false);
                expected.ExecutionIdentity.AssertEquivalent(candidate.Solution, ct);
                await candidate.Context.AssertLoadCurrentAsync(candidate.Solution, operation.WorkspaceId, _pathPolicy,
                    SupplementalPackageReadAuthority.None, PhysicalInputClosure.FromExpectation(expected), ct).ConfigureAwait(false);
                issued.AssertMutationCurrent(this, operation, ct);
            }
            catch (Exception primary) { failure = primary; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Mutation verification candidate cleanup failed.", failure, candidate); }
        }
        else throw new ArgumentOutOfRangeException(nameof(target));
        ct.ThrowIfCancellationRequested();
        issued.AssertMutationCurrent(this, operation, ct);
        lock (_mutationLock)
        {
            AssertMutationIntents(operation, issued.DesiredFiles);
            operation.SourceNamespacesVerified();
        }
    }

    Task IWorkspaceRecoveryAuthority.CommitRestorationAsync(PhysicalMutationRecovery operation,
        PhysicalGraphExpectation expected, bool retainedRecovery, CancellationToken ct)
    {
        lock (_mutationLock)
        {
            RequireMutation(operation);
            if (retainedRecovery)
            {
                if (!_recoveries.TryGetValue(operation.WorkspaceId, out var pending) || !ReferenceEquals(pending, operation))
                    throw new WorkspaceMutationOwnershipException("The recovery no longer owns this workspace.");
            }
            else RequireActive(operation);
        }
        return CommitPhysicalGraphCoreAsync(operation.WorkspaceId, expected, retainedRecovery ? operation : null, ct);
    }

    private sealed class PhysicalRestorationAuthority(WorkspaceManager manager, PhysicalMutationRecovery operation,
        PhysicalGraphStage stage, PhysicalGraphCandidate actual, bool preservesIdentity, bool forwardMutation,
        ImmutableArray<FileSnapshotDto> desiredFiles) : IPhysicalRestorationAuthority
    {
        private bool _disposed;
        public PhysicalGraphExpectation Expected => stage.Expected;
        internal PhysicalGraphLayout Layout => stage.Layout;
        public bool PreservesOriginalIdentity { get; } = preservesIdentity;
        internal PhysicalGraphCandidate Source => actual;
        internal ImmutableArray<FileSnapshotDto> DesiredFiles => desiredFiles;
        internal bool CapturedSourceVerified { get; private set; }
        internal void MarkCapturedSourceVerified() => CapturedSourceVerified = true;
        public async Task AssertSourceBeforeEffectAsync(PhysicalMutationRecovery mutation,
            IReadOnlyList<string> sourceCoordinates, CancellationToken ct)
        {
            AssertMutationCurrent(manager, mutation, ct);
            await actual.AssertSourceCurrentAsync(mutation, manager._pathPolicy, sourceCoordinates, ct).ConfigureAwait(false);
            AssertMutationCurrent(manager, mutation, ct);
        }
        internal void AssertMutationCurrent(WorkspaceManager owner, PhysicalMutationRecovery mutation, CancellationToken ct)
        {
            AssertMutationAssociation(owner, mutation);
            stage.AssertCustodyObservationCurrent(ct);
        }
        internal void AssertMutationAssociation(WorkspaceManager owner, PhysicalMutationRecovery mutation)
        {
            if (!ReferenceEquals(owner, manager) || !ReferenceEquals(mutation, operation) || !forwardMutation)
                throw new WorkspaceMutationOwnershipException("The restoration is not this operation's issued mutation verification.");
            lock (manager._mutationLock)
            {
                if (_disposed || !manager.RequireMutation(operation).RestorationAuthorities.Contains(this))
                    throw new WorkspaceMutationOwnershipException("The mutation verification no longer belongs to this operation.");
                manager.AssertMutationIntents(operation, desiredFiles);
            }
        }
        internal void AssertCurrent(CancellationToken ct)
        {
            lock (manager._mutationLock)
                if (_disposed || !manager.RequireMutation(operation).RestorationAuthorities.Contains(this))
                    throw new WorkspaceMutationOwnershipException("The desired custody observation no longer belongs to this operation.");
            stage.AssertCustodyObservationCurrent(ct);
        }
        public void Dispose()
        {
            lock (manager._mutationLock)
            {
                if (_disposed) return;
                _disposed = true;
                if (manager._mutationStates.TryGetValue(operation, out var state)) state.RestorationAuthorities.Remove(this);
            }
            OwnedResourceCleanup.Dispose("Physical restoration resource cleanup failed.", null, stage, actual);
        }
    }
    private readonly object _mutationLock = new();
    private readonly Dictionary<object, long> _mutationEpochs = new(ReferenceEqualityComparer.Instance);
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<PhysicalMutationRecovery, MutationState> _mutationStates = new();
    private readonly Dictionary<string, PhysicalMutationRecovery> _activeMutations = new(StringComparer.Ordinal);

    private sealed class MutationState(long capturedEpoch)
    {
        internal long CapturedEpoch { get; } = capturedEpoch;
        internal long? CompletedEpoch { get; set; }
        internal Solution? PostSolution { get; set; }
        internal ImmutableArray<FileSnapshotDto> PostFiles { get; set; } = [];
        internal bool PostStateConflict { get; set; }
        internal Dictionary<string, FileSnapshotDto> WriteIntents { get; } = new(FileSystemPath.Comparer);
        internal HashSet<PhysicalRestorationAuthority> RestorationAuthorities { get; } = [];
    }

    private void AdvanceMutationEpoch(object session)
    {
        lock (_mutationLock) _mutationEpochs[session] = _mutationEpochs.GetValueOrDefault(session) + 1;
    }

    void IWorkspaceRecoveryAuthority.EnsureMutationAllowed(string workspaceId, IReadOnlyList<string> paths) => EnsureMutationAllowed(workspaceId, paths);

    private void EnsureMutationAllowed(string workspaceId, IReadOnlyList<string> paths)
    {
        var owned = paths.Select(Path.GetFullPath).ToHashSet(FileSystemPath.Comparer);
        lock (_mutationLock)
            if (_recoveries.Values.Concat(_activeMutations.Values).Any(operation => operation.WorkspaceId == workspaceId
                || operation.Files.Any(file => owned.Contains(Path.GetFullPath(file.FilePath)))))
                throw new PublicInvalidOperationException("Recover the failed physical operation through user undo, or finish its active operation, before mutating these files or workspace.");
    }

    private IReadOnlyList<string> CurrentPhysicalPaths(string workspaceId)
    {
        var solution = GetCurrentSolution(workspaceId);
        return solution.Projects.Select(project => project.FilePath).Append(solution.FilePath)
            .Concat(solution.Projects.SelectMany(SolutionFileMutationPlanner.AllDocuments).Select(document => document.FilePath))
            .OfType<string>().ToArray();
    }

    private static bool MatchesFiles(IEnumerable<FileSnapshotDto> files)
    {
        foreach (var file in files)
        {
            byte[]? bytes;
            try { bytes = PhysicalInputReader.ReadAllBytes(file.FilePath); }
            catch (FileNotFoundException) { bytes = null; }
            catch (DirectoryNotFoundException) { bytes = null; }
            if (file.OriginalText is null ? bytes is not null : bytes is null || (!file.OriginalBytes.IsDefault
                ? !file.OriginalBytes.AsSpan().SequenceEqual(bytes)
                : file.OriginalText != CsprojSemanticEquality.CreateSnapshot(bytes).Content)) return false;
        }
        return true;
    }

    async Task<PhysicalMutationRecovery> IWorkspaceRecoveryAuthority.CaptureRecoveryAsync(string workspaceId, string description, IReadOnlyList<FileSnapshotDto> beforeFiles, CancellationToken ct)
    {
        var normalized = beforeFiles.Select(CanonicalSnapshot).ToArray();
        if (normalized.Select(file => file.FilePath).Distinct(FileSystemPath.Comparer).Count() != normalized.Length)
            throw InvalidOperationErrors.Internal("Physical operation ownership must not contain duplicate paths.");
        EnsureMutationAllowed(workspaceId, beforeFiles.Select(file => file.FilePath).ToArray());
        var knownSolution = GetCurrentSolution(workspaceId);
        var knownPaths = knownSolution.Projects.Select(project => project.FilePath).Append(knownSolution.FilePath)
            .OfType<string>().Concat(beforeFiles.Select(file => file.FilePath)).Distinct(FileSystemPath.Comparer).ToArray();
        await AuthorizePhysicalPathsAsync(workspaceId, knownPaths, ct).ConfigureAwait(false);
        foreach (var file in beforeFiles) EnsurePhysicalPath(file.FilePath);
        if (!MatchesFiles(normalized))
            throw new PublicInvalidOperationException("Source bytes changed before recovery preparation; reload before applying changes.");
        var files = normalized.Select(file => file.OriginalText is null ? new FileSnapshotDto(file.FilePath, null)
            : !file.OriginalBytes.IsDefault ? file : FileSnapshotDto.FromExistingBytes(file.FilePath, PhysicalInputReader.ReadAllBytes(file.FilePath))).ToImmutableArray();
        var session = GetRequiredSession(workspaceId);
        var solution = GetCurrentSolution(workspaceId);
        var layout = await PhysicalGraphLayout.CreateAsync(workspaceId, session.LoadedPath
            ?? throw InvalidOperationErrors.Internal("A physical operation has no loaded path."), solution, _pathPolicy, ct).ConfigureAwait(false);
        var graphPaths = CurrentPhysicalPaths(workspaceId).Concat(layout.DefinitionFiles).Concat(layout.ConditionInputs.Select(input => input.FilePath))
            .Concat(solution.Projects.SelectMany(project => project.MetadataReferences).OfType<PortableExecutableReference>()
                .Select(reference => reference.FilePath).OfType<string>())
            .Concat(solution.Projects.SelectMany(project => project.AnalyzerReferences).Select(reference => reference.FullPath).OfType<string>())
            .Where(path => !layout.IsImmutablePath(path))
            .Concat(files.Select(file => file.FilePath)).Distinct(FileSystemPath.Comparer);
        var mutablePaths = graphPaths.ToArray();
        await _pathPolicy.ValidateAsync(workspaceId, mutablePaths, ct).ConfigureAwait(false);
        var inventoryBuilder = ImmutableArray.CreateBuilder<PhysicalInputIdentity>();
        foreach (var path in mutablePaths)
        {
            inventoryBuilder.Add(await PhysicalInputIdentity.CaptureAsync(path, ct).ConfigureAwait(false));
        }
        var inventory = inventoryBuilder.ToImmutable();
        var operation = new PhysicalMutationRecovery(workspaceId, description, solution, GetCurrentVersion(workspaceId), files, session)
        { CapturedGraphFiles = inventory, CapturedLayout = layout };
        lock (_mutationLock) _mutationStates.Add(operation, new MutationState(_mutationEpochs.GetValueOrDefault(session)));
        return operation;
    }

    private MutationState RequireMutation(PhysicalMutationRecovery operation)
    {
        if (!ReferenceEquals(GetRequiredSession(operation.WorkspaceId), operation.SessionIdentity)
            || !_mutationStates.TryGetValue(operation, out var state))
            throw new WorkspaceMutationOwnershipException("The physical operation belongs to another or expired workspace session.");
        return state;
    }

    Task IWorkspacePathAuthority.AuthorizePathsAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct) =>
        AuthorizePhysicalPathsAsync(workspaceId, paths, ct);

    private async Task AuthorizePhysicalPathsAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
    {
        try { await _pathPolicy.ValidateAsync(workspaceId, paths, ct).ConfigureAwait(false); }
        catch (Exception failure) when (WorkspacePathPolicyRefusal.Is(failure, WorkspacePathPolicyRefusalKind.AliasedPath))
        {
            // The policy already inspected and rejected this physical alias. Preserve
            // the mutation boundary's refusal without probing denied paths again.
            throw PhysicalPathRequired();
        }
    }

    async Task IWorkspaceRecoveryAuthority.EnsurePreparedOperationCurrentAsync(PhysicalMutationRecovery operation, CancellationToken ct)
    {
        await _pathPolicy.ValidateAsync(operation.WorkspaceId, operation.Files.Select(file => file.FilePath).ToArray(), ct).ConfigureAwait(false);
        lock (_mutationLock) RequirePreparedOwnership(operation);
    }

    private void RequirePreparedOwnership(PhysicalMutationRecovery operation)
    {
        var state = RequireMutation(operation);
        EnsureMutationAllowed(operation.WorkspaceId, operation.Files.Select(file => file.FilePath).ToArray());
        if (state.CompletedEpoch is not null || _mutationEpochs.GetValueOrDefault(operation.SessionIdentity) != state.CapturedEpoch
            || !ReferenceEquals(GetCurrentSolution(operation.WorkspaceId), operation.PreApplySolution)
            || GetCurrentVersion(operation.WorkspaceId) != operation.PreApplyVersion || !MatchesFiles(operation.Files))
            throw new WorkspaceMutationOwnershipException("The prepared physical operation changed or was superseded; capture the current source again.");
    }

    async Task<IWorkspaceMutationLease> IWorkspaceRecoveryAuthority.BeginMutationAsync(PhysicalMutationRecovery operation, CancellationToken ct)
    {
        await _pathPolicy.ValidateAsync(operation.WorkspaceId, operation.Files.Select(file => file.FilePath).ToArray(), ct).ConfigureAwait(false);
        lock (_mutationLock)
        {
            var state = RequireMutation(operation);
            EnsureMutationAllowed(operation.WorkspaceId, operation.Files.Select(file => file.FilePath).ToArray());
            var completed = state.CompletedEpoch is not null;
            if (!completed) RequirePreparedOwnership(operation);
            var epoch = completed ? state.CompletedEpoch!.Value : state.CapturedEpoch;
            var solution = completed ? state.PostSolution : operation.PreApplySolution;
            var files = completed ? state.PostFiles : operation.Files;
            if (state.PostStateConflict)
                throw new WorkspaceMutationOwnershipException("The operation's post-state has external changes; it cannot overwrite them.");
            if (_mutationEpochs.GetValueOrDefault(operation.SessionIdentity) != epoch
                || !ReferenceEquals(GetCurrentSolution(operation.WorkspaceId), solution))
                throw new WorkspaceMutationOwnershipException("The physical operation was superseded; prepare it again against the current source.");
            if (!MatchesFiles(files))
            {
                if (completed) RetainPostConflict(operation, state);
                throw new WorkspaceMutationOwnershipException("The physical operation's files changed externally; recovery cannot overwrite them.");
            }
            _activeMutations.Add(operation.WorkspaceId, operation);
            if (!completed) operation.RecoveryFiles = [];
            return new MutationLease(this, operation, GetCurrentVersion(operation.WorkspaceId), files,
                GetCurrentSolution(operation.WorkspaceId));
        }
    }

    private void RequireActive(PhysicalMutationRecovery operation)
    {
        RequireMutation(operation);
        if (!_activeMutations.TryGetValue(operation.WorkspaceId, out var active) || !ReferenceEquals(active, operation))
            throw new WorkspaceMutationOwnershipException("The physical mutation does not own the active operation.");
    }

    private MutationState RequireCurrentOwnership(PhysicalMutationRecovery operation)
    {
        var state = RequireMutation(operation);
        if (state.PostStateConflict)
            throw new WorkspaceMutationOwnershipException("The operation's post-state has external changes; it cannot retain recovery authority.");
        if (_recoveries.TryGetValue(operation.WorkspaceId, out var retained) && ReferenceEquals(retained, operation)) return state;
        if (_activeMutations.TryGetValue(operation.WorkspaceId, out var active))
        {
            if (ReferenceEquals(active, operation)) return state;
            throw new WorkspaceMutationOwnershipException("Another physical operation is active; this operation was superseded.");
        }
        if (state.CompletedEpoch != _mutationEpochs.GetValueOrDefault(operation.SessionIdentity)
            || !ReferenceEquals(state.PostSolution, GetCurrentSolution(operation.WorkspaceId)))
            throw new WorkspaceMutationOwnershipException("The physical operation was superseded; it cannot retain recovery authority.");
        return state;
    }

    private void RetainPostConflict(PhysicalMutationRecovery operation, MutationState state)
    {
        state.PostStateConflict = true;
        _recoveries[operation.WorkspaceId] = operation;
    }

    async Task IWorkspaceRecoveryAuthority.EnsureOperationCurrentAsync(PhysicalMutationRecovery operation, CancellationToken ct)
    {
        await _pathPolicy.ValidateAsync(operation.WorkspaceId, operation.Files.Select(file => file.FilePath).ToArray(), ct).ConfigureAwait(false);
        lock (_mutationLock)
        {
            var state = RequireCurrentOwnership(operation);
            if (_recoveries.ContainsKey(operation.WorkspaceId))
                throw new PublicInvalidOperationException("Recover the failed operation through user undo before publishing another result.");
            if (!_activeMutations.ContainsKey(operation.WorkspaceId) && !MatchesFiles(state.PostFiles))
            {
                RetainPostConflict(operation, state);
                throw new WorkspaceMutationOwnershipException("The physical operation's post-state changed; it cannot publish undo authority.");
            }
        }
    }

    Task IWorkspaceRecoveryAuthority.CommitMutationAsync(PhysicalMutationRecovery operation, Solution? expected, CancellationToken ct)
    {
        lock (_mutationLock) RequireActive(operation);
        return CommitPhysicalSolutionCoreAsync(operation.WorkspaceId, expected, recovery: null, ct);
    }

    void IWorkspaceRecoveryAuthority.RestoreCapturedVersion(PhysicalMutationRecovery operation)
    {
        lock (_mutationLock)
        {
            RequireActive(operation);
            GetRequiredSession(operation.WorkspaceId).SetVersion(operation.PreApplyVersion);
        }
    }

    private sealed class MutationLease(WorkspaceManager manager, PhysicalMutationRecovery operation, int entryVersion, ImmutableArray<FileSnapshotDto> entryFiles, Solution entrySolution) : IWorkspaceMutationLease
    {
        private bool _finished;
        public void BeforeWrite(FileSnapshotDto intendedPost)
        {
            intendedPost = CanonicalSnapshot(intendedPost);
            lock (manager._mutationLock)
            {
                manager.RequireActive(operation);
                var path = intendedPost.FilePath;
                var file = operation.Files.SingleOrDefault(file => FileSystemPath.Comparer.Equals(file.FilePath, path))
                    ?? throw InvalidOperationErrors.Internal("A physical write is outside its captured operation.");
                manager.RequireMutation(operation).WriteIntents[path] = intendedPost;
                if (!operation.RecoveryFiles.Any(item => FileSystemPath.Comparer.Equals(item.FilePath, path)))
                    operation.RecoveryFiles = operation.RecoveryFiles.Add(file);
            }
        }
        public void Complete(IReadOnlyList<FileSnapshotDto> intendedPostFiles)
        {
            intendedPostFiles = intendedPostFiles.Select(CanonicalSnapshot).ToArray();
            lock (manager._mutationLock)
            {
                manager.RequireActive(operation);
                if (operation.HasRetainedSourceTransitions)
                    throw new WorkspaceMutationOwnershipException("Source transition custody must be resolved before completing the operation.");
                if (operation.HasUnverifiedSourceNamespaces)
                    throw new WorkspaceMutationOwnershipException("Source namespace changes require native graph verification before completing the operation.");
                var paths = intendedPostFiles.Select(file => file.FilePath).ToHashSet(FileSystemPath.Comparer);
                if (paths.Count != intendedPostFiles.Count || !paths.SetEquals(operation.Files.Select(file => file.FilePath)))
                    throw InvalidOperationErrors.Internal("Completed operation bytes must cover exactly its captured physical ownership.");
                var state = manager.RequireMutation(operation);
                var before = operation.Files.ToDictionary(file => file.FilePath, FileSystemPath.Comparer);
                foreach (var supplied in intendedPostFiles)
                {
                    var registered = state.WriteIntents.GetValueOrDefault(supplied.FilePath) ?? before[supplied.FilePath];
                    if (!SameSnapshot(registered, supplied))
                        throw InvalidOperationErrors.Internal("Completed operation bytes must match its registered writes and unchanged captured files.");
                }
                manager.AdvanceMutationEpoch(operation.SessionIdentity);
                state.CompletedEpoch = manager._mutationEpochs[operation.SessionIdentity];
                state.PostSolution = manager.GetCurrentSolution(operation.WorkspaceId);
                state.PostFiles = intendedPostFiles.Where(file => !SameSnapshot(before[file.FilePath], file)).ToImmutableArray();
                operation.RecoveryFiles = state.PostFiles.Select(file => before[file.FilePath]).ToImmutableArray();
                manager._activeMutations.Remove(operation.WorkspaceId);
                _finished = true;
            }
        }
        public void CancelBeforeSourceTransition(IPhysicalRestorationAuthority verification)
        {
            lock (manager._mutationLock)
            {
                if (_finished || verification is not PhysicalRestorationAuthority issued)
                    throw new WorkspaceMutationOwnershipException("Unused mutation cancellation requires its live native source verification.");
                issued.AssertMutationAssociation(manager, operation);
                if (!issued.CapturedSourceVerified || operation.HasAttemptedSourceTransition || operation.HasCompletedSourceTransitions
                    || operation.HasRetainedSourceTransitions || operation.HasUnverifiedSourceNamespaces
                    || manager.GetCurrentVersion(operation.WorkspaceId) != entryVersion
                    || !ReferenceEquals(manager.GetCurrentSolution(operation.WorkspaceId), entrySolution))
                    throw new WorkspaceMutationOwnershipException("An attempted mutation or changed session cannot cancel unused source authority.");
                // No source write was attempted. This releases authority without claiming
                // that externally replaced bytes are current or restored.
                manager._activeMutations.Remove(operation.WorkspaceId);
                _finished = true;
            }
        }
        public async Task AbortAsync(CancellationToken ct)
        {
            await manager._pathPolicy.ValidateAsync(operation.WorkspaceId, entryFiles.Select(file => file.FilePath).ToArray(), ct).ConfigureAwait(false);
            if (operation.HasUnverifiedSourceNamespaces)
            {
                lock (manager._mutationLock) manager.RequireActive(operation);
                await NativeCompilerContextCertificate.Require(entrySolution).AssertCurrentAsync(entrySolution,
                    operation.WorkspaceId, manager._pathPolicy, ct).ConfigureAwait(false);
            }
            lock (manager._mutationLock)
            {
                manager.RequireActive(operation);
                if (operation.HasRetainedSourceTransitions)
                    throw new WorkspaceMutationOwnershipException("Source transition custody must be resolved before aborting the operation.");
                if (manager.GetCurrentVersion(operation.WorkspaceId) != entryVersion || !MatchesFiles(entryFiles))
                    throw InvalidOperationErrors.Internal("Aborting an operation requires its pre-mutation version and exact files.");
                operation.SourceNamespacesVerified();
                manager._activeMutations.Remove(operation.WorkspaceId);
                _finished = true;
            }
        }
        public void Dispose()
        {
            lock (manager._mutationLock)
            {
                if (_finished) return;
                ((IWorkspaceRecoveryAuthority)manager).RetainRecovery(operation);
                manager._activeMutations.Remove(operation.WorkspaceId);
                _finished = true;
            }
        }
    }

    private static bool SameSnapshot(FileSnapshotDto before, FileSnapshotDto after) =>
        before.OriginalText is null ? after.OriginalText is null
            : after.OriginalText is not null && !before.OriginalBytes.IsDefault && !after.OriginalBytes.IsDefault
                && before.OriginalBytes.AsSpan().SequenceEqual(after.OriginalBytes.AsSpan());

    private static FileSnapshotDto CanonicalSnapshot(FileSnapshotDto snapshot)
    {
        if (!Path.IsPathFullyQualified(snapshot.FilePath))
            throw new PublicInvalidOperationException("Physical operation paths must be fully qualified.");
        return snapshot with { FilePath = Path.GetFullPath(snapshot.FilePath) };
    }

    PhysicalMutationRecovery? IWorkspaceRecoveryAuthority.GetPendingRecovery(string workspaceId) => _recoveries.GetValueOrDefault(workspaceId);

    void IWorkspaceRecoveryAuthority.RetainRecovery(PhysicalMutationRecovery operation)
    {
        lock (_mutationLock)
        {
            RequireCurrentOwnership(operation);
            if (_recoveries.TryGetValue(operation.WorkspaceId, out var existing) && !ReferenceEquals(existing, operation))
                throw InvalidOperationErrors.Internal("Another physical operation already owns recovery.");
            _recoveries[operation.WorkspaceId] = operation;
        }
    }

    void IWorkspaceRecoveryAuthority.AssertKnownFileRead(PhysicalMutationRecovery operation, FileSnapshotDto observed)
    {
        observed = CanonicalSnapshot(observed);
        lock (_mutationLock)
        {
            var state = RequireMutation(operation);
            if (!_recoveries.TryGetValue(operation.WorkspaceId, out var retained) || !ReferenceEquals(retained, operation))
                RequireCurrentOwnership(operation);
            var entry = operation.Files.SingleOrDefault(file => FileSystemPath.Comparer.Equals(file.FilePath, observed.FilePath))
                ?? throw InvalidOperationErrors.Internal("A physical read is outside its issued operation.");
            if (SameSnapshot(entry, observed)
                || state.WriteIntents.TryGetValue(observed.FilePath, out var intended) && SameSnapshot(intended, observed)
                || operation.CompletedSourceTransition(observed.FilePath) is { } transitioned && SameSnapshot(transitioned, observed)
                || state.PostFiles.Any(file => FileSystemPath.Comparer.Equals(file.FilePath, observed.FilePath) && SameSnapshot(file, observed)))
                return;
            if (state.CompletedEpoch is not null) RetainPostConflict(operation, state);
            throw new WorkspaceMutationOwnershipException("A physical read has conflicting bytes. Restore its captured pre-state or intended post-state before retrying recovery.");
        }
    }

    async Task IWorkspaceRecoveryAuthority.EnsureRecoveryCurrentAsync(PhysicalMutationRecovery recovery, CancellationToken ct)
    {
        FileSnapshotDto[] files;
        lock (_mutationLock)
        {
            var state = RequireMutation(recovery);
            if (!_recoveries.TryGetValue(recovery.WorkspaceId, out var pending) || !ReferenceEquals(pending, recovery))
                throw new WorkspaceMutationOwnershipException("The physical recovery capability is no longer current.");
            files = (state.CompletedEpoch is null ? recovery.RecoveryFiles : state.PostFiles).ToArray();
        }
        await recovery.ResolveSourceTransitionsAsync(this, ct).ConfigureAwait(false);
        foreach (var file in files)
        {
            await _pathPolicy.ValidateAsync(recovery.WorkspaceId, [file.FilePath], ct).ConfigureAwait(false);
            var bytes = await FileSnapshotCapture.ReadIfPresentAsync(file.FilePath, PhysicalInputReader.ReadAllBytesAsync, ct).ConfigureAwait(false);
            ((IWorkspaceRecoveryAuthority)this).AssertKnownFileRead(recovery,
                bytes is null ? new FileSnapshotDto(file.FilePath, null) : FileSnapshotDto.FromExistingBytes(file.FilePath, bytes));
        }
        lock (_mutationLock)
        {
            var state = RequireMutation(recovery);
            if (!_recoveries.TryGetValue(recovery.WorkspaceId, out var pending) || !ReferenceEquals(pending, recovery))
                throw new WorkspaceMutationOwnershipException("The physical recovery capability is no longer current.");
            state.PostStateConflict = false;
        }
    }

}
