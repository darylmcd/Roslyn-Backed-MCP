using System.Runtime.CompilerServices;

namespace RoslynMcp.Core.Services;

internal enum WorkspacePathPolicyRefusalKind { SanctionedRootBoundary, MissingConfiguration, ClientRootsUnavailable, AliasedPath }

/// <summary>Identifies issued policy refusals without changing their public exception contracts.</summary>
internal static class WorkspacePathPolicyRefusal
{
    private sealed record Refusal(WorkspacePathPolicyRefusalKind Kind);
    private static readonly ConditionalWeakTable<Exception, Refusal> Issued = new();

    internal static T Mark<T>(T exception, WorkspacePathPolicyRefusalKind kind) where T : Exception
    {
        Issued.Add(exception, new(kind));
        return exception;
    }

    internal static bool Is(Exception exception, WorkspacePathPolicyRefusalKind kind)
        => Issued.TryGetValue(exception, out var refusal) && refusal.Kind == kind;

    internal static bool IsDenied(Exception exception)
        => exception is UnauthorizedAccessException || Issued.TryGetValue(exception, out _);

    internal static void Transfer(Exception original, Exception sanitized)
    {
        if (Issued.TryGetValue(original, out var refusal)) Issued.Add(sanitized, refusal);
    }
}
