using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class BuildPathRolesLiteralExistsTests
{
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
    public Task NativeExists_EscapedExactQueryRetainsLiteralText(string metacharacters, bool propertyProduced)
        => VerifyAsync(metacharacters, propertyProduced, escaped: true);

    [TestMethod]
    [DataRow("*")]
    [DataRow("?")]
    public Task NativeExists_UnescapedWildcardQueryIsExactFileQuery(string metacharacters)
        => VerifyAsync(metacharacters, propertyProduced: false, escaped: false);

    private static async Task VerifyAsync(string metacharacters, bool propertyProduced, bool escaped)
    {
        MsBuildInitializer.EnsureInitialized();
        var root = Path.Combine(TestTempRoot.Current, "literal-exists-role-" + Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var queried = Path.Combine(root, "literal" + metacharacters + ".flag");
            var cannotCreateOnWindows = OperatingSystem.IsWindows() && metacharacters is "*" or "?";
            if (!cannotCreateOnWindows) await File.WriteAllTextAsync(queried, "owned exact query input");
            // A native Exists wildcard argument does not enumerate this matching file.
            await File.WriteAllTextAsync(Path.Combine(root, "literalMatched.flag"), "separate glob-looking match");
            var nativeTruth = File.Exists(queried);
            Assert.AreEqual(!cannotCreateOnWindows, nativeTruth);
            var query = escaped ? Escape(queried) : queried;
            var xml = new XElement("Project");
            if (propertyProduced) xml.Add(new XElement("PropertyGroup", new XElement("QueryInput", query)));
            xml.Add(new XElement("PropertyGroup", new XElement("Selected", new XAttribute("Condition",
                "Exists('" + (propertyProduced ? "$(QueryInput)" : query) + "')"), "true")));
            var path = Path.Combine(root, "Owner.proj");
            await File.WriteAllTextAsync(path, xml.ToString());
            Assert.AreEqual(nativeTruth, ReadNativeTruth(path),
                "Independent native evaluation must establish exact filename/query semantics before role capture.");
            var node = EvaluatedBuildGraph.FromRoots([path], new Dictionary<string, string>(),
                ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None).Nodes.Single();
            var probes = node.PathEvidence.ConditionProbes.Where(probe =>
                FileSystemPath.Comparer.Equals(Path.GetFullPath(probe.Path), Path.GetFullPath(queried))).ToArray();
            Assert.IsTrue(probes.Length > 0, "Exact native query input must remain in the captured condition inventory.");
            Assert.IsTrue(probes.All(probe => probe.ObservedExists == nativeTruth));
            Assert.IsTrue(probes.All(probe => FileSystemPath.Comparer.Equals(Path.GetFullPath(probe.QueryPath), Path.GetFullPath(queried))));
            Assert.AreEqual(nativeTruth, File.Exists(queried));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            OwnedResourceCleanup.Dispose("Failed to release native fixture.", primary,
                OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string Escape(string value) => Microsoft.Build.Evaluation.ProjectCollection.Escape(value);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool ReadNativeTruth(string path)
    {
        Microsoft.Build.Evaluation.ProjectCollection? collection = null;
        Exception? primary = null;
        try
        {
            collection = new Microsoft.Build.Evaluation.ProjectCollection();
            return collection.LoadProject(path).GetPropertyValue("Selected") == "true";
        }
        catch (Exception failure) { primary = failure; throw; }
        finally { OwnedResourceCleanup.Dispose("Failed to release native truth reader.", primary, collection); }
    }
}
