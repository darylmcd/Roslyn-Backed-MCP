using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeCompilerReferenceContextTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize] public static void Initialize(TestContext _) => InitializeServices();
    [TestMethod]
    public async Task DiamondWithDistinctDirectAndTransitiveFrameworks_PreservesDirectEdgeDuringMembershipOnlyApply()
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(NativeCompilerReferenceContextTests), Guid.NewGuid().ToString("N"));
        var appPath = Path.Combine(root, "App", "App.csproj");
        var contractPath = Path.Combine(root, "Contract", "Contract.csproj");
        string? workspaceId = null;
        JsonDocument? assets = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(Path.Combine(root, "global.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(root, "Directory.Build.props"), """
                <Project><PropertyGroup>
                  <Configuration Condition="'$(Configuration)' == ''">Debug</Configuration>
                  <NuGetAudit>false</NuGetAudit>
                </PropertyGroup></Project>
                """);
            foreach (var name in new[] { "App", "Library", "Contract" })
            {
                var directory = Path.Combine(root, name);
                Directory.CreateDirectory(directory);
                var references = name switch
                {
                    "App" => """<ItemGroup><ProjectReference Include="../Contract/Contract.csproj" /><ProjectReference Include="../Library/Library.csproj" /></ItemGroup>""",
                    "Library" => """<ItemGroup><ProjectReference Include="../Contract/Contract.csproj" AdditionalProperties="TargetFramework=net9.0" SetTargetFramework="TargetFramework=net9.0" /></ItemGroup>""",
                    _ => ""
                };
                await File.WriteAllTextAsync(Path.Combine(directory, name + ".csproj"),
                    "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>" + (name == "Contract"
                        ? "<TargetFrameworks>net9.0;net10.0</TargetFrameworks>"
                        : "<TargetFramework>" + (name == "Library" ? "net9.0" : "net10.0") + "</TargetFramework>")
                        + "</PropertyGroup>" + references + "</Project>");
                await File.WriteAllTextAsync(Path.Combine(directory, "Entry.cs"), "namespace " + name + "; public class Entry { }");
            }
            // Materialize the real complete multi-target assets before the native graph load.
            foreach (var path in new[] { contractPath, appPath })
            {
                var restored = await DotnetCommandRunner.RunAsync(root, path,
                    ["restore", path, "--nologo", "-p:Configuration=Debug"], CancellationToken.None);
                Assert.IsTrue(restored.Succeeded, "The controlled source restore must succeed for each actual context.");
            }
            workspaceId = (await WorkspaceManager.LoadAsync(appPath,
                RoslynMcp.Tests.Helpers.OwnedNativeLoadContext.Properties(appPath, new Dictionary<string, string> { ["Configuration"] = "Debug" }), CancellationToken.None)).WorkspaceId;
            var original = WorkspaceManager.GetCurrentSolution(workspaceId);
            var app = original.Projects.Single(project => project.Name == "App");
            var library = original.Projects.Single(project => project.Name == "Library");
            var contracts = original.Projects.Where(project => FileSystemPath.Comparer.Equals(project.FilePath, contractPath)).ToArray();
            Assert.HasCount(2, contracts, "The premise requires two actually loaded contexts of the same physical project.");
            var directContext = contracts.Single(project => FileSystemPath.Comparer.Equals(project.OutputFilePath, Path.Combine(root, "Contract", "bin", "Debug", "net10.0", "Contract.dll")));
            var transitiveContext = contracts.Single(project => FileSystemPath.Comparer.Equals(project.OutputFilePath, Path.Combine(root, "Contract", "bin", "Debug", "net9.0", "Contract.dll")));
            Assert.AreNotEqual(directContext.Id, transitiveContext.Id);
            Assert.AreEqual(directContext.Id, app.ProjectReferences.Single(reference => original.GetProject(reference.ProjectId)?.FilePath == contractPath).ProjectId);
            Assert.AreEqual(transitiveContext.Id, library.ProjectReferences.Single().ProjectId);
            var graph = EvaluatedBuildGraph.Source(original, CancellationToken.None);
            var assetsPath = Path.Combine(root, "Contract", "obj", "project.assets.json");
            assets = JsonDocument.Parse(await File.ReadAllBytesAsync(assetsPath));
            Assert.IsTrue(ProjectMetadataParser.TryGetAssetsTarget(assets.RootElement, EvaluatedBuildGraph.Resolve(directContext, graph).Inputs, false, out _));
            Assert.IsTrue(ProjectMetadataParser.TryGetAssetsTarget(assets.RootElement, EvaluatedBuildGraph.Resolve(transitiveContext, graph).Inputs, false, out _));
            var appNode = EvaluatedBuildGraph.Resolve(app, graph);
            Assert.HasCount(1, appNode.Dependencies.Where(node => FileSystemPath.Comparer.Equals(node.ProjectPath, contractPath)
                && node.Inputs.TargetFramework == "net10.0").ToArray());
            var projectBefore = await File.ReadAllBytesAsync(contractPath);
            var assetsBefore = await File.ReadAllBytesAsync(assetsPath);
            var additionPath = Path.Combine(root, "App", "Review.txt");
            var modified = original.AddAdditionalDocument(DocumentId.CreateNewId(app.Id), "Review.txt",
                SourceText.From("reviewed membership\n"), filePath: additionPath);
            var changes = await SolutionDiffHelper.ComputeChangesAsync(original, modified,
                SolutionDiffReadContext.ForWorkspace(WorkspaceManager, workspaceId), CancellationToken.None);
            Assert.IsTrue(changes.Any(change => FileSystemPath.Comparer.Equals(change.FilePath, appPath)));
            var token = PreviewStore.Store(workspaceId, original, modified, WorkspaceManager.GetCurrentVersion(workspaceId),
                "membership with exact direct context", changes, PreviewKind.Unspecified);
            var applied = await RefactoringService.ApplyRefactoringAsync(token, "test_apply", CancellationToken.None);
            Assert.IsTrue(applied.Success, applied.Error);
            var current = WorkspaceManager.GetCurrentSolution(workspaceId);
            var currentApp = current.Projects.Single(project => project.Name == "App");
            var currentLibrary = current.Projects.Single(project => project.Name == "Library");
            Assert.AreEqual(directContext.OutputFilePath, current.GetProject(currentApp.ProjectReferences.Single(reference => current.GetProject(reference.ProjectId)?.FilePath == contractPath).ProjectId)?.OutputFilePath);
            Assert.AreEqual(transitiveContext.OutputFilePath, current.GetProject(currentLibrary.ProjectReferences.Single().ProjectId)?.OutputFilePath);
            Assert.AreEqual("reviewed membership\n", await File.ReadAllTextAsync(additionPath));
            CollectionAssert.AreEqual(projectBefore, await File.ReadAllBytesAsync(contractPath));
            CollectionAssert.AreEqual(assetsBefore, await File.ReadAllBytesAsync(assetsPath));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Native compiler context fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => assets?.Dispose()),
                CleanupFailureCollector.FromAction(() => { if (workspaceId is not null) WorkspaceManager.Close(workspaceId); }),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }
}
