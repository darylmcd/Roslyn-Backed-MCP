using System.Xml.Linq;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class SupplementalPackageReadAuthorityTests
{
    [TestMethod]
    [DataRow("current")]
    [DataRow("revoke")]
    [DataRow("close")]
    [DataRow("reload")]
    [DataRow("assets")]
    [DataRow("package")]
    public async Task InheritedPackageReads_RetainOriginalSessionGrantAndExactInputs(string transition)
    {
        NuGetInstallationFixture? fixture = null;
        WorkspaceManager? manager = null;
        Microsoft.CodeAnalysis.AdhocWorkspace? unrelated = null;
        Exception? primary = null;
        try
        {
            fixture = await NuGetInstallationFixture.CreateAsync("Owned.Example", "1.0.0",
                new Dictionary<string, byte[]> { ["contentFiles/cs/any/Input.cs"] = "class Input { }"u8.ToArray() });
            var policy = new OriginalScopePolicy(fixture.Cache);
            var wrapped = new ForkInvocationPathPolicy(policy);
            manager = CreateManager(wrapped, new WorkspaceSessionLoader());
            var loaded = await manager.LoadAsync(Path.Combine(fixture.Root, "Owner.csproj"), fixture.LoadProperties, CancellationToken.None);
            var original = manager.GetCurrentSolution(loaded.WorkspaceId);
            var loader = (IWorkspaceEvaluationLoader)manager;
            var authority = loader.CapturePackageReadAuthority(loaded.WorkspaceId, original);
            policy.OriginalWorkspaceId = loaded.WorkspaceId;
            policy.CacheScopes.Clear();
            await authority.AssertCurrentAsync(CancellationToken.None);
            Assert.IsTrue(policy.CacheScopes.Count > 0, "The custom cache must require live authorization.");
            Assert.IsTrue(policy.CacheScopes.All(scope => scope == loaded.WorkspaceId));
            var packageInput = authority.Files.Single(path => path.EndsWith("Input.cs", StringComparison.Ordinal));
            Assert.IsFalse(authority.Files.Contains(Path.Combine(fixture.Cache, "unproved.cs"), FileSystemPath.Comparer));
            unrelated = new Microsoft.CodeAnalysis.AdhocWorkspace();
            Assert.ThrowsExactly<WorkspaceMutationOwnershipException>(() =>
                loader.CapturePackageReadAuthority(loaded.WorkspaceId, unrelated.CurrentSolution));

            switch (transition)
            {
                case "current":
                    await authority.AssertCurrentAsync(CancellationToken.None);
                    Assert.AreSame(original, manager.GetCurrentSolution(loaded.WorkspaceId));
                    break;
                case "revoke":
                    policy.Revoked = true;
                    await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => authority.AssertCurrentAsync(CancellationToken.None));
                    break;
                case "close":
                    Assert.IsTrue(manager.Close(loaded.WorkspaceId));
                    await Assert.ThrowsExactlyAsync<WorkspaceMutationOwnershipException>(() => authority.AssertCurrentAsync(CancellationToken.None));
                    break;
                case "reload":
                    await manager.ReloadAsync(loaded.WorkspaceId, CancellationToken.None);
                    await Assert.ThrowsExactlyAsync<WorkspaceMutationOwnershipException>(() => authority.AssertCurrentAsync(CancellationToken.None));
                    break;
                default:
                    await File.AppendAllTextAsync(transition == "assets" ? fixture.Assets : packageInput, " ");
                    await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => authority.AssertCurrentAsync(CancellationToken.None));
                    break;
            }
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Original package grant fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => unrelated?.Dispose()),
                CleanupFailureCollector.FromAction(() => manager?.Dispose()),
                CleanupFailureCollector.FromAction(() => fixture?.Dispose()));
        }
    }

    [TestMethod]
    [DataRow(true, false)]
    [DataRow(false, false)]
    [DataRow(true, true)]
    public Task MissingPrivateAssets_UseOnlyOriginalPackageReadsAndRemainUncertified(bool retainAuthoredPackageReference,
        bool closeSourceBeforePublication)
        => ObservePrivateAssetsAsync(retainAuthoredPackageReference, closeSourceBeforePublication, restoreBeforeCandidate: false, scopePrivateEntry: false);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task RestoredPrivateAssets_RequireOriginalCurrentCustomCacheScope(bool scopePrivateEntry)
        => ObservePrivateAssetsAsync(retainAuthoredPackageReference: true, closeSourceBeforePublication: false, restoreBeforeCandidate: true, scopePrivateEntry);

    private async Task ObservePrivateAssetsAsync(bool retainAuthoredPackageReference, bool closeSourceBeforePublication,
        bool restoreBeforeCandidate, bool scopePrivateEntry)
    {
        NuGetInstallationFixture? fixture = null;
        WorkspaceManager? manager = null;
        PhysicalDirectoryScope.OwnedRoot? destinationOwner = null;
        IDisposable? scope = null;
        IDisposable? ownedCopy = null;
        PhysicalGraphCandidate? candidate = null;
        Exception? primary = null;
        try
        {
            fixture = await NuGetInstallationFixture.CreateAsync("Owned.Example", "1.0.0", new Dictionary<string, byte[]>
            {
                ["build/Owned.Example.props"] = """<Project><ItemGroup><Compile Include="$(MSBuildThisFileDirectory)../contentFiles/cs/any/Inherited.cs" /></ItemGroup></Project>"""u8.ToArray(),
                ["contentFiles/cs/any/Inherited.cs"] = "class Inherited { }"u8.ToArray()
            });
            var sourceProject = Path.Combine(fixture.Root, "Owner.csproj");
            if (!retainAuthoredPackageReference)
            {
                var definition = System.Xml.Linq.XDocument.Load(sourceProject);
                definition.Descendants("PackageReference").Remove();
                definition.Save(sourceProject);
            }
            var policy = new OriginalScopePolicy(fixture.Cache);
            var wrapped = new ForkInvocationPathPolicy(policy);
            var observedLoader = new CloseSourceAfterLoadLoader();
            manager = CreateManager(wrapped, observedLoader);
            var loaded = await manager.LoadAsync(sourceProject, fixture.LoadProperties, CancellationToken.None);
            var loader = (IWorkspaceEvaluationLoader)manager;
            var original = manager.GetCurrentSolution(loaded.WorkspaceId);
            var authority = loader.CapturePackageReadAuthority(loaded.WorkspaceId, original);
            var inheritedPath = authority.Files.Single(path => path.EndsWith("Inherited.cs", StringComparison.Ordinal));
            Assert.IsTrue(original.Projects.SelectMany(project => project.Documents)
                .Any(document => FileSystemPath.Comparer.Equals(document.FilePath, inheritedPath)),
                "Actual retained NuGet imports must contribute the source compiler document before transfer.");
            Assert.AreEqual(retainAuthoredPackageReference,
                System.Xml.Linq.XDocument.Load(sourceProject).Descendants("PackageReference").Any());
            policy.OriginalWorkspaceId = loaded.WorkspaceId;
            var sourceLoadedPath = manager.GetStatus(loaded.WorkspaceId).LoadedPath
                ?? throw new AssertFailedException("The source must retain its exact loaded entry.");
            var layout = await PhysicalGraphLayout.CreateAsync(loaded.WorkspaceId, sourceLoadedPath, original, wrapped, CancellationToken.None);
            destinationOwner = PhysicalDirectoryScope.CreateOwnedRoot(Path.Combine(fixture.Root, "private"));
            var privateProject = layout.MapTo(destinationOwner.Path, sourceLoadedPath);
            var privateRoot = Path.GetDirectoryName(privateProject)
                ?? throw new AssertFailedException("The mapped private project must retain its directory.");
            Directory.CreateDirectory(Path.Combine(privateRoot, "obj"));
            foreach (var name in new[] { "Owner.csproj", "global.json", "Directory.Build.props" })
                File.Copy(Path.Combine(fixture.Root, name), Path.Combine(privateRoot, name));
            foreach (var source in Directory.EnumerateFiles(Path.Combine(fixture.Root, "obj"), "*.nuget.g.*"))
                File.Copy(source, Path.Combine(privateRoot, "obj", Path.GetFileName(source)));
            if (scopePrivateEntry) policy.EntryRoot = privateRoot;
            Assert.IsFalse(File.Exists(Path.Combine(privateRoot, "obj", "project.assets.json")));
            var privateProperties = OwnedNativeLoadContext.Properties(privateProject, new Dictionary<string, string>());
            var invocation = loader.CaptureForkInvocationAuthority(loaded.WorkspaceId, original,
                privateProject, privateProperties, destinationOwner, layout);
            scope = wrapped.Enter(invocation);
            ownedCopy = invocation.EnterOwnedCopy(destinationOwner, layout);
            if (closeSourceBeforePublication)
            {
                observedLoader.AfterLoad = () => Assert.IsTrue(manager.Close(loaded.WorkspaceId));
                await Assert.ThrowsExactlyAsync<WorkspaceMutationOwnershipException>(() => loader.LoadWithPropertiesAsync(privateProject,
                    privateProperties, authority, invocation, CancellationToken.None));
                Assert.IsTrue(observedLoader.ObservedCallback, "The real native loader must return before source closure.");
                Assert.HasCount(0, manager.ListWorkspaces(), "The private semantic workspace must not be published after original authority closes.");
                return;
            }
            var result = await loader.LoadWithPropertiesAsync(privateProject,
                privateProperties, authority, invocation, CancellationToken.None);
            Assert.IsTrue(result.RestoreRequired);
            var privateSolution = manager.GetCurrentSolution(result.WorkspaceId);
            Assert.IsTrue(privateSolution.Projects.SelectMany(project => project.Documents)
                .Any(document => FileSystemPath.Comparer.Equals(document.FilePath, inheritedPath)));
            Assert.ThrowsExactly<PublicInvalidOperationException>(() => NativeCompilerContextCertificate.Require(privateSolution));
            Assert.IsTrue(policy.CacheScopes.All(scope => scope == loaded.WorkspaceId), "Inherited reads retain the original grant scope.");
            Assert.IsFalse(File.Exists(Path.Combine(privateRoot, "obj", "project.assets.json")), "No selecting assets may be synthesized.");
            if (restoreBeforeCandidate)
            {
                var properties = OwnedNativeLoadContext.Properties(privateProject, new Dictionary<string, string>());
                var restored = await new DotnetCommandRunner().RunAsync(privateRoot, privateProject,
                    MsBuildCommandProperties.Append(["restore", privateProject, "--nologo", "--source", Path.Combine(fixture.Root, "feed"),
                        "--packages", fixture.Cache], properties), CancellationToken.None);
                Assert.IsTrue(restored.Succeeded, "The private selecting assets must come from an actual successful owned restore.");
                Assert.IsTrue(File.Exists(Path.Combine(privateRoot, "obj", "project.assets.json")));
                await authority.AssertCurrentAsync(CancellationToken.None);
                candidate = await PhysicalGraphCandidate.LoadAsync(result.WorkspaceId, privateProject, properties,
                    wrapped, new WorkspaceSessionLoader(), NullLogger.Instance, null, CancellationToken.None);
                _ = NativeCompilerContextCertificate.Require(candidate.Solution);
                await loader.ReloadWithInvocationAuthorityAsync(result.WorkspaceId, invocation!, CancellationToken.None);
                var reloaded = manager.GetCurrentSolution(result.WorkspaceId);
                await NativeCompilerContextCertificate.Require(reloaded).AssertCurrentAsync(reloaded, result.WorkspaceId, wrapped, CancellationToken.None);
                var project = reloaded.Projects.Single();
                var reference = project.MetadataReferences.OfType<Microsoft.CodeAnalysis.PortableExecutableReference>().First();
                var admitted = await manager.ReadAdmittedImagesAsync(result.WorkspaceId, project.Id, reference, CancellationToken.None);
                Assert.IsTrue(admitted.Length > 0, "Downstream manager image reads must share the live invocation policy.");
                await invocation!.AssertCurrentAsync(CancellationToken.None);
                Assert.IsTrue(policy.CacheScopes.All(scope => scope == loaded.WorkspaceId),
                    "Independent final proof must validate external dependencies in the original current grant scope.");
                var completedCopy = ownedCopy;
                ownedCopy = null;
                completedCopy.Dispose();
                var completedScope = scope;
                scope = null;
                completedScope.Dispose();
                await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => manager.ReloadAsync(result.WorkspaceId, CancellationToken.None));
                Assert.AreSame(reloaded, manager.GetCurrentSolution(result.WorkspaceId), "A retained fork must not keep the operation's delegated grant.");
            }
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Private package namespace cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => candidate?.Dispose()),
                CleanupFailureCollector.FromAction(() => manager?.Dispose()),
                CleanupFailureCollector.FromAction(() => destinationOwner?.Dispose()),
                CleanupFailureCollector.FromAction(() => ownedCopy?.Dispose()),
                CleanupFailureCollector.FromAction(() => scope?.Dispose()),
                CleanupFailureCollector.FromAction(() => fixture?.Dispose()));
        }
    }

    private static WorkspaceManager CreateManager(ForkInvocationPathPolicy policy, WorkspaceSessionLoader sessionLoader)
    {
        FileWatcherService? watcher = null;
        try
        {
            watcher = new FileWatcherService(NullLogger<FileWatcherService>.Instance);
            return new WorkspaceManager(policy, NullLogger<WorkspaceManager>.Instance, new PreviewStore(), watcher,
                new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 4 }, cacheStore: null, sessionLoader: sessionLoader);
        }
        catch (Exception failure)
        {
            OwnedResourceCleanup.DisposePreservingFailure("Supplemental package manager setup cleanup failed.", failure, watcher);
            throw;
        }
    }

    private sealed class CloseSourceAfterLoadLoader : WorkspaceSessionLoader
    {
        internal Action? AfterLoad { get; set; }
        internal bool ObservedCallback { get; private set; }
        public override async Task<(MSBuildWorkspace Workspace, AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease Lease,
            NativeCompilerContextCertificate Context, NativeSourceAdmission.SourceAdmissionReceipt? SourceAdmission)> CreateAndOpenAsync(string workspaceId, string path,
            IDictionary<string, string>? globalProperties, IWorkspacePathPolicy pathPolicy, WorkspaceDiagnosticsSink diagnostics,
            ILogger logger, SupplementalPackageReadAuthority supplementalPackages, NativeSourceAdmission? sourceAdmission, CancellationToken ct)
        {
            var loaded = await base.CreateAndOpenAsync(workspaceId, path, globalProperties, pathPolicy, diagnostics, logger,
                supplementalPackages, sourceAdmission, ct);
            try
            {
                if (AfterLoad is { } callback)
                {
                    AfterLoad = null;
                    ObservedCallback = true;
                    callback();
                }
                return loaded;
            }
            catch (Exception failure)
            {
                OwnedResourceCleanup.Dispose("Native loader callback cleanup failed.", failure, loaded.Workspace, loaded.Lease);
                throw;
            }
        }
    }

    private sealed class OriginalScopePolicy(string cache) : IWorkspacePathPolicy
    {
        private readonly OwnedTestWorkspacePathPolicy _owned = new();
        internal string? OriginalWorkspaceId { get; set; }
        internal bool Revoked { get; set; }
        internal string? EntryRoot { get; set; }
        internal System.Collections.Concurrent.ConcurrentQueue<string> CacheScopes { get; } = new();
        public Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        {
            if (EntryRoot is { } entry && paths.Any(path => FileSystemPath.Comparer.Equals(path, entry) || FileSystemPath.IsStrictDescendant(entry, path))
                && workspaceId != OriginalWorkspaceId)
                throw new UnauthorizedAccessException("The original project-root grant is unavailable in this workspace scope.");
            if (paths.Any(path => FileSystemPath.Comparer.Equals(path, cache) || FileSystemPath.IsStrictDescendant(cache, path)))
            {
                CacheScopes.Enqueue(workspaceId);
                if (Revoked || OriginalWorkspaceId is not null && workspaceId != OriginalWorkspaceId)
                    throw new UnauthorizedAccessException("The exact original cache grant is not current in this workspace scope.");
            }
            return _owned.ValidateAsync(workspaceId, paths, ct);
        }
    }
}
