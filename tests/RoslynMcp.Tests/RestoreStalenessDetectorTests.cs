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

        Assert.IsFalse(detector.IsRestoreRequired(projectFilePath, NullLogger.Instance));
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

        Assert.IsTrue(detector.IsRestoreRequired(projectFilePath, NullLogger.Instance));
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

        Assert.IsTrue(detector.IsRestoreRequired(projectFilePath, NullLogger.Instance));
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

        Assert.IsFalse(detector.IsRestoreRequired(projectFilePath, NullLogger.Instance));
    }

    [TestMethod]
    public void IsRestoreRequired_NonexistentProjectFile_ReturnsFalse()
    {
        var detector = new RestoreStalenessDetector();

        Assert.IsFalse(detector.IsRestoreRequired(
            Path.Combine(_tempRoot, "DoesNotExist.csproj"),
            NullLogger.Instance));
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

        Assert.IsTrue(detector.DetectRestoreRequired(projects, NullLogger.Instance));
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

        Assert.IsFalse(detector.DetectRestoreRequired(projects, NullLogger.Instance));
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
