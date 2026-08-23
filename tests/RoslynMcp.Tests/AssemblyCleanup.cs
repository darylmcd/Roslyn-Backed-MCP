namespace RoslynMcp.Tests;

using RoslynMcp.Tests.Helpers;

[TestClass]
public static class AssemblyLifecycle
{
    [AssemblyCleanup]
    public static async Task Cleanup()
    {
        await CleanupFailureCollector.RunAsync(
            "Test assembly cleanup failed.",
            async () => await TestBase.DisposeAssemblyResourcesAsync().ConfigureAwait(false),
            CleanupFailureCollector.FromAction(TestTempRoot.DeleteCurrent),
            CleanupFailureCollector.FromAction(TestTempRoot.ReapAbandonedRuns)).ConfigureAwait(false);
    }
}
