using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class GeneratedEditorConfigSnapshotTests
{
    [TestMethod]
    [DataRow("configured")]
    [DataRow("fallback")]
    [DataRow("project-reference")]
    public void NativePathOperands_UseNativeFullPathCoordinates(string shape)
    {
        MsBuildInitializer.EnsureInitialized();
        CheckNativePathOperand(shape);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CheckNativePathOperand(string shape)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(NativePathOperands_UseNativeFullPathCoordinates), Guid.NewGuid().ToString("N"));
        Microsoft.Build.Evaluation.ProjectCollection? collection = null;
        AdhocWorkspace? workspace = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "Owner.csproj");
            new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0"),
                    new XElement("IntermediateOutputPath", @"obj\Debug/"),
                    shape == "configured" ? new XElement("GeneratedMSBuildEditorConfigFile", @"custom\config.editorconfig") : null),
                shape == "project-reference" ? new XElement("ItemGroup", new XElement("ProjectReference",
                    new XAttribute("Include", @"child\Child.csproj"))) : null)).Save(path);
            collection = new Microsoft.Build.Evaluation.ProjectCollection();
            var evaluated = collection.LoadProject(path);
            if (shape == "project-reference")
            {
                var native = evaluated.GetItems("ProjectReference").Single().GetMetadataValue("FullPath");
                Assert.AreEqual(Path.Combine(root, "child", "Child.csproj"), native);
                workspace = new AdhocWorkspace();
                var solution = workspace.CurrentSolution.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(),
                    VersionStamp.Create(), "Owner", "Owner", LanguageNames.CSharp, filePath: path));
                var captured = EvaluatedProjectGraph.References(solution.Projects.Single(), ImmutableDictionary<string, string>.Empty);
                Assert.AreEqual(native, captured.Single().TargetPath);
            }
            else
            {
                var configured = evaluated.GetPropertyValue("GeneratedMSBuildEditorConfigFile");
                if (string.IsNullOrEmpty(configured))
                    configured = evaluated.GetPropertyValue("IntermediateOutputPath") + "Owner.GeneratedMSBuildEditorConfig.editorconfig";
                var native = evaluated.AddItem("EditorConfigFiles", Microsoft.Build.Evaluation.ProjectCollection.Escape(configured))
                    .Single().GetMetadataValue("FullPath");
                var expected = shape == "configured" ? Path.Combine(root, "custom", "config.editorconfig")
                    : Path.Combine(root, "obj", "Debug", "net10.0", "Owner.GeneratedMSBuildEditorConfig.editorconfig");
                Assert.AreEqual(expected, native, "The native item metadata is the path interpretation oracle.");
                Assert.AreEqual(native, EvaluatedProjectGraph.CaptureGeneratedEditorConfigPath(evaluated));
            }
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Generated editorconfig native fixture cleanup failed.", primary,
                workspace, collection,
                OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    [TestMethod]
    [DataRow("configured")]
    [DataRow("absolute-configured")]
    [DataRow("fallback")]
    [DataRow("custom-override")]
    [DataRow("absent")]
    public void CapturedDescriptor_PreservesGeneratorProvenanceAndPathRules(string shape)
    {
        MsBuildInitializer.EnsureInitialized();
        CheckDescriptor(shape);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CheckDescriptor(string shape)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(GeneratedEditorConfigSnapshotTests), Guid.NewGuid().ToString("N"));
        AdhocWorkspace? workspace = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "Owner.csproj");
            var managed = Path.Combine(root, "Microsoft.Managed.Core.targets");
            const string targetName = "GenerateMSBuildEditorConfigFileShouldRun";
            new XDocument(new XElement("Project", new XElement("Target", new XAttribute("Name", targetName)))).Save(managed);
            new XDocument(new XElement("Project",
                new XElement("PropertyGroup", new XElement("IntermediateOutputPath", "obj/controlled/"),
                    shape == "configured" ? new XElement("GeneratedMSBuildEditorConfigFile", "custom/config.editorconfig")
                        : shape == "absolute-configured" ? new XElement("GeneratedMSBuildEditorConfigFile", Path.Combine(root, "absolute", "config.editorconfig")) : null),
                shape != "absent" ? new XElement("Import", new XAttribute("Project", managed)) : null,
                shape == "custom-override" ? new XElement("Target", new XAttribute("Name", targetName)) : null)).Save(path);
            var graph = EvaluatedBuildGraph.FromRoots([path], ImmutableDictionary<string, string>.Empty,
                ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None);
            var node = graph.Nodes.Single();
            var expected = shape switch
            {
                "configured" => Path.Combine(root, "custom", "config.editorconfig"),
                "absolute-configured" => Path.Combine(root, "absolute", "config.editorconfig"),
                "fallback" => Path.Combine(root, "obj", "controlled", "Owner.GeneratedMSBuildEditorConfig.editorconfig"),
                _ => null
            };
            Assert.AreEqual(expected, node.GeneratedEditorConfigPath);
            Assert.AreEqual(expected, EvaluatedProjectGraph.GeneratedEditorConfigPath(path, node.Globals));
            var changed = node with { GeneratedEditorConfigPath = Path.Combine(root, "different.editorconfig") };
            Assert.IsFalse(EvaluatedBuildGraph.StructuredInputsEquivalent(node, changed, CancellationToken.None));
            Assert.IsFalse(NativeBuildMembership.Capture(graph, CancellationToken.None).EquivalentTo(
                NativeBuildMembership.Capture(new([changed], [changed]), CancellationToken.None)));
            if (shape is "configured" or "absolute-configured")
            {
                workspace = new AdhocWorkspace();
                var solution = workspace.CurrentSolution.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(),
                    VersionStamp.Create(), "Owner", "Owner", LanguageNames.CSharp, filePath: path));
                var execution = EvaluatedExecutionIdentity.Capture(solution, static value => value,
                    ImmutableHashSet<string>.Empty, CancellationToken.None);
                execution.AssertEquivalent(solution, CancellationToken.None);
                var definition = XDocument.Load(path);
                definition.Descendants("GeneratedMSBuildEditorConfigFile").Single().Value = "custom/changed.editorconfig";
                definition.Save(path);
                Assert.ThrowsExactly<PhysicalGraphMismatchException>(() => execution.AssertEquivalent(solution, CancellationToken.None));
            }
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Generated editorconfig native fixture cleanup failed.", primary,
                workspace,
                OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    [TestMethod]
    public void GeneratedCoordinate_MapsBetweenPrivateAndReviewedGraphs()
    {
        MsBuildInitializer.EnsureInitialized();
        CheckMappedCoordinate();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CheckMappedCoordinate()
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(GeneratedEditorConfigSnapshotTests), Guid.NewGuid().ToString("N"));
        var reviewedRoot = Path.Combine(root, "reviewed");
        var privateRoot = Path.Combine(root, "private");
        AdhocWorkspace? reviewedWorkspace = null, privateWorkspace = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(reviewedRoot);
            Directory.CreateDirectory(privateRoot);
            foreach (var directory in new[] { reviewedRoot, privateRoot })
            {
                new XDocument(new XElement("Project", new XElement("Target",
                    new XAttribute("Name", "GenerateMSBuildEditorConfigFileShouldRun"))))
                    .Save(Path.Combine(directory, "Microsoft.Managed.Core.targets"));
                new XDocument(new XElement("Project", new XElement("PropertyGroup",
                    new XElement("GeneratedMSBuildEditorConfigFile", Path.Combine(directory, "obj", "config.editorconfig"))),
                    new XElement("Import", new XAttribute("Project", "Microsoft.Managed.Core.targets"))))
                    .Save(Path.Combine(directory, "Owner.csproj"));
            }
            reviewedWorkspace = new AdhocWorkspace();
            privateWorkspace = new AdhocWorkspace();
            var reviewed = reviewedWorkspace.CurrentSolution.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(),
                VersionStamp.Create(), "Owner", "Owner", LanguageNames.CSharp, filePath: Path.Combine(reviewedRoot, "Owner.csproj")));
            var staged = privateWorkspace.CurrentSolution.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(),
                VersionStamp.Create(), "Owner", "Owner", LanguageNames.CSharp, filePath: Path.Combine(privateRoot, "Owner.csproj")));
            string ToReviewed(string path) => path.Equals(privateRoot, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(privateRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    ? Path.GetFullPath(Path.GetRelativePath(privateRoot, path), reviewedRoot) : path;
            var execution = EvaluatedExecutionIdentity.Capture(staged, ToReviewed, ImmutableHashSet<string>.Empty, CancellationToken.None);
            execution.AssertEquivalent(reviewed, CancellationToken.None);
            var projectPath = Path.Combine(reviewedRoot, "Owner.csproj");
            var changed = XDocument.Load(projectPath);
            changed.Descendants("GeneratedMSBuildEditorConfigFile").Single().Value = Path.Combine(reviewedRoot, "obj", "different.editorconfig");
            changed.Save(projectPath);
            Assert.ThrowsExactly<PhysicalGraphMismatchException>(() => execution.AssertEquivalent(reviewed, CancellationToken.None));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Generated editorconfig native fixture cleanup failed.", primary,
                privateWorkspace, reviewedWorkspace,
                OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    [TestMethod]
    public void MultiTargetGraph_CapturesEachConfiguredInnerContext()
    {
        MsBuildInitializer.EnsureInitialized();
        CheckMultiTarget();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CheckMultiTarget()
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(GeneratedEditorConfigSnapshotTests), Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "Owner.csproj");
            File.WriteAllText(path, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFrameworks>net10.0;net9.0</TargetFrameworks>
                    <GeneratedMSBuildEditorConfigFile>obj/$(TargetFramework)/controlled.editorconfig</GeneratedMSBuildEditorConfigFile>
                  </PropertyGroup>
                </Project>
                """);
            var graph = EvaluatedBuildGraph.FromRoots([path], ImmutableDictionary<string, string>.Empty,
                ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None);
            var inner = graph.Nodes.Where(node => !node.IsOuterBuild).ToArray();
            Assert.HasCount(2, inner);
            CollectionAssert.AreEquivalent(new[] { "net10.0", "net9.0" }, inner.Select(node => node.TargetFramework).ToArray());
            foreach (var node in inner)
            {
                Assert.AreEqual(Path.Combine(root, "obj", node.TargetFramework!, "controlled.editorconfig"), node.GeneratedEditorConfigPath);
                Assert.AreEqual(node.GeneratedEditorConfigPath, EvaluatedProjectGraph.GeneratedEditorConfigPath(path, node.Globals));
            }
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Generated editorconfig native fixture cleanup failed.", primary,
                OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }
}
