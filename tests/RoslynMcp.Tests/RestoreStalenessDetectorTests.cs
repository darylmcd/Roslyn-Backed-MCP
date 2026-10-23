using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Unit coverage for <see cref="RestoreStalenessDetector"/> — extracted from
/// <see cref="WorkspaceManager"/> by workspace-manager-decompose-restore-and-analyzer-
/// subsystems as a zero-external-caller, purely code-moved collaborator. Uses a small
/// hand-built temp-dir fixture (a single .csproj + obj/project.assets.json) rather than the
/// full SampleSolution — <see cref="RestoreStalenessDetector.IsRestoreRequired"/> only ever
/// reads a project file and its sibling assets file from disk.
/// </summary>
[TestClass]
public sealed class RestoreStalenessDetectorTests
{
    private string _tempRoot = null!;

    [TestInitialize]
    public void Initialize()
    {
        _tempRoot = Path.Combine(TestTempRoot.Current, "RestoreStaleness", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    [TestCleanup]
    public void Cleanup()
    {
        TestFixtureFileSystem.DeleteDirectoryIfExists(_tempRoot);
    }


    [TestMethod]
    [DataRow("null")]
    [DataRow("[]")]
    [DataRow("""{"project":null}""")]
    [DataRow("""{"project":{"frameworks":{"net10.0":null}}}""")]
    [DataRow("""{"project":{"frameworks":{"net10.0":{"dependencies":{"Newtonsoft.Json":4}}}}}""")]
    [DataRow("""{"project":{"frameworks":{"net10.0":{"centralPackageVersions":{"Newtonsoft.Json":[]}}}}}""")]
    public void IsRestoreRequired_ValidJsonWrongShape_RequiresRestore(string json)
    {
        var path = WriteProject("Shape.csproj", PackageReferenceProject);
        Directory.CreateDirectory(Path.Combine(_tempRoot, "obj"));
        File.WriteAllText(Path.Combine(_tempRoot, "obj", "project.assets.json"), json);
        Assert.IsTrue(new RestoreStalenessDetector().IsRestoreRequired(path, System.Collections.Immutable.ImmutableDictionary<string, string>.Empty, NullLogger.Instance));
    }

    [TestMethod]
    public void IsRestoreRequired_NoPackageReferences_ReturnsFalse()
    {
        var projectFilePath = WriteProject(
            "NoRefs.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var detector = new RestoreStalenessDetector();

        Assert.IsFalse(detector.IsRestoreRequired(projectFilePath, System.Collections.Immutable.ImmutableDictionary<string, string>.Empty, NullLogger.Instance));
    }

    [TestMethod]
    public void IsRestoreRequired_MissingAssetsFile_ReturnsTrue()
    {
        var projectFilePath = WriteProject(
            "MissingAssets.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
              </ItemGroup>
            </Project>
            """);

        var detector = new RestoreStalenessDetector();

        Assert.IsTrue(detector.IsRestoreRequired(projectFilePath, System.Collections.Immutable.ImmutableDictionary<string, string>.Empty, NullLogger.Instance));
    }

    private const string PackageReferenceProject = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
          </ItemGroup>
        </Project>
        """;

    [TestMethod]
    [DataRow("{broken-private-assets-content")]
    [DataRow("[]")]
    [DataRow("{\"project\":42}")]
    public void IsRestoreRequired_UnusableAssetsWithKnownPackages_ReturnsTrue(string contents)
    {
        var project = WriteProject("Unusable.csproj", PackageReferenceProject);
        WriteAssets(project, "Newtonsoft.Json", "13.0.3");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(project)!, "obj", "project.assets.json"), contents);

        var logger = new ListLogger<RestoreStalenessDetector>();
        Assert.IsTrue(new RestoreStalenessDetector().IsRestoreRequired(project, ImmutableDictionary<string, string>.Empty, logger));
        Assert.HasCount(1, logger.Entries);
        Assert.IsNull(logger.Entries[0].Exception, "Assets diagnostics must not retain raw parse or I/O exceptions.");
        Assert.IsFalse(logger.Entries[0].Message.Contains(project, StringComparison.Ordinal));
        Assert.IsFalse(logger.Entries[0].Message.Contains("broken-private-assets-content", StringComparison.Ordinal));
        Assert.IsNull(RestoreStalenessDetector.TryReadRestorePackagesPath(project, ImmutableDictionary<string, string>.Empty),
            "Restore planning must tolerate the same unusable assets.");
    }

    [TestMethod]
    public void IsRestoreRequired_UnreadableAssetsWithKnownPackages_ReturnsTrue()
    {
        var project = WriteProject("LockedAssets.csproj", PackageReferenceProject);
        WriteAssets(project, "Newtonsoft.Json", "13.0.3");
        using var exclusive = new FileStream(Path.Combine(Path.GetDirectoryName(project)!, "obj", "project.assets.json"),
            FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.IsTrue(new RestoreStalenessDetector().IsRestoreRequired(project, ImmutableDictionary<string, string>.Empty, NullLogger.Instance));
    }

    [TestMethod]
    public void IsRestoreRequired_UnreadableProject_DoesNotClaimKnownPackageFailure()
    {
        var project = WriteProject("LockedMetadata.csproj", PackageReferenceProject);
        using var exclusive = new FileStream(project, FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.IsFalse(new RestoreStalenessDetector().IsRestoreRequired(project, ImmutableDictionary<string, string>.Empty, NullLogger.Instance));
    }

    [TestMethod]
    public void HasMissingAssets_PackageReferenceWithoutAssetsFile_ReturnsTrue()
    {
        var projectFilePath = WriteProject("NeverRestored.csproj", PackageReferenceProject);

        Assert.IsTrue(RestoreStalenessDetector.HasMissingAssets(projectFilePath, System.Collections.Immutable.ImmutableDictionary<string, string>.Empty, NullLogger.Instance));
    }

    [TestMethod]
    public void HasMissingAssets_AssetsPresentWithDriftedVersion_ReturnsFalse()
    {
        var projectFilePath = WriteProject("Drifted.csproj", PackageReferenceProject);
        WriteAssets(projectFilePath, "Newtonsoft.Json", "12.0.1");

        Assert.IsFalse(RestoreStalenessDetector.HasMissingAssets(projectFilePath, System.Collections.Immutable.ImmutableDictionary<string, string>.Empty, NullLogger.Instance), "Drift is not missing assets.");
        Assert.IsTrue(new RestoreStalenessDetector().IsRestoreRequired(projectFilePath, System.Collections.Immutable.ImmutableDictionary<string, string>.Empty, NullLogger.Instance));
    }

    [TestMethod]
    public void HasMissingAssets_NoPackageReferencesOrMissingProject_ReturnsFalse()
    {
        var noRefs = WriteProject(
            "NoRefs2.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        Assert.IsFalse(RestoreStalenessDetector.HasMissingAssets(noRefs, System.Collections.Immutable.ImmutableDictionary<string, string>.Empty, NullLogger.Instance));
        Assert.IsFalse(RestoreStalenessDetector.HasMissingAssets(Path.Combine(_tempRoot, "Nope.csproj"), System.Collections.Immutable.ImmutableDictionary<string, string>.Empty, NullLogger.Instance));
    }

    [TestMethod]
    public void HasMissingAssets_UnreadableProjectFile_ReturnsFalseWithoutThrowing()
    {
        var projectFilePath = WriteProject("Locked.csproj", PackageReferenceProject);
        using var exclusive = new FileStream(projectFilePath, FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.IsFalse(RestoreStalenessDetector.HasMissingAssets(projectFilePath, System.Collections.Immutable.ImmutableDictionary<string, string>.Empty, NullLogger.Instance));
    }

    [TestMethod]
    public void HasMissingAssets_UnevaluableProject_ConservativelyRequiresRestoreWithoutInventingItems()
    {
        // Well-formed XML that MSBuild rejects (unknown top-level element) so the assets-path
        // evaluation throws InvalidProjectFileException; a PackageReference keeps the probe going.
        var projectFilePath = WriteProject(
            "Unevaluable.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <Bogus />
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
              </ItemGroup>
            </Project>
            """);

        Assert.IsTrue(RestoreStalenessDetector.HasMissingAssets(projectFilePath, System.Collections.Immutable.ImmutableDictionary<string, string>.Empty, NullLogger.Instance));
    }

    [TestMethod]
    public void HasMissingAssets_MalformedXml_DoesNotClaimAnEvaluatedRestoreRequirement()
    {
        var path = WriteProject("Malformed.csproj", "<Project><PropertyGroup>");
        Assert.IsFalse(RestoreStalenessDetector.HasMissingAssets(path, ImmutableDictionary<string, string>.Empty, NullLogger.Instance));
    }

    [TestMethod]
    public void HasMissingAssets_ArtifactsOutputLayout_FindsRelocatedAssets()
    {
        // UseArtifactsOutput moves project.assets.json to <artifacts>/obj/<ProjectName>/, so the
        // default <projectDir>/obj probe would misread a fully restored project as never restored.
        File.WriteAllText(
            Path.Combine(_tempRoot, "Directory.Build.props"),
            "<Project><PropertyGroup><UseArtifactsOutput>true</UseArtifactsOutput></PropertyGroup></Project>");
        var projectFilePath = WriteProject("Relocated.csproj", PackageReferenceProject);

        Assert.IsTrue(RestoreStalenessDetector.HasMissingAssets(projectFilePath, System.Collections.Immutable.ImmutableDictionary<string, string>.Empty, NullLogger.Instance), "Nothing restored yet.");

        var relocatedDirectory = Path.Combine(_tempRoot, "artifacts", "obj", "Relocated");
        Directory.CreateDirectory(relocatedDirectory);
        File.WriteAllText(
            Path.Combine(relocatedDirectory, "project.assets.json"),
            "{\"project\":{\"restore\":{\"packagesPath\":\"D:/scratch\"},\"frameworks\":{\"net10.0\":{\"dependencies\":{\"Newtonsoft.Json\":{\"version\":\"13.0.3\"}}}}}}");

        Assert.IsFalse(RestoreStalenessDetector.HasMissingAssets(projectFilePath, System.Collections.Immutable.ImmutableDictionary<string, string>.Empty, NullLogger.Instance));
        Assert.IsFalse(new RestoreStalenessDetector().IsRestoreRequired(projectFilePath, System.Collections.Immutable.ImmutableDictionary<string, string>.Empty, NullLogger.Instance));
        Assert.AreEqual("D:/scratch", RestoreStalenessDetector.TryReadRestorePackagesPath(projectFilePath, System.Collections.Immutable.ImmutableDictionary<string, string>.Empty));
    }

    [TestMethod]
    public void IsRestoreRequired_AssetsVersionMismatch_ReturnsTrue()
    {
        var projectFilePath = WriteProject(
            "Mismatch.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
              </ItemGroup>
            </Project>
            """);
        WriteAssets(projectFilePath, "Newtonsoft.Json", "12.0.0");

        var detector = new RestoreStalenessDetector();

        Assert.IsTrue(detector.IsRestoreRequired(projectFilePath, System.Collections.Immutable.ImmutableDictionary<string, string>.Empty, NullLogger.Instance));
    }

    [TestMethod]
    public void IsRestoreRequired_AssetsVersionMatches_ReturnsFalse()
    {
        var projectFilePath = WriteProject(
            "Match.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
              </ItemGroup>
            </Project>
            """);
        WriteAssets(projectFilePath, "Newtonsoft.Json", "13.0.3");

        var detector = new RestoreStalenessDetector();

        Assert.IsFalse(detector.IsRestoreRequired(projectFilePath, System.Collections.Immutable.ImmutableDictionary<string, string>.Empty, NullLogger.Instance));
    }

    [TestMethod]
    public void IsRestoreRequired_NonexistentProjectFile_ReturnsFalse()
    {
        var detector = new RestoreStalenessDetector();

        Assert.IsFalse(detector.IsRestoreRequired(Path.Combine(_tempRoot, "DoesNotExist.csproj"), System.Collections.Immutable.ImmutableDictionary<string, string>.Empty, NullLogger.Instance));
    }

    [TestMethod]
    public async Task DetectRestoreRequired_AggregatesAcrossProjects_TrueWhenAnyProjectStale()
    {
        var freshProjectPath = WriteProject(
            "Fresh.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
              </ItemGroup>
            </Project>
            """);
        WriteAssets(freshProjectPath, "Newtonsoft.Json", "13.0.3");

        var staleProjectPath = WriteProject(
            "Stale.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
              </ItemGroup>
            </Project>
            """);
        // Intentionally no assets file for the "stale" project.

        var projects = ImmutableArray.Create(
            MakeProjectStatus(freshProjectPath),
            MakeProjectStatus(staleProjectPath));

        var detector = new RestoreStalenessDetector();

        using var workspace = new Microsoft.CodeAnalysis.AdhocWorkspace();
        Assert.IsTrue(await detector.DetectRestoreRequiredAsync(CreateSolution(workspace, projects),
            (paths, token) => new RoslynMcp.Tests.Helpers.OwnedTestWorkspacePathPolicy().ValidateAsync("restore-staleness", paths, token), NullLogger.Instance, CancellationToken.None));
    }

    [TestMethod]
    public async Task DetectRestoreRequired_AllProjectsFresh_ReturnsFalse()
    {
        var freshProjectPath = WriteProject(
            "AllFresh.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var projects = ImmutableArray.Create(MakeProjectStatus(freshProjectPath));

        var detector = new RestoreStalenessDetector();

        using var workspace = new Microsoft.CodeAnalysis.AdhocWorkspace();
        Assert.IsFalse(await detector.DetectRestoreRequiredAsync(CreateSolution(workspace, projects),
            (paths, token) => new RoslynMcp.Tests.Helpers.OwnedTestWorkspacePathPolicy().ValidateAsync("restore-staleness", paths, token), NullLogger.Instance, CancellationToken.None));
    }

    [TestMethod]
    public void HasRestoreRequiredWorkspaceDiagnostics_ThreeOrMoreSuspiciousDiagnostics_ReturnsTrue()
    {
        var diagnostics = new[]
        {
            MakeDiagnostic("CS0234", "The type or namespace could not be found"),
            MakeDiagnostic("CS0246", "The type or namespace name could not be found"),
            MakeDiagnostic("CS0246", "The type or namespace name could not be found"),
        };

        Assert.IsTrue(RestoreStalenessDetector.HasRestoreRequiredWorkspaceDiagnostics(diagnostics));
    }

    [TestMethod]
    public void HasRestoreRequiredWorkspaceDiagnostics_FewerThanThree_ReturnsFalse()
    {
        var diagnostics = new[]
        {
            MakeDiagnostic("CS0234", "unrelated"),
        };

        Assert.IsFalse(RestoreStalenessDetector.HasRestoreRequiredWorkspaceDiagnostics(diagnostics));
    }

    [TestMethod]
    public void WorkspaceDiagnosticClassifier_UnresolvedAnalyzerDiagnostic_ReturnsTrue()
    {
        var diagnostics = new[]
        {
            MakeDiagnostic("WORKSPACE_UNRESOLVED_ANALYZER", "stripped"),
        };

        Assert.IsTrue(WorkspaceDiagnosticClassifier.HasBuildRequired(diagnostics));
    }

    [TestMethod]
    public void WorkspaceDiagnosticClassifier_NoMatchingDiagnostic_ReturnsFalse()
    {
        var diagnostics = new[]
        {
            MakeDiagnostic("CS0246", "unrelated"),
        };

        Assert.IsFalse(WorkspaceDiagnosticClassifier.HasBuildRequired(diagnostics));
    }

    private static Microsoft.CodeAnalysis.Solution CreateSolution(Microsoft.CodeAnalysis.Workspace workspace, ImmutableArray<ProjectStatusDto> projects)
    {
        var solution = workspace.CurrentSolution;
        foreach (var project in projects)
            solution = solution.AddProject(Microsoft.CodeAnalysis.ProjectInfo.Create(Microsoft.CodeAnalysis.ProjectId.CreateNewId(),
                Microsoft.CodeAnalysis.VersionStamp.Create(), project.Name, project.AssemblyName, Microsoft.CodeAnalysis.LanguageNames.CSharp, filePath: project.FilePath));
        return solution;
    }

    private static DiagnosticDto MakeDiagnostic(string id, string message) =>
        new(Id: id, Message: message, Severity: "Warning", Category: "Compiler",
            FilePath: null, StartLine: null, StartColumn: null, EndLine: null, EndColumn: null);

    private static ProjectStatusDto MakeProjectStatus(string projectFilePath) =>
        new(Name: Path.GetFileNameWithoutExtension(projectFilePath),
            FilePath: projectFilePath,
            DocumentCount: 0,
            ProjectReferences: Array.Empty<string>(),
            TargetFrameworks: Array.Empty<string>(),
            IsTestProject: false,
            AssemblyName: Path.GetFileNameWithoutExtension(projectFilePath),
            OutputType: "Library");

    private string WriteProject(string fileName, string contents)
    {
        // Each project gets its own subdirectory so its obj/project.assets.json fixture
        // cannot be shadowed by a sibling project written into the same temp root (all
        // projects would otherwise share one "obj" directory).
        var projectDirectory = Path.Combine(_tempRoot, Path.GetFileNameWithoutExtension(fileName));
        Directory.CreateDirectory(projectDirectory);
        var path = Path.Combine(projectDirectory, fileName);
        File.WriteAllText(path, contents);
        return path;
    }

    private static void WriteAssets(string projectFilePath, string packageId, string version)
    {
        var projectDirectory = Path.GetDirectoryName(projectFilePath)!;
        var objDirectory = Path.Combine(projectDirectory, "obj");
        Directory.CreateDirectory(objDirectory);
        var assetsJson = $$"""
            {
              "project": {
                "frameworks": {
                  "net10.0": {
                    "dependencies": {
                      "{{packageId}}": { "version": "{{version}}" }
                    }
                  }
                }
              }
            }
            """;
        File.WriteAllText(Path.Combine(objDirectory, "project.assets.json"), assetsJson);
    }
}
