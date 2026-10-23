using System.Collections.Immutable;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Uses a manager-issued current source scope only during one explicit fork operation.</summary>
public sealed class ForkInvocationPathPolicy : IWorkspacePathPolicy
{
    private static readonly AsyncLocal<ActiveScope?> Current = new();
    private readonly IWorkspacePathPolicy _normal;

    public ForkInvocationPathPolicy(IWorkspacePathPolicy normal)
    {
        ArgumentNullException.ThrowIfNull(normal);
        if (normal is ForkInvocationPathPolicy)
            throw new ArgumentException("Fork path policies require the normal underlying policy.", nameof(normal));
        _normal = normal;
    }

    public Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
    {
        var active = Current.Value;
        return active is null ? _normal.ValidateAsync(workspaceId, paths, ct)
            : ValidateScopedAsync(active, workspaceId, paths, ct);
    }

    internal Task ValidateUnscopedAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        => _normal.ValidateAsync(workspaceId, paths, ct);

    internal static ForkInvocationAuthority? ActiveAuthority
    {
        get
        {
            if (Current.Value is not { } active) return null;
            active.Policy.AssertActiveScope(active);
            return active.Authority;
        }
    }

    internal static void RequireActive(ForkInvocationAuthority authority)
    {
        var active = Current.Value;
        if (active is null || !ReferenceEquals(active.Authority, authority)) throw Refusal();
        active.Policy.AssertActiveScope(active);
    }

    internal static void AssertPolicyReceiver(IWorkspacePathPolicy policy)
    {
        if (Current.Value is not { } active) return;
        if (!ReferenceEquals(active.Policy, policy)) throw Refusal();
        active.Policy.AssertActiveScope(active);
    }

    internal IDisposable Enter(ForkInvocationAuthority authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        if (Current.Value is not null || !ReferenceEquals(authority.Policy, this)) throw Refusal();
        authority.AssertCurrent();
        var active = new ActiveScope(this, authority);
        Current.Value = active;
        return new ScopeLease(active);
    }

    private async Task ValidateScopedAsync(ActiveScope active, string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        AssertActive(active, workspaceId);
        var frozen = paths.ToImmutableArray();
        await active.Authority.ValidateCurrentAsync(workspaceId, frozen, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        AssertActive(active, workspaceId);
    }

    private void AssertActive(ActiveScope active, string workspaceId)
    {
        AssertActiveScope(active);
        if (!active.Authority.AllowsWorkspace(workspaceId)) throw Refusal();
    }

    private void AssertActiveScope(ActiveScope active)
    {
        if (!ReferenceEquals(active.Policy, this) || Volatile.Read(ref active.Revoked) != 0
            || !ReferenceEquals(Current.Value, active)) throw Refusal();
        active.Authority.AssertCurrent();
    }

    private static PublicInvalidOperationException Refusal()
        => new("The fork invocation path authority is unavailable or does not belong to this operation. Recreate the fork from its current original workspace.");

    private sealed class ActiveScope(ForkInvocationPathPolicy policy, ForkInvocationAuthority authority)
    {
        internal ForkInvocationPathPolicy Policy { get; } = policy;
        internal ForkInvocationAuthority Authority { get; } = authority;
        internal int Revoked;
    }

    private sealed class ScopeLease(ActiveScope active) : IDisposable
    {
        public void Dispose()
        {
            active.Authority.Revoke();
            Interlocked.Exchange(ref active.Revoked, 1);
            if (ReferenceEquals(Current.Value, active)) Current.Value = null;
        }
    }
}

internal abstract class ForkInvocationAuthority(ForkInvocationPathPolicy policy)
{
    private int _revoked;
    private readonly AsyncLocal<OwnedCopyScope?> _ownedCopies = new();
    internal ForkInvocationPathPolicy Policy { get; } = policy;
    internal abstract string StageParentPath { get; }

    internal void AssertCurrent()
    {
        if (Volatile.Read(ref _revoked) != 0) throw Refusal();
        AssertOwnedCopies(_ownedCopies.Value);
        AssertCurrentCore();
        AssertOwnedCopies(_ownedCopies.Value);
        if (Volatile.Read(ref _revoked) != 0) throw Refusal();
    }

