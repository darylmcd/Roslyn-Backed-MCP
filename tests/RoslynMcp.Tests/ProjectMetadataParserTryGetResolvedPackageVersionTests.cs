using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// tunit-treenode-filter-or-requires-tunit-fix: TestRunnerService needs a project's real,
/// NuGet-resolved package version (e.g. TUnit.Engine, to gate the OR filter translation on
/// whether thomhurst/TUnit#6026 is fixed) rather than a version guessed from the .csproj's
/// PackageReference Version attribute, which can be a range/wildcard/floating version that
/// doesn't say what actually got restored. project.assets.json is the authoritative record.
/// </summary>
[TestClass]
public sealed class ProjectMetadataParserTryGetResolvedPackageVersionTests
{
    private readonly List<string> _createdRoots = [];

    [TestCleanup]
    public async Task Cleanup() =>
        await CleanupFailureCollector.DeleteDirectoriesAsync(_createdRoots);

    private string CreateProjectDirectory()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "ProjectMetadataParserTryGetResolvedPackageVersionTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        _createdRoots.Add(root);
        return root;
    }

    private static void WriteAssetsFile(string projectDirectory, string librariesJson)
    {
        var objDir = Path.Combine(projectDirectory, "obj");
        Directory.CreateDirectory(objDir);
        File.WriteAllText(
            Path.Combine(objDir, "project.assets.json"),
            $$"""{ "targets": { "net10.0": { {{librariesJson}} } }, "libraries": { {{librariesJson}} }, "project": { "frameworks": { "net10.0": {} } } }""");
    }

    [TestMethod]
    public void TryGetResolvedPackageVersion_PackagePresent_ReturnsParsedVersion()
    {
        var projectDirectory = CreateProjectDirectory();
        WriteAssetsFile(projectDirectory, "\"TUnit.Engine/1.46.0\": { \"type\": \"package\" }");
        var projectPath = Path.Combine(projectDirectory, "Fixture.csproj");
        File.WriteAllText(projectPath, "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework><ProjectAssetsFile>obj/project.assets.json</ProjectAssetsFile></PropertyGroup></Project>");

        var version = ProjectMetadataParser.TryGetResolvedPackageVersions(projectPath, "TUnit.Engine", System.Collections.Immutable.ImmutableDictionary<string, string>.Empty);

        Assert.AreEqual(new NuGet.Versioning.NuGetVersion(1, 46, 0), version?.Single());
    }

    [TestMethod]
    public void TryGetResolvedPackageVersion_PackageNameMatchIsCaseInsensitive()
    {
        var projectDirectory = CreateProjectDirectory();
        WriteAssetsFile(projectDirectory, "\"tunit.engine/1.46.0\": { \"type\": \"package\" }");
        var projectPath = Path.Combine(projectDirectory, "Fixture.csproj");
        File.WriteAllText(projectPath, "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework><ProjectAssetsFile>obj/project.assets.json</ProjectAssetsFile></PropertyGroup></Project>");

        var version = ProjectMetadataParser.TryGetResolvedPackageVersions(projectPath, "TUnit.Engine", System.Collections.Immutable.ImmutableDictionary<string, string>.Empty);

        Assert.AreEqual(new NuGet.Versioning.NuGetVersion(1, 46, 0), version?.Single());
    }

    [TestMethod]
    public void TryGetResolvedPackageVersion_PackageNotPresent_ReturnsNull()
    {
        var projectDirectory = CreateProjectDirectory();
        WriteAssetsFile(projectDirectory, "\"MSTest.TestFramework/3.6.0\": { \"type\": \"package\" }");
        var projectPath = Path.Combine(projectDirectory, "Fixture.csproj");
        File.WriteAllText(projectPath, "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework><ProjectAssetsFile>obj/project.assets.json</ProjectAssetsFile></PropertyGroup></Project>");

        var version = ProjectMetadataParser.TryGetResolvedPackageVersions(projectPath, "TUnit.Engine", System.Collections.Immutable.ImmutableDictionary<string, string>.Empty);

        Assert.IsNull(version);
    }

    [TestMethod]
    public void TryGetResolvedPackageVersion_NoAssetsFile_ReturnsNull()
    {
        // Project never restored — obj/project.assets.json doesn't exist yet.
        var projectDirectory = CreateProjectDirectory();
        var projectPath = Path.Combine(projectDirectory, "Fixture.csproj");
        File.WriteAllText(projectPath, "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework><ProjectAssetsFile>obj/project.assets.json</ProjectAssetsFile></PropertyGroup></Project>");

        var version = ProjectMetadataParser.TryGetResolvedPackageVersions(projectPath, "TUnit.Engine", System.Collections.Immutable.ImmutableDictionary<string, string>.Empty);

        Assert.IsNull(version);
    }

    [TestMethod]
    public void TryGetResolvedPackageVersion_MalformedAssetsFile_ReturnsNullWithoutThrowing()
    {
        var projectDirectory = CreateProjectDirectory();
        var objDir = Path.Combine(projectDirectory, "obj");
        Directory.CreateDirectory(objDir);
        File.WriteAllText(Path.Combine(objDir, "project.assets.json"), "{ not valid json");
        var projectPath = Path.Combine(projectDirectory, "Fixture.csproj");
        File.WriteAllText(projectPath, "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework><ProjectAssetsFile>obj/project.assets.json</ProjectAssetsFile></PropertyGroup></Project>");

        var version = ProjectMetadataParser.TryGetResolvedPackageVersions(projectPath, "TUnit.Engine", System.Collections.Immutable.ImmutableDictionary<string, string>.Empty);

        Assert.IsNull(version);
    }

    [TestMethod]
    public void TryGetResolvedPackageVersion_NullProjectPath_ReturnsNull()
    {
        Assert.IsNull(ProjectMetadataParser.TryGetResolvedPackageVersions(null, "TUnit.Engine", System.Collections.Immutable.ImmutableDictionary<string, string>.Empty));
    }

    [TestMethod]
    public async Task Cleanup_DeleteFailureForOneRoot_SurfacesAggregateExceptionAfterAttemptingAllRoots()
    {
        var root1 = CreateProjectDirectory();
        var root2 = CreateProjectDirectory();
        var expectedFailure = new IOException("Injected delete failure.");

        var ex = await Assert.ThrowsExactlyAsync<AggregateException>(async () =>
            await CleanupFailureCollector.DeleteDirectoriesAsync(
                [root1, root2],
                root =>
                {
                    if (string.Equals(root, root1, StringComparison.Ordinal))
                    {
                        throw expectedFailure;
                    }

                    Directory.Delete(root, recursive: true);
                }));

        Assert.AreEqual(1, ex.InnerExceptions.Count);
        Assert.AreSame(expectedFailure, ex.InnerExceptions[0]);
        Assert.IsFalse(Directory.Exists(root2), "The second root's delete must still be attempted despite the first root's failure.");
    }
}
