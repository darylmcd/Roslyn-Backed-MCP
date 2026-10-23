using System.Xml.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Security;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeDocumentDegradedAuthorityTests
{
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task HostDocumentGrant_RemainsMandatoryWhileAliasesPermitSemanticReads(bool alias, bool denyOtherDocument)
    {
        NuGetInstallationFixture? fixture = null;
        WorkspaceManager? manager = null;
        FileWatcherService? watcher = null;
        string? outside = null, deniedFile = null;
        Exception? fixtureFailure = null;
        try
        {
            fixture = await NuGetInstallationFixture.CreateAsync("Owned.HostDocumentBoundary", "1.0.0",
                new Dictionary<string, byte[]> { ["marker.txt"] = "owned fixture"u8.ToArray() });
            outside = fixture.Root + "-outside";
            deniedFile = outside + "-denied.cs";
            Directory.CreateDirectory(outside);
            var external = Path.Combine(outside, "External.cs");
            await File.WriteAllTextAsync(external, "public class External { public int Value => 42; }");
            var include = external;
            if (alias)
            {
                var link = Path.Combine(fixture.Root, "alias");
                if (!TestFixtureFileSystem.TryCreateDirectoryLink(link, outside))
                {
                    Assert.Inconclusive("Directory links are unavailable in this test environment.");
                    return;
                }
                include = Path.Combine(link, "External.cs");
            }
            var project = Path.Combine(fixture.Root, "Owner.csproj");
            var definition = XDocument.Load(project);
            definition.Root!.Add(new XElement("PropertyGroup", new XElement("EnableDefaultCompileItems", "false")),
                new XElement("ItemGroup", new XElement("Compile", new XAttribute("Include", include))));
            if (denyOtherDocument)
            {
                await File.WriteAllTextAsync(deniedFile, "public class Denied { }");
                definition.Root.Add(new XElement("ItemGroup", new XElement("Compile", new XAttribute("Include", deniedFile))));
            }
            await File.WriteAllTextAsync(project, definition.ToString());
            var policy = new WorkspacePathPolicy(new SecurityOptions { SanctionedRoots = alias ? [fixture.Root, outside] : [fixture.Root] });
            watcher = new FileWatcherService(NullLogger<FileWatcherService>.Instance);
            manager = new WorkspaceManager(new ForkInvocationPathPolicy(policy), NullLogger<WorkspaceManager>.Instance,
                new PreviewStore(), watcher,
                new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 2 });
            if (!alias || denyOtherDocument)
            {
                var denial = await Assert.ThrowsExactlyAsync<ArgumentException>(() => manager.LoadAsync(project, fixture.LoadProperties, CancellationToken.None));
                Assert.IsTrue(WorkspacePathPolicyRefusal.Is(denial, WorkspacePathPolicyRefusalKind.SanctionedRootBoundary));
                Assert.IsEmpty(manager.ListWorkspaces(), "A denied Compile input must never publish a semantic workspace.");
                Assert.AreEqual("public class External { public int Value => 42; }", await File.ReadAllTextAsync(external));
                if (denyOtherDocument) Assert.AreEqual("public class Denied { }", await File.ReadAllTextAsync(deniedFile));
                return;
            }
            var status = await manager.LoadAsync(project, fixture.LoadProperties, CancellationToken.None);
            Assert.IsTrue(RoslynMcp.Core.Models.WorkspaceStatusSummaryDto.From(status).IsReady);
            var solution = manager.GetCurrentSolution(status.WorkspaceId);
            var document = solution.Projects.Single().Documents.Single(item => item.Name == "External.cs");
            StringAssert.Contains((await document.GetTextAsync()).ToString(), "Value => 42");
            Assert.ThrowsExactly<PublicInvalidOperationException>(() => NativeCompilerContextCertificate.Require(solution));
            StringAssert.Contains(status.WorkspaceDiagnostics.Single(item => item.Id == "WORKSPACE_COMPILER_CONTEXT_UNAVAILABLE").Message,
                "filesystem alias");
            var edits = new EditService(manager, NullLogger<EditService>.Instance);
            Task Apply() => edits.ApplyTextEditsAsync(status.WorkspaceId, include, [new TextEditDto(1, 1, 1, 1, "// changed\n")],
                "apply_text_edit", CancellationToken.None);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(Apply);
            Assert.AreEqual("public class External { public int Value => 42; }", await File.ReadAllTextAsync(external));

        }
        catch (Exception failure) { fixtureFailure = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Native document authority fixture cleanup failed.", fixtureFailure,
                CleanupFailureCollector.FromAction(() => manager?.Dispose()),
                CleanupFailureCollector.FromAction(() => { if (manager is null) watcher?.Dispose(); }),
                CleanupFailureCollector.FromAction(() => fixture?.Dispose()),
                CleanupFailureCollector.FromAction(() => { if (outside is not null) TestFixtureFileSystem.DeleteDirectoryIfExists(outside); }),
                CleanupFailureCollector.FromAction(() => { if (deniedFile is not null) File.Delete(deniedFile); }));
        }
    }

    [TestMethod]
    public async Task HostPackageGrantRefusal_RemainsDependencyDenialRatherThanInvalidAssets()
    {
        NuGetInstallationFixture? fixture = null;
        Exception? fixtureFailure = null;
        try
        {
            fixture = await NuGetInstallationFixture.CreateAsync("Owned.HostPackageBoundary", "1.0.0",
                new Dictionary<string, byte[]> { ["marker.txt"] = "owned fixture"u8.ToArray() });
            var project = Path.Combine(fixture.Root, "Owner.csproj");
            var policy = new WorkspacePathPolicy(new SecurityOptions
            {
                SanctionedRoots = [project, Path.Combine(fixture.Root, "obj"), Path.Combine(fixture.Root, "global.json"),
                Path.Combine(fixture.Root, "Directory.Build.props")]
            });
            var loaded = await new WorkspaceSessionLoader().CreateAndOpenAsync("host-package-denied", project,
                fixture.LoadProperties, policy, new WorkspaceDiagnosticsSink(64), NullLogger.Instance,
                SupplementalPackageReadAuthority.None, null, CancellationToken.None);
            Exception? primary = null;
            try
            {
                Assert.AreEqual(NativeCompilerContextAvailability.DependencyGrantRequired, loaded.Context.Availability);
                Assert.HasCount(1, loaded.Workspace.CurrentSolution.ProjectIds);
            }
            catch (Exception failure) { primary = failure; throw; }
            finally { OwnedResourceCleanup.Dispose("Host package refusal test cleanup failed.", primary, loaded.Workspace, loaded.Lease); }
        }
        catch (Exception failure) { fixtureFailure = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Native document authority fixture cleanup failed.", fixtureFailure,
                CleanupFailureCollector.FromAction(() => fixture?.Dispose()));
        }
    }

    [TestMethod]
    public async Task UnmarkedPolicyArgumentException_RemainsFatalDuringDocumentAdmission()
    {
        NuGetInstallationFixture? fixture = null;
        Exception? fixtureFailure = null;
        try
        {
            fixture = await NuGetInstallationFixture.CreateAsync("Owned.InvalidDocumentPolicy", "1.0.0",
                new Dictionary<string, byte[]> { ["marker.txt"] = "owned fixture"u8.ToArray() });
            var document = Path.Combine(fixture.Root, "Invalid.cs");
            await File.WriteAllTextAsync(document, "public class Invalid { }");
            var policy = new InvalidDocumentPolicy(document, new WorkspacePathPolicy(new SecurityOptions { SanctionedRoots = [fixture.Root] }));
            var failure = await Assert.ThrowsExactlyAsync<ArgumentException>(() => NativeCompilerContextCertificate.Epoch.CaptureAsync(
                "invalid-policy", Path.Combine(fixture.Root, "Owner.csproj"), fixture.LoadProperties, policy,
                SupplementalPackageReadAuthority.None, CancellationToken.None));
            StringAssert.Contains(failure.Message, "invalid document policy");
            Assert.IsFalse(WorkspacePathPolicyRefusal.IsDenied(failure));
        }
        catch (Exception failure) { fixtureFailure = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Native document authority fixture cleanup failed.", fixtureFailure,
                CleanupFailureCollector.FromAction(() => fixture?.Dispose()));
        }
    }

    private sealed class InvalidDocumentPolicy(string document, IWorkspacePathPolicy inner) : IWorkspacePathPolicy
    {
        public Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        {
            if (paths.Contains(document, FileSystemPath.Comparer)) throw new ArgumentException("invalid document policy", "paths");
            return inner.ValidateAsync(workspaceId, paths, ct);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DocumentGrant_RemainsMandatoryWhileAuthorizedAliasesPreserveOwnedIdentity(bool authorizedAlias)
    {
        NuGetInstallationFixture? fixture = null;
        WorkspaceManager? manager = null;
        FileWatcherService? watcher = null;
        Exception? fixtureFailure = null;
        try
        {
            fixture = await NuGetInstallationFixture.CreateAsync("Owned.DocumentBoundary", "1.0.0",
                new Dictionary<string, byte[]> { ["marker.txt"] = "owned fixture"u8.ToArray() });
            var project = Path.Combine(fixture.Root, "Owner.csproj");
            var externalRoot = Path.Combine(fixture.Root, "external");
            Directory.CreateDirectory(externalRoot);
            var external = Path.Combine(externalRoot, "External.cs");
            await File.WriteAllTextAsync(external, "public class External { public int Value => 42; }");
            var include = external;
            if (authorizedAlias)
            {
                var link = Path.Combine(fixture.Root, "alias");
                if (!TestFixtureFileSystem.TryCreateDirectoryLink(link, externalRoot))
                {
                    Assert.Inconclusive("Directory links are unavailable in this test environment.");
                    return;
                }
                include = Path.Combine(link, "External.cs");
            }
            var owned = Path.Combine(fixture.Root, "Owned.cs");
            await File.WriteAllTextAsync(owned, "public class Owned { }");
            var definition = XDocument.Load(project);
            definition.Root!.Add(new XElement("PropertyGroup", new XElement("EnableDefaultCompileItems", "false")),
                new XElement("ItemGroup", new XElement("Compile", new XAttribute("Include", owned)),
                    new XElement("Compile", new XAttribute("Include", include))));
            await File.WriteAllTextAsync(project, definition.ToString());
            var policy = new DocumentPolicy(external, authorizedAlias);
            watcher = new FileWatcherService(NullLogger<FileWatcherService>.Instance);
            manager = new WorkspaceManager(new ForkInvocationPathPolicy(policy), NullLogger<WorkspaceManager>.Instance,
            new PreviewStore(), watcher,
            new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 2 });
            if (!authorizedAlias)
            {
                await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => manager.LoadAsync(project, fixture.LoadProperties, CancellationToken.None));
                Assert.IsTrue(policy.DocumentChecks > 0, "The denied Compile document must be presented to current policy.");
                Assert.IsEmpty(manager.ListWorkspaces(), "A denied Compile input must never publish a semantic workspace.");
                Assert.AreEqual("public class External { public int Value => 42; }", await File.ReadAllTextAsync(external));
                Assert.AreEqual("public class Owned { }", await File.ReadAllTextAsync(owned));
                return;
            }
            var status = await manager.LoadAsync(project, fixture.LoadProperties, CancellationToken.None);
            Assert.IsFalse(status.RestoreRequired, "A physical alias limitation is not a missing restore artifact.");
            Assert.IsTrue(RoslynMcp.Core.Models.WorkspaceStatusSummaryDto.From(status).IsReady,
                "Current restored semantic inputs remain ready even when aliases prohibit physical mutation authority.");
            var solution = manager.GetCurrentSolution(status.WorkspaceId);
            var document = solution.Projects.Single().Documents.Single(item => item.Name == "External.cs");
            StringAssert.Contains((await document.GetTextAsync()).ToString(), "Value => 42");
            Assert.ThrowsExactly<PublicInvalidOperationException>(() => NativeCompilerContextCertificate.Require(solution));
            var changed = solution.WithDocumentText(document.Id, Microsoft.CodeAnalysis.Text.SourceText.From("public class External { }"));
            Assert.ThrowsExactly<InvalidOperationException>(() => WorkspaceManager.EnsureChangedDocumentPathsArePhysical(solution, changed),
                "Semantic alias loading must not authorize edits through that alias.");
            var diagnostic = status.WorkspaceDiagnostics.Single(item => item.Id == "WORKSPACE_COMPILER_CONTEXT_UNAVAILABLE");
            StringAssert.Contains(diagnostic.Message, "filesystem alias");
            Assert.IsTrue(policy.DocumentChecks > 0, "The external document must be presented to current policy.");

            var epoch = await NativeCompilerContextCertificate.Epoch.CaptureAsync("document-epoch", project,
                fixture.LoadProperties, policy, SupplementalPackageReadAuthority.None, CancellationToken.None);
            await epoch.AssertPreparationCurrentAsync("document-epoch", policy, CancellationToken.None);
            await File.AppendAllTextAsync(owned, "\n// changed after capture");
            var failure = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                epoch.AssertPreparationCurrentAsync("document-epoch", policy, CancellationToken.None));
            StringAssert.Contains(failure.Message, "Physical execution inputs changed");
        }
        catch (Exception failure) { fixtureFailure = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Native document authority fixture cleanup failed.", fixtureFailure,
                CleanupFailureCollector.FromAction(() => manager?.Dispose()),
                CleanupFailureCollector.FromAction(() => { if (manager is null) watcher?.Dispose(); }),
                CleanupFailureCollector.FromAction(() => fixture?.Dispose()));
        }
    }

    private sealed class DocumentPolicy(string external, bool authorizeAlias) : IWorkspacePathPolicy
    {
        private readonly OwnedTestWorkspacePathPolicy _owned = new();
        internal int DocumentChecks { get; private set; }
        public async Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        {
            foreach (var path in paths)
            {
                var physical = PhysicalPathResolver.Resolve(path);
                if (FileSystemPath.Comparer.Equals(physical, external))
                {
                    DocumentChecks++;
                    if (!authorizeAlias) throw new UnauthorizedAccessException("The external source grant is absent.");
                }
                await _owned.ValidateAsync(workspaceId, [physical], ct);
            }
        }
    }
}
