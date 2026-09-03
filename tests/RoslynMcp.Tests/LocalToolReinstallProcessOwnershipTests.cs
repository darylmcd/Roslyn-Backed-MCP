using System.Diagnostics;
using System.Globalization;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class LocalToolReinstallProcessOwnershipTests
{
    private static readonly TimeSpan _processTimeout = TimeSpan.FromSeconds(30);

    [TestMethod]
    [TestCategory("Process")]
    public async Task ReinstallScript_StopsOwnedProcessBeforeToolMutationAndPreservesUnrelatedProcess()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The global-tool executable lock and ownership contract is Windows-specific.");
        }

        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var fixtureRoot = Path.Combine(
            TestTempRoot.Current,
            nameof(LocalToolReinstallProcessOwnershipTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixtureRoot);

        Process? ownedProcess = null;
        Process? unrelatedProcess = null;
        try
        {
            ownedProcess = StartNamedRoslynMcpProcess(fixtureRoot, "owned");
            unrelatedProcess = StartNamedRoslynMcpProcess(fixtureRoot, "unrelated");
            var ownedStartUtc = ownedProcess.StartTime
                .ToUniversalTime()
                .ToString("O", CultureInfo.InvariantCulture);
            var invocationLogPath = Path.Combine(fixtureRoot, "dotnet-invocations.log");
            var wrapperPath = await WriteWrapperAsync(fixtureRoot);

            var result = await PwshScriptRunner.RunAsync(
                [
                    "-NoProfile",
                    "-File",
                    wrapperPath,
                    "-ReinstallScript",
                    Path.Combine(repositoryRoot, "eng", "reinstall-local-tool.ps1"),
                    "-PackageSource",
                    fixtureRoot,
                    "-ProjectPath",
                    Path.Combine(repositoryRoot, "src", "RoslynMcp.Host.Stdio", "RoslynMcp.Host.Stdio.csproj"),
                    "-OwnedProcessId",
                    ownedProcess.Id.ToString(CultureInfo.InvariantCulture),
                    "-OwnedProcessStartedAtUtc",
                    ownedStartUtc,
                    "-ToolStoreRoot",
                    fixtureRoot,
                    "-InvocationLogPath",
                    invocationLogPath,
                ],
                workingDirectory: fixtureRoot,
                timeout: _processTimeout,
                description: "local tool reinstall ownership fixture");

            Assert.AreEqual(0, result.ExitCode, result.AllOutput);
            await ownedProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsTrue(ownedProcess.HasExited, "The explicitly owned roslynmcp process survived reinstall preparation.");
            Assert.IsFalse(unrelatedProcess.HasExited, "An unrelated roslynmcp process was terminated.");

            var invocations = await File.ReadAllLinesAsync(invocationLogPath);
            Assert.HasCount(5, invocations);
            Assert.IsTrue(
                invocations[0].StartsWith("ownedAlive=True|msbuild -nologo", StringComparison.Ordinal),
                "Read-only version discovery should occur before shutdown." + Environment.NewLine +
                string.Join(Environment.NewLine, invocations));
            Assert.IsTrue(
                invocations.Skip(1).All(line => line.StartsWith("ownedAlive=False|", StringComparison.Ordinal)),
                "Every tool mutation must occur after the owned process exits." + Environment.NewLine +
                string.Join(Environment.NewLine, invocations));
            StringAssert.Contains(invocations[1], "tool list -g --format json");
            StringAssert.Contains(invocations[2], "tool uninstall -g Darylmcd.RoslynMcp");
            StringAssert.Contains(invocations[3], "tool uninstall -g RoslynMcp");
            StringAssert.Contains(invocations[4], "tool install -g Darylmcd.RoslynMcp");
        }
        finally
        {
            await TerminateIfRunningAsync(ownedProcess);
            await TerminateIfRunningAsync(unrelatedProcess);
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    [TestMethod]
    public void ReinstallEntrypoints_UseOwnershipScopedHelperWithoutNameWideTermination()
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var project = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "RoslynMcp.Host.Stdio",
            "RoslynMcp.Host.Stdio.csproj"));
        var justfile = File.ReadAllText(Path.Combine(repositoryRoot, "justfile"));
        var script = File.ReadAllText(Path.Combine(repositoryRoot, "eng", "reinstall-local-tool.ps1"));
        var sharedScript = File.ReadAllText(Path.Combine(repositoryRoot, "eng", "stop-owned-tool-store-process.ps1"));

        StringAssert.Contains(project, "reinstall-local-tool.ps1");
        StringAssert.Contains(project, "ReinstallToolProcessId");
        StringAssert.Contains(project, "ReinstallToolProcessStartedAtUtc");
        StringAssert.Contains(justfile, "reinstall-local-tool.ps1");
        StringAssert.Contains(script, "ROSLYNMCP_REINSTALL_PROCESS_ID");
        StringAssert.Contains(script, "ROSLYNMCP_REINSTALL_PROCESS_STARTED_AT_UTC");
        // The termination call itself now lives in the shared, dot-sourced script
        // (eng/stop-owned-tool-store-process.ps1) so eng/reinstall-local-tool.ps1 and
        // `just tool-update` cannot drift out of sync on how a PID gets stopped.
        StringAssert.Contains(sharedScript, "Stop-Process -Id $OwnedProcessId");
        Assert.IsFalse(project.Contains("taskkill", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(justfile.Contains("taskkill", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(script.Contains("Get-Process -Name", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(sharedScript.Contains("Get-Process -Name", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(script.Contains("killall", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(sharedScript.Contains("killall", StringComparison.OrdinalIgnoreCase));
    }

    private static Process StartNamedRoslynMcpProcess(string fixtureRoot, string instanceName)
    {
        var executableDirectory = Path.Combine(fixtureRoot, instanceName);
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
        var wrapperPath = Path.Combine(fixtureRoot, "invoke-reinstall.ps1");
        await File.WriteAllTextAsync(
            wrapperPath,
            """
            param(
                [string]$ReinstallScript,
                [string]$PackageSource,
                [string]$ProjectPath,
                [int]$OwnedProcessId,
                [string]$OwnedProcessStartedAtUtc,
                [string]$ToolStoreRoot,
                [string]$InvocationLogPath
            )

            function global:dotnet {
                $invocationArguments = @($args | ForEach-Object { [string]$_ })
                $ownedAlive = $null -ne (Get-Process -Id $OwnedProcessId -ErrorAction SilentlyContinue)
                Add-Content -LiteralPath $InvocationLogPath -Value "ownedAlive=$ownedAlive|$($invocationArguments -join ' ')"
                $global:LASTEXITCODE = 0
                if ($invocationArguments.Count -ge 1 -and $invocationArguments[0] -eq 'msbuild') {
                    Write-Output '9.9.9-test'
                    return
                }
                if ($invocationArguments.Count -ge 4 -and
                    $invocationArguments[0] -eq 'tool' -and
                    $invocationArguments[1] -eq 'list') {
                    Write-Output '{"version":1,"data":[{"packageId":"darylmcd.roslynmcp"},{"packageId":"roslynmcp"}]}'
                    return
                }

                Write-Output "fixture dotnet $($invocationArguments -join ' ')"
            }

            & $ReinstallScript `
                -PackageSource $PackageSource `
                -ProjectPath $ProjectPath `
                -OwnedProcessId $OwnedProcessId `
                -OwnedProcessStartedAtUtc $OwnedProcessStartedAtUtc `
                -ToolStoreRoot $ToolStoreRoot
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
