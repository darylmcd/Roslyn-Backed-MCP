using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeGraphItemCoordinateTests
{
    [TestMethod]
    [DataRow("Analyzer", @"generated\Nested\Input.cs")]
    [DataRow("Analyzer", "generated/literal%5CInput.cs")]
    [DataRow("Analyzer", "generated/literal%255CInput.cs")]
    [DataRow("Analyzer", "owned-rooted-backslash")]
    [DataRow("Compile", @"generated\Nested\Input.cs")]
    [DataRow("Compile", "generated/literal%5CInput.cs")]
    [DataRow("Compile", "generated/literal%255CInput.cs")]
    [DataRow("Compile", "owned-rooted-backslash")]
    [DataRow("AdditionalFiles", @"generated\Nested\Input.cs")]
    [DataRow("AdditionalFiles", "generated/literal%5CInput.cs")]
    [DataRow("AdditionalFiles", "generated/literal%255CInput.cs")]
    [DataRow("AdditionalFiles", "owned-rooted-backslash")]
    [DataRow("EditorConfigFiles", @"generated\Nested\Input.cs")]
    [DataRow("EditorConfigFiles", "generated/literal%5CInput.cs")]
    [DataRow("EditorConfigFiles", "generated/literal%255CInput.cs")]
    [DataRow("EditorConfigFiles", "owned-rooted-backslash")]
    public async Task GraphInputCoordinate_MatchesInstalledNativeFullPath(string itemType, string escapedInclude)
    {
        MsBuildInitializer.EnsureInitialized();
        await CheckAsync(itemType, escapedInclude);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task CheckAsync(string itemType, string escapedInclude) =>
        NativeItemCoordinateOracle.WithNativeItemAsync(nameof(NativeGraphItemCoordinateTests), itemType, escapedInclude,
            (projectPath, nativePath) =>
        {
            var graph = EvaluatedBuildGraph.FromRoots([projectPath], new Dictionary<string, string>(),
                ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None);
            var owner = graph.Nodes.Single(node => node.ProjectPath == projectPath);
            var inputs = itemType == "Analyzer" ? owner.ArtifactFiles : owner.DocumentFiles;
            Assert.AreEqual(1, inputs.Count);
            Assert.AreEqual(nativePath, inputs.Single(),
                "The graph must authorize and hash the exact file selected by native item metadata.");
            return Task.CompletedTask;
        });
}
