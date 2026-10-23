using System.Runtime.CompilerServices;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeOutputProducerObservationTests
{
    [TestMethod]
    [DataRow("WriteLinesToFile", false)]
    [DataRow("WriteLinesToFile", true)]
    [DataRow("WriteCodeFragment", false)]
    [DataRow("WriteCodeFragment", true)]
    public async Task OwnedPreparationProducer_ReportsExactSuccessfulWriterTaskParameterAndOrigin(string writerTask, bool lowerCaseTask)
    {
        RoslynMcp.Roslyn.MsBuildInitializer.EnsureInitialized();
        await ProbeCoreAsync(writerTask, lowerCaseTask);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task ProbeCoreAsync(string writerTask, bool lowerCaseTask)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(NativeOutputProducerObservationTests), Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var project = Path.Combine(root, "Native.csproj");
            var output = Path.Combine(root, "OwnedGenerated.txt");
            var taskName = lowerCaseTask ? writerTask.ToLowerInvariant() : writerTask;
            var writer = writerTask == "WriteCodeFragment"
                ? new System.Xml.Linq.XElement(taskName,
                    new System.Xml.Linq.XAttribute("Language", "C#"),
                    new System.Xml.Linq.XAttribute("OutputFile", "$(MSBuildProjectDirectory)/OwnedGenerated.txt"),
                    new System.Xml.Linq.XAttribute("AssemblyAttributes", "@(OwnedAttributes)"))
                : new System.Xml.Linq.XElement(taskName,
                    new System.Xml.Linq.XAttribute("File", "$(MSBuildProjectDirectory)/OwnedGenerated.txt"),
                    new System.Xml.Linq.XAttribute("Lines", "owned"),
                    new System.Xml.Linq.XAttribute("Overwrite", "true"));
            var definition = new System.Xml.Linq.XElement("Project", new System.Xml.Linq.XAttribute("Sdk", "Microsoft.NET.Sdk"),
                new System.Xml.Linq.XElement("PropertyGroup", new System.Xml.Linq.XElement("TargetFramework", "net10.0"), new System.Xml.Linq.XElement("NuGetAudit", "false")),
                new System.Xml.Linq.XElement("ItemGroup", new System.Xml.Linq.XElement("OwnedAttributes",
                    new System.Xml.Linq.XAttribute("Include", "System.Reflection.AssemblyTitleAttribute"), new System.Xml.Linq.XElement("_Parameter1", "owned"))),
                new System.Xml.Linq.XElement("Target", new System.Xml.Linq.XAttribute("Name", "WriteOwnedProbe"),
                    new System.Xml.Linq.XAttribute("BeforeTargets", "CoreCompile"), writer));
            await File.WriteAllTextAsync(project, definition.ToString());
            await File.WriteAllTextAsync(Path.Combine(root, "Native.cs"), "public class Native { }");
            var globals = OwnedNativeLoadContext.Properties(project, new Dictionary<string, string>());
            var toolsVersion = OwnedNativeCompilerInputContext.CaptureToolsVersion(project, globals);
            var (preparation, _) = await NativeCompilerTaskInputs.PrepareOutputsAsync(project, globals, "net10.0", toolsVersion,
                ["Compile", "CoreCompile"], CancellationToken.None);
            var observed = preparation.Writers;
            Assert.IsTrue(File.Exists(output), "The synthetic native writer must actually execute.");
            var writers = observed.Where(candidate => FileSystemPath.Comparer.Equals(Path.GetFullPath(candidate.Path, root), output)).ToArray();
            Assert.HasCount(1, writers, "An actual successful output parameter must identify the owned native writer.");
            StringAssert.Contains(await File.ReadAllTextAsync(output), "owned", "The actual writer must materialize the authored attribute/value.");
            var receipt = writers[0];
            Assert.IsTrue(receipt.Task.EndsWith(writerTask, StringComparison.OrdinalIgnoreCase), "The native task event must identify the selected task spelling independently of canonicalization.");
            Assert.AreEqual("Microsoft.Build.Tasks." + writerTask, receipt.Implementation);
            var expectedContext = NativeTaskExecutionIdentity.EscapedContextIdentity(NativeCompilerTaskInputs.EffectiveGlobals(globals, "net10.0"), toolsVersion, CancellationToken.None);
            Assert.AreEqual(expectedContext, receipt.ContextIdentity);
            Assert.AreEqual("WriteOwnedProbe", receipt.Target);
            Assert.AreEqual(writerTask == "WriteCodeFragment" ? "OutputFile" : "File", receipt.Parameter);
            Assert.IsTrue(FileSystemPath.Comparer.Equals(project, receipt.Project));
            Assert.IsTrue(FileSystemPath.Comparer.Equals(project, receipt.TargetFile));
            Assert.IsTrue(FileSystemPath.Comparer.Equals(Path.Combine(NativeCompilerTaskInputs.RegisteredSdkRoot(), "Microsoft.Build.Tasks.Core.dll"), receipt.Assembly),
                "Task identity must be its actual SDK implementation, not its name.");
        }
        catch (Exception failure)
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Native output logger probe cleanup failed.", failure,
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
            throw;
        }
        TestFixtureFileSystem.DeleteDirectoryIfExists(root);
    }

}
