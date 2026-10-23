using System.Text;
using System.Xml.Linq;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class BuildPathAliasProvenanceTests
{
    [TestMethod]
    public async Task SharedPackagePath_UsesImportProvenanceAndPrivateItemCoordinates()
    {
        var root = Path.Combine(TestTempRoot.Current, "import-item-roles", Guid.NewGuid().ToString("N"));
        var destination = Path.Combine(root, "private");
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var project = Path.Combine(root, "owner.proj");
            var package = Path.Combine(root, "package.targets");
            await File.WriteAllTextAsync(package, "<Project />");
            var condition = $"Exists('{package}')";
            var document = new XDocument(new XElement("Project",
                new XElement("ImportGroup", new XAttribute("Condition", condition),
                    new XElement("Import", new XAttribute("Project", package), new XAttribute("Condition", condition))),
                new XElement("ItemGroup", new XElement("Compile", new XAttribute("Include", package), new XAttribute("Condition", condition)))));
            var bytes = Encoding.UTF8.GetBytes(document.ToString());
            await File.WriteAllBytesAsync(project, bytes);
            var graph = EvaluatedBuildGraph.FromRoots([project],
                System.Collections.Immutable.ImmutableDictionary<string, string>.Empty,
                System.Collections.Immutable.ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None);
            string Map(string path) => Path.Combine(destination, Path.GetRelativePath(root, path));
            string MapImport(string path) => FileSystemPath.Comparer.Equals(path, package) ? path : Map(path);
            var rendered = SolutionFileMutationPlanner.RelocateDefinition(project, bytes, Map, MapImport,
                BuildPathRelocation.KnownPathProperties, graph.Nodes.SelectMany(node => node.PathEvidence.Declarations).ToArray());
            var actual = XDocument.Parse(Encoding.UTF8.GetString(rendered));
            Assert.AreEqual(package, actual.Descendants("Import").Single().Attribute("Project")!.Value);
            Assert.AreEqual(condition, actual.Descendants("Import").Single().Attribute("Condition")!.Value);
            Assert.AreEqual(condition, actual.Descendants("ImportGroup").Single().Attribute("Condition")!.Value);
            Assert.AreEqual(Map(package), actual.Descendants("Compile").Single().Attribute("Include")!.Value);
            Assert.AreEqual($"Exists('{Map(package)}')", actual.Descendants("Compile").Single().Attribute("Condition")!.Value);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Path provenance fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OutputAlias_NativeEvidenceRelocatesOnlyActiveDeclaration(bool active)
    {
        var source = Path.Combine(TestTempRoot.Current, "native-alias-" + Guid.NewGuid().ToString("N"));
        var fork = Path.Combine(source, "private");
        var semantic = Path.Combine(source, "semantic-identity");
        var project = Path.Combine(source, "alias.proj");
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(source);
            var document = new XDocument(new XElement("Project", new XElement("PropertyGroup",
                new XElement("SemanticValue", semantic),
                new XElement("BaseOutputPath", new XAttribute("Condition", active ? "'true' == 'true'" : "'false' == 'true'"), "$(SemanticValue)"))));
            var bytes = Encoding.UTF8.GetBytes(document.ToString());
            await File.WriteAllBytesAsync(project, bytes);
            var graph = EvaluatedBuildGraph.FromRoots([project],
                System.Collections.Immutable.ImmutableDictionary<string, string>.Empty,
                System.Collections.Immutable.ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None);
            string Map(string path) => Path.Combine(fork, Path.GetRelativePath(source, path));
            var declarations = graph.Nodes.SelectMany(node => node.PathEvidence.Declarations).ToArray();
            var relocated = SolutionFileMutationPlanner.RelocateDefinition(project, bytes, Map, Map,
                BuildPathRelocation.KnownPathProperties, declarations);
            var actual = XDocument.Parse(Encoding.UTF8.GetString(relocated));
            Assert.AreEqual(active ? 1 : 0, declarations.Count(declaration => declaration.Name == "BaseOutputPath" && declaration.Attribute is null),
                "Native evaluation must capture only the active path consumer.");
            Assert.AreEqual(semantic, actual.Descendants("SemanticValue").Single().Value,
                "A semantic alias declaration remains unchanged even when a path consumer uses it.");
            Assert.AreEqual(active ? Map(semantic) : "$(SemanticValue)", actual.Descendants("BaseOutputPath").Single().Value,
                "Only the active native path consumer receives relocated coordinates.");
            Assert.AreEqual(semantic, BuildPathRelocation.RelocateGlobal("SemanticValue", semantic, Map, BuildPathRelocation.KnownPathProperties),
                "The name alone never changes an unrelated semantic global value.");
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Path provenance fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(source)));
        }
    }

    [TestMethod]
    public void ReassignedOutputAlias_LeavesSemanticDeclarationByteValueExact()
    {
        var source = Path.Combine(TestTempRoot.Current, "reassigned-alias");
        var fork = Path.Combine(TestTempRoot.Current, "private-reassigned-alias");
        var semantic = Path.Combine(source, "semantic-identity");
        var document = new XDocument(new XElement("Project", new XElement("PropertyGroup",
            new XElement("Alias", Path.Combine(source, "actual-output")),
            new XElement("BaseOutputPath", "$(Alias)"),
            new XElement("Alias", semantic))));
        var bytes = Encoding.UTF8.GetBytes(document.ToString());
        var actual = SolutionFileMutationPlanner.RelocateDefinition(Path.Combine(source, "fixture.csproj"), bytes,
            path => Path.Combine(fork, Path.GetRelativePath(source, path)),
            path => Path.Combine(fork, Path.GetRelativePath(source, path)), BuildPathRelocation.KnownPathProperties, []);
        Assert.AreEqual(semantic, XDocument.Parse(Encoding.UTF8.GetString(actual)).Descendants("Alias").Last().Value);
    }
    [TestMethod]
    public async Task ActiveSharedConsumer_PreservesGlobalFrameworkCorrelationAndReassignedSemanticAlias()
    {
        var root = Path.Combine(TestTempRoot.Current, "active-consumer", Guid.NewGuid().ToString("N"));
        var fork = Path.Combine(root, "private");
        var projectPath = Path.Combine(root, "active.proj");
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var semantic = Path.Combine(root, "semantic-identity");
            var document = new XDocument(new XElement("Project", new XElement("PropertyGroup",
                new XElement("Alias", Path.Combine(root, "output", "$(TargetFramework)")),
                new XElement("OutputPath", "$(Alias)/bin/"),
                new XElement("Alias", semantic))));
            var bytes = Encoding.UTF8.GetBytes(document.ToString());
            await File.WriteAllBytesAsync(projectPath, bytes);
            var graph = EvaluatedBuildGraph.FromEntries(new[] { "first", "second" }.Select(framework =>
                new EvaluatedBuildGraph.Entry(projectPath, System.Collections.Immutable.ImmutableDictionary<string, string>.Empty.Add("TargetFramework", framework))),
                System.Collections.Immutable.ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None);
            string Map(string path) => RoslynMcp.Roslyn.Helpers.FileSystemPath.Comparer.Equals(Path.GetFullPath(path), fork)
                || RoslynMcp.Roslyn.Helpers.FileSystemPath.IsStrictDescendant(fork, Path.GetFullPath(path))
                ? path : Path.Combine(fork, Path.GetRelativePath(root, path));
            var relocated = SolutionFileMutationPlanner.RelocateDefinition(projectPath, bytes,
                Map,
                Map, BuildPathRelocation.KnownPathProperties,
                graph.Nodes.SelectMany(node => node.PathEvidence.Declarations).ToArray());
            var actual = XDocument.Parse(Encoding.UTF8.GetString(relocated));
            Assert.AreEqual(semantic, actual.Descendants("Alias").Last().Value);
            StringAssert.Contains(actual.Descendants("OutputPath").Single().Value, "$(TargetFramework)");
            StringAssert.Contains(actual.Descendants("OutputPath").Single().Value, fork);
            var privatePath = Path.Combine(fork, "active.proj");
            Directory.CreateDirectory(fork);
            await File.WriteAllBytesAsync(privatePath, relocated);
            var replayed = EvaluatedBuildGraph.FromEntries(new[] { "first", "second" }.Select(framework =>
                new EvaluatedBuildGraph.Entry(privatePath, System.Collections.Immutable.ImmutableDictionary<string, string>.Empty.Add("TargetFramework", framework))),
                System.Collections.Immutable.ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None);
            foreach (var node in replayed.Nodes)
                Assert.IsTrue(node.PathEvidence.Directories.Contains(Path.Combine(fork, "output", node.Globals["TargetFramework"], "bin"), RoslynMcp.Roslyn.Helpers.FileSystemPath.Comparer));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Path provenance fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    [TestMethod]
    public async Task OverwrittenOutputDeclaration_DoesNotOwnHistoricalScratchCoordinates()
    {
        var root = Path.Combine(TestTempRoot.Current, "overwritten-output", Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var projectPath = Path.Combine(root, "active.proj");
            var historical = Path.Combine(Path.GetPathRoot(Directory.GetCurrentDirectory())!, "historical-output-coordinate");
            var final = Path.Combine(root, "actual-output");
            await File.WriteAllTextAsync(projectPath, new XElement("Project", new XElement("PropertyGroup",
                new XElement("Scratch", historical), new XElement("OutputPath", "$(Scratch)"), new XElement("OutputPath", final))).ToString());
            var node = EvaluatedBuildGraph.FromRoots([projectPath], new Dictionary<string, string>(),
                System.Collections.Immutable.ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None).Nodes.Single();
            CollectionAssert.Contains(node.PathEvidence.Directories.ToArray(), final);
            Assert.IsFalse(node.PathEvidence.OutputCoordinates.Any(path => RoslynMcp.Roslyn.Helpers.FileSystemPath.Comparer.Equals(path, historical)),
                "An overwritten value without a final consumer cannot widen physical ownership.");
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Path provenance fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    [TestMethod]
    [DataRow(false, true, false)]
    [DataRow(true, false, false)]
    [DataRow(true, true, false)]
    [DataRow(false, true, true)]
    [DataRow(true, false, true)]
    [DataRow(true, true, true)]
    public async Task ActiveAliasCondition_UsesPrivateMarkerAndPreservesSemanticAlias(bool sourceMarker, bool privateMarker, bool directoryMarker)
    {
        var root = Path.Combine(TestTempRoot.Current, "alias-condition", Guid.NewGuid().ToString("N"));
        var fork = Path.Combine(root, "private");
        var inputs = Path.Combine(root, "inputs");
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(inputs);
            Directory.CreateDirectory(Path.Combine(fork, "inputs"));
            var projectPath = Path.Combine(root, "active.proj");
            var privatePath = Path.Combine(fork, "active.proj");
            var document = new XDocument(new XElement("Project", new XElement("PropertyGroup", new XElement("SemanticValue", "$(Root)")),
                new XElement("ItemGroup", new XElement("Compile", new XAttribute("Include", "$(Root)/A.cs"),
                    new XAttribute("Condition", "Exists('$(Root)/marker.flag')")))));
            var bytes = Encoding.UTF8.GetBytes(document.ToString());
            await File.WriteAllBytesAsync(projectPath, bytes);
            await File.WriteAllTextAsync(Path.Combine(inputs, "A.cs"), "public class MarkerControlled { }");
            if (directoryMarker) Directory.CreateDirectory(Path.Combine(inputs, "marker.flag"));
            else await File.WriteAllTextAsync(Path.Combine(inputs, "marker.flag"), "selected");
            await File.WriteAllTextAsync(Path.Combine(fork, "inputs", "A.cs"), "public class MarkerControlled { }");
            if (privateMarker)
            {
                if (directoryMarker) Directory.CreateDirectory(Path.Combine(fork, "inputs", "marker.flag"));
                else await File.WriteAllTextAsync(Path.Combine(fork, "inputs", "marker.flag"), "selected");
            }
            var globals = new Dictionary<string, string> { ["Root"] = inputs };
            var graph = EvaluatedBuildGraph.FromRoots([projectPath], globals,
                System.Collections.Immutable.ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None);
            string Map(string path) => RoslynMcp.Roslyn.Helpers.FileSystemPath.Comparer.Equals(Path.GetFullPath(path), fork)
                || RoslynMcp.Roslyn.Helpers.FileSystemPath.IsStrictDescendant(fork, Path.GetFullPath(path))
                ? path : Path.Combine(fork, Path.GetRelativePath(root, path));
            var relocated = SolutionFileMutationPlanner.RelocateDefinition(projectPath, bytes, Map, Map,
                BuildPathRelocation.KnownPathProperties, graph.Nodes.SelectMany(node => node.PathEvidence.Declarations).ToArray());
            await File.WriteAllBytesAsync(privatePath, relocated);
            if (!sourceMarker)
            {
                if (directoryMarker) Directory.Delete(Path.Combine(inputs, "marker.flag"));
                else File.Delete(Path.Combine(inputs, "marker.flag"));
            }
            var actual = EvaluatedBuildGraph.FromRoots([privatePath], globals,
                System.Collections.Immutable.ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None).Nodes.Single();
            Assert.AreEqual(inputs, actual.Globals["Root"]);
            Assert.AreEqual("$(Root)", XDocument.Parse(Encoding.UTF8.GetString(relocated)).Descendants("SemanticValue").Single().Value);
            Assert.AreEqual(privateMarker, actual.DocumentFiles.Contains(Path.Combine(fork, "inputs", "A.cs"), RoslynMcp.Roslyn.Helpers.FileSystemPath.Comparer));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Path provenance fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    [TestMethod]
    public async Task NamespacedActivePathConsumer_ReplaysValidMSBuildXml()
    {
        var root = Path.Combine(TestTempRoot.Current, "namespaced-consumer", Guid.NewGuid().ToString("N"));
        var fork = Path.Combine(root, "private");
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(fork);
            XNamespace ns = "http://schemas.microsoft.com/developer/msbuild/2003";
            var projectPath = Path.Combine(root, "active.proj");
            var bytes = Encoding.UTF8.GetBytes(new XElement(ns + "Project", new XElement(ns + "PropertyGroup",
                new XElement(ns + "Alias", Path.Combine(root, "output")), new XElement(ns + "OutputPath", "$(Alias)"))).ToString());
            await File.WriteAllBytesAsync(projectPath, bytes);
            var graph = EvaluatedBuildGraph.FromRoots([projectPath], new Dictionary<string, string>(),
                System.Collections.Immutable.ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None);
            string Map(string path) => RoslynMcp.Roslyn.Helpers.FileSystemPath.Comparer.Equals(Path.GetFullPath(path), fork)
                || RoslynMcp.Roslyn.Helpers.FileSystemPath.IsStrictDescendant(fork, Path.GetFullPath(path))
                ? path : Path.Combine(fork, Path.GetRelativePath(root, path));
            var replay = SolutionFileMutationPlanner.RelocateDefinition(projectPath, bytes, Map, Map,
                BuildPathRelocation.KnownPathProperties, graph.Nodes.SelectMany(node => node.PathEvidence.Declarations).ToArray());
            var privatePath = Path.Combine(fork, "active.proj");
            await File.WriteAllBytesAsync(privatePath, replay);
            var actual = EvaluatedBuildGraph.FromRoots([privatePath], new Dictionary<string, string>(),
                System.Collections.Immutable.ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None).Nodes.Single();
            CollectionAssert.Contains(actual.PathEvidence.Directories.ToArray(), Path.Combine(fork, "output"));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Path provenance fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task PhysicalConditionIdentity_DistinguishesExistingDirectoryAndMissingInput(bool exists)
    {
        var root = Path.Combine(TestTempRoot.Current, "condition-presence", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "marker");
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            if (exists) Directory.CreateDirectory(path);
            var identity = await PhysicalInputIdentity.CaptureAsync(path, CancellationToken.None);
            Assert.AreEqual(exists ? PhysicalInputKind.Directory : PhysicalInputKind.Missing, identity.Kind);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Path provenance fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    [TestMethod]
    public async Task HistoricalWildcardImport_UsesOriginalBindingWithoutChangingLaterSemanticAlias()
    {
        var root = Path.Combine(TestTempRoot.Current, "historical-import", Guid.NewGuid().ToString("N"));
        var fork = Path.Combine(root, "private");
        var imports = Path.Combine(root, "imports");
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(imports);
            Directory.CreateDirectory(Path.Combine(fork, "imports"));
            foreach (var name in new[] { "First", "Second" })
            {
                var imported = new XElement("Project", new XElement("PropertyGroup", new XElement(name, "selected"))).ToString();
                await File.WriteAllTextAsync(Path.Combine(imports, name + ".props"), imported);
                await File.WriteAllTextAsync(Path.Combine(fork, "imports", name + ".props"), imported);
            }
            var projectPath = Path.Combine(root, "active.proj");
            var semantic = Path.Combine(root, "semantic-import-identity");
            var bytes = Encoding.UTF8.GetBytes(new XElement("Project",
                new XElement("PropertyGroup", new XElement("ImportAlias", imports)),
                new XElement("Import", new XAttribute("Project", "$(ImportAlias)/*.props")),
                new XElement("PropertyGroup", new XElement("ImportAlias", semantic))).ToString());
            await File.WriteAllBytesAsync(projectPath, bytes);
            var graph = EvaluatedBuildGraph.FromRoots([projectPath], new Dictionary<string, string>(),
                System.Collections.Immutable.ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None);
            string Map(string path) => RoslynMcp.Roslyn.Helpers.FileSystemPath.Comparer.Equals(Path.GetFullPath(path), fork)
                || RoslynMcp.Roslyn.Helpers.FileSystemPath.IsStrictDescendant(fork, Path.GetFullPath(path))
                ? path : Path.Combine(fork, Path.GetRelativePath(root, path));
            var replay = SolutionFileMutationPlanner.RelocateDefinition(projectPath, bytes, Map, Map, BuildPathRelocation.KnownPathProperties,
                graph.Nodes.SelectMany(node => node.PathEvidence.Declarations).ToArray());
            var privatePath = Path.Combine(fork, "active.proj");
            await File.WriteAllBytesAsync(privatePath, replay);
            File.Delete(Path.Combine(imports, "First.props"));
            File.Delete(Path.Combine(imports, "Second.props"));
            var actual = EvaluatedBuildGraph.FromRoots([privatePath], new Dictionary<string, string>(),
                System.Collections.Immutable.ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None).Nodes.Single();
            Assert.AreEqual(semantic, XDocument.Parse(Encoding.UTF8.GetString(replay)).Descendants("ImportAlias").Last().Value);
            CollectionAssert.Contains(actual.BuildInputs.Definitions.ToArray(), Path.Combine(fork, "imports", "First.props"));
            CollectionAssert.Contains(actual.BuildInputs.Definitions.ToArray(), Path.Combine(fork, "imports", "Second.props"));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Path provenance fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task ConditionDirectoryLayout_ReplaysExactPresenceWithoutRecursivelyCopyingProbeContents(bool exists)
    {
        var scope = Path.Combine(TestTempRoot.Current, "directory-probe-layout", Guid.NewGuid().ToString("N"));
        var root = Path.Combine(scope, "source");
        var inputs = Path.Combine(scope, "inputs");
        var marker = Path.Combine(inputs, "marker");
        var fork = Path.Combine(scope, "fork");
        Exception? primary = null;
        Microsoft.CodeAnalysis.AdhocWorkspace? workspace = null;
        PhysicalDirectoryScope.OwnedRoot? copyOwner = null;
        try
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(inputs);
            var projectPath = Path.Combine(root, "active.csproj");
            var bytes = Encoding.UTF8.GetBytes(new XElement("Project", new XElement("PropertyGroup", new XElement("ProbeRoot", inputs)),
                new XElement("ItemGroup", new XElement("Compile", new XAttribute("Include", "$(ProbeRoot)/A.cs"),
                    new XAttribute("Condition", "Exists('$(ProbeRoot)/marker')")))).ToString());
            await File.WriteAllBytesAsync(projectPath, bytes);
            await File.WriteAllTextAsync(Path.Combine(inputs, "A.cs"), "public class MarkerControlled { }");
            var sentinel = Path.Combine(marker, "unrelated.txt");
            if (exists)
            {
                Directory.CreateDirectory(marker);
                await File.WriteAllTextAsync(sentinel, "leave this unrelated child in the source fixture");
            }
            workspace = new Microsoft.CodeAnalysis.AdhocWorkspace();
            var project = workspace.AddProject(Microsoft.CodeAnalysis.ProjectInfo.Create(Microsoft.CodeAnalysis.ProjectId.CreateNewId(),
                Microsoft.CodeAnalysis.VersionStamp.Create(), "active", "active", Microsoft.CodeAnalysis.LanguageNames.CSharp, filePath: projectPath));
            var policy = new OwnedTestWorkspacePathPolicy();
            var layout = await PhysicalGraphLayout.CreateAsync("condition-layout", projectPath, project.Solution, policy, CancellationToken.None);
            var identity = layout.ConditionInputs.Single(input => RoslynMcp.Roslyn.Helpers.FileSystemPath.Comparer.Equals(input.FilePath, marker));
            Assert.AreEqual(exists ? PhysicalInputKind.Directory : PhysicalInputKind.Missing, identity.Kind);
            var plan = await layout.PlanCopyAsync("condition-layout", fork, policy, CancellationToken.None);
            await policy.ValidateAsync("condition-layout", plan.Inventory, CancellationToken.None);
            copyOwner = PhysicalDirectoryScope.CreateOwnedRoot(fork);
            await plan.CopyAsync(copyOwner, CancellationToken.None);
            var privateMarker = layout.MapTo(fork, marker);
            Assert.AreEqual(exists, Directory.Exists(privateMarker));
            Assert.IsFalse(File.Exists(Path.Combine(privateMarker, "unrelated.txt")), "An existence probe never authorizes recursive content copying.");
            if (exists) Assert.AreEqual("leave this unrelated child in the source fixture", await File.ReadAllTextAsync(sentinel));
            var privatePath = layout.MapTo(fork, projectPath);
            await File.WriteAllBytesAsync(privatePath, SolutionFileMutationPlanner.RelocateDefinition(projectPath, bytes,
                path => layout.MapInputPath(fork, path),
                path => layout.MapInputPath(fork, path), BuildPathRelocation.KnownPathProperties,
                layout.BuildNodes.SelectMany(node => node.PathEvidence.Declarations).ToArray()));
            if (exists)
            {
                File.Delete(sentinel);
                Directory.Delete(marker);
            }
            else Directory.CreateDirectory(marker);
            var actual = EvaluatedBuildGraph.FromRoots([privatePath], new Dictionary<string, string>(),
                System.Collections.Immutable.ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None).Nodes.Single();
            Assert.AreEqual(exists, actual.DocumentFiles.Contains(layout.MapTo(fork, Path.Combine(inputs, "A.cs")), RoslynMcp.Roslyn.Helpers.FileSystemPath.Comparer));
            Assert.AreEqual(inputs, XDocument.Parse(await File.ReadAllTextAsync(privatePath)).Descendants("ProbeRoot").Single().Value);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Path provenance fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => copyOwner?.Dispose()),
                CleanupFailureCollector.FromAction(() => workspace?.Dispose()),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(scope)));
        }
    }

    [TestMethod]
    [DataRow("PropertyGroup", "OutputPath", false)]
    [DataRow("ItemGroup", "Compile", false)]
    [DataRow("PropertyGroup", "OutputPath", true)]
    [DataRow("ItemGroup", "Compile", true)]
    public async Task InactiveParent_DoesNotEnrollOrRewriteUnexecutedChildCondition(string groupName, string childName, bool literal)
    {
        var root = Path.Combine(TestTempRoot.Current, "inactive-parent", Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var outside = Path.Combine(Path.GetPathRoot(Directory.GetCurrentDirectory())!, "never-authorized-condition", "marker.flag");
            var operand = literal ? outside : "$(PrivateOutside)";
            var child = new XElement(childName, new XAttribute("Condition", "Exists('" + operand + "')"));
            if (childName == "Compile") child.SetAttributeValue("Include", operand + "/A.cs");
            else child.Value = operand + "/output";
            var bytes = Encoding.UTF8.GetBytes(new XElement("Project",
                new XElement(groupName, new XAttribute("Condition", "'false' == 'true'"), child)).ToString());
            var path = Path.Combine(root, "active.proj");
            await File.WriteAllBytesAsync(path, bytes);
            var graph = EvaluatedBuildGraph.FromRoots([path], new Dictionary<string, string> { ["PrivateOutside"] = outside },
                System.Collections.Immutable.ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None);
            var evidence = graph.Nodes.Single().PathEvidence;
            Assert.IsFalse(evidence.ConditionProbes.Any(probe => probe.Path.StartsWith(Path.GetDirectoryName(outside)!, StringComparison.Ordinal)),
                "Native evaluation skips a child predicate beneath a false parent.");
            Assert.IsFalse(evidence.InputCoordinates.Any(coordinate => coordinate.Target.StartsWith(Path.GetDirectoryName(outside)!, StringComparison.Ordinal)));
            CollectionAssert.AreEqual(bytes, SolutionFileMutationPlanner.RelocateDefinition(path, bytes,
                value => Path.Combine(root, "private", Path.GetFileName(value)),
                value => Path.Combine(root, "private", Path.GetFileName(value)), BuildPathRelocation.KnownPathProperties, evidence.Declarations));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Path provenance fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    [TestMethod]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task FunctionExistsOperand_ReplaysPrivateMarker(bool sourceMarker, bool privateMarker)
    {
        var root = Path.Combine(TestTempRoot.Current, "function-condition", Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source");
        var fork = Path.Combine(root, "private");
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(fork);
            var path = Path.Combine(source, "active.proj");
            var bytes = Encoding.UTF8.GetBytes(new XElement("Project", new XElement("ItemGroup",
                new XElement("Compile", new XAttribute("Include", "$(Root)/A.cs"),
                    new XAttribute("Condition", "Exists('$([System.IO.Path]::Combine($(Root), marker.flag))')")))).ToString());
            await File.WriteAllBytesAsync(path, bytes);
            await File.WriteAllTextAsync(Path.Combine(source, "A.cs"), "public class Selected { }");
            await File.WriteAllTextAsync(Path.Combine(fork, "A.cs"), "public class Selected { }");
            await File.WriteAllTextAsync(Path.Combine(source, "marker.flag"), "selected");
            if (privateMarker) await File.WriteAllTextAsync(Path.Combine(fork, "marker.flag"), "selected");
            var globals = new Dictionary<string, string> { ["Root"] = source };
            var graph = EvaluatedBuildGraph.FromRoots([path], globals,
                System.Collections.Immutable.ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None);
            CollectionAssert.Contains(graph.Nodes.Single().DocumentFiles.ToArray(), Path.Combine(source, "A.cs"),
                "The native property function must select the source document before transport.");
            string Map(string value) => value.StartsWith(fork, StringComparison.Ordinal) ? value : Path.Combine(fork, Path.GetRelativePath(source, value));
            var replay = SolutionFileMutationPlanner.RelocateDefinition(path, bytes, Map, Map, BuildPathRelocation.KnownPathProperties,
                graph.Nodes.SelectMany(node => node.PathEvidence.Declarations).ToArray());
            var privatePath = Path.Combine(fork, "active.proj");
            await File.WriteAllBytesAsync(privatePath, replay);
            if (!sourceMarker) File.Delete(Path.Combine(source, "marker.flag"));
            var actual = EvaluatedBuildGraph.FromRoots([privatePath], globals,
                System.Collections.Immutable.ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None).Nodes.Single();
            Assert.AreEqual(privateMarker, actual.DocumentFiles.Contains(Path.Combine(fork, "A.cs"), RoslynMcp.Roslyn.Helpers.FileSystemPath.Comparer));
            Assert.AreEqual(source, actual.Globals["Root"]);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Path provenance fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

}
