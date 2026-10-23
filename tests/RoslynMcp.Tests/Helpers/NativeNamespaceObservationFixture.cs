using System.Collections.Immutable;
using System.Xml.Linq;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests.Helpers;

/// <summary>Authored native observers without service-directory precreation or glob exclusions.</summary>
internal static class NativeNamespaceObservationFixture
{
    internal const string SeedName = "owned-native-namespace-seed.txt";
    private const string FlagName = "OwnedNamespaceObserved";
    internal sealed record Observation(string[] AdditionalFiles, string Flag);

    internal static async Task AddAsync(string project, string sourceRoot, string shape, string serviceDirectory)
    {
        var relative = Path.GetRelativePath(Path.GetDirectoryName(project)!, sourceRoot).Replace('\\', '/');
        var xml = XDocument.Load(project);
        var owner = xml.Root ?? throw new AssertFailedException("The fixture requires an actual native project.");
        if (shape == "recursive-item")
        {
            await File.WriteAllTextAsync(Path.Combine(sourceRoot, SeedName), "original native recursive input");
            owner.Add(new XElement("ItemGroup", new XElement("AdditionalFiles",
                new XAttribute("Include", relative + "/**/" + SeedName))));
        }
        else if (shape == "native-exists")
        {
            owner.Add(new XElement("PropertyGroup", new XElement(FlagName, "absent")),
                new XElement("PropertyGroup", new XAttribute("Condition",
                    "Exists('$(MSBuildProjectDirectory)/" + relative + "/.roslynmcp/" + serviceDirectory + "')"),
                    new XElement(FlagName, "present")));
        }
        else throw new AssertFailedException("Unknown native namespace observer.");
        await File.WriteAllTextAsync(project, xml.ToString());
    }

    internal static Observation Capture(string project, IReadOnlyDictionary<string, string> globals) => new(
        MsBuildMetadataHelper.EvaluateCompilerDeclarations(project, globals).Items.Where(item => item.Kind == "AdditionalFiles")
            .Select(item => item.Path).Order(FileSystemPath.Comparer).ToArray(),
        MsBuildMetadataHelper.EvaluateProperties(project, globals, FlagName).Single());

    internal static Observation AssertBaseline(string project, string sourceRoot, string shape, string serviceDirectory,
        IReadOnlyDictionary<string, string> globals)
    {
        var graph = EvaluatedBuildGraph.FromRoots([project], globals, ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None);
        var before = Capture(project, globals);
        if (shape == "recursive-item")
        {
            Assert.IsTrue(graph.Nodes.SelectMany(node => node.PathEvidence.TraversalRoots).Contains(sourceRoot, FileSystemPath.Comparer),
                "The completed native query must actually traverse the source root.");
            Assert.AreEqual(1, before.AdditionalFiles.Count(path => FileSystemPath.Comparer.Equals(path, Path.Combine(sourceRoot, SeedName))),
                "The authored native seed must be selected exactly once before service namespaces exist.");
        }
        else
        {
            var coordinate = Path.Combine(sourceRoot, ".roslynmcp", serviceDirectory);
            Assert.IsFalse(Directory.Exists(coordinate), "Precreating the service parent would hide its source-visible effect.");
            Assert.AreEqual("absent", before.Flag);
            Assert.IsTrue(graph.Nodes.SelectMany(node => node.PathEvidence.ConditionProbes)
                .Any(probe => FileSystemPath.Comparer.Equals(probe.Path, coordinate)
                    && FileSystemPath.Comparer.Equals(Path.GetFullPath(probe.QueryPath), coordinate) && probe.ObservedExists == false),
                "The completed native condition receipt must witness the actual absent service namespace.");
        }
        return before;
    }

    internal static void AssertUnchanged(Observation expected, string project, IReadOnlyDictionary<string, string> globals)
    {
        var current = Capture(project, globals);
        Assert.AreSequenceEqual(expected.AdditionalFiles, current.AdditionalFiles,
            "Service-owned staging/forking must not introduce native recursive source members.");
        Assert.AreEqual(expected.Flag, current.Flag,
            "Service-owned staging/forking must not change an executed native source condition.");
    }
}
