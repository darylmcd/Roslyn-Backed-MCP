using System.Collections.Immutable;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeTraversalRootTests
{
    [TestMethod]
    [DataRow("include", true)]
    [DataRow("import", true)]
    [DataRow("remove", false)]
    [DataRow("update", false)]
    [DataRow("exclude", false)]
    [DataRow("inactive-item", false)]
    [DataRow("inactive-import", false)]
    [DataRow("literal", false)]
    public async Task NativeZeroMatchTraversal_BindsActualRootsWithoutInventingFilterOrLiteralQueries(string shape, bool traverses)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(NativeTraversalRootTests), Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source");
        var external = Path.Combine(root, "external");
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(external);
            var project = Path.Combine(source, "Owner.proj");
            var body = shape switch
            {
                "include" => "<ItemGroup><Compile Include='../external/**/*.cs' /></ItemGroup>",
                "import" => "<Import Project='imports/entry.props' />",
                "remove" => "<ItemGroup><Compile Include='Owned.cs' /><Compile Remove='../external/**/*.cs' /></ItemGroup>",
                "update" => "<ItemGroup><Compile Include='Owned.cs' /><Compile Update='../external/**/*.cs'><Link>changed</Link></Compile></ItemGroup>",
                "exclude" => "<ItemGroup><Compile Include='Owned.cs' Exclude='../external/**/*.cs' /></ItemGroup>",
                "inactive-item" => "<ItemGroup Condition='false'><Compile Include='../external/**/*.cs' /></ItemGroup>",
                "inactive-import" => "<Import Project='../external/**/*.props' Condition='false' />",
                "literal" => "<ItemGroup><Compile Include='../external/literal%253B.cs' /></ItemGroup>",
                _ => throw new AssertFailedException("Unknown native fixture shape.")
            };
            if (shape == "import")
            {
                Directory.CreateDirectory(Path.Combine(source, "imports"));
                await File.WriteAllTextAsync(Path.Combine(source, "imports", "entry.props"),
                    "<Project><Import Project='../../external/**/*.props' /></Project>");
            }
            await File.WriteAllTextAsync(Path.Combine(source, "Owned.cs"), "class Owned {} ");
            await File.WriteAllTextAsync(project, "<Project>" + body + "</Project>");
            // Reuse the exact authored paths in another graph capture. Neither an
            // earlier empty result nor another node may hide a native query.
            for (var pass = 0; pass != 2; pass++)
            {
                var graph = EvaluatedBuildGraph.FromRoots([project], new Dictionary<string, string>(),
                    ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None);
                var observed = graph.Nodes.SelectMany(node => node.PathEvidence.TraversalRoots).ToHashSet(FileSystemPath.Comparer);
                Assert.AreEqual(traverses, observed.Contains(external), "Native enumeration must establish the actual external traversal base.");
                var membership = NativeBuildMembership.Capture(graph, CancellationToken.None);
                var candidate = Path.Combine(external, "collector-" + Guid.NewGuid().ToString("N"));
                Task Check() => membership.AssertTemporaryPathDisjointAsync(candidate, [], static (_, _) => Task.CompletedTask, CancellationToken.None);
                if (traverses) await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(Check);
                else await Check();
                Assert.IsFalse(Directory.Exists(candidate), "Disjointness proof precedes any collector namespace effect.");
            }
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Native traversal fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }
}
