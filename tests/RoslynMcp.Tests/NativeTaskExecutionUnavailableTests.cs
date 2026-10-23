using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeTaskExecutionUnavailableTests
{
    [TestMethod]
    [DataRow("semantic-input")]
    [DataRow("irrelevant-output")]
    [DataRow("authored-mutation")]
    public async Task UnverifiedCreatedTaskHistory_RetainsOnlyIndependentNativeAuthority(string shape)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(NativeTaskExecutionUnavailableTests), Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var project = Path.Combine(root, "Native.csproj");
            var source = Path.Combine(root, "Native.cs");
            var selected = shape == "authored-mutation" ? source : Path.Combine(root, "obj", "Selected.txt");
            var enrollment = shape == "semantic-input" ? "<ItemGroup><AdditionalFiles Include=\"obj/*.txt\" /></ItemGroup>" : "";
            await File.WriteAllTextAsync(project, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit></PropertyGroup>
                  <UsingTask TaskName="WriteLinesToFile" Architecture="x64"
                             AssemblyFile="$(MSBuildToolsPath)/Microsoft.Build.Tasks.Core.dll" />
                  <UsingTask TaskName="WriteLinesToFile" Architecture="x86" TaskFactory="RoslynCodeTaskFactory"
                             AssemblyFile="$(MSBuildToolsPath)/Microsoft.Build.Tasks.Core.dll">
                    <ParameterGroup><File ParameterType="System.String" Required="true" /></ParameterGroup>
                    <Task><Code Type="Fragment" Language="cs"><![CDATA[
                      System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(File));
                      System.IO.File.WriteAllText(File, "public class AuthoredFactoryExecuted { }");
                      return true;
                    ]]></Code></Task>
                  </UsingTask>
                  ENROLLMENT
                  <Target Name="CreateUnverifiedInput" BeforeTargets="CoreCompile">
                    <WriteLinesToFile MSBuildArchitecture="x64" File="$(MSBuildProjectDirectory)/obj/Builtin.txt" Lines="builtin executed" Overwrite="true" />
                    <WriteLinesToFile MSBuildArchitecture="x86" File="$(SelectedOutput)" />
                  </Target>
                </Project>
                """.Replace("ENROLLMENT", enrollment, StringComparison.Ordinal));
            await File.WriteAllTextAsync(source, "public class Native { public int Value => 7; }");
            var properties = OwnedNativeLoadContext.Properties(project, new Dictionary<string, string> { ["SelectedOutput"] = selected });
            var policy = new OwnedTestWorkspacePathPolicy();
            async Task LoadAsync()
            {
                var loaded = await new WorkspaceSessionLoader().CreateAndOpenAsync("unverified-child", project, properties,
                    policy, new WorkspaceDiagnosticsSink(64), NullLogger.Instance, SupplementalPackageReadAuthority.None, null, CancellationToken.None);
                Exception? loadFailure = null;
                try
                {
                    Assert.IsTrue(loaded.Workspace.CurrentSolution.Projects.Single().Documents.Any(document => document.Name == "Native.cs"));
                    var projection = await loaded.Context.NormalizeAsync("unverified-child", policy, CancellationToken.None);
                    if (shape == "semantic-input")
                    {
                        Assert.IsTrue(projection.Solution.Projects.Single().AdditionalDocuments.Any(document => document.Name == "Selected.txt"),
                            "The unverified writer output must actually become a native semantic input.");
                        Assert.AreEqual("TaskExecutionUnavailable", loaded.Context.Availability.ToString());
                        var refusal = Assert.ThrowsExactly<PublicInvalidOperationException>(() => NativeCompilerContextCertificate.Require(projection.Solution));
                        StringAssert.Contains(refusal.Message, "task execution");
                    }
                    else Assert.AreEqual(NativeCompilerContextAvailability.Certified, NativeCompilerContextCertificate.Require(projection.Solution).Availability);
                }
                catch (Exception failure) { loadFailure = failure; throw; }
                finally { OwnedResourceCleanup.Dispose("Unverified child native load cleanup failed.", loadFailure, loaded.Workspace, loaded.Lease); }
            }
            Exception? observedFailure = null;
            try
            {
                if (shape == "authored-mutation") await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(LoadAsync);
                else await LoadAsync();
            }
            catch (Exception failure) { observedFailure = failure; }
            Assert.AreEqual("public class AuthoredFactoryExecuted { }", await File.ReadAllTextAsync(selected),
                "The custom selected native body must execute before classifying authority.");
            Assert.AreEqual("builtin executed", (await File.ReadAllTextAsync(Path.Combine(root, "obj", "Builtin.txt"))).Trim());
            if (observedFailure is not null) ExceptionDispatchInfo.Capture(observedFailure).Throw();
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Unverified child fixture cleanup failed.", primary,
            CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }
}
