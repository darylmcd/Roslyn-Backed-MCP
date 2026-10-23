using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeTaskRegistrationsTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OriginalRegistry_PreservesActiveExpandedBindingsWithoutRepeatingFunctions(bool active)
    {
        RoslynMcp.Roslyn.MsBuildInitializer.EnsureInitialized();
        await InspectAsync(active);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task InspectAsync(bool active)
    {
        Console.WriteLine("NATIVE-REGISTRY-CAPABILITY sdk=" + new DirectoryInfo(NativeCompilerTaskInputs.RegisteredSdkRoot()).Name
            + ";buildFileVersion=" + System.Diagnostics.FileVersionInfo.GetVersionInfo(typeof(ProjectInstance).Assembly.Location).FileVersion);
        var root = Path.Combine(TestTempRoot.Current, nameof(NativeTaskRegistrationsTests), Guid.NewGuid().ToString("N"));
        ProjectCollection? collection = null;
        ProjectCollection? nextCollection = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var project = Path.Combine(root, "Registry.proj");
            var factoryInput = Path.Combine(root, "factory.txt");
            await File.WriteAllTextAsync(factoryInput, "RoslynCodeTaskFactory");
            var definition = new XElement("Project",
                new XElement("PropertyGroup", new XElement("ProbeName", "ProbeTask"),
                    new XElement("ProbeFactory", "$([System.IO.File]::ReadAllText('" + ProjectCollection.Escape(factoryInput) + "'))")),
                new XElement("UsingTask", new XAttribute("TaskName", "$(ProbeName)"), new XAttribute("TaskFactory", "$(ProbeFactory)"),
                    new XAttribute("AssemblyFile", Path.Combine(NativeCompilerTaskInputs.RegisteredSdkRoot(), "Microsoft.Build.Tasks.Core.dll")),
                    new XAttribute("Condition", "'$(Enabled)' == 'true'"), new XAttribute("Override", "true"),
                    new XElement("ParameterGroup", new XElement("File", new XAttribute("ParameterType", "System.String"), new XAttribute("Required", "true"))),
                    new XElement("Task", new XAttribute("Evaluate", "false"), new XElement("Code", new XAttribute("Type", "Fragment"),
                        new XAttribute("Language", "cs"), "return true;"))));
            await File.WriteAllTextAsync(project, definition.ToString());
            collection = new ProjectCollection();
            var original = new ProjectInstance(project, new Dictionary<string, string> { ["Enabled"] = active ? "true" : "false" }, null, collection);
            var first = NativeTaskRegistrations.Capture(original, CancellationToken.None);
            Assert.HasCount(active ? 1 : 0, first.Registrations);
            if (active)
            {
                Assert.AreEqual("ProbeTask", first.Registrations.Single().Name);
                Assert.AreEqual("RoslynCodeTaskFactory", first.Registrations.Single().Factory);
                CollectionAssert.Contains(first.Registrations.Single().OverrideNames.ToArray(), "ProbeTask");
            }
            await File.WriteAllTextAsync(factoryInput, "ChangedFactory");
            var second = NativeTaskRegistrations.Capture(original, CancellationToken.None);
            Assert.AreEqual(first, second, "Read-only capture must not evaluate the property function or task factory again.");
            Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
            Assert.AreEqual("NativeTaskRegistrations", second.ToString());
            if (active)
            {
                nextCollection = new ProjectCollection();
                var next = new ProjectInstance(project, new Dictionary<string, string> { ["Enabled"] = "true" }, null, nextCollection);
                var changed = NativeTaskRegistrations.Capture(next, CancellationToken.None);
                Assert.AreEqual("ChangedFactory", changed.Registrations.Single().Factory);
                Assert.AreNotEqual(first, changed);
            }
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Native registry fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => nextCollection?.Dispose()),
                CleanupFailureCollector.FromAction(() => collection?.Dispose()),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }
}
