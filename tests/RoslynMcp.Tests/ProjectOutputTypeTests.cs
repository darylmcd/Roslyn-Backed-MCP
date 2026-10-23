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

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Status_UsesOneSelectedImportedContextForFrameworkTestKindAndOutput(bool selected)
    {
        using var fixture = CreateProjectFixture("Context.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFrameworks>net9.0;net10.0</TargetFrameworks><NuGetAudit>false</NuGetAudit></PropertyGroup>
              <Import Project="Status.props" />
            </Project>
            """);
        await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(fixture.ProjectPath)!, "Status.props"), """
            <Project>
              <PropertyGroup><OutputType>Library</OutputType></PropertyGroup>
              <PropertyGroup Condition="'$(StatusFlavor)' == 'selected' And '$(TargetFramework)' == 'net10.0'">
                <OutputType>Exe</OutputType><IsTestProject>true</IsTestProject>
              </PropertyGroup>
            </Project>
            """);
        var properties = RoslynMcp.Tests.Helpers.OwnedNativeLoadContext.Properties(fixture.ProjectPath,
            new Dictionary<string, string> { ["TargetFramework"] = "net10.0", ["StatusFlavor"] = selected ? "selected" : "other" });
        var loaded = await WorkspaceManager.LoadAsync(fixture.ProjectPath, properties, CancellationToken.None);
        try
        {
            Assert.IsTrue(loaded.IsLoaded);
            var project = loaded.Projects.Single();
            CollectionAssert.AreEqual(new[] { "net10.0" }, project.TargetFrameworks.ToArray());
            Assert.AreEqual(selected, project.IsTestProject);
            Assert.AreEqual(selected ? "Exe" : "Library", project.OutputType);
            var status = await WorkspaceManager.GetStatusAsync(loaded.WorkspaceId, CancellationToken.None);
            Assert.AreEqual(project.OutputType, status.Projects.Single().OutputType);
            Assert.AreEqual(project.IsTestProject, status.Projects.Single().IsTestProject);
        }
        finally { WorkspaceManager.Close(loaded.WorkspaceId); }
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

        var result = ProjectMetadataParser.GetEvaluatedOutputType(fixture.ProjectPath, System.Collections.Immutable.ImmutableDictionary<string, string>.Empty, NullLogger.Instance);

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

        var result = ProjectMetadataParser.GetEvaluatedOutputType(fixture.ProjectPath, System.Collections.Immutable.ImmutableDictionary<string, string>.Empty, NullLogger.Instance);

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

        var result = ProjectMetadataParser.GetEvaluatedOutputType(fixture.ProjectPath, System.Collections.Immutable.ImmutableDictionary<string, string>.Empty, NullLogger.Instance);

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

        var result = ProjectMetadataParser.GetEvaluatedOutputType(fixture.ProjectPath, System.Collections.Immutable.ImmutableDictionary<string, string>.Empty, NullLogger.Instance);

        Assert.AreEqual("Exe", result,
            "Explicit <OutputType>Exe</OutputType> must round-trip through the evaluator unchanged.");
    }

    [TestMethod]
    public void GetEvaluatedOutputType_MissingFile_ReturnsNull()
    {
        // The evaluator must not throw when handed a non-existent path; the caller falls
        // back to the XML-only path which itself returns "Library".
        var missingPath = Path.Combine(Path.GetTempPath(), $"does_not_exist_{Guid.NewGuid():N}.csproj");
        var result = ProjectMetadataParser.GetEvaluatedOutputType(missingPath, System.Collections.Immutable.ImmutableDictionary<string, string>.Empty, NullLogger.Instance);

        Assert.IsNull(result, "A non-existent project path must return null, not throw or return a default value.");
    }

    [TestMethod]
    public async Task BothSurfaces_SdkWebProject_AgreeOnOutputType_Exe()
    {
        // get-msbuild-properties-vs-workspace-reload-outputtype-mismatch: cross-surface
        // regression guard. The production fix for SDK.Web/SDK.Worker OutputType reporting
        // landed in project-graph-output-type-misreports-sdk-defaulted-exe — both surfaces
        // now flow through MSBuild evaluation (ProjectCollection.LoadProject), so they
        // must report identical values. This test asserts that contract for a
        // Microsoft.NET.Sdk.Web project that omits <OutputType> from its XML:
        //
        //   Surface 1 — workspace_reload  (WorkspaceManager.GetStatusAsync → ProjectStatusDto.OutputType)
        //              reads OutputType from the same evaluated graph as the other status fields.
        //   Surface 2 — get_msbuild_properties (MsBuildEvaluationService.GetEvaluatedPropertiesAsync)
        //              uses ProjectCollection.LoadProject directly.
        //
        // Both must return "Exe" for SDK.Web projects. Divergence (e.g. "Library" vs "Exe")
        // would mean one surface regressed back to the pre-fix XML-only path.
        using var fixture = CreateProjectFixture(
            "SdkWebCrossSurface.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var loadStatus = await WorkspaceManager.LoadAsync(fixture.ProjectPath, RoslynMcp.Tests.Helpers.OwnedNativeLoadContext.Properties(fixture.ProjectPath, new Dictionary<string, string>()), CancellationToken.None);
        try
        {
            // Surface 1: workspace_reload result. The single project in the loaded csproj
            // surfaces via WorkspaceStatusDto.Projects with its evaluated OutputType.
            var status = await WorkspaceManager.GetStatusAsync(loadStatus.WorkspaceId, CancellationToken.None);
            Assert.AreEqual(1, status.Projects.Count, "Loading a single .csproj must surface exactly one project.");
            var workspaceReloadOutputType = status.Projects[0].OutputType;
            var projectName = status.Projects[0].Name;

            // Surface 2: get_msbuild_properties result. Filtering by includedNames=["OutputType"]
            // narrows the dump to just the property we care about, mirroring the typical caller
            // pattern (small allowlist, not a full ~600-property dump).
            var propertiesDump = await MsBuildEvaluationService.GetEvaluatedPropertiesAsync(
                loadStatus.WorkspaceId,
                projectName,
                propertyNameFilter: null,
                includedNames: new[] { "OutputType" },
                CancellationToken.None);

            Assert.IsTrue(
                propertiesDump.Properties.TryGetValue("OutputType", out var msbuildOutputType),
                "get_msbuild_properties must include OutputType when the caller's allowlist requests it.");

            // Cross-surface assertion: both surfaces must agree, and the agreed-upon value
            // must be "Exe" (the SDK-defaulted value for Microsoft.NET.Sdk.Web).
            Assert.AreEqual("Exe", workspaceReloadOutputType,
                "workspace_reload surface must report OutputType=Exe for Microsoft.NET.Sdk.Web projects (SDK-defaulted value).");
            Assert.AreEqual("Exe", msbuildOutputType,
                "get_msbuild_properties surface must report OutputType=Exe for Microsoft.NET.Sdk.Web projects (SDK-defaulted value).");
            Assert.AreEqual(workspaceReloadOutputType, msbuildOutputType,
                $"Cross-surface mismatch: workspace_reload reported '{workspaceReloadOutputType}' but get_msbuild_properties reported '{msbuildOutputType}'. " +
                "Both surfaces draw from MSBuild evaluation and must agree — divergence indicates a regression in evaluated status projection or MsBuildEvaluationService.");
        }
        finally
        {
            WorkspaceManager.Close(loadStatus.WorkspaceId);
        }
    }

    private static ProjectFixture CreateProjectFixture(string fileName, string content)
    {
        var tempRoot = Path.Combine(
            TestTempRoot.Current,
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

        public void Dispose() => TestFixtureFileSystem.DeleteDirectoryIfExists(rootPath);
    }
}
