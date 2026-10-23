namespace RoslynMcp.Tests.Helpers;

/// <summary>Direct parser fixtures have no independently evaluated native traversal membership.</summary>
internal static class CompilerArgumentFixtureAuthority
{
    internal static Task NoNativeTraversalAsync(string stage, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
