using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>Real native-host registration controls; restored package presence alone is not registration proof.</summary>
[TestClass]
[TestCategory("Network")]
[DoNotParallelize]
public sealed class NativeMtpReporterRegistrationTests : TestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [TestMethod]
    [DataRow("ordinary", true)]
    [DataRow("excluded-assets", false)]
    [DataRow("removed-hook", false)]
    [DataRow("manual-hook", true)]
    public async Task ActualMSTestReporterRegistration_PreservesNativeSuccessOrFailure(string configuration, bool succeeds)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(NativeMtpReporterRegistrationTests), Guid.NewGuid().ToString("N"));
        string? workspaceId = null;
        CancellationTokenSource? restoreDeadline = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(Path.Combine(root, "global.json"),
                """{ "test": { "runner": "Microsoft.Testing.Platform" } }""");
            var exclusion = configuration == "excluded-assets" ? "ExcludeAssets=\"all\"" : "";
            // Remove only the reporter hook. MSTest's framework registration remains intact.
            // The custom hook explicitly uses the reporter's public registration API.
            var hooks = configuration is "removed-hook" or "manual-hook" ? """
                <Target Name="ControlReporterRegistration" BeforeTargets="_GenerateSelfRegisteredExtensionsFileInputCache">
                  <ItemGroup>
                    <TestingPlatformBuilderHook Remove="2006B3F7-93D2-4D9C-9C69-F41A1F21C9C7" />
                  </ItemGroup>
                </Target>
                """ : "";
            if (configuration == "manual-hook") hooks = """
                <Target Name="ControlReporterRegistration" BeforeTargets="_GenerateSelfRegisteredExtensionsFileInputCache">
                  <ItemGroup>
                    <TestingPlatformBuilderHook Remove="2006B3F7-93D2-4D9C-9C69-F41A1F21C9C7" />
                    <TestingPlatformBuilderHook Include="FixtureManualReporter">
                      <DisplayName>Fixture manual reporter registration</DisplayName>
                      <TypeFullName>NativeReporterProof.ManualReporter</TypeFullName>
                    </TestingPlatformBuilderHook>
                  </ItemGroup>
                </Target>
                """;
            var path = Path.Combine(root, "ReporterTests.csproj");
            await File.WriteAllTextAsync(path, $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <OutputType>Exe</OutputType>
                    <IsTestProject>true</IsTestProject>
                    <EnableMSTestRunner>true</EnableMSTestRunner>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="MSTest.TestAdapter" Version="4.5.1" />
                    <PackageReference Include="MSTest.TestFramework" Version="4.5.1" />
                    <PackageReference Include="Microsoft.Testing.Extensions.TrxReport" Version="2.5.1" {{exclusion}} />
                  </ItemGroup>
                  {{hooks}}
                </Project>
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "Cases.cs"), """
                using Microsoft.VisualStudio.TestTools.UnitTesting;
                namespace NativeReporterProof;
                [TestClass] public sealed class Cases
                {
                    [TestMethod] public void Passes() { }
                }
                """);
            if (configuration == "manual-hook") await File.WriteAllTextAsync(Path.Combine(root, "ManualReporter.cs"), """
                using Microsoft.Testing.Extensions;
                using Microsoft.Testing.Platform.Builder;
                namespace NativeReporterProof;
                public static class ManualReporter
                {
                    public static void AddExtensions(ITestApplicationBuilder builder, string[] args) => builder.AddTrxReportProvider();
                }
                """);
            var loadProperties = OwnedNativeLoadContext.Properties(path, new Dictionary<string, string>());
            restoreDeadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var restore = await DotnetCommandRunner.RunAsync(root, path,
                MsBuildCommandProperties.Append(["restore", path, "--nologo"], loadProperties), restoreDeadline.Token);
            Assert.IsTrue(restore.Succeeded, restore.StdOut + restore.StdErr);
            workspaceId = (await WorkspaceManager.LoadAsync(path, loadProperties, CancellationToken.None)).WorkspaceId;
            _ = NativeCompilerContextCertificate.Require(WorkspaceManager.GetCurrentSolution(workspaceId));
            var registeredSource = Directory.GetFiles(Path.Combine(root, "obj"), "SelfRegisteredExtensions.cs", SearchOption.AllDirectories);
            Assert.HasCount(1, registeredSource, "The actual native MTP generation target must supply its registration source.");
            var registrations = await File.ReadAllTextAsync(registeredSource.Single());
            Assert.AreEqual(configuration == "ordinary", registrations.Contains(
                "Microsoft.Testing.Extensions.TrxReport.TestingPlatformBuilderHook", StringComparison.Ordinal), registrations);
            Assert.AreEqual(configuration == "manual-hook", registrations.Contains(
                "NativeReporterProof.ManualReporter", StringComparison.Ordinal), registrations);
            var result = await TestRunnerService.RunTestsAsync(workspaceId, null, null, CancellationToken.None);
            var output = result.Execution.StdOut + result.Execution.StdErr;
            var builtProject = WorkspaceManager.GetCurrentSolution(workspaceId).Projects.Single();
            Assert.IsNotNull(builtProject.OutputFilePath);
            Assert.IsTrue(File.Exists(builtProject.OutputFilePath), output);
            CollectionAssert.Contains(result.Execution.Arguments.ToArray(), "--report-trx");
            CollectionAssert.Contains(result.Execution.Arguments.ToArray(), "--no-restore");
            Assert.AreEqual(succeeds, result.Execution.Succeeded, output);
            if (succeeds)
            {
                Assert.AreEqual(1, result.Total, output);
                Assert.AreEqual(1, result.Passed);
                Assert.AreEqual(0, result.Failed);
                Assert.IsNull(result.FailureEnvelope);
            }
            else
            {
                Assert.AreEqual(5, result.Execution.ExitCode, output);
                Assert.AreEqual(0, result.Total, output);
                Assert.IsNotNull(result.FailureEnvelope);
                Assert.AreEqual("Unknown", result.FailureEnvelope.ErrorKind);
                StringAssert.Contains(output, "Handshake failures");
                Assert.IsFalse(string.IsNullOrWhiteSpace(result.FailureEnvelope.Summary));
                // SDK orchestration reports its failed handshake, hiding the child's
                // invalid-option detail. Invoke the SAME already-built host directly to
                // establish that missing reporter registration caused this failure.
                var project = WorkspaceManager.GetCurrentSolution(workspaceId).Projects.Single();
                Assert.IsNotNull(project.OutputFilePath);
                Assert.IsTrue(File.Exists(project.OutputFilePath));
                var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
                Exception? directFailure = null;
                try
                {
                    var direct = await new DotnetCommandRunner().RunAsync(root, path,
                        ["exec", project.OutputFilePath, "--report-trx", "--results-directory", Path.Combine(root, "direct-results")], timeout.Token);
                    Assert.IsFalse(direct.Succeeded, direct.StdOut + direct.StdErr);
                    Assert.AreEqual(5, direct.ExitCode, direct.StdOut + direct.StdErr);
                    StringAssert.Contains(direct.StdOut + direct.StdErr, "report-trx");
                }
                catch (Exception failure) { directFailure = failure; throw; }
                finally
                {
                    await CleanupFailureCollector.RunAfterFailureAsync("Direct native reporter fixture cleanup failed.", directFailure,
                        CleanupFailureCollector.FromAction(timeout.Dispose));
                }
            }
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Native reporter registration fixture cleanup failed.", primary,
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
