using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// project-graph-output-type-misreports-sdk-defaulted-exe regression guard. SDK-style
/// projects that rely on <c>Microsoft.NET.Sdk.Web</c> or <c>Microsoft.NET.Sdk.Worker</c>
/// inherit <c>OutputType=Exe</c> from the SDK's targets and typically omit the
/// <c>&lt;OutputType&gt;</c> element from the .csproj XML. The pre-fix XML-only path read
/// the absent element and defaulted to <c>"Library"</c>; the post-fix path runs MSBuild
/// property evaluation via <see cref="Microsoft.Build.Evaluation.ProjectCollection"/> so the
/// SDK-injected value surfaces correctly.
/// </summary>
[TestClass]
public sealed class ProjectOutputTypeTests : TestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        // Ensures MSBuild is registered for the ProjectCollection evaluations below.
        InitializeServices();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        DisposeServices();
    }

    [TestMethod]
    public void GetEvaluatedOutputType_SdkWebProject_WithoutExplicitOutputType_ReturnsExe()
    {
        // Pre-fix: GetOutputType(XDocument?) saw no <OutputType> element and returned "Library".
        // Post-fix: GetEvaluatedOutputType opens the project under MSBuild and reads the
        // SDK-defaulted property, returning "Exe".
        using var fixture = CreateProjectFixture(
            "WebApp.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var result = ProjectMetadataParser.GetEvaluatedOutputType(fixture.ProjectPath, NullLogger.Instance);

        Assert.AreEqual("Exe", result,
            "Microsoft.NET.Sdk.Web projects inherit OutputType=Exe from the SDK targets; the evaluator must surface it even when the XML omits the element.");
    }

    [TestMethod]
    public void GetEvaluatedOutputType_SdkWorkerProject_WithoutExplicitOutputType_ReturnsExe()
    {
        // Worker SDK is the other commonly-affected case — generic-host background services
        // (ASP.NET Core hosted services, IHostedService apps) use Microsoft.NET.Sdk.Worker
        // and rely on the same SDK-defaulted OutputType=Exe.
        using var fixture = CreateProjectFixture(
            "WorkerApp.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk.Worker">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var result = ProjectMetadataParser.GetEvaluatedOutputType(fixture.ProjectPath, NullLogger.Instance);

        Assert.AreEqual("Exe", result,
            "Microsoft.NET.Sdk.Worker projects inherit OutputType=Exe from the SDK targets; the evaluator must surface it.");
    }

    [TestMethod]
    public void GetEvaluatedOutputType_PlainSdkLibrary_WithoutExplicitOutputType_ReturnsLibrary()
    {
        // Regression guard: a plain Microsoft.NET.Sdk library project (no <OutputType>) must
        // still return "Library" via MSBuild evaluation — the SDK defaults the property to
        // "Library" for this case. This proves the evaluator did not just hard-code "Exe".
        using var fixture = CreateProjectFixture(
            "PlainLib.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var result = ProjectMetadataParser.GetEvaluatedOutputType(fixture.ProjectPath, NullLogger.Instance);

        Assert.AreEqual("Library", result,
            "Microsoft.NET.Sdk library projects default OutputType=Library; the evaluator must report that value, not silently misreport.");
    }

    [TestMethod]
    public void GetEvaluatedOutputType_ExplicitExe_PreservesValue()
    {
        // An explicit <OutputType>Exe</OutputType> on a plain Microsoft.NET.Sdk console
        // project must still produce "Exe" — the evaluator and the XML-only path agree
        // on this case.
        using var fixture = CreateProjectFixture(
            "ConsoleApp.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var result = ProjectMetadataParser.GetEvaluatedOutputType(fixture.ProjectPath, NullLogger.Instance);

        Assert.AreEqual("Exe", result,
            "Explicit <OutputType>Exe</OutputType> must round-trip through the evaluator unchanged.");
    }

    [TestMethod]
    public void GetEvaluatedOutputType_MissingFile_ReturnsNull()
    {
        // The evaluator must not throw when handed a non-existent path; the caller falls
        // back to the XML-only path which itself returns "Library".
        var missingPath = Path.Combine(Path.GetTempPath(), $"does_not_exist_{Guid.NewGuid():N}.csproj");
        var result = ProjectMetadataParser.GetEvaluatedOutputType(missingPath, NullLogger.Instance);

        Assert.IsNull(result, "A non-existent project path must return null, not throw or return a default value.");
    }

    private static ProjectFixture CreateProjectFixture(string fileName, string content)
    {
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "RoslynMcpTests",
            "ProjectOutputTypeTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var projectPath = Path.Combine(tempRoot, fileName);
        File.WriteAllText(projectPath, content);
        return new ProjectFixture(tempRoot, projectPath);
    }

    private sealed class ProjectFixture(string rootPath, string projectPath) : IDisposable
    {
        public string ProjectPath { get; } = projectPath;

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(rootPath))
                {
                    Directory.Delete(rootPath, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup. Locked files just leak a temp directory that the
                // OS will reclaim on the next sweep.
            }
        }
    }
}
