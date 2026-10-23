using System.Text;
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
public sealed class PhysicalGraphContentReplayTests
{
    [TestMethod]
    public async Task PackageTargetCompileWithDefaultGlobRemoval_PreservesOneDocumentAndExternalImport()
    {
        var targets = Encoding.UTF8.GetBytes("""
            <Project>
              <ItemGroup><Compile Include="$(MSBuildThisFileDirectory)../sources/Input.cs" /></ItemGroup>
            </Project>
            """);
        NuGetInstallationFixture? fixture = null;
        WorkspaceManager? manager = null;
        FileWatcherService? watcher = null;
        PhysicalGraphStage? stage = null;
        Exception? primary = null;
        try
        {
            fixture = await NuGetInstallationFixture.CreateAsync("Owned.CompileReplay", "1.0.0", new Dictionary<string, byte[]>
            {
                ["sources/Input.cs"] = Encoding.UTF8.GetBytes("public class PackageInput { public const int Value = 7; }"),
                ["buildTransitive/Owned.CompileReplay.targets"] = targets
            });
            var project = Path.Combine(fixture.Root, "Owner.csproj");
            var input = Path.Combine(fixture.Cache, "owned.compilereplay", "1.0.0", "sources", "Input.cs");
            // The package targets owns the Compile inclusion. Remove the SDK's
            // relative glob item explicitly before that later external import.
            var projectXml = XDocument.Load(project);
            projectXml.Root!.Add(new XElement("ItemGroup", new XElement("Compile",
                new XAttribute("Remove", Path.GetRelativePath(fixture.Root, input)))));
            await File.WriteAllTextAsync(project, projectXml.ToString());
            await File.WriteAllTextAsync(Path.Combine(fixture.Root, "Entry.cs"), "public class Entry { public int Read() => PackageInput.Value; }");
            var globals = OwnedNativeLoadContext.Properties(project, new Dictionary<string, string>());
            var restored = await new DotnetCommandRunner().RunAsync(fixture.Root, project,
                MsBuildCommandProperties.Append(["restore", project, "--nologo", "--source", Path.Combine(fixture.Root, "feed"), "--packages", fixture.Cache], globals), CancellationToken.None);
            Assert.IsTrue(restored.Succeeded, "The controlled package must be restored before testing native item removal.");
            var policy = new ForkInvocationPathPolicy(new OwnedTestWorkspacePathPolicy());
            watcher = new FileWatcherService(NullLogger<FileWatcherService>.Instance);
            manager = new WorkspaceManager(policy, NullLogger<WorkspaceManager>.Instance, new PreviewStore(),
                watcher, new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 4 });
            var status = await manager.LoadAsync(project, globals, CancellationToken.None);
            var source = manager.GetCurrentSolution(status.WorkspaceId);
            var sourceProject = source.Projects.Single();
            Assert.AreEqual(1, sourceProject.Documents.Count(document => FileSystemPath.Comparer.Equals(document.FilePath, input)),
                "The source graph must retain only the external target inclusion after removing the SDK glob item.");
            var sourceCompilation = await sourceProject.GetCompilationAsync();
            Assert.IsNotNull(sourceCompilation);
            Assert.IsFalse(sourceCompilation.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
            var layout = await PhysicalGraphLayout.CreateAsync(status.WorkspaceId, project, source, policy, CancellationToken.None);
            var importedTargets = layout.Packages.Files.Single(path => path.EndsWith("buildTransitive/Owned.CompileReplay.targets".Replace('/', Path.DirectorySeparatorChar), StringComparison.Ordinal));
            Assert.IsTrue(layout.ContentReplayFiles.Contains(input, FileSystemPath.Comparer));
            stage = await PhysicalGraphStage.CreateAsync(status.WorkspaceId, project, source, layout, [], globals,
                new WorkspaceSessionLoader(), policy,
                ((IWorkspaceEvaluationLoader)manager).CaptureStageInvocationAuthority(status.WorkspaceId, source), NullLogger.Instance, CancellationToken.None);
            stage.EnterReadAuthority();
            var stagedProject = stage.Expected.Solution.Projects.Single();
            var stagedInputs = stagedProject.Documents.Where(document => document.FilePath is { } path
                && FileSystemPath.Comparer.Equals(stage.Expected.MapPath(path), input)).ToArray();
            Assert.AreEqual(1, stagedInputs.Length, "Replay must preserve the authored glob removal and external target inclusion.");
            Assert.AreEqual("public class PackageInput { public const int Value = 7; }", (await stagedInputs[0].GetTextAsync()).ToString());
            var stagedCompilation = await stagedProject.GetCompilationAsync();
            Assert.IsNotNull(stagedCompilation);
            Assert.IsFalse(stagedCompilation.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
            var imported = EvaluatedBuildGraph.Source(stage.Expected.Solution, CancellationToken.None).Nodes.SelectMany(node => node.BuildInputs.Definitions);
            Assert.IsTrue(imported.Contains(importedTargets, FileSystemPath.Comparer), "The actual package import must remain external.");
            CollectionAssert.AreEqual(targets, await File.ReadAllBytesAsync(importedTargets));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Content replay fixture cleanup failed.", primary,
                stage, manager, manager is null ? watcher : null, fixture);
        }
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public Task PackageFilesWithNativeItemRoles_PreservePrivateCandidatesAndExternalImports(bool cacheInsideProject)
        => PackageFilesWithNativeItemRoles_PreservePrivateCandidatesAndExternalImports(cacheInsideProject, ItemRoles.DefaultGlob);

    private enum ItemRoles { DefaultGlob, AbsoluteOnly, Mixed }

    private static async Task PackageFilesWithNativeItemRoles_PreservePrivateCandidatesAndExternalImports(bool cacheInsideProject, ItemRoles roles)
    {
        const string id = "Owned.Replay";
        var targets = Encoding.UTF8.GetBytes("""
            <Project>
              <ItemGroup><Reference Include="Owned.Target"><HintPath>$(MSBuildThisFileDirectory)../candidates/99/Owned.Target.dll</HintPath></Reference></ItemGroup>
            </Project>
            """);
        NuGetInstallationFixture? fixture = null;
        WorkspaceManager? manager = null;
        FileWatcherService? watcher = null;
        PhysicalGraphStage? stage = null;
        Exception? primary = null;
        try
        {
            fixture = await NuGetInstallationFixture.CreateAsync(id, "1.0.0", new Dictionary<string, byte[]>
            {
                ["candidates/00/Owned.Target.dll"] = Emit(1),
                ["candidates/99/Owned.Target.dll"] = Emit(2),
                ["buildTransitive/Owned.Replay.targets"] = targets
            });
            var project = Path.Combine(fixture.Root, "Owner.csproj");
            if (!cacheInsideProject)
            {
                var directory = Path.Combine(fixture.Root, "project");
                Directory.CreateDirectory(directory);
                var nested = Path.Combine(directory, "Owner.csproj");
                File.Move(project, nested);
                project = nested;
            }
            var missing = Path.Combine(Path.GetDirectoryName(project)!, "missing-native-item.flag");
            var xml = XDocument.Load(project);
            if (roles != ItemRoles.DefaultGlob)
            {
                xml.Root!.Add(new XElement("PropertyGroup", new XElement("EnableDefaultNoneItems", "false")));
                xml.Root.Add(new XElement("ItemGroup", new XElement("None", new XAttribute("Include",
                    Path.Combine(fixture.Cache, "owned.replay", "1.0.0", "candidates", "00", "Owned.Target.dll")))));
                if (roles == ItemRoles.Mixed)
                    xml.Root.Add(new XElement("ItemGroup", new XElement("Content", new XAttribute("Include",
                        Path.GetRelativePath(Path.GetDirectoryName(project)!, Path.Combine(fixture.Cache,
                            "owned.replay", "1.0.0", "buildTransitive", "Owned.Replay.targets"))))));
            }
            xml.Root!.Add(new XElement("ItemGroup", new XElement("None", new XAttribute("Include", missing))));
            await File.WriteAllTextAsync(project, xml.ToString());
            await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(project)!, "Entry.cs"), "public class Entry { }");
            var globals = OwnedNativeLoadContext.Properties(project, new Dictionary<string, string>());
            var restored = await new DotnetCommandRunner().RunAsync(Path.GetDirectoryName(project)!, project,
                MsBuildCommandProperties.Append(["restore", project, "--nologo", "--source", Path.Combine(fixture.Root, "feed"), "--packages", fixture.Cache], globals), CancellationToken.None);
            Assert.IsTrue(restored.Succeeded, "The actual owned package must be restored before native replay.");
            var grants = new RevocablePathPolicy();
            var policy = new ForkInvocationPathPolicy(grants);
            watcher = new FileWatcherService(NullLogger<FileWatcherService>.Instance);
            manager = new WorkspaceManager(policy, NullLogger<WorkspaceManager>.Instance, new PreviewStore(),
                watcher, new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 4 });
            var status = await manager.LoadAsync(project, globals, CancellationToken.None);
            var source = manager.GetCurrentSolution(status.WorkspaceId);
            var selected = source.Projects.Single().MetadataReferences.OfType<PortableExecutableReference>()
                .Single(reference => Path.GetFileName(reference.FilePath) == "Owned.Target.dll").FilePath!;
            StringAssert.Contains(selected.Replace('\\', '/'), cacheInsideProject ? "/candidates/00/" : "/candidates/99/");
            var layout = await PhysicalGraphLayout.CreateAsync(status.WorkspaceId, project, source, policy, CancellationToken.None);
            var importedTargets = layout.Packages.Files.Single(path => path.EndsWith("buildTransitive/Owned.Replay.targets".Replace('/', Path.DirectorySeparatorChar), StringComparison.Ordinal));
            var replayImports = cacheInsideProject && roles != ItemRoles.AbsoluteOnly;
            Assert.AreEqual(replayImports, layout.ContentReplayFiles.Contains(importedTargets, FileSystemPath.Comparer));
            Assert.IsTrue(layout.IsImmutablePath(importedTargets), "A content role must not erase the package-origin classification.");
            Assert.IsFalse(layout.DefinitionFiles.Contains(importedTargets, FileSystemPath.Comparer));
            stage = await PhysicalGraphStage.CreateAsync(status.WorkspaceId, project, source, layout, [], globals,
                new WorkspaceSessionLoader(), policy,
                ((IWorkspaceEvaluationLoader)manager).CaptureStageInvocationAuthority(status.WorkspaceId, source), NullLogger.Instance, CancellationToken.None);
            stage.EnterReadAuthority();
            var stagedReference = stage.Expected.Solution.Projects.Single().MetadataReferences.OfType<PortableExecutableReference>()
                .Single(reference => Path.GetFileName(reference.FilePath) == "Owned.Target.dll").FilePath!;
            Assert.IsTrue(FileSystemPath.Comparer.Equals(selected, stage.Expected.MapPath(stagedReference)));
            var imported = EvaluatedBuildGraph.Source(stage.Expected.Solution, CancellationToken.None).Nodes.SelectMany(node => node.BuildInputs.Definitions);
            Assert.IsTrue(imported.Contains(importedTargets, FileSystemPath.Comparer), "The real native package import must remain external.");
            if (replayImports)
            {
                var copiedTargets = stage.MapFromSource(importedTargets);
                Assert.AreNotEqual(importedTargets, copiedTargets);
                CollectionAssert.AreEqual(targets, await File.ReadAllBytesAsync(copiedTargets), "A package file copied as None must not be rendered as an authored definition.");
            }
            else Assert.AreEqual(importedTargets, stage.MapFromSource(importedTargets));
            Assert.AreEqual(missing, stage.ReplayAuthority.MapActual(missing), "A nonmember path must never gain replay equivalence.");
            var unlistedPackagePath = Path.Combine(fixture.Cache, "owned.replay", "1.0.0", "unlisted.dll");
            Assert.AreEqual(unlistedPackagePath, stage.ReplayAuthority.MapActual(unlistedPackagePath),
                "Being inside an admitted package directory does not admit a file.");
            if (cacheInsideProject)
            {
                var replaySource = layout.ContentReplayFiles.Single(path => FileSystemPath.Comparer.Equals(path, selected));
                var replayCopy = stage.ReplayAuthority.MapActual(replaySource);
                Assert.AreNotEqual(replaySource, replayCopy);
                var originalBytes = await File.ReadAllBytesAsync(replayCopy);
                await stage.ReplayAuthority.AssertCurrentAsync(status.WorkspaceId, policy, CancellationToken.None);
                Exception? replayFailure = null;
                try
                {
                    await File.WriteAllBytesAsync(replayCopy, [0, 1, 2, 3]);
                    await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                        stage.ReplayAuthority.AssertCurrentAsync(status.WorkspaceId, policy, CancellationToken.None));
                }
                catch (Exception failure) { replayFailure = failure; throw; }
                finally
                {
                    await CleanupFailureCollector.RunAfterFailureAsync("Replay copy byte restoration failed.", replayFailure,
                        async () => { await File.WriteAllBytesAsync(replayCopy, originalBytes); });
                }
                await stage.ReplayAuthority.AssertCurrentAsync(status.WorkspaceId, policy, CancellationToken.None);
                grants.DeniedPath = replaySource;
                try
                {
                    await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() =>
                        stage.ReplayAuthority.AssertCurrentAsync(status.WorkspaceId, policy, CancellationToken.None));
                    Assert.AreEqual(1, grants.Refusals, "The original package item must retain its current-grant requirement.");
                }
                finally { grants.DeniedPath = null; }
                await stage.ReplayAuthority.AssertCurrentAsync(status.WorkspaceId, policy, CancellationToken.None);
            }
            Assert.IsTrue(stage.Expected.BuildInputs.Any(input => FileSystemPath.Comparer.Equals(input.FilePath, missing) && !input.Exists));
            await File.WriteAllTextAsync(missing, "now-present");
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => stage.Expected.AssertBuildInputsAsync(status.WorkspaceId, policy, CancellationToken.None));
            CollectionAssert.AreEqual(targets, await File.ReadAllBytesAsync(importedTargets));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Content replay fixture cleanup failed.", primary,
                stage, manager, manager is null ? watcher : null, fixture);
        }
    }

    [TestMethod]
    public async Task AbsolutePackageItems_PreserveExternalImportAndReference()
    {
        // Disable default globs so the native candidate is an explicit absolute item.
        await PackageFilesWithNativeItemRoles_PreservePrivateCandidatesAndExternalImports(true, ItemRoles.AbsoluteOnly);
    }

    [TestMethod]
    public async Task MixedAbsoluteNoneAndRelativeContent_PreserveExternalImportAndPrivateCopy()
        => await PackageFilesWithNativeItemRoles_PreservePrivateCandidatesAndExternalImports(true, ItemRoles.Mixed);

    private sealed class RevocablePathPolicy : IWorkspacePathPolicy
    {
        internal string? DeniedPath { get; set; }
        internal int Refusals { get; private set; }
        public Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        {
            if (DeniedPath is { } denied && paths.Contains(denied, FileSystemPath.Comparer))
            {
                Refusals++;
                throw new UnauthorizedAccessException("The content replay source grant was revoked.");
            }
            return new OwnedTestWorkspacePathPolicy().ValidateAsync(workspaceId, paths, ct);
        }
    }

    private static byte[] Emit(int value)
    {
        var compilation = CSharpCompilation.Create("Owned.Target", [CSharpSyntaxTree.ParseText($"public class Target {{ public const int Value = {value}; }}")],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)], new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var image = new MemoryStream();
        var result = compilation.Emit(image);
        Assert.IsTrue(result.Success, "The candidate assembly must be a real valid PE.");
        return image.ToArray();
    }
}
