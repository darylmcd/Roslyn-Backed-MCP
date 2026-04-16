namespace RoslynMcp.Tests;

/// <summary>
/// Regression for `mcp-stdio-console-flush-on-exit` (P4): IT-Chat-Bot 2026-04-13 §9.4
/// reported that the host exited without flushing pending stdout when stdin closed —
/// clients reading the pipe received 0 bytes. Three flush paths now exist (per
/// Program.cs):
/// <list type="number">
///   <item><description><c>AppDomain.CurrentDomain.ProcessExit</c> hook (synchronous, fires on every exit path).</description></item>
///   <item><description><c>IHostApplicationLifetime.ApplicationStopping</c> callback (synchronous, fires on graceful shutdown).</description></item>
///   <item><description>Post-<c>RunAsync</c> sync + async flushes (final belt-and-suspenders).</description></item>
/// </list>
/// End-to-end verification (real subprocess + stdin pipe) is documented but not in this
/// suite (would require a published binary). This test guards the structural shape — a
/// future refactor that drops one of the flush paths breaks this test.
/// </summary>
[TestClass]
public sealed class StdioFlushOnExitTests
{
    private static readonly string ProgramSource =
        File.ReadAllText(Path.Combine(
            Path.GetDirectoryName(typeof(StdioFlushOnExitTests).Assembly.Location)!,
            "..", "..", "..", "..", "..", "src", "RoslynMcp.Host.Stdio", "Program.cs"));

    [TestMethod]
    public void Program_RegistersProcessExitFlushHandler()
    {
        Assert.IsTrue(ProgramSource.Contains("AppDomain.CurrentDomain.ProcessExit"),
            "Program.cs must register an AppDomain.CurrentDomain.ProcessExit handler so " +
            "the synchronous flush fires on every exit path (including stdin-EOF where " +
            "the SDK transport may exit before async FlushAsync completes).");
        Assert.IsTrue(ProgramSource.Contains("Console.Out.Flush()"),
            "ProcessExit handler must invoke Console.Out.Flush() synchronously.");
    }

    [TestMethod]
    public void Program_HasGracefulShutdownFlush_ViaApplicationStopping()
    {
        // ApplicationStopping is the SIGINT/SIGTERM path. Without this, graceful shutdowns
        // could miss buffered MCP responses.
        Assert.IsTrue(ProgramSource.Contains("ApplicationStopping.Register"),
            "Program.cs must register an ApplicationStopping.Register handler for graceful shutdown.");
    }

    [TestMethod]
    public void Program_HasPostRunAsyncFlushes_BothSyncAndAsync()
    {
        // After RunAsync returns, both sync and async flushes run as the final
        // belt-and-suspenders. Sync ensures the buffer is drained before disposal;
        // async re-flushes any encoder writes that batched behind the sync call.
        var postRunFlushSync = ProgramSource.IndexOf("await host.RunAsync()", StringComparison.Ordinal);
        Assert.IsTrue(postRunFlushSync > 0, "RunAsync await missing");
        var postSegment = ProgramSource.Substring(postRunFlushSync);
        Assert.IsTrue(postSegment.Contains("Console.Out.Flush()"),
            "Post-RunAsync block must include synchronous Console.Out.Flush().");
        Assert.IsTrue(postSegment.Contains("Console.Out.FlushAsync()"),
            "Post-RunAsync block must include asynchronous Console.Out.FlushAsync().");
    }
}
