using System.Collections.Immutable;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeOutputTaskFactoryTests
{
    [TestMethod]
    [DataRow("WriteLinesToFile", true)]
    [DataRow("WriteLinesToFile", false)]
    [DataRow("WriteCodeFragment", true)]
    [DataRow("WriteCodeFragment", false)]
    public async Task InlineFactoryWithTrustedFactoryDll_DoesNotImpersonateBuiltinWriterAuthority(string writerTask, bool taskSucceeds)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(NativeOutputTaskFactoryTests), Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var project = Path.Combine(root, "Native.csproj");
            var output = Path.Combine(root, "obj", "FactoryReceipt.txt");
            var projectText = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit></PropertyGroup>
                  <UsingTask TaskName="WriteLinesToFile" TaskFactory="RoslynCodeTaskFactory"
                             AssemblyFile="$(MSBuildToolsPath)/Microsoft.Build.Tasks.Core.dll" Override="true">
                    <ParameterGroup>
                      <File ParameterType="System.String" Required="true" />
                      <Lines ParameterType="System.String[]" />
                      <Overwrite ParameterType="System.Boolean" />
                    </ParameterGroup>
                    <Task>
                      <Code Type="Fragment" Language="cs"><![CDATA[
                        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(File));
                        System.IO.File.WriteAllText(File, "authored factory executed");
                        return true;
                      ]]></Code>
                    </Task>
                  </UsingTask>
                  <Target Name="OwnedFactoryProbe" BeforeTargets="CoreCompile">
                    <WriteLinesToFile File="$(MSBuildProjectDirectory)/obj/FactoryReceipt.txt" Lines="owned" Overwrite="true" />
                  </Target>
                </Project>
                """;
            if (writerTask == "WriteCodeFragment")
                projectText = projectText.Replace("WriteLinesToFile", "WriteCodeFragment", StringComparison.Ordinal)
                    .Replace("<File ParameterType=", "<OutputFile ParameterType=", StringComparison.Ordinal)
                    .Replace("<Lines ParameterType=\"System.String[]\" />", "<Language ParameterType=\"System.String\" />", StringComparison.Ordinal)
                    .Replace("<Overwrite ParameterType=\"System.Boolean\" />", "", StringComparison.Ordinal)
                    .Replace("GetDirectoryName(File)", "GetDirectoryName(OutputFile)", StringComparison.Ordinal)
                    .Replace("WriteAllText(File,", "WriteAllText(OutputFile,", StringComparison.Ordinal)
                    .Replace(" File=\"$(MSBuildProjectDirectory)/obj/FactoryReceipt.txt\" Lines=\"owned\" Overwrite=\"true\"", " OutputFile=\"$(MSBuildProjectDirectory)/obj/FactoryReceipt.txt\" Language=\"C#\"", StringComparison.Ordinal);
            if (!taskSucceeds) projectText = projectText.Replace("return true;", "return false;", StringComparison.Ordinal);
            await File.WriteAllTextAsync(project, projectText);
            await File.WriteAllTextAsync(Path.Combine(root, "Native.cs"), "public class Native { }");
            var globals = OwnedNativeLoadContext.Properties(project, new Dictionary<string, string>());
            var graph = EvaluatedBuildGraph.FromRoots([project], globals, ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None);
            var node = graph.Nodes.Single(owner => !owner.IsOuterBuild);
            var assemblies = await NativeOutputPreparation.CaptureWriterAssembliesAsync(node, CancellationToken.None);
            var (preparation, producerSucceeded) = await NativeCompilerTaskInputs.PrepareOutputsAsync(project, node.Globals, node.TargetFramework, node.TaskRegistrations.ToolsVersion,
                ["OwnedFactoryProbe"], CancellationToken.None);
            var receipts = preparation.Writers;
            Assert.AreEqual("authored factory executed", await File.ReadAllTextAsync(output), "The custom task factory must actually execute.");
            Assert.AreEqual(taskSucceeds, producerSucceeded, "The same actual native invocation must retain the factory task outcome.");
            var matching = receipts.Where(item => FileSystemPath.Comparer.Equals(Path.GetFullPath(item.Path, root), output)).ToArray();
            Assert.HasCount(taskSucceeds ? 1 : 0, matching, "Failed native tasks must not issue successful writer facts even after writing their output.");
            if (!taskSucceeds) return;
            var receipt = matching.Single();
            Assert.IsTrue(receipt.Task.EndsWith(writerTask, StringComparison.OrdinalIgnoreCase));
            Assert.AreEqual(writerTask == "WriteCodeFragment" ? "OutputFile" : "File", receipt.Parameter);
            Assert.AreNotEqual("Microsoft.Build.Tasks." + writerTask, receipt.Implementation,
                "Actual inline-factory execution must remain unqualified as the builtin task implementation.");
            Assert.IsTrue(FileSystemPath.Comparer.Equals(Path.Combine(NativeCompilerTaskInputs.RegisteredSdkRoot(), "Microsoft.Build.Tasks.Core.dll"), receipt.Assembly),
                "MSBuild reports the trusted FACTORY assembly even though the actual task body is authored inline code.");
            var admitted = await NativeOutputPreparation.AdmitWriterOutputsAsync("factory-probe", graph, node,
                ImmutableHashSet.Create<string>(FileSystemPath.Comparer), assemblies, [receipt], new OwnedTestWorkspacePathPolicy(), CancellationToken.None);
            Assert.IsEmpty(admitted, "A trusted factory DLL does not prove the builtin writer implementation.");
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Native task factory fixture cleanup failed.", primary,
            CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }
}
