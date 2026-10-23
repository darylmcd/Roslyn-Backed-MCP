using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeOutputTaskSelectionTests
{
    [TestMethod]
    [DataRow("WriteLinesToFile", false, false)]
    [DataRow("WriteLinesToFile", true, false)]
    [DataRow("WriteCodeFragment", false, false)]
    [DataRow("WriteCodeFragment", true, false)]
    [DataRow("WriteLinesToFile", false, true)]
    [DataRow("WriteLinesToFile", true, true)]
    [DataRow("WriteCodeFragment", false, true)]
    [DataRow("WriteCodeFragment", true, true)]
    public async Task SameNameDifferentHostSelection_CannotBorrowPreviouslyCachedBuiltin(string writerTask, bool useCustom, bool failAfterWriters)
    {
        RoslynMcp.Roslyn.MsBuildInitializer.EnsureInitialized();
        await ProbeSelectionAsync(writerTask, useCustom, failAfterWriters);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task ProbeSelectionAsync(string writerTask, bool useCustom, bool failAfterWriters)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(NativeOutputTaskSelectionTests), Guid.NewGuid().ToString("N"));
        Microsoft.Build.Evaluation.ProjectCollection? nativeEvaluation = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var project = Path.Combine(root, "Native.csproj");
            var first = Path.Combine(root, "obj", "Builtin.txt");
            var second = Path.Combine(root, "obj", "Selected.txt");
            var projectText = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit></PropertyGroup>
                  <UsingTask TaskName="WriteLinesToFile" Architecture="x64"
                             AssemblyFile="$(MSBuildToolsPath)/Microsoft.Build.Tasks.Core.dll" />
                  <UsingTask TaskName="WriteLinesToFile" Architecture="x86" TaskFactory="RoslynCodeTaskFactory"
                             AssemblyFile="$(MSBuildToolsPath)/Microsoft.Build.Tasks.Core.dll">
                    <ParameterGroup>
                      <File ParameterType="System.String" Required="true" />
                      <Lines ParameterType="System.String[]" />
                      <Overwrite ParameterType="System.Boolean" />
                    </ParameterGroup>
                    <Task><Code Type="Fragment" Language="cs"><![CDATA[
                      System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(File));
                      System.IO.File.WriteAllText(File, "authored factory executed");
                      return true;
                    ]]></Code></Task>
                  </UsingTask>
                  <Target Name="SelectionProbe">
                    <WriteLinesToFile MSBuildArchitecture="x64" File="$(MSBuildProjectDirectory)/obj/Builtin.txt" Lines="builtin executed" Overwrite="true" />
                    <WriteLinesToFile MSBuildArchitecture="$(SelectedArchitecture)" File="$(MSBuildProjectDirectory)/obj/Selected.txt" Lines="builtin executed" Overwrite="true" />
                    <Error Condition="'$(FailAfterWriters)' == 'true'" Text="owned post-writer failure" />
                  </Target>
                </Project>
                """;
            if (writerTask == "WriteCodeFragment")
                projectText = projectText.Replace("WriteLinesToFile", "WriteCodeFragment", StringComparison.Ordinal)
                    .Replace("<File ParameterType=", "<OutputFile ParameterType=", StringComparison.Ordinal)
                    .Replace("<Lines ParameterType=\"System.String[]\" />", "<AssemblyAttributes ParameterType=\"Microsoft.Build.Framework.ITaskItem[]\" />", StringComparison.Ordinal)
                    .Replace("<Overwrite ParameterType=\"System.Boolean\" />", "<Language ParameterType=\"System.String\" />", StringComparison.Ordinal)
                    .Replace("GetDirectoryName(File)", "GetDirectoryName(OutputFile)", StringComparison.Ordinal)
                    .Replace("WriteAllText(File,", "WriteAllText(OutputFile,", StringComparison.Ordinal)
                    .Replace(" File=", " OutputFile=", StringComparison.Ordinal)
                    .Replace(" Lines=\"builtin executed\" Overwrite=\"true\"", " Language=\"C#\" AssemblyAttributes=\"@(OwnedAttributes)\"", StringComparison.Ordinal)
                    .Replace("<Target Name=\"SelectionProbe\">", "<ItemGroup><OwnedAttributes Include=\"System.Reflection.AssemblyTitleAttribute\"><_Parameter1>builtin executed</_Parameter1></OwnedAttributes></ItemGroup><Target Name=\"SelectionProbe\">", StringComparison.Ordinal);
            await File.WriteAllTextAsync(project, projectText);
            const string contextProperty = "WriterContextSentinel";
            const string contextValue = "writer-context%3B;\"quoted\"";
            var escapedContextValue = Microsoft.Build.Evaluation.ProjectCollection.Escape(contextValue);
            var globals = OwnedNativeLoadContext.Properties(project, new Dictionary<string, string>
            { ["SelectedArchitecture"] = useCustom ? "x86" : "x64", [contextProperty] = escapedContextValue,
                ["FailAfterWriters"] = failAfterWriters ? "true" : "false" });
            var graph = EvaluatedBuildGraph.FromRoots([project], globals, ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None);
            var node = graph.Nodes.Single(owner => !owner.IsOuterBuild);
            Assert.AreEqual(escapedContextValue, node.Globals[contextProperty], "The graph must retain native API-escaped global values.");
            nativeEvaluation = new Microsoft.Build.Evaluation.ProjectCollection();
            var evaluated = nativeEvaluation.LoadProject(project, node.Globals.ToDictionary(pair => pair.Key, pair => pair.Value),
                node.TaskRegistrations.ToolsVersion);
            Assert.AreEqual(contextValue, evaluated.GetPropertyValue(contextProperty),
                "The native evaluated property must independently preserve the literal percent, semicolon and quote payload.");
            var assemblies = await NativeOutputPreparation.CaptureWriterAssembliesAsync(node, CancellationToken.None);
            var (preparation, producerSucceeded) = await NativeCompilerTaskInputs.PrepareOutputsAsync(project, node.Globals, node.TargetFramework, node.TaskRegistrations.ToolsVersion,
                ["SelectionProbe"], CancellationToken.None);
            var receipts = preparation.Writers;
            var firstBody = (await File.ReadAllTextAsync(first)).Trim();
            var secondBody = (await File.ReadAllTextAsync(second)).Trim();
            if (writerTask == "WriteCodeFragment")
            {
                StringAssert.Contains(firstBody, "\"builtin executed\"", "The first native builtin task must actually emit its assembly attribute before the selected task runs.");
                if (useCustom) Assert.AreEqual("authored factory executed", secondBody);
                else StringAssert.Contains(secondBody, "\"builtin executed\"",
                    "The selected builtin must actually emit its assembly attribute.");
            }
            else
            {
                Assert.AreEqual("builtin executed", firstBody);
                Assert.AreEqual(useCustom ? "authored factory executed" : "builtin executed", secondBody,
                    "Both native bodies must execute under disjoint task identities before evaluating receipt authority.");
            }
            var builtin = receipts.Single(receipt => FileSystemPath.Comparer.Equals(Path.GetFullPath(receipt.Path, root), first));
            Assert.AreEqual("Microsoft.Build.Tasks." + writerTask, builtin.Implementation,
                "The first native task must independently establish the actual builtin cached task identity.");
            Assert.IsTrue(builtin.Task.EndsWith(writerTask, StringComparison.OrdinalIgnoreCase));
            Assert.AreEqual(writerTask == "WriteCodeFragment" ? "OutputFile" : "File", builtin.Parameter);
            var expectedContext = NativeTaskExecutionIdentity.EscapedContextIdentity(NativeCompilerTaskInputs.EffectiveGlobals(node.Globals, node.TargetFramework),
                node.TaskRegistrations.ToolsVersion, CancellationToken.None);
            Assert.AreEqual(expectedContext, builtin.ContextIdentity);
            Assert.IsTrue(FileSystemPath.Comparer.Equals(Path.Combine(NativeCompilerTaskInputs.RegisteredSdkRoot(), "Microsoft.Build.Tasks.Core.dll"), builtin.Assembly));
            var selected = receipts.Single(receipt => FileSystemPath.Comparer.Equals(Path.GetFullPath(receipt.Path, root), second));
            Assert.IsTrue(selected.Task.EndsWith(writerTask, StringComparison.OrdinalIgnoreCase));
            Assert.AreEqual(writerTask == "WriteCodeFragment" ? "OutputFile" : "File", selected.Parameter);
            Assert.AreEqual(expectedContext, selected.ContextIdentity);
            if (!useCustom) Assert.AreEqual("Microsoft.Build.Tasks." + writerTask, selected.Implementation,
                "The native direct builtin selection must have verified implementation metadata.");
            else Assert.AreNotEqual("Microsoft.Build.Tasks." + writerTask, selected.Implementation,
                "The actual selected authored factory must not borrow the previously cached builtin implementation.");
            Assert.IsTrue(FileSystemPath.Comparer.Equals(Path.Combine(NativeCompilerTaskInputs.RegisteredSdkRoot(), "Microsoft.Build.Tasks.Core.dll"), selected.Assembly));
            var admitted = await NativeOutputPreparation.AdmitWriterOutputsAsync("selected-factory", graph, node,
                ImmutableHashSet.Create<string>(FileSystemPath.Comparer), assemblies, [selected], new OwnedTestWorkspacePathPolicy(), CancellationToken.None);
            Assert.HasCount(useCustom ? 0 : 1, admitted);
            Assert.AreEqual(!failAfterWriters, producerSucceeded,
                "The same actual native invocation must report whole-producer success independently of successful writer facts.");
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Native selected-factory fixture cleanup failed.", primary,
            CleanupFailureCollector.FromAction(() => nativeEvaluation?.Dispose()),
            CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }
}
