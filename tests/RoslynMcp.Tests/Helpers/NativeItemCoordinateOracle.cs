using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml.Linq;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests.Helpers;

internal static class NativeItemCoordinateOracle
{
    internal static Task WithNativeItemAsync(string fixtureName, string itemType, string escapedInclude,
        Func<string, string, Task> inspect)
    {
        ArgumentNullException.ThrowIfNull(inspect);
        MsBuildInitializer.EnsureInitialized();
        return WithNativeItemCoreAsync(fixtureName, itemType, escapedInclude, inspect);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task WithNativeItemCoreAsync(string fixtureName, string itemType, string escapedInclude, Func<string, string, Task> inspect)
    {
        var lexical = Path.Combine(TestTempRoot.Current, fixtureName, Guid.NewGuid().ToString("N"));
        var root = lexical;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(lexical);
            root = PhysicalPathResolver.Resolve(lexical);
            var projectPath = Path.Combine(root, "Owner.proj");
            if (escapedInclude == "owned-rooted-backslash")
                escapedInclude = Microsoft.Build.Evaluation.ProjectCollection.Escape(Path.Combine(root, "rooted", "Input.cs").Replace('/', '\\'));
            new XDocument(new XElement("Project", new XElement("ItemGroup", new XElement(itemType, new XAttribute("Include", escapedInclude))))).Save(projectPath);
            // The installed native process owns item expansion and its project-relative
            // FullPath metadata. No expected-coordinate algorithm is reproduced here.
            var result = await new DotnetCommandRunner().RunAsync(root, projectPath, ["msbuild", projectPath, "-nologo", "-verbosity:quiet", "-nodeReuse:false", "-maxCpuCount:1", "-getItem:" + itemType], CancellationToken.None);
            Assert.IsTrue(result.Succeeded, result.StdErr + result.StdOut);
            using var receipt = JsonDocument.Parse(result.StdOut);
            var nativeItems = receipt.RootElement.GetProperty("Items").GetProperty(itemType);
            Assert.AreEqual(1, nativeItems.GetArrayLength());
            var nativePath = nativeItems[0].GetProperty("FullPath").GetString() ?? throw new AssertFailedException("Native item FullPath is absent.");
            Assert.IsTrue(Path.IsPathFullyQualified(nativePath));
            await inspect(projectPath, nativePath);
        }
        catch (Exception failure)
        {
            primary = failure;
            throw;
        }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Native coordinate fixture cleanup failed.", primary, CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }
}
