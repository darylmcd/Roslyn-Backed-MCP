using System.IO.Pipes;
using System.Text;
using RoslynMcp.Roslyn.Helpers;
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
    public void CreateStartInfo_Strips_Inherited_MSBuild_Location_Variables()
    {
        // MSBuildLocator / `dotnet test` pin MSBuild to the server's SDK via these variables. A child
        // dotnet that resolves a different SDK (ubuntu-26.04 ships 11.0.100 beside 10.0.404) would
        // otherwise launch worker nodes from the pinned SDK and fail every node handshake.
        string[] names = ["MSBUILD_EXE_PATH", "MSBuildExtensionsPath", "MSBuildSDKsPath"];
        var saved = names.ToDictionary(n => n, Environment.GetEnvironmentVariable);
        try
        {
            foreach (var name in names)
            {
                Environment.SetEnvironmentVariable(name, "/pinned/sdk/" + name);
            }

            var startInfo = DotnetCommandRunner.CreateStartInfo("work", ["msbuild", "x.proj"]);

            foreach (var name in names)
            {
                Assert.IsFalse(startInfo.Environment.ContainsKey(name), name + " leaked into the child environment.");
            }
        }
        finally
        {
            foreach (var (name, value) in saved)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
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
    public async Task Pump_GraceAlreadyExpiredBeforeItRuns_StillReturnsEverythingInThePipe()
    {
        // The hosted failure shape: the child wrote one short line and exited, and the capture
        // only got to run after the post-exit grace had already expired. Everything the child
        // wrote is still in the pipe at that point and must be returned.
        var source = new ScriptedSource(heldOpen: true, "parent-"u8.ToArray(), "output"u8.ToArray());

        var result = await ProcessOutputPump.ReadAsync(
            source, Encoding.UTF8, 12_000, onDecodedChunk: null, hasExited: () => true,
            postExitGracePeriod: TimeSpan.Zero, ProcessOutputPump.DefaultPollInterval, TimeProvider.System,
            CancellationToken.None);

        Assert.AreEqual("parent-output", result);
    }

    [TestMethod]
    public async Task Pump_DescendantHoldsThePipeOpen_ReturnsBufferedOutputWhenGraceExpires()
    {
        // The pipe yields the child's output and then never ends: a descendant still holds the
        // write end. The grace period bounds the wait and nothing already read is dropped.
        var source = new ScriptedSource(heldOpen: true, "Build succeeded."u8.ToArray());
        var grace = TimeSpan.FromMilliseconds(250);
        var elapsed = System.Diagnostics.Stopwatch.StartNew();

        var result = await ProcessOutputPump.ReadAsync(
            source, Encoding.UTF8, 12_000, onDecodedChunk: null, hasExited: () => true,
            grace, TimeSpan.FromMilliseconds(5), TimeProvider.System, CancellationToken.None);

        Assert.AreEqual("Build succeeded.", result);
        Assert.IsTrue(elapsed.Elapsed >= grace, "The pump must keep listening for the whole grace period.");
    }

    [TestMethod]
    public async Task Pump_ChildStillRunning_DoesNotStopOnAnEmptyPipe()
    {
        var exited = false;
        var source = new ScriptedSource(heldOpen: true, "first"u8.ToArray());
        var pump = ProcessOutputPump.ReadAsync(
            source, Encoding.UTF8, 12_000, onDecodedChunk: null, hasExited: () => Volatile.Read(ref exited),
            postExitGracePeriod: TimeSpan.Zero, TimeSpan.FromMilliseconds(5), TimeProvider.System,
            CancellationToken.None);

        await Task.Delay(100);
        Assert.IsFalse(pump.IsCompleted, "An empty pipe is not the end of output while the child runs.");
        source.Enqueue(" second"u8.ToArray());
        Volatile.Write(ref exited, true);

        Assert.AreEqual("first second", await pump);
    }

    [TestMethod]
    public async Task Pump_EveryWriterClosed_ReturnsWithoutWaitingForTheGracePeriod()
    {
        var source = new ScriptedSource(heldOpen: false, "done"u8.ToArray());

        var result = await ProcessOutputPump.ReadAsync(
            source, Encoding.UTF8, 12_000, onDecodedChunk: null, hasExited: () => false,
            postExitGracePeriod: Timeout.InfiniteTimeSpan, ProcessOutputPump.DefaultPollInterval,
            TimeProvider.System, CancellationToken.None);

        Assert.AreEqual("done", result);
    }

    [TestMethod]
    public async Task Pump_DescendantNeverStopsWriting_IsBoundedAfterTheGracePeriod()
    {
        var source = new EndlessSource();

        var result = await ProcessOutputPump.ReadAsync(
            source, Encoding.UTF8, 100, onDecodedChunk: null, hasExited: () => true,
            postExitGracePeriod: TimeSpan.Zero, ProcessOutputPump.DefaultPollInterval, TimeProvider.System,
            CancellationToken.None);

        Assert.AreEqual(100, result.Length, "Output stays bounded to its final characters.");
        Assert.IsTrue(source.BytesRead >= ProcessOutputPump.PostGraceByteLimit);
        Assert.IsTrue(source.BytesRead < ProcessOutputPump.PostGraceByteLimit + 8192,
            "The post-grace drain must stop at its byte limit.");
    }

    [TestMethod]
    public async Task Pump_CallerCancellation_Propagates()
    {
        // GatedCommandExecutor's timeout semantics depend on the cancellation escaping.
        var source = new ScriptedSource(heldOpen: true);
        using var callerCts = new CancellationTokenSource();
        var pump = ProcessOutputPump.ReadAsync(
            source, Encoding.UTF8, 12_000, onDecodedChunk: null, hasExited: () => false,
            DotnetCommandRunner.PostExitDrainGracePeriod, TimeSpan.FromMilliseconds(5), TimeProvider.System,
            callerCts.Token);

        callerCts.CancelAfter(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await pump);
    }

    [TestMethod]
    public async Task Pump_ReportsEachDecodedChunkOnce()
    {
        var chunks = new List<string>();
        var source = new ScriptedSource(heldOpen: false, "one "u8.ToArray(), "two"u8.ToArray());

        await ProcessOutputPump.ReadAsync(
            source, Encoding.UTF8, 12_000, chunks.Add, hasExited: () => false,
            postExitGracePeriod: Timeout.InfiniteTimeSpan, ProcessOutputPump.DefaultPollInterval,
            TimeProvider.System, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "one ", "two" }, chunks);
    }

    [TestMethod]
    public void PipeSource_ReadsOnlyWhatIsAlreadyInARealPipe_AndReportsItsEnd()
    {
        using var readEnd = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.None);
        using var writeEnd = new AnonymousPipeClientStream(PipeDirection.Out, readEnd.ClientSafePipeHandle);
        using var reader = new StreamReader(readEnd, Encoding.UTF8);
        var source = ProcessPipeSource.Create(reader);
        var buffer = new byte[64];

        Assert.AreEqual(AvailableReadStatus.Empty, source.ReadAvailable(buffer, out var read));
        Assert.AreEqual(0, read);

        writeEnd.Write("parent-output"u8);
        writeEnd.Flush();
        Assert.AreEqual(AvailableReadStatus.Data, source.ReadAvailable(buffer, out read));
        Assert.AreEqual("parent-output", Encoding.UTF8.GetString(buffer, 0, read));
        Assert.AreEqual(AvailableReadStatus.Empty, source.ReadAvailable(buffer, out read));

        // Bytes written before the last writer closed are still delivered before the end.
        writeEnd.Write("tail"u8);
        writeEnd.Dispose();
        readEnd.DisposeLocalCopyOfClientHandle();
        Assert.AreEqual(AvailableReadStatus.Data, source.ReadAvailable(buffer, out read));
        Assert.AreEqual("tail", Encoding.UTF8.GetString(buffer, 0, read));
        Assert.AreEqual(AvailableReadStatus.End, source.ReadAvailable(buffer, out read));
    }

    [TestMethod]
    public void PipeSource_ReadIsCappedByTheDestination()
    {
        using var readEnd = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.None);
        using var writeEnd = new AnonymousPipeClientStream(PipeDirection.Out, readEnd.ClientSafePipeHandle);
        using var reader = new StreamReader(readEnd, Encoding.UTF8);
        var source = ProcessPipeSource.Create(reader);
        writeEnd.Write("abcdef"u8);
        writeEnd.Flush();
        var buffer = new byte[4];

        Assert.AreEqual(AvailableReadStatus.Data, source.ReadAvailable(buffer, out var read));
        Assert.AreEqual("abcd", Encoding.UTF8.GetString(buffer, 0, read));
        Assert.AreEqual(AvailableReadStatus.Data, source.ReadAvailable(buffer, out read));
        Assert.AreEqual("ef", Encoding.UTF8.GetString(buffer, 0, read));
    }

    [TestMethod]
    public void Decoder_MultiByteCharacterSplitAcrossReads_IsNotCorruptedOrDropped()
    {
        var bytes = Encoding.UTF8.GetBytes("naïve ✓");
        var decoder = new ProcessOutputDecoder(Encoding.UTF8);
        var text = new StringBuilder();

        foreach (var value in bytes)
        {
            text.Append(decoder.Decode([value], flush: false));
        }
        text.Append(decoder.Decode([], flush: true));

        Assert.AreEqual("naïve ✓", text.ToString());
    }

    [TestMethod]
    [DataRow("utf-8")]
    [DataRow("utf-16")]
    [DataRow("utf-16BE")]
    [DataRow("utf-32")]
    [DataRow("utf-32BE")]
    public void Decoder_LeadingByteOrderMark_SelectsThatEncodingAndIsNotEmitted(string encodingName)
    {
        // Process creates its redirected readers with byte-order-mark detection enabled.
        var encoding = Encoding.GetEncoding(encodingName);
        byte[] bytes = [.. encoding.GetPreamble(), .. encoding.GetBytes("ok ✓")];
        Assert.AreNotEqual(0, encoding.GetPreamble().Length);

        foreach (var chunkSize in new[] { 1, 2, 3, bytes.Length })
        {
            var decoder = new ProcessOutputDecoder(Encoding.Latin1);
            var text = new StringBuilder();
            for (var offset = 0; offset < bytes.Length; offset += chunkSize)
            {
                text.Append(decoder.Decode(bytes.AsSpan(offset, Math.Min(chunkSize, bytes.Length - offset)), flush: false));
            }
            text.Append(decoder.Decode([], flush: true));

            Assert.AreEqual("ok ✓", text.ToString(), $"{encodingName}, chunk size {chunkSize}");
        }
    }

    [TestMethod]
    [DataRow(new byte[] { }, "")]
    [DataRow(new byte[] { 0x61 }, "a")]
    [DataRow(new byte[] { 0xEF, 0xBB }, "ï»")]
    [DataRow(new byte[] { 0xFF, 0x41, 0x42 }, "ÿAB")]
    public void Decoder_OutputWithoutAByteOrderMark_UsesTheConfiguredEncoding(byte[] bytes, string expected)
    {
        var decoder = new ProcessOutputDecoder(Encoding.Latin1);

        var text = decoder.Decode(bytes, flush: false) + decoder.Decode([], flush: true);

        Assert.AreEqual(expected, text);
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
    /// Hands over queued chunks one read at a time, then reports an empty pipe (a descendant
    /// still holds it open) or its end.
    /// </summary>
    private sealed class ScriptedSource(bool heldOpen, params byte[][] chunks) : IAvailableByteSource
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<byte[]> _chunks = new(chunks);

        public void Enqueue(byte[] chunk) => _chunks.Enqueue(chunk);

        public AvailableReadStatus ReadAvailable(Span<byte> destination, out int bytesRead)
        {
            if (_chunks.TryDequeue(out var chunk))
            {
                chunk.CopyTo(destination);
                bytesRead = chunk.Length;
                return AvailableReadStatus.Data;
            }

            bytesRead = 0;
            return heldOpen ? AvailableReadStatus.Empty : AvailableReadStatus.End;
        }
    }

    /// <summary>A descendant that never stops writing.</summary>
    private sealed class EndlessSource : IAvailableByteSource
    {
        public long BytesRead { get; private set; }

        public AvailableReadStatus ReadAvailable(Span<byte> destination, out int bytesRead)
        {
            destination.Fill((byte)'x');
            bytesRead = destination.Length;
            BytesRead += bytesRead;
            return AvailableReadStatus.Data;
        }
    }
}
