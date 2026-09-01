using System.Diagnostics;

namespace RoslynMcp.Tests.Helpers;

[TestClass]
public sealed class PwshScriptRunnerTests
{
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
        try
        {
            var childPidPath = Path.Combine(fixtureRoot, "child.pid");
            var scriptPath = Path.Combine(fixtureRoot, "spawn-child.ps1");
            await File.WriteAllTextAsync(
                scriptPath,
                "param([string]$ChildPidPath)\n" +
                "$pwsh = (Get-Process -Id $PID).Path\n" +
                "$child = Start-Process -FilePath $pwsh -ArgumentList '-NoProfile','-Command','Start-Sleep -Seconds 30' -PassThru\n" +
                "$child.Id | Set-Content -LiteralPath $ChildPidPath\n" +
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
            }

            Assert.IsTrue(File.Exists(childPidPath), "The fixture did not start its child process.");
            var childPid = int.Parse(await File.ReadAllTextAsync(childPidPath), System.Globalization.CultureInfo.InvariantCulture);
            Assert.IsTrue(
                await WaitForProcessExitAsync(childPid),
                $"Child process {childPid} survived process-tree termination.");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
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
