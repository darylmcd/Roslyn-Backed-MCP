using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class BuildPathRolesLiteralPropertyTests
{
    [TestMethod]
    [DataRow("*", "OutputPath")]
    [DataRow("?", "OutputPath")]
    [DataRow("$(", "OutputPath")]
    [DataRow("@(", "OutputPath")]
    [DataRow("%(", "OutputPath")]
    [DataRow("*", "TargetPath")]
    [DataRow("?", "TargetPath")]
    [DataRow("$(", "TargetPath")]
    [DataRow("@(", "TargetPath")]
    [DataRow("%(", "TargetPath")]
    [DataRow("*", "AssemblyOriginatorKeyFile")]
    [DataRow("?", "AssemblyOriginatorKeyFile")]
    [DataRow("$(", "AssemblyOriginatorKeyFile")]
    [DataRow("@(", "AssemblyOriginatorKeyFile")]
    [DataRow("%(", "AssemblyOriginatorKeyFile")]
    [DataRow("*", "MSBuildUserExtensionsPath")]
    [DataRow("?", "MSBuildUserExtensionsPath")]
    [DataRow("$(", "MSBuildUserExtensionsPath")]
    [DataRow("@(", "MSBuildUserExtensionsPath")]
    [DataRow("%(", "MSBuildUserExtensionsPath")]
    public Task NativeEvaluatedPhysicalProperty_RetainsEscapedLiteralText(string metacharacters, string property)
        => VerifyAsync(metacharacters, property, escaped: true);

    [TestMethod]
    public Task NativeEvaluatedOutputDirectory_DoesNotTreatRawWildcardAsGlob()
        => VerifyAsync("*", "OutputPath", escaped: false);

    private static async Task VerifyAsync(string metacharacters, string property, bool escaped)
    {
        MsBuildInitializer.EnsureInitialized();
        var root = Path.Combine(TestTempRoot.Current, "literal-property-role-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Exception? primaryFailure = null;
        try
        {
            var literalDirectory = Path.Combine(root, "literal" + metacharacters);
            var isDirectory = property is "OutputPath" or "MSBuildUserExtensionsPath";
            var value = isDirectory ? literalDirectory : Path.Combine(literalDirectory, "output.bin");
            var xml = new XElement("Project", new XElement("PropertyGroup",
                new XElement("OwnedPath", escaped ? Escape(value) : value), new XElement(property, "$(OwnedPath)")));
            if (property == "MSBuildUserExtensionsPath") xml.Add(new XElement("ItemGroup",
                new XElement("Content", new XAttribute("Include", "$(MSBuildUserExtensionsPath)/input.txt"))));
            var path = Path.Combine(root, "Owner.proj");
            await File.WriteAllTextAsync(path, xml.ToString());
            Assert.AreEqual(value, ReadNativeProperty(path, property),
                "Independent native property evaluation must establish exact scalar path text before role capture.");
            var node = EvaluatedBuildGraph.FromRoots([path], new Dictionary<string, string>(),
                ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None).Nodes.Single();
            var captured = property switch
            {
                "OutputPath" => node.PathEvidence.Directories,
                "TargetPath" => node.PathEvidence.OutputFiles,
                "MSBuildUserExtensionsPath" => node.PathEvidence.NativeInputDirectories,
                _ => node.PathEvidence.InputFiles
            };
            Assert.IsTrue(captured.Any(input => FileSystemPath.Comparer.Equals(Path.GetFullPath(input), Path.GetFullPath(value))),
                "An already evaluated physical property must not disappear when its literal value resembles a pattern or expression.");
        }
        catch (Exception failure)
        {
            primaryFailure = failure;
            throw;
        }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Native fixture cleanup failed.", primaryFailure,
                OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string Escape(string value) => Microsoft.Build.Evaluation.ProjectCollection.Escape(value);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string ReadNativeProperty(string path, string property)
    {
        using var collection = new Microsoft.Build.Evaluation.ProjectCollection();
        return collection.LoadProject(path).GetPropertyValue(property);
    }
}
