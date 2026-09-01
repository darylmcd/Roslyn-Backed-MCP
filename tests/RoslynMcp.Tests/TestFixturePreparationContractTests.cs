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
        StringAssert.Contains(release, "prepare-test-fixtures.ps1");
        StringAssert.Contains(release, "'-p:TestFixturesPrepared=true'");
        Assert.IsFalse(release.Contains("dotnet restore $sampleSolutionPath", StringComparison.Ordinal));
        Assert.IsFalse(release.Contains("$generatedDocSolutionPath", StringComparison.Ordinal));
        Assert.IsFalse(release.Contains("$buildFailureSolutionPath", StringComparison.Ordinal));
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
