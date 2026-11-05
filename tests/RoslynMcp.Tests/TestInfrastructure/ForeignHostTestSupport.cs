using System.Diagnostics;

namespace RoslynMcp.Tests;

/// <summary>
/// Windows-only stand-ins for "another host" used by the analyzer-shadow sweep tests: a live peer
/// process whose PID names a creator directory, and a separate process that holds one file open.
/// One owner for the cmd.exe launch mechanics so a PATH-independence fix lands once.
/// </summary>
internal static class ForeignHostTestSupport
{
    /// <summary>
    /// Starts a cmd.exe peer that prints <c>ready</c> and then blocks on stdin. It stays alive until its
    /// stdin is closed or it is killed; read the barrier with <see cref="ReadReadyAsync"/>.
    /// </summary>
    internal static Process StartPeer(string workingDirectory)
    {
        var startInfo = new ProcessStartInfo("cmd.exe")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("echo ready & set /p RMCP_SHADOW_WAIT=");
        return Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start owned peer.");
    }

    internal static async Task ReadReadyAsync(Process peer)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Assert.AreEqual("ready", (await peer.StandardOutput.ReadLineAsync(cancellation.Token))?.Trim());
    }

    /// <summary>Backdates a directory past the sweeper's staleness window. Call after the last write into it.</summary>
    internal static void AgeBeyondStaleLease(string directory)
    {
        var timestamp = DateTime.UtcNow - TimeSpan.FromDays(2);
        Directory.SetCreationTimeUtc(directory, timestamp);
        Directory.SetLastWriteTimeUtc(directory, timestamp);
    }

    /// <summary>
    /// A separate process that holds a file open until killed: the group-wide <c>9&gt;&gt;</c>
    /// redirection keeps the file open for the group's lifetime, which is all an exclusive-open
    /// ownership probe needs to see a live holder. cmd.exe starts in milliseconds, unlike a
    /// PowerShell cold start that exceeded the ready deadline on loaded hosted runners.
    /// </summary>
    internal sealed class FileHolder : IDisposable
    {
        private readonly string _readyPath = Path.Combine(TestTempRoot.Current, "file-holder-ready-" + Guid.NewGuid().ToString("N"));
        private readonly string _scriptPath = Path.Combine(TestTempRoot.Current, "file-holder-" + Guid.NewGuid().ToString("N") + ".cmd");
        private Process? _process;

        public bool HasExited => _process?.HasExited ?? true;

        public async Task StartAsync(string filePath)
        {
            // ping.exe by absolute path: a bare `ping` resolves through the inherited PATH, and a
            // session without System32 on it ends the group at once, silently releasing the file.
            File.WriteAllText(
                _scriptPath,
                "@echo off\r\n" +
                "( (echo ready>\"%RMCP_HOLD_READY%\") & \"%SystemRoot%\\System32\\ping.exe\" -n 120 127.0.0.1 >nul ) 9>>\"%RMCP_HOLD_FILE%\"\r\n");
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _scriptPath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                }
            };
            process.StartInfo.Environment["RMCP_HOLD_FILE"] = filePath;
            process.StartInfo.Environment["RMCP_HOLD_READY"] = _readyPath;
            _process = process;
            Assert.IsTrue(process.Start(), "The file holder must start.");
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            while (!File.Exists(_readyPath) && !process.HasExited && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }

            Assert.IsTrue(File.Exists(_readyPath),
                process.HasExited ? await process.StandardError.ReadToEndAsync() : "The holder did not open the file.");
        }

        /// <summary>Kills the holder tree. The handle can close a few milliseconds after the exit is observable.</summary>
        public void Kill()
        {
            if (_process is { HasExited: false } running)
            {
                running.Kill(entireProcessTree: true);
                running.WaitForExit(10_000);
            }
        }

        public void Dispose()
        {
            Kill();
            _process?.Dispose();
            File.Delete(_readyPath);
            File.Delete(_scriptPath);
        }
    }
}
