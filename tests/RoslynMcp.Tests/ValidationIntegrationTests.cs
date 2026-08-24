using System.Reflection;
using Microsoft.CodeAnalysis.Diagnostics;
using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

[DoNotParallelize]
[TestClass]
public class ValidationIntegrationTests : SharedWorkspaceTestBase
{
    private static string WorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        WorkspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        DisposeServices();
    }

    [TestMethod]
    public void Workspace_Status_Identifies_Test_Project_Metadata()
    {
        var status = WorkspaceManager.GetStatus(WorkspaceId);
        var testProject = status.Projects.FirstOrDefault(project => project.Name == "SampleLib.Tests");

        Assert.IsNotNull(testProject, "SampleLib.Tests project not found.");
        Assert.IsTrue(testProject.IsTestProject, "SampleLib.Tests should be recognized as a test project.");
        // Microsoft.NET.Test.Sdk injects <OutputType>Exe</OutputType> for the test-host runner
        // (`dotnet test` invokes testhost.exe). The post-fix MSBuild evaluator surfaces this value;
        // the pre-fix XML-only path saw no element and defaulted to "Library". Test-vs-library
        // discrimination lives on IsTestProject (asserted above), not OutputType.
        Assert.AreEqual("Exe", testProject.OutputType);
        Assert.AreEqual("SampleLib.Tests", testProject.AssemblyName);
    }

    [TestMethod]
    public async Task Test_Discover_Returns_Test_Methods_For_Test_Project()
    {
        var discovery = await TestDiscoveryService.DiscoverTestsAsync(WorkspaceId, CancellationToken.None);

        Assert.IsTrue(discovery.TestProjects.Count > 0, "Expected at least one discovered test project.");
        var testProject = discovery.TestProjects.FirstOrDefault(project => project.ProjectName == "SampleLib.Tests");
        Assert.IsNotNull(testProject, "Expected SampleLib.Tests to be discovered.");
        Assert.IsTrue(testProject.Tests.Any(test => test.DisplayName == "CountAnimals_Returns_Total_Count"));
        Assert.IsTrue(testProject.Tests.Any(test => test.DisplayName == "GetAllAnimals_Returns_Dog_And_Cat"));
    }

    [TestMethod]
    public async Task Build_Workspace_Returns_Structured_Success()
    {
        var result = await BuildService.BuildWorkspaceAsync(WorkspaceId, CancellationToken.None);

        Assert.IsTrue(result.Execution.Succeeded, result.Execution.StdErr);
        Assert.AreEqual(0, result.Execution.ExitCode);
        Assert.AreEqual(0, result.ErrorCount, "Expected no build errors for the sample solution (warnings may appear in some SDK configurations).");
    }

    [TestMethod]
    public async Task ReleaseConfiguration_AnalyzerReference_Loads_From_Shadow_Copy()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Analyzer shadow-copy loading relies on Windows file-lock semantics.");
            return;
        }

        var repositorySolutionPath = Path.Combine(RepositoryRootPath, "RoslynMcp.slnx");

        // Match the testhost's actual build configuration. Otherwise MSBuildWorkspace
        // defaults to Debug evaluation even when CI built only Release; the analyzer
        // ProjectReference then resolves to a missing Debug TargetPath and Roslyn drops
        // the AnalyzerFileReference. Explicit globals also force a fresh evaluation rather
        // than reusing a session cached under different properties.
        var configuration = AppContext.BaseDirectory.Contains(
            Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase) ? "Release" : "Debug";
        var globalProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Configuration"] = configuration,
        };
        var loaded = await WorkspaceManager.LoadAsync(
            repositorySolutionPath,
            globalProperties,
            CancellationToken.None);

        try
        {
            var solution = WorkspaceManager.GetCurrentSolution(loaded.WorkspaceId);
            var hostProject = solution.Projects.First(project => project.Name == "RoslynMcp.Host.Stdio");
            var analyzerReference = hostProject.AnalyzerReferences
                .OfType<AnalyzerFileReference>()
                .FirstOrDefault(reference =>
                    reference.FullPath?.Contains("ServerSurfaceCatalogAnalyzer", StringComparison.OrdinalIgnoreCase) == true);

            Assert.IsNotNull(analyzerReference, "Expected Host.Stdio to carry the server-surface catalog analyzer reference.");

            var loader = GetAnalyzerAssemblyLoader(analyzerReference);
            Assert.IsNotNull(loader, "Expected the analyzer reference to have a loader.");
            StringAssert.Contains(loader.GetType().FullName ?? loader.GetType().Name, "ShadowCopyAnalyzerAssemblyLoader");

            var analyzers = analyzerReference.GetAnalyzers(hostProject.Language);
            Assert.IsTrue(
                analyzers.Any(analyzer => analyzer.GetType().Name == "ServerSurfaceCatalogAnalyzer"),
                "Expected the shadow-copy loader to preserve analyzer discovery.");

            var build = await BuildService.BuildProjectAsync(
                loaded.WorkspaceId,
                "RoslynMcp.Host.Stdio",
                CancellationToken.None);
            Assert.IsTrue(build.Execution.Succeeded, build.Execution.StdErr);
            Assert.IsFalse(
                (build.Execution.StdOut + build.Execution.StdErr).Contains("MSB3027", StringComparison.OrdinalIgnoreCase),
                "The Release-configured build must not hit the analyzer DLL file-lock retry path.");

            using var stream = new FileStream(
                analyzerReference.FullPath!,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);
            Assert.IsTrue(stream.CanWrite, "The original analyzer DLL must remain writable after analyzer discovery.");
        }
        finally
        {
            WorkspaceManager.Close(loaded.WorkspaceId);
        }
    }

    [TestMethod]
    public async Task Test_Run_Returns_Structured_Success()
    {
        var result = await TestRunnerService.RunTestsAsync(
            WorkspaceId,
            projectName: "SampleLib.Tests",
            filter: null,
            CancellationToken.None);

        Assert.IsTrue(result.Execution.Succeeded, result.Execution.StdErr);
        // Total includes AnimalServiceTests (2), AnimalFormatterTests (2), and the
        // test-related-files direct-reference ranking fixture (2).
        Assert.AreEqual(6, result.Total);
        Assert.AreEqual(6, result.Passed);
        Assert.AreEqual(0, result.Failed);
    }

    [TestMethod]
    public async Task Build_Workspace_Returns_Parsed_Diagnostics_For_Broken_Solution()
    {
        var buildFailureWorkspace = await WorkspaceManager.LoadAsync(BuildFailureSolutionPath, CancellationToken.None);

        var result = await BuildService.BuildWorkspaceAsync(buildFailureWorkspace.WorkspaceId, CancellationToken.None);

        Assert.IsFalse(result.Execution.Succeeded, "Build should fail for the broken fixture.");
        Assert.IsTrue(result.Diagnostics.Any(diagnostic => diagnostic.Id == "CS0103"));
        var missingSymbol = result.Diagnostics.First(diagnostic => diagnostic.Id == "CS0103");
        Assert.IsNotNull(missingSymbol.EndLine, "Build diagnostics should recover endLine from Roslyn diagnostics when CLI output omits it.");
        Assert.IsNotNull(missingSymbol.EndColumn, "Build diagnostics should recover endColumn from Roslyn diagnostics when CLI output omits it.");
        Assert.IsTrue(missingSymbol.EndColumn > missingSymbol.StartColumn,
            $"Expected the recovered end column to span the missing symbol. Diagnostic: {missingSymbol}");
    }

    [TestMethod]
    public async Task Test_Run_Parses_Failing_Test_Results_From_Isolated_Copy()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;
        var testFilePath = Path.Combine(copiedRoot, "SampleLib.Tests", "AnimalServiceTests.cs");

        try
        {
            var originalContents = await File.ReadAllTextAsync(testFilePath, CancellationToken.None);
            var failingContents = originalContents.Replace("Assert.AreEqual(2, count);", "Assert.AreEqual(3, count);");
            await File.WriteAllTextAsync(testFilePath, failingContents, CancellationToken.None);

            var copiedWorkspace = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
            var result = await TestRunnerService.RunTestsAsync(
                copiedWorkspace.WorkspaceId,
                projectName: "SampleLib.Tests",
                filter: null,
                CancellationToken.None);

            Assert.IsFalse(result.Execution.Succeeded, "Test run should report failure for the modified fixture.");
            Assert.AreEqual(1, result.Failed);
            Assert.IsTrue(
                result.Failures.Any(IsCountAnimalsFailure),
                $"Expected failing test CountAnimals_Returns_Total_Count in failures. Got: {string.Join("; ", result.Failures.Select(f => $"{f.DisplayName} | {f.FullyQualifiedName}"))}");

            static bool IsCountAnimalsFailure(TestFailureDto failure) =>
                string.Equals(failure.DisplayName, "CountAnimals_Returns_Total_Count", StringComparison.Ordinal)
                || (failure.FullyQualifiedName?.Contains("CountAnimals_Returns_Total_Count", StringComparison.Ordinal) ?? false);
        }
        finally
        {
            DeleteDirectoryIfExists(copiedRoot);
        }
    }

    private static object? GetAnalyzerAssemblyLoader(AnalyzerFileReference reference) =>
        typeof(AnalyzerFileReference)
            .GetField("_assemblyLoader", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(reference);
}
