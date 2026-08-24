using System.Diagnostics;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NuGetAuditGateTests
{
    [TestMethod]
    public void LocalAndHostedGates_UseSharedFailClosedRestoreAudit()
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(repositoryRoot, "eng", "verify-nuget-audit.ps1"));
        // Lowercase 'justfile' matches the on-disk name. A capitalized spelling resolves on
        // case-insensitive Windows but throws on the case-sensitive Linux publish runner.
        var justfile = File.ReadAllText(Path.Combine(repositoryRoot, "justfile"));
        var workflow = File.ReadAllText(Path.Combine(repositoryRoot, ".github", "workflows", "ci.yml"));

        StringAssert.Contains(script, "--force-evaluate");
        StringAssert.Contains(script, "-p:NuGetAudit=true");
        StringAssert.Contains(script, "-p:NuGetAuditMode=all");
        foreach (var warningCode in new[] { "NU1900", "NU1901", "NU1902", "NU1903", "NU1904" })
        {
            StringAssert.Contains(script, warningCode);
        }

        StringAssert.Contains(justfile, "./eng/verify-nuget-audit.ps1");
        StringAssert.Contains(workflow, "./eng/verify-nuget-audit.ps1 -SolutionPath RoslynMcp.slnx");
        Assert.IsFalse(justfile.Contains("package list", StringComparison.Ordinal));
        Assert.IsFalse(workflow.Contains("package list --project RoslynMcp.slnx --vulnerable", StringComparison.Ordinal));
    }

    [TestMethod]
    [TestCategory("Process")]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task DefaultSolutionPath_ResolvesFromRepository_WhenCallerWorkingDirectoryIsUnrelated()
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var fixtureRoot = Path.Combine(TestTempRoot.Current, "NuGetAudit", Guid.NewGuid().ToString("N"));
        var fakeBin = Path.Combine(fixtureRoot, "bin");
        var unrelatedWorkingDirectory = Path.Combine(fixtureRoot, "unrelated");
        var capturePath = Path.Combine(fixtureRoot, "arguments.txt");
        Directory.CreateDirectory(fakeBin);
        Directory.CreateDirectory(unrelatedWorkingDirectory);

        try
        {
            WriteFakeDotnet(fakeBin);
            var result = await RunAuditScriptAsync(
                repositoryRoot,
                unrelatedWorkingDirectory,
                fakeBin,
                capturePath);

            Assert.AreEqual(0, result.ExitCode, result.AllOutput);
            var arguments = NormalizeCapturedAuditArguments(await File.ReadAllLinesAsync(capturePath));
            CollectionAssert.AreEqual(
                ExpectedAuditArguments(Path.GetFullPath(Path.Combine(repositoryRoot, "RoslynMcp.slnx"))),
                arguments,
                $"Actual arguments: [{string.Join(" | ", arguments)}]");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    [TestMethod]
    [TestCategory("Process")]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task AbsoluteSolutionPath_IsPreservedAtDotnetBoundary()
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var fixtureRoot = Path.Combine(TestTempRoot.Current, "NuGetAudit", Guid.NewGuid().ToString("N"));
        var fakeBin = Path.Combine(fixtureRoot, "bin");
        var unrelatedWorkingDirectory = Path.Combine(fixtureRoot, "unrelated");
        var absoluteSolutionPath = Path.Combine(fixtureRoot, "absolute.slnx");
        var capturePath = Path.Combine(fixtureRoot, "arguments.txt");
        Directory.CreateDirectory(fakeBin);
        Directory.CreateDirectory(unrelatedWorkingDirectory);
        await File.WriteAllTextAsync(absoluteSolutionPath, "<Solution />");

        try
        {
            WriteFakeDotnet(fakeBin);
            var result = await RunAuditScriptAsync(
                repositoryRoot,
                unrelatedWorkingDirectory,
                fakeBin,
                capturePath,
                absoluteSolutionPath);

            Assert.AreEqual(0, result.ExitCode, result.AllOutput);
            var arguments = NormalizeCapturedAuditArguments(await File.ReadAllLinesAsync(capturePath));
            CollectionAssert.AreEqual(
                ExpectedAuditArguments(Path.GetFullPath(absoluteSolutionPath)),
                arguments,
                $"Actual arguments: [{string.Join(" | ", arguments)}]");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    [TestMethod]
    [TestCategory("Process")]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task DotnetAuditFailure_PropagatesAfterUsingFailClosedArguments()
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var fixtureRoot = Path.Combine(TestTempRoot.Current, "NuGetAudit", Guid.NewGuid().ToString("N"));
        var fakeBin = Path.Combine(fixtureRoot, "bin");
        var workingDirectory = Path.Combine(fixtureRoot, "working");
        var solutionPath = Path.Combine(fixtureRoot, "failure.slnx");
        var capturePath = Path.Combine(fixtureRoot, "arguments.txt");
        Directory.CreateDirectory(fakeBin);
        Directory.CreateDirectory(workingDirectory);
        await File.WriteAllTextAsync(solutionPath, "<Solution />");

        try
        {
            WriteFakeDotnet(fakeBin, exitCode: 23);
            var result = await RunAuditScriptAsync(
                repositoryRoot,
                workingDirectory,
                fakeBin,
                capturePath,
                solutionPath);

            Assert.AreNotEqual(0, result.ExitCode, "A failed restore audit must propagate as verifier failure.");
            StringAssert.Contains(result.AllOutput, "NuGet vulnerability audit failed with exit code 23");
            var arguments = NormalizeCapturedAuditArguments(await File.ReadAllLinesAsync(capturePath));
            CollectionAssert.AreEqual(
                ExpectedAuditArguments(Path.GetFullPath(solutionPath)),
                arguments,
                $"Actual arguments: [{string.Join(" | ", arguments)}]");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    [TestMethod]
    [TestCategory("Process")]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task MissingRelativeSolution_FailsBeforeDotnetAndIgnoresCallerDirectory()
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var fixtureRoot = Path.Combine(TestTempRoot.Current, "NuGetAudit", Guid.NewGuid().ToString("N"));
        var fakeBin = Path.Combine(fixtureRoot, "bin");
        var unrelatedWorkingDirectory = Path.Combine(fixtureRoot, "unrelated");
        var capturePath = Path.Combine(fixtureRoot, "arguments.txt");
        const string relativeSolutionPath = "caller-owned.slnx";
        Directory.CreateDirectory(fakeBin);
        Directory.CreateDirectory(unrelatedWorkingDirectory);
        await File.WriteAllTextAsync(Path.Combine(unrelatedWorkingDirectory, relativeSolutionPath), "<Solution />");

        try
        {
            WriteFakeDotnet(fakeBin);
            var result = await RunAuditScriptAsync(
                repositoryRoot,
                unrelatedWorkingDirectory,
                fakeBin,
                capturePath,
                relativeSolutionPath);

            Assert.AreNotEqual(0, result.ExitCode, "A missing repository-owned solution must fail closed.");
            var expectedPath = Path.GetFullPath(Path.Combine(repositoryRoot, relativeSolutionPath));
            StringAssert.Contains(result.AllOutput, expectedPath);
            Assert.IsFalse(File.Exists(capturePath), "dotnet must not run when the resolved solution is absent.");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    private static async Task<ProcessResult> RunAuditScriptAsync(
        string repositoryRoot,
        string workingDirectory,
        string fakeBin,
        string capturePath,
        string? solutionPath = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(Path.Combine(repositoryRoot, "eng", "verify-nuget-audit.ps1"));
        if (solutionPath is not null)
        {
            startInfo.ArgumentList.Add("-SolutionPath");
            startInfo.ArgumentList.Add(solutionPath);
        }
        startInfo.Environment["CAPTURE_PATH"] = capturePath;
        startInfo.Environment["PATH"] = fakeBin + Path.PathSeparator + startInfo.Environment["PATH"];

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start NuGet audit verifier.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("NuGet audit verifier timed out after 20 seconds.");
        }

        return new(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private sealed record ProcessResult(int ExitCode, string StdOut, string StdErr)
    {
        public string AllOutput => PowerShellOutputNormalizer.Normalize(StdOut + Environment.NewLine + StdErr);
    }

    private static string[] ExpectedAuditArguments(string solutionPath) =>
    [
        "restore",
        solutionPath,
        "--force-evaluate",
        "--verbosity",
        "minimal",
        "-p:NuGetAudit=true",
        "-p:NuGetAuditMode=all",
        "-p:WarningsAsErrors=NU1900%3BNU1901%3BNU1902%3BNU1903%3BNU1904",
    ];

    private static string[] NormalizeCapturedAuditArguments(IReadOnlyList<string> arguments)
    {
        var normalized = new List<string>(arguments.Count);
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            // PowerShell's legacy .cmd transport splits `-p:Name=value`; the real
            // dotnet executable receives one argument. Recompose only that known
            // property shape so the cross-platform test asserts the logical argv.
            if (argument.StartsWith("-p:", StringComparison.Ordinal) &&
                !argument.Contains('=') &&
                index + 1 < arguments.Count)
            {
                normalized.Add($"{argument}={arguments[++index]}");
                continue;
            }

            normalized.Add(argument);
        }

        return normalized.ToArray();
    }

    private static void WriteFakeDotnet(string fakeBin, int exitCode = 0)
    {
        if (OperatingSystem.IsWindows())
        {
            File.WriteAllText(
                Path.Combine(fakeBin, "dotnet.cmd"),
                "@echo off\r\n" +
                ":capture\r\n" +
                "if \"%~1\"==\"\" goto done\r\n" +
                ">>\"%CAPTURE_PATH%\" echo %~1\r\n" +
                "shift\r\n" +
                "goto capture\r\n" +
                ":done\r\n" +
                $"exit /b {exitCode}\r\n");
            return;
        }

        var path = Path.Combine(fakeBin, "dotnet");
        File.WriteAllText(path, $"#!/bin/sh\nprintf '%s\\n' \"$@\" > \"$CAPTURE_PATH\"\nexit {exitCode}\n");
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}
