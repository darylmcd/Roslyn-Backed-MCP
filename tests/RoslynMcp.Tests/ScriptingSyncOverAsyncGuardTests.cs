namespace RoslynMcp.Tests;

[TestClass]
public sealed class ScriptingSyncOverAsyncGuardTests
{
    [TestMethod]
    public void ExecuteScript_SyncFenceCarriesApprovedDedicatedWorkerMarker()
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var scriptingServicePath = Path.Combine(
            repoRoot,
            "src",
            "RoslynMcp.Roslyn",
            "Services",
            "ScriptingService.cs");
        var source = File.ReadAllText(scriptingServicePath);

        Assert.AreEqual(1, CountOccurrences(source, ".GetAwaiter().GetResult()"));
        StringAssert.Contains(source, "APPROVED-SYNC-OVER-ASYNC");
        StringAssert.Contains(source, "dedicated worker thread");
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }
}
