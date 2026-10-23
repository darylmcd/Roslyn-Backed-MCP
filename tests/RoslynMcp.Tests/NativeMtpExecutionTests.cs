using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>Real CLI/TRX proof; package restore requires network and is run separately from the CI-equivalent filter.</summary>
[TestClass]
[TestCategory("Network")]
[DoNotParallelize]
public sealed class NativeMtpExecutionTests : TestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [TestMethod]
    [DataRow("mstest", false)]
    [DataRow("xunit", false)]
    [DataRow("tunit", false)]
    [DataRow("mstest", true)]
    public async Task ActualNativeFramework_ParsesTrxAndAppliesFrameworkSpecificFilter(string framework, bool projectReference)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(NativeMtpExecutionTests), framework + "-" + Guid.NewGuid().ToString("N"));
        string? workspaceId = null;
        CancellationTokenSource? restoreDeadline = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(Path.Combine(root, "global.json"),
                """{ "test": { "runner": "Microsoft.Testing.Platform" } }""");
            var properties = framework == "mstest" ? "<EnableMSTestRunner>true</EnableMSTestRunner>"
                : framework == "xunit" ? "<UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>" : "";
            // Match the repository's MSTest version; xUnit's pinned MTP-v2 package supplies its native TRX reporter.
            var packages = framework == "mstest"
                ? """
                  <PackageReference Include="MSTest.TestAdapter" Version="4.5.1" />
                  <PackageReference Include="MSTest.TestFramework" Version="4.5.1" />
                  <PackageReference Include="Microsoft.Testing.Extensions.TrxReport" Version="2.5.1" />
                  """
                : framework == "xunit" ? """<PackageReference Include="xunit.v3.mtp-v2" Version="4.0.1" />"""
                : """<PackageReference Include="TUnit" Version="1.46.0" />""";
            await File.WriteAllTextAsync(Path.Combine(root, "Directory.Build.props"), "<Project />");
            var referenceItems = projectReference
                ? """<ProjectReference Include="Library/Library.csproj" /><Compile Remove="Library/**/*.cs" />""" : "";
            if (projectReference)
            {
                var library = Path.Combine(root, "Library");
                Directory.CreateDirectory(library);
                await File.WriteAllTextAsync(Path.Combine(library, "Library.csproj"),
                    """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit></PropertyGroup></Project>""");
                await File.WriteAllTextAsync(Path.Combine(library, "Library.cs"), "namespace NativeLibrary; public sealed class Value { }");
            }
            var projectPath = Path.Combine(root, "NativeTests.csproj");
            await File.WriteAllTextAsync(projectPath, $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <OutputType>Exe</OutputType>
                    <IsTestProject>true</IsTestProject>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <NuGetAudit>false</NuGetAudit>
                    {{properties}}
                  </PropertyGroup>
                  <ItemGroup>{{packages}}{{referenceItems}}</ItemGroup>
                </Project>
                """);
            var tests = framework == "mstest"
                ? """
                  using Microsoft.VisualStudio.TestTools.UnitTesting;
                  namespace NativeProof;
                  [TestClass] public sealed class Cases
                  {
                      [TestMethod] public void KeptCase() { }
                      [TestMethod] public void OtherCase() => Assert.Fail("Unselected sentinel must not execute in the filtered run.");
                  }
                  """
                : framework == "xunit" ? """
                  namespace NativeProof;
                  public sealed class Cases
                  {
                      [Xunit.Fact] public void KeptCase() { }
                      [Xunit.Fact] public void OtherCase() => Xunit.Assert.True(false, "Unselected sentinel must not execute in the filtered run.");
                  }
                  """
                : """
                  namespace NativeProof;
                  public sealed class Cases
                  {
                      [TUnit.Core.Test] public void KeptCase() { }
                      [TUnit.Core.Test] public void OtherCase() => throw new System.InvalidOperationException("Unselected sentinel must not execute in the filtered run.");
                  }
                  """;
            await File.WriteAllTextAsync(Path.Combine(root, "Cases.cs"), tests);
            var loadProperties = OwnedNativeLoadContext.Properties(projectPath, new Dictionary<string, string>());
            restoreDeadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var restore = await DotnetCommandRunner.RunAsync(root, projectPath,
                RoslynMcp.Roslyn.Services.MsBuildCommandProperties.Append(["restore", projectPath, "--nologo"], loadProperties),
                restoreDeadline.Token);
            Assert.IsTrue(restore.Succeeded, restore.StdOut + restore.StdErr);
            workspaceId = (await WorkspaceManager.LoadAsync(projectPath, loadProperties, CancellationToken.None)).WorkspaceId;
            _ = NativeCompilerContextCertificate.Require(WorkspaceManager.GetCurrentSolution(workspaceId));
            if (projectReference)
            {
                var owner = WorkspaceManager.GetCurrentSolution(workspaceId).Projects.Single(project => project.Name == "NativeTests");
                var compilation = await owner.GetCompilationAsync(CancellationToken.None);
                Assert.IsNotNull(compilation);
                Assert.IsTrue(compilation.References.OfType<Microsoft.CodeAnalysis.CompilationReference>()
                    .Any(reference => reference.Compilation.AssemblyName == "Library"),
                    "The native source dependency must be a CompilationReference, not a consumed output PE.");
            }
            var all = await TestRunnerService.RunTestsAsync(workspaceId, null, null, CancellationToken.None);
            Assert.AreEqual(2, all.Total, all.Execution.StdOut + all.Execution.StdErr);
            Assert.AreEqual(1, all.Passed);
            Assert.AreEqual(1, all.Failed);
            Assert.IsFalse(all.Execution.Succeeded);
            Assert.IsTrue(all.Failures.Any(failure => failure.DisplayName.Contains("OtherCase", StringComparison.Ordinal)));

            var filtered = await TestRunnerService.RunTestsAsync(workspaceId, null,
                "FullyQualifiedName=NativeProof.Cases.KeptCase", CancellationToken.None);
            Assert.IsTrue(filtered.Execution.Succeeded, filtered.Execution.StdOut + filtered.Execution.StdErr);
            Assert.AreEqual(1, filtered.Total);
            Assert.AreEqual(1, filtered.Passed);
            Assert.AreEqual(0, filtered.Failed);
            Assert.IsEmpty(filtered.Failures);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Native MTP fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => restoreDeadline?.Dispose()),
                CleanupFailureCollector.FromAction(() => { if (workspaceId is not null) WorkspaceManager.Close(workspaceId); }),
                CleanupFailureCollector.FromAction(() =>
                {
                    Assert.IsTrue(FileSystemPath.IsStrictDescendant(Path.GetFullPath(TestTempRoot.Current), Path.GetFullPath(root)));
                    TestFixtureFileSystem.DeleteDirectoryIfExists(root);
                }));
        }
    }
}
