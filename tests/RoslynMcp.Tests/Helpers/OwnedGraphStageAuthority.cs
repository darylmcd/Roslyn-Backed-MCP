using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests.Helpers;

/// <summary>Explicit authority for direct native-stage fixtures that have no manager session.</summary>
internal sealed class OwnedGraphStageAuthority : ForkInvocationAuthority
{
    private readonly string _workspaceId;
    private readonly Microsoft.CodeAnalysis.Workspace _workspace;
    private readonly Solution _captured;
    private OwnedGraphStageAuthority(string workspaceId, Solution source, ForkInvocationPathPolicy policy, string stageParent) : base(policy)
    {
        _workspaceId = workspaceId;
        _workspace = source.Workspace;
        _captured = _workspace.CurrentSolution;
        StageParentPath = stageParent;
    }

    internal override string StageParentPath { get; }
    internal static OwnedGraphStageAuthority Create(string workspaceId, Solution source, IWorkspacePathPolicy policy, string stageParent)
    {
        // Fixture-owned anchor setup precedes the product's namespace admission.
        Directory.CreateDirectory(Path.GetDirectoryName(stageParent)!);
        return new(workspaceId, source, policy as ForkInvocationPathPolicy ?? new ForkInvocationPathPolicy(policy), stageParent);
    }

    protected override void AssertCurrentCore()
    {
        if (!ReferenceEquals(_workspace.CurrentSolution, _captured))
            throw new WorkspaceMutationOwnershipException("The direct stage fixture's native owner changed.");
    }
    protected override bool AllowsWorkspaceCore(string workspaceId) => workspaceId == _workspaceId;
    protected override Task ValidateCurrentCoreAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        => Policy.ValidateUnscopedAsync(_workspaceId, paths, ct);
    protected override Task AssertCurrentCoreAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        AssertCurrentCore();
        return Task.CompletedTask;
    }
}
