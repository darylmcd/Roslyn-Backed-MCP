using System.Security;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ImmutableInputAuthorityTests
{
    [TestMethod]
    [DataRow("")]
    [DataRow("NetCoreRoot")]
    [DataRow("NetCoreTargetingPackRoot")]
    public async Task ProjectControlledToolRoot_CannotExemptDeniedArtifact(string propertyName)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(ImmutableInputAuthorityTests), Guid.NewGuid().ToString("N"));
        var projectDirectory = Path.Combine(root, "owned");
        var deniedDirectory = Path.Combine(root, "denied");
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(projectDirectory);
            Directory.CreateDirectory(deniedDirectory);
            var projectPath = Path.Combine(projectDirectory, "Owner.csproj");
            var artifact = Path.Combine(deniedDirectory, "BenignReference.dll");
            File.Copy(typeof(object).Assembly.Location, artifact);
            var property = propertyName.Length == 0 ? "" : $"<{propertyName}>{SecurityElement.Escape(deniedDirectory)}</{propertyName}>";
            await File.WriteAllTextAsync(projectPath, $"<Project><PropertyGroup>{property}<ProjectAssetsFile>obj/project.assets.json</ProjectAssetsFile></PropertyGroup></Project>");
            await File.WriteAllTextAsync(Path.Combine(projectDirectory, "global.json"), "{}");
            var workspace = new AdhocWorkspace();
            Exception? usingCleanupPrimary1 = null;
            try
            {
                var id = ProjectId.CreateNewId();
                var solution = workspace.CurrentSolution.AddProject(ProjectInfo.Create(id, VersionStamp.Create(), "Owner", "Owner", LanguageNames.CSharp,
                    filePath: projectPath, metadataReferences: [MetadataReference.CreateFromFile(artifact)]));
                var policy = new DenySubtreePolicy(deniedDirectory);
                await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => PhysicalGraphLayout.CreateAsync(
                    "forged-tool-root", projectPath, solution, policy, CancellationToken.None));
                Assert.IsTrue(policy.DeniedProbes > 0, "The current policy must observe the denied artifact before any explicit artifact read/copy.");
                CollectionAssert.AreEqual(File.ReadAllBytes(typeof(object).Assembly.Location), await File.ReadAllBytesAsync(artifact));

            }
            catch (Exception failure) { usingCleanupPrimary1 = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Native authority resource cleanup failed.", usingCleanupPrimary1, workspace); }
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Native authority fixture directory cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    [TestMethod]
    [DataRow("unrelated-authored-subtree")]
    [DataRow("example.package/1.2.3")]
    public async Task UnverifiedLibraryPath_CannotMintImmutablePackageDirectory(string libraryPath)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(ImmutableInputAuthorityTests), Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var assets = Path.Combine(root, "project.assets.json");
            await File.WriteAllTextAsync(assets, JsonSerializer.Serialize(new
            {
                packageFolders = new Dictionary<string, object> { [root] = new { } },
                libraries = new Dictionary<string, object>
                {
                    ["Example.Package/1.2.3"] = new { type = "package", path = libraryPath, files = new[] { "Benign.cs" } }
                }
            }));
            await new OwnedTestWorkspacePathPolicy().ValidateAsync("forged-package", [assets], CancellationToken.None);
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                BuildInputProvenance.ReadPackagesAsync([assets], [], (paths, token) => new OwnedTestWorkspacePathPolicy().ValidateAsync("package-proof", paths, token), CancellationToken.None));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Native authority fixture directory cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    private sealed class DenySubtreePolicy(string deniedDirectory) : IWorkspacePathPolicy
    {
        public int DeniedProbes { get; private set; }
        public Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (paths.Any(path => FileSystemPath.Comparer.Equals(path, deniedDirectory)
                || FileSystemPath.IsStrictDescendant(deniedDirectory, path)))
            {
                DeniedProbes++;
                throw new UnauthorizedAccessException("The controlled artifact subtree is outside current authority.");
            }
            return new OwnedTestWorkspacePathPolicy().ValidateAsync(workspaceId, paths, ct);
        }
    }
}
