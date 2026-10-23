using System.Xml.Linq;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class RestorePathSlotNativeTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize] public static void Initialize(TestContext _) => InitializeServices();
    [TestMethod]
    [DataRow(false, "none")]
    [DataRow(true, "none")]
    [DataRow(false, "ContentPreprocessorOutputDirectory")]
    [DataRow(true, "ContentPreprocessorOutputDirectory")]
    [DataRow(false, "BaseOutputPath")]
    [DataRow(true, "BaseOutputPath")]
    [DataRow(false, "PublishDir")]
    [DataRow(true, "PublishDir")]
    [DataRow(false, "CustomOutputBase")]
    [DataRow(true, "CustomOutputBase")]
    public async Task NativeFork_ConsumedAbsolutePathSlotsArePrivateAndSourceAssetsStayExact(bool global, string externalOutput)
    {
        var scope = Path.Combine(TestTempRoot.Current, nameof(RestorePathSlotNativeTests), Guid.NewGuid().ToString("N"));
        var root = Path.Combine(scope, "source");
        var projectPath = Path.Combine(root, "Native.csproj");
        var externalOutputs = externalOutput != "none";
        var coordinateRoot = externalOutputs ? scope : root;
        var slots = new Dictionary<string, string>
        {
            ["ContentPreprocessorOutputDirectory"] = Path.Combine(root, "obj", "preprocessed"),
            ["ProjectAssetsFile"] = Path.Combine(root, "obj", "project.assets.json"),
            ["TargetPath"] = Path.Combine(root, "bin", "Native.dll")
        };
        if (externalOutputs)
        {
            slots[externalOutput] = Path.Combine(scope, "external-output");
        }

        string? sourceId = null;
        WorkspaceForkApplyResultDto? fork = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(Path.Combine(root, "global.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(root, "Directory.Build.props"), "<Project />");
            var properties = new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0"), new XElement("NuGetAudit", "false"));
            if (!global)
                foreach (var(name, value)in slots)
                    properties.Add(new XElement(name, value));
            if (externalOutput == "CustomOutputBase")
                properties.Add(new XElement("OutputPath", "$(CustomOutputBase)/bin/"));
            await File.WriteAllTextAsync(projectPath, new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"), properties).ToString());
            await File.WriteAllTextAsync(Path.Combine(root, "Entry.cs"), "public class OriginalEntry { }");
            var globals = OwnedNativeLoadContext.Properties(projectPath, global ? slots : new Dictionary<string, string>());
            var restore = await DotnetCommandRunner.RunAsync(root, projectPath, MsBuildCommandProperties.Append(["restore", projectPath, "--nologo"], globals), CancellationToken.None);
            Assert.IsTrue(restore.Succeeded, "The controlled source restore must succeed.");
            var sourceAssets = await File.ReadAllBytesAsync(slots["ProjectAssetsFile"]);
            var sourceProject = await File.ReadAllBytesAsync(projectPath);
            var sourceEntry = await File.ReadAllBytesAsync(Path.Combine(root, "Entry.cs"));
            sourceId = (await WorkspaceManager.LoadAsync(projectPath, globals, CancellationToken.None)).WorkspaceId;
            var sourceOutput = externalOutput == "CustomOutputBase" ? MsBuildMetadataHelper.EvaluateProperties(projectPath, globals, "OutputPath").Single() : null;
            // The SDK can create an empty BaseOutputPath while loading/restoring. Establish
            // the missing-directory case after that real producer, without deleting artifacts.
            if (externalOutputs && Directory.Exists(slots[externalOutput]))
            {
                Assert.AreEqual(Path.GetFullPath(Path.Combine(scope, "external-output")), Path.GetFullPath(slots[externalOutput]));
                Assert.AreEqual(0, Directory.EnumerateFiles(slots[externalOutput], "*", SearchOption.AllDirectories).Count(), "The source producer may create only empty controlled output directories.");
                Directory.Delete(slots[externalOutput], recursive: true);
            }

            var original = WorkspaceManager.GetCurrentSolution(sourceId);
            var document = original.Projects.Single().Documents.Single(item => item.Name == "Entry.cs");
            var modified = original.WithDocumentText(document.Id, SourceText.From("public class ReviewedEntry { }"));
            var token = PreviewStore.Store(sourceId, original, modified, WorkspaceManager.GetCurrentVersion(sourceId), "native path-slot transport", [], PreviewKind.Unspecified);
            var service = new WorkspaceForkApplyService(PathPolicy, WorkspaceManager, WorkspaceExecutionGate, PreviewStore, new WorkspaceValidationService(CompileCheckService, DiagnosticService, TestDiscoveryService, TestRunnerService, WorkspaceManager, ChangeTracker), TestRunnerService, new DotnetCommandRunner(), NullLogger<WorkspaceForkApplyService>.Instance, PhysicalGraphNamespace.ForkParent, TimeProvider.System);
            if (externalOutputs)
                Assert.IsFalse(Directory.Exists(slots[externalOutput]), "This case requires a missing source output directory before isolation.");
            fork = await service.ApplyAsync(sourceId, token, "keep", false, null, "native-slots", CancellationToken.None);
            Assert.IsTrue(fork.Success);
            CollectionAssert.AreEqual(sourceAssets, await File.ReadAllBytesAsync(slots["ProjectAssetsFile"]));
            CollectionAssert.AreEqual(sourceProject, await File.ReadAllBytesAsync(projectPath));
            CollectionAssert.AreEqual(sourceEntry, await File.ReadAllBytesAsync(Path.Combine(root, "Entry.cs")));
            if (externalOutputs)
                Assert.IsFalse(Directory.Exists(slots[externalOutput]), "Fork work must not create a source output directory.");
            var node = EvaluatedBuildGraph.Source(WorkspaceManager.GetCurrentSolution(fork.ForkWorkspaceId!), CancellationToken.None).Nodes.Single(item => !item.IsOuterBuild);
            var actual = MsBuildMetadataHelper.EvaluateProperties(node.ProjectPath, node.Globals, slots.Keys.ToArray());
            if (externalOutput == "CustomOutputBase")
            {
                var output = MsBuildMetadataHelper.EvaluateProperties(node.ProjectPath, node.Globals, "OutputPath").Single();
                Assert.IsNotNull(sourceOutput);
                Assert.AreEqual(Path.TrimEndingDirectorySeparator(Path.Combine(fork.ForkPath!, Path.GetRelativePath(scope, Path.GetFullPath(sourceOutput, root)))), Path.TrimEndingDirectorySeparator(Path.GetFullPath(output, Path.GetDirectoryName(node.ProjectPath)!)));
            }

            if (externalOutputs && externalOutput != "CustomOutputBase")
                Assert.AreEqual(Path.Combine(fork.ForkPath!, "external-output"), Path.TrimEndingDirectorySeparator(Path.GetFullPath(actual[slots.Keys.ToList().IndexOf(externalOutput)])), "The explicitly configured sibling output must be private even when it did not exist at capture.");
            foreach (var(entry, index)in slots.Select((entry, index) => (entry, index)))
            {
                if (entry.Key == "CustomOutputBase")
                    Assert.AreEqual(entry.Value, actual[index], "A semantic alias stays exact while its concrete OutputPath consumer is relocated.");
                else
                    Assert.AreEqual(Path.Combine(fork.ForkPath!, Path.GetRelativePath(coordinateRoot, entry.Value)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(actual[index])));
            }

            Assert.AreEqual(Path.Combine(fork.ForkPath!, Path.GetRelativePath(coordinateRoot, slots["TargetPath"])), WorkspaceManager.GetCurrentSolution(fork.ForkWorkspaceId!).Projects.Single().OutputFilePath);
        }
        catch (Exception failure)
        {
            primary = failure;
            foreach (var key in new[]
            {
                "BuildGraphFailureTypes",
                "BuildGraphErrorCodes",
                "BuildGraphFailureStages",
                "BuildGraphXmlFailureSymbols"
            }

            )
                if (failure.Data[key] is string label)
                    Console.WriteLine(key + "=" + label);
            throw;
        }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Native path-slot fixture cleanup failed.", primary, CleanupFailureCollector.FromAction(() =>
            {
                if (fork?.ForkWorkspaceId is { } id)
                    WorkspaceManager.Close(id);
            }), CleanupFailureCollector.FromAction(() =>
            {
                if (sourceId is not null)
                    WorkspaceManager.Close(sourceId);
            }), CleanupFailureCollector.FromAction(() =>
            {
                if (fork?.ForkPath is { } path)
                    TestFixtureFileSystem.DeleteDirectoryIfExists(path);
            }), CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(scope)));
        }
    }
}
