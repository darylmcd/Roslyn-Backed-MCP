using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

public sealed partial class WorkspaceManager
{
    void IWorkspaceEvaluationLoader.AssertForkPathPolicy(ForkInvocationPathPolicy policy)
    {
        if (!ReferenceEquals(policy, _pathPolicy))
            throw new WorkspaceMutationOwnershipException("The fork operation and workspace manager must share one path policy instance.");
        ForkInvocationPathPolicy.AssertPolicyReceiver(policy);
        if (ForkInvocationPathPolicy.ActiveAuthority is not null)
            throw new WorkspaceMutationOwnershipException("A fork invocation cannot start another fork operation inside its active scope.");
    }

    SupplementalPackageReadAuthority IWorkspaceEvaluationLoader.CapturePackageReadAuthority(string workspaceId, Solution reviewedSource)
    {
        var source = CaptureOriginalSource(workspaceId, reviewedSource);
        return new OriginalPackageReadAuthority(source, NativeCompilerContextCertificate.Require(reviewedSource).InstalledPackages);
    }

    ForkInvocationAuthority IWorkspaceEvaluationLoader.CaptureForkInvocationAuthority(string workspaceId, Solution reviewedSource,
        string destinationPath, IReadOnlyDictionary<string, string> properties,
        PhysicalDirectoryScope.OwnedRoot destination, PhysicalGraphLayout layout)
        => new ManagerForkInvocationAuthority(this, CaptureOriginalSource(workspaceId, reviewedSource),
            ValidateWorkspacePathLexically(destinationPath), properties, destination, layout);

    ForkInvocationAuthority IWorkspaceEvaluationLoader.CaptureStageInvocationAuthority(string workspaceId, Solution reviewedSource)
    {
        if (ForkInvocationPathPolicy.ActiveAuthority is { } active)
        {
            if (active is not (ManagerForkInvocationAuthority or ManagerStageInvocationAuthority)
                || !ReferenceEquals(GetRequiredSession(workspaceId).Capture()?.Solution, reviewedSource)
                || !ReferenceEquals(active.Policy, _pathPolicy) || !active.AllowsWorkspace(workspaceId))
                throw new WorkspaceMutationOwnershipException("Staging requires this manager's exact current operation snapshot.");
            active.AssertCurrent();
            return active;
        }
        return new ManagerStageInvocationAuthority(_pathPolicy, CaptureOriginalSource(workspaceId, reviewedSource));
    }

    private sealed class ManagerStageInvocationAuthority(ForkInvocationPathPolicy policy, OriginalSourceScope source) : ForkInvocationAuthority(policy)
    {
        internal override string StageParentPath { get; } = PhysicalGraphNamespace.StagingParent;
        private readonly HashSet<string> _observed = new(FileSystemPath.Comparer);
        protected override void AssertCurrentCore() => source.AssertCurrent();
        protected override bool AllowsWorkspaceCore(string workspaceId) => workspaceId == source.WorkspaceId;
        protected override async Task ValidateCurrentCoreAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        {
            await source.ValidateAsync(paths, ct).ConfigureAwait(false);
            lock (_observed) _observed.UnionWith(paths);
        }
        protected override Task AssertCurrentCoreAsync(CancellationToken ct)
        {
            string[] paths;
            lock (_observed) paths = _observed.ToArray();
            return source.ValidateAsync(paths, ct);
        }
    }

    private OriginalSourceScope CaptureOriginalSource(string workspaceId, Solution reviewedSource)
    {
        var session = GetRequiredSession(workspaceId);
        var loaded = session.Capture();
        if (loaded is null || !ReferenceEquals(loaded.Solution, reviewedSource))
            throw new WorkspaceMutationOwnershipException("Fork reads require the current original workspace snapshot.");
        var source = new OriginalSourceScope(this, session, loaded, session.Version);
        source.AssertCurrent();
        return source;
    }

    private sealed class OriginalSourceScope
    {
        private readonly WorkspaceManager _manager;
        private readonly WorkspaceSession _session;
        private readonly WorkspaceSession.LoadedGraph _loaded;
        private readonly int _version;
        internal OriginalSourceScope(WorkspaceManager manager, WorkspaceSession session, WorkspaceSession.LoadedGraph loaded, int version)
        { _manager = manager; _session = session; _loaded = loaded; _version = version; }
        internal string WorkspaceId => _session.WorkspaceId;
        internal string LoadedPath => _session.LoadedPath ?? throw new WorkspaceMutationOwnershipException("The original workspace has no loaded entry path.");
        internal bool Matches(OriginalSourceScope other) => ReferenceEquals(_manager, other._manager)
            && ReferenceEquals(_session, other._session) && ReferenceEquals(_loaded, other._loaded) && _version == other._version;
        internal void AssertCurrent()
        {
            if (!_manager._sessions.TryGetValue(_session.WorkspaceId, out var current) || !ReferenceEquals(current, _session)
                || !ReferenceEquals(_session.Capture(), _loaded) || _session.Version != _version || _session.LoadLock.CurrentCount != 1)
                throw new WorkspaceMutationOwnershipException("The original workspace changed or closed; recreate the fork from its current loaded snapshot.");
        }

