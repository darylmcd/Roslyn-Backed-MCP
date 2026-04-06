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

        var tempRoot = Path.Combine(Path.GetTempPath(), "RoslynMcpTests");
        if (Directory.Exists(tempRoot))
        {
            try
            {
                Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
                // Best-effort cleanup — another test runner instance may hold a lock.
            }
        }
    }
}
