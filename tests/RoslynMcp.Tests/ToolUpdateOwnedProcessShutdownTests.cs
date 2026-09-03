using System.Diagnostics;
using System.Globalization;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// Covers <c>eng/stop-owned-tool-store-process.ps1</c>, the script that makes the owned-process
/// shutdown guard reachable from <c>just tool-update</c> (previously only the local-pack path via
/// <c>eng/reinstall-local-tool.ps1</c> could stop an owned process). The critical new behavior
/// this row adds over the prior inlined guard: an image-path-under-tool-store check that can tell
/// the Layer 1 shim (running from the tool store) apart from the plugin's <c>dnx</c>-launched
/// Layer 2 server (same process name, different origin) — the old guard, keyed on process name
/// alone, could not make that distinction.
/// </summary>
[TestClass]
public sealed class ToolUpdateOwnedProcessShutdownTests
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(30);

    [TestMethod]
    public void SharedScript_DiscoversProcessesViaCimInsteadOfProcessNameLookup()
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(repositoryRoot, "eng", "stop-owned-tool-store-process.ps1"));

        Assert.IsFalse(
            script.Contains("Get-Process -Name", StringComparison.OrdinalIgnoreCase),
            "Tool-store lock discovery must use Get-CimInstance Win32_Process, not name-based Get-Process, so it can see every holder including ones this process cannot enumerate by name alone.");
        Assert.IsFalse(script.Contains("taskkill", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(script.Contains("killall", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(script, "Get-CimInstance Win32_Process");
        StringAssert.Contains(script, "function Stop-OwnedToolStoreProcess");
        StringAssert.Contains(script, "function Assert-ToolStoreUnlocked");
    }

    [TestMethod]
    public void ReinstallScript_DotSourcesSharedScriptAndNoLongerInlinesTheGuard()
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(repositoryRoot, "eng", "reinstall-local-tool.ps1"));

        StringAssert.Contains(script, "stop-owned-tool-store-process.ps1");
        StringAssert.Contains(script, "Stop-OwnedToolStoreProcess");
        Assert.IsFalse(
            script.Contains("function Stop-OwnedRoslynMcpProcess", StringComparison.Ordinal),
            "The ownership guard must be extracted, not duplicated, so eng/reinstall-local-tool.ps1 and " +
            "eng/stop-owned-tool-store-process.ps1 cannot drift out of sync.");
    }

    [TestMethod]
    public void Justfile_ToolUpdateRecipe_RunsSharedScriptBeforeMutatingTheToolStore()
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var justfile = File.ReadAllText(Path.Combine(repositoryRoot, "justfile"));

        var recipeStart = justfile.IndexOf("tool-update:", StringComparison.Ordinal);
        Assert.AreNotEqual(-1, recipeStart, "justfile must declare a tool-update recipe.");
        var recipeEnd = justfile.IndexOf("\n\n", recipeStart, StringComparison.Ordinal);
        var recipeBody = recipeEnd == -1
            ? justfile[recipeStart..]
            : justfile[recipeStart..recipeEnd];

        var shutdownIndex = recipeBody.IndexOf("stop-owned-tool-store-process.ps1", StringComparison.Ordinal);
        var updateIndex = recipeBody.IndexOf("dotnet tool update", StringComparison.Ordinal);
        Assert.AreNotEqual(-1, shutdownIndex, "tool-update must invoke the shared shutdown/assert script.");
        Assert.AreNotEqual(-1, updateIndex, "tool-update must still run dotnet tool update.");
        Assert.IsTrue(
            shutdownIndex < updateIndex,
            "The shutdown/assert script must run before dotnet tool update mutates the tool store.");
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task StopOwnedToolStoreProcess_RefusesTerminationWhenImagePathIsOutsideToolStoreRoot()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The tool-store image-path attribution contract is Windows-specific.");
        }

        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var fixtureRoot = Path.Combine(
            TestTempRoot.Current,
            nameof(ToolUpdateOwnedProcessShutdownTests),
            "outside",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixtureRoot);
        var unrelatedStoreRoot = Path.Combine(fixtureRoot, "unrelated-store");
        Directory.CreateDirectory(unrelatedStoreRoot);

        Process? fixtureProcess = null;
        try
        {
            // The fixture process's image lives OUTSIDE the tool store root — this simulates the
            // plugin's dnx-launched Layer 2 server, which shares the roslynmcp.exe process name
            // but never runs from the tool store.
            fixtureProcess = StartRoslynMcpFixtureProcess(Path.Combine(fixtureRoot, "layer2-like"));
            var startedAtUtc = fixtureProcess.StartTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            var wrapperPath = await WriteWrapperAsync(fixtureRoot);

            var result = await PwshScriptRunner.RunAsync(
                [
                    "-NoProfile",
                    "-File",
                    wrapperPath,
                    "-SharedScript",
                    Path.Combine(repositoryRoot, "eng", "stop-owned-tool-store-process.ps1"),
                    "-Mode",
                    "Stop",
                    "-OwnedProcessId",
                    fixtureProcess.Id.ToString(CultureInfo.InvariantCulture),
                    "-OwnedProcessStartedAtUtc",
                    startedAtUtc,
                    "-ToolStoreRoot",
                    unrelatedStoreRoot,
                ],
                workingDirectory: fixtureRoot,
                timeout: ProcessTimeout,
                description: "tool-store shutdown outside-store fixture");

            Assert.AreNotEqual(0, result.ExitCode, "Termination must be refused when the image path is outside the tool store root." + Environment.NewLine + result.AllOutput);
            StringAssert.Contains(result.AllOutput, "not under the tool store root");
            fixtureProcess.Refresh();
            Assert.IsFalse(fixtureProcess.HasExited, "A process outside the tool store root must never be terminated, even with a matching PID and start time.");
        }
        finally
        {
            await TerminateIfRunningAsync(fixtureProcess);
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task AssertToolStoreUnlocked_FailsClosedNamingHolderWithoutTerminatingIt()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The tool-store lock assertion contract is Windows-specific.");
        }

        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var fixtureRoot = Path.Combine(
            TestTempRoot.Current,
            nameof(ToolUpdateOwnedProcessShutdownTests),
            "locked",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixtureRoot);
        var toolStoreRoot = Path.Combine(fixtureRoot, "store");
        Directory.CreateDirectory(toolStoreRoot);

        Process? holderProcess = null;
        try
        {
            holderProcess = StartRoslynMcpFixtureProcess(Path.Combine(toolStoreRoot, "holder"));
            var wrapperPath = await WriteWrapperAsync(fixtureRoot);

            var result = await PwshScriptRunner.RunAsync(
                [
                    "-NoProfile",
                    "-File",
                    wrapperPath,
                    "-SharedScript",
                    Path.Combine(repositoryRoot, "eng", "stop-owned-tool-store-process.ps1"),
                    "-Mode",
                    "Assert",
                    "-ToolStoreRoot",
                    toolStoreRoot,
                ],
                workingDirectory: fixtureRoot,
                timeout: ProcessTimeout,
                description: "tool-store assert-unlocked fixture");

            Assert.AreNotEqual(0, result.ExitCode, "A held tool store must fail closed." + Environment.NewLine + result.AllOutput);
            StringAssert.Contains(result.AllOutput, "still locked");
            StringAssert.Contains(result.AllOutput, holderProcess.Id.ToString(CultureInfo.InvariantCulture));
            holderProcess.Refresh();
            Assert.IsFalse(holderProcess.HasExited, "Assert-ToolStoreUnlocked must never terminate a holder — it only detects and names it.");
        }
        finally
        {
            await TerminateIfRunningAsync(holderProcess);
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task AssertToolStoreUnlocked_ReturnsWithoutThrowingWhenToolStoreRootDoesNotExistYet()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The tool-store lock assertion contract is Windows-specific.");
        }

        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var fixtureRoot = Path.Combine(
            TestTempRoot.Current,
            nameof(ToolUpdateOwnedProcessShutdownTests),
            "missing",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixtureRoot);
        var neverCreatedToolStoreRoot = Path.Combine(fixtureRoot, "never-created-store");

        try
        {
            var wrapperPath = await WriteWrapperAsync(fixtureRoot);

            var result = await PwshScriptRunner.RunAsync(
                [
                    "-NoProfile",
                    "-File",
                    wrapperPath,
                    "-SharedScript",
                    Path.Combine(repositoryRoot, "eng", "stop-owned-tool-store-process.ps1"),
                    "-Mode",
                    "Assert",
                    "-ToolStoreRoot",
                    neverCreatedToolStoreRoot,
                ],
                workingDirectory: fixtureRoot,
                timeout: ProcessTimeout,
                description: "tool-store assert-unlocked missing-root fixture");

            Assert.AreEqual(0, result.ExitCode, "A fresh machine with no tool store yet must not fail closed." + Environment.NewLine + result.AllOutput);
            StringAssert.Contains(result.AllOutput, "NO_THROW");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    private static Process StartRoslynMcpFixtureProcess(string executableDirectory)
    {
        Directory.CreateDirectory(executableDirectory);
        var executablePath = Path.Combine(executableDirectory, "roslynmcp.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "PING.EXE"), executablePath);
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("127.0.0.1");
        startInfo.ArgumentList.Add("-t");
        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the controlled roslynmcp process fixture.");
        if (process.WaitForExit(250))
        {
            throw new InvalidOperationException("The controlled roslynmcp process fixture exited before the test began.");
        }

        Assert.AreEqual("roslynmcp", process.ProcessName, ignoreCase: true);
        return process;
    }

    private static async Task<string> WriteWrapperAsync(string fixtureRoot)
    {
        var wrapperPath = Path.Combine(fixtureRoot, "invoke-shared-script.ps1");
        await File.WriteAllTextAsync(
            wrapperPath,
            """
            param(
                [string]$SharedScript,
                [string]$Mode,
                [int]$OwnedProcessId = 0,
                [string]$OwnedProcessStartedAtUtc = '',
                [string]$ToolStoreRoot = ''
            )

            . $SharedScript

            try {
                switch ($Mode) {
                    'Stop' {
                        Stop-OwnedToolStoreProcess `
                            -OwnedProcessId $OwnedProcessId `
                            -OwnedProcessStartedAtUtc $OwnedProcessStartedAtUtc `
                            -ToolStoreRoot $ToolStoreRoot
                        Write-Output 'NO_THROW'
                    }
                    'Assert' {
                        Assert-ToolStoreUnlocked -ToolStoreRoot $ToolStoreRoot
                        Write-Output 'NO_THROW'
                    }
                    default {
                        throw "Unknown mode: $Mode"
                    }
                }
                exit 0
            }
            catch {
                Write-Output "THREW:$($_.Exception.Message)"
                exit 1
            }
            """);
        return wrapperPath;
    }

    private static async Task TerminateIfRunningAsync(Process? process)
    {
        if (process is null)
        {
            return;
        }

        using (process)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