    internal bool AllowsWorkspace(string workspaceId)
    {
        AssertCurrent();
        return AllowsWorkspaceCore(workspaceId);
    }

    internal async Task ValidateCurrentAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        AssertCurrent();
        if (!AllowsWorkspaceCore(workspaceId)) throw Refusal();
        var copies = _ownedCopies.Value;
        var mapped = MapOwnedPaths(copies, paths, ct);
        await ValidateCurrentCoreAsync(workspaceId, mapped, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (!ReferenceEquals(_ownedCopies.Value, copies)) throw Refusal();
        _ = MapOwnedPaths(copies, paths, ct);
        AssertCurrent();
    }

    internal async Task AssertCurrentAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        AssertCurrent();
        var copies = _ownedCopies.Value;
        await AssertCurrentCoreAsync(ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (!ReferenceEquals(_ownedCopies.Value, copies)) throw Refusal();
        AssertOwnedCopies(copies);
        AssertCurrent();
    }

    internal void Revoke() => Interlocked.Exchange(ref _revoked, 1);

    internal IDisposable EnterOwnedCopy(Helpers.PhysicalDirectoryScope.OwnedRoot owner, PhysicalGraphLayout layout)
    {
        AssertCurrent();
        owner.AssertCurrentPath(CancellationToken.None);
        var scope = new OwnedCopyScope(owner, layout, _ownedCopies.Value);
        _ownedCopies.Value = scope;
        return new OwnedCopyLease(this, scope);
    }

    private static void AssertOwnedCopies(OwnedCopyScope? scope)
    {
        for (var current = scope; current is not null; current = current.Parent)
        {
            if (Volatile.Read(ref current.Revoked) != 0) throw Refusal();
            current.Owner.AssertCurrentPath(CancellationToken.None);
        }
    }

    private static string[] MapOwnedPaths(OwnedCopyScope? scopes, IReadOnlyList<string> paths, CancellationToken ct)
    {
        AssertOwnedCopies(scopes);
        var mapped = paths.ToArray();
        for (var scope = scopes; scope is not null; scope = scope.Parent)
        {
            ct.ThrowIfCancellationRequested();
            var selected = Enumerable.Range(0, mapped.Length).Where(index =>
                Helpers.FileSystemPath.Comparer.Equals(mapped[index], scope.Owner.Path)
                || Helpers.FileSystemPath.IsStrictDescendant(scope.Owner.Path, mapped[index])).ToArray();
            scope.Owner.AssertPhysicalPaths(selected.Select(index =>
                System.IO.Path.GetRelativePath(scope.Owner.Path, mapped[index])).ToArray(), ct);
            foreach (var index in selected)
            {
                ct.ThrowIfCancellationRequested();
                var source = scope.Layout.MapFrom(scope.Owner.Path, mapped[index]);
                if (!Helpers.FileSystemPath.Comparer.Equals(scope.Layout.MapTo(scope.Owner.Path, source),
                    System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(mapped[index]))))
                    throw Refusal();
                mapped[index] = source;
            }
        }
        AssertOwnedCopies(scopes);
        return mapped;
    }

    private sealed class OwnedCopyScope(Helpers.PhysicalDirectoryScope.OwnedRoot owner, PhysicalGraphLayout layout, OwnedCopyScope? parent)
    {
        internal Helpers.PhysicalDirectoryScope.OwnedRoot Owner { get; } = owner;
        internal PhysicalGraphLayout Layout { get; } = layout;
        internal OwnedCopyScope? Parent { get; } = parent;
        internal int Revoked;
    }

    private sealed class OwnedCopyLease(ForkInvocationAuthority authority, OwnedCopyScope scope) : IDisposable
    {
        public void Dispose()
        {
            Interlocked.Exchange(ref scope.Revoked, 1);
            if (ReferenceEquals(authority._ownedCopies.Value, scope)) authority._ownedCopies.Value = scope.Parent;
        }
    }
    protected abstract void AssertCurrentCore();
    protected abstract Task AssertCurrentCoreAsync(CancellationToken ct);
    protected abstract bool AllowsWorkspaceCore(string workspaceId);
    protected abstract Task ValidateCurrentCoreAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct);

    private static PublicInvalidOperationException Refusal()
        => new("The original workspace changed or the fork invocation ended. Recreate the fork from its current original workspace.");
}
