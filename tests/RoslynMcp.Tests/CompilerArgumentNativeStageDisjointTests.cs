using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class CompilerArgumentNativeStageDisjointTests
{
    [TestMethod]
    [DataRow("include", true)]
    [DataRow("import", true)]
    [DataRow("include", false)]
    [DataRow("import", false)]
    public async Task NativeZeroMatchTraversal_ExcludesParserStageBeforeAnyNamespaceEffect(string shape, bool overlaps)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(CompilerArgumentNativeStageDisjointTests), Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source");
        var traversal = Path.Combine(root, "native-traversal");
        var privateParent = Path.Combine(root, "private-stage");
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(traversal);
            Directory.CreateDirectory(privateParent);
            var project = Path.Combine(source, "Owner.proj");
            var body = shape == "include"
                ? "<ItemGroup><Compile Include='../native-traversal/**/*.cs' /></ItemGroup>"
                : "<Import Project='../native-traversal/**/*.props' />";
            await File.WriteAllTextAsync(project, "<Project>" + body + "</Project>");
            await File.WriteAllTextAsync(Path.Combine(source, "root.ruleset"),
                "<RuleSet Name='Root' ToolsVersion='14.0'><Rules AnalyzerId='Compiler' RuleNamespace='Compiler'><Rule Id='CS0168' Action='Error' /></Rules></RuleSet>");
            var graph = EvaluatedBuildGraph.FromRoots([project], new Dictionary<string, string>(),
                ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None);
            Assert.IsTrue(graph.Nodes.SelectMany(node => node.PathEvidence.TraversalRoots)
                .Contains(traversal, FileSystemPath.Comparer), "A completed actual native zero-match query must establish the traversal root.");
            Assert.AreEqual(0, graph.Nodes.Sum(node => node.DocumentFiles.Count));
            var membership = NativeBuildMembership.Capture(graph, CancellationToken.None);
            var parent = overlaps ? traversal : privateParent;
            var stage = Path.Combine(parent, "issued-parser-stage");
            var before = Directory.GetFileSystemEntries(traversal);
            var stageObservedAfterCreation = false;
            Task NativeDisjoint(string candidate, CancellationToken ct) => membership.AssertTemporaryPathDisjointAsync(
                candidate, [], static (_, _) => Task.CompletedTask, ct);
            async Task Authorize(IReadOnlyList<string> paths, CancellationToken ct)
            {
                if (Directory.Exists(stage))
                {
                    stageObservedAfterCreation = true;
                    // Old code discovers this boundary only after creating its
                    // private stage; the recorded effect survives Windows cleanup.
                    await NativeDisjoint(stage, ct);
                }
            }
            Task<byte[]?> Read(string path, CancellationToken ct) => FileSnapshotCapture.ReadIfPresentAsync(path, File.ReadAllBytesAsync, ct);
            var context = new CompilerArgumentInputContext(source, Authorize, Read,
                (path, ct) => FileContentDigest.FromReaderAsync(path, Read, ct), () => stage, NativeDisjoint);
            if (overlaps)
            {
                var refusal = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                    context.ParseAsync(["-ruleset:root.ruleset"], CancellationToken.None));
                StringAssert.Contains(refusal.PublicMessage, "outside every source traversal");
                Assert.IsFalse(stageObservedAfterCreation, "Native traversal disjointness must refuse BEFORE creating the parser stage, even without /recurse.");
                Assert.AreSequenceEqual(before, Directory.GetFileSystemEntries(traversal));
                Assert.IsFalse(Directory.Exists(stage));
            }
            else
            {
                var receipt = await context.ParseAsync(["-ruleset:root.ruleset"], CancellationToken.None);
                Assert.AreEqual(ReportDiagnostic.Error, receipt.Arguments.CompilationOptions.SpecificDiagnosticOptions["CS0168"]);
                Assert.AreEqual(0, receipt.RecursiveTraversalRoots.Count(), "This control must exercise native roots without a compiler /recurse receipt.");
                Assert.IsTrue(stageObservedAfterCreation, "The disjoint positive must actually exercise parser staging.");
                Assert.AreSequenceEqual(before, Directory.GetFileSystemEntries(traversal));
            }
        }
        catch (Exception exception) { primary = exception; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native parser-stage fixture cleanup failed.", primary,
            OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root))); }
    }
}
