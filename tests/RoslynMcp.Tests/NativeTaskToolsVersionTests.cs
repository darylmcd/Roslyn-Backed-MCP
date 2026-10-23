using System.Text.RegularExpressions;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeTaskToolsVersionTests
{
    [TestMethod]
    [DataRow("default")]
    [DataRow("legacy-project")]
    [DataRow("legacy-nested")]
    public async Task NativeToolsetNormalization_PreservesActualEffectiveWriterContext(string shape)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(NativeTaskToolsVersionTests), Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var project = Path.Combine(root, "Native.csproj");
            var output = Path.Combine(root, "Toolset.txt");
            var declaration = shape == "legacy-project" ? "ToolsVersion=\"15.0\"" : "";
            var dispatch = shape == "legacy-nested"
                ? "<MSBuild Projects=\"$(MSBuildProjectFullPath)\" Targets=\"Inner\" ToolsVersion=\"15.0\" />"
                : "<CallTarget Targets=\"Inner\" />";
            await File.WriteAllTextAsync(project, """
                <Project Sdk="Microsoft.NET.Sdk" TOOLSET>
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit></PropertyGroup>
                  <Target Name="Outer">DISPATCH</Target>
                  <Target Name="Inner"><WriteLinesToFile File="$(MSBuildProjectDirectory)/Toolset.txt" Lines="$(MSBuildToolsVersion)" Overwrite="true" /></Target>
                </Project>
                """.Replace("TOOLSET", declaration, StringComparison.Ordinal).Replace("DISPATCH", dispatch, StringComparison.Ordinal));
            var globals = OwnedNativeLoadContext.Properties(project, new Dictionary<string, string>());
            var original = MsBuildMetadataHelper.EvaluateProperties(project, globals, "MSBuildToolsVersion").Single();
            var execution = await new DotnetCommandRunner().RunAsync(root, project,
                MsBuildCommandProperties.Append(["msbuild", project, "-nologo", "-m:1", "-nr:false", "-target:Outer"],
                    NativeCompilerTaskInputs.EffectiveGlobals(globals, "net10.0")), CancellationToken.None);
            var codes = Regex.Matches(execution.StdOut + execution.StdErr, @"\b(?:MSB|CS)\d{4}\b")
                .Select(match => match.Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (shape == "legacy-nested")
            {
                Assert.IsFalse(execution.Succeeded, "An unsupported explicit native toolset request must not be certified as an owner mismatch.");
                CollectionAssert.Contains(codes, "MSB4132");
                Assert.IsFalse(File.Exists(output));
                return;
            }
            Assert.IsTrue(execution.Succeeded, "Independent native toolset control must complete; codes=" + string.Join(",", codes));
            Assert.IsTrue(File.Exists(output), "Independent native writer body must execute before context capture.");
            Assert.AreEqual(original, (await File.ReadAllTextAsync(output)).Trim(),
                "The independent effective native toolset must match the original evaluation.");
            File.Delete(output);
            var toolsVersion = OwnedNativeCompilerInputContext.CaptureToolsVersion(project, globals);
            Assert.AreEqual(original, toolsVersion);
            var (preparation, _) = await NativeCompilerTaskInputs.PrepareOutputsAsync(project, globals, "net10.0", toolsVersion, ["Outer"], CancellationToken.None);
            var receipts = preparation.Writers;
            Assert.HasCount(1, receipts);
            Assert.AreEqual("Microsoft.Build.Tasks.WriteLinesToFile", receipts.Single().Implementation);
            Assert.AreEqual(original, (await File.ReadAllTextAsync(output)).Trim(),
                "Legacy request strings may normalize; the actual effective native toolset is the premise.");
            Console.WriteLine("NATIVE-TOOLSET-NORMALIZATION shape=" + shape + ";effectiveMatched=True");
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Native toolset fixture cleanup failed.", primary,
            CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }
}
