namespace RoslynMcp.Tests;

[TestClass]
public static class AssemblyLifecycle
{
    [AssemblyCleanup]
    public static void Cleanup()
    {
        // Dispose shared services owned by TestBase (workspace manager, file watchers,
        // build host subprocesses). Must run before tempRoot cleanup so we do not race
        // with file watcher disposal.
        TestBase.DisposeAssemblyResources();

        // Delete THIS run's subtree only — never the shared parent. Deleting
        // `%TEMP%/RoslynMcpTests` wholesale meant the first assembly to finish destroyed every
        // concurrently-running assembly's in-flight fixtures (row
        // test-temp-root-shared-cleanup-race); the old best-effort catch here protected only the
        // deleter, never the victim, which observed a DirectoryNotFoundException mid-test.
        TestTempRoot.DeleteCurrent();

        // Per-run roots would otherwise accumulate forever when a host crashes before cleanup.
        // Age-gated so a live sibling run is never a candidate.
        TestTempRoot.ReapAbandonedRuns();
    }
}
