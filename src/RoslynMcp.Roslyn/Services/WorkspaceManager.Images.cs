using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;

namespace RoslynMcp.Roslyn.Services;

public sealed partial class WorkspaceManager
{
    public async Task<ImmutableArray<AdmittedModuleImage>> ReadAdmittedImagesAsync(string workspaceId,
        ProjectId projectId, PortableExecutableReference reference, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var solution = GetCurrentSolution(workspaceId);
        var images = await NativeCompilerContextCertificate.Require(solution)
            .ReadAdmittedImagesAsync(solution, projectId, reference, workspaceId, _pathPolicy, ct).ConfigureAwait(false);
        if (!ReferenceEquals(solution, GetCurrentSolution(workspaceId)))
            throw new StaleWorkspaceTransitionException(workspaceId, "The loaded workspace changed while reading admitted images; retry against its current snapshot.");
        return images;
    }
}
