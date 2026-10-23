using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class EvaluatedRestoreInputsTests
{
    private string _root = null!;
    [TestMethod]
    [DataRow("ProjectAssetsFile", "custom/actual.assets.json", "custom/actual.assets.json")]
    [DataRow("MSBuildProjectExtensionsPath", "custom/", "custom/project.assets.json")]
    [DataRow("BaseIntermediateOutputPath", "custom/", "custom/project.assets.json")]
    public void Consumers_UseCapturedAssetsLocationDespiteStaleDefault(string property, string value, string relativeAssets)
    {
        WithRoot(() =>
        {
            var path = Project("Probe.csproj", "<PackageReference Include=\"Chosen\" Version=\"1.2.3\" />");
            var actual = Path.GetFullPath(Path.Combine(_root, relativeAssets));
            var stale = Path.Combine(_root, "obj", "project.assets.json");
            WriteAssets(stale, "9.9.9", "stale-cache");
            WriteAssets(actual, "1.2.3", "actual-cache");
            var globals = new Dictionary<string, string> { [property] = value };
            Assert.AreEqual(new NuGet.Versioning.NuGetVersion(1, 2, 3), ProjectMetadataParser.TryGetResolvedPackageVersions(path, "Chosen", globals)?.Single());
            Assert.AreEqual("actual-cache", RestoreStalenessDetector.TryReadRestorePackagesPath(path, globals));
            Assert.IsFalse(RestoreStalenessDetector.HasMissingAssets(path, globals, NullLogger.Instance));
            Assert.IsFalse(new RestoreStalenessDetector().IsRestoreRequired(path, globals, NullLogger.Instance));
            Assert.AreEqual("actual-cache", RestoreInvocationPlanner.Capture([path], globals, CancellationToken.None).Single().PackagesPath);
            var artifacts = RestoreArtifactLocator.Find(path, globals, CancellationToken.None).ToArray();
            CollectionAssert.Contains(artifacts, actual);
            CollectionAssert.DoesNotContain(artifacts, stale);
            File.Delete(actual);
            Assert.IsNull(ProjectMetadataParser.TryGetResolvedPackageVersions(path, "Chosen", globals));
            Assert.IsNull(RestoreStalenessDetector.TryReadRestorePackagesPath(path, globals));
            Assert.IsTrue(RestoreStalenessDetector.HasMissingAssets(path, globals, NullLogger.Instance), "A stale default assets file cannot hide missing configured output.");
        });
    }

    [TestMethod]
    [DataRow("custom/", "custom")]
    [DataRow("custom/../obj/", "obj")]
    public void ExtensionsDirectory_ExposesCanonicalPhysicalIdentityWithoutChangingAssetsLocation(string configured, string directory)
    {
        WithRoot(() =>
        {
            var path = Project("Canonical.csproj", "");
            var inputs = MsBuildMetadataHelper.EvaluateRestoreInputs(path,
                new Dictionary<string, string> { ["MSBuildProjectExtensionsPath"] = configured });
            Assert.AreEqual(Path.Combine(_root, directory), inputs.ExtensionsPath);
            Assert.AreEqual(Path.Combine(_root, directory, "project.assets.json"), inputs.AssetsPath);
            WorkspaceManager.EnsurePhysicalPath(inputs.ExtensionsPath);
        });
    }

    [TestMethod]
    public void ConditionalPackagesAndExpandedVersions_UseSameCapturedEvaluation()
    {
        WithRoot(() =>
        {
            var path = Project("Conditions.csproj", """
                <PackageReference Include="Chosen" Version="$(ChosenVersion)" Condition="'$(Flavor)' == 'selected'" />
                <PackageReference Include="Inactive" Version="9.9.9" Condition="'$(Flavor)' == 'other'" />
                """);
            var assets = Path.Combine(_root, "obj", "project.assets.json");
            WriteAssets(assets, "1.2.3", "actual-cache");
            var globals = new Dictionary<string, string> { ["Flavor"] = "selected", ["ChosenVersion"] = "1.2.3" };
            var inputs = MsBuildMetadataHelper.EvaluateRestoreInputs(path, globals);
            Assert.HasCount(1, inputs.Packages);
            Assert.AreEqual("Chosen", inputs.Packages[0].Id);
            Assert.AreEqual("1.2.3", inputs.Packages[0].RequestedVersion);
            Assert.IsFalse(new RestoreStalenessDetector().IsRestoreRequired(path, globals, NullLogger.Instance));
            globals["Flavor"] = "other";
            Assert.IsTrue(new RestoreStalenessDetector().IsRestoreRequired(path, globals, NullLogger.Instance));
            globals["Flavor"] = "none";
            File.Delete(assets);
            Assert.IsFalse(RestoreStalenessDetector.HasMissingAssets(path, globals, NullLogger.Instance), "An inactive PackageReference does not require restore.");
            Assert.IsFalse(new RestoreStalenessDetector().IsRestoreRequired(path, globals, NullLogger.Instance));
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CentralAndVersionOverrideMetadata_AreEvaluatedBeforeComparingAssets(bool overridden)
    {
        WithRoot(() =>
        {
            File.WriteAllText(Path.Combine(_root, "Directory.Packages.props"), """
                <Project><PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup>
                  <ItemGroup>
                    <PackageVersion Include="Chosen" Version="$(CentralVersion)" Condition="'$(Flavor)' == 'selected'" />
                    <PackageVersion Include="Chosen" Version="9.9.9" Condition="'$(Flavor)' == 'other'" />
                  </ItemGroup>
                </Project>
                """);
            var path = Project("Central.csproj", "<PackageReference Include=\"Chosen\" VersionOverride=\"$(OverrideVersion)\" />");
            var globals = new Dictionary<string, string> { ["CentralVersion"] = "1.2.3", ["Flavor"] = "selected" };
            if (overridden) globals["OverrideVersion"] = "2.0.0";
            var version = overridden ? "2.0.0" : "1.2.3";
            WriteAssets(Path.Combine(_root, "obj", "project.assets.json"), version, "actual-cache", centralVersion: "1.2.3");
            var inputs = MsBuildMetadataHelper.EvaluateRestoreInputs(path, globals);
            var chosen = inputs.Packages.Single(package => package.Id == "Chosen");
            Assert.AreEqual(version, chosen.RequestedVersion);
            Assert.AreEqual(!overridden, chosen.UsesCentralVersion);
            Assert.IsFalse(new RestoreStalenessDetector().IsRestoreRequired(path, globals, NullLogger.Instance));
        });
    }

    [TestMethod]
    public void RaceProbe_TracksConfiguredDependencyArtifactsAndIgnoresUnrelatedObjTree()
    {
        WithRoot(() =>
        {
            var application = Project("App.csproj", "<ProjectReference Include=\"dependency/Dependency.csproj\" />");
            Directory.CreateDirectory(Path.Combine(_root, "dependency"));
            var dependency = Path.Combine(_root, "dependency", "Dependency.csproj");
            File.WriteAllText(dependency, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
            var globals = new Dictionary<string, string> { ["MSBuildProjectExtensionsPath"] = "custom/" };
            var appAssets = Path.Combine(_root, "custom", "project.assets.json");
            var dependencyAssets = Path.Combine(_root, "dependency", "custom", "project.assets.json");
            WriteAssets(appAssets, "1.2.3", "cache");
            WriteAssets(dependencyAssets, "1.2.3", "cache");
            var unrelated = Path.Combine(_root, "unrelated", "obj", "project.assets.json");
            WriteAssets(unrelated, "9.9.9", "unrelated");
            var solution = Path.Combine(_root, "Probe.slnx");
            File.WriteAllText(solution, "<Solution><Project Path=\"App.csproj\" /></Solution>");
            var artifacts = RestoreArtifactLocator.Find(solution, globals, CancellationToken.None).ToArray();
            CollectionAssert.Contains(artifacts, appAssets);
            CollectionAssert.Contains(artifacts, dependencyAssets);
            CollectionAssert.DoesNotContain(artifacts, unrelated);
        });
    }

    [TestMethod]
    public void EvaluationFailure_DoesNotExposeCapturedPropertyValues()
    {
        WithRoot(() =>
        {
            var path = Path.Combine(_root, "Broken.csproj");
            File.WriteAllText(path, "<Project><Import Project=\"$(PrivateImport)\" /></Project>");
            const string sentinel = "synthetic-unavailable-import-734bc";
            var exception = Assert.ThrowsExactly<RoslynMcp.Core.Services.PublicInvalidOperationException>(() => MsBuildMetadataHelper.ResolveAssetsPath(path,
                new Dictionary<string, string> { ["PrivateImport"] = sentinel }));
            Assert.IsFalse(exception.ToString().Contains(sentinel, StringComparison.Ordinal));
            Assert.IsNull(exception.InnerException);
        });
    }

    [TestMethod]
    [DataRow("1.0", "[1.0.0, )", false, false)]
    [DataRow("[1.0,2.0)", "[1.0.0, 2.0.0)", false, false)]
    [DataRow("[1.0]", "[1.0.0]", false, false)]
    [DataRow("1.0.0-RC.1", "[1.0.0-rc.1, )", false, false)]
    [DataRow("1.*", "1.*", false, false)]
    [DataRow("1.*", "[1.*, )", false, false)]
    [DataRow("1.*", "2.*", true, false)]
    [DataRow("1.*", "[1.0.0, )", true, false)]
    [DataRow("[1.0,2.0)", "[1.0.0, 2.0.0]", true, false)]
    [DataRow("[1.0]", "[1.0.0, )", true, false)]
    [DataRow("1.0", "[1.0.0, )", false, true)]
    [DataRow("[1.0,2.0)", "[1.0.0, 2.0.0)", false, true)]
    [DataRow("1.*", "2.*", true, true)]
    public void PackageRequestEquivalence_UsesNuGetRangesAndFloatingPreference(string expected, string actual, bool stale, bool central)
    {
        WithRoot(() =>
        {
            if (central)
                File.WriteAllText(Path.Combine(_root, "Directory.Packages.props"), new System.Xml.Linq.XDocument(
                    new System.Xml.Linq.XElement("Project",
                        new System.Xml.Linq.XElement("PropertyGroup", new System.Xml.Linq.XElement("ManagePackageVersionsCentrally", "true")),
                        new System.Xml.Linq.XElement("ItemGroup", new System.Xml.Linq.XElement("PackageVersion",
                            new System.Xml.Linq.XAttribute("Include", "Chosen"), new System.Xml.Linq.XAttribute("Version", expected))))).ToString());
            var path = Project("SemanticRange.csproj", central ? """<PackageReference Include="Chosen" />"""
                : new System.Xml.Linq.XElement("PackageReference", new System.Xml.Linq.XAttribute("Include", "Chosen"),
                    new System.Xml.Linq.XAttribute("Version", expected)).ToString());
            WriteAssets(Path.Combine(_root, "obj", "project.assets.json"), actual, "actual-cache", central ? actual : null);
            Assert.AreEqual(stale, new RestoreStalenessDetector().IsRestoreRequired(path,
                new Dictionary<string, string>(), NullLogger.Instance));
        });
    }

    [TestMethod]
    public void PropertyDerivedAssetsReadFailure_RedactsPathAndExceptionBeforeLogging()
    {
        WithRoot(() =>
        {
            const string sentinel = "synthetic-assets-path-57a32";
            var path = Project("Redaction.csproj", """<PackageReference Include="Chosen" Version="1.2.3" />""");
            var assets = Path.Combine(_root, sentinel, "project.assets.json");
            WriteAssets(assets, "1.2.3", "cache");
            FileStream? locked = null;
            Exception? primary = null;
            try
            {
                locked = new FileStream(assets, FileMode.Open, FileAccess.Read, FileShare.None);
                Assert.ThrowsExactly<IOException>(() => { using var probe = File.OpenRead(assets); }, "The fixture must reach an actual assets read failure.");
                var logger = new CapturingLogger();
                _ = new RestoreStalenessDetector().IsRestoreRequired(path,
                    new Dictionary<string, string> { ["ProjectAssetsFile"] = assets }, logger);
                Assert.HasCount(1, logger.Entries);
                Assert.IsFalse(logger.Entries[0].Contains(assets, StringComparison.OrdinalIgnoreCase));
                Assert.IsFalse(logger.Entries[0].Contains(sentinel, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception failure) { primary = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Locked assets fixture cleanup failed.", primary, locked); }
        });
    }

    private void WithRoot(Action body)
    {
        _root = Path.Combine(TestTempRoot.Current, nameof(EvaluatedRestoreInputsTests), Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(_root);
            body();
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Evaluated restore fixture cleanup failed.", primary,
                OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(_root)));
        }
    }

    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger
    {
        internal List<string> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add(formatter(state, exception) + exception);
    }

    private string Project(string name, string items)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup>" + items + "</ItemGroup></Project>");
        return path;
    }

    private static void WriteAssets(string path, string version, string packagesPath, string? centralVersion = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            targets = new Dictionary<string, object> { ["net10.0"] = new Dictionary<string, object> { ["Chosen/" + version] = new { type = "package" } } },
            libraries = new Dictionary<string, object> { ["Chosen/" + version] = new { type = "package" } },
            project = new
            {
                restore = new { packagesPath },
                frameworks = new Dictionary<string, object>
                {
                    ["net10.0"] = new
                    {
                        dependencies = new Dictionary<string, object> { ["Chosen"] = new { version } },
                        centralPackageVersions = centralVersion is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["Chosen"] = centralVersion }
                    }
                }
            }
        }));
    }
}
