using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeCompilerIdentityUnavailableTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExecutedCustomCompilerIdentity_IsExplicitlyUnavailableWithoutAbortingNativeBody(bool preparation)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(NativeCompilerIdentityUnavailableTests), Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            RoslynMcp.Roslyn.MsBuildInitializer.EnsureInitialized();
            var project = Path.Combine(root, "Native.csproj");
            var body = Path.Combine(root, "Body.txt");
            var after = Path.Combine(root, "After.txt");
            await File.WriteAllTextAsync(project, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit></PropertyGroup>
                  <UsingTask TaskName="Csc" TaskFactory="RoslynCodeTaskFactory" Override="true"
                             AssemblyFile="$(MSBuildToolsPath)/Microsoft.Build.Tasks.Core.dll">
                    <ParameterGroup>
                      <Marker ParameterType="System.String" Required="true" />
                      <CommandLineArgs ParameterType="Microsoft.Build.Framework.ITaskItem[]" Output="true" />
                    </ParameterGroup>
                    <Task><Code Type="Fragment" Language="cs"><![CDATA[
                      System.IO.File.WriteAllText(Marker, "custom compiler body executed");
                      CommandLineArgs = new Microsoft.Build.Framework.ITaskItem[] { new Microsoft.Build.Utilities.TaskItem("controlled-unverified-input") };
                      return true;
                    ]]></Code></Task>
                  </UsingTask>
                  <Target Name="CompilerProbe">
                    <Csc Marker="$(MSBuildProjectDirectory)/Body.txt"><Output TaskParameter="CommandLineArgs" ItemName="ActualArgs" /></Csc>
                    <PropertyGroup><ReturnedCount>@(ActualArgs->Count())</ReturnedCount></PropertyGroup>
                    <WriteLinesToFile File="$(MSBuildProjectDirectory)/After.txt" Lines="$(ReturnedCount)" Overwrite="true" />
                  </Target>
                </Project>
                """);
            var globals = OwnedNativeLoadContext.Properties(project, new Dictionary<string, string>());
            var independent = await new DotnetCommandRunner().RunAsync(root, project,
                MsBuildCommandProperties.Append(["msbuild", project, "-nologo", "-m:1", "-nr:false", "-target:CompilerProbe"],
                    NativeCompilerTaskInputs.EffectiveGlobals(globals, "net10.0")), CancellationToken.None);
            Assert.IsTrue(independent.Succeeded, "The independent custom native compiler task must complete.");
            Assert.AreEqual("custom compiler body executed", await File.ReadAllTextAsync(body));
            Assert.AreEqual("1", (await File.ReadAllTextAsync(after)).Trim());
            File.Delete(body);
            File.Delete(after);
            var purpose = preparation ? NativeCompilerTaskInputs.CapturePurpose.PreparationOutputs : NativeCompilerTaskInputs.CapturePurpose.FinalCompiler;
            var toolsVersion = OwnedNativeCompilerInputContext.CaptureToolsVersion(project, globals);
            if (preparation)
            {
                var (capture, _) = await NativeCompilerTaskInputs.ReadRawAsync(project, globals, "net10.0", toolsVersion, ["CompilerProbe"], purpose, CancellationToken.None);
                Assert.IsTrue(capture.CompilerIdentityUnavailable, "Preparation must preserve an explicit compiler implementation capability fact.");
            }
            else
            {
                var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => NativeCompilerTaskInputs.ReadRawAsync(
                    project, globals, "net10.0", toolsVersion, ["CompilerProbe"], purpose, CancellationToken.None));
                Assert.AreEqual("NativeTaskExecutionUnavailableException", failure.GetType().Name,
                    "Unavailable implementation proof must remain distinct from unexpected producer or protocol failure.");
            }
            Assert.AreEqual("custom compiler body executed", await File.ReadAllTextAsync(body), "Captured execution must complete before returning capability uncertainty.");
            Assert.AreEqual("1", (await File.ReadAllTextAsync(after)).Trim());
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Unverified compiler fixture cleanup failed.", primary,
            CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }
}
