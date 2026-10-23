using System.Collections.Immutable;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeOutputPreparationPathTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PhysicalPreparationInputs_RejectExistingAndNewLinkComponentsBeforeHashing(bool alreadyLinked)
    {
        await WithRootAsync(async root =>
        {
            var target = Path.Combine(root, "target");
            Directory.CreateDirectory(target);
            var alias = Path.Combine(root, "alias");
            if (alreadyLinked) Assert.IsTrue(TestFixtureFileSystem.TryCreateDirectoryLink(alias, target));
            var output = Path.Combine(alias, "obj");
            if (!alreadyLinked)
            {
                Assert.AreEqual(PhysicalInputKind.Missing, (await PhysicalInputIdentity.CaptureAsync(output, CancellationToken.None)).Kind);
                Assert.IsTrue(TestFixtureFileSystem.TryCreateDirectoryLink(alias, target));
            }
            Directory.CreateDirectory(output);
            Assert.IsFalse((File.GetAttributes(output) & FileAttributes.ReparsePoint) != 0, "The leaf is a real directory; its parent has the link.");
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => PhysicalInputIdentity.CaptureAsync(output, CancellationToken.None));
        });
    }

    [TestMethod]
    [DataRow("required", true)]
    [DataRow("sibling", false)]
    [DataRow("file", false)]
    public async Task OutputAncestors_OnlyExactNecessaryDirectoryCreationIsAllowed(string shape, bool accepted)
    {
        await WithRootAsync(async root =>
        {
            var project = Path.Combine(root, "Native.csproj");
            await File.WriteAllTextAsync(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit><IntermediateOutputPath>prepared/net10.0/</IntermediateOutputPath></PropertyGroup></Project>");
            var graph = EvaluatedBuildGraph.FromRoots([project], OwnedNativeLoadContext.Properties(project, new Dictionary<string, string>()), ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None);
            var output = Path.Combine(root, shape == "sibling" ? "prepared-other" : "prepared");
            var before = await PhysicalInputIdentity.CaptureAsync(output, CancellationToken.None);
            Assert.AreEqual(PhysicalInputKind.Missing, before.Kind);
            if (shape == "file") await File.WriteAllTextAsync(output, "not a directory");
            else Directory.CreateDirectory(output);
            Task Inspect() => NativeOutputPreparation.AssertInputsAsync("output-ancestry", graph, [before],
                ImmutableHashSet.Create<string>(FileSystemPath.Comparer), ImmutableHashSet.Create(FileSystemPath.Comparer, output),
                new OwnedTestWorkspacePathPolicy(), CancellationToken.None);
            if (accepted) await Inspect();
            else await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(Inspect);
        });
    }

    private static async Task WithRootAsync(Func<string, Task> run)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(NativeOutputPreparationPathTests), Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            await run(root);
        }
        catch (Exception failure)
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Output path fixture cleanup failed.", failure,
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
            throw;
        }
        TestFixtureFileSystem.DeleteDirectoryIfExists(root);
    }
}
