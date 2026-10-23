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
public sealed class NativeCompilerContextCertificateTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CompilerProducer_RejectsAuthoredInputChangedDuringAdditionalNativeExecution(bool mutateArtifact)
    {
        var fixture = await CreateAsync(mutateDuringLoad: false, mutateDuringProducer: true, mutateArtifact: mutateArtifact);
        var policy = new OwnedTestWorkspacePathPolicy();
        Exception? primary = null;
        try
        {
            var loaded = await new WorkspaceSessionLoader().CreateAndOpenAsync("producer-epoch", fixture.Project, OwnedNativeLoadContext.Properties(fixture.Project, new Dictionary<string, string>()), policy,
                new WorkspaceDiagnosticsSink(64), NullLogger.Instance, SupplementalPackageReadAuthority.None, null, CancellationToken.None);
            Exception? loadedPrimary = null;
            try
            {
                var artifact = Path.Combine(fixture.Root, "Artifact.dll");
                var before = mutateArtifact ? await File.ReadAllBytesAsync(artifact) : [];
                NativeBuildMembership CaptureMembership() => NativeBuildMembership.Capture(
                    EvaluatedBuildGraph.FromRoots([fixture.Project],
                        OwnedNativeLoadContext.Properties(fixture.Project, new Dictionary<string, string>()),
                        System.Collections.Immutable.ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None), CancellationToken.None);
                var membershipBeforeTrigger = CaptureMembership();
                var triggerDirectory = Path.Combine(fixture.Root, "obj");
                Assert.IsTrue(Directory.Exists(triggerDirectory), "Restore must create the owned intermediate directory before certification.");
                await File.WriteAllTextAsync(Path.Combine(triggerDirectory, "producer-start.marker"), "owned-fixture");
                Assert.IsTrue(membershipBeforeTrigger.EquivalentTo(CaptureMembership()),
                    "Execution-only trigger must not change the captured native evaluated membership.");
                var failure = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                    EvaluatedProjectGraph.WithPersistedReferenceMetadataAsync("producer-epoch", loaded.Context, policy, CancellationToken.None));
                StringAssert.Contains(failure.Message, "Physical execution inputs changed");
                if (mutateArtifact)
                {
                    var after = await File.ReadAllBytesAsync(artifact);
                    CollectionAssert.AreNotEqual(before, after);
                    CollectionAssert.AreEqual(await File.ReadAllBytesAsync(Path.Combine(fixture.Root, "Replacement.dll")), after);
                }
                else StringAssert.Contains(await File.ReadAllTextAsync(fixture.Props), "after");
            }
            catch (Exception failure) { loadedPrimary = failure; throw; }
            finally { OwnedResourceCleanup.Dispose("Producer epoch load cleanup failed.", loadedPrimary, loaded.Workspace, loaded.Lease); }
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Producer epoch fixture cleanup failed.", primary,
            CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(fixture.Root)));
        }
    }

    [TestMethod]
    public async Task InvalidAssets_LoadsWithoutMutationAuthority_ExplicitKnownRootRepairAndReloadCertifies()
    {
        var fixture = await CreateAsync(mutateDuringLoad: false, mutateDuringProducer: false, mutateArtifact: false);
        Exception? primary = null;
        WorkspaceManager? manager = null;
        FileWatcherService? watcher = null;
        try
        {
            var assets = Path.Combine(fixture.Root, "obj", "project.assets.json");
            var knownRoot = RestoreStalenessDetector.ReadRequiredRestorePackagesPathFromAssets(assets, CancellationToken.None);
            Assert.IsNotNull(knownRoot);
            Assert.IsFalse(string.IsNullOrWhiteSpace(knownRoot));
            await File.WriteAllTextAsync(assets, "{invalid-assets");
            var policy = new OwnedTestWorkspacePathPolicy();
            watcher = new FileWatcherService(NullLogger<FileWatcherService>.Instance);
            manager = new WorkspaceManager(new RoslynMcp.Roslyn.Services.ForkInvocationPathPolicy(policy), NullLogger<WorkspaceManager>.Instance,
                new PreviewStore(), watcher,
                new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 4 });
            var degradedStatus = await manager.LoadAsync(fixture.Project, OwnedNativeLoadContext.Properties(fixture.Project, new Dictionary<string, string>()), CancellationToken.None);
            Assert.IsTrue(degradedStatus.RestoreRequired);
            Assert.HasCount(1, manager.GetCurrentSolution(degradedStatus.WorkspaceId).ProjectIds);
            var unavailableDiagnostic = degradedStatus.WorkspaceDiagnostics.Single(diagnostic =>
                diagnostic.Id == "WORKSPACE_COMPILER_CONTEXT_UNAVAILABLE");
            StringAssert.Contains(unavailableDiagnostic.Message, "--packages <known-root>");
            StringAssert.Contains(unavailableDiagnostic.Message, "--force");
            StringAssert.Contains(unavailableDiagnostic.Message, "remove that exact file first");
            Assert.ThrowsExactly<PublicInvalidOperationException>(() =>
                NativeCompilerContextCertificate.Require(manager.GetCurrentSolution(degradedStatus.WorkspaceId)));
            var degraded = await new WorkspaceSessionLoader().CreateAndOpenAsync("invalid-assets", fixture.Project, OwnedNativeLoadContext.Properties(fixture.Project, new Dictionary<string, string>()), policy,
                new WorkspaceDiagnosticsSink(64), NullLogger.Instance, SupplementalPackageReadAuthority.None, null, CancellationToken.None);
            Exception? degradedPrimary = null;
            try
            {
                Assert.AreEqual(NativeCompilerContextAvailability.InvalidRestoreAssets, degraded.Context.Availability);
                var normalized = (await EvaluatedProjectGraph.WithPersistedReferenceMetadataAsync("invalid-assets", degraded.Context, policy, CancellationToken.None)).Solution;
                Assert.HasCount(1, normalized.ProjectIds, "Malformed restore artifacts must retain degraded semantic loading.");
                var refusal = Assert.ThrowsExactly<PublicInvalidOperationException>(() => NativeCompilerContextCertificate.Require(normalized));
                StringAssert.Contains(refusal.Message, "--packages <known-root>");
            }
            catch (Exception failure) { degradedPrimary = failure; throw; }
            finally { OwnedResourceCleanup.Dispose("Degraded native load cleanup failed.", degradedPrimary, degraded.Workspace, degraded.Lease); }
            Assert.AreEqual("{invalid-assets", await File.ReadAllTextAsync(assets));
            File.Delete(assets);
            var restored = await new DotnetCommandRunner().RunAsync(fixture.Root, fixture.Project,
                MsBuildCommandProperties.Append(["restore", fixture.Project, "--force", "--nologo", "--packages", knownRoot],
                    OwnedNativeLoadContext.Properties(fixture.Project, new Dictionary<string, string>())), CancellationToken.None);
            Assert.IsTrue(restored.Succeeded, restored.StdErr + restored.StdOut);
            Assert.IsTrue(File.Exists(assets));
            var repairedStatus = await manager.ReloadAsync(degradedStatus.WorkspaceId, CancellationToken.None);
            Assert.IsTrue(NativeCompilerContextCertificate.TryGet(manager.GetCurrentSolution(repairedStatus.WorkspaceId), out var repairedContext));
            Assert.IsFalse(repairedStatus.RestoreRequired, $"Context: {repairedContext?.Availability}; diagnostics: {string.Join(", ", repairedStatus.WorkspaceDiagnostics.Select(diagnostic => diagnostic.Id))}");
            Assert.IsFalse(repairedStatus.WorkspaceDiagnostics.Any(diagnostic =>
                diagnostic.Id == "WORKSPACE_COMPILER_CONTEXT_UNAVAILABLE"));
            _ = NativeCompilerContextCertificate.Require(manager.GetCurrentSolution(repairedStatus.WorkspaceId));
            var repaired = await new WorkspaceSessionLoader().CreateAndOpenAsync("repaired-assets", fixture.Project, OwnedNativeLoadContext.Properties(fixture.Project, new Dictionary<string, string>()), policy,
                new WorkspaceDiagnosticsSink(64), NullLogger.Instance, SupplementalPackageReadAuthority.None, null, CancellationToken.None);
            Exception? repairedPrimary = null;
            try
            {
                Assert.AreEqual(NativeCompilerContextAvailability.Certified, repaired.Context.Availability);
                var normalized = (await EvaluatedProjectGraph.WithPersistedReferenceMetadataAsync("repaired-assets", repaired.Context, policy, CancellationToken.None)).Solution;
                Assert.AreSame(repaired.Context, NativeCompilerContextCertificate.Require(normalized));
                Assert.AreEqual(knownRoot, RestoreStalenessDetector.ReadRequiredRestorePackagesPathFromAssets(assets, CancellationToken.None));
            }
            catch (Exception failure) { repairedPrimary = failure; throw; }
            finally { OwnedResourceCleanup.Dispose("Repaired native load cleanup failed.", repairedPrimary, repaired.Workspace, repaired.Lease); }
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Invalid assets fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => manager?.Dispose()),
                CleanupFailureCollector.FromAction(() => { if (manager is null) watcher?.Dispose(); }),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(fixture.Root)));
        }
    }

    [TestMethod]
    public async Task OwnedLoad_RejectsAuthoredInputChangedDuringNativeEvaluation()
    {
        var fixture = await CreateAsync(mutateDuringLoad: true, mutateDuringProducer: false, mutateArtifact: false);
        Exception? primary = null;
        try
        {
            var failure = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => new WorkspaceSessionLoader()
                .CreateAndOpenAsync("native-epoch", fixture.Project, OwnedNativeLoadContext.Properties(fixture.Project, new Dictionary<string, string>()), new OwnedTestWorkspacePathPolicy(),
                    new WorkspaceDiagnosticsSink(64), NullLogger.Instance, SupplementalPackageReadAuthority.None, null, CancellationToken.None));
            StringAssert.Contains(failure.Message, "Physical execution inputs changed");
            StringAssert.Contains(await File.ReadAllTextAsync(fixture.Props), "after");
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Native epoch fixture cleanup failed.", primary,
            CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(fixture.Root)));
        }
    }

    [TestMethod]
    public async Task PreviewClone_CannotMintNativeContextAuthority_AndChangedLoadEpochIsRejected()
    {
        var fixture = await CreateAsync(mutateDuringLoad: false, mutateDuringProducer: false, mutateArtifact: false);
        var policy = new OwnedTestWorkspacePathPolicy();
        Exception? rootPrimary = null;
        try
        {
            var loaded = await new WorkspaceSessionLoader().CreateAndOpenAsync("native-clone", fixture.Project, OwnedNativeLoadContext.Properties(fixture.Project, new Dictionary<string, string>()), policy,
                new WorkspaceDiagnosticsSink(64), NullLogger.Instance, SupplementalPackageReadAuthority.None, null, CancellationToken.None);
            Exception? primary = null;
            try
            {
                var original = (await EvaluatedProjectGraph.WithPersistedReferenceMetadataAsync("native-clone", loaded.Context, policy, CancellationToken.None)).Solution;
                var owner = original.Projects.Single();
                Task Approve(IReadOnlyList<string> paths, CancellationToken ct) => policy.ValidateAsync("native-clone", paths, ct);
                async Task<byte[]?> ReadCurrent(string path, CancellationToken ct)
                {
                    await Approve([path], ct);
                    return await FileSnapshotCapture.ReadIfPresentAsync(path, File.ReadAllBytesAsync, ct);
                }
                var actual = await RestoredCompilerInputAuthority.CaptureReferenceAuthorityAsync(original, [owner.Id],
                    ReadCurrent, (digestPath, digestToken) => FileContentDigest.FromReaderAsync(digestPath, ReadCurrent, digestToken), Approve, CancellationToken.None);
                actual.AssertCurrent(owner);
                var clone = original.WithProjectOutputFilePath(owner.Id, Path.Combine(fixture.Root, "forged", "Native.dll"));
                var accesses = 0;
                Task<byte[]?> Read(string _, CancellationToken __) { accesses++; return Task.FromResult<byte[]?>(null); }
                Task Authorize(IReadOnlyList<string> _, CancellationToken __) { accesses++; return Task.CompletedTask; }
                await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => RestoredCompilerInputAuthority.CaptureReferenceAuthorityAsync(
                    clone, [owner.Id], Read, (digestPath, digestToken) => FileContentDigest.FromReaderAsync(digestPath, Read, digestToken), Authorize, CancellationToken.None));
                Assert.AreEqual(0, accesses, "A caller-created clone cannot authorize reads or mint an evaluated native receipt.");
                await File.WriteAllTextAsync(fixture.Props, "<Project><PropertyGroup><NativeEpochMarker>changed</NativeEpochMarker></PropertyGroup></Project>");
                await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => RestoredCompilerInputAuthority.CaptureReferenceAuthorityAsync(
                    original, [owner.Id], ReadCurrent, (digestPath, digestToken) => FileContentDigest.FromReaderAsync(digestPath, ReadCurrent, digestToken), Approve, CancellationToken.None));
            }
            catch (Exception failure) { primary = failure; throw; }
            finally { OwnedResourceCleanup.Dispose("Native context fixture disposal failed.", primary, loaded.Workspace, loaded.Lease); }
        }
        catch (Exception failure) { rootPrimary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Native context fixture directory cleanup failed.", rootPrimary,
            CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(fixture.Root)));
        }
    }

    private static async Task<(string Root, string Project, string Props)> CreateAsync(bool mutateDuringLoad, bool mutateDuringProducer, bool mutateArtifact)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(NativeCompilerContextCertificateTests), Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(Path.Combine(root, "global.json"), "{}");
            var props = Path.Combine(root, "Directory.Build.props");
            await File.WriteAllTextAsync(props, "<Project><PropertyGroup><NativeEpochMarker>before</NativeEpochMarker></PropertyGroup></Project>");
            var project = new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0"), new XElement("NuGetAudit", "false")));
            if (mutateDuringProducer)
            {
                var dependency = Path.Combine(root, "Dependency");
                Directory.CreateDirectory(dependency);
                await File.WriteAllTextAsync(Path.Combine(dependency, "Dependency.csproj"),
                    "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit></PropertyGroup></Project>");
                await File.WriteAllTextAsync(Path.Combine(dependency, "Dependency.cs"), "public class Dependency { }");
                project.Add(new XElement("ItemGroup", new XElement("ProjectReference", new XAttribute("Include", "Dependency/Dependency.csproj")),
                    new XElement("Compile", new XAttribute("Remove", "Dependency/**"))));
            }
            if (mutateArtifact)
            {
                await EmitArtifactAsync(Path.Combine(root, "Artifact.dll"), 1);
                await EmitArtifactAsync(Path.Combine(root, "Replacement.dll"), 2);
                project.Add(new XElement("ItemGroup", new XElement("Reference", new XAttribute("Include", "CompilerEpochArtifact"),
                    new XElement("HintPath", "Artifact.dll"))));
            }
            if (mutateDuringLoad || mutateDuringProducer)
                project.Add(new XElement("Target", new XAttribute("Name", "MutateAuthoredCompilerInput"),
                    new XAttribute("BeforeTargets", "CoreCompile"), new XAttribute("Condition", "'$(DesignTimeBuild)' == 'true'" + (mutateDuringProducer ? " and Exists('$(MSBuildProjectDirectory)/obj/producer-start.marker')" : "")),
                    mutateArtifact
                        ? new XElement("Copy", new XAttribute("SourceFiles", "$(MSBuildProjectDirectory)/Replacement.dll"),
                            new XAttribute("DestinationFiles", "$(MSBuildProjectDirectory)/Artifact.dll"))
                        : new XElement("WriteLinesToFile", new XAttribute("File", "$(MSBuildProjectDirectory)/Directory.Build.props"),
                            new XAttribute("Lines", "<Project><PropertyGroup><NativeEpochMarker>after</NativeEpochMarker></PropertyGroup></Project>"),
                            new XAttribute("Overwrite", "true"))));
            var path = Path.Combine(root, "Native.csproj");
            await File.WriteAllTextAsync(path, project.ToString());
            await File.WriteAllTextAsync(Path.Combine(root, "Native.cs"), "public class Native { }");
            var restore = await new DotnetCommandRunner().RunAsync(root, path, MsBuildCommandProperties.Append(["restore", path, "--nologo"],
                OwnedNativeLoadContext.Properties(path, new Dictionary<string, string>())), CancellationToken.None);
            Assert.IsTrue(restore.Succeeded, restore.StdErr + restore.StdOut);
            return (root, path, props);
        }
        catch (Exception failure)
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Native context fixture preparation cleanup failed.", failure,
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
            throw;
        }
    }
    private static async Task EmitArtifactAsync(string path, int value)
    {
        var compilation = CSharpCompilation.Create("CompilerEpochArtifact",
            [CSharpSyntaxTree.ParseText("public class Artifact { public const int Value = " + value + "; }")],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)], new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var bytes = new MemoryStream();
        var emitted = compilation.Emit(bytes);
        Assert.IsTrue(emitted.Success, string.Join("\n", emitted.Diagnostics));
        await File.WriteAllBytesAsync(path, bytes.ToArray());
    }

}
