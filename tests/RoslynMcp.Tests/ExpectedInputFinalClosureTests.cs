using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ExpectedInputFinalClosureTests
{
    [TestMethod]
    public async Task CommitRestoration_FinalCandidateGrantCannotChangeCopiedOnlyInput()
    {
        NuGetInstallationFixture? fixture = null;
        WorkspaceManager? manager = null;
        FileWatcherService? watcher = null;
        IWorkspaceMutationLease? lease = null;
        IPhysicalRestorationAuthority? staged = null;
        Exception? primary = null;
        try
        {
            fixture = await NuGetInstallationFixture.CreateAsync("Owned.ExpectedClosure", "1.0.0",
                new Dictionary<string, byte[]> { ["marker.txt"] = "owned package"u8.ToArray() });
            var project = Path.Combine(fixture.Root, "Owner.csproj");
            var source = Path.Combine(fixture.Root, "source", "Entry.cs");
            var opaque = Path.Combine(fixture.Root, "opaque", "Unimported.props");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            Directory.CreateDirectory(Path.GetDirectoryName(opaque)!);
            var before = "public class Entry { }"u8.ToArray();
            var desired = "public class Entry { public int Value => 1; }"u8.ToArray();
            var opaqueBefore = "<Project><PropertyGroup><Unused>before</Unused></PropertyGroup></Project>"u8.ToArray();
            var opaqueAfter = "<Project><PropertyGroup><Unused>after</Unused></PropertyGroup></Project>"u8.ToArray();
            await File.WriteAllBytesAsync(source, before);
            await File.WriteAllBytesAsync(opaque, opaqueBefore);
            var definition = XDocument.Load(project);
            definition.Root!.Add(new XElement("PropertyGroup", new XElement("EnableDefaultItems", "false")),
                new XElement("ItemGroup", new XElement("Compile", new XAttribute("Include", "source/Entry.cs"))));
            await File.WriteAllTextAsync(project, definition.ToString());
            var policy = new MutateFinalGrantPolicy(new OwnedTestWorkspacePathPolicy(), project, source, opaque, opaqueAfter);
            var detector = new ArmAfterActualRestoreInspection(policy, project, source, opaque);
            watcher = new FileWatcherService(NullLogger<FileWatcherService>.Instance);
            manager = new WorkspaceManager(new ForkInvocationPathPolicy(policy), NullLogger<WorkspaceManager>.Instance,
                new PreviewStore(), watcher, new WorkspaceManagerOptions(), cacheStore: null, sessionLoader: null,
                restoreStalenessDetector: detector);
            var status = await manager.LoadAsync(project, fixture.LoadProperties, CancellationToken.None);
            var snapshot = manager.GetCurrentSolution(status.WorkspaceId);
            Assert.AreEqual(NativeCompilerContextAvailability.Certified, NativeCompilerContextCertificate.Require(snapshot).Availability);
            var version = manager.GetCurrentVersion(status.WorkspaceId);
            var authority = (IWorkspaceRecoveryAuthority)manager;
            var captured = FileSnapshotDto.FromExistingBytes(source, before);
            var operation = await authority.CaptureRecoveryAsync(status.WorkspaceId, "Expected copied input closure", [captured], CancellationToken.None);
            lease = await authority.BeginMutationAsync(operation, CancellationToken.None);
            var intended = FileSnapshotDto.FromExistingBytes(source, desired);
            lease.BeforeWrite(intended);
            staged = await authority.StageRestorationAsync(operation, [intended], operation.CapturedLayout, CancellationToken.None);
            Assert.IsTrue(staged.Expected.BuildInputs.Any(input => FileSystemPath.Comparer.Equals(input.FilePath, opaque)),
                "The real copied inventory must retain the otherwise unimported file.");
            await PhysicalDocumentSetFileSystem.Instance.TransitionAsync(source, before, desired, operation, authority, CancellationToken.None);
            CollectionAssert.AreEqual(desired, await File.ReadAllBytesAsync(source));
            detector.CommitPhase = true;
            Exception? observed = null;
            try { await authority.CommitRestorationAsync(operation, staged.Expected, retainedRecovery: false, CancellationToken.None); }
            catch (Exception failure) { observed = failure; }

            Assert.IsTrue(detector.SawCandidateWithoutOpaque,
                "The actual candidate certificate must exclude the copied-only input before the actor is armed.");
            Assert.AreEqual(1, policy.Mutations, "The actor must run in a real final candidate grant after restore inspection.");
            CollectionAssert.AreEqual(opaqueAfter, await File.ReadAllBytesAsync(opaque));
            Assert.IsInstanceOfType<PublicInvalidOperationException>(observed,
                $"The separately frozen copied input must refuse publication after the later candidate callback mutates it. FinalGrantContainedOpaque={policy.ObservedGrantContainedOpaque}");
            Assert.AreSame(snapshot, manager.GetCurrentSolution(status.WorkspaceId));
            Assert.AreEqual(version, manager.GetCurrentVersion(status.WorkspaceId));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Expected input final closure fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => staged?.Dispose()),
                CleanupFailureCollector.FromAction(() => lease?.Dispose()),
                CleanupFailureCollector.FromAction(() => manager?.Dispose()),
                CleanupFailureCollector.FromAction(() => { if (manager is null) watcher?.Dispose(); }),
                CleanupFailureCollector.FromAction(() => fixture?.Dispose()));
        }
    }

    private sealed class MutateFinalGrantPolicy(IWorkspacePathPolicy inner, string project, string source,
        string opaque, byte[] mutation) : IWorkspacePathPolicy
    {
        internal bool Armed { get; set; }
        internal int Mutations { get; private set; }
        internal bool ObservedGrantContainedOpaque { get; private set; }
        public async Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        {
            await inner.ValidateAsync(workspaceId, paths, ct);
            if (Armed && Mutations == 0 && paths.Contains(project, FileSystemPath.Comparer)
                && paths.Contains(source, FileSystemPath.Comparer))
            {
                ObservedGrantContainedOpaque = paths.Contains(opaque, FileSystemPath.Comparer);
                Mutations++;
                await File.WriteAllBytesAsync(opaque, mutation, ct);
            }
        }
    }

    private sealed class ArmAfterActualRestoreInspection(MutateFinalGrantPolicy policy, string project, string source,
        string opaque) : RestoreStalenessDetector
    {
        internal bool CommitPhase { get; set; }
        internal bool SawCandidateWithoutOpaque { get; private set; }
        public override async Task<bool> DetectRestoreRequiredAsync(Solution solution,
            Func<IReadOnlyList<string>, CancellationToken, Task> authorize, ILogger logger, CancellationToken ct)
        {
            var result = await base.DetectRestoreRequiredAsync(solution, authorize, logger, ct);
            if (CommitPhase)
            {
                var grants = NativeCompilerContextCertificate.Require(solution).FinalRestoreGrantPaths(solution);
                Assert.IsTrue(grants.Contains(project, FileSystemPath.Comparer));
                Assert.IsTrue(grants.Contains(source, FileSystemPath.Comparer));
                Assert.IsFalse(grants.Contains(opaque, FileSystemPath.Comparer));
                SawCandidateWithoutOpaque = true;
                policy.Armed = true;
            }
            return result;
        }
    }
}
