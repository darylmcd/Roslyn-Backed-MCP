using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeAssetsCoordinateOracleTests
{
    [TestMethod]
    [DataRow(false, @"locks\project.assets.json")]
    [DataRow(true, @"locks\project.assets.json")]
    [DataRow(false, "locks%5Cproject.assets.json")]
    [DataRow(true, "locks%5Cproject.assets.json")]
    public async Task ProjectAssetsFile_CaptureMatchesActualSdkTaskStringRole(bool lateOverride, string operand)
    {
        MsBuildInitializer.EnsureInitialized();
        await CheckAsync(lateOverride, operand);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task CheckAsync(bool lateOverride, string operand)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(NativeAssetsCoordinateOracleTests), Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            NativeSdkRestoreFixture.InitializeDirectory(root);
            File.WriteAllText(Path.Combine(root, "NuGet.Config"), "<configuration><packageSources><clear /></packageSources></configuration>");
            var project = Path.Combine(root, "Owner.csproj");
            NativeSdkRestoreFixture.WriteProject(project, "<Project><PropertyGroup><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup></Project>");
            var restore = await NativeCoordinateOracleProcess.RunAsync(project, "Restore", root);
            Assert.AreEqual(0, restore.ExitCode, restore.StdOut + restore.StdErr);
            var valid = File.ReadAllBytes(Path.Combine(root, "obj", "project.assets.json"));
            var stamp = Path.Combine(root, "resolved-assets.txt");
            var xml = new XElement("Project",
                new XElement("Import", new XAttribute("Project", "Sdk.props"), new XAttribute("Sdk", "Microsoft.NET.Sdk")),
                new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0"),
                    new XElement("EnableDefaultCompileItems", "false"), new XElement("NuGetAudit", "false"),
                    new XElement("DisablePackageAssetsCache", "true")));
            var rootedOperand = "$(MSBuildProjectDirectory)/" + operand;
            if (!lateOverride) xml.Add(new XElement("PropertyGroup", new XElement("ProjectAssetsFile", rootedOperand)));
            xml.Add(new XElement("Import", new XAttribute("Project", "Sdk.targets"), new XAttribute("Sdk", "Microsoft.NET.Sdk")));
            if (lateOverride) xml.Add(new XElement("PropertyGroup", new XElement("ProjectAssetsFile", rootedOperand)));
            xml.Add(new XElement("Target", new XAttribute("Name", "CoordinateOracle"),
                new XAttribute("DependsOnTargets", "ResolvePackageAssets"),
                new XElement("WriteLinesToFile", new XAttribute("File", stamp), new XAttribute("Overwrite", "true"),
                    new XAttribute("Lines", "actual SDK ResolvePackageAssets completed"))));
            new XDocument(xml).Save(project);
            var normalized = Path.Combine(root, "locks", "project.assets.json");
            var literal = Path.Combine(root, @"locks\project.assets.json");
            Directory.CreateDirectory(Path.GetDirectoryName(normalized)!);
            var distinct = !FileSystemPath.Comparer.Equals(normalized, literal);
            File.WriteAllBytes(normalized, valid);
            if (distinct) File.WriteAllText(literal, "invalid lockfile at literal backslash coordinate");
            var first = await NativeCoordinateOracleProcess.RunAsync(project, "CoordinateOracle", root);
            string selected;
            if (distinct)
            {
                var firstCompleted = File.Exists(stamp);
                File.Delete(stamp);
                File.WriteAllText(normalized, "invalid lockfile at separator coordinate");
                File.WriteAllBytes(literal, valid);
                var second = await NativeCoordinateOracleProcess.RunAsync(project, "CoordinateOracle", root);
                var secondCompleted = File.Exists(stamp);
                Assert.AreNotEqual(firstCompleted, secondCompleted,
                    "Exactly one reversed valid/invalid lockfile layout must satisfy the actual SDK task.\n" + first.StdOut + first.StdErr + second.StdOut + second.StdErr);
                Assert.AreEqual(firstCompleted ? 0 : 1, first.ExitCode, first.StdOut + first.StdErr);
                Assert.AreEqual(secondCompleted ? 0 : 1, second.ExitCode, second.StdOut + second.StdErr);
                selected = firstCompleted ? normalized : literal;
            }
            else
            {
                Assert.AreEqual(0, first.ExitCode, first.StdOut + first.StdErr);
                Assert.IsTrue(File.Exists(stamp), "The real SDK task must execute successfully before coordinate comparisons.");
                selected = normalized;
            }
            File.WriteAllBytes(selected, valid);
            var inputs = MsBuildMetadataHelper.EvaluateRestoreInputs(project, new Dictionary<string, string>());
            Assert.AreEqual(selected, inputs.AssetsPath,
                "Restore-input capture must name the lockfile proven by the SDK task, including late scalar overrides.");
            var graph = EvaluatedBuildGraph.FromRoots([project], new Dictionary<string, string>(),
                ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None);
            Assert.AreEqual(selected, graph.Nodes.Single().Inputs.AssetsPath);
        }
        catch (Exception exception) { primary = exception; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native assets fixture cleanup failed.", primary,
            OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root))); }
    }

    [TestMethod]
    [DataRow(@"locks\project.assets.json")]
    [DataRow("locks%5Cproject.assets.json")]
    public async Task LateRelativeProjectAssetsFile_IsRejectedByActualSdkTask(string operand)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(NativeAssetsCoordinateOracleTests), Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            NativeSdkRestoreFixture.InitializeDirectory(root);
            var project = Path.Combine(root, "Owner.csproj");
            new XDocument(new XElement("Project",
                new XElement("Import", new XAttribute("Project", "Sdk.props"), new XAttribute("Sdk", "Microsoft.NET.Sdk")),
                new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0"), new XElement("EnableDefaultCompileItems", "false")),
                new XElement("Import", new XAttribute("Project", "Sdk.targets"), new XAttribute("Sdk", "Microsoft.NET.Sdk")),
                new XElement("PropertyGroup", new XElement("ProjectAssetsFile", operand)))).Save(project);
            var run = await NativeCoordinateOracleProcess.RunAsync(project, "ResolvePackageAssets", root);
            Assert.AreNotEqual(0, run.ExitCode);
            StringAssert.Contains(run.StdOut + run.StdErr, "NETSDK1006");
        }
        catch (Exception exception) { primary = exception; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native relative assets fixture cleanup failed.", primary,
            OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root))); }
    }
}
