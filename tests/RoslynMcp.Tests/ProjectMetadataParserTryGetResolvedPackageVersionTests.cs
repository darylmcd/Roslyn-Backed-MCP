using RoslynMcp.Roslyn.Helpers;

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
    public void Cleanup()
    {
        foreach (var root in _createdRoots)
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup; a locked file just leaks a temp directory.
            }
        }
    }

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
}
