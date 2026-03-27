namespace RoslynMcp.Tests;

[TestClass]
public static class AssemblyLifecycle
{
    [AssemblyCleanup]
    public static void Cleanup()
    {
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
