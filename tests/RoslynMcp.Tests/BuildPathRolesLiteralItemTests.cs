using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class BuildPathRolesLiteralItemTests
{
    [TestMethod]
    [DataRow("Include")]
    [DataRow("Update")]
    [DataRow("Remove")]
    [DataRow("Exclude")]
    public async Task AbsoluteLiteralItem_RetainsCoordinateWithoutParentConditionProbe(string attribute)
    {
        var root = Path.Combine(TestTempRoot.Current, "literal-item-role-" + Guid.NewGuid().ToString("N"));
        var projectDirectory = Path.Combine(root, "project");
        var external = Path.Combine(root, "external");
        Directory.CreateDirectory(projectDirectory);
        Directory.CreateDirectory(external);
        Exception? primaryFailure = null;
        try
        {
            var file = Path.Combine(external, "literal.txt");
            await File.WriteAllTextAsync(file, "owned literal content");
            var relative = Path.GetRelativePath(projectDirectory, file);
            var group = new XElement("ItemGroup");
            if (attribute is "Update" or "Remove") group.Add(new XElement("Content", new XAttribute("Include", relative)));
            var item = attribute == "Exclude"
                ? new XElement("Content", new XAttribute("Include", relative), new XAttribute("Exclude", file))
                : new XElement("Content", new XAttribute(attribute, file));
            if (attribute == "Update") item.Add(new XElement("Marker", "updated"));
            group.Add(item);
            var path = Path.Combine(projectDirectory, "Owner.proj");
            await File.WriteAllTextAsync(path, new XElement("Project", group).ToString());
            MsBuildInitializer.EnsureInitialized();
            var native = ReadNativeItems(path);
            Assert.AreEqual(attribute is "Remove" or "Exclude" ? 0 : 1, native.Length,
                "Native evaluation must establish the literal item operation before role assertions.");
            if (native.Length == 1)
            {
                Assert.AreEqual(file, native[0].Path);
                Assert.AreEqual(attribute == "Update" ? "updated" : "", native[0].Marker);
            }
            var node = ReadNode(path);
            Assert.IsTrue(node.PathEvidence.InputCoordinates.Any(coordinate => Same(coordinate.Directory, external)),
                "A literal path retains its relocation anchor even when native evaluation removes or excludes the item.");
            if (native.Length == 1) Assert.IsTrue(node.PathEvidence.InputFiles.Any(input => Same(input, file)));
            Assert.IsFalse(node.PathEvidence.ConditionProbes.Any(probe => Same(probe.Path, external)),
                "A literal file coordinate does not imply a queried or enumerated parent directory.");
            Assert.AreEqual("owned literal content", await File.ReadAllTextAsync(file));
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

    [TestMethod]
    [DataRow("*", false, false)]
    [DataRow("?", false, false)]
    [DataRow("*", true, false)]
    [DataRow("?", true, false)]
    [DataRow("*", false, true)]
    [DataRow("?", false, true)]
    [DataRow("*", true, true)]
    [DataRow("?", true, true)]
    [DataRow("$(", false, false)]
    [DataRow("@(", false, false)]
    [DataRow("%(", false, false)]
    [DataRow("$(", true, false)]
    [DataRow("@(", true, false)]
    [DataRow("%(", true, false)]
    [DataRow("$(", false, true)]
    [DataRow("@(", false, true)]
    [DataRow("%(", false, true)]
    [DataRow("$(", true, true)]
    [DataRow("@(", true, true)]
    [DataRow("%(", true, true)]
    public async Task EscapedWildcardItem_IsNativeLiteralWithoutDirectoryProbe(string metacharacters, bool propertyProduced, bool directoryComponent)
    {
        var root = Path.Combine(TestTempRoot.Current, "escaped-item-role-" + Guid.NewGuid().ToString("N"));
        var external = Path.Combine(root, "external");
        Directory.CreateDirectory(external);
        Exception? primaryFailure = null;
        try
        {
            var literal = directoryComponent ? Path.Combine(external, "literal" + metacharacters, "file.txt")
                : Path.Combine(external, "literal" + metacharacters + ".txt");
            var literalParent = Path.GetDirectoryName(literal)!;
            var escaped = EscapeLiteral(literal);
            var xml = new XElement("Project");
            if (propertyProduced) xml.Add(new XElement("PropertyGroup", new XElement("LiteralInput", escaped)));
            xml.Add(new XElement("ItemGroup", new XElement("Content", new XAttribute("Include", propertyProduced ? "$(LiteralInput)" : escaped))));
            var path = Path.Combine(root, "Owner.proj");
            await File.WriteAllTextAsync(path, xml.ToString());
            MsBuildInitializer.EnsureInitialized();
            var native = ReadNativeItems(path);
            Assert.AreEqual(1, native.Length, "Escaped metacharacters denote one literal native item, including a missing physical file.");
            Assert.AreEqual(literal, native[0].Path);
            var node = ReadNode(path);
            Assert.IsTrue(node.PathEvidence.InputCoordinates.Any(coordinate => Same(coordinate.Directory, literalParent)));
            Assert.IsTrue(node.PathEvidence.InputFiles.Any(input => Same(input, literal)));
            Assert.IsFalse(node.PathEvidence.ConditionProbes.Any(probe => Same(probe.Path, literalParent)),
                "Decoding an escaped metacharacter must not invent wildcard directory authority.");
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

    [TestMethod]
    [DataRow("*", false)]
    [DataRow("?", false)]
    [DataRow("$(", false)]
    [DataRow("@(", false)]
    [DataRow("%(", false)]
    [DataRow("*", true)]
    [DataRow("?", true)]
    [DataRow("$(", true)]
    [DataRow("@(", true)]
    [DataRow("%(", true)]
    public async Task ExactNativeArtifactPath_RetainsEscapedFilename(string metacharacters, bool hintPath)
    {
        var root = Path.Combine(TestTempRoot.Current, "exact-artifact-role-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Exception? primaryFailure = null;
        try
        {
            var artifact = Path.Combine(root, "external", "literal" + metacharacters, "assembly.dll");
            var declaration = hintPath ? new XElement("Reference", new XAttribute("Include", "LiteralReference"),
                new XElement("HintPath", "$(ArtifactDirectory)/assembly.dll"))
                : new XElement("Analyzer", new XAttribute("Include", EscapeLiteral(artifact)));
            var path = Path.Combine(root, "Owner.proj");
            await File.WriteAllTextAsync(path, new XElement("Project",
                new XElement("PropertyGroup", new XElement("ArtifactDirectory", EscapeLiteral(Path.GetDirectoryName(artifact)!))),
                new XElement("ItemGroup", declaration)).ToString());
            MsBuildInitializer.EnsureInitialized();
            CollectionAssert.AreEqual(new[] { artifact }, ReadNativeArtifactPaths(path, hintPath),
                "Actual native item/metadata evaluation must establish exact filename text before graph inventory assertions.");
            var node = ReadNode(path);
            Assert.IsTrue(node.ArtifactFiles.Any(input => Same(input, artifact)),
                "An admitted native artifact path must not disappear because its literal filename resembles MSBuild syntax.");
            if (!hintPath) Assert.IsTrue(node.PathEvidence.InputFiles.Any(input => Same(input, artifact)));
            Assert.IsTrue(node.PathEvidence.InputCoordinates.Any(coordinate => Same(coordinate.Directory, Path.GetDirectoryName(artifact)!)),
                "Exact native analyzer/HintPath coordinates must remain available for relocation.");
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

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ActualWildcardItem_RetainsNativeDirectoryProbe(bool propertyProduced)
    {
        var root = Path.Combine(TestTempRoot.Current, "glob-item-role-" + Guid.NewGuid().ToString("N"));
        var external = Path.Combine(root, "external");
        Directory.CreateDirectory(external);
        Exception? primaryFailure = null;
        try
        {
            var first = Path.Combine(external, "First.txt");
            var second = Path.Combine(external, "Second.txt");
            await File.WriteAllTextAsync(first, "first");
            await File.WriteAllTextAsync(second, "second");
            var pattern = Path.Combine(external, "*.txt");
            var xml = new XElement("Project");
            if (propertyProduced) xml.Add(new XElement("PropertyGroup", new XElement("InputPattern", pattern)));
            xml.Add(new XElement("ItemGroup", new XElement("Content", new XAttribute("Include", propertyProduced ? "$(InputPattern)" : pattern))));
            var path = Path.Combine(root, "Owner.proj");
            await File.WriteAllTextAsync(path, xml.ToString());
            MsBuildInitializer.EnsureInitialized();
            CollectionAssert.AreEquivalent(new[] { first, second }, ReadNativeItems(path).Select(item => item.Path).ToArray(),
                "Actual native enumeration must establish both matches before checking the directory role.");
            var node = ReadNode(path);
            Assert.IsTrue(node.PathEvidence.InputCoordinates.Any(coordinate => Same(coordinate.Directory, external)));
            Assert.IsTrue(node.PathEvidence.ConditionProbes.Any(probe => Same(probe.Path, external)),
                "Removing spurious literal-parent probes must retain real wildcard enumeration inputs.");
            Assert.IsTrue(node.PathEvidence.InputFiles.Any(input => Same(input, first)));
            Assert.IsTrue(node.PathEvidence.InputFiles.Any(input => Same(input, second)));
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

    private static EvaluatedBuildGraph.Node ReadNode(string path) => EvaluatedBuildGraph.FromRoots([path],
        new Dictionary<string, string>(), ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None).Nodes.Single();

    private static bool Same(string left, string right) => FileSystemPath.Comparer.Equals(Path.GetFullPath(left), Path.GetFullPath(right));

    private static string EscapeLiteral(string value) => value.Replace("%", "%25", StringComparison.Ordinal)
        .Replace("*", "%2A", StringComparison.Ordinal).Replace("?", "%3F", StringComparison.Ordinal)
        .Replace("$", "%24", StringComparison.Ordinal).Replace("@", "%40", StringComparison.Ordinal)
        .Replace("(", "%28", StringComparison.Ordinal).Replace(")", "%29", StringComparison.Ordinal).Replace(";", "%3B", StringComparison.Ordinal);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string[] ReadNativeArtifactPaths(string path, bool hintPath)
    {
        using var collection = new Microsoft.Build.Evaluation.ProjectCollection();
        var project = collection.LoadProject(path);
        return project.GetItems(hintPath ? "Reference" : "Analyzer").Select(item =>
            hintPath ? Path.GetFullPath(item.GetMetadataValue("HintPath"), Path.GetDirectoryName(path)!)
                : item.GetMetadataValue("FullPath")).ToArray();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (string Path, string Marker)[] ReadNativeItems(string path)
    {
        using var collection = new Microsoft.Build.Evaluation.ProjectCollection();
        var project = collection.LoadProject(path);
        return project.GetItems("Content").Select(item =>
            (item.GetMetadataValue("FullPath"), item.GetMetadataValue("Marker"))).ToArray();
    }
}
