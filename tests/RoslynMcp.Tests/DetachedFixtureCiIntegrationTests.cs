using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
[TestCategory("Process")]
public sealed class DetachedFixtureCiIntegrationTests
{
    [TestMethod]
    public async Task CopiedSupportFiles_SourceRootEvaluatesToOneLiteralDestination_WithItemMetacharacters()
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(DetachedFixtureCiIntegrationTests), Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source");
        var destination = Path.Combine(root, "actual %3B;%25()$(ValidIdentifier)@(literal)' destination");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(source, "Directory.Build.props"),
            "<Project><PropertyGroup><Nullable>enable</Nullable><ContinuousIntegrationBuild Condition=\"'$(CI)' == 'true'\">true</ContinuousIntegrationBuild><DeterministicSourcePaths>true</DeterministicSourcePaths></PropertyGroup></Project>");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        try
        {
            TestFixtureFileSystem.CopyRepositorySupportFiles(source, destination);
            var project = Path.Combine(destination, "probe.proj");
            File.WriteAllText(project, "<Project><PropertyGroup><ValidIdentifier>must-not-expand</ValidIdentifier></PropertyGroup><Import Project=\"Directory.Build.props\" /></Project>");
            var execution = await new DotnetCommandRunner().RunAsync(destination, project,
                ["msbuild", project, "-nologo", "-p:CI=true", "-getItem:SourceRoot",
                    "-getProperty:Nullable,ContinuousIntegrationBuild,DeterministicSourcePaths"], timeout.Token);
            Assert.AreEqual(0, execution.ExitCode, execution.StdOut + execution.StdErr);
            using var json = JsonDocument.Parse(execution.StdOut);
            var sourceRoots = json.RootElement.GetProperty("Items").GetProperty("SourceRoot");
            Assert.AreEqual(1, sourceRoots.GetArrayLength(), execution.StdOut);
            Assert.AreEqual(Path.GetFullPath(destination) + Path.DirectorySeparatorChar,
                sourceRoots[0].GetProperty("Identity").GetString(), execution.StdOut);
            Assert.AreEqual(Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination)),
                Path.TrimEndingDirectorySeparator(sourceRoots[0].GetProperty("FullPath").GetString()!), execution.StdOut);
            var properties = json.RootElement.GetProperty("Properties");
            Assert.AreEqual("enable", properties.GetProperty("Nullable").GetString());
            Assert.AreEqual("true", properties.GetProperty("ContinuousIntegrationBuild").GetString());
            Assert.AreEqual("true", properties.GetProperty("DeterministicSourcePaths").GetString());
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(root);
        }
    }

    [TestMethod]
    public async Task DetachedFixture_CiDesignTimeBuild_PreservesConfiguredCompilerOptions_ThroughAllRoutes()
    {
        var repository = TestFixtureFileSystem.FindRepositoryRoot();
        var project = Path.Combine(repository, "tests", "RoslynMcp.Tests", "RoslynMcp.Tests.csproj");
        var results = Path.Combine(TestTempRoot.Current, nameof(DetachedFixtureCiIntegrationTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(results);
        var configuration = typeof(DetachedFixtureCiIntegrationTests).Assembly
            .GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        try
        {
            var execution = await new DotnetCommandRunner().RunAsync(repository, project,
                ["test", project, "--configuration", configuration, "--no-build", "--no-restore",
                    "-p:TestFixturesPrepared=true", "--environment", "CI=true",
                    "--filter", "FullyQualifiedName=RoslynMcp.Tests.DetachedFixtureNativeContextIntegrationTests.Extraction_Preserves_Native_Nullable_Context_Through_Preview_Apply_Reload",
                    "--results-directory", results, "--logger", "trx;LogFileName=detached-ci.trx"], timeout.Token);
            var trxPath = Path.Combine(results, "detached-ci.trx");
            Assert.IsTrue(File.Exists(trxPath), execution.StdOut + Environment.NewLine + execution.StdErr);
            var trx = XDocument.Load(trxPath);
            XNamespace ns = trx.Root!.Name.Namespace;
            var counters = trx.Descendants(ns + "Counters").Single();
            var details = execution.StdOut + Environment.NewLine + execution.StdErr + Environment.NewLine +
                string.Join(Environment.NewLine, trx.Descendants(ns + "Message").Select(e => e.Value));
            Assert.AreEqual(0, execution.ExitCode, details);
            foreach (var name in new[] { "total", "executed", "passed" })
                Assert.AreEqual("4", counters.Attribute(name)?.Value, name + ": " + details);
            foreach (var counter in counters.Attributes().Where(a => a.Name.LocalName is not ("total" or "executed" or "passed")))
                Assert.AreEqual("0", counter.Value, counter.Name + ": " + details);
            var resultsEntries = trx.Descendants(ns + "UnitTestResult").ToArray();
            Assert.AreEqual(4, resultsEntries.Length, details);
            Assert.IsTrue(resultsEntries.All(e => e.Attribute("outcome")?.Value == "Passed"), details);
            Console.WriteLine("CI child verified: " + counters + "; Passed results=" + resultsEntries.Length);
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(results);
        }
    }
}
