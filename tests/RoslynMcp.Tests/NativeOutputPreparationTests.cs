using System.Collections.Immutable;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeOutputPreparationTests
{
    [TestMethod]
    public async Task FreshNoEdgeOutputDirectory_FinalNativeOptionsUseStabilizedCondition()
    {
        await WithProjectAsync("condition", async (root, project) =>
        {
            Assert.IsFalse(Directory.Exists(Path.Combine(root, "obj")));
            await LoadAsync(project, solution =>
            {
                Assert.IsTrue(((CSharpParseOptions)solution.Projects.Single().ParseOptions!).PreprocessorSymbolNames.Contains("PREPARED_OUTPUT"));
                Assert.AreEqual(NativeCompilerContextAvailability.Certified, NativeCompilerContextCertificate.Require(solution).Availability);
                return Task.CompletedTask;
            });
        });
    }

    [TestMethod]
    public async Task MissingGeneratedInputs_RecordActualPreparationAndNativeOpenProducerPhases()
    {
        await WithProjectAsync("condition", async (root, project) =>
        {
            RoslynMcp.Roslyn.MsBuildInitializer.EnsureInitialized();
            Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace? workspace = null;
            Exception? primary = null;
            try
            {
                var properties = OwnedNativeLoadContext.Properties(project, new Dictionary<string, string>());
                var policy = new OwnedTestWorkspacePathPolicy();
                var initial = await NativeCompilerContextCertificate.Epoch.CaptureAsync("writer-phase-inventory", project,
                    properties, policy, SupplementalPackageReadAuthority.None, CancellationToken.None);
                Assert.IsInstanceOfType<NativeCompilerContextCertificate.Epoch.CertifiedEpoch>(initial);
                var graph = EvaluatedBuildGraph.FromRoots([project], properties,
                    System.Collections.Immutable.ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None);
                var node = graph.Nodes.Single(item => !item.IsOuterBuild);
                var directory = Path.Combine(root, "obj", "Debug", "net10.0");
                var expected = new[]
                {
                    (Path: Path.Combine(directory, ".NETCoreApp,Version=v10.0.AssemblyAttributes.cs"),
                        Task: "WriteLinesToFile", Implementation: "Microsoft.Build.Tasks.WriteLinesToFile", Parameter: "File"),
                    (Path: Path.Combine(directory, "Native.AssemblyInfo.cs"),
                        Task: "WriteCodeFragment", Implementation: "Microsoft.Build.Tasks.WriteCodeFragment", Parameter: "OutputFile"),
                    (Path: Path.Combine(directory, "Native.GeneratedMSBuildEditorConfig.editorconfig"),
                        Task: "GenerateMSBuildEditorConfig", Implementation: "Microsoft.CodeAnalysis.BuildTasks.GenerateMSBuildEditorConfig", Parameter: "FileName")
                };
                var before = await PhysicalInputCaptureBatch.CaptureAsync(expected.Select(item => item.Path).ToImmutableArray(), CancellationToken.None);
                Assert.IsTrue(before.All(input => input.Kind == PhysicalInputKind.Missing),
                    "All three actual generated-input coordinates must be absent before the first producer phase.");
                var (preparation, _) = await NativeCompilerTaskInputs.PrepareOutputsAsync(node.ProjectPath, node.Globals,
                    node.TargetFramework, node.TaskRegistrations.ToolsVersion, node.CompilerReferences.NativeCompileTargets, CancellationToken.None);
                var afterPreparation = await PhysicalInputCaptureBatch.CaptureAsync(expected.Select(item => item.Path).ToImmutableArray(), CancellationToken.None);
                foreach (var receipt in preparation.Writers)
                    Console.WriteLine($"NativePreparationWriter: Project={receipt.Project}; Target={receipt.Target}; TargetFile={receipt.TargetFile}; Task={receipt.Task}; Assembly={receipt.Assembly}; Implementation={receipt.Implementation}; Parameter={receipt.Parameter}; Path={receipt.Path}; Context={receipt.ContextIdentity}");

                workspace = Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace.Create(properties);
                var native = await workspace.OpenProjectAsync(project, cancellationToken: CancellationToken.None);
                var afterOpen = await PhysicalInputCaptureBatch.CaptureAsync(expected.Select(item => item.Path).ToImmutableArray(), CancellationToken.None);
                foreach (var item in expected)
                {
                    var capturedBefore = before.Single(input => FileSystemPath.Comparer.Equals(input.FilePath, item.Path));
                    var capturedPreparation = afterPreparation.Single(input => FileSystemPath.Comparer.Equals(input.FilePath, item.Path));
                    var capturedOpen = afterOpen.Single(input => FileSystemPath.Comparer.Equals(input.FilePath, item.Path));
                    var firstPhase = capturedPreparation.Kind == PhysicalInputKind.File ? "Preparation" : "NativeOpen";
                    Console.WriteLine($"NativeGeneratedInputPhase: Path={item.Path}; Before={capturedBefore.Kind}; AfterPreparation={capturedPreparation.Kind}; AfterOpen={capturedOpen.Kind}; FirstPhase={firstPhase}");
                }
                Console.WriteLine("NativeGeneratedDocuments: " + string.Join(";", native.Documents.Select(document => document.FilePath)));
                Assert.IsTrue(afterOpen.All(input => input.Kind == PhysicalInputKind.File),
                    "Actual native preparation/Open must materialize all three inputs before missing-receipt assertions are meaningful.");
                var context = NativeTaskExecutionIdentity.EscapedContextIdentity(
                    NativeCompilerTaskInputs.EffectiveGlobals(node.Globals, node.TargetFramework), node.TaskRegistrations.ToolsVersion, CancellationToken.None);
                foreach (var item in expected)
                {
                    if (afterPreparation.Single(input => FileSystemPath.Comparer.Equals(input.FilePath, item.Path)).Kind != PhysicalInputKind.File)
                        continue; // A NativeOpen-only transition is reported separately; it is not a missing preparation receipt.
                    var receipts = preparation.Writers.Where(receipt => FileSystemPath.Comparer.Equals(
                        Path.GetFullPath(receipt.Path, Path.GetDirectoryName(node.ProjectPath)!), item.Path)).ToArray();
                    Assert.HasCount(1, receipts, $"The actual Preparation-created input requires its exact successful native writer receipt: {item.Task}.");
                    Assert.AreEqual(item.Task, receipts[0].Task.Split('.').Last());
                    Assert.AreEqual(item.Implementation, receipts[0].Implementation);
                    Assert.AreEqual(item.Parameter, receipts[0].Parameter);
                    Assert.AreEqual(context, receipts[0].ContextIdentity);
                }
            }
            catch (Exception failure) { primary = failure; throw; }
            finally
            {
                await CleanupFailureCollector.RunAfterFailureAsync("Native generated-input phase inventory cleanup failed.", primary,
                    CleanupFailureCollector.FromAction(() => workspace?.Dispose()));
            }
        });
    }

    [TestMethod]
    public async Task EditorConfigMutation_InitialAdmissionAllowsItsProvenOutputDirectoryTransition()
    {
        await WithProjectAsync("condition", async (root, project) =>
        {
            TestServiceContainer? services = null;
            Exception? primary = null;
            var firstChance = new List<string>();
            var mismatchedPaths = new HashSet<string>(StringComparer.Ordinal);
            var fixtureCoordinates = new HashSet<string>(FileSystemPath.Comparer) { root, project };
            var observing = new AsyncLocal<bool> { Value = true };
            EventHandler<System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs> capture = (_, args) =>
            {
                if (observing.Value && args.Exception is PublicInvalidOperationException failure
                    && failure.Message.Contains("Physical execution inputs changed", StringComparison.Ordinal))
                    lock (firstChance)
                    {
                        var hash = failure.Data["PhysicalInputPathSha256"] as string;
                        if (hash is not null) mismatchedPaths.Add(hash);
                        firstChance.Add($"PhysicalInputPathSha256={hash}; ExpectedPhysicalInputKind={failure.Data["ExpectedPhysicalInputKind"]}; ActualPhysicalInputKind={failure.Data["ActualPhysicalInputKind"]}\n{failure}");
                    }
            };
            AppDomain.CurrentDomain.FirstChanceException += capture;
            try
            {
                var config = Path.Combine(root, ".editorconfig");
                await File.WriteAllTextAsync(config, "[*.cs]\nindent_size = 4\n");
                var policy = new ForkInvocationPathPolicy(new OwnedTestWorkspacePathPolicy());
                services = TestServiceContainer.Create(new ValidationServiceOptions(), policy);
                var properties = OwnedNativeLoadContext.Properties(project, new Dictionary<string, string>());
                var status = await services.WorkspaceManager.LoadAsync(project, properties, CancellationToken.None);
                var snapshot = services.WorkspaceManager.GetCurrentSolution(status.WorkspaceId);
                Assert.AreEqual(NativeCompilerContextAvailability.Certified, NativeCompilerContextCertificate.Require(snapshot).Availability);
                var version = services.WorkspaceManager.GetCurrentVersion(status.WorkspaceId);
                var output = Path.Combine(root, "obj");
                Assert.IsTrue(Directory.Exists(output), "The first real native preparation must have materialized its output directory.");
                TestFixtureFileSystem.DeleteDirectoryIfExists(output);
                Assert.IsFalse(Directory.Exists(output));
                Assert.IsFalse(File.Exists(Path.Combine(output, "project.assets.json")), "This package-free fixture has no selecting restore payload to discard or invent.");
                var initial = await NativeCompilerContextCertificate.Epoch.CaptureAsync(status.WorkspaceId, project,
                    properties, policy, SupplementalPackageReadAuthority.None, CancellationToken.None);
                Assert.IsInstanceOfType<NativeCompilerContextCertificate.Epoch.CertifiedEpoch>(initial,
                    "The actual missing-output initial epoch must remain certified before any producer runs.");
                foreach (var input in ((NativeCompilerContextCertificate.Epoch.CertifiedEpoch)initial).Inputs)
                    if (FileSystemPath.Comparer.Equals(input.FilePath, root) || FileSystemPath.IsStrictDescendant(root, input.FilePath))
                        fixtureCoordinates.Add(input.FilePath);
                Assert.IsFalse(Directory.Exists(output), "Capturing the native initial epoch must not perform the output transition.");

                await services.EditorConfigService.SetOptionAsync(status.WorkspaceId, Path.Combine(root, "Native.cs"),
                    "indent_size", "8", "set_editorconfig_option", CancellationToken.None);

                Assert.IsTrue(Directory.Exists(output), "The admitted real producer must recreate its declared directory.");
                StringAssert.Contains(await File.ReadAllTextAsync(config), "indent_size = 8");
                Assert.AreSame(snapshot, services.WorkspaceManager.GetCurrentSolution(status.WorkspaceId));
                Assert.AreEqual(version, services.WorkspaceManager.GetCurrentVersion(status.WorkspaceId));
                Assert.AreEqual(StaleReasons.Apply, services.WorkspaceManager.GetStatus(status.WorkspaceId).StaleReason);
                Assert.IsNull(((IWorkspaceRecoveryAuthority)services.WorkspaceManager).GetPendingRecovery(status.WorkspaceId));
                await services.WorkspaceManager.ReloadAsync(status.WorkspaceId, CancellationToken.None);
                var current = services.WorkspaceManager.GetCurrentSolution(status.WorkspaceId);
                Assert.AreEqual(NativeCompilerContextAvailability.Certified, NativeCompilerContextCertificate.Require(current).Availability);
                Assert.IsTrue(((CSharpParseOptions)current.Projects.Single().ParseOptions!).PreprocessorSymbolNames.Contains("PREPARED_OUTPUT"));
                StringAssert.Contains(await File.ReadAllTextAsync(config), "indent_size = 8");
            }
            catch (Exception failure)
            {
                primary = failure;
                lock (firstChance) Console.WriteLine("EditorConfig initial output transition first-chance evidence:\n" + string.Join("\n", firstChance));
                try
                {
                    // Post-failure diagnostics enumerate names only in this owned test
                    // fixture. They issue no read grant and do not change admission.
                    foreach (var entry in Directory.EnumerateFileSystemEntries(root, "*", new EnumerationOptions
                        { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
                        fixtureCoordinates.Add(entry);
                    foreach (var coordinate in fixtureCoordinates)
                    {
                        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(coordinate)));
                        lock (firstChance)
                            if (mismatchedPaths.Contains(hash)) Console.WriteLine($"FixturePhysicalInputHashMatch: {hash} => {coordinate}");
                    }
                }
                catch (Exception diagnosticFailure)
                {
                    primary = new AggregateException("Output transition failed with a fixture-coordinate diagnostic failure.", failure, diagnosticFailure);
                    throw primary;
                }
                throw;
            }
            finally
            {
                observing.Value = false;
                AppDomain.CurrentDomain.FirstChanceException -= capture;
                await CleanupFailureCollector.RunAfterFailureAsync("EditorConfig output transition fixture cleanup failed.", primary,
                    CleanupFailureCollector.FromAction(() => services?.Dispose()));
            }
        });
    }

    [TestMethod]
    [DataRow("project.assets.json")]
    [DataRow("Native.csproj.nuget.dgspec.json")]
    public async Task NoPackageUnrestoredOutputPreparation_PreservesExactMissingRestoreArtifact(string artifact)
    {
        await WithProjectAsync("condition", async (root, project) =>
        {
            var path = Path.Combine(root, "obj", artifact);
            Assert.IsFalse(File.Exists(path));
            await LoadAsync(project, async solution =>
            {
                Assert.IsFalse(File.Exists(path), "Native preparation must not restore missing artifacts.");
                Assert.AreEqual(NativeCompilerContextAvailability.Certified, NativeCompilerContextCertificate.Require(solution).Availability);
                var layout = await PhysicalGraphLayout.CreateAsync("output-absence", project, solution, new OwnedTestWorkspacePathPolicy(), CancellationToken.None);
                Assert.IsTrue(layout.BuildNodes.Count > 0, "Exact broken-state physical replay must retain graph authority.");
            });
            Assert.IsFalse(File.Exists(path));
        });
    }

    [TestMethod]
    public async Task PreparationProducer_AuthoredInputMutationCannotBeRecapturedAsBaseline()
    {
        await WithProjectAsync("mutate-authored", async (root, project) =>
        {
            var source = Path.Combine(root, "Native.cs");
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => LoadAsync(project, _ => Task.CompletedTask));
            Assert.IsTrue((await File.ReadAllTextAsync(source)).Contains("ChangedDuringPreparation", StringComparison.Ordinal), "The native writer must actually execute before testing refusal.");
        });
    }

    [TestMethod]
    public async Task FinalProducer_ChangesStabilizedDeclaredOutputRefusesWithoutRetry()
    {
        await WithProjectAsync("changing-output", async (root, project) =>
        {
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => LoadAsync(project, _ => Task.CompletedTask));
            Assert.IsTrue(File.Exists(Path.Combine(root, "obj", "changing.editorconfig")), "The declared native output producer must execute.");
        });
    }

    [TestMethod]
    public async Task RestoredProjectEdge_PreparationRetainsActualCompilerSelectedContext()
    {
        await WithProjectAsync("edge", async (root, project) =>
        {
            var properties = OwnedNativeLoadContext.Properties(project, new Dictionary<string, string>());
            var restored = await new DotnetCommandRunner().RunAsync(root, project,
                MsBuildCommandProperties.Append(["restore", project, "--nologo"], properties), CancellationToken.None);
            Assert.IsTrue(restored.Succeeded, restored.StdErr + restored.StdOut);
            Assert.IsFalse(Directory.Exists(Path.Combine(root, "prepared")), "Restore must not precreate the declared compiler output under test.");
            await LoadAsync(project, solution =>
            {
                var owner = solution.Projects.Single(candidate => FileSystemPath.Comparer.Equals(candidate.FilePath, project));
                Assert.HasCount(1, owner.ProjectReferences);
                Assert.IsTrue(((CSharpParseOptions)owner.ParseOptions!).PreprocessorSymbolNames.Contains("PREPARED_OUTPUT"));
                Assert.HasCount(1, NativeCompilerContextCertificate.Require(solution).ReferenceInputs(owner));
                return Task.CompletedTask;
            });
        });
    }

    [TestMethod]
    public async Task PreparationProducer_DeclaredOutputCannotReplaceAnActiveFileItemInput()
    {
        await WithProjectAsync("item-output-mutation", async (root, project) =>
        {
            var input = Path.Combine(root, "obj", "input-overlap.editorconfig");
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => LoadAsync(project, _ => Task.CompletedTask));
            StringAssert.Contains(await File.ReadAllTextAsync(input), "changed-input-role",
                "The real native writer must exercise the overlapping output before publication refuses.");
        });
    }

    [TestMethod]
    public async Task PublishedOutputOnly_MaterializationDoesNotInvalidateCompilerInputs()
    {
        await WithProjectAsync("condition", async (_, project) =>
        {
            await LoadAsync(project, async solution =>
            {
                var policy = new OwnedTestWorkspacePathPolicy();
                var certificate = NativeCompilerContextCertificate.Require(solution);
                var graph = EvaluatedBuildGraph.Source(solution, CancellationToken.None);
                var target = graph.Nodes.Single(node => !node.IsOuterBuild).TargetPath!;
                Assert.IsFalse(File.Exists(target));
                Assert.IsTrue(graph.Nodes.Single().PathEvidence.OutputFiles.Contains(target, FileSystemPath.Comparer));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await File.WriteAllTextAsync(target, "Materialized independently after the input certificate was issued.");
                await certificate.AssertCurrentAsync(solution, "output-preparation", policy, CancellationToken.None);
            });
        });
    }

    [TestMethod]
    [DataRow("condition-output")]
    [DataRow("item-output")]
    public async Task PublishedOutput_InputRoleOverlapRetainsItsOriginalEpoch(string kind)
    {
        await WithProjectAsync(kind, async (_, project) =>
        {
            await LoadAsync(project, async solution =>
            {
                var policy = new OwnedTestWorkspacePathPolicy();
                var certificate = NativeCompilerContextCertificate.Require(solution);
                var graph = EvaluatedBuildGraph.Source(solution, CancellationToken.None);
                var node = graph.Nodes.Single(candidate => !candidate.IsOuterBuild);
                var target = node.TargetPath!;
                Assert.IsTrue(node.PathEvidence.OutputFiles.Contains(target, FileSystemPath.Comparer));
                if (kind == "condition-output")
                    Assert.IsTrue(node.PathEvidence.ConditionProbes.Any(probe => FileSystemPath.Comparer.Equals(probe.Path, target)));
                else
                    Assert.IsTrue(node.FileItems.Any(item => FileSystemPath.Comparer.Equals(item.Path, target)));
                await certificate.AssertCurrentAsync(solution, "output-preparation", policy, CancellationToken.None);
                await File.AppendAllTextAsync(target, " changed after publication");
                await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                    certificate.AssertCurrentAsync(solution, "output-preparation", policy, CancellationToken.None));
            });
        });
    }

    private static async Task LoadAsync(string project, Func<Solution, Task> inspect)
    {
        var policy = new OwnedTestWorkspacePathPolicy();
        var loaded = await new WorkspaceSessionLoader().CreateAndOpenAsync("output-preparation", project,
            OwnedNativeLoadContext.Properties(project, new Dictionary<string, string>()), policy,
            new WorkspaceDiagnosticsSink(64), NullLogger.Instance, SupplementalPackageReadAuthority.None, null, CancellationToken.None);
        try
        {
            var projection = await EvaluatedProjectGraph.WithPersistedReferenceMetadataAsync("output-preparation", loaded.Context, policy, CancellationToken.None);
            await inspect(projection.Solution);
        }
        catch (Exception failure)
        {
            OwnedResourceCleanup.Dispose("Native output fixture load cleanup failed.", failure, loaded.Workspace, loaded.Lease);
            throw;
        }
        OwnedResourceCleanup.Dispose("Native output fixture load cleanup failed.", null, loaded.Workspace, loaded.Lease);
    }

    private static async Task WithProjectAsync(string kind, Func<string, string, Task> run)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(NativeOutputPreparationTests), Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var projectPath = Path.Combine(root, "Native.csproj");
            var coordinate = kind == "edge" ? "prepared" : "obj";
            var project = new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0"), new XElement("NuGetAudit", "false"),
                    new XElement("DefineConstants", new XAttribute("Condition", "Exists('$(MSBuildProjectDirectory)/" + coordinate + "')"), "$(DefineConstants);PREPARED_OUTPUT")));
            if (kind == "edge")
            {
                Directory.CreateDirectory(Path.Combine(root, "Dependency"));
                await File.WriteAllTextAsync(Path.Combine(root, "Dependency", "Dependency.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit></PropertyGroup></Project>");
                await File.WriteAllTextAsync(Path.Combine(root, "Dependency", "Dependency.cs"), "public class Dependency { }");
                project.Element("PropertyGroup")!.Add(new XElement("IntermediateOutputPath", "prepared/"));
                project.Add(new XElement("ItemGroup", new XElement("ProjectReference", new XAttribute("Include", "Dependency/Dependency.csproj")), new XElement("Compile", new XAttribute("Remove", "Dependency/**"))));
            }
            if (kind == "mutate-authored")
                project.Add(new XElement("Target", new XAttribute("Name", "WriteAuthoredInput"), new XAttribute("BeforeTargets", "CoreCompile"),
                    new XAttribute("Condition", "'$(DesignTimeBuild)' == 'true'"),
                    new XElement("WriteLinesToFile", new XAttribute("File", "$(MSBuildProjectDirectory)/Native.cs"), new XAttribute("Lines", "public class ChangedDuringPreparation { }"), new XAttribute("Overwrite", "true"))));
            if (kind == "changing-output")
            {
                project.Element("PropertyGroup")!.Add(new XElement("GeneratedMSBuildEditorConfigFile", "$(MSBuildProjectDirectory)/obj/changing.editorconfig"));
                project.Add(new XElement("Target", new XAttribute("Name", "ChangeDeclaredOutput"), new XAttribute("BeforeTargets", "CoreCompile"),
                    new XAttribute("Condition", "'$(DesignTimeBuild)' == 'true'"),
                    new XElement("WriteLinesToFile", new XAttribute("File", "$(GeneratedMSBuildEditorConfigFile)"), new XAttribute("Lines", "# $([System.Guid]::NewGuid())"), new XAttribute("Overwrite", "true"))));
            }
            if (kind == "item-output-mutation")
            {
                var input = Path.Combine(root, "obj", "input-overlap.editorconfig");
                Directory.CreateDirectory(Path.GetDirectoryName(input)!);
                await File.WriteAllTextAsync(input, "# original authored content input");
                project.Element("PropertyGroup")!.Add(new XElement("GeneratedMSBuildEditorConfigFile", input));
                project.Add(new XElement("ItemGroup", new XElement("Content", new XAttribute("Include", input))));
                project.Add(new XElement("Target", new XAttribute("Name", "WriteOverlappingInput"), new XAttribute("BeforeTargets", "CoreCompile"),
                    new XAttribute("Condition", "'$(DesignTimeBuild)' == 'true'"),
                    new XElement("WriteLinesToFile", new XAttribute("File", input), new XAttribute("Lines", "# changed-input-role"), new XAttribute("Overwrite", "true"))));
            }
            if (kind is "condition-output" or "item-output")
            {
                var output = Path.Combine(root, "bin", "Debug", "net10.0", "Native.dll");
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                await File.WriteAllTextAsync(output, "original output with an independent input role");
                if (kind == "condition-output")
                    project.Add(new XElement("PropertyGroup", new XElement("OutputWitness",
                        new XAttribute("Condition", "Exists('" + output + "')"), "present")));
                else
                    project.Add(new XElement("ItemGroup", new XElement("AdditionalFiles", new XAttribute("Include", output))));
            }
            await File.WriteAllTextAsync(projectPath, project.ToString());
            await File.WriteAllTextAsync(Path.Combine(root, "Native.cs"), "public class Native { }");
            await run(root, projectPath);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Native output fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }
}
