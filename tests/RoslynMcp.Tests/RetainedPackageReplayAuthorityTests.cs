using System.Xml.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class RetainedPackageReplayAuthorityTests
{
    [TestMethod]
    public async Task RemovedSelection_RetainsPackageProofForRemainingNativeGlob()
    {
        var fixture = await NuGetInstallationFixture.CreateAsync("Owned.Retained", "1.0.0", new Dictionary<string, byte[]> { ["sources/Input.cs"] = "// retained native glob"u8.ToArray() });
        Exception? resourceFailure9 = null;
        try
        {
            var project = Path.Combine(fixture.Root, "Owner.csproj");
            var member = Path.Combine(fixture.Cache, "owned.retained", "1.0.0", "sources", "Input.cs");
            var policy = new RevocablePolicy();
            var before = await PhysicalGraphCandidate.LoadAsync("retained-before", project, fixture.LoadProperties, policy, new WorkspaceSessionLoader(), NullLogger.Instance, null, CancellationToken.None);
            Exception? resourceFailure10 = null;
            try
            {
                var original = await PhysicalGraphLayout.CreateAsync("retained-before", project, before.Solution, policy, CancellationToken.None);
                Assert.IsTrue(original.ContentReplayFiles.Contains(member, FileSystemPath.Comparer));
                var xml = XDocument.Load(project);
                xml.Descendants("PackageReference").Remove();
                await File.WriteAllTextAsync(project, xml.ToString());
                var restore = await new DotnetCommandRunner().RunAsync(fixture.Root, project, MsBuildCommandProperties.Append(["restore", project, "--force", "--nologo", "--source", Path.Combine(fixture.Root, "feed"), "--packages", fixture.Cache], fixture.LoadProperties), CancellationToken.None);
                Assert.IsTrue(restore.Succeeded);
                var after = await PhysicalGraphCandidate.LoadAsync("retained-after", project, fixture.LoadProperties, policy, new WorkspaceSessionLoader(), NullLogger.Instance, null, CancellationToken.None);
                Exception? resourceFailure11 = null;
                try
                {
                    Assert.AreEqual(1, after.Solution.Projects.Single().Documents.Count(document => FileSystemPath.Comparer.Equals(document.FilePath, member)), "The deselected installation still participates through the native SDK glob.");
                    var current = await PhysicalGraphLayout.CreateAsync("retained-after", project, after.Solution, policy, CancellationToken.None);
                    Assert.IsFalse(current.Packages.Files.Contains(member, FileSystemPath.Comparer));
                    var added = PhysicalGraphLayout.WithAdditionalInputs(original, [], [], [], current.BuildNodes, current.Packages, current.ConfigurationInputs, current.ConditionInputs, current.SdkInputs);
                    foreach (var layout in new[]
                    {
                        added,
                        PhysicalGraphLayout.Union(current, original)
                    }

                    )
                    {
                        Assert.IsTrue(layout.Packages.Files.Contains(member, FileSystemPath.Comparer), "Retained exact replay membership requires its independently verified package proof.");
                        CollectionAssert.AreEqual(current.Packages.AssetInputs.ToArray(), layout.Packages.AssetInputs.ToArray(), "Historical selecting assets must not replace or conflict with the current epoch.");
                        var destination = Path.Combine(TestTempRoot.Current, "retained-private-" + Guid.NewGuid().ToString("N"));
                        Exception? destinationFailure = null;
                        try
                        {
                            foreach (var path in layout.ContentReplayFiles)
                            {
                                var target = layout.MapTo(destination, path);
                                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                                File.Copy(path, target);
                            }

                            var authority = await ContentReplayAuthority.CaptureAsync("retained-after", layout, destination, policy, CancellationToken.None);
                            await authority.AssertCurrentAsync("retained-after", policy, CancellationToken.None);
                            var archive = layout.Packages.Files.Single(path => path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase));
                            foreach (var changed in new[]
                            {
                                member,
                                archive
                            }

                            )
                            {
                                var bytes = await File.ReadAllBytesAsync(changed);
                                Exception? mutationFailure = null;
                                try
                                {
                                    await File.WriteAllBytesAsync(changed, [1, 2, 3]);
                                    await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => authority.AssertCurrentAsync("retained-after", policy, CancellationToken.None));
                                }
                                catch (Exception failure)
                                {
                                    mutationFailure = failure;
                                    throw;
                                }
                                finally
                                {
                                    await CleanupFailureCollector.RunAfterFailureAsync("Retained package bytes restoration failed.", mutationFailure, async () => await File.WriteAllBytesAsync(changed, bytes));
                                }
                            }

                            policy.Denied = member;
                            try
                            {
                                await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => authority.AssertCurrentAsync("retained-after", policy, CancellationToken.None));
                            }
                            finally
                            {
                                policy.Denied = null;
                            }

                            await authority.AssertCurrentAsync("retained-after", policy, CancellationToken.None);
                        }
                        catch (Exception failure)
                        {
                            destinationFailure = failure;
                            throw;
                        }
                        finally
                        {
                            await CleanupFailureCollector.RunAfterFailureAsync("Retained replay destination cleanup failed.", destinationFailure, CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(destination)));
                        }
                    }
                }
                catch (Exception cleanupFailure)
                {
                    resourceFailure11 = cleanupFailure;
                    throw;
                }
                finally
                {
                    OwnedResourceCleanup.DisposePreservingFailure("Restore authority resource cleanup failed.", resourceFailure11, after);
                }
            }
            catch (Exception cleanupFailure)
            {
                resourceFailure10 = cleanupFailure;
                throw;
            }
            finally
            {
                OwnedResourceCleanup.DisposePreservingFailure("Restore authority resource cleanup failed.", resourceFailure10, before);
            }
        }
        catch (Exception cleanupFailure)
        {
            resourceFailure9 = cleanupFailure;
            throw;
        }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Restore authority resource cleanup failed.", resourceFailure9, fixture);
        }
    }

    [TestMethod]
    public async Task ExactReplayMembershipWithoutInstalledProof_CannotRecaptureArbitraryBytes()
    {
        var root = Path.Combine(TestTempRoot.Current, "unproven-replay-" + Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var file = Path.Combine(root, "Input.cs");
            await File.WriteAllTextAsync(file, "// arbitrary bytes");
            var layout = new PhysicalGraphLayout(root, root, [root], [], [], new([], [file], [root]), [], [], [file], [], [], [], [], InstalledPackageAuthority.Merge([]), [], [file], null);
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => ContentReplayAuthority.CaptureAsync("unproven", layout, Path.Combine(root, "private"), new OwnedTestWorkspacePathPolicy(), CancellationToken.None));
        }
        catch (Exception failure)
        {
            primary = failure;
            throw;
        }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Unproven replay fixture cleanup failed.", primary, CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    private sealed class RevocablePolicy : IWorkspacePathPolicy
    {
        internal string? Denied { get; set; }
        public Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        {
            if (Denied is { } denied && paths.Contains(denied, FileSystemPath.Comparer))
                throw new UnauthorizedAccessException("The retained package member grant was revoked.");
            return new OwnedTestWorkspacePathPolicy().ValidateAsync(workspaceId, paths, ct);
        }
    }
}
