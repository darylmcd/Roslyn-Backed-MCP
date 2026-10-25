using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using RoslynMcp.Tests.Support;

namespace RoslynMcp.Tests.Helpers;

[TestClass]
public sealed class PwshScriptRunnerTests
{
    [TestMethod]
    public void CaptureDiagnostic_ReportsFailureTypeAndNativeCodeWithoutExceptionMessage()
    {
        Assert.AreEqual("captured", PwshScriptRunner.CaptureDiagnostic(() => "captured"));
        Assert.AreEqual("error:Win32Exception:6", PwshScriptRunner.CaptureDiagnostic(
            () => throw new Win32Exception(6, "private-message-sentinel")));
        Assert.AreEqual("error:InvalidOperationException", PwshScriptRunner.CaptureDiagnostic(
            () => throw new InvalidOperationException("private-message-sentinel")));
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task RunAsync_PreservesUtf32BomAndShortOutputAtEof()
    {
        var result = await PwshScriptRunner.RunAsync(
            ["-NoProfile", "-Command", """
                $encoding = [Text.Encoding]::UTF32
                $out = [Console]::OpenStandardOutput()
                $bytes = $encoding.GetPreamble() + $encoding.GetBytes('a')
                $out.Write($bytes, 0, $bytes.Length)
                [Console]::Error.Write('xyz')
                """],
            timeout: TimeSpan.FromSeconds(30),
            description: "output-prefix EOF fixture");
        Assert.AreEqual(0, result.ExitCode, result.AllOutput);
        Assert.AreEqual("a", result.StdOut);
        Assert.AreEqual("xyz", result.StdErr);
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task RunAsync_PreservesSplitMultibyteOutputAndBomEncoding()
    {
        var result = await PwshScriptRunner.RunAsync(
            ["-NoProfile", "-Command", """
                $text = "alpha`r`n" + [char]0x03b2 + [char]::ConvertFromUtf32(0x1f642) + "tail"
                $utf8 = [Text.UTF8Encoding]::new($true)
                $utf16 = [Text.UnicodeEncoding]::new($false, $true)
                $out = [Console]::OpenStandardOutput()
                $err = [Console]::OpenStandardError()
                foreach ($byte in ($utf8.GetPreamble() + $utf8.GetBytes($text))) {
                    $out.WriteByte($byte); $out.Flush(); Start-Sleep -Milliseconds 5
                }
                foreach ($byte in ($utf16.GetPreamble() + $utf16.GetBytes($text))) {
                    $err.WriteByte($byte); $err.Flush(); Start-Sleep -Milliseconds 5
                }
                """],
            timeout: TimeSpan.FromSeconds(30),
            description: "split-encoding output fixture");
        Assert.AreEqual(0, result.ExitCode, result.AllOutput);
        Assert.AreEqual("alpha\r\nβ🙂tail", result.StdOut);
        Assert.AreEqual(result.StdOut, result.StdErr);
    }

    [TestMethod]
    [TestCategory("Process")]
    [DataRow("held-pipes")]
    [DataRow("large-output")]
    public async Task RunAsync_LimitedWorkers_DrainsWithoutBlockingOtherProcesses(string scenario)
    {
        var root = CreateFixtureRoot();
        try
        {
            var result = await PwshScriptRunner.RunExecutableAsync(
                "dotnet",
                [Path.Combine(AppContext.BaseDirectory, "ProcessProbes", "ProcessPipeWorkerProbe", "ProcessPipeWorkerProbe.dll"),
                 typeof(PwshScriptRunner).Assembly.Location, root, scenario],
                environment: null,
                timeout: TimeSpan.FromSeconds(45),
                description: "isolated output-worker probe");
            Assert.AreEqual(0, result.ExitCode, result.AllOutput);
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(root);
        }
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task GitFixture_OneAvailableWorker_CompletesAfterNativeExit()
    {
        await GitFixtureRunner.RequireAvailableAsync();
        var root = CreateFixtureRoot();
        var probeRoot = Path.Combine(root, OperatingSystem.IsWindows() ? "quoted ' paths" : "quoted ' paths with\\backslash");
        Directory.CreateDirectory(probeRoot);
        try
        {
            var result = await PwshScriptRunner.RunExecutableAsync(
                "dotnet",
                [Path.Combine(AppContext.BaseDirectory, "ProcessProbes", "GitFixtureWorkerProbe", "GitFixtureWorkerProbe.dll"),
                 typeof(GitFixtureRunner).Assembly.Location, probeRoot],
                environment: null,
                timeout: TimeSpan.FromSeconds(45),
                description: "isolated Git fixture worker probe");
            Assert.AreEqual(0, result.ExitCode, result.AllOutput);
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(root);
        }
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task RunAsync_PreservesArgumentBoundaries()
    {
        var fixtureRoot = CreateFixtureRoot();
        try
        {
            var scriptPath = Path.Combine(fixtureRoot, "capture-arguments.ps1");
            await File.WriteAllTextAsync(
                scriptPath,
                "param([string]$First, [string]$Second)\n@($First, $Second) | ConvertTo-Json -Compress\n");

            var result = await PwshScriptRunner.RunAsync(
                ["-NoProfile", "-File", scriptPath, "alpha beta", "semi;value"],
                timeout: TimeSpan.FromSeconds(30),
                description: "argument-boundary fixture");

            Assert.AreEqual(0, result.ExitCode, result.AllOutput);
            StringAssert.Contains(result.StdOut, "alpha beta");
            StringAssert.Contains(result.StdOut, "semi;value");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task RunAsync_ReturnsNonzeroExitAndBothOutputStreams()
    {
        var result = await PwshScriptRunner.RunAsync(
            [
                "-NoProfile",
                "-Command",
                "[Console]::Out.WriteLine('stdout-sentinel'); " +
                "[Console]::Error.WriteLine('stderr-sentinel'); exit 23",
            ],
            timeout: TimeSpan.FromSeconds(30),
            description: "output fixture");

        Assert.AreEqual(23, result.ExitCode, result.AllOutput);
        StringAssert.Contains(result.StdOut, "stdout-sentinel");
        StringAssert.Contains(result.StdErr, "stderr-sentinel");
    }

    [TestMethod]
    [TestCategory("Process")]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RunAsync_CancellationTerminatesChildProcessTree(bool callerCancellation)
    {
        var fixtureRoot = CreateFixtureRoot();
        var childPidPath = Path.Combine(fixtureRoot, "child.pid");
        try
        {
            var scriptPath = Path.Combine(fixtureRoot, "spawn-child.ps1");
            await File.WriteAllTextAsync(
                scriptPath,
                "param([string]$ChildPidPath)\n" +
                "$pwsh = (Get-Process -Id $PID).Path\n" +
                "$info = [Diagnostics.ProcessStartInfo]::new($pwsh)\n" +
                "$info.UseShellExecute = $false; $info.CreateNoWindow = $true\n" +
                "foreach ($argument in @('-NoProfile', '-Command', 'Start-Sleep -Seconds 30')) { $info.ArgumentList.Add($argument) }\n" +
                "$child = [Diagnostics.Process]::Start($info)\n" +
                "[IO.File]::WriteAllText($ChildPidPath + '.tmp', [string]$child.Id)\n" +
                "[IO.File]::Move($ChildPidPath + '.tmp', $ChildPidPath)\n" +
                "$child.Dispose()\n" +
                "Start-Sleep -Seconds 30\n");

            using var cancellation = new CancellationTokenSource();
            if (callerCancellation)
            {
                var runTask = PwshScriptRunner.RunAsync(
                    ["-NoProfile", "-File", scriptPath, "-ChildPidPath", childPidPath],
                    cancellationToken: cancellation.Token,
                    description: "caller-cancelled process-tree fixture");
                var childStarted = await WaitForFileAsync(childPidPath, TimeSpan.FromSeconds(10));
                cancellation.Cancel();

                await Assert.ThrowsAsync<OperationCanceledException>(() => runTask);
                Assert.IsTrue(childStarted, "The fixture did not start its child process within 10 seconds.");
            }
            else
            {
                var exception = await Assert.ThrowsExactlyAsync<TimeoutException>(() =>
                    PwshScriptRunner.RunAsync(
                        ["-NoProfile", "-File", scriptPath, "-ChildPidPath", childPidPath],
                        timeout: TimeSpan.FromSeconds(10),
                        description: "timed-out process-tree fixture"));
                StringAssert.Contains(exception.Message, "timed out");
                AssertTimeoutDiagnostics(exception, inheritedPipes: false);
            }

            Assert.IsTrue(File.Exists(childPidPath), "The fixture did not start its child process.");
            var childPid = int.Parse(await File.ReadAllTextAsync(childPidPath), System.Globalization.CultureInfo.InvariantCulture);
            Assert.IsTrue(
                await WaitForProcessExitAsync(childPid),
                $"Child process {childPid} survived process-tree termination.");
        }
        finally
        {
            await StopFixtureChildAsync(childPidPath);
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    [TestMethod]
    [TestCategory("Process")]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public async Task RunAsync_ExitedParentWithInheritedPipes_StillHonorsCancellation(bool callerCancellation, bool useGitFixture)
    {
        var fixtureRoot = CreateFixtureRoot();
        var childPidPath = Path.Combine(fixtureRoot, "child.pid");
        var parentPidPath = Path.Combine(fixtureRoot, "parent.pid");
        var releasePath = Path.Combine(fixtureRoot, "release");
        using var cancellation = new CancellationTokenSource();
        Task? runTask = null;
        try
        {
            var childScript = Path.Combine(fixtureRoot, "retain-pipes.ps1");
            await File.WriteAllTextAsync(childScript, """
                param([string]$ReleasePath, [string]$ChildPidPath)
                [IO.File]::WriteAllText($ChildPidPath + '.tmp', [string]$PID)
                [IO.File]::Move($ChildPidPath + '.tmp', $ChildPidPath)
                [Console]::Out.WriteLine('stdout-held')
                [Console]::Error.WriteLine('stderr-held')
                $deadline = [DateTime]::UtcNow.AddMinutes(2)
                while (!(Test-Path -LiteralPath $ReleasePath) -and [DateTime]::UtcNow -lt $deadline) {
                    Start-Sleep -Milliseconds 50
                }
                """);
            var parentScript = Path.Combine(fixtureRoot, "exit-with-child.ps1");
            await File.WriteAllTextAsync(parentScript, """
                param([string]$ChildScript, [string]$ReleasePath, [string]$ChildPidPath, [string]$ParentPidPath)
                [IO.File]::WriteAllText($ParentPidPath, [string]$PID)
                $info = [Diagnostics.ProcessStartInfo]::new((Get-Process -Id $PID).Path)
                $info.UseShellExecute = $false
                $info.CreateNoWindow = $true
                foreach ($argument in @('-NoProfile', '-File', $ChildScript, $ReleasePath, $ChildPidPath)) {
                    $info.ArgumentList.Add($argument)
                }
                $child = [Diagnostics.Process]::Start($info)
                $child.Dispose()
                """);

            var arguments = new[] { "-NoProfile", "-File", parentScript, childScript, releasePath, childPidPath, parentPidPath };
            if (useGitFixture)
            {
                // Git aliases execute through a shell; quote each fixture path independently.
                var alias = "!pwsh " + string.Join(" ", arguments.Select(argument =>
                    "'" + argument.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'"));
                runTask = GitFixtureRunner.RunGitCaptureAsync(fixtureRoot,
                    "-c", "alias.retain-pipes=" + alias, "retain-pipes");
            }
            else
            {
                runTask = PwshScriptRunner.RunAsync(
                    arguments,
                    timeout: callerCancellation ? null : TimeSpan.FromSeconds(15),
                    cancellationToken: cancellation.Token,
                    description: "inherited-pipe fixture");
            }
            Assert.IsTrue(await WaitForFileAsync(childPidPath, TimeSpan.FromSeconds(30)), "Descendant must start.");
            var parentPid = int.Parse(await File.ReadAllTextAsync(parentPidPath), System.Globalization.CultureInfo.InvariantCulture);
            Assert.IsTrue(await WaitForProcessExitAsync(parentPid), "Parent must exit before cancellation.");
            Assert.IsFalse(runTask.IsCompleted, "The descendant must retain its parent's redirected pipes.");

            if (callerCancellation)
            {
                cancellation.Cancel();
                await Assert.ThrowsAsync<OperationCanceledException>(() => runTask.WaitAsync(TimeSpan.FromSeconds(30)));
                Assert.IsTrue(runTask.IsCanceled, "The runner must preserve caller cancellation after its drain budget.");
            }
            else
            {
                var exception = await Assert.ThrowsExactlyAsync<TimeoutException>(() => runTask.WaitAsync(TimeSpan.FromSeconds(45)));
                Assert.IsTrue(runTask.IsCompleted, "The runner itself must finish before the test's safety timeout.");
                StringAssert.Contains(exception.Message, useGitFixture ? "timed out after 30 seconds" : "inherited-pipe fixture timed out");
                AssertTimeoutDiagnostics(exception, inheritedPipes: true);
            }
        }
        finally
        {
            await File.WriteAllTextAsync(releasePath, string.Empty);
            cancellation.Cancel();
            await StopFixtureChildAsync(childPidPath);
            if (runTask is not null)
            {
                try { await runTask.WaitAsync(TimeSpan.FromSeconds(30)); }
                catch (OperationCanceledException) { /* Expected runner cancellation. */ }
                catch (TimeoutException) when (runTask.IsCompleted) { /* Expected runner timeout. */ }
            }
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task GitFixture_PreservesArgumentsAndReportsNonzeroExit()
    {
        var fixtureRoot = CreateFixtureRoot();
        try
        {
            var value = "alpha beta; semi'quote";
            var output = await GitFixtureRunner.RunGitCaptureAsync(fixtureRoot,
                "-c", "fixture.value=" + value, "config", "--get", "fixture.value");
            Assert.AreEqual(value, output.TrimEnd('\r', '\n'));
            var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                GitFixtureRunner.RunGitCaptureAsync(fixtureRoot, "--not-a-git-option"));
            StringAssert.Contains(exception.Message, "exited 129");
            StringAssert.Contains(exception.Message, "stderr=[unknown option");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    private static void AssertTimeoutDiagnostics(TimeoutException exception, bool inheritedPipes)
    {
        var message = exception.Message;
        StringAssert.Matches(message, new Regex(@"elapsedMs=\d+; phaseElapsedMs=\d+"));
        StringAssert.Contains(message, "timeoutRequested=True; callerCancellationRequested=False");
        StringAssert.Matches(message, new Regex(@"sampleUtc=\d{4}-\d{2}-\d{2}T[^;]*Z"));
        StringAssert.Matches(message, new Regex(@"processStartUtc=\d{4}-\d{2}-\d{2}T[^;]*Z"));
        StringAssert.Contains(message, OperatingSystem.IsWindows()
            ? "exitTimeSource=windows-kernel"
            : "exitTimeSource=runtime-wait-observation");
        if (message.Contains("processExited=True", StringComparison.Ordinal))
            StringAssert.Matches(message, new Regex(@"processExitUtc=\d{4}-\d{2}-\d{2}T[^;]*Z"));
        else
            StringAssert.Contains(message, "processExitUtc=running");

        if (inheritedPipes)
        {
            StringAssert.Contains(message, "phase=output drain");
            StringAssert.Contains(message, "processExited=True");
        }

        foreach (var stream in new[] { "stdout", "stderr" })
        {
            var state = OperatingSystem.IsWindows()
                ? inheritedPipes ? @"available; bytes=\d+" : @"(?:available; bytes=\d+|eof; error=109)"
                : "unsupported";
            StringAssert.Matches(message, new Regex(
                stream + @"Pipe=\{sampleElapsedMs=\d+; state=" + state + @"\}"));
        }
    }

    private static async Task StopFixtureChildAsync(string childPidPath)
    {
        if (!File.Exists(childPidPath))
            return;
        var childPid = int.Parse(await File.ReadAllTextAsync(childPidPath), System.Globalization.CultureInfo.InvariantCulture);
        Process child;
        try { child = Process.GetProcessById(childPid); }
        catch (ArgumentException) { return; } // The owned fixture already exited.
        using (child)
        {
            try
            {
                if (!child.HasExited)
                    child.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) when (child.HasExited) { /* Exit raced with cleanup. */ }
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private static string CreateFixtureRoot()
    {
        var path = Path.Combine(TestTempRoot.Current, nameof(PwshScriptRunnerTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task<bool> WaitForProcessExitAsync(int processId)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (process.HasExited)
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
                return true;
            }

            await Task.Delay(100);
        }

        return false;
    }

    private static async Task<bool> WaitForFileAsync(string path, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (File.Exists(path))
            {
                return true;
            }

            await Task.Delay(100);
        }

        return File.Exists(path);
    }
}
