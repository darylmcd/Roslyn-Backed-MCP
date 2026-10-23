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
// Ambient controls mutate process-wide environment; native membership freezes every inherited property.
[DoNotParallelize]
public sealed class NativeMembershipPropertyTests
{
    [TestMethod]
    public void Membership_BindsUnreferencedProcessEnvironmentWithoutChangingItemsOrDocuments()
    {
        MsBuildInitializer.EnsureInitialized();
        CheckUnreferencedAmbientProperty();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CheckUnreferencedAmbientProperty()
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(NativeMembershipPropertyTests), Guid.NewGuid().ToString("N"));
        var ambient = "ROSLYN_UNREFERENCED_MEMBERSHIP_" + Guid.NewGuid().ToString("N");
        var previous = Environment.GetEnvironmentVariable(ambient);
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var project = Path.Combine(root, "Owner.csproj");
            var document = Path.Combine(root, "Entry.cs");
            const string definition = "<Project><ItemGroup><Compile Include=\"Entry.cs\" /></ItemGroup></Project>";
            Assert.IsFalse(definition.Contains(ambient, StringComparison.Ordinal), "The project must not reference the unique environment key.");
            File.WriteAllText(project, definition);
            File.WriteAllText(document, "public sealed class Entry { }");
            var projectBytes = File.ReadAllBytes(project);
            var documentBytes = File.ReadAllBytes(document);
            Environment.SetEnvironmentVariable(ambient, "before");
            Assert.AreEqual("before", ReadProperty(project, ambient), "A real native evaluation must inherit the unreferenced ambient property.");
            var graph = Capture(project);
            var membership = NativeBuildMembership.Capture(graph, CancellationToken.None);
            membership.AssertCurrent(CancellationToken.None);
            Assert.HasCount(1, graph.Nodes);
            CollectionAssert.Contains(graph.Nodes.Single().DocumentFiles.ToArray(), document);
            Environment.SetEnvironmentVariable(ambient, "after");
            Assert.AreEqual("after", ReadProperty(project, ambient), "A fresh native evaluation must observe the changed ambient value.");
            var changed = Capture(project);
            Assert.HasCount(1, changed.Nodes);
            CollectionAssert.AreEqual(graph.EntryNodes.Select(node => node.ProjectPath).ToArray(),
                changed.EntryNodes.Select(node => node.ProjectPath).ToArray());
            CollectionAssert.AreEquivalent(graph.EntryNodes.Single().Globals.ToArray(), changed.EntryNodes.Single().Globals.ToArray());
            Assert.AreEqual(graph.Nodes.Single().EvaluatedItems, changed.Nodes.Single().EvaluatedItems);
            CollectionAssert.AreEqual(graph.Nodes.Single().DocumentFiles.ToArray(), changed.Nodes.Single().DocumentFiles.ToArray());
            CollectionAssert.AreEqual(projectBytes, File.ReadAllBytes(project));
            CollectionAssert.AreEqual(documentBytes, File.ReadAllBytes(document));
            Assert.AreNotEqual(graph.Nodes.Single().EvaluatedProperties, changed.Nodes.Single().EvaluatedProperties,
                "Unreferenced process environment remains part of the complete native evaluated property context.");
            Assert.IsFalse(membership.EquivalentTo(NativeBuildMembership.Capture(changed, CancellationToken.None)));
            Assert.ThrowsExactly<PublicInvalidOperationException>(() => membership.AssertCurrent(CancellationToken.None));
            Environment.SetEnvironmentVariable(ambient, "before");
            Assert.AreEqual("before", ReadProperty(project, ambient));
            membership.AssertCurrent(CancellationToken.None);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Unreferenced ambient membership fixture cleanup failed.", primary,
                OwnedResourceCleanup.FromAction(() => Environment.SetEnvironmentVariable(ambient, previous)),
                OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    [TestMethod]
    [DataRow("DefineConstants", "BEFORE", "AFTER", false)]
    [DataRow("LangVersion", "12.0", "13.0", false)]
    [DataRow("DefineConstants", "BEFORE", "AFTER", true)]
    public void Membership_BindsAmbientEvaluatedCompilerProperties(string property, string before, string after, bool overridden)
    {
        MsBuildInitializer.EnsureInitialized();
        CheckAmbientProperty(property, before, after, overridden);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CheckAmbientProperty(string property, string before, string after, bool overridden)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(NativeMembershipPropertyTests), Guid.NewGuid().ToString("N"));
        var ambient = "ROSLYN_MEMBERSHIP_" + Guid.NewGuid().ToString("N");
        var previous = Environment.GetEnvironmentVariable(ambient);
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var project = Path.Combine(root, "Owner.csproj");
            new XDocument(new XElement("Project", new XElement("PropertyGroup",
                overridden ? new XElement(ambient, before) : null,
                new XElement("IntermediateSemanticValue", "$(" + ambient + ")"),
                new XElement(property, "$(IntermediateSemanticValue)")),
                new XElement("ItemGroup", new XElement("Compile", new XAttribute("Include", "Entry.cs")))))
                .Save(project);
            File.WriteAllText(Path.Combine(root, "Entry.cs"), "public class Entry {}");
            var bytes = File.ReadAllBytes(project);
            Environment.SetEnvironmentVariable(ambient, before);
            Assert.AreEqual(before, ReadProperty(project, property));
            var graph = Capture(project);
            var membership = NativeBuildMembership.Capture(graph, CancellationToken.None);
            membership.AssertCurrent(CancellationToken.None);
            // A file which no native item selects cannot change evaluated context.
            File.WriteAllText(Path.Combine(root, "unselected.bin"), "unchanged evaluation");
            membership.AssertCurrent(CancellationToken.None);
            Environment.SetEnvironmentVariable(ambient, after);
            Assert.AreEqual(overridden ? before : after, ReadProperty(project, property),
                "A fresh native evaluation must observe the ambient compiler-property premise.");
            var changed = Capture(project);
            Assert.AreEqual(graph.Nodes.Single().EvaluatedItems, changed.Nodes.Single().EvaluatedItems,
                "This regression changes evaluated properties without changing item identity.");
            if (overridden) membership.AssertCurrent(CancellationToken.None);
            else
            {
                Assert.IsFalse(membership.EquivalentTo(NativeBuildMembership.Capture(changed, CancellationToken.None)));
                Assert.ThrowsExactly<PublicInvalidOperationException>(() => membership.AssertCurrent(CancellationToken.None));
            }
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(project));
            Environment.SetEnvironmentVariable(ambient, before);
            membership.AssertCurrent(CancellationToken.None);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Ambient membership fixture cleanup failed.", primary,
                OwnedResourceCleanup.FromAction(() => Environment.SetEnvironmentVariable(ambient, previous)),
                OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    private static EvaluatedBuildGraph.Graph Capture(string project) => EvaluatedBuildGraph.FromRoots([project],
        ImmutableDictionary<string, string>.Empty, ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None);

    private static string ReadProperty(string project, string property)
    {
        Microsoft.Build.Evaluation.ProjectCollection? collection = null;
        Exception? primary = null;
        try
        {
            collection = new Microsoft.Build.Evaluation.ProjectCollection();
            return collection.LoadProject(project).GetPropertyValue(property);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native fixture cleanup failed.", primary, collection); }
    }

    [TestMethod]
    [DataRow("order", true)]
    [DataRow("name-case", true)]
    [DataRow("value", false)]
    [DataRow("missing", false)]
    [DataRow("surrogate", false)]
    public void EvaluatedPropertyEncoding_PreservesExactValuesAndIgnoresPropertyEnumerationOrder(string change, bool equal)
    {
        MsBuildInitializer.EnsureInitialized();
        CheckEncoding(change, equal);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CheckEncoding(string change, bool equal)
    {
        Microsoft.Build.Evaluation.ProjectCollection? collection = null;
        Exception? primary = null;
        try
        {
            collection = new Microsoft.Build.Evaluation.ProjectCollection();
            var root = Microsoft.Build.Construction.ProjectRootElement.Create(collection);
            root.FullPath = Path.Combine(TestTempRoot.Current, nameof(NativeMembershipPropertyTests), "Encoding.csproj");
            Microsoft.Build.Execution.ProjectInstance Create(bool changed)
            {
                var instance = new Microsoft.Build.Execution.ProjectInstance(root, new Dictionary<string, string>(), null, collection);
                var values = new List<KeyValuePair<string, string>>
                {
                    new("CustomSemanticAlpha", change == "surrogate" ? new string((char)0xd800, 1) : "one"),
                    new("CustomSemanticBeta", "two")
                };
                if (changed)
                {
                    if (change == "order") values.Reverse();
                    if (change == "name-case") values[0] = new("customsemanticalpha", "one");
                    if (change == "value") values[0] = new("CustomSemanticAlpha", "changed");
                    if (change == "missing") values.RemoveAt(0);
                    if (change == "surrogate") values[0] = new("CustomSemanticAlpha", new string((char)0xfffd, 1));
                }
                foreach (var pair in values) instance.SetProperty(pair.Key, pair.Value);
                return instance;
            }
            var before = NativeBuildMembership.CaptureEvaluation(Create(false), CancellationToken.None);
            var after = NativeBuildMembership.CaptureEvaluation(Create(true), CancellationToken.None);
            if (equal) Assert.AreEqual(before.Properties, after.Properties);
            else Assert.AreNotEqual(before.Properties, after.Properties);
            Assert.AreEqual(before.Items, after.Items);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native fixture cleanup failed.", primary, collection); }
    }
}
