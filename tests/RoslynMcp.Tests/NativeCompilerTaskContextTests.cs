using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeCompilerTaskContextTests
{
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task FinalCompilerReceipt_RequiresActualGlobalsNotOnlyProjectPath(bool nested, bool qualifiedTask)
    {
        RoslynMcp.Roslyn.MsBuildInitializer.EnsureInitialized();
        var root = Path.Combine(TestTempRoot.Current, nameof(NativeCompilerTaskContextTests), Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, "obj"));
            var project = Path.Combine(root, "Native.csproj");
            var marker = Path.Combine(root, "obj", "CompilerContext.txt");
            await File.WriteAllTextAsync(Path.Combine(root, "Probe.cs"), "public class Probe { }");
            XElement Compiler() => new(qualifiedTask ? "Microsoft.CodeAnalysis.BuildTasks.Csc" : "Csc", new XAttribute("Sources", "$(MSBuildProjectDirectory)/Probe.cs"),
                new XAttribute("References", typeof(object).Assembly.Location), new XAttribute("OutputAssembly", "$(MSBuildProjectDirectory)/obj/Probe.dll"),
                new XAttribute("TargetType", "Library"), new XAttribute("NoConfig", "true"),
                new XAttribute("SkipCompilerExecution", "true"), new XAttribute("ProvideCommandLineArgs", "true"),
                new XElement("Output", new XAttribute("TaskParameter", "CommandLineArgs"), new XAttribute("ItemName", "ActualCompilerArgs")));
            XElement Inner(string name) => new("Target", new XAttribute("Name", name),
                Compiler(), new XElement("PropertyGroup", new XElement("CompilerContextReceipt", "$(Mode)|@(ActualCompilerArgs->Count())")),
                new XElement("WriteLinesToFile", new XAttribute("File", marker), new XAttribute("Lines", "$(CompilerContextReceipt)"), new XAttribute("Overwrite", "true")));
            var definition = new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0"), new XElement("NuGetAudit", "false")),
                new XElement("Target", new XAttribute("Name", "OuterProbe"),
                    new XElement("CallTarget", new XAttribute("Condition", "'$(UseNested)' != 'true'"), new XAttribute("Targets", "DirectProbe")),
                    new XElement("MSBuild", new XAttribute("Condition", "'$(UseNested)' == 'true'"), new XAttribute("Projects", "$(MSBuildProjectFullPath)"),
                        new XAttribute("Targets", "InnerProbe"), new XAttribute("Properties", "Mode=nested"))),
                Inner("DirectProbe"), Inner("InnerProbe"));
            await File.WriteAllTextAsync(project, definition.ToString());
            var properties = OwnedNativeLoadContext.Properties(project, new Dictionary<string, string>
            { ["Mode"] = "outer", ["UseNested"] = nested ? "true" : "false" });
            {
                // The rejecting capture intentionally cancels at TaskStarted.
                // Establish the full native body premise in an independent owned run.
                var execution = await new DotnetCommandRunner().RunAsync(root, project,
                    MsBuildCommandProperties.Append(["msbuild", project, "-nologo", "-m:1", "-nr:false", "-target:OuterProbe"],
                        NativeCompilerTaskInputs.EffectiveGlobals(properties, "net10.0")), CancellationToken.None);
                var errorCodes = Regex.Matches(execution.StdOut + execution.StdErr, @"\b(?:MSB|CS)\d{4}\b")
                    .Select(match => match.Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
                Assert.IsTrue(execution.Succeeded, "The independent native compiler-context control must complete; codes=" + string.Join(",", errorCodes));
                AssertCompilerCompleted();
            }
            var toolsVersion = OwnedNativeCompilerInputContext.CaptureToolsVersion(project, properties);
            async Task Read()
            {
                var (receipt, _) = await NativeCompilerTaskInputs.ReadRawAsync(project, properties, "net10.0", toolsVersion, ["OuterProbe"],
                    NativeCompilerTaskInputs.CapturePurpose.FinalCompiler, CancellationToken.None);
                Assert.HasCount(1, receipt.References);
                Assert.IsTrue(receipt.Arguments.Length > 0);
                Assert.AreEqual("NativeCompilerRawReceipt", receipt.ToString());
            }
            Exception? observedFailure = null;
            try
            {
                if (nested)
                {
                    var refusal = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(Read);
                    StringAssert.Contains(refusal.Message, "trusted SDK task and owner",
                        "The captured negative must reach the actual TaskStarted identity boundary.");
                }
                else await Read();
            }
            catch (Exception failure) { observedFailure = failure; }
            if (!nested) AssertCompilerCompleted();
            if (observedFailure is not null) ExceptionDispatchInfo.Capture(observedFailure).Throw();

            void AssertCompilerCompleted()
            {
                var actual = File.ReadAllText(marker).Trim().Split('|');
                Assert.HasCount(2, actual);
                Assert.AreEqual(nested ? "nested" : "outer", actual[0], "The native AFTER-Csc marker must retain the actual producer configuration.");
                Assert.IsTrue(int.TryParse(actual[1], out var argumentCount) && argumentCount > 0,
                    "The actual Csc task must return nonempty CommandLineArgs before the marker executes.");
            }
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Native compiler-context fixture cleanup failed.", primary,
            CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }
}
