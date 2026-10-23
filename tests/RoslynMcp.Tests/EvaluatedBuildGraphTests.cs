using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class EvaluatedBuildGraphTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize] public static void ClassInit(TestContext _) => InitializeServices();
    [TestMethod]
    public async Task SoleNativeContext_RequiresKnownOutputIdentityAfterPhysicalEvaluationChanges()
    {
        IsolatedWorkspaceScope? source = null;
        string? workspaceId = null;
        Exception? primary = null;
        try
        {
            source = CreateIsolatedWorkspaceCopy();
            var path = source.GetPath("Single", "Single.csproj");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            static string Definition(string flavor) => "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework>"
                + "<NuGetAudit>false</NuGetAudit><OutputPath>bin/" + flavor + "/</OutputPath></PropertyGroup></Project>";
            await File.WriteAllTextAsync(path, Definition("before"));
            var status = await WorkspaceManager.LoadAsync(path, RoslynMcp.Tests.Helpers.OwnedNativeLoadContext.Properties(path, new Dictionary<string, string>()), CancellationToken.None);
            workspaceId = status.WorkspaceId;
            var project = WorkspaceManager.GetCurrentSolution(status.WorkspaceId).Projects.Single();
            var original = EvaluatedBuildGraph.Source(project.Solution, CancellationToken.None);
            Assert.HasCount(1, original.Nodes.Where(node => !node.IsOuterBuild).ToArray());
            Assert.AreEqual(project.OutputFilePath, EvaluatedBuildGraph.Resolve(project, original).TargetPath);
            await File.WriteAllTextAsync(path, Definition("after"));
            var changed = EvaluatedBuildGraph.Source(project.Solution, CancellationToken.None);
            Assert.HasCount(1, changed.Nodes.Where(node => !node.IsOuterBuild).ToArray());
            Assert.ThrowsExactly<PublicInvalidOperationException>(() => EvaluatedBuildGraph.Resolve(project, changed));
            var current = project.Solution.WithProjectOutputFilePath(project.Id, changed.Nodes.Single(node => !node.IsOuterBuild).TargetPath).GetProject(project.Id)!;
            Assert.AreEqual(current.OutputFilePath, EvaluatedBuildGraph.Resolve(current, changed).TargetPath);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Sole native context fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => { if (workspaceId is not null) WorkspaceManager.Close(workspaceId); }),
                () => source is null ? ValueTask.CompletedTask : source.DisposeAsync());
        }
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task ActualSourceAndFork_RespectNativeEdgeContextAuthority(bool explicitGraphMode, bool edgeOnlyOverrides)
    {
        IsolatedWorkspaceScope? source = null;
        string? workspaceId = null;
        WorkspaceForkApplyResultDto? fork = null;
        Exception? failure = null;
        try
        {
            source = CreateIsolatedWorkspaceCopy();
            var consumerPath = source.GetPath("Consumer", "Consumer.csproj");
            var dependencyPath = source.GetPath("Dependency", "Dependency.csproj");
            Directory.CreateDirectory(Path.GetDirectoryName(consumerPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(dependencyPath)!);
            await File.WriteAllTextAsync(consumerPath, """
                <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit></PropertyGroup>
                  <Import Project="GraphMode.props" />
                  <ItemGroup><ProjectReference Include="../Dependency/Dependency.csproj" AdditionalProperties="EdgeFlavor=selected"
                    GlobalPropertiesToRemove="Inherited" SetTargetFramework="TargetFramework=net10.0" /></ItemGroup></Project>
                """);
            var graphLeaf = source.GetPath("GraphLeaf", "GraphLeaf.csproj");
            Directory.CreateDirectory(Path.GetDirectoryName(graphLeaf)!);
            await File.WriteAllTextAsync(graphLeaf, """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit></PropertyGroup></Project>""");
            await File.WriteAllTextAsync(source.GetPath("Consumer", "GraphMode.props"), """
                <Project><PropertyGroup><SelectedGraphMode Condition="'$(IsGraphBuild)' == 'true'">enabled</SelectedGraphMode></PropertyGroup>
                  <ItemGroup Condition="'$(SelectedGraphMode)' == 'enabled'"><ProjectReference Include="../GraphLeaf/GraphLeaf.csproj" /></ItemGroup></Project>
                """);
            await File.WriteAllTextAsync(dependencyPath, """
                <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFrameworks>net9.0;net10.0</TargetFrameworks><NuGetAudit>false</NuGetAudit></PropertyGroup>
                  <ItemGroup>
                    <Compile Include="Edge.conditional" Condition="'$(EdgeFlavor)' == 'selected' and '$(Inherited)' == '' and '$(TargetFramework)' == 'net10.0'" />
                    <Compile Include="GraphOnly.conditional" Condition="'$(IsGraphBuild)' == 'true'" />
                  </ItemGroup></Project>
                """);
            var consumerCode = source.GetPath("Consumer", "Consumer.cs");
            await File.WriteAllTextAsync(consumerCode, "public class Consumer { public int Get() => Dependency.Value; }");
            await File.WriteAllTextAsync(source.GetPath("Dependency", "Dependency.cs"), "public class Dependency { public static int Value => 1; }");
            await File.WriteAllTextAsync(source.GetPath("Dependency", "Edge.conditional"), "public class EdgeAuthority { }");
            await File.WriteAllTextAsync(source.GetPath("Dependency", "GraphOnly.conditional"), "public class ExplicitGraphAuthority { }");
            await File.WriteAllTextAsync(source.SolutionPath, """<Solution><Project Path="Consumer/Consumer.csproj" /></Solution>""");
            const string sentinel = "synthetic-graph-context%with;value-27c1";
            var properties = new Dictionary<string, string> { ["Inherited"] = "root-only", ["PrivateGraphValue"] = sentinel };
            if (!edgeOnlyOverrides)
            {
                properties.Remove("Inherited");
                properties["EdgeFlavor"] = "selected";
                properties["TargetFramework"] = "net10.0";
            }
            if (explicitGraphMode) properties["IsGraphBuild"] = "true";
            await RestoreCopiedSolutionAsync(source.SolutionPath, properties, CancellationToken.None);
            var nativeDocumentPaths = new Dictionary<string, string[]>(FileSystemPath.Comparer);
            Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace? raw = null;
            Microsoft.Build.Evaluation.ProjectCollection? nativeEvaluation = null;
            Exception? nativePrimary = null;
            try
            {
                raw = Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace.Create(OwnedNativeLoadContext.Properties(source.SolutionPath, properties));
                var rawSolution = await raw.OpenSolutionAsync(source.SolutionPath);
                var rawDependency = rawSolution.Projects.Single(project => FileSystemPath.Comparer.Equals(project.FilePath, dependencyPath));
                foreach (var nativeProject in rawSolution.Projects)
                    nativeDocumentPaths.Add(nativeProject.FilePath!, SolutionFileMutationPlanner.AllDocuments(nativeProject)
                        .Select(document => document.FilePath!).OrderBy(path => path, FileSystemPath.Comparer).ToArray());
                Console.WriteLine("RAW-NATIVE-EDGE: " + string.Join(";", rawDependency.Documents.Select(document => document.Name)));
                var nativeGraph = EvaluatedBuildGraph.Source(rawSolution, CancellationToken.None);
                var nativeNode = nativeGraph.Nodes.Single(node => !node.IsOuterBuild && FileSystemPath.Comparer.Equals(node.ProjectPath, dependencyPath));
                Assert.AreEqual("selected", nativeNode.Globals["EdgeFlavor"]);
                Assert.IsFalse(nativeNode.Globals.ContainsKey("Inherited"));
                Assert.AreEqual("net10.0", nativeNode.TargetFramework);
                Assert.AreEqual(sentinel, nativeNode.Globals["PrivateGraphValue"]);
                Assert.AreEqual(explicitGraphMode, nativeNode.Globals.ContainsKey("IsGraphBuild"));
                if (explicitGraphMode) Assert.AreEqual("true", nativeNode.Globals["IsGraphBuild"]);
                var (nativeCapture, _) = await NativeCompilerTaskInputs.ReadRawAsync(nativeNode.ProjectPath, nativeNode.Globals,
                    nativeNode.TargetFramework, nativeNode.TaskRegistrations.ToolsVersion, nativeNode.CompilerReferences.NativeCompileTargets,
                    NativeCompilerTaskInputs.CapturePurpose.FinalCompiler, CancellationToken.None);
                var nativeArguments = Microsoft.CodeAnalysis.CSharp.CSharpCommandLineParser.Default.Parse(nativeCapture.Arguments,
                    Path.GetDirectoryName(nativeNode.ProjectPath)!, sdkDirectory: null);
                Assert.IsFalse(nativeArguments.Errors.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
                    "The actual Csc command items must parse as a valid selected compiler context.");
                var nativeSourceFiles = nativeArguments.SourceFiles.Select(file => Path.GetFullPath(file.Path, Path.GetDirectoryName(nativeNode.ProjectPath)!)).ToArray();
                CollectionAssert.Contains(nativeSourceFiles, source.GetPath("Dependency", "Dependency.cs"));
                CollectionAssert.Contains(nativeSourceFiles, source.GetPath("Dependency", "Edge.conditional"),
                    "The actual Csc producer must independently prove the static graph's edge context.");
                Assert.AreEqual(explicitGraphMode, nativeSourceFiles.Contains(source.GetPath("Dependency", "GraphOnly.conditional"), FileSystemPath.Comparer));
                Assert.IsNotNull(nativeArguments.OutputFileName);
                Assert.IsNotNull(nativeNode.TargetPath);
                var effectiveGlobals = NativeCompilerTaskInputs.EffectiveGlobals(nativeNode.Globals, nativeNode.TargetFramework);
                nativeEvaluation = new Microsoft.Build.Evaluation.ProjectCollection();
                var evaluated = nativeEvaluation.LoadProject(nativeNode.ProjectPath, effectiveGlobals, nativeNode.TaskRegistrations.ToolsVersion);
                Assert.AreEqual("selected", evaluated.GetPropertyValue("EdgeFlavor"));
                Assert.AreEqual("", evaluated.GetPropertyValue("Inherited"));
                Assert.AreEqual("net10.0", evaluated.GetPropertyValue("TargetFramework"));
                Assert.AreEqual(sentinel, evaluated.GetPropertyValue("PrivateGraphValue"));
                Assert.AreEqual(explicitGraphMode, evaluated.GetPropertyValue("IsGraphBuild") == "true");
                Assert.AreEqual(nativeNode.TaskRegistrations.ToolsVersion, evaluated.ToolsVersion);
                var intermediateOutputs = evaluated.GetItems("IntermediateAssembly").Select(item => item.GetMetadataValue("FullPath")).ToArray();
                var parsedOutput = Path.GetFullPath(Path.Combine(nativeArguments.OutputDirectory, nativeArguments.OutputFileName));
                var taskOutput = Path.GetFullPath(nativeCapture.Output, Path.GetDirectoryName(nativeNode.ProjectPath)!);
                var finalOutput = evaluated.GetPropertyValue("TargetPath");
                Console.WriteLine("NATIVE-OUTPUT-ROLES: " + JsonSerializer.Serialize(new
                {
                    taskOutput, parsedOutput, intermediateOutputs, finalOutput, nativeNode.TargetPath,
                    nativeNode.TargetFramework, nativeNode.TaskRegistrations.ToolsVersion
                }));
                Assert.HasCount(1, intermediateOutputs, "The selected native evaluation must declare one Csc IntermediateAssembly output.");
                Assert.IsFalse(string.IsNullOrWhiteSpace(intermediateOutputs[0]));
                Assert.IsTrue(FileSystemPath.Comparer.Equals(intermediateOutputs[0], taskOutput),
                    "The actual Csc output receipt must identify the selected intermediate assembly.");
                Assert.IsTrue(FileSystemPath.Comparer.Equals(intermediateOutputs[0], parsedOutput),
                    "The actual Csc /out argument must identify the same intermediate assembly.");
                Assert.IsTrue(FileSystemPath.Comparer.Equals(nativeNode.TargetPath, Path.GetFullPath(finalOutput, Path.GetDirectoryName(nativeNode.ProjectPath)!)),
                    "The final project TargetPath must retain its separate evaluated output role.");
                Assert.AreEqual(explicitGraphMode, rawDependency.Documents.Any(document => document.Name == "GraphOnly.conditional"),
                    "Native semantic graph-mode behavior must be established independently of the edge-context verdict.");
                Assert.AreEqual(!edgeOnlyOverrides, rawDependency.Documents.Any(document => document.Name == "Edge.conditional"),
                    "The independently loaded semantic snapshot must prove whether caller globals match the selected edge context.");
            }
            catch (Exception exception) { nativePrimary = exception; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Raw native edge workspace cleanup failed.", nativePrimary, nativeEvaluation, raw); }
            var status = await WorkspaceManager.LoadAsync(source.SolutionPath, RoslynMcp.Tests.Helpers.OwnedNativeLoadContext.Properties(source.SolutionPath, properties), CancellationToken.None);
            workspaceId = status.WorkspaceId;
            var original = WorkspaceManager.GetCurrentSolution(status.WorkspaceId);
            var dependency = original.Projects.Single(project => project.FilePath == dependencyPath);
            Console.WriteLine("EDGE-DOCUMENTS: " + string.Join(";", dependency.Documents.Select(document => document.Name)));
            Console.WriteLine("EDGE-NODE: " + JsonSerializer.Serialize(EvaluatedBuildGraph.Resolve(dependency, CancellationToken.None).Globals));
            Assert.IsTrue(NativeCompilerContextCertificate.TryGet(original, out var certificate));
            Assert.IsNotNull(certificate);
            Assert.AreEqual(edgeOnlyOverrides ? NativeCompilerContextAvailability.SemanticContextMismatch : NativeCompilerContextAvailability.Certified,
                certificate.Availability, "Compiler authority must follow actual native context agreement, preserving semantic loading on divergence.");
            Assert.AreEqual(!edgeOnlyOverrides, dependency.Documents.Any(document => document.Name == "Edge.conditional"));
            if (edgeOnlyOverrides)
            {
                foreach (var nativeProject in certificate.NativeSolution.Projects)
                {
                    var preserved = original.GetProject(nativeProject.Id);
                    Assert.IsNotNull(preserved);
                    CollectionAssert.AreEqual(nativeProject.DocumentIds.ToArray(), preserved.DocumentIds.ToArray());
                    CollectionAssert.AreEqual(nativeProject.Documents.Select(document => document.FilePath).ToArray(),
                        preserved.Documents.Select(document => document.FilePath).ToArray());
                    CollectionAssert.AreEqual(nativeDocumentPaths[nativeProject.FilePath!],
                        SolutionFileMutationPlanner.AllDocuments(preserved).Select(document => document.FilePath!)
                            .OrderBy(path => path, FileSystemPath.Comparer).ToArray(),
                        "Semantic-only publication must preserve the independently loaded native document path set.");
                }
                var unavailable = Assert.ThrowsExactly<PublicInvalidOperationException>(() => NativeCompilerContextCertificate.Require(original));
                StringAssert.Contains(unavailable.Message, "semantic snapshot differs");
            }
            else await certificate.AssertCurrentAsync(original, status.WorkspaceId, PathPolicy, CancellationToken.None);
            Assert.AreEqual(explicitGraphMode, dependency.Documents.Any(document => document.Name == "GraphOnly.conditional"));
            Assert.AreEqual(explicitGraphMode, original.Projects.Any(project => project.FilePath == graphLeaf));
            var node = EvaluatedBuildGraph.Resolve(dependency, CancellationToken.None);
            Assert.AreEqual("selected", node.Globals["EdgeFlavor"]);
            Assert.IsFalse(node.Globals.ContainsKey("Inherited"));
            Assert.AreEqual("net10.0", node.TargetFramework);
            Assert.AreEqual(sentinel, node.Globals["PrivateGraphValue"]);
            Assert.AreEqual(explicitGraphMode, node.Globals.ContainsKey("IsGraphBuild"));
            var restores = RestoreInvocationPlanner.Capture(original, CancellationToken.None);
            var plan = RestoreInvocationPlanner.Plan(source.SolutionPath, restores);
            Assert.HasCount(explicitGraphMode ? 3 : 2, plan);
            var dependencyRestore = plan.Single(invocation => invocation.TargetPath == dependencyPath);
            CollectionAssert.Contains(dependencyRestore.Arguments.ToArray(), "-p:EdgeFlavor=selected");
            Assert.IsFalse(dependencyRestore.Arguments.Any(argument => argument.StartsWith("-p:Inherited=", StringComparison.OrdinalIgnoreCase)));
            var document = original.Projects.Single(project => project.FilePath == consumerPath).Documents.Single(document => document.FilePath == consumerCode);
            var text = await document.GetTextAsync();
            var modified = original.WithDocumentText(document.Id, SourceText.From(text + "\n// reviewed edge-context edit\n", text.Encoding));
            var changes = await SolutionDiffHelper.ComputeChangesAsync(original, modified, SolutionDiffReadContext.ForWorkspace(WorkspaceManager, status.WorkspaceId), CancellationToken.None);
            var version = WorkspaceManager.GetCurrentVersion(status.WorkspaceId);
            var token = PreviewStore.Store(status.WorkspaceId, original, modified, version, "edge context", changes, PreviewKind.Unspecified);
            var validation = new WorkspaceValidationService(CompileCheckService, DiagnosticService, TestDiscoveryService,
                TestRunnerService, WorkspaceManager, ChangeTracker);
            var forkParent = edgeOnlyOverrides ? source.GetPath("RefusalBoundary", "roslynmcp-forks") : PhysicalGraphNamespace.ForkParent;
            var service = new WorkspaceForkApplyService(PathPolicy, WorkspaceManager, WorkspaceExecutionGate, PreviewStore,
                validation, TestRunnerService, new DotnetCommandRunner(), NullLogger<WorkspaceForkApplyService>.Instance, forkParent, TimeProvider.System);
            if (edgeOnlyOverrides)
            {
                Assert.IsFalse(Directory.Exists(forkParent), "The dedicated refusal-effect boundary must begin absent.");
                var sourceBytes = await File.ReadAllBytesAsync(consumerCode);
                var refusal = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                    service.ApplyAsync(status.WorkspaceId, token, "keep", false, null, null, CancellationToken.None));
                StringAssert.Contains(refusal.Message, "semantic snapshot differs",
                    "Actual fork apply must refuse at the same unavailable native compiler-context boundary.");
                Assert.IsFalse(Directory.Exists(forkParent), "Unavailable compiler authority must refuse before fork namespace effects.");
                CollectionAssert.AreEqual(sourceBytes, await File.ReadAllBytesAsync(consumerCode));
                Assert.AreSame(original, WorkspaceManager.GetCurrentSolution(status.WorkspaceId));
                Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(status.WorkspaceId));
                Assert.IsNotNull(PreviewStore.Retrieve(token));
                return;
            }
            fork = await service.ApplyAsync(status.WorkspaceId, token, "keep", false, null, null, CancellationToken.None);
            Assert.IsTrue(fork.Success);
            var forkSolution = WorkspaceManager.GetCurrentSolution(fork.ForkWorkspaceId!);
            var forkDependency = forkSolution.Projects.Single(project => project.FilePath == Path.Combine(fork.ForkPath, "Dependency", "Dependency.csproj"));
            Assert.IsTrue(forkDependency.Documents.Any(item => item.Name == "Edge.conditional"));
            Assert.AreEqual(explicitGraphMode, forkDependency.Documents.Any(item => item.Name == "GraphOnly.conditional"));
            Assert.AreEqual(explicitGraphMode, forkSolution.Projects.Any(project => project.FilePath == Path.Combine(fork.ForkPath, "GraphLeaf", "GraphLeaf.csproj")));
            var forkNode = EvaluatedBuildGraph.Resolve(forkDependency, CancellationToken.None);
            Assert.AreEqual("selected", forkNode.Globals["EdgeFlavor"]);
            Assert.IsFalse(forkNode.Globals.ContainsKey("Inherited"));
            Assert.AreEqual(sentinel, forkNode.Globals["PrivateGraphValue"]);
            Assert.AreSame(original, WorkspaceManager.GetCurrentSolution(status.WorkspaceId));
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(status.WorkspaceId));
            Assert.IsNotNull(PreviewStore.Retrieve(token));
            Assert.IsFalse(JsonSerializer.Serialize(fork).Contains(sentinel, StringComparison.Ordinal));
            Assert.IsFalse(JsonSerializer.Serialize(fork).Contains(Uri.EscapeDataString(sentinel), StringComparison.Ordinal));
        }
        catch (Exception exception) { failure = exception; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Edge-context fixture cleanup failed.", failure,
                CleanupFailureCollector.FromAction(() => { if (workspaceId is not null) WorkspaceManager.Close(workspaceId); }),
                CleanupFailureCollector.FromAction(() => { if (fork?.ForkWorkspaceId is { } id) WorkspaceManager.Close(id); }),
                CleanupFailureCollector.FromAction(() => { if (fork is not null) TestFixtureFileSystem.DeleteDirectoryIfExists(fork.ForkPath); }),
                () => source is null ? ValueTask.CompletedTask : source.DisposeAsync());
        }
    }

    [TestMethod]
    public void ReviewedImportedDefinitionOverlay_ChangesGraphWithoutChangingSourceBytes()
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(EvaluatedBuildGraphTests), Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var project = Path.Combine(root, "Root.csproj");
            var imported = Path.Combine(root, "Edges.props");
            var dependency = Path.Combine(root, "Dependency.csproj");
            File.WriteAllText(project, """<Project><Import Project="Edges.props" /></Project>""");
            File.WriteAllText(imported, "<Project />");
            File.WriteAllText(dependency, "<Project />");
            var before = File.ReadAllBytes(imported);
            var overlay = System.Text.Encoding.UTF8.GetBytes("""
                <Project><ItemGroup><ProjectReference Include="Dependency.csproj" AdditionalProperties="Flavor=reviewed" /></ItemGroup></Project>
                """);
            var graph = EvaluatedBuildGraph.FromRoots([project], ImmutableDictionary<string, string>.Empty,
                new Dictionary<string, byte[]> { [imported] = overlay }, CancellationToken.None);
            var target = graph.Nodes.Single(node => node.ProjectPath == dependency);
            Assert.AreEqual("reviewed", target.Globals["Flavor"]);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(imported));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Evaluated graph fixture cleanup failed.", primary,
                OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }


    [TestMethod]
    [DataRow("literal%with;value", "literal%with;value", "literal%25with%3Bvalue")]
    [DataRow("encoded%3Bvalue", "encoded;value", "encoded%3Bvalue")]
    [DataRow("nested%253Bvalue", "nested%3Bvalue", "nested%253Bvalue")]
    public void EscapedGlobalContext_RoundTripsEvaluationAndCommandTransport(string captured, string evaluated, string argument)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(EvaluatedBuildGraphTests), Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var project = Path.Combine(root, "Root.csproj");
            File.WriteAllText(project, """<Project><ItemGroup><PackageReference Include="Echo" Version="$(PrivateValue)" /></ItemGroup></Project>""");
            var globals = new Dictionary<string, string> { ["PrivateValue"] = captured };
            var first = EvaluatedBuildGraph.FromRoots([project], globals, ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None).Nodes.Single();
            Assert.AreEqual(captured, first.Globals["PrivateValue"]);
            Assert.AreEqual(evaluated, first.Inputs.Packages.Single().RequestedVersion);
            var replayed = EvaluatedBuildGraph.FromEntries([new(project, first.Globals)], ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None).Nodes.Single();
            Assert.AreEqual(evaluated, replayed.Inputs.Packages.Single().RequestedVersion);
            CollectionAssert.Contains(MsBuildCommandProperties.Append(["restore", project], first.Globals), "-p:PrivateValue=" + argument);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Evaluated graph fixture cleanup failed.", primary,
                OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    [TestMethod]
    public void DistinctEdgeContexts_AreRetainedAndLoadedOutputSelectsOnlyProvenIdentity()
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(EvaluatedBuildGraphTests), Guid.NewGuid().ToString("N"));
        AdhocWorkspace? workspace = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var project = Path.Combine(root, "Root.csproj");
            var dependency = Path.Combine(root, "Dependency.csproj");
            File.WriteAllText(project, """
                <Project><ItemGroup>
                  <ProjectReference Include="Dependency.csproj" AdditionalProperties="Flavor=one" />
                  <ProjectReference Include="Dependency.csproj" AdditionalProperties="Flavor=two" />
                </ItemGroup></Project>
                """);
            File.WriteAllText(dependency, """<Project><PropertyGroup><TargetPath>bin/$(Flavor)/Dependency.dll</TargetPath></PropertyGroup></Project>""");
            var graph = EvaluatedBuildGraph.FromRoots([project], ImmutableDictionary<string, string>.Empty,
                ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None);
            Assert.HasCount(2, graph.Nodes.Where(node => node.ProjectPath == dependency).ToArray());
            workspace = new AdhocWorkspace();
            var id = ProjectId.CreateNewId();
            var solution = workspace.CurrentSolution.AddProject(ProjectInfo.Create(id, VersionStamp.Create(), "Dependency", "Dependency", LanguageNames.CSharp,
                filePath: dependency, outputFilePath: Path.Combine(root, "bin", "two", "Dependency.dll")));
            Assert.AreEqual("two", EvaluatedBuildGraph.Resolve(solution.GetProject(id)!, graph).Globals["Flavor"]);
            var ambiguous = solution.WithProjectOutputFilePath(id, null).GetProject(id)!;
            Assert.ThrowsExactly<PublicInvalidOperationException>(() => EvaluatedBuildGraph.Resolve(ambiguous, graph));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Evaluated graph fixture cleanup failed.", primary, workspace,
                OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    [TestMethod]
    public void GraphEvaluationFailure_DoesNotExposeCapturedValuesOrInnerChain()
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(EvaluatedBuildGraphTests), Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var project = Path.Combine(root, "Broken.csproj");
            File.WriteAllText(project, """<Project><Import Project="$(PrivateImport)" /></Project>""");
            const string sentinel = "synthetic-missing-import-417f";
            var exception = Assert.ThrowsExactly<PublicInvalidOperationException>(() => EvaluatedBuildGraph.FromRoots([project],
                new Dictionary<string, string> { ["PrivateImport"] = sentinel }, ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None));
            Assert.IsFalse(exception.ToString().Contains(sentinel, StringComparison.Ordinal));
            Assert.IsNull(exception.InnerException);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Evaluated graph fixture cleanup failed.", primary,
                OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }
}
