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
            $$"""{ "libraries": { {{librariesJson}} } }""");
    }

    [TestMethod]
    public void TryGetResolvedPackageVersion_PackagePresent_ReturnsParsedVersion()
    {
        var projectDirectory = CreateProjectDirectory();
        WriteAssetsFile(projectDirectory, "\"TUnit.Engine/1.46.0\": { \"type\": \"package\" }");
        var projectPath = Path.Combine(projectDirectory, "Fixture.csproj");
        File.WriteAllText(projectPath, "<Project />");

        var version = ProjectMetadataParser.TryGetResolvedPackageVersion(projectPath, "TUnit.Engine");

        Assert.AreEqual(new Version(1, 46, 0), version);
    }

    [TestMethod]
    public void TryGetResolvedPackageVersion_PackageNameMatchIsCaseInsensitive()
    {
        var projectDirectory = CreateProjectDirectory();
        WriteAssetsFile(projectDirectory, "\"tunit.engine/1.46.0\": { \"type\": \"package\" }");
        var projectPath = Path.Combine(projectDirectory, "Fixture.csproj");
        File.WriteAllText(projectPath, "<Project />");

        var version = ProjectMetadataParser.TryGetResolvedPackageVersion(projectPath, "TUnit.Engine");

        Assert.AreEqual(new Version(1, 46, 0), version);
    }

    [TestMethod]
    public void TryGetResolvedPackageVersion_PackageNotPresent_ReturnsNull()
    {
        var projectDirectory = CreateProjectDirectory();
        WriteAssetsFile(projectDirectory, "\"MSTest.TestFramework/3.6.0\": { \"type\": \"package\" }");
        var projectPath = Path.Combine(projectDirectory, "Fixture.csproj");
        File.WriteAllText(projectPath, "<Project />");

        var version = ProjectMetadataParser.TryGetResolvedPackageVersion(projectPath, "TUnit.Engine");

        Assert.IsNull(version);
    }

    [TestMethod]
    public void TryGetResolvedPackageVersion_NoAssetsFile_ReturnsNull()
    {
        // Project never restored — obj/project.assets.json doesn't exist yet.
        var projectDirectory = CreateProjectDirectory();
        var projectPath = Path.Combine(projectDirectory, "Fixture.csproj");
        File.WriteAllText(projectPath, "<Project />");

        var version = ProjectMetadataParser.TryGetResolvedPackageVersion(projectPath, "TUnit.Engine");

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
        File.WriteAllText(projectPath, "<Project />");

        var version = ProjectMetadataParser.TryGetResolvedPackageVersion(projectPath, "TUnit.Engine");

        Assert.IsNull(version);
    }

    [TestMethod]
    public void TryGetResolvedPackageVersion_NullProjectPath_ReturnsNull()
    {
        Assert.IsNull(ProjectMetadataParser.TryGetResolvedPackageVersion(null, "TUnit.Engine"));
    }

    [TestMethod]
    [DataRow("[]")]
    [DataRow("null")]
    [DataRow("42")]
    [DataRow("{\"libraries\":[]}")]
    [DataRow("{\"libraries\":null}")]
    [DataRow("{\"libraries\":true}")]
    public void TryGetResolvedPackageVersion_UnexpectedJsonKind_ReturnsUnknown(string json)
    {
        var directory = CreateProjectDirectory();
        Directory.CreateDirectory(Path.Combine(directory, "obj"));
        File.WriteAllText(Path.Combine(directory, "Fixture.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(directory, "obj", "project.assets.json"), json);
        Assert.IsNull(ProjectMetadataParser.TryGetResolvedPackageVersion(
            Path.Combine(directory, "Fixture.csproj"), "TUnit.Engine"));
    }

    [TestMethod]
    [DataRow("BaseIntermediateOutputPath")]
    [DataRow("MSBuildProjectExtensionsPath")]
    public void TryGetResolvedPackageVersion_AlternateAssetsLayout_ReadsRestoredVersion(string property)
    {
        var directory = CreateProjectDirectory();
        var projectPath = Path.Combine(directory, "Fixture.csproj");
        File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(directory, "Directory.Build.props"),
            $"<Project><PropertyGroup><{property}>customobj/</{property}></PropertyGroup></Project>");
        Directory.CreateDirectory(Path.Combine(directory, "customobj"));
        File.WriteAllText(Path.Combine(directory, "customobj", "project.assets.json"),
            "{\"libraries\":{\"TUnit.Engine/1.46.0\":{\"type\":\"package\"}}}");
        Assert.AreEqual(new Version(1, 46, 0),
            ProjectMetadataParser.TryGetResolvedPackageVersion(projectPath, "TUnit.Engine"));
    }

    [TestMethod]
    public void TryGetResolvedPackageVersion_StaleDefaultAssets_DoNotOverrideActiveLayout()
    {
        var directory = CreateProjectDirectory();
        var projectPath = Path.Combine(directory, "Fixture.csproj");
        File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(directory, "Directory.Build.props"),
            "<Project><PropertyGroup><MSBuildProjectExtensionsPath>customobj/</MSBuildProjectExtensionsPath></PropertyGroup></Project>");
        WriteAssetsFile(directory, "\"TUnit.Engine/1.46.0\":{}");
        Directory.CreateDirectory(Path.Combine(directory, "customobj"));
        File.WriteAllText(Path.Combine(directory, "customobj", "project.assets.json"),
            "{\"libraries\":{\"TUnit.Engine/1.45.0\":{\"type\":\"package\"}}}");
        Assert.AreEqual(new Version(1, 45, 0),
            ProjectMetadataParser.TryGetResolvedPackageVersion(projectPath, "TUnit.Engine"));
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
