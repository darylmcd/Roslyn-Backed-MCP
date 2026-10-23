using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class EvaluatedExecutionStringIdentityTests
{
    [TestMethod]
    [DataRow("global-value")]
    [DataRow("global-key")]
    [DataRow("default-alias")]
    [DataRow("target-key")]
    [DataRow("runtime")]
    [DataRow("package-id")]
    [DataRow("same")]
    public async Task CapturedGraphIdentity_PreservesExactUtf16CodeUnits(string shape)
    {
        MsBuildInitializer.EnsureInitialized();
        await CheckAsync(shape, structuredOnly: false);
    }

    [TestMethod]
    [DataRow("default-alias")]
    [DataRow("target-key")]
    [DataRow("runtime")]
    [DataRow("package-id")]
    [DataRow("same")]
    public async Task DuplicateStructuredInputs_PreserveExactUtf16CodeUnits(string shape)
    {
        MsBuildInitializer.EnsureInitialized();
        await CheckAsync(shape, structuredOnly: true);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task CheckAsync(string shape, bool structuredOnly)
    {
        var root = Path.Combine(TestTempRoot.Current, "graph-code-units-" + Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "Owner.csproj");
            new XDocument(new XElement("Project", new XElement("PropertyGroup",
                new XElement("TargetPath", Path.Combine(root, "Owner.dll"))))).Save(path);
            var graph = EvaluatedBuildGraph.FromRoots([path], ImmutableDictionary<string, string>.Empty,
                ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None);
            Assert.AreEqual(1, graph.Nodes.Length);
            var first = new string((char)0xD800, 1);
            var second = shape == "same" ? first : new string((char)0xD801, 1);
            var left = Change(graph.Nodes[0], shape, first);
            var right = Change(graph.Nodes[0], shape, second);
            if (structuredOnly)
            {
                Assert.AreEqual(shape == "same", EvaluatedBuildGraph.StructuredInputsEquivalent(left, right, CancellationToken.None),
                    "Duplicate consistency must distinguish every captured code unit, including nested dictionary keys.");
                return;
            }
            var before = EvaluatedExecutionIdentity.Capture(new EvaluatedBuildGraph.Graph([left], [left]), static value => value,
                ImmutableHashSet<string>.Empty, CancellationToken.None);
            var after = EvaluatedExecutionIdentity.Capture(new EvaluatedBuildGraph.Graph([right], [right]), static value => value,
                ImmutableHashSet<string>.Empty, CancellationToken.None);
            // Bind the actual two captured identity fields; do not compare a test copy of the encoder.
            var fields = new[] { "_entries", "_nodes" };
            var equal = fields.All(name => ((string[])typeof(EvaluatedExecutionIdentity)
                    .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(before)!)
                .SequenceEqual((string[])typeof(EvaluatedExecutionIdentity)
                    .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(after)!, StringComparer.Ordinal));
            Assert.AreEqual(shape == "same", equal,
                "Captured execution identity must not replace distinct UTF-16 code units with the same replacement character.");
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Native UTF16 fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    private static EvaluatedBuildGraph.Node Change(EvaluatedBuildGraph.Node node, string shape, string value) => shape switch
    {
        "global-value" => node with { Globals = node.Globals.SetItem("SemanticContext", value) },
        "global-key" => node with { Globals = node.Globals.SetItem("SemanticContext" + value, "fixed") },
        "default-alias" or "same" => node with
        {
            CompilerReferences = node.CompilerReferences with { Defaults = new(true, [value], false) }
        },
        "target-key" => node with
        {
            CompilerReferences = node.CompilerReferences with
            {
                Declared = [new(node.ProjectPath, new(true, [], false),
                    [new(node.ProjectPath, node.TargetPath, "fixed", ImmutableDictionary<string, string>.Empty.Add("Context" + value, "fixed"))])]
            }
        },
        "runtime" => node with { Inputs = node.Inputs with { RuntimeIdentifier = value } },
        "package-id" => node with { Inputs = node.Inputs with { Packages = [new("Package" + value, "1.0.0", false)] } },
        _ => throw new AssertFailedException("Unknown controlled shape.")
    };
}
