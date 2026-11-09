using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;

namespace RoslynMcp.Roslyn.Helpers;

internal static class InterfaceConflictCompilation
{
    private const string UnavailableMessage =
        "The interface conflict check could not compile every project. Retry after restoring and reloading the workspace.";

    internal static async Task<Compilation> RequireAsync(
        string workspaceId, ICompilationCache cache, Project project, CancellationToken ct)
    {
        try
        {
            return await cache.GetCompilationAsync(workspaceId, project, ct).ConfigureAwait(false)
                ?? throw new PublicInvalidOperationException(UnavailableMessage);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new PublicInvalidOperationException(UnavailableMessage, ex);
        }
    }
}
