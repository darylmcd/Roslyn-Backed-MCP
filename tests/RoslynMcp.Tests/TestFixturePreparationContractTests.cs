using System.Xml.Linq;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class TestFixturePreparationContractTests
{
    private static readonly TimeSpan _processTimeout = TimeSpan.FromMinutes(3);

    [TestMethod]
    [TestCategory("Process")]
    public async Task DocumentedCommand_PreparesFreshFixtureBeforeTestsExecuteAndIsIdempotent()
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var fixtureRoot = Path.Combine(
            TestTempRoot.Current,
            "RoslynMcpFixturePreparationTests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var sampleProjectPath = await CreateSampleSolutionAsync(fixtureRoot, "AlphaFixture");
            await CreateFixtureDependentTestProjectAsync(repositoryRoot, fixtureRoot);
            Directory.CreateDirectory(Path.Combine(fixtureRoot, "eng"));
            File.Copy(
                Path.Combine(repositoryRoot, "eng", "prepare-test-fixtures.ps1"),
                Path.Combine(fixtureRoot, "eng", "prepare-test-fixtures.ps1"));
            await File.WriteAllTextAsync(
                Path.Combine(fixtureRoot, "RoslynMcp.slnx"),
                """
                <Solution>
                  <Project Path="tests/FixtureDependentTests/FixtureDependentTests.csproj" />
                </Solution>
                """);

            var mainRestore = await RunMainSolutionRestoreAsync(fixtureRoot);
            Assert.AreEqual(
                0,
                mainRestore.ExitCode,
                $"stdout={mainRestore.StdOut}{Environment.NewLine}stderr={mainRestore.StdErr}");
            var assetsPath = Path.Combine(Path.GetDirectoryName(sampleProjectPath)!, "obj", "project.assets.json");
            Assert.IsTrue(
                File.Exists(Path.Combine(
                    fixtureRoot, "tests", "FixtureDependentTests", "obj", "project.assets.json")),
                "The regression setup must restore the test project before clearing only sample state.");
            Assert.IsFalse(File.Exists(assetsPath), "The regression fixture must begin without owned sample obj state.");

            var first = await RunDocumentedCommandAsync(fixtureRoot);
            var second = await RunDocumentedCommandAsync(fixtureRoot);

            Assert.AreEqual(0, first.ExitCode, $"stdout={first.StdOut}{Environment.NewLine}stderr={first.StdErr}");
            Assert.AreEqual(0, second.ExitCode, $"stdout={second.StdOut}{Environment.NewLine}stderr={second.StdErr}");
            Assert.IsTrue(File.Exists(assetsPath), "The documented command did not restore the owned sample fixture.");
            StringAssert.Contains(first.StdOut, "AlphaFixture");
            StringAssert.Contains(first.StdOut, "Prepared 1 owned sample solution(s).");
            StringAssert.Contains(first.StdOut, "Passed!");
            StringAssert.Contains(second.StdOut, "Prepared 1 owned sample solution(s).");
            StringAssert.Contains(second.StdOut, "Passed!");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    [TestMethod]
    public void DirectTestJustAndReleaseValidation_UseOnePreparationOwner()
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var project = XDocument.Load(Path.Combine(
            repositoryRoot,
            "tests",
            "RoslynMcp.Tests",
            "RoslynMcp.Tests.csproj"));
        var target = project.Descendants("Target").Single(element =>
            (string?)element.Attribute("Name") == "PrepareTestFixtures");
        Assert.AreEqual("VSTest", (string?)target.Attribute("BeforeTargets"));
        Assert.AreEqual("'$(TestFixturesPrepared)' != 'true'", (string?)target.Attribute("Condition"));
        StringAssert.Contains((string?)target.Element("Exec")?.Attribute("Command") ?? string.Empty,
            "prepare-test-fixtures.ps1");

        var justfile = File.ReadAllText(Path.Combine(repositoryRoot, "justfile"));
        StringAssert.Contains(justfile, "prepare-test-fixtures:");
        StringAssert.Contains(justfile, "test: prepare-test-fixtures");
        StringAssert.Contains(justfile, "test-release: prepare-test-fixtures");
        StringAssert.Contains(justfile, "-p:TestFixturesPrepared=true");

        var release = File.ReadAllText(Path.Combine(repositoryRoot, "eng", "verify-release.ps1"));
        StringAssert.Contains(release, "prepare-validation-dependencies.ps1");
        Assert.IsFalse(release.Contains("prepare-test-fixtures.ps1", StringComparison.Ordinal));
        var preparation = File.ReadAllText(Path.Combine(repositoryRoot, "eng", "prepare-validation-dependencies.ps1"));
        StringAssert.Contains(preparation, "prepare-test-fixtures.ps1");
        StringAssert.Contains(preparation, "& dotnet restore $solutionPath --nologo");
        Assert.IsFalse(preparation.Contains("Get-ChildItem", StringComparison.Ordinal));
        var populationPosition = release.IndexOf("'prepare-validation-dependencies.ps1'", StringComparison.Ordinal);
        var licensePosition = release.IndexOf("-Description 'Restored third-party license validation'", StringComparison.Ordinal);
        var buildPosition = release.IndexOf("dotnet build $solutionPath", StringComparison.Ordinal);
        Assert.IsTrue(populationPosition < licensePosition && licensePosition < buildPosition);
        StringAssert.Contains(release, "RepoRoot = $repoRoot; SolutionPath = $solutionPath; Verify = $true; VerifyRestoredLicenses = $true");
        StringAssert.Contains(release, "'-p:TestFixturesPrepared=true'");
        Assert.IsFalse(release.Contains("dotnet restore $sampleSolutionPath", StringComparison.Ordinal));
        Assert.IsFalse(release.Contains("$generatedDocSolutionPath", StringComparison.Ordinal));
        Assert.IsFalse(release.Contains("$buildFailureSolutionPath", StringComparison.Ordinal));
    }

    [TestMethod]
    [TestCategory("Process")]
    [DataRow(false, 0, 5)]
    [DataRow(false, -1, 0)]
    [DataRow(false, 1, 1)]
    [DataRow(false, 2, 2)]
    [DataRow(true, 0, 5)]
    [DataRow(true, 1, 1)]
    [DataRow(true, 2, 2)]
    public async Task CachePopulation_ExecutesRealPreparationAndStopsOnRestoreFailureAsync(bool quality, int failedCall, int expectedCalls)
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var fixtureRoot = Path.Combine(TestTempRoot.Current, nameof(TestFixturePreparationContractTests), Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(fixtureRoot, "eng"));
            foreach (var scriptName in new[] { "prepare-validation-dependencies.ps1", "prepare-test-fixtures.ps1", "verify-nuget-audit.ps1" })
                File.Copy(Path.Combine(repositoryRoot, "eng", scriptName), Path.Combine(fixtureRoot, "eng", scriptName));
            if (failedCall != -1)
                await File.WriteAllTextAsync(Path.Combine(fixtureRoot, "RoslynMcp.slnx"), "<Solution />");
            var samplePaths = Directory.GetFiles(Path.Combine(repositoryRoot, "samples"), "*.slnx", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(repositoryRoot, path)).Order(StringComparer.Ordinal).ToArray();
            Assert.HasCount(4, samplePaths, "Update the complete package-population inventory if owned samples change.");
            foreach (var relativePath in samplePaths)
            {
                var path = Path.Combine(fixtureRoot, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, "<Solution />");
            }
            var workflow = File.ReadAllText(Path.Combine(repositoryRoot, ".github", "workflows", "ci.yml")).ReplaceLineEndings("\n");
            var jobMarker = quality ? "  quality:\n" : "  validate:\n";
            var start = workflow.IndexOf(jobMarker, StringComparison.Ordinal);
            Assert.IsTrue(start >= 0);
            var end = workflow.IndexOf("\n  #", start + jobMarker.Length, StringComparison.Ordinal);
            var job = end < 0 ? workflow[start..] : workflow[start..end];
            static string RunCommand(string job, string name)
            {
                var marker = "      - name: " + name + "\n";
                var start = job.IndexOf(marker, StringComparison.Ordinal);
                Assert.IsTrue(start >= 0, name);
                var run = job.IndexOf("        run: ", start, StringComparison.Ordinal);
                var end = job.IndexOf('\n', run);
                Assert.IsTrue(run >= start);
                return job[(run + "        run: ".Length)..end];
            }
            var command = RunCommand(job, "Prepare NuGet cache for persistence");
            var audit = quality ? RunCommand(job, "Audit packages for known vulnerabilities") + "\n" : string.Empty;
            var wrapper = Path.Combine(fixtureRoot, "population-probe.ps1");
            await File.WriteAllTextAsync(wrapper, $$"""
                $ErrorActionPreference = 'Stop'
                $global:populationCallCount = 0
                function global:dotnet {
                    $global:populationCallCount++
                    [IO.File]::AppendAllText((Join-Path $PWD.Path 'calls.jsonl'), ((@($args) | ConvertTo-Json -Compress) + [Environment]::NewLine))
                    $global:LASTEXITCODE = if ($global:populationCallCount -eq {{failedCall}}) { 23 } else { 0 }
                }
                {{audit}}{{command}}
                [IO.File]::WriteAllText((Join-Path $PWD.Path 'save-reached.txt'), 'save')
                dotnet build RoslynMcp.slnx
                """);
            var result = await PwshScriptRunner.RunAsync(["-NoProfile", "-File", wrapper],
                workingDirectory: fixtureRoot, timeout: TimeSpan.FromSeconds(30), description: "real cache population with owned dotnet stub");
            var diagnostic = result.StdOut + result.StdErr;
            Assert.AreEqual(failedCall == 0, result.ExitCode == 0, diagnostic);
            if (expectedCalls == 0)
            {
                StringAssert.Contains(PowerShellOutputNormalizer.Normalize(diagnostic), "Validation solution does not exist");
                Assert.IsFalse(File.Exists(Path.Combine(fixtureRoot, "calls.jsonl")), diagnostic);
                Assert.IsFalse(File.Exists(Path.Combine(fixtureRoot, "save-reached.txt")), diagnostic);
                return;
            }
            var calls = File.ReadAllLines(Path.Combine(fixtureRoot, "calls.jsonl"))
                .Select(line => System.Text.Json.JsonSerializer.Deserialize<string[]>(line)!).ToArray();
            var restores = calls.Where(call => call[0] == "restore").ToArray();
            Assert.HasCount(expectedCalls, restores, diagnostic);
            Assert.AreEqual(Path.Combine(fixtureRoot, "RoslynMcp.slnx"), restores[0][1]);
            if (quality)
            {
                CollectionAssert.Contains(restores[0], "-p:NuGetAudit=true");
                CollectionAssert.Contains(restores[0], "-p:NuGetAuditMode=all");
                CollectionAssert.Contains(restores[0], "-p:WarningsAsErrors=NU1900%3BNU1901%3BNU1902%3BNU1903%3BNU1904");
            }
            Assert.AreEqual(failedCall == 0, File.Exists(Path.Combine(fixtureRoot, "save-reached.txt")), diagnostic);
            Assert.AreEqual(failedCall == 0 ? 1 : 0, calls.Count(call => call[0] == "build"), diagnostic);
            if (failedCall == 0)
                CollectionAssert.AreEquivalent(samplePaths.Select(path => Path.Combine(fixtureRoot, path)).ToArray(), restores.Skip(1).Select(call => call[1]).ToArray());
            else
                StringAssert.Contains(PowerShellOutputNormalizer.Normalize(diagnostic), "exit code 23");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    private static Task<PwshScriptResult> RunDocumentedCommandAsync(string fixtureRoot) =>
        PwshScriptRunner.RunAsync(
            ["-NoProfile", "-Command", "dotnet test RoslynMcp.slnx --nologo"],
            workingDirectory: fixtureRoot,
            timeout: _processTimeout,
            description: "documented standalone test command");

    private static Task<PwshScriptResult> RunMainSolutionRestoreAsync(string fixtureRoot) =>
        PwshScriptRunner.RunAsync(
            ["-NoProfile", "-Command", "dotnet restore RoslynMcp.slnx --nologo"],
            workingDirectory: fixtureRoot,
            timeout: _processTimeout,
            description: "standalone test regression setup restore");

    private static async Task CreateFixtureDependentTestProjectAsync(
        string repositoryRoot,
        string fixtureRoot)
    {
        var ownerProject = XDocument.Load(Path.Combine(
            repositoryRoot,
            "tests",
            "RoslynMcp.Tests",
            "RoslynMcp.Tests.csproj"));
        var centralPackages = XDocument.Load(Path.Combine(repositoryRoot, "Directory.Packages.props"));
        string PackageVersion(string packageId) =>
            (string?)centralPackages.Descendants("PackageVersion").Single(element =>
                (string?)element.Attribute("Include") == packageId).Attribute("Version")
            ?? throw new InvalidOperationException($"Central package version is missing for {packageId}.");
        var preparationTarget = new XElement(ownerProject.Descendants("Target").Single(element =>
            (string?)element.Attribute("Name") == "PrepareTestFixtures"));
        var projectDirectory = Path.Combine(fixtureRoot, "tests", "FixtureDependentTests");
        Directory.CreateDirectory(projectDirectory);

        var project = new XDocument(
            new XElement("Project",
                new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                new XElement("PropertyGroup",
                    new XElement("TargetFramework", "net10.0"),
                    new XElement("IsTestProject", "true"),
                    new XElement("IsPackable", "false")),
                new XElement("ItemGroup",
                    new XElement("PackageReference",
                        new XAttribute("Include", "Microsoft.NET.Test.Sdk"),
                        new XAttribute("Version", PackageVersion("Microsoft.NET.Test.Sdk"))),
                    new XElement("PackageReference",
                        new XAttribute("Include", "MSTest.TestAdapter"),
                        new XAttribute("Version", PackageVersion("MSTest.TestAdapter"))),
                    new XElement("PackageReference",
                        new XAttribute("Include", "MSTest.TestFramework"),
                        new XAttribute("Version", PackageVersion("MSTest.TestFramework")))),
                preparationTarget));
        project.Save(Path.Combine(projectDirectory, "FixtureDependentTests.csproj"));
        await File.WriteAllTextAsync(
            Path.Combine(projectDirectory, "FixtureLoadTests.cs"),
            """
            using System;
            using System.IO;
            using Microsoft.VisualStudio.TestTools.UnitTesting;

            [TestClass]
            public sealed class FixtureLoadTests
            {
                [TestMethod]
                public void OwnedSampleAssetsExistBeforeFixtureDependentTestExecutes()
                {
                    var directory = new DirectoryInfo(AppContext.BaseDirectory);
                    while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "samples")))
                    {
                        directory = directory.Parent;
                    }

                    Assert.IsNotNull(directory, "Could not locate the isolated repository root.");
                    var assetsPath = Path.Combine(
                        directory.FullName, "samples", "AlphaFixture", "Project", "obj", "project.assets.json");
                    Assert.IsTrue(File.Exists(assetsPath), $"Fixture assets were absent: {assetsPath}");
                }
            }
            """);
    }

    private static async Task<string> CreateSampleSolutionAsync(string fixtureRoot, string fixtureName)
    {
        var projectDirectory = Path.Combine(fixtureRoot, "samples", fixtureName, "Project");
        Directory.CreateDirectory(projectDirectory);
        var projectPath = Path.Combine(projectDirectory, "Project.csproj");
        await File.WriteAllTextAsync(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(fixtureRoot, "samples", fixtureName, $"{fixtureName}.slnx"),
            """
            <Solution>
              <Project Path="Project/Project.csproj" />
            </Solution>
            """);
        return projectPath;
    }
}
