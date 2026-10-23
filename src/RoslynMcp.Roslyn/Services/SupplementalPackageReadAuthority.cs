using System.Collections.Immutable;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Exact inherited package reads; never compiler or physical graph certification.</summary>
internal abstract class SupplementalPackageReadAuthority
{
    private protected SupplementalPackageReadAuthority() { }

    internal static SupplementalPackageReadAuthority None { get; } = new NoSupplementalPackages();
    internal abstract ImmutableArray<string> Files { get; }
    internal abstract bool ProtectsSource(string workspaceId);
    internal abstract Task AssertCurrentAsync(CancellationToken ct);
    internal abstract Task AuthorizeFinalAsync(string workspaceId, IWorkspacePathPolicy policy,
        IReadOnlyList<string> mandatoryPaths, CancellationToken ct);
    internal abstract Task AssertPhysicalCurrentAsync(CancellationToken ct);

    public sealed override string ToString() => "Supplemental package read authority";

    private sealed class NoSupplementalPackages : SupplementalPackageReadAuthority
    {
        internal override ImmutableArray<string> Files => [];
        internal override bool ProtectsSource(string workspaceId) => false;
        internal override Task AssertCurrentAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
        internal override Task AuthorizeFinalAsync(string workspaceId, IWorkspacePathPolicy policy,
            IReadOnlyList<string> mandatoryPaths, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return policy.ValidateAsync(workspaceId, mandatoryPaths, ct);
        }
        internal override Task AssertPhysicalCurrentAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
