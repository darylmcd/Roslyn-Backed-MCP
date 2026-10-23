using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class WorkspaceSessionLoaderAdmissionTests
{
    [TestMethod]
    public void CandidateDiagnostics_TransferOnlyAcceptedMessagesAndPreserveBoundedForwarding()
    {
        var sink = new WorkspaceDiagnosticsSink(2);
        var discarded = sink.CreateCandidate();
        var accepted = sink.CreateCandidate();
        var original = new RoslynMcp.Core.Models.DiagnosticDto("original", "original", "Warning", "Workspace", null, null, null, null, null, null);
        sink.Queue.Enqueue(original);
        discarded.Queue.Enqueue(original with { Id = "discarded" });
        accepted.Queue.Enqueue(original with { Id = "accepted" });
        accepted.ForwardTo(sink, "diagnostics", NullLogger.Instance);
        CollectionAssert.AreEqual(new[] { "original", "accepted" }, sink.Queue.Select(item => item.Id).ToArray());
        var later = accepted.CreateCandidate();
        later.Queue.Enqueue(original with { Id = "later" });
        later.ForwardTo(accepted, "diagnostics", NullLogger.Instance);
        CollectionAssert.AreEqual(new[] { "accepted", "later" }, sink.Queue.Select(item => item.Id).ToArray());
        Assert.ThrowsExactly<InvalidOperationException>(() => accepted.ForwardTo(sink, "diagnostics", NullLogger.Instance));
    }

    [TestMethod]
    [DataRow("stable")]
    [DataRow("declared-output")]
    [DataRow("authored-input")]
    [DataRow("revoked-grant")]
    [DataRow("canceled")]
    [DataRow("unavailable")]
    [DataRow("open-failure")]
    public async Task NativeOpen_ReusesOnlyAnUnchangedCompleteAdmission(string scenario)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(WorkspaceSessionLoaderAdmissionTests), Guid.NewGuid().ToString("N"));
        var project = Path.Combine(root, "Admission.csproj");
        var source = Path.Combine(root, "Owned.cs");
        var output = Path.Combine(root, "obj", "admission.xml");
        var policy = new RevocablePolicy();
        CancellationTokenSource? cancellation = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(Path.Combine(root, "global.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(root, "Directory.Build.props"), "<Project />");
            await File.WriteAllTextAsync(project,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit><DocumentationFile>obj/admission.xml</DocumentationFile></PropertyGroup></Project>");
            await File.WriteAllTextAsync(source, "public sealed class Owned { }");
            var properties = OwnedNativeLoadContext.Properties(project, new Dictionary<string, string>());
            var restore = await new DotnetCommandRunner().RunAsync(root, project,
                MsBuildCommandProperties.Append(["restore", project, "--nologo"], properties), CancellationToken.None);
            Assert.IsTrue(restore.Succeeded, restore.StdErr + restore.StdOut);

            // Establish actual SDK-generated inputs before measuring a stable load.
            await LoadAndReleaseAsync(new WorkspaceSessionLoader(), CancellationToken.None);
            if (scenario == "unavailable")
                await File.WriteAllTextAsync(Path.Combine(root, "obj", "project.assets.json"), "{invalid-assets");
            cancellation = new CancellationTokenSource();
            var loader = new ObservedLoader(async () =>
            {
                if (scenario == "declared-output") await File.WriteAllTextAsync(output, "<doc>owned output transition</doc>");
                if (scenario == "authored-input") await File.WriteAllTextAsync(source, "public sealed class Changed { }");
                if (scenario == "revoked-grant") policy.Denied = source;
                if (scenario == "canceled") cancellation.Cancel();
                if (scenario == "open-failure") throw new InvalidOperationException("Controlled native Open failure.");
            });
            if (scenario == "open-failure")
            {
                var failure = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => LoadAndReleaseAsync(loader, cancellation.Token));
                StringAssert.Contains(failure.Message, "Controlled native Open failure.");
                Assert.AreEqual(1, loader.OpenCount, "Native Open failure must not be retried.");
            }
            else if (scenario == "authored-input")
            {
                await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => LoadAndReleaseAsync(loader, cancellation.Token));
                Assert.AreEqual(1, loader.OpenCount, "An input mutation must fail rather than trigger another Open.");
                StringAssert.Contains(await File.ReadAllTextAsync(source), "Changed");
            }
            else if (scenario == "revoked-grant")
            {
                await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => LoadAndReleaseAsync(loader, cancellation.Token));
                Assert.AreEqual(1, loader.OpenCount, "A denied grant must not select the second-open branch.");
            }
            else if (scenario == "canceled")
            {
                await Assert.ThrowsAsync<OperationCanceledException>(() => LoadAndReleaseAsync(loader, cancellation.Token));
                Assert.AreEqual(1, loader.OpenCount, "Cancellation must not cause another Open.");
            }
            else
            {
                await LoadAndReleaseAsync(loader, cancellation.Token);
                Assert.AreEqual(scenario == "stable" ? 1 : 2, loader.OpenCount);
                Assert.AreEqual("public sealed class Owned { }", await File.ReadAllTextAsync(source));
            }

            async Task LoadAndReleaseAsync(WorkspaceSessionLoader candidate, CancellationToken ct)
            {
                var loaded = await candidate.CreateAndOpenAsync("admission", project, properties, policy,
                    new WorkspaceDiagnosticsSink(64), NullLogger.Instance, SupplementalPackageReadAuthority.None, null, ct);
                Exception? loadFailure = null;
                try
                {
                    var unavailable = scenario == "unavailable" && candidate is ObservedLoader;
                    Assert.AreEqual(unavailable ? NativeCompilerContextAvailability.InvalidRestoreAssets
                        : NativeCompilerContextAvailability.Certified, loaded.Context.Availability);
                    var projection = await EvaluatedProjectGraph.WithPersistedReferenceMetadataAsync("admission", loaded.Context, policy, ct);
                    Assert.AreEqual(1, projection.Solution.ProjectIds.Count);
                    if (unavailable)
                        Assert.ThrowsExactly<PublicInvalidOperationException>(() => NativeCompilerContextCertificate.Require(projection.Solution));
                    else _ = NativeCompilerContextCertificate.Require(projection.Solution);
                }
                catch (Exception failure) { loadFailure = failure; throw; }
                finally { OwnedResourceCleanup.Dispose("Admission test native cleanup failed.", loadFailure, loaded.Workspace, loaded.Lease); }
            }
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Admission test fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => cancellation?.Dispose()),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    private sealed class ObservedLoader(Func<Task> afterFirstOpen) : WorkspaceSessionLoader
    {
        internal int OpenCount { get; private set; }
        protected override async Task OpenNativeAsync(MSBuildWorkspace workspace, string path, CancellationToken ct)
        {
            OpenCount++;
            await base.OpenNativeAsync(workspace, path, ct);
            if (OpenCount == 1) await afterFirstOpen();
        }
    }

    private sealed class RevocablePolicy : IWorkspacePathPolicy
    {
        private readonly OwnedTestWorkspacePathPolicy _owned = new();
        internal string? Denied { get; set; }
        public Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        {
            if (Denied is { } denied && paths.Contains(denied, FileSystemPath.Comparer))
                throw new UnauthorizedAccessException("The owned source grant was revoked.");
            return _owned.ValidateAsync(workspaceId, paths, ct);
        }
    }
}
