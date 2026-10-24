using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Unit coverage for <see cref="RestoreStalenessDetector"/> — extracted from
/// <see cref="WorkspaceManager"/> by workspace-manager-decompose-restore-and-analyzer-
/// subsystems as a zero-external-caller, purely code-moved collaborator. Uses a small
/// hand-built temp-dir fixture (a single .csproj + obj/project.assets.json) rather than the
/// full SampleSolution. The detector evaluates SDK/props metadata to locate the active assets
/// file, then compares its package observations with the declared expectations.
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
    [DataRow("{\"project\":null}")]
    [DataRow("{\"project\":{\"frameworks\":{\"net10.0\":null}}}")]
    [DataRow("{\"project\":{\"frameworks\":{\"net10.0\":{\"dependencies\":[]}}}}")]
    [DataRow("{\"project\":{\"frameworks\":{\"net10.0\":{\"dependencies\":{\"Newtonsoft.Json\":4}}}}}")]
    [DataRow("{\"project\":{\"frameworks\":{\"net10.0\":{\"dependencies\":{\"Newtonsoft.Json\":{\"version\":[]}}}}}}")]
    [DataRow("{\"project\":{\"frameworks\":{\"net10.0\":{\"centralPackageVersions\":[]}}}}")]
    [DataRow("{\"project\":{\"frameworks\":{\"net10.0\":{\"centralPackageVersions\":{\"Newtonsoft.Json\":[]}}}}}")]
    public void IsRestoreRequired_ValidJsonWrongShape_RequiresRestoreWithSanitizedDiagnostic(string json)
    {
        var project = WriteProject("Shape.csproj", PackageReferenceProject);
        var assetsDirectory = Path.Combine(Path.GetDirectoryName(project)!, "obj");
        Directory.CreateDirectory(assetsDirectory);
        File.WriteAllText(Path.Combine(assetsDirectory, "project.assets.json"), json);
        var logger = new ListLogger<RestoreStalenessDetector>();

        Assert.IsTrue(new RestoreStalenessDetector().IsRestoreRequired(project, ImmutableDictionary<string, string>.Empty, logger));
        Assert.HasCount(1, logger.Entries);
        Assert.IsNull(logger.Entries[0].Exception);
        Assert.IsFalse(logger.Entries[0].Message.Contains(project, StringComparison.Ordinal));
        Assert.IsFalse(logger.Entries[0].Message.Contains(json, StringComparison.Ordinal));
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

        Assert.IsFalse(detector.IsRestoreRequired(projectFilePath, ImmutableDictionary<string, string>.Empty, NullLogger.Instance));
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

        Assert.IsTrue(detector.IsRestoreRequired(projectFilePath, ImmutableDictionary<string, string>.Empty, NullLogger.Instance));
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
        Assert.IsNull(RestoreStalenessDetector.TryReadRestorePackagesPath(project),
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

        Assert.IsTrue(RestoreStalenessDetector.HasMissingAssets(projectFilePath, NullLogger.Instance));
    }

    [TestMethod]
    public void HasMissingAssets_AssetsPresentWithDriftedVersion_ReturnsFalse()
    {
        var projectFilePath = WriteProject("Drifted.csproj", PackageReferenceProject);
        WriteAssets(projectFilePath, "Newtonsoft.Json", "12.0.1");

        Assert.IsFalse(RestoreStalenessDetector.HasMissingAssets(projectFilePath, NullLogger.Instance), "Drift is not missing assets.");
        Assert.IsTrue(new RestoreStalenessDetector().IsRestoreRequired(projectFilePath, ImmutableDictionary<string, string>.Empty, NullLogger.Instance));
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

        Assert.IsFalse(RestoreStalenessDetector.HasMissingAssets(noRefs, NullLogger.Instance));
        Assert.IsFalse(RestoreStalenessDetector.HasMissingAssets(Path.Combine(_tempRoot, "Nope.csproj"), NullLogger.Instance));
    }

    [TestMethod]
    public void HasMissingAssets_UnreadableProjectFile_ReturnsFalseWithoutThrowing()
    {
        var projectFilePath = WriteProject("Locked.csproj", PackageReferenceProject);
        using var exclusive = new FileStream(projectFilePath, FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.IsFalse(RestoreStalenessDetector.HasMissingAssets(projectFilePath, NullLogger.Instance));
    }

    [TestMethod]
    public void HasMissingAssets_UnevaluableProject_DoesNotThrowAndTreatsAssetsAsAbsent()
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

        Assert.IsTrue(RestoreStalenessDetector.HasMissingAssets(projectFilePath, NullLogger.Instance));
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

        Assert.IsTrue(RestoreStalenessDetector.HasMissingAssets(projectFilePath, NullLogger.Instance), "Nothing restored yet.");

        var relocatedDirectory = Path.Combine(_tempRoot, "artifacts", "obj", "Relocated");
        Directory.CreateDirectory(relocatedDirectory);
        File.WriteAllText(
            Path.Combine(relocatedDirectory, "project.assets.json"),
            "{\"project\":{\"restore\":{\"packagesPath\":\"D:/scratch\"},\"frameworks\":{\"net10.0\":{\"dependencies\":{\"Newtonsoft.Json\":{\"version\":\"13.0.3\"}}}}}}");

        Assert.IsFalse(RestoreStalenessDetector.HasMissingAssets(projectFilePath, NullLogger.Instance));
        Assert.IsFalse(new RestoreStalenessDetector().IsRestoreRequired(projectFilePath, ImmutableDictionary<string, string>.Empty, NullLogger.Instance));
        Assert.AreEqual("D:/scratch", RestoreStalenessDetector.TryReadRestorePackagesPath(projectFilePath));
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

        Assert.IsTrue(detector.IsRestoreRequired(projectFilePath, ImmutableDictionary<string, string>.Empty, NullLogger.Instance));
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

        Assert.IsFalse(detector.IsRestoreRequired(projectFilePath, ImmutableDictionary<string, string>.Empty, NullLogger.Instance));
    }

    [TestMethod]
    public void IsRestoreRequired_NonexistentProjectFile_ReturnsFalse()
    {
        var detector = new RestoreStalenessDetector();

        Assert.IsFalse(detector.IsRestoreRequired(
            Path.Combine(_tempRoot, "DoesNotExist.csproj"),
            ImmutableDictionary<string, string>.Empty,
            NullLogger.Instance));
    }

    [TestMethod]
    public void IsRestoreRequired_ConfiguredMissingAssetsControl_IgnoresOtherConfiguration()
    {
        var project = WriteProject("Configured.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ProjectAssetsFile>$(MSBuildProjectDirectory)/obj/$(Configuration)/project.assets.json</ProjectAssetsFile>
              </PropertyGroup>
              <ItemGroup><PackageReference Include="Newtonsoft.Json" Version="13.0.3" /></ItemGroup>
            </Project>
            """);
        WriteAssets(project, "Newtonsoft.Json", "13.0.3");
        var objDirectory = Path.Combine(Path.GetDirectoryName(project)!, "obj");
        Directory.CreateDirectory(Path.Combine(objDirectory, "Debug"));
        File.Move(Path.Combine(objDirectory, "project.assets.json"), Path.Combine(objDirectory, "Debug", "project.assets.json"));
        var detector = new RestoreStalenessDetector();

        Assert.IsFalse(detector.IsRestoreRequired(project,
            ImmutableDictionary<string, string>.Empty.Add("Configuration", "Debug"), NullLogger.Instance));
        Assert.IsTrue(detector.IsRestoreRequired(project,
            ImmutableDictionary<string, string>.Empty.Add("Configuration", "Release"), NullLogger.Instance));
    }

    [TestMethod]
    public void DetectRestoreRequired_AggregatesAcrossProjects_TrueWhenAnyProjectStale()
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

        Assert.IsTrue(detector.DetectRestoreRequired(projects, ImmutableDictionary<string, string>.Empty, NullLogger.Instance));
    }

    [TestMethod]
    public void DetectRestoreRequired_AllProjectsFresh_ReturnsFalse()
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

        Assert.IsFalse(detector.DetectRestoreRequired(projects, ImmutableDictionary<string, string>.Empty, NullLogger.Instance));
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

    [TestMethod]
    [DataRow("default", false)]
    [DataRow("default", true)]
    [DataRow("base", false)]
    [DataRow("base", true)]
    [DataRow("extensions", false)]
    [DataRow("extensions", true)]
    [DataRow("explicit", false)]
    [DataRow("explicit", true)]
    public void ActiveAssets_ReadersUseEvaluatedLayout(string layout, bool readVersion)
    {
        var (project, active, stale, packages) = CreateAssetsLayout(layout);
        WriteAssetsFile(stale, "Newtonsoft.Json", "12.0.1", Path.Combine(_tempRoot, "stale-packages"));
        WriteAssetsFile(active, "Newtonsoft.Json", "13.0.3", packages);

        if (readVersion)
            Assert.AreEqual(new Version(13, 0, 3), ProjectMetadataParser.TryGetResolvedPackageVersion(project, "Newtonsoft.Json"));
        else
            Assert.AreEqual(packages, RestoreStalenessDetector.TryReadRestorePackagesPath(project));

        Assert.IsFalse(RestoreStalenessDetector.HasMissingAssets(project, NullLogger.Instance));
        Assert.IsFalse(new RestoreStalenessDetector().IsRestoreRequired(project, MsBuildMetadataHelper.DefaultGlobalProperties, NullLogger.Instance));
    }

    [TestMethod]
    [DataRow("base", false)]
    [DataRow("base", true)]
    [DataRow("extensions", false)]
    [DataRow("extensions", true)]
    [DataRow("explicit", false)]
    [DataRow("explicit", true)]
    public void ActiveAssets_MissingActiveFileNeverAuthorizesStaleDefault(string layout, bool readVersion)
    {
        var (project, active, stale, _) = CreateAssetsLayout(layout);
        WriteAssetsFile(stale, "Newtonsoft.Json", "13.0.3", Path.Combine(_tempRoot, "stale-packages"));
        Assert.IsFalse(File.Exists(active));

        if (readVersion)
            Assert.IsNull(ProjectMetadataParser.TryGetResolvedPackageVersion(project, "Newtonsoft.Json"));
        else
            Assert.IsNull(RestoreStalenessDetector.TryReadRestorePackagesPath(project));

        Assert.IsTrue(RestoreStalenessDetector.HasMissingAssets(project, NullLogger.Instance));
        Assert.IsTrue(new RestoreStalenessDetector().IsRestoreRequired(project, MsBuildMetadataHelper.DefaultGlobalProperties, NullLogger.Instance));
    }

    [TestMethod]
    [DataRow("<Project />", false)]
    [DataRow("<Project />", true)]
    [DataRow("<Project>", false)]
    [DataRow("<Project>", true)]
    [DataRow("<Project><PropertyGroup><ProjectAssetsFile>%00</ProjectAssetsFile></PropertyGroup></Project>", false)]
    [DataRow("<Project><PropertyGroup><ProjectAssetsFile>%00</ProjectAssetsFile></PropertyGroup></Project>", true)]
    public void ActiveAssets_UnknownLayoutDoesNotAuthorizeDefault(string contents, bool readVersion)
    {
        var project = WriteProject("Unknown.csproj", contents);
        WriteAssetsFile(Path.Combine(Path.GetDirectoryName(project)!, "obj", "project.assets.json"),
            "Newtonsoft.Json", "13.0.3", Path.Combine(_tempRoot, "stale-packages"));

        if (readVersion)
            Assert.IsNull(ProjectMetadataParser.TryGetResolvedPackageVersion(project, "Newtonsoft.Json"));
        else
            Assert.IsNull(RestoreStalenessDetector.TryReadRestorePackagesPath(project));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ActiveAssets_UnreadableProjectDoesNotAuthorizeDefault(bool readVersion)
    {
        var (project, active, _, packages) = CreateAssetsLayout("default");
        WriteAssetsFile(active, "Newtonsoft.Json", "13.0.3", packages);
        using var exclusive = new FileStream(project, FileMode.Open, FileAccess.Read, FileShare.None);

        if (readVersion)
            Assert.IsNull(ProjectMetadataParser.TryGetResolvedPackageVersion(project, "Newtonsoft.Json"));
        else
            Assert.IsNull(RestoreStalenessDetector.TryReadRestorePackagesPath(project));
    }

    [TestMethod]
    public void ActiveAssets_ConfiguredDefaultAndAlternateBothPresentUseSelectedContext()
    {
        var project = WriteProject("ConfiguredAuthority.csproj", PackageReferenceProject.Replace(
            "<TargetFramework>net10.0</TargetFramework>", """
            <TargetFramework>net10.0</TargetFramework>
            <ProjectAssetsFile Condition="'$(Configuration)' == 'Debug'">$(MSBuildProjectDirectory)/active/project.assets.json</ProjectAssetsFile>
            """, StringComparison.Ordinal));
        var directory = Path.GetDirectoryName(project)!;
        WriteAssetsFile(Path.Combine(directory, "obj", "project.assets.json"), "Newtonsoft.Json", "13.0.3", null);
        WriteAssetsFile(Path.Combine(directory, "active", "project.assets.json"), "Newtonsoft.Json", "12.0.1", null);
        var detector = new RestoreStalenessDetector();

        Assert.IsTrue(detector.IsRestoreRequired(project,
            ImmutableDictionary<string, string>.Empty.Add("Configuration", "Debug"), NullLogger.Instance));
        Assert.IsFalse(detector.IsRestoreRequired(project,
            ImmutableDictionary<string, string>.Empty.Add("Configuration", "Release"), NullLogger.Instance));
    }

    private (string Project, string Active, string Default, string Packages) CreateAssetsLayout(string layout)
    {
        var project = WriteProject("Authority.csproj", PackageReferenceProject);
        var directory = Path.GetDirectoryName(project)!;
        var property = layout switch
        {
            "base" => "<BaseIntermediateOutputPath>active/</BaseIntermediateOutputPath>",
            "extensions" => "<MSBuildProjectExtensionsPath>active/</MSBuildProjectExtensionsPath>",
            "explicit" => "<ProjectAssetsFile>active/reviewed.assets.json</ProjectAssetsFile>",
            "default" => string.Empty,
            _ => throw new AssertFailedException("Unknown fixture layout."),
        };
        File.WriteAllText(Path.Combine(directory, "Directory.Build.props"), $"<Project><PropertyGroup>{property}</PropertyGroup></Project>");
        var defaultPath = Path.Combine(directory, "obj", "project.assets.json");
        var active = layout == "default" ? defaultPath
            : Path.Combine(directory, "active", layout == "explicit" ? "reviewed.assets.json" : "project.assets.json");
        return (project, active, defaultPath, Path.Combine(directory, "active-packages"));
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
        WriteAssetsFile(Path.Combine(projectDirectory, "obj", "project.assets.json"), packageId, version, null);
    }

    private static void WriteAssetsFile(string assetsPath, string packageId, string version, string? packagesPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(assetsPath)!);
        var assets = new
        {
            project = new
            {
                restore = new { packagesPath },
                frameworks = new Dictionary<string, object>
                {
                    ["net10.0"] = new { dependencies = new Dictionary<string, object> { [packageId] = new { version } } },
                },
            },
            libraries = new Dictionary<string, object> { [$"{packageId}/{version}"] = new { type = "package" } },
        };
        File.WriteAllText(assetsPath, System.Text.Json.JsonSerializer.Serialize(assets));
    }
}
