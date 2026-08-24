using System.Diagnostics;
using System.Text.RegularExpressions;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class PackageFamilyContractTests
{
    private const int ProcessTimeoutSeconds = 60;
    private const int CleanupTimeoutSeconds = 15;
    private const int TestTimeoutMilliseconds = 90_000;

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [TestCategory("Process")]
    [Timeout(TestTimeoutMilliseconds, CooperativeCancellation = true)]
    public async Task PackageFamilyGate_RejectsSplitMicrosoftBuildPinsBeforeRestore()
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var fixtureRoot = CreateFixtureRoot();
        try
        {
            var packagesPath = Path.Combine(fixtureRoot, "Directory.Packages.props");
            File.Copy(Path.Combine(repositoryRoot, "Directory.Packages.props"), packagesPath);
            var packages = await File.ReadAllTextAsync(packagesPath);
            var familyVersion = ReadCentralVersion(packagesPath, "Microsoft.Build");
            packages = packages.Replace(
                $"<PackageVersion Include=\"Microsoft.Build.Framework\" Version=\"{familyVersion}\" />",
                "<PackageVersion Include=\"Microsoft.Build.Framework\" Version=\"18.9.6-test\" />",
                StringComparison.Ordinal);
            await File.WriteAllTextAsync(packagesPath, packages);

            var result = await RunPowerShellAsync(
                repositoryRoot,
                fixtureRoot,
                "verify-package-family-parity.ps1",
                "-PackagesPath",
                packagesPath);

            Assert.AreNotEqual(0, result.ExitCode, "A split Microsoft.Build family must fail before restore.");
            StringAssert.Contains(result.AllOutput, "Microsoft.Build compile family pins must match");
            StringAssert.Contains(result.AllOutput, $"Microsoft.Build={familyVersion}");
            StringAssert.Contains(result.AllOutput, "Microsoft.Build.Framework=18.9.6-test");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    [TestMethod]
    [TestCategory("Process")]
    [Timeout(TestTimeoutMilliseconds, CooperativeCancellation = true)]
    public async Task UpgradeMatrixGate_RejectsNonProtocolPackageDriftWithBothVersions()
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var fixtureRoot = CreateFixtureRoot();
        try
        {
            var packagesPath = Path.Combine(fixtureRoot, "Directory.Packages.props");
            var matrixPath = Path.Combine(fixtureRoot, "upgrade-matrix.md");
            File.Copy(Path.Combine(repositoryRoot, "Directory.Packages.props"), packagesPath);
            File.Copy(Path.Combine(repositoryRoot, "docs", "upgrade-matrix.md"), matrixPath);
            var centralVersion = ReadCentralVersion(packagesPath, "Microsoft.Extensions.Logging");
            var matrix = await File.ReadAllTextAsync(matrixPath);
            matrix = matrix.Replace(
                $"| `Microsoft.Extensions.Logging` | `{centralVersion}` |",
                "| `Microsoft.Extensions.Logging` | `0.0.0-stale` |",
                StringComparison.Ordinal);
            await File.WriteAllTextAsync(matrixPath, matrix);

            var result = await RunPowerShellAsync(
                repositoryRoot,
                fixtureRoot,
                "verify-upgrade-matrix.ps1",
                "-PackagesPath",
                packagesPath,
                "-MatrixPath",
                matrixPath);

            Assert.AreNotEqual(0, result.ExitCode, "A stale non-MCP matrix row must fail parity.");
            StringAssert.Contains(result.AllOutput, "Microsoft.Extensions.Logging");
            StringAssert.Contains(result.AllOutput, "0.0.0-stale");
            StringAssert.Contains(result.AllOutput, centralVersion);
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    [TestMethod]
    [TestCategory("Process")]
    [DataRow("missing", "has no central-package row for 'Example.Package'")]
    [DataRow("duplicate-case-insensitive", "contains duplicate rows")]
    [DataRow("malformed-matrix", "malformed central-package row")]
    [DataRow("malformed-central", "without a non-empty Include and Version")]
    [DataRow("extra", "no matching")]
    [Timeout(TestTimeoutMilliseconds, CooperativeCancellation = true)]
    public async Task UpgradeMatrixGate_FailsClosedForInvalidBijection(
        string fixtureKind,
        string expectedDiagnostic)
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var fixtureRoot = CreateFixtureRoot();
        try
        {
            var packagesPath = Path.Combine(fixtureRoot, "Directory.Packages.props");
            var matrixPath = Path.Combine(fixtureRoot, "upgrade-matrix.md");
            var centralRow = fixtureKind == "malformed-central"
                ? "    <PackageVersion Include=\"Example.Package\" />"
                : "    <PackageVersion Include=\"Example.Package\" Version=\"1.2.3\" />";
            await File.WriteAllTextAsync(
                packagesPath,
                $"<Project><ItemGroup>{Environment.NewLine}{centralRow}{Environment.NewLine}</ItemGroup></Project>");

            var rows = fixtureKind switch
            {
                "missing" => string.Empty,
                "duplicate-case-insensitive" =>
                    MatrixRow("Example.Package", "1.2.3") + Environment.NewLine +
                    MatrixRow("example.package", "1.2.3"),
                "malformed-matrix" => "| `Example.Package` | 1.2.3 | `Directory.Packages.props` | malformed |",
                "extra" =>
                    MatrixRow("Example.Package", "1.2.3") + Environment.NewLine +
                    MatrixRow("Extra.Package", "9.9.9"),
                "malformed-central" => MatrixRow("Example.Package", "1.2.3"),
                _ => throw new ArgumentOutOfRangeException(nameof(fixtureKind), fixtureKind, "Unknown fixture kind."),
            };
            await File.WriteAllTextAsync(matrixPath, rows);

            var result = await RunPowerShellAsync(
                repositoryRoot,
                fixtureRoot,
                "verify-upgrade-matrix.ps1",
                "-PackagesPath",
                packagesPath,
                "-MatrixPath",
                matrixPath);

            Assert.AreNotEqual(0, result.ExitCode, $"Fixture '{fixtureKind}' must fail closed.");
            StringAssert.Contains(result.AllOutput, expectedDiagnostic);
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    [TestMethod]
    [TestCategory("Process")]
    [Timeout(TestTimeoutMilliseconds, CooperativeCancellation = true)]
    public async Task UpgradeMatrixGate_AcceptsExactMinimalBijection()
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var fixtureRoot = CreateFixtureRoot();
        try
        {
            var packagesPath = Path.Combine(fixtureRoot, "Directory.Packages.props");
            var matrixPath = Path.Combine(fixtureRoot, "upgrade-matrix.md");
            await File.WriteAllTextAsync(
                packagesPath,
                "<Project><ItemGroup><PackageVersion Include=\"Example.Package\" Version=\"1.2.3\" /></ItemGroup></Project>");
            await File.WriteAllTextAsync(matrixPath, MatrixRow("Example.Package", "1.2.3"));

            var result = await RunPowerShellAsync(
                repositoryRoot,
                fixtureRoot,
                "verify-upgrade-matrix.ps1",
                "-PackagesPath",
                packagesPath,
                "-MatrixPath",
                matrixPath);

            Assert.AreEqual(0, result.ExitCode, result.AllOutput);
            StringAssert.Contains(result.AllOutput, "passed for 1 central package pins");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    [TestMethod]
    public void Dependabot_RoutesCoordinatedFamiliesTogether_AndMcpOutsideRoutineServicing()
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var dependabot = File.ReadAllText(Path.Combine(repositoryRoot, ".github", "dependabot.yml"));
        var coordinatedGroups = new[]
        {
            (
                Block: GetYamlBlock(dependabot, "      msbuild-compile-family:", "      roslyn-api-family:"),
                Packages: new[]
                {
                    "Microsoft.Build",
                    "Microsoft.Build.Framework",
                    "Microsoft.Build.Tasks.Core",
                    "Microsoft.Build.Utilities.Core",
                }),
            (
                Block: GetYamlBlock(dependabot, "      roslyn-api-family:", "      extensions-runtime-family:"),
                Packages: new[]
                {
                    "Microsoft.CodeAnalysis.CSharp",
                    "Microsoft.CodeAnalysis.Analyzers",
                    "Microsoft.CodeAnalysis.CSharp.Workspaces",
                    "Microsoft.CodeAnalysis.CSharp.Features",
                    "Microsoft.CodeAnalysis.Features",
                    "Microsoft.CodeAnalysis.Workspaces.MSBuild",
                    "Microsoft.CodeAnalysis.CSharp.Scripting",
                }),
            (
                Block: GetYamlBlock(dependabot, "      extensions-runtime-family:", "      nuget-minor-patch:"),
                Packages: new[]
                {
                    "Microsoft.Extensions.Hosting",
                    "Microsoft.Extensions.Http",
                    "Microsoft.Extensions.Logging",
                    "Microsoft.Extensions.Logging.Console",
                }),
        };
        var routineStart = dependabot.IndexOf("      nuget-minor-patch:", StringComparison.Ordinal);
        Assert.IsTrue(routineStart >= 0, "The routine NuGet group was not found.");
        var routineGroup = dependabot[routineStart..];
        var excludeStart = routineGroup.IndexOf("        exclude-patterns:", StringComparison.Ordinal);
        Assert.IsTrue(excludeStart >= 0, "The routine NuGet exclude-patterns subsection was not found.");
        var routineExcludes = routineGroup[excludeStart..];

        foreach (var (group, packages) in coordinatedGroups)
        {
            Assert.IsFalse(group.Contains("update-types:", StringComparison.Ordinal));
            foreach (var packageId in packages)
            {
                StringAssert.Contains(group, $"- \"{packageId}\"");
                StringAssert.Contains(routineExcludes, $"- \"{packageId}\"");
            }
        }
        StringAssert.Contains(routineExcludes, "- \"ModelContextProtocol\"");

        var matrix = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "upgrade-matrix.md"));
        var mcpVersion = ReadCentralVersion(Path.Combine(repositoryRoot, "Directory.Packages.props"), "ModelContextProtocol");
        StringAssert.Contains(matrix, $"`ModelContextProtocol` | `{mcpVersion}` | `Directory.Packages.props` | Contract-sensitive; dedicated PR");
        StringAssert.Contains(matrix, "all supported raw-wire protocol eras");
    }

    [TestMethod]
    public void CryptographySecurityPin_HasSingleVersionSource()
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var centralPath = Path.Combine(repositoryRoot, "Directory.Packages.props");
        var centralDocument = System.Xml.Linq.XDocument.Load(centralPath, System.Xml.Linq.LoadOptions.PreserveWhitespace);
        var packageNode = centralDocument.Descendants("PackageVersion")
            .Single(element => string.Equals(
                (string?)element.Attribute("Include"),
                "System.Security.Cryptography.Xml",
                StringComparison.Ordinal));
        var centralRationale = packageNode.NodesBeforeSelf()
            .OfType<System.Xml.Linq.XComment>()
            .LastOrDefault()?.Value
            ?? throw new AssertFailedException("The central cryptography security rationale was not found.");
        var project = File.ReadAllText(Path.Combine(repositoryRoot, "src", "RoslynMcp.Roslyn", "RoslynMcp.Roslyn.csproj"));
        var commentStart = project.IndexOf("<!-- Direct security override", StringComparison.Ordinal);
        var commentEnd = project.IndexOf("-->", commentStart, StringComparison.Ordinal);
        Assert.IsTrue(commentStart >= 0 && commentEnd > commentStart, "The direct security override comment was not found.");
        var projectRationale = project[commentStart..(commentEnd + 3)];

        StringAssert.Contains(centralRationale, "Transitive security pin");
        StringAssert.Contains(centralRationale, "audited release");
        StringAssert.Contains(centralRationale, "known advisories");
        StringAssert.Contains(projectRationale, "Directory.Packages.props");
        foreach (var (location, rationale) in new[]
        {
            ("central package comment", centralRationale),
            ("consuming project comment", projectRationale),
        })
        {
            Assert.IsFalse(
                Regex.IsMatch(rationale, @"\b\d+\.\d+\.\d+\b", RegexOptions.CultureInvariant),
                $"The {location} must not duplicate the PackageVersion value.");
        }
    }

    private static string CreateFixtureRoot()
    {
        var path = Path.Combine(TestTempRoot.Current, "PackagePolicy", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string ReadCentralVersion(string packagesPath, string packageId)
    {
        var document = System.Xml.Linq.XDocument.Load(packagesPath);
        return document.Descendants("PackageVersion")
            .Single(element => string.Equals((string?)element.Attribute("Include"), packageId, StringComparison.Ordinal))
            .Attribute("Version")?.Value
            ?? throw new InvalidOperationException($"Central pin '{packageId}' has no Version value.");
    }

    private static string GetYamlBlock(string yaml, string startMarker, string endMarker)
    {
        var start = yaml.IndexOf(startMarker, StringComparison.Ordinal);
        var end = yaml.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.IsTrue(start >= 0 && end > start, $"YAML block '{startMarker}' was not found.");
        return yaml[start..end];
    }

    private static string MatrixRow(string packageId, string version) =>
        $"| `{packageId}` | `{version}` | `Directory.Packages.props` | fixture |";

    private async Task<PowerShellResult> RunPowerShellAsync(
        string repositoryRoot,
        string workingDirectory,
        string scriptName,
        params string[] arguments)
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
        startInfo.ArgumentList.Add(Path.Combine(repositoryRoot, "eng", scriptName));
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start '{scriptName}'.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(ProcessTimeoutSeconds));
        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            timeout.Token,
            TestContext.CancellationToken);
        try
        {
            await process.WaitForExitAsync(waitCancellation.Token);
            var capturedOutput = await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(waitCancellation.Token);
            return new(process.ExitCode, capturedOutput[0], capturedOutput[1]);
        }
        catch (OperationCanceledException exception) when (timeout.IsCancellationRequested)
        {
            var output = await RequestTerminationAndDrainAsync(
                process,
                stdoutTask,
                stderrTask,
                scriptName);
            throw new TimeoutException(
                $"'{scriptName}' timed out after {ProcessTimeoutSeconds} seconds. Output:{Environment.NewLine}{output}",
                exception);
        }
        catch (OperationCanceledException) when (TestContext.CancellationToken.IsCancellationRequested)
        {
            await RequestTerminationAndDrainAsync(process, stdoutTask, stderrTask, scriptName);
            throw;
        }
    }

    private static async Task<string> RequestTerminationAndDrainAsync(
        Process process,
        Task<string> stdoutTask,
        Task<string> stderrTask,
        string description)
    {
        if (!process.HasExited)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) when (process.HasExited)
            {
                // The process exited between the state check and the kill request.
            }
        }

        using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(CleanupTimeoutSeconds));
        try
        {
            await process.WaitForExitAsync(cleanupTimeout.Token);
            var capturedOutput = await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(cleanupTimeout.Token);
            return PowerShellOutputNormalizer.Normalize(
                capturedOutput[0] + Environment.NewLine + capturedOutput[1]);
        }
        catch (OperationCanceledException exception) when (cleanupTimeout.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"{description} did not exit and close redirected streams within " +
                $"{CleanupTimeoutSeconds} seconds after process-tree termination was requested.",
                exception);
        }
    }

    private sealed record PowerShellResult(int ExitCode, string StdOut, string StdErr)
    {
        public string AllOutput => PowerShellOutputNormalizer.Normalize(StdOut + Environment.NewLine + StdErr);
    }
}
