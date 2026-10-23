using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeCapturedItemCoordinateTests
{
    [TestMethod]
    [DataRow("Compile", @"generated\Nested\Input.cs")]
    [DataRow("Compile", "generated/literal%5CInput.cs")]
    [DataRow("Compile", "generated/literal%255CInput.cs")]
    [DataRow("Compile", "owned-rooted-backslash")]
    [DataRow("ProjectReference", @"references\Nested\Target.csproj")]
    [DataRow("ProjectReference", "references/literal%5CTarget.csproj")]
    [DataRow("ProjectReference", "references/literal%255CTarget.csproj")]
    [DataRow("ProjectReference", "owned-rooted-backslash")]
    public async Task CapturedItemCoordinate_MatchesInstalledNativeFullPath(string itemType, string escapedInclude)
    {
        MsBuildInitializer.EnsureInitialized();
        await CheckAsync(itemType, escapedInclude);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task CheckAsync(string itemType, string escapedInclude) =>
        NativeItemCoordinateOracle.WithNativeItemAsync(nameof(NativeCapturedItemCoordinateTests), itemType, escapedInclude,
            (projectPath, nativePath) =>
        {
            using var collection = new Microsoft.Build.Evaluation.ProjectCollection();
            var instance = new Microsoft.Build.Execution.ProjectInstance(projectPath,
                new Dictionary<string, string>(), null, collection);
            if (itemType == "ProjectReference")
            {
                var references = CompilerProjectReferences.Capture(instance,
                    new[] { ImmutableArray<CompilerProjectReferenceTarget>.Empty });
                Assert.AreEqual(1, references.Declared.Length);
                Assert.AreEqual(nativePath, references.Declared[0].Path);
            }
            else
            {
                var evaluation = NativeBuildMembership.CaptureEvaluation(instance, CancellationToken.None);
                Assert.AreEqual(1, evaluation.Files.Length);
                Assert.AreEqual(nativePath, evaluation.Files[0].Path,
                    "Restore output/package admission must compare the actual native file coordinate.");
            }
            return Task.CompletedTask;
        });
}
