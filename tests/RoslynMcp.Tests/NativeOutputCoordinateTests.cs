using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml.Linq;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeOutputCoordinateTests
{
    [TestMethod]
    [DataRow(@"custom\out\Owner.dll")]
    [DataRow("custom/literal%5Cname.dll")]
    [DataRow("custom/literal%255Cname.dll")]
    [DataRow("owned-rooted-backslash")]
    public async Task TargetPath_MatchesActualGetTargetPath(string escapedPath)
    {
        MsBuildInitializer.EnsureInitialized();
        await CheckAsync(escapedPath, extensions: false, fallbackAssets: false);
    }

    [TestMethod]
    [DataRow(@"custom\obj\")]
    [DataRow("custom/literal%5Cname/")]
    [DataRow("custom/literal%255Cname/")]
    [DataRow("owned-rooted-backslash")]
    public async Task ExtensionsPath_MatchesActualRestoreOutputConversion(string escapedPath)
    {
        MsBuildInitializer.EnsureInitialized();
        await CheckAsync(escapedPath, extensions: true, fallbackAssets: false);
    }

    [TestMethod]
    [DataRow(@"custom\obj\")]
    [DataRow("custom/literal%5Cname/")]
    [DataRow("custom/literal%255Cname/")]
    [DataRow("owned-rooted-backslash")]
    public async Task MissingAssetsProperty_MatchesActualDefaultRestoreFile(string escapedPath)
    {
        MsBuildInitializer.EnsureInitialized();
        await CheckAsync(escapedPath, extensions: true, fallbackAssets: true);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task CheckAsync(string escapedPath, bool extensions, bool fallbackAssets)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(NativeOutputCoordinateTests), Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "Owner.csproj");
            if (escapedPath == "owned-rooted-backslash")
                escapedPath = Microsoft.Build.Evaluation.ProjectCollection.Escape(
                    Path.Combine(root, "rooted", extensions ? "obj" : "Owner.dll").Replace('/', '\\'));
            var project = new XElement("Project",
                new XElement("Import", new XAttribute("Project", "Sdk.props"), new XAttribute("Sdk", "Microsoft.NET.Sdk")),
                new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0"),
                    new XElement(extensions ? "MSBuildProjectExtensionsPath" : "TargetPath", escapedPath)),
                new XElement("Import", new XAttribute("Project", "Sdk.targets"), new XAttribute("Sdk", "Microsoft.NET.Sdk")));
            if (fallbackAssets)
                project.Add(new XElement("PropertyGroup", new XElement("ProjectAssetsFile", "")));
            // Capture item metadata inside the native project context. Re-reading FullPath
            // from a returned relative item in this test host would use the wrong directory.
            project.Add(new XElement("Target", new XAttribute("Name", "ReadNativeOutputCoordinate"),
                new XAttribute("DependsOnTargets", extensions ? "_GenerateRestoreProjectSpec" : "GetTargetPath"),
                new XElement("PropertyGroup", new XElement("_NativeOutputCoordinate",
                    extensions ? "$(RestoreOutputAbsolutePath)" : "@(TargetPathWithTargetPlatformMoniker->'%(FullPath)')"))));
            new XDocument(project).Save(path);
            var graphRoot = path;
            if (!extensions)
            {
                graphRoot = Path.Combine(root, "Consumer.csproj");
                new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                    new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0")),
                    new XElement("ItemGroup", new XElement("ProjectReference", new XAttribute("Include", "Owner.csproj")))))
                    .Save(graphRoot);
            }
            var graph = EvaluatedBuildGraph.FromRoots([graphRoot], new Dictionary<string, string>(),
                ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None);
            var owner = graph.Nodes.Single(node => FileSystemPath.Comparer.Equals(node.ProjectPath, path));
            var captured = extensions ? owner.Inputs.ExtensionsPath : owner.TargetPath;
            // The child owns its SDK/NuGet assembly context; the test host has its own
            // NuGet package versions and must not execute the SDK restore task in-process.
            var result = await new DotnetCommandRunner().RunAsync(root, path,
                ["msbuild", path, "-nologo", "-verbosity:quiet", "-nodeReuse:false", "-maxCpuCount:1",
                    "-target:ReadNativeOutputCoordinate", "-getProperty:_NativeOutputCoordinate,MSBuildProjectFullPath"],
                CancellationToken.None);
            Assert.IsTrue(result.Succeeded, "The native coordinate oracle must succeed before comparing product coordinates. " + result.StdErr + result.StdOut);
            using var receipt = JsonDocument.Parse(result.StdOut);
            var properties = receipt.RootElement.GetProperty("Properties");
            Assert.AreEqual(path, properties.GetProperty("MSBuildProjectFullPath").GetString());
            var native = properties.GetProperty("_NativeOutputCoordinate").GetString()
                ?? throw new AssertFailedException("The native output coordinate is missing.");
            Assert.IsTrue(Path.IsPathFullyQualified(native));
            if (extensions) native = Path.TrimEndingDirectorySeparator(native);
            Assert.AreEqual(native, captured);
            if (fallbackAssets)
            {
                var restore = await new DotnetCommandRunner().RunAsync(root, path,
                    ["restore", path, "--packages", Path.Combine(root, "packages"), "--nologo"], CancellationToken.None);
                Assert.IsTrue(restore.Succeeded, restore.StdErr + restore.StdOut);
                var nativeAssets = Path.Combine(native, "project.assets.json");
                Assert.IsTrue(File.Exists(nativeAssets), "Native restore must produce the default assets file at the observed output directory.");
                Assert.AreEqual(nativeAssets, owner.Inputs.AssetsPath);
            }
            if (!extensions)
            {
                var consumer = graph.Nodes.Single(node => FileSystemPath.Comparer.Equals(node.ProjectPath, graphRoot));
                Assert.AreEqual(native, consumer.CompilerReferences.Declared.Single().Targets.Single().Output);
            }
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Native output coordinate fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }
}
