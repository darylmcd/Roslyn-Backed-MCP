using System.Text.Json;
using System.Xml.Linq;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class CopiedSolutionRestoreBindingTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [TestMethod]
    public async Task RestoreCopiedSolution_PreservesRecordedCacheAndConditionalGraph()
    {
        var root = Path.Combine(TestTempRoot.Current, "copied-restore-binding-" + Guid.NewGuid().ToString("N"));
        var solutionPath = Path.Combine(root, "SampleSolution.slnx");
        var scope = CreateWorkspaceScope(root, solutionPath, WorkspaceManager);
        string? workspaceId = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(Path.Combine(root, "Directory.Build.props"), "<Project />");
            await File.WriteAllTextAsync(Path.Combine(root, "global.json"), "{}");
            var recordedCache = Path.Combine(root, "recorded-cache");
            var ambientCache = Path.Combine(root, "ambient-cache");
            var configuration = new XElement("configuration",
                new XElement("packageSources", new XElement("clear")),
                new XElement("config", new XElement("add", new XAttribute("key", "globalPackagesFolder"),
                    new XAttribute("value", ambientCache))));
            await File.WriteAllTextAsync(Path.Combine(root, "NuGet.Config"), configuration.ToString());
            await File.WriteAllTextAsync(solutionPath, "<Solution />");
            AddProjectToCopiedSolution(root, "SampleLib", "net10.0");
            await File.WriteAllTextAsync(scope.GetPath("SampleLib", "Library.cs"),
                "namespace SampleLib; public sealed class Library { }");

            var baseline = await DotnetCommandRunner.RunAsync(root, solutionPath,
                MsBuildCommandProperties.Append(["restore", solutionPath, "--nologo", "--packages", recordedCache],
                    OwnedNativeLoadContext.Properties(solutionPath, new Dictionary<string, string>())), CancellationToken.None);
            Assert.IsTrue(baseline.Succeeded, "The real initial restore must establish the recorded cache. " + baseline.StdErr);
            AssertCache("SampleLib");

            AddProjectToCopiedSolution(root, "Contracts", "net10.0");
            var contracts = scope.GetPath("Contracts", "Contracts.csproj");
            var project = XDocument.Load(contracts);
            project.Root!.Element("PropertyGroup")!.Element("TargetFramework")!.Value = "net999.0";
            project.Root.Add(new XElement("PropertyGroup", new XAttribute("Condition", "'$(FixtureFlavor)' == 'reviewed'"),
                new XElement("TargetFramework", "net10.0"),
                new XElement("DefineConstants", "$(DefineConstants);REVIEWED_FIXTURE")));
            await File.WriteAllTextAsync(contracts, project.ToString());
            await File.WriteAllTextAsync(scope.GetPath("Contracts", "Contract.cs"),
                "namespace Contracts; public interface IContract { }");
            var properties = new Dictionary<string, string> { ["FixtureFlavor"] = "reviewed" };

            await RestoreCopiedSolutionAsync(solutionPath, properties, CancellationToken.None);

            AssertCache("SampleLib");
            AssertCache("Contracts");
            using (var assets = JsonDocument.Parse(await File.ReadAllBytesAsync(scope.GetPath("Contracts", "obj", "project.assets.json"))))
            {
                Assert.IsTrue(assets.RootElement.GetProperty("project").GetProperty("frameworks")
                    .TryGetProperty("net10.0", out _), "Restore must use the same conditional framework as native load.");
            }

            var status = await WorkspaceManager.LoadAsync(solutionPath,
                OwnedNativeLoadContext.Properties(solutionPath, properties), CancellationToken.None);
            workspaceId = status.WorkspaceId;
            var loaded = WorkspaceManager.GetCurrentSolution(workspaceId);
            var options = (CSharpParseOptions)loaded.Projects.Single(item => item.Name == "Contracts").ParseOptions!;
            CollectionAssert.Contains(options.PreprocessorSymbolNames.ToArray(), "REVIEWED_FIXTURE");
            _ = NativeCompilerContextCertificate.Require(loaded);

            void AssertCache(string name)
            {
                var actual = RestoreStalenessDetector.ReadRequiredRestorePackagesPathFromAssets(
                    scope.GetPath(name, "obj", "project.assets.json"), CancellationToken.None);
                Assert.IsNotNull(actual);
                Assert.IsTrue(FileSystemPath.Comparer.Equals(Path.GetFullPath(recordedCache), Path.GetFullPath(actual)),
                    "Explicit fixture restore must preserve the recorded cache instead of the conflicting local default.");
                Assert.IsFalse(FileSystemPath.Comparer.Equals(Path.GetFullPath(ambientCache), Path.GetFullPath(actual)));
            }
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Copied restore binding fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => { if (workspaceId is not null) WorkspaceManager.Close(workspaceId); }),
                () => scope.DisposeAsync());
        }
    }
}
