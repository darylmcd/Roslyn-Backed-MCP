namespace RoslynMcp.Tests;

public abstract class SharedWorkspaceTestBase : TestBase
{
    protected static Task<string> LoadSharedSampleWorkspaceAsync(CancellationToken ct = default)
    {
        return GetOrLoadWorkspaceIdAsync(SampleSolutionPath, ct);
    }
}