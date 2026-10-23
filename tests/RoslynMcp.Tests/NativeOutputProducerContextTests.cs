using System.Collections.Immutable;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeOutputProducerContextTests
{
    [TestMethod]
    [DataRow("WriteLinesToFile", false)]
    [DataRow("WriteLinesToFile", true)]
    [DataRow("WriteCodeFragment", false)]
    [DataRow("WriteCodeFragment", true)]
    public async Task SameProjectDifferentGlobals_CannotBorrowOriginalWriterRole(string writerTask, bool nested)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(NativeOutputProducerContextTests), Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var project = Path.Combine(root, "Native.csproj");
            var output = Path.Combine(root, "obj", "ContextReceipt.txt");
            var projectText = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit></PropertyGroup>
                  <Target Name="OuterProbe">
                    <WriteLinesToFile Condition="'$(UseNested)' != 'true'" File="$(MSBuildProjectDirectory)/obj/ContextReceipt.txt" Lines="$(Mode)" Overwrite="true" />
                    <MSBuild Condition="'$(UseNested)' == 'true'" Projects="$(MSBuildProjectFullPath)" Targets="InnerProbe" Properties="Mode=nested" />
                  </Target>
                  <Target Name="InnerProbe">
                    <WriteLinesToFile File="$(MSBuildProjectDirectory)/obj/ContextReceipt.txt" Lines="$(Mode)" Overwrite="true" />
                  </Target>
                </Project>
                """;
            if (writerTask == "WriteCodeFragment")
            {
                projectText = projectText.Replace("WriteLinesToFile", "WriteCodeFragment", StringComparison.Ordinal)
                    .Replace("File=", "OutputFile=", StringComparison.Ordinal)
                    .Replace("Lines=\"$(Mode)\" Overwrite=\"true\"", "Language=\"C#\" AssemblyAttributes=\"@(OwnedAttributes)\"", StringComparison.Ordinal)
                    .Replace("<Target Name=\"OuterProbe\">", "<ItemGroup><OwnedAttributes Include=\"System.Reflection.AssemblyTitleAttribute\"><_Parameter1>$(Mode)</_Parameter1></OwnedAttributes></ItemGroup><Target Name=\"OuterProbe\">", StringComparison.Ordinal);
            }
            await File.WriteAllTextAsync(project, projectText);
            var globals = OwnedNativeLoadContext.Properties(project, new Dictionary<string, string>
            { ["Mode"] = "outer", ["UseNested"] = nested ? "true" : "false" });
            var graph = EvaluatedBuildGraph.FromRoots([project], globals, ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None);
            var node = graph.Nodes.Single(owner => !owner.IsOuterBuild);
            Assert.AreEqual("outer", node.Globals["Mode"]);
            var assemblies = await NativeOutputPreparation.CaptureWriterAssembliesAsync(node, CancellationToken.None);
            var (preparation, _) = await NativeCompilerTaskInputs.PrepareOutputsAsync(project, node.Globals, node.TargetFramework, node.TaskRegistrations.ToolsVersion,
                ["OuterProbe"], CancellationToken.None);
            var receipts = preparation.Writers;
            var actualOutput = (await File.ReadAllTextAsync(output)).Trim();
            if (writerTask == "WriteCodeFragment")
                StringAssert.Contains(actualOutput, "\"" + (nested ? "nested" : "outer") + "\"", "The actual task must emit the attribute under the asserted native globals.");
            else Assert.AreEqual(nested ? "nested" : "outer", actualOutput, "The actual task must execute under the asserted native global context.");
            var receipt = receipts.Single(item => FileSystemPath.Comparer.Equals(Path.GetFullPath(item.Path, root), output));
            Assert.IsTrue(receipt.Task.EndsWith(writerTask, StringComparison.OrdinalIgnoreCase));
            Assert.AreEqual(writerTask == "WriteCodeFragment" ? "OutputFile" : "File", receipt.Parameter);
            Assert.AreEqual("Microsoft.Build.Tasks." + writerTask, receipt.Implementation, "Actual builtin task implementation metadata must be available.");
            var expectedContext = NativeTaskExecutionIdentity.EscapedContextIdentity(NativeCompilerTaskInputs.EffectiveGlobals(node.Globals, node.TargetFramework), node.TaskRegistrations.ToolsVersion, CancellationToken.None);
            if (nested) Assert.AreNotEqual(expectedContext, receipt.ContextIdentity);
            else Assert.AreEqual(expectedContext, receipt.ContextIdentity, "The original execution must retain its exact effective globals.");
            var admitted = await NativeOutputPreparation.AdmitWriterOutputsAsync("context-probe", graph, node,
                ImmutableHashSet.Create<string>(FileSystemPath.Comparer), assemblies, [receipt], new OwnedTestWorkspacePathPolicy(), CancellationToken.None);
            Assert.HasCount(nested ? 0 : 1, admitted, "A same-path task from a different native evaluation cannot borrow the original context's output role.");
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Native producer context fixture cleanup failed.", primary,
            CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }
}