        internal async Task ValidateAsync(IReadOnlyList<string> paths, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            AssertCurrent();
            await _manager._pathPolicy.ValidateUnscopedAsync(_session.WorkspaceId, paths, ct).ConfigureAwait(false);
            AssertCurrent();
        }
    }

    private sealed class OriginalPackageReadAuthority(OriginalSourceScope source, InstalledPackageAuthority packages)
        : SupplementalPackageReadAuthority
    {
        internal override ImmutableArray<string> Files => packages.Files;
        internal override bool ProtectsSource(string workspaceId) => workspaceId == source.WorkspaceId;
        internal bool Matches(OriginalSourceScope expected) => source.Matches(expected);
        internal override async Task AssertCurrentAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            source.AssertCurrent();
            await packages.AssertCurrentAsync(source.ValidateAsync, ct).ConfigureAwait(false);
            source.AssertCurrent();
        }
        internal override async Task AuthorizeFinalAsync(string workspaceId, IWorkspacePathPolicy policy,
            IReadOnlyList<string> mandatoryPaths, CancellationToken ct)
        {
            var invocation = RequireFinalScope();
            // The same fork receiver maps private mandatory coordinates to this
            // original source before validating the complete union in ONE batch.
            await policy.ValidateAsync(workspaceId, mandatoryPaths.Concat(packages.RequiresCurrentGrant)
                .Distinct(FileSystemPath.Comparer).ToArray(), ct).ConfigureAwait(false);
            ForkInvocationPathPolicy.RequireActive(invocation);
            _ = RequireFinalScope();

            ManagerForkInvocationAuthority RequireFinalScope()
            {
                ct.ThrowIfCancellationRequested();
                source.AssertCurrent();
                if (ForkInvocationPathPolicy.ActiveAuthority is not ManagerForkInvocationAuthority active
                    || !ReferenceEquals(policy, active.Policy))
                    throw new WorkspaceMutationOwnershipException("Final inherited package grants require their exact fork policy receiver.");
                active.RequireSupplemental(this);
                active.RequireFinalLoadWorkspace(workspaceId);
                return active;
            }
        }
        internal override async Task AssertPhysicalCurrentAsync(CancellationToken ct)
        {
            source.AssertCurrent();
            await PhysicalInputCaptureBatch.AssertCurrentAsync(packages.Inputs, ct).ConfigureAwait(false);
            source.AssertCurrent();
        }
    }

    private ManagerForkInvocationAuthority? RequireInvocation(ForkInvocationAuthority? invocation)
    {
        var active = ForkInvocationPathPolicy.ActiveAuthority;
        if (invocation is null)
        {
            if (active is not null)
                throw new WorkspaceMutationOwnershipException("An active fork invocation requires its explicit workspace load authority.");
            return null;
        }
        ForkInvocationPathPolicy.RequireActive(invocation);
        if (invocation is not ManagerForkInvocationAuthority owned || !ReferenceEquals(owned.Manager, this)
            || !ReferenceEquals(invocation.Policy, _pathPolicy))
            throw new WorkspaceMutationOwnershipException("The fork invocation belongs to another workspace manager.");
        owned.AssertCurrent();
        return owned;
    }

    private sealed class ManagerForkInvocationAuthority : ForkInvocationAuthority
    {
        internal override string StageParentPath { get; } = PhysicalGraphNamespace.StagingParent;
        private readonly OriginalSourceScope _source;
        private readonly string _destinationPath;
        private readonly ImmutableDictionary<string, string> _properties;
        private readonly PhysicalDirectoryScope.OwnedRoot _ownedDestination;
        private readonly object _stateLock = new();
        private readonly HashSet<string> _observedPaths = new(FileSystemPath.Comparer);
        private WorkspaceSession? _destination;
        private WorkspaceSession.LoadedGraph? _expectedGraph;
        private int _expectedVersion;
        private bool _registered;
        internal WorkspaceManager Manager { get; }
        internal string SourceWorkspaceId => _source.WorkspaceId;

        internal ManagerForkInvocationAuthority(WorkspaceManager manager, OriginalSourceScope source, string destinationPath,
            IReadOnlyDictionary<string, string> properties, PhysicalDirectoryScope.OwnedRoot destination, PhysicalGraphLayout layout) : base(manager._pathPolicy)
        {
            Manager = manager;
            _source = source;
            _destinationPath = destinationPath;
            _properties = properties.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);
            _ownedDestination = destination;
            if (!FileSystemPath.Comparer.Equals(layout.MapFrom(destination.Path, destinationPath), source.LoadedPath))
                throw new WorkspaceMutationOwnershipException("The owned fork does not map to its reviewed native graph.");
        }

        protected override void AssertCurrentCore()
        {
            _ownedDestination.AssertCurrentPath(CancellationToken.None);
            _source.AssertCurrent();
            lock (_stateLock) AssertDestinationCurrent();
        }

        private void AssertDestinationCurrent()
        {
            if (_destination is not { } destination) return;
            if (!ReferenceEquals(destination.Capture(), _expectedGraph) || destination.Version != _expectedVersion
                || (_registered && (!Manager._sessions.TryGetValue(destination.WorkspaceId, out var current)
                    || !ReferenceEquals(current, destination)))
                || (destination.LoadedPath is not null && !FileSystemPath.Comparer.Equals(destination.LoadedPath, _destinationPath)))
                throw new WorkspaceMutationOwnershipException("The fork workspace changed or closed outside its issuing operation.");
        }

        protected override bool AllowsWorkspaceCore(string workspaceId)
        {
            lock (_stateLock) return workspaceId == _source.WorkspaceId || workspaceId == _destination?.WorkspaceId;
        }

        protected override async Task AssertCurrentCoreAsync(CancellationToken ct)
        {
            string[] paths;
            lock (_stateLock) paths = _observedPaths.ToArray();
            await _source.ValidateAsync(paths, ct).ConfigureAwait(false);
        }

        protected override async Task ValidateCurrentCoreAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        {
            await _source.ValidateAsync(paths, ct).ConfigureAwait(false);
            lock (_stateLock)
            {
                AssertDestinationCurrent();
                _observedPaths.UnionWith(paths);
            }
        }

        internal void RequireSupplemental(SupplementalPackageReadAuthority supplemental)
        {
            AssertCurrent();
            if (!ReferenceEquals(supplemental, SupplementalPackageReadAuthority.None)
                && (supplemental is not OriginalPackageReadAuthority inherited || !inherited.Matches(_source)))
                throw new WorkspaceMutationOwnershipException("The inherited package receipt does not belong to this fork's exact original workspace snapshot.");
        }

        internal void BindAllocated(WorkspaceSession session, string path, IDictionary<string, string>? properties)
        {
            AssertCurrent();
            lock (_stateLock)
            {
                if (_destination is not null || session.WorkspaceId == _source.WorkspaceId
                    || !FileSystemPath.Comparer.Equals(path, _destinationPath) || !PropertiesEqual(properties))
                    throw new WorkspaceMutationOwnershipException("The allocated fork does not match its reviewed destination and evaluation properties.");
                _destination = session;
                _expectedGraph = session.Capture();
                _expectedVersion = session.Version;
            }
        }

        internal void RequireDestination(WorkspaceSession session, string path, IDictionary<string, string>? properties)
        {
            AssertCurrent();
            lock (_stateLock)
                if (!ReferenceEquals(_destination, session) || !FileSystemPath.Comparer.Equals(path, _destinationPath)
                    || !PropertiesEqual(properties))
                    throw new WorkspaceMutationOwnershipException("The native load does not match the fork invocation destination.");
        }

        internal void RequireFinalLoadWorkspace(string workspaceId)
        {
            AssertCurrent();
            lock (_stateLock)
            {
                // Initial loading has an allocated session with no loaded graph
                // and no registration yet; identity, not readiness, is required.
                if (_destination is null || _destination.WorkspaceId != workspaceId)
                    throw new WorkspaceMutationOwnershipException("Final native load grants belong to another allocated destination.");
                AssertDestinationCurrent();
            }
        }

        private bool PropertiesEqual(IDictionary<string, string>? properties)
            => properties is not null && properties.Count == _properties.Count
                && properties.All(pair => _properties.TryGetValue(pair.Key, out var value)
                    && string.Equals(value, pair.Value, StringComparison.Ordinal));

        internal void Publish(WorkspaceSession session, Action publish)
        {
            AssertCurrent();
            lock (_stateLock)
            {
                if (!ReferenceEquals(_destination, session))
                    throw new WorkspaceMutationOwnershipException("The publication belongs to another fork workspace.");
                AssertDestinationCurrent();
                publish();
                _expectedGraph = session.Capture();
                _expectedVersion = session.Version;
            }
            AssertCurrent();
        }

        internal void Registered(WorkspaceSession session)
        {
            lock (_stateLock)
            {
                if (!ReferenceEquals(_destination, session) || !Manager._sessions.TryGetValue(session.WorkspaceId, out var current)
                    || !ReferenceEquals(current, session))
                    throw new WorkspaceMutationOwnershipException("The fork registration does not belong to its issuing operation.");
                _registered = true;
            }
            AssertCurrent();
        }
    }
}
