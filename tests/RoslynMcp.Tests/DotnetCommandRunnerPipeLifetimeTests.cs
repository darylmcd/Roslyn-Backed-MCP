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
[TestClass]
public class DotnetCommandRunnerPipeLifetimeTests
{
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
    [DataRow("")]
    [DataRow("   ")]
    public void CreateStartInfo_BlankExecutablePath_ThrowsArgumentExceptionNamingTheParameter(string executablePath)
    {
        var ex = Assert.ThrowsExactly<ArgumentException>(
            () => DotnetCommandRunner.CreateStartInfo("work", ["build", "x.slnx"], executablePath));

        Assert.AreEqual("executablePath", ex.ParamName);
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
    [TestCategory("Process")]
    [Timeout(90_000)]
    public async Task RunAsync_ConcurrentOwnedDescendants_HoldPipesWithoutDisruptingEachOtherAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), nameof(DotnetCommandRunnerPipeLifetimeTests), Guid.NewGuid().ToString("N"));
        var children = new System.Diagnostics.Process?[2];
        try
        {
            var tasks = Enumerable.Range(0, 2).Select(async index =>
            {
                var folder = Path.Combine(root, index.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Directory.CreateDirectory(folder);
                await File.WriteAllTextAsync(Path.Combine(folder, "child.ps1"), """
                    [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'ready'), 'ready')
                    $deadline = [DateTime]::UtcNow.AddSeconds(60)
                    while (-not [IO.File]::Exists((Join-Path $PSScriptRoot 'release'))) {
                        if ([DateTime]::UtcNow -gt $deadline) { exit 91 }
                        Start-Sleep -Milliseconds 50
                    }
                    """);
                await File.WriteAllTextAsync(Path.Combine(folder, "parent.ps1"), """
                    $info = [Diagnostics.ProcessStartInfo]::new()
                    $info.FileName = (Get-Process -Id $PID).Path
                    $info.UseShellExecute = $false
                    $info.CreateNoWindow = $true
                    $info.ArgumentList.Add('-NoProfile')
                    $info.ArgumentList.Add('-File')
                    $info.ArgumentList.Add((Join-Path $PSScriptRoot 'child.ps1'))
                    $child = [Diagnostics.Process]::Start($info)
                    [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'pid.tmp'), $child.Id.ToString())
                    [IO.File]::Move((Join-Path $PSScriptRoot 'pid.tmp'), (Join-Path $PSScriptRoot 'pid'))
                    $deadline = [DateTime]::UtcNow.AddSeconds(15)
                    while (-not [IO.File]::Exists((Join-Path $PSScriptRoot 'ready'))) {
                        if ([DateTime]::UtcNow -gt $deadline) { exit 92 }
                        Start-Sleep -Milliseconds 50
                    }
                    [Console]::Out.WriteLine('parent-output')
                    [Console]::Error.WriteLine('parent-error')
                    """);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var executionTask = new DotnetCommandRunner().RunAsync(
                    folder,
                    "pipe-probe",
                    ["-NoProfile", "-File", Path.Combine(folder, "parent.ps1")],
                    earlyKillPatterns: null,
                    executablePath: OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh",
                    timeout.Token);
                var pidPath = Path.Combine(folder, "pid");
                try
                {
                    while (!File.Exists(pidPath))
                    {
                        await Task.Delay(50, timeout.Token);
                    }
                    children[index] = System.Diagnostics.Process.GetProcessById(int.Parse(
                        await File.ReadAllTextAsync(pidPath, timeout.Token), System.Globalization.CultureInfo.InvariantCulture));
                    // Pin the owned process identity before its parent can exit; cleanup never
                    // reopens a potentially recycled PID.
                    _ = children[index]!.SafeHandle;
                }
                catch
                {
                    timeout.Cancel();
                    try { await executionTask; }
                    catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }
                    throw;
                }
                return await executionTask;
            }).ToArray();
            var executions = await Task.WhenAll(tasks);
            for (var i = 0; i < executions.Length; i++)
            {
                var child = children[i]!;
                Assert.AreEqual(0, executions[i].ExitCode, executions[i].StdErr);
                StringAssert.Contains(executions[i].StdOut, "parent-output");
                StringAssert.Contains(executions[i].StdErr, "parent-error");
                Assert.IsTrue(executions[i].DurationMs >= DotnetCommandRunner.PostExitDrainGracePeriod.TotalMilliseconds,
                    "The probe must exercise the bounded drain, rather than receive an early pipe EOF.");
                Assert.IsFalse(child.HasExited, "The runner must finish while its descendant still holds both pipes.");
            }

            await File.WriteAllTextAsync(Path.Combine(root, "0", "release"), "release");
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await children[0]!.WaitForExitAsync(cleanup.Token);
            Assert.AreEqual(0, children[0]!.ExitCode);
            Assert.IsFalse(children[1]!.HasExited, "Cleaning one agent's child must preserve the other agent's child.");
        }
        finally
        {
            // Release only descendants created by this test, including on assertion/cancellation failure.
            for (var i = 0; i < 2; i++)
            {
                var folder = Path.Combine(root, i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (!Directory.Exists(folder)) continue;
                await File.WriteAllTextAsync(Path.Combine(folder, "release"), "release");
            }
            await Task.WhenAll(children.OfType<System.Diagnostics.Process>().Select(async child =>
            {
                try
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await child.WaitForExitAsync(cleanup.Token);
                }
                finally
                {
                    child.Dispose();
                }
            }));
            TestFixtureFileSystem.DeleteDirectoryIfExists(root);
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
