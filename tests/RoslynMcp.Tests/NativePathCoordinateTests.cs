using System.Runtime.CompilerServices;
using System.Xml.Linq;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativePathCoordinateTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PublishedEvidenceCoordinates_UseCanonicalLexicalIdentity(bool dotSegments)
    {
        MsBuildInitializer.EnsureInitialized();
        CheckPublishedCoordinates(dotSegments);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CheckPublishedCoordinates(bool dotSegments)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(NativePathCoordinateTests), Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "input"));
            File.WriteAllText(Path.Combine(root, "input", "data.txt"), "input");
            var path = Path.Combine(root, "Owner.proj");
            var operandRoot = Microsoft.Build.Evaluation.ProjectCollection.Escape(root).Replace('\\', '/');
            if (dotSegments) operandRoot += "/./";
            new XDocument(new XElement("Project",
                new XElement("PropertyGroup",
                    new XElement("InputRoot", operandRoot + "/input"),
                    new XElement("OutputRoot", operandRoot + "/out"),
                    new XElement("OutputPath", "$(OutputRoot)/bin/"),
                    new XElement("DocumentationFile", "$(OutputRoot)/docs/api.xml")),
                new XElement("ItemGroup", new XElement("AdditionalFiles", new XAttribute("Include", "$(InputRoot)/*.txt"))))).Save(path);
            var graph = EvaluatedBuildGraph.FromRoots([path], new Dictionary<string, string>(),
                System.Collections.Immutable.ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None);
            var evidence = graph.Nodes.Single().PathEvidence;
            Assert.IsTrue(evidence.InputCoordinates.Length > 0);
            Assert.IsTrue(evidence.OutputCoordinates.Length > 0);
            Assert.IsTrue(evidence.ConditionProbes.Any(probe => probe.ObservedExists is null));
            foreach (var coordinate in evidence.InputCoordinates)
            {
                Assert.AreEqual(Canonical(coordinate.Directory), coordinate.Directory);
                Assert.AreEqual(Canonical(coordinate.Target), coordinate.Target);
            }
            foreach (var coordinate in evidence.OutputCoordinates)
                Assert.AreEqual(Canonical(coordinate), coordinate);
            foreach (var probe in evidence.ConditionProbes.Where(probe => probe.ObservedExists is null))
                Assert.AreEqual(Canonical(probe.Path), probe.Path);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Native coordinate fixture cleanup failed.", primary,
                OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }

        static string Canonical(string value) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
    }

    [TestMethod]
    [DataRow(@"nested\name", "ordinary")]
    [DataRow("nested%5Cname", "ordinary")]
    [DataRow("nested%255Cname", "ordinary")]
    [DataRow("nested/name", "backslash-root")]
    [DataRow("nested/name", "escaped-backslash-root")]
    public void ItemInputPrefix_MatchesNativeFullPath(string escapedPart, string rootShape)
    {
        MsBuildInitializer.EnsureInitialized();
        CheckItemInputPrefix(escapedPart, rootShape);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CheckItemInputPrefix(string escapedPart, string rootShape)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(NativePathCoordinateTests), Guid.NewGuid().ToString("N"));
        Microsoft.Build.Evaluation.ProjectCollection? collection = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "Owner.proj");
            var escapedRoot = Microsoft.Build.Evaluation.ProjectCollection.Escape(root);
            if (rootShape != "ordinary")
            {
                escapedRoot = Microsoft.Build.Evaluation.ProjectCollection.Escape(root.Replace('/', '\\'));
                if (rootShape == "escaped-backslash-root") escapedRoot = escapedRoot.Replace("\\", "%5C", StringComparison.Ordinal);
            }
            new XDocument(new XElement("Project",
                new XElement("PropertyGroup", new XElement("InputRoot",
                    escapedRoot + "/" + escapedPart)),
                new XElement("ItemGroup", new XElement("AdditionalFiles", new XAttribute("Include", "$(InputRoot)/data.txt"))))).Save(path);
            collection = new Microsoft.Build.Evaluation.ProjectCollection();
            var evaluated = collection.LoadProject(path);
            var native = evaluated.GetItems("AdditionalFiles").Single().GetMetadataValue("FullPath");
            var graph = EvaluatedBuildGraph.FromRoots([path], new Dictionary<string, string>(),
                System.Collections.Immutable.ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None);
            var evidence = graph.Nodes.Single().PathEvidence;
            CollectionAssert.Contains(evidence.InputFiles.ToArray(), native);
            Assert.IsTrue(evidence.InputCoordinates.Any(coordinate =>
                FileSystemPath.Comparer.Equals(coordinate.Directory, Path.GetDirectoryName(native))
                && FileSystemPath.Comparer.Equals(coordinate.Target, native)),
                "Consumed item prefixes must bind the native file coordinate, preserving literal percent escapes.");
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Native coordinate fixture cleanup failed.", primary, collection,
                OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    [TestMethod]
    [DataRow("Compile", @"nested\source.cs")]
    [DataRow("Analyzer", @"nested\analyzer.dll")]
    [DataRow("AdditionalFiles", @"nested\data%3Bpart%25.txt")]
    [DataRow("EditorConfigFiles", @"nested\config%3Bpart%25.editorconfig")]
    [DataRow("AdditionalFiles", "nested/literal%5Cname.txt")]
    public void CompilerItemCoordinates_MatchNativeFullPath(string kind, string escapedInclude)
    {
        MsBuildInitializer.EnsureInitialized();
        CheckCompilerItem(kind, escapedInclude);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CheckCompilerItem(string kind, string escapedInclude)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(NativePathCoordinateTests), Guid.NewGuid().ToString("N"));
        Microsoft.Build.Evaluation.ProjectCollection? collection = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "Owner.csproj");
            new XDocument(new XElement("Project", new XElement("ItemGroup", new XElement(kind,
                new XAttribute("Include", escapedInclude))))).Save(path);
            collection = new Microsoft.Build.Evaluation.ProjectCollection();
            var evaluated = collection.LoadProject(path);
            var item = evaluated.GetItems(kind).Single();
            var native = item.GetMetadataValue("FullPath");
            Assert.IsTrue(Path.IsPathFullyQualified(native));
            if (escapedInclude.Contains("%3B", StringComparison.Ordinal)) StringAssert.Contains(native, ";part%");
            var captured = MsBuildMetadataHelper.EvaluateCompilerDeclarations(path, new Dictionary<string, string>());
            Assert.AreEqual(native, captured.Items.Single().Path);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Native coordinate fixture cleanup failed.", primary, collection,
                OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    [TestMethod]
    [DataRow(@"custom\config%3Bpart%25.editorconfig")]
    [DataRow("custom/literal%5Cname.editorconfig")]
    public void GeneratedConfig_PreservesEscapedNativeTaskOperand(string escapedPath)
    {
        MsBuildInitializer.EnsureInitialized();
        CheckGenerated(escapedPath);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CheckGenerated(string escapedPath)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(NativePathCoordinateTests), Guid.NewGuid().ToString("N"));
        Microsoft.Build.Execution.BuildManager? manager = null;
        Microsoft.Build.Evaluation.ProjectCollection? collection = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "Owner.csproj");
            new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0"),
                    new XElement("GeneratedMSBuildEditorConfigFile", escapedPath)),
                new XElement("ItemGroup", new XElement("EditorConfigFiles", new XAttribute("Include", "$(GeneratedMSBuildEditorConfigFile)"))))).Save(path);
            collection = new Microsoft.Build.Evaluation.ProjectCollection();
            var evaluated = collection.LoadProject(path);
            var captured = EvaluatedProjectGraph.CaptureGeneratedEditorConfigPath(evaluated);
            Directory.CreateDirectory(Path.Combine(root, "custom", "literal"));
            manager = new Microsoft.Build.Execution.BuildManager("GeneratedCoordinate-" + Guid.NewGuid().ToString("N"));
            var parameters = new Microsoft.Build.Execution.BuildParameters(collection)
            {
                EnableNodeReuse = false,
                MaxNodeCount = 1,
                Loggers = [new Microsoft.Build.Logging.ConsoleLogger(Microsoft.Build.Framework.LoggerVerbosity.Minimal)]
            };
            var request = new Microsoft.Build.Execution.BuildRequestData(evaluated.CreateProjectInstance(),
                ["GenerateMSBuildEditorConfigFile"], hostServices: new Microsoft.Build.Execution.HostServices());
            var result = manager.Build(parameters, request);
            Assert.AreEqual(Microsoft.Build.Execution.BuildResultCode.Success, result.OverallResult,
                "The installed SDK task must actually emit the independent coordinate oracle.");
            var emitted = Directory.GetFiles(root, "*.editorconfig", SearchOption.AllDirectories);
            Assert.HasCount(1, emitted);
            StringAssert.Contains(File.ReadAllText(emitted[0]), "is_global = true");
            if (escapedPath.Contains("%3B", StringComparison.Ordinal)) StringAssert.Contains(emitted[0], ";part%");
            Assert.AreEqual(emitted[0], captured);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Native coordinate fixture cleanup failed.", primary, manager, collection,
                OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }
    [TestMethod]
    [DataRow("Compile", "membership")]
    [DataRow("Compile", "graph")]
    [DataRow("Analyzer", "membership")]
    [DataRow("Analyzer", "graph")]
    [DataRow("AdditionalFiles", "membership")]
    [DataRow("AdditionalFiles", "graph")]
    [DataRow("EditorConfigFiles", "membership")]
    [DataRow("EditorConfigFiles", "graph")]
    [DataRow("ProjectReference", "membership")]
    [DataRow("ProjectReference", "graph")]
    [DataRow("ProjectReference", "declaration")]
    [DataRow("Compile", "path-evidence")]
    [DataRow("Analyzer", "path-evidence")]
    [DataRow("AdditionalFiles", "path-evidence")]
    [DataRow("EditorConfigFiles", "path-evidence")]
    [DataRow("ProjectReference", "path-evidence")]
    public void GraphAndMembershipCoordinates_MatchNativeItemFullPath(string kind, string consumer)
    {
        MsBuildInitializer.EnsureInitialized();
        CheckGraphItem(kind, consumer);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CheckGraphItem(string kind, string consumer)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(NativePathCoordinateTests), Guid.NewGuid().ToString("N"));
        Microsoft.Build.Evaluation.ProjectCollection? collection = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "nested"));
            var path = Path.Combine(root, "Owner.csproj");
            var child = Path.Combine(root, "nested", "Child.csproj");
            new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0")))).Save(child);
            var include = kind == "ProjectReference" ? @"nested\Child.csproj" : @"nested\value%3Bpart%25.input";
            new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0"), new XElement("EnableDefaultItems", "false")),
                new XElement("ItemGroup", new XElement(kind, new XAttribute("Include", include))))).Save(path);
            collection = new Microsoft.Build.Evaluation.ProjectCollection();
            var evaluated = collection.LoadProject(path);
            var native = evaluated.GetItems(kind).Single(item =>
                FileSystemPath.Comparer.Equals(item.Xml.ContainingProject.FullPath, path)
                && item.UnevaluatedInclude == include).GetMetadataValue("FullPath");
            var instance = evaluated.CreateProjectInstance();
            if (consumer == "membership")
            {
                var membership = NativeBuildMembership.CaptureEvaluation(instance, CancellationToken.None);
                Assert.IsTrue(membership.Files.Any(file => file.Path == native), "Membership must retain the native item's exact coordinate.");
                return;
            }
            if (consumer == "declaration")
            {
                var references = CompilerProjectReferences.Capture(instance,
                    new[] { System.Collections.Immutable.ImmutableArray<CompilerProjectReferenceTarget>.Empty });
                Assert.AreEqual(native, references.Declared.Single().Path);
                return;
            }
            var graph = EvaluatedBuildGraph.FromRoots([path], System.Collections.Immutable.ImmutableDictionary<string, string>.Empty,
                System.Collections.Immutable.ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None);
            var node = graph.Nodes.Single(node => node.ProjectPath == path);
            if (consumer == "path-evidence") CollectionAssert.Contains(node.PathEvidence.InputFiles.ToArray(), native);
            else if (kind == "ProjectReference")
            {
                var declaration = node.CompilerReferences.Declared.Single();
                Assert.AreEqual(native, declaration.Path);
                Assert.AreEqual(native, declaration.Targets.Single().Path,
                    "The native reference coordinate must select its exact captured compiler context.");
            }
            else if (kind == "Analyzer") CollectionAssert.Contains(node.ArtifactFiles.ToArray(), native);
            else CollectionAssert.Contains(node.DocumentFiles.ToArray(), native);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Native coordinate fixture cleanup failed.", primary, collection,
                OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

}
