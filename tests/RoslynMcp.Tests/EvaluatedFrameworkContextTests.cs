using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class EvaluatedFrameworkContextTests
{
    private string _root = null!;


    [TestMethod]
    public void ImportedConditionalTestPackages_UseOnlyActiveEvaluation()
    {
        InFixture(() =>
        {
            File.WriteAllText(Path.Combine(_root, "Testing.props"), """
                <Project><ItemGroup>
                  <PackageReference Include="TUnit" Version="1.46.0" Condition="'$(Flavor)' == 'mtp'" />
                  <PackageReference Include="MSTest.TestFramework" Version="4.5.1" Condition="'$(Flavor)' == 'classic'" />
                </ItemGroup><PropertyGroup Condition="'$(Flavor)' == 'explicit'"><IsTestProject>true</IsTestProject></PropertyGroup></Project>
                """);
            var path = Project("""<Import Project="Testing.props" />""");
            var globals = new Dictionary<string, string> { ["Flavor"] = "mtp" };
            Assert.IsTrue(ProjectMetadataParser.IsTestProject(path, globals));
            Assert.IsTrue(ProjectMetadataParser.RequiresMtpNativeExecution(path, globals));
            globals["Flavor"] = "classic";
            Assert.IsTrue(ProjectMetadataParser.IsTestProject(path, globals));
            Assert.IsFalse(ProjectMetadataParser.RequiresMtpNativeExecution(path, globals));
            globals["Flavor"] = "explicit";
            Assert.IsTrue(ProjectMetadataParser.IsTestProject(path, globals));
            Assert.IsFalse(ProjectMetadataParser.RequiresMtpNativeExecution(path, globals));
            globals["Flavor"] = "none";
            Assert.IsFalse(ProjectMetadataParser.IsTestProject(path, globals));
        });
    }

    [TestMethod]
    public void MultiTargetResolvedVersions_CheckEveryApplicableTargetAndSelectedFramework()
    {
        InFixture(() =>
        {
            var path = Project("", multi: true);
            Assets("1.45.8", "1.46.1");
            var globals = new Dictionary<string, string>();
            var versions = ProjectMetadataParser.TryGetResolvedPackageVersions(path, "TUnit.Engine", globals);
            Assert.HasCount(2, versions!);
            const string filter = "FullyQualifiedName=N.C.A|FullyQualifiedName=N.C.B";
            Assert.ThrowsExactly<PublicInvalidOperationException>(() => TreeNodeFilterTranslator.Translate(filter, versions));
            globals["TargetFramework"] = "net10.0";
            versions = ProjectMetadataParser.TryGetResolvedPackageVersions(path, "TUnit.Engine", globals);
            Assert.HasCount(1, versions!);
            Assert.AreEqual("/*/N/C/(A|B)", TreeNodeFilterTranslator.Translate(filter, versions));
            globals.Clear();
            Assets("1.46.0", "1.47.0-preview.1");
            Assert.AreEqual("/*/N/C/(A|B)", TreeNodeFilterTranslator.Translate(filter, ProjectMetadataParser.TryGetResolvedPackageVersions(path, "TUnit.Engine", globals)));
            Assets("1.46.0-preview.1", "1.47.0");
            Assert.ThrowsExactly<PublicInvalidOperationException>(() => TreeNodeFilterTranslator.Translate(filter, ProjectMetadataParser.TryGetResolvedPackageVersions(path, "TUnit.Engine", globals)));
        });
    }

    [TestMethod]
    public void MultiTargetFreshness_UsesPerFrameworkConditionalExpectations()
    {
        InFixture(() =>
        {
            var path = Project("""
                <ItemGroup>
                  <PackageReference Include="Chosen" Version="1.0.0" Condition="'$(TargetFramework)' == 'net9.0'" />
                  <PackageReference Include="Chosen" Version="2.0.0" Condition="'$(TargetFramework)' == 'net10.0'" />
                </ItemGroup>
                """, multi: true);
            Assets("1.0.0", "2.0.0", "Chosen");
            var globals = new Dictionary<string, string>();
            var detector = new RestoreStalenessDetector();
            Assert.IsFalse(detector.IsRestoreRequired(path, globals, NullLogger.Instance));
            Assets("2.0.0", "1.0.0", "Chosen");
            Assert.IsTrue(detector.IsRestoreRequired(path, globals, NullLogger.Instance), "Another target's matching version cannot conceal drift.");
        });
    }

    [TestMethod]
    public void MixedFrameworkExecutionModes_RequireConcreteFrameworkSelection()
    {
        InFixture(() =>
        {
            var path = Project("""
                <ItemGroup>
                  <PackageReference Include="TUnit" Version="1.46.0" Condition="'$(TargetFramework)' == 'net9.0'" />
                  <PackageReference Include="MSTest.TestFramework" Version="4.5.1" Condition="'$(TargetFramework)' == 'net10.0'" />
                </ItemGroup>
                """, multi: true);
            var globals = new Dictionary<string, string>();
            Assert.ThrowsExactly<PublicInvalidOperationException>(() => ProjectMetadataParser.RequiresMtpNativeExecution(path, globals));
            globals["TargetFramework"] = "net9.0";
            Assert.IsTrue(ProjectMetadataParser.RequiresMtpNativeExecution(path, globals));
            globals["TargetFramework"] = "net10.0";
            Assert.IsFalse(ProjectMetadataParser.RequiresMtpNativeExecution(path, globals));
        });
    }

    [TestMethod]
    public void FrameworkSpecificRecordedRoots_ProduceExactIsolatedRestoreInvocations()
    {
        InFixture(() =>
        {
            var path = Project("""
                <PropertyGroup><ProjectAssetsFile>assets/$(TargetFramework)/project.assets.json</ProjectAssetsFile></PropertyGroup>
                """, multi: true);
            foreach (var framework in new[] { "net9.0", "net10.0" })
            {
                var directory = Path.Combine(_root, "assets", framework);
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "project.assets.json"), JsonSerializer.Serialize(new
                {
                    project = new { restore = new { packagesPath = Path.Combine(_root, framework + "-cache") } }
                }));
            }
            var capture = RestoreInvocationPlanner.Capture([path], new Dictionary<string, string>(), CancellationToken.None);
            Assert.HasCount(2, capture);
            var invocations = RestoreInvocationPlanner.Plan(path, capture);
            Assert.HasCount(2, invocations);
            foreach (var invocation in invocations)
            {
                var args = invocation.Arguments.ToArray();
                CollectionAssert.Contains(args, "--no-dependencies");
                var framework = capture.Single(item => args.Contains("-p:TargetFramework=" + item.TargetFramework)).TargetFramework;
                Assert.AreEqual(Path.Combine(_root, framework + "-cache"), args[Array.IndexOf(args, "--packages") + 1]);
            }
        });
    }

    [TestMethod]
    public void SharedAssets_UsesOneProvenCompleteOuterRestoreAndPreservesOtherGlobals()
    {
        InFixture(() =>
        {
            var path = Project("", multi: true);
            var captured = RestoreInvocationPlanner.Capture([path], new Dictionary<string, string> { ["Flavor"] = "selected" }, CancellationToken.None);
            Assert.HasCount(2, captured);
            Assert.AreEqual(1, captured.Select(item => item.AssetsPath).Distinct().Count());
            var invocation = RestoreInvocationPlanner.Plan(path, captured).Single();
            Assert.IsFalse(invocation.Arguments.Any(argument => argument.StartsWith("-p:TargetFramework=", StringComparison.OrdinalIgnoreCase)));
            CollectionAssert.Contains(invocation.Arguments.ToArray(), "-p:Flavor=selected");
        });
    }

    [TestMethod]
    public void SharedAssets_RefusesDistinctNonFrameworkGlobalsBeforeCommands()
    {
        InFixture(() =>
        {
            var path = Project("", multi: true);
            var captured = RestoreInvocationPlanner.Capture([path], new Dictionary<string, string>(), CancellationToken.None).ToArray();
            captured[0] = captured[0] with { EvaluationProperties = captured[0].EvaluationProperties.SetItem("Flavor", "synthetic-context-one-73bb") };
            captured[1] = captured[1] with { EvaluationProperties = captured[1].EvaluationProperties.SetItem("Flavor", "synthetic-context-two-73bb") };
            var failure = Assert.ThrowsExactly<PublicInvalidOperationException>(() => RestoreInvocationPlanner.Plan(path, captured));
            StringAssert.Contains(failure.Message, "separate ProjectAssetsFile");
            Assert.IsFalse(failure.Message.Contains("synthetic-context-one-73bb", StringComparison.Ordinal));
            Assert.IsFalse(Directory.Exists(Path.Combine(_root, ".roslynmcp")));
        });
    }

    [TestMethod]
    public void SharedAssets_RefusesPartialOuterSetRatherThanRestoringUnreviewedFramework()
    {
        InFixture(() =>
        {
            var path = Project("", multi: true);
            File.WriteAllText(path, File.ReadAllText(path).Replace("net9.0;net10.0", "net8.0;net9.0;net10.0", StringComparison.Ordinal));
            var captured = RestoreInvocationPlanner.Capture([path], new Dictionary<string, string>(), CancellationToken.None).Where(item => item.TargetFramework != "net8.0").ToArray();
            Assert.HasCount(2, captured);
            Assert.ThrowsExactly<PublicInvalidOperationException>(() => RestoreInvocationPlanner.Plan(path, captured));
        });
    }

    [TestMethod]
    public void TargetedProjectCapture_EnrolsCoStorageSiblingsBeforeRestore()
    {
        InFixture(() =>
        {
            var path = Project("", multi: true);
            var graph = EvaluatedBuildGraph.FromRoots([path], new Dictionary<string, string>(), ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None);
            var selected = graph.Nodes.Single(node => !node.IsOuterBuild && node.TargetFramework == "net9.0");
            AdhocWorkspace? workspace = null;
            Exception? primary = null;
            try
            {
                workspace = new AdhocWorkspace();
                var id = ProjectId.CreateNewId();
                var solution = workspace.CurrentSolution.AddProject(ProjectInfo.Create(id, VersionStamp.Create(), "Test", "Test", LanguageNames.CSharp,
                    filePath: path, outputFilePath: selected.TargetPath));
                var captured = RestoreInvocationPlanner.Capture(solution.GetProject(id)!, CancellationToken.None);
                CollectionAssert.AreEquivalent(new[] { "net9.0", "net10.0" }, captured.Select(item => item.TargetFramework).ToArray());
                Assert.HasCount(1, RestoreInvocationPlanner.Plan(path, captured));
            }
            catch (Exception failure) { primary = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Framework context workspace cleanup failed.", primary, workspace); }
        });
    }

    [TestMethod]
    public void ResolvedVersion_RequiresSelectedRidTargetAndRejectsMissingOrAmbiguousPackage()
    {
        InFixture(() =>
        {
            var path = Project("");
            Assets("1.0.0", "1.46.0");
            var globals = new Dictionary<string, string> { ["RuntimeIdentifier"] = "win-x64" };
            Assert.IsNull(ProjectMetadataParser.TryGetResolvedPackageVersions(path, "TUnit.Engine", globals));
            var assetsPath = Path.Combine(_root, "obj", "project.assets.json");
            File.WriteAllText(assetsPath, """
                {"project":{"frameworks":{"net10.0":{}}},"targets":{"net10.0/win-x64":{"TUnit.Engine/1.47.0":{"type":"package"}}}}
                """);
            Assert.AreEqual("1.47.0", ProjectMetadataParser.TryGetResolvedPackageVersions(path, "TUnit.Engine", globals)?.Single().ToString());
            File.WriteAllText(assetsPath, """
                {"project":{"frameworks":{"net10.0":{}}},"targets":{"net10.0/win-x64":{"TUnit.Engine/1.47.0":{},"TUnit.Engine/1.45.8":{}}}}
                """);
            Assert.IsNull(ProjectMetadataParser.TryGetResolvedPackageVersions(path, "TUnit.Engine", globals));
        });
    }

    [TestMethod]
    public void LegacyFrameworkMonikerAndModernTargetAlias_SelectActualAssetsTarget()
    {
        InFixture(() =>
        {
            var path = Path.Combine(_root, "Legacy.csproj");
            File.WriteAllText(path, """
                <Project><PropertyGroup><TargetFrameworkMoniker>.NETFramework,Version=v4.8</TargetFrameworkMoniker>
                  <ProjectAssetsFile>obj/project.assets.json</ProjectAssetsFile></PropertyGroup>
                  <ItemGroup><PackageReference Include="Chosen" Version="1.2.3" /></ItemGroup></Project>
                """);
            Directory.CreateDirectory(Path.Combine(_root, "obj"));
            File.WriteAllText(Path.Combine(_root, "obj", "project.assets.json"), """
                {"project":{"frameworks":{".NETFramework,Version=v4.8":{"dependencies":{"Chosen":{"version":"1.2.3"}}}}},
                 "targets":{".NETFramework,Version=v4.8":{"Chosen/1.2.3":{"type":"package"}}}}
                """);
            var globals = new Dictionary<string, string>();
            Assert.AreEqual("1.2.3", ProjectMetadataParser.TryGetResolvedPackageVersions(path, "Chosen", globals)?.Single().ToString());
            Assert.IsFalse(new RestoreStalenessDetector().IsRestoreRequired(path, globals, NullLogger.Instance));
            Assert.IsNull(RestoreInvocationPlanner.Capture([path], globals, CancellationToken.None).Single().TargetFramework, "A legacy moniker is not an invented TargetFramework global.");
            File.WriteAllText(path, """
                <Project><PropertyGroup><TargetFramework>net10.0</TargetFramework>
                  <TargetFrameworkMoniker>.NETCoreApp,Version=v10.0</TargetFrameworkMoniker>
                  <ProjectAssetsFile>obj/project.assets.json</ProjectAssetsFile></PropertyGroup></Project>
                """);
            File.WriteAllText(Path.Combine(_root, "obj", "project.assets.json"), """
                {"project":{"frameworks":{".NETCoreApp,Version=v10.0":{"targetAlias":"net10.0"}}},
                 "targets":{".NETCoreApp,Version=v10.0":{"Chosen/2.0.0":{"type":"package"}}}}
                """);
            Assert.AreEqual("2.0.0", ProjectMetadataParser.TryGetResolvedPackageVersions(path, "Chosen", globals)?.Single().ToString());
        });
    }

    [TestMethod]
    public void SameNamedProjectTarget_DoesNotAuthorizePackageVersion()
    {
        InFixture(() =>
        {
            var path = Project("");
            Directory.CreateDirectory(Path.Combine(_root, "obj"));
            File.WriteAllText(Path.Combine(_root, "obj", "project.assets.json"), """
                {"project":{"frameworks":{"net10.0":{}}},"targets":{"net10.0":{"TUnit.Engine/1.47.0":{"type":"project"}}}}
                """);
            Assert.IsNull(ProjectMetadataParser.TryGetResolvedPackageVersions(path, "TUnit.Engine", new Dictionary<string, string>()));
        });
    }

    [TestMethod]
    public void RestoreArtifactInventory_TracksOnlyTheEvaluatedProjectsDependencySpec()
    {
        InFixture(() =>
        {
            var path = Project("");
            var directory = Path.Combine(_root, "custom");
            Directory.CreateDirectory(directory);
            var unrelated = Path.Combine(directory, "Unrelated.csproj.nuget.dgspec.json");
            File.WriteAllText(unrelated, "{}");
            var artifacts = RestoreArtifactLocator.Find(path, new Dictionary<string, string> { ["MSBuildProjectExtensionsPath"] = directory }, CancellationToken.None);
            CollectionAssert.DoesNotContain(artifacts.ToArray(), unrelated);
            CollectionAssert.Contains(artifacts.ToArray(), Path.Combine(directory, "Test.csproj.nuget.dgspec.json"));
        });
    }

    private void InFixture(Action body)
    {
        _root = Path.Combine(TestTempRoot.Current, "EvaluatedFrameworks", Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(_root);
            body();
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Framework context fixture cleanup failed.", primary,
                OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(_root)));
        }
    }

    private string Project(string body, bool multi = false)
    {
        var path = Path.Combine(_root, "Test.csproj");
        File.WriteAllText(path, """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>""" +
            (multi ? "<TargetFrameworks>net9.0;net10.0</TargetFrameworks>" : "<TargetFramework>net10.0</TargetFramework>") +
            "</PropertyGroup>" + body + "</Project>");
        return path;
    }

    private void Assets(string net9, string net10, string package = "TUnit.Engine")
    {
        var targets = new Dictionary<string, object>();
        var frameworks = new Dictionary<string, object>();
        foreach (var (framework, version) in new[] { ("net9.0", net9), ("net10.0", net10) })
        {
            targets[framework] = new Dictionary<string, object> { [package + "/" + version] = new { type = "package" } };
            frameworks[framework] = new { dependencies = new Dictionary<string, object> { [package] = new { version } } };
        }
        Directory.CreateDirectory(Path.Combine(_root, "obj"));
        File.WriteAllText(Path.Combine(_root, "obj", "project.assets.json"), JsonSerializer.Serialize(new { targets, project = new { frameworks } }));
    }
}
