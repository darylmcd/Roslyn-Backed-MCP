using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class PhysicalGraphPlanAuthorityTests
{
    [TestMethod]
    public async Task CopyPlanning_RenewsCurrentDirectoryGrantBeforeListing()
    {
        var fixture = Path.Combine(TestTempRoot.Current, nameof(PhysicalGraphPlanAuthorityTests), Guid.NewGuid().ToString("N"));
        var source = Path.Combine(fixture, "source");
        var destination = Path.Combine(fixture, "private-copy");
        AdhocWorkspace? workspace = null;
        string? originalAcl = null;
        UnixFileMode? originalMode = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(source);
            var projectPath = Path.Combine(source, "Source.csproj");
            await File.WriteAllTextAsync(projectPath, "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
            await File.WriteAllTextAsync(Path.Combine(source, "PRIVATE-NOW-DENIED.cs"), "class PrivateSource { }");
            workspace = new AdhocWorkspace();
            var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(),
                "Source", "Source", LanguageNames.CSharp, filePath: projectPath));
            var policy = new RevocablePolicy(source);
            var layout = await PhysicalGraphLayout.CreateAsync("plan-current-grant", projectPath, project.Solution, policy, CancellationToken.None);
            if (OperatingSystem.IsWindows())
            {
                var directory = new DirectoryInfo(source);
                var acl = directory.GetAccessControl();
                originalAcl = acl.GetSecurityDescriptorSddlForm(AccessControlSections.Access);
                using var identity = WindowsIdentity.GetCurrent();
                var sid = identity.User ?? throw new AssertFailedException("The permission premise requires the current user SID.");
                acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.ListDirectory,
                    InheritanceFlags.None, PropagationFlags.None, AccessControlType.Deny));
                directory.SetAccessControl(acl);
            }
            else
            {
                originalMode = File.GetUnixFileMode(source);
                File.SetUnixFileMode(source, UnixFileMode.UserExecute);
            }
            Assert.ThrowsExactly<UnauthorizedAccessException>(() => Directory.GetFileSystemEntries(source),
                "The actual source listing must fail independently before testing grant ordering.");
            policy.Revoked = true;
            var refusal = await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() =>
                layout.PlanCopyAsync("plan-current-grant", destination, policy, CancellationToken.None),
                "Current policy must refuse before a native listing can fail.");
            Assert.AreSame(policy.Refusal, refusal, "A native permission error cannot stand in for the policy's exact refusal.");
            Assert.IsTrue(policy.RevokedChecks > 0);
            Assert.IsFalse(Directory.Exists(destination));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Graph plan authority fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => workspace?.Dispose()),
                CleanupFailureCollector.FromAction(() =>
                {
                    if (OperatingSystem.IsWindows() && originalAcl is not null)
                    {
                        var acl = new DirectorySecurity();
                        acl.SetSecurityDescriptorSddlForm(originalAcl, AccessControlSections.Access);
                        new DirectoryInfo(source).SetAccessControl(acl);
                        Assert.AreEqual(originalAcl, new DirectoryInfo(source).GetAccessControl()
                            .GetSecurityDescriptorSddlForm(AccessControlSections.Access));
                    }
                    else if (!OperatingSystem.IsWindows() && originalMode is { } mode)
                    {
                        File.SetUnixFileMode(source, mode);
                        Assert.AreEqual(mode, File.GetUnixFileMode(source));
                    }
                }),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(fixture)));
        }
    }

    private sealed class RevocablePolicy(string source) : IWorkspacePathPolicy
    {
        private readonly OwnedTestWorkspacePathPolicy _owned = new();
        internal bool Revoked { get; set; }
        internal int RevokedChecks { get; private set; }
        internal UnauthorizedAccessException Refusal { get; } = new("Current caller revoked the source directory grant.");
        public Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        {
            if (Revoked && paths.Any(path => FileSystemPath.Comparer.Equals(source, path)
                || FileSystemPath.IsStrictDescendant(source, path)))
            {
                RevokedChecks++;
                throw Refusal;
            }
            return _owned.ValidateAsync(workspaceId, paths, ct);
        }
    }
}
