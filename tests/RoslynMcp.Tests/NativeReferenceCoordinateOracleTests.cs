using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeReferenceCoordinateOracleTests
{
    [TestMethod]
    [DataRow(@"nested\oracle.dll", "nested/oracle.dll")]
    [DataRow("nested%5Coracle.dll", "nested/oracle.dll")]
    [DataRow("nested%255Coracle.dll", "nested%5Coracle.dll")]
    [DataRow(@"nested\oracle%3Bpart%25.dll", "nested/oracle;part%.dll")]
    public async Task HintPath_CapturedCoordinatesMatchActualResolveAssemblyReference(string operand, string physicalRelative)
    {
        MsBuildInitializer.EnsureInitialized();
        await CheckAsync(operand, physicalRelative);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task CheckAsync(string operand, string physicalRelative)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(NativeReferenceCoordinateOracleTests), Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            NativeSdkRestoreFixture.InitializeDirectory(root);
            var expected = Path.GetFullPath(physicalRelative, root);
            Directory.CreateDirectory(Path.GetDirectoryName(expected)!);
            byte[] VersionedImage(int version)
            {
                using var buffer = new MemoryStream();
                var result = CSharpCompilation.Create("CoordinateOracleInput",
                    [CSharpSyntaxTree.ParseText("[assembly:System.Reflection.AssemblyVersion(\"" + version + ".0.0.0\")] public sealed class CoordinateOracleInput { }")],
                    [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)).Emit(buffer);
                Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
                return buffer.ToArray();
            }
            var valid = VersionedImage(0);
            var decoded = Microsoft.Build.Evaluation.ProjectCollection.Unescape(operand);
            var raw = Path.GetFullPath(decoded, root);
            var candidates = new[] { expected, raw, Path.GetFullPath(operand, root) }
                .Distinct(FileSystemPath.Comparer).ToArray();
            foreach (var candidate in candidates) Directory.CreateDirectory(Path.GetDirectoryName(candidate)!);
            var project = Path.Combine(root, "Owner.proj");
            var output = Path.Combine(root, "resolved.txt");
            var argumentReceipt = Path.Combine(root, "native-csc.rsp");
            var argumentOracle = NativeRarCscArgumentOracle.Create(root);
            new XDocument(new XElement("Project",
                new XElement("UsingTask", new XAttribute("TaskName", "NativeRarCscOracle"), new XAttribute("AssemblyFile", Microsoft.Build.Evaluation.ProjectCollection.Escape(argumentOracle))),
                new XElement("UsingTask", new XAttribute("TaskName", "ResolveAssemblyReference"),
                    new XAttribute("AssemblyFile", "$(MSBuildToolsPath)/Microsoft.Build.Tasks.Core.dll")),
                new XElement("PropertyGroup", new XElement("ReferenceRoot", Microsoft.Build.Evaluation.ProjectCollection.Escape(root))),
                new XElement("ItemGroup", new XElement("Reference", new XAttribute("Include", "CoordinateOracleInput, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null"),
                    new XElement("HintPath", "$(ReferenceRoot)/" + operand), new XElement("SpecificVersion", "true"))),
                new XElement("Target", new XAttribute("Name", "CoordinateSelection"), new XAttribute("Returns", "@(NativeResolved)"),
                    new XElement("ResolveAssemblyReference", new XAttribute("Assemblies", "@(Reference)"),
                        new XAttribute("SearchPaths", "{HintPathFromItem}"),
                        new XAttribute("FindDependencies", "false"), new XAttribute("FindRelatedFiles", "false"),
                        new XAttribute("FindSatellites", "false"), new XAttribute("FindSerializationAssemblies", "false"),
                        new XElement("Output", new XAttribute("TaskParameter", "ResolvedFiles"), new XAttribute("ItemName", "NativeResolved")))),
                new XElement("Target", new XAttribute("Name", "CoordinateOracle"), new XAttribute("DependsOnTargets", "CoordinateSelection"),
                    new XElement("NativeRarCscOracle", new XAttribute("Condition", "'@(NativeResolved->Count())' != '0'"),
                        new XAttribute("References", "@(NativeResolved)"),
                        new XAttribute("CscTaskAssembly", "$(MSBuildToolsPath)/Roslyn/Microsoft.Build.Tasks.CodeAnalysis.dll"),
                        new XAttribute("ReceiptPath", Microsoft.Build.Evaluation.ProjectCollection.Escape(argumentReceipt))),
                    new XElement("WriteLinesToFile", new XAttribute("File", output), new XAttribute("Overwrite", "true"),
                        new XAttribute("Lines", "@(NativeResolved->'%(FusionName)')"))))).Save(project);
            // A path emitted through WriteLinesToFile would cross another MSBuild
            // string-unescape boundary. Prove selected bytes by strict assembly
            // identity and reversed images, preserving literal percent filenames.
            var selections = new List<string>();
            var diagnostics = new List<string>();
            foreach (var validAt in candidates)
            {
                foreach (var candidate in candidates)
                    File.WriteAllText(candidate, "not a managed image: inverse native selection control");
                File.WriteAllBytes(validAt, valid);
                using (var physicalImage = new PEReader(ImmutableArray.CreateRange(File.ReadAllBytes(validAt))))
                {
                    Assert.IsTrue(physicalImage.HasMetadata, "The exact candidate pathname must hold a valid managed image.");
                    var metadata = physicalImage.GetMetadataReader();
                    Assert.AreEqual("CoordinateOracleInput", metadata.GetString(metadata.GetAssemblyDefinition().Name));
                }
                var run = await NativeCoordinateOracleProcess.RunAsync(project, "CoordinateOracle", root);
                diagnostics.Add(validAt + Environment.NewLine + run.StdOut + run.StdErr);
                Assert.AreEqual(0, run.ExitCode, string.Join(Environment.NewLine, diagnostics));
                var resolved = File.ReadAllLines(output);
                Assert.IsTrue(resolved.Length <= 1, "The exact single native reference cannot produce multiple assemblies.");
                if (resolved.Length == 0) continue;
                StringAssert.StartsWith(resolved[0], "CoordinateOracleInput,");
                selections.Add(validAt);
            }
            var splitNativeRoles = selections.Count == 0;
            var metadataPath = expected;
            if (splitNativeRoles)
            {
                Assert.IsFalse(OperatingSystem.IsWindows(), string.Join(Environment.NewLine, diagnostics));
                Assert.AreEqual("nested%5Coracle.dll", operand, string.Join(Environment.NewLine, diagnostics));
                Assert.IsFalse(FileSystemPath.Comparer.Equals(expected, raw));
                var versions = new[] { VersionedImage(1), VersionedImage(2) };
                foreach (var normalizedVersion in new[] { 1, 2 })
                {
                    foreach (var candidate in candidates) File.WriteAllText(candidate, "invalid unrelated coordinate");
                    File.WriteAllBytes(expected, versions[normalizedVersion - 1]);
                    File.WriteAllBytes(raw, versions[2 - normalizedVersion]);
                    foreach (var requestedVersion in new[] { 1, 2 })
                    {
                        SetRequestedVersion(requestedVersion);
                        var qualified = await NativeCoordinateOracleProcess.RunAsync(project, "CoordinateOracle", root);
                        Assert.AreEqual(0, qualified.ExitCode, qualified.StdOut + qualified.StdErr);
                        var cliSelected = File.ReadAllLines(output);
                        if (requestedVersion != 3 - normalizedVersion)
                            Assert.IsEmpty(cliSelected, "The actual SDK CLI must reject the raw-coordinate version mismatch.");
                        else
                        {
                            Assert.HasCount(1, cliSelected);
                            StringAssert.StartsWith(cliSelected[0], "CoordinateOracleInput, Version=" + normalizedVersion + ".0.0.0,");
                        }
                        var selected = ReadNativeTargetItems(project);
                        if (requestedVersion != 3 - normalizedVersion)
                            Assert.IsEmpty(selected, "Native RAR must select versions from the raw scalar coordinate.");
                        else
                        {
                            Assert.HasCount(1, selected);
                            StringAssert.StartsWith(selected[0].FusionName, "CoordinateOracleInput, Version=" + normalizedVersion + ".0.0.0,");
                        }
                    }
                }
                // RAR's selection witness and returned compiler operand are separate
                // physical roles on this qualified native scalar-metadata profile.
                expected = raw;
            }
            else
            {
                Assert.HasCount(1, selections, string.Join(Environment.NewLine, diagnostics));
                expected = selections.Single();
                metadataPath = expected;
            }
            SetRequestedVersion(0);
            foreach (var candidate in candidates) File.WriteAllText(candidate, "not a managed image");
            File.WriteAllBytes(expected, valid);
            File.WriteAllBytes(metadataPath, valid);
            File.Delete(argumentReceipt);
            var finalNative = await NativeCoordinateOracleProcess.RunAsync(project, "CoordinateOracle", root);
            Assert.AreEqual(0, finalNative.ExitCode, finalNative.StdOut + finalNative.StdErr);
            Assert.HasCount(1, File.ReadAllLines(output));
            Assert.IsTrue(File.Exists(argumentReceipt), "This exact native CLI invocation must emit a new Csc argument receipt.");
            var nativeArguments = CommandLineParser.SplitCommandLineIntoArguments(await File.ReadAllTextAsync(argumentReceipt), removeHashComments: false).ToImmutableArray();
            var actualCompilerArguments = CSharpCommandLineParser.Default.Parse(nativeArguments, root, sdkDirectory: null);
            Assert.HasCount(1, actualCompilerArguments.MetadataReferences);
            metadataPath = Path.GetFullPath(actualCompilerArguments.MetadataReferences.Single().Reference, root);
            // RAR selection and the compiler task operand can cross distinct native boundaries.
            // Qualify the actual consumer coordinate with a valid PE, then rerun the native pipeline.
            Assert.IsTrue(FileSystemPath.IsStrictDescendant(root, metadataPath));
            Directory.CreateDirectory(Path.GetDirectoryName(metadataPath)!);
            File.WriteAllBytes(metadataPath, valid);
            File.Delete(argumentReceipt);
            var qualifiedConsumer = await NativeCoordinateOracleProcess.RunAsync(project, "CoordinateOracle", root);
            Assert.AreEqual(0, qualifiedConsumer.ExitCode, qualifiedConsumer.StdOut + qualifiedConsumer.StdErr);
            Assert.HasCount(1, File.ReadAllLines(output));
            Assert.IsTrue(File.Exists(argumentReceipt), "The qualified current CLI producer must emit its own new receipt.");
            nativeArguments = CommandLineParser.SplitCommandLineIntoArguments(await File.ReadAllTextAsync(argumentReceipt), removeHashComments: false).ToImmutableArray();
            var qualifiedArguments = CSharpCommandLineParser.Default.Parse(nativeArguments, root, sdkDirectory: null);
            Assert.HasCount(1, qualifiedArguments.MetadataReferences);
            Assert.AreEqual(metadataPath, Path.GetFullPath(qualifiedArguments.MetadataReferences.Single().Reference, root));
            var nativeItems = ReadNativeTargetItems(project);
            Assert.HasCount(1, nativeItems);
            StringAssert.StartsWith(nativeItems[0].FusionName, "CoordinateOracleInput, Version=0.0.0.0,");

            void SetRequestedVersion(int version)
            {
                var definition = XDocument.Load(project);
                definition.Descendants("Reference").Single().SetAttributeValue("Include",
                    "CoordinateOracleInput, Version=" + version + ".0.0.0, Culture=neutral, PublicKeyToken=null");
                definition.Save(project);
            }
            var graph = EvaluatedBuildGraph.FromRoots([project], new Dictionary<string, string>(),
                ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None);
            var node = graph.Nodes.Single();
            Assert.IsTrue(node.ArtifactFiles.Contains(expected, FileSystemPath.Comparer),
                "Graph artifact inventory must name the actual RAR-selected input.");
            Assert.IsTrue(node.FileItems.Any(item => FileSystemPath.Comparer.Equals(item.Path, expected)),
                "Frozen graph membership must name the actual RAR-selected input.");
            Assert.IsTrue(node.PathEvidence.InputCoordinates.Any(item => FileSystemPath.Comparer.Equals(item.Target, expected)),
                "Path provenance must name the actual RAR-selected input.");
            var declaration = MsBuildMetadataHelper.EvaluateCompilerDeclarations(project, new Dictionary<string, string>())
                .Items.Single(item => item.Kind == "Reference");
            Assert.AreEqual(expected, declaration.Path);
            {
                var distinctConsumer = !FileSystemPath.Comparer.Equals(expected, metadataPath);
                var artifact = await PhysicalInputIdentity.CaptureAsync(expected, CancellationToken.None);
                var policy = new OwnedTestWorkspacePathPolicy();
                var parser = new CompilerArgumentInputContext(root,
                    (paths, token) => policy.ValidateAsync("native-reference-coordinate", paths, token),
                    (path, token) => FileSnapshotCapture.ReadIfPresentAsync(path, PhysicalInputReader.ReadAllBytesAsync, token),
                    (path, token) => FileSnapshotCapture.ReadIfPresentAsync(path, PhysicalInputReader.ReadDigestAsync, token),
                    CompilerArgumentInputContext.CreateStagePath, CompilerArgumentFixtureAuthority.NoNativeTraversalAsync);
                var parsed = await parser.ParseAsync(nativeArguments, CancellationToken.None);
                var resolved = await parser.ResolveReferencesAsync(parsed, [], CancellationToken.None);
                Assert.AreEqual(metadataPath, resolved.References.Single().Path);
                Assert.IsTrue(resolved.Parsing.Inputs.Any(input => FileSystemPath.Comparer.Equals(input.FilePath, metadataPath)));
                File.WriteAllBytes(expected, VersionedImage(1));
                await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => PhysicalInputIdentity.AssertCurrentAsync([artifact], CancellationToken.None));
                Assert.IsEmpty(ReadNativeTargetItems(project), "Changing the raw selection witness must change actual native version selection.");
                File.WriteAllBytes(expected, valid);
                File.WriteAllBytes(metadataPath, VersionedImage(2));
                using (var consumerImage = new PEReader(ImmutableArray.CreateRange(File.ReadAllBytes(metadataPath))))
                    Assert.AreEqual(new Version(2, 0, 0, 0), consumerImage.GetMetadataReader().GetAssemblyDefinition().Version);
                if (splitNativeRoles && distinctConsumer)
                {
                    var changedMetadata = ReadNativeTargetItems(project);
                    Assert.HasCount(1, changedMetadata);
                    StringAssert.StartsWith(changedMetadata[0].FusionName, "CoordinateOracleInput, Version=2.0.0.0,");
                }
                await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => parser.AssertCurrentAsync(resolved.Parsing, CancellationToken.None));
            }
        }
        catch (Exception exception) { primary = exception; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native reference fixture cleanup failed.", primary,
            OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root))); }
    }

    private sealed record NativeResolved(string Path, string FusionName);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static NativeResolved[] ReadNativeTargetItems(string project)
    {
        var collection = new Microsoft.Build.Evaluation.ProjectCollection();
        Microsoft.Build.Execution.BuildManager? manager = null;
        Exception? primary = null;
        try
        {
            manager = new Microsoft.Build.Execution.BuildManager("ReferenceCoordinate-" + Guid.NewGuid().ToString("N"));
            var result = manager.Build(new Microsoft.Build.Execution.BuildParameters(collection)
                { EnableNodeReuse = false, MaxNodeCount = 1 },
                new Microsoft.Build.Execution.BuildRequestData(collection.LoadProject(project).CreateProjectInstance(), ["CoordinateSelection"],
                    hostServices: new Microsoft.Build.Execution.HostServices()));
            Assert.AreEqual(Microsoft.Build.Execution.BuildResultCode.Success, result.OverallResult);
            return result.ResultsByTarget["CoordinateSelection"].Items.Select(item => new NativeResolved(item.ItemSpec, item.GetMetadata("FusionName"))).ToArray();
        }
        catch (Exception exception) { primary = exception; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native target-result fixture cleanup failed.", primary, manager, collection); }
    }
}

internal static class NativeCoordinateOracleProcess
{
    internal static Task<PwshScriptResult> RunAsync(string project, string target, string directory) =>
        PwshScriptRunner.RunExecutableAsync("dotnet", ["msbuild", project, "-target:" + target, "-nologo", "-verbosity:minimal", "-nodeReuse:false"],
            new Dictionary<string, string?>(), workingDirectory: directory, timeout: TimeSpan.FromSeconds(90),
            description: "native SDK coordinate oracle");
}
