using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression coverage for the MSBuild node-reuse pipe-inheritance hang: reusable worker
/// nodes (and VBCSCompiler) spawned by a child <c>dotnet</c> command inherit the redirected
/// stdout/stderr write handles and outlive the child by up to 15 minutes, so an unbounded
/// EOF wait deadlocks long after the command finished. On CI this surfaced as serial
/// 5-minute <see cref="TimeoutException"/>s across unrelated integration tests and two
/// job-timeout kills. <see cref="DotnetCommandRunner"/> now (a) disables node reuse for
/// spawned commands and (b) bounds the post-exit stream drain.
/// </summary>
[DoNotParallelize]
[TestClass]
public class DotnetCommandRunnerPipeLifetimeTests
{
    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        // Kill every warm build server (MSBuild node pool AND VBCSCompiler) so the
        // regression test's build deterministically spawns FRESH server processes — only
        // freshly spawned descendants inherit this process's redirected pipe handles
        // (pre-existing servers hold someone else's), and the inherited-handle case is
        // the one the bounded post-exit drain exists for.
        await ShutdownBuildServersAsync();
    }

    [TestMethod]
    public void CreateStartInfo_Disables_MSBuild_Node_Reuse()
    {
        var startInfo = DotnetCommandRunner.CreateStartInfo("work", ["build", "x.slnx"]);

        Assert.AreEqual("dotnet", startInfo.FileName);
        Assert.AreEqual("1", startInfo.Environment["MSBUILDDISABLENODEREUSE"]);
        Assert.IsTrue(startInfo.RedirectStandardInput);
        Assert.IsTrue(startInfo.RedirectStandardOutput);
        Assert.IsTrue(startInfo.RedirectStandardError);
        CollectionAssert.AreEqual(new[] { "build", "x.slnx" }, startInfo.ArgumentList);
    }

    [TestMethod]
    public void CreateStartInfo_UsesConfiguredDotnetCompatibleExecutable()
    {
        var startInfo = DotnetCommandRunner.CreateStartInfo(
            "work",
            ["restore", "x.slnx"],
            @"C:\sdk\dotnet.exe");

        Assert.AreEqual(@"C:\sdk\dotnet.exe", startInfo.FileName);
    }

    [TestMethod]
    [TestCategory("Process")]
    [Timeout(30_000)]
    public async Task RunAsync_ProvidesImmediateStandardInputEof()
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var runner = new DotnetCommandRunner();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var execution = await runner.RunAsync(
            repoRoot,
            "stdin-probe",
            [
                "-NoProfile",
                "-Command",
                "$inputText = [Console]::In.ReadToEnd(); Write-Output \"stdin-length=$($inputText.Length)\"",
            ],
            earlyKillPatterns: null,
            executablePath: OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh",
            timeout.Token);

        Assert.AreEqual(0, execution.ExitCode, execution.StdErr);
        StringAssert.Contains(execution.StdOut, "stdin-length=0");
    }

    [TestMethod]
    public async Task Bounded_Drain_Returns_Buffered_Output_When_Pipe_Never_Reaches_EOF()
    {
        // Deterministic stand-in for the inherited-handle case: the stream yields the
        // child's output, then never EOFs (a descendant still holds the pipe write end).
        // The drain token must unblock the read and return everything read so far.
        using var stream = new NeverEndingStream("Build succeeded."u8.ToArray());
        using var reader = new StreamReader(stream);
        using var drainCts = new CancellationTokenSource();

        var readTask = DotnetCommandRunner.ReadBoundedAsync(
            reader, 12_000, drainCts.Token, callerCt: CancellationToken.None);

        drainCts.CancelAfter(TimeSpan.FromMilliseconds(250));
        var result = await readTask;

        Assert.AreEqual("Build succeeded.", result);
    }

    [TestMethod]
    public async Task Caller_Cancellation_Still_Propagates_From_The_Reader()
    {
        // The drain-expiry swallow must not also swallow real caller cancellation —
        // GatedCommandExecutor's timeout semantics depend on the OCE escaping.
        using var stream = new NeverEndingStream([]);
        using var reader = new StreamReader(stream);
        using var callerCts = new CancellationTokenSource();

        var readTask = DotnetCommandRunner.ReadBoundedAsync(
            reader, 12_000, callerCts.Token, callerCt: callerCts.Token);

        callerCts.CancelAfter(TimeSpan.FromMilliseconds(250));
        // Derived-type match: the fabricated stream surfaces TaskCanceledException; a real
        // pipe surfaces OperationCanceledException. Both must escape the drain swallow.
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await readTask);
    }

    [TestMethod]
    [Timeout(240_000)]
    public async Task RunAsync_Returns_Promptly_When_Build_Server_Descendants_Hold_The_Pipe()
    {
        // End-to-end canary: a cold -nodeReuse:true -m:2 build can spawn MSBuild worker
        // nodes / VBCSCompiler that inherit the redirected pipe handles and outlive the
        // child (whether they do depends on SDK version and server warmth, so this cannot
        // deterministically reproduce the hang everywhere — the two unit tests above cover
        // the drain semantics deterministically). If the environment does produce a
        // pipe-holding descendant, a drain regression turns this into a clean [Timeout]
        // failure instead of a 15-minute suite stall.
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var solutionPath = TestFixtureFileSystem.FindFixturePath(
            repoRoot, "SampleSolution", "SampleSolution.slnx", "SampleSolution.sln");
        var workingDirectory = Path.GetDirectoryName(solutionPath)!;
        var runner = new DotnetCommandRunner();

        var execution = await runner.RunAsync(
            workingDirectory,
            solutionPath,
            ["build", solutionPath, "-m:2", "-nodeReuse:true", "--nologo"],
            CancellationToken.None);

        Assert.AreEqual(0, execution.ExitCode, execution.StdErr);
        Assert.IsTrue(execution.Succeeded);
        Assert.IsFalse(string.IsNullOrWhiteSpace(execution.StdOut),
            "The bounded drain must still capture the child's own output.");
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        // The regression test intentionally leaves -nodeReuse:true worker nodes behind;
        // shut them down so they don't linger 15 minutes on the host (the exact resource
        // rot the runner's MSBUILDDISABLENODEREUSE default prevents elsewhere).
        await ShutdownBuildServersAsync();
    }

    private static async Task ShutdownBuildServersAsync()
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var runner = new DotnetCommandRunner();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        try
        {
            var execution = await runner.RunAsync(
                repoRoot,
                "build-server",
                ["build-server", "shutdown"],
                timeout.Token);
            Assert.AreEqual(0, execution.ExitCode, execution.StdErr + execution.StdOut);
        }
        catch (OperationCanceledException exception) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException(
                "dotnet build-server shutdown did not complete within 30 seconds.",
                exception);
        }
    }

    /// <summary>
    /// Yields its initial payload, then blocks every subsequent read until the supplied
    /// token cancels — modelling a pipe whose write end is still held by a descendant
    /// process after the direct child exited.
    /// </summary>
    private sealed class NeverEndingStream(byte[] initialData) : Stream
    {
        private bool _drained;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_drained && initialData.Length > 0)
            {
                _drained = true;
                initialData.CopyTo(buffer);
                return initialData.Length;
            }

            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count)
            => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
