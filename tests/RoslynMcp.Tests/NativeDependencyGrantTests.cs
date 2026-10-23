using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeDependencyGrantTests
{
    [TestMethod]
    public async Task DeniedNativeUserExtension_RetainsSemanticLoad_ExactGrantAndReloadCertifies()
    {
        NuGetInstallationFixture? fixture = null;
        WorkspaceManager? manager = null;
        Exception? primary = null;
        try
        {
            fixture = await NuGetInstallationFixture.CreateAsync("Owned.Dependency", "1.0.0",
                new Dictionary<string, byte[]> { ["lib/net10.0/_._"] = [] });
            var extensions = Path.Combine(fixture.Root, "owned-user-extensions");
            var before = Path.Combine(extensions, "Current", "Imports", "Microsoft.Common.props", "ImportBefore");
            Directory.CreateDirectory(before);
            await File.WriteAllTextAsync(Path.Combine(before, "Owned.props"),
                "<Project><PropertyGroup><DefineConstants>$(DefineConstants);OWNED_AMBIENT</DefineConstants></PropertyGroup></Project>");
            var globals = new Dictionary<string, string> { ["MSBuildUserExtensionsPath"] = extensions };
            var policy = new RevocableDependencyPolicy(extensions);
            manager = Manager(policy);
            var status = await manager.LoadAsync(Path.Combine(fixture.Root, "Owner.csproj"), RoslynMcp.Tests.Helpers.OwnedNativeLoadContext.Properties(Path.Combine(fixture.Root, "Owner.csproj"), globals), CancellationToken.None);
            Assert.IsTrue(policy.Denials > 0, "The current policy must observe the actual evaluated extension input.");
            Assert.IsFalse(status.RestoreRequired,
                "Restore cannot authorize a denied build-input grant; valid package assets remain current while compiler authority stays unavailable.");
            var diagnostic = status.WorkspaceDiagnostics.Single(item => item.Id == "WORKSPACE_COMPILER_CONTEXT_UNAVAILABLE");
            StringAssert.Contains(diagnostic.Message, "Authorize");
            StringAssert.Contains(diagnostic.Message, "reload");
            Assert.IsFalse(diagnostic.Message.Contains("recorded package installation", StringComparison.OrdinalIgnoreCase),
                "An arbitrary build dependency needs guidance that does not misclassify it as a package cache.");
            var semantic = manager.GetCurrentSolution(status.WorkspaceId);
            var options = Assert.IsInstanceOfType<CSharpParseOptions>(semantic.Projects.Single().ParseOptions);
            Assert.IsTrue(options.PreprocessorSymbolNames.Contains("OWNED_AMBIENT"),
                "The trusted native semantic load must really consume the fixture's inherited build input.");
            Assert.ThrowsExactly<PublicInvalidOperationException>(() => NativeCompilerContextCertificate.Require(semantic));
            policy.Deny = false;
            Assert.ThrowsExactly<PublicInvalidOperationException>(() => NativeCompilerContextCertificate.Require(semantic),
                "A new grant must not bless an already-issued unavailable snapshot.");
            var reloaded = await manager.ReloadAsync(status.WorkspaceId, CancellationToken.None);
            var current = manager.GetCurrentSolution(reloaded.WorkspaceId);
            Assert.AreEqual("None", policy.InnerDenialCategory, "The granted native fixture must have no unrelated current-policy denial.");
            _ = NativeCompilerContextCertificate.Require(current);
            Assert.AreNotSame(semantic, current);
            Assert.IsFalse(reloaded.WorkspaceDiagnostics.Any(item => item.Id == "WORKSPACE_COMPILER_CONTEXT_UNAVAILABLE"));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Native dependency grant fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => manager?.Dispose()),
                CleanupFailureCollector.FromAction(() => fixture?.Dispose()));
        }
    }

    [TestMethod]
    [DataRow("Owner.csproj")]
    [DataRow("project.assets.json")]
    [DataRow("global.json")]
    public async Task SourceAssetsAndCliConfigurationDenial_RemainsFatal(string input)
    {
        NuGetInstallationFixture? fixture = null;
        WorkspaceManager? manager = null;
        Exception? primary = null;
        try
        {
            fixture = await NuGetInstallationFixture.CreateAsync("Owned.Dependency", "1.0.0",
                new Dictionary<string, byte[]> { ["lib/net10.0/_._"] = [] });
            var denied = input == "project.assets.json" ? fixture.Assets : Path.Combine(fixture.Root, input);
            var policy = new RevocableDependencyPolicy(denied);
            manager = Manager(policy);
            var globals = new Dictionary<string, string> { ["MSBuildUserExtensionsPath"] = Path.Combine(fixture.Root, "user-extensions") };
            Task Load() => manager.LoadAsync(Path.Combine(fixture.Root, "Owner.csproj"),
                OwnedNativeLoadContext.Properties(Path.Combine(fixture.Root, "Owner.csproj"), globals), CancellationToken.None);
            if (input == "global.json")
            {
                var failure = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(Load);
                StringAssert.Contains(failure.Message, "nearest global.json");
            }
            else
            {
                var failure = await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(Load);
                StringAssert.Contains(failure.Message, "owned build dependency");
            }
            Assert.IsTrue(policy.Denials > 0, "The configured source/assets/config denial must be reached, not an earlier unrelated input.");
            Assert.HasCount(0, manager.ListWorkspaces(), "A mandatory input refusal must not publish a semantic session.");
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Native dependency grant fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => manager?.Dispose()),
                CleanupFailureCollector.FromAction(() => fixture?.Dispose()));
        }
    }

    private static WorkspaceManager Manager(IWorkspacePathPolicy policy)
    {
        FileWatcherService? watcher = null;
        try
        {
            watcher = new FileWatcherService(NullLogger<FileWatcherService>.Instance);
            return new WorkspaceManager(new ForkInvocationPathPolicy(policy),
                NullLogger<WorkspaceManager>.Instance, new PreviewStore(), watcher,
                new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 4 });
        }
        catch (Exception failure)
        {
            OwnedResourceCleanup.DisposePreservingFailure("Native dependency manager setup cleanup failed.", failure, watcher);
            throw;
        }
    }

    private sealed class RevocableDependencyPolicy(string denied) : IWorkspacePathPolicy
    {
        private readonly OwnedTestWorkspacePathPolicy _inner = new();
        internal bool Deny { get; set; } = true;
        internal int Denials { get; private set; }
        internal string InnerDenialCategory { get; private set; } = "None";
        public Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (Deny && paths.Any(path => FileSystemPath.Comparer.Equals(path, denied) || FileSystemPath.IsStrictDescendant(denied, path)))
            {
                Denials++;
                throw new UnauthorizedAccessException("The owned build dependency requires a current grant.");
            }
            try { return _inner.ValidateAsync(workspaceId, paths, ct); }
            catch (UnauthorizedAccessException failure)
            {
                var filename = failure.Data["RejectedFileName"] as string;
                InnerDenialCategory = filename switch
                {
                    "ImportBefore" => "ImportBefore",
                    "ImportAfter" => "ImportAfter",
                    "WorkloadManifest.targets" => "SdkWorkloadManifest",
                    _ => "OtherDependency"
                };
                throw;
            }
        }
    }
}
