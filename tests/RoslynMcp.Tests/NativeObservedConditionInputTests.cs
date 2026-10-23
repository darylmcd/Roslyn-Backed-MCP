using RoslynMcp.Roslyn.Helpers;
using System.Xml.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeObservedConditionInputTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Layout_RefusesPresenceChangedAfterNativeObservationBeforeIdentityCapture(bool initiallyPresent)
    {
        var fixture = await NuGetInstallationFixture.CreateAsync("Owned.ConditionEpoch", "1.0.0", new Dictionary<string, byte[]> { ["lib/net10.0/_._"] = [] });
        Exception? resourceFailure16 = null;
        try
        {
            var project = Path.Combine(fixture.Root, "Owner.csproj");
            var marker = Path.Combine(fixture.Root, "marker.flag");
            if (initiallyPresent)
                await File.WriteAllTextAsync(marker, "initial marker");
            await AddConditionAsync(project, suffix: false);
            var originalProject = await File.ReadAllBytesAsync(project);
            var globals = OwnedNativeLoadContext.Properties(project, new Dictionary<string, string> { ["MarkerPath"] = marker });
            var policy = new PresenceFlipPolicy(marker, initiallyPresent);
            var source = await PhysicalGraphCandidate.LoadAsync("condition-epoch-source", project, globals, policy, new WorkspaceSessionLoader(), NullLogger.Instance, null, CancellationToken.None);
            Exception? resourceFailure17 = null;
            try
            {
                Assert.AreEqual(initiallyPresent, HasMarker(source.Solution.Projects.Single()), "Actual native source truth must be established before the capture hook is armed.");
                policy.Armed = true;
                Exception? failure = null;
                try
                {
                    _ = await PhysicalGraphLayout.CreateAsync("condition-epoch-layout", project, source.Solution, policy, CancellationToken.None);
                }
                catch (Exception observed)
                {
                    failure = observed;
                }

                Assert.IsTrue(policy.Triggered, "The exact per-path authorization boundary must change presence after native observation and before the identity read.");
                Assert.IsInstanceOfType<PublicInvalidOperationException>(failure, "A newly read physical state must not certify the older native branch.");
                Assert.AreEqual(!initiallyPresent, File.Exists(marker));
                if (!initiallyPresent)
                    Assert.AreEqual("external marker", await File.ReadAllTextAsync(marker));
                CollectionAssert.AreEqual(originalProject, await File.ReadAllBytesAsync(project));
                Assert.IsFalse(Directory.Exists(Path.Combine(fixture.Root, ".roslynmcp", "staging")));
            }
            catch (Exception cleanupFailure)
            {
                resourceFailure17 = cleanupFailure;
                throw;
            }
            finally
            {
                OwnedResourceCleanup.DisposePreservingFailure("Restore authority resource cleanup failed.", resourceFailure17, source);
            }
        }
        catch (Exception cleanupFailure)
        {
            resourceFailure16 = cleanupFailure;
            throw;
        }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Restore authority resource cleanup failed.", resourceFailure16, fixture);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Layout_ObservedRegularFileUsesNativeDirectorySuffixIntent(bool suffix)
    {
        var fixture = await NuGetInstallationFixture.CreateAsync("Owned.ConditionSuffix", "1.0.0", new Dictionary<string, byte[]> { ["lib/net10.0/_._"] = [] });
        Exception? resourceFailure18 = null;
        try
        {
            var project = Path.Combine(fixture.Root, "Owner.csproj");
            var marker = Path.Combine(fixture.Root, "marker.flag");
            await File.WriteAllTextAsync(marker, "regular file");
            await AddConditionAsync(project, suffix);
            var globals = OwnedNativeLoadContext.Properties(project, new Dictionary<string, string> { ["MarkerPath"] = marker });
            var policy = new OwnedTestWorkspacePathPolicy();
            var source = await PhysicalGraphCandidate.LoadAsync("condition-suffix-source", project, globals, policy, new WorkspaceSessionLoader(), NullLogger.Instance, null, CancellationToken.None);
            Exception? resourceFailure19 = null;
            try
            {
                Assert.AreEqual(!suffix, HasMarker(source.Solution.Projects.Single()), "Native source must distinguish file from file-with-directory-suffix.");
                var layout = await PhysicalGraphLayout.CreateAsync("condition-suffix-layout", project, source.Solution, policy, CancellationToken.None);
                Assert.AreEqual(PhysicalInputKind.File, layout.ConditionInputs.Single(input => RoslynMcp.Roslyn.Helpers.FileSystemPath.Comparer.Equals(input.FilePath, marker)).Kind);
                Assert.AreEqual("regular file", await File.ReadAllTextAsync(marker));
            }
            catch (Exception cleanupFailure)
            {
                resourceFailure19 = cleanupFailure;
                throw;
            }
            finally
            {
                OwnedResourceCleanup.DisposePreservingFailure("Restore authority resource cleanup failed.", resourceFailure19, source);
            }
        }
        catch (Exception cleanupFailure)
        {
            resourceFailure18 = cleanupFailure;
            throw;
        }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Restore authority resource cleanup failed.", resourceFailure18, fixture);
        }
    }

    private static async Task AddConditionAsync(string project, bool suffix)
    {
        var xml = XDocument.Parse(await File.ReadAllTextAsync(project));
        var root = xml.Root ?? throw new AssertFailedException("The owned native project must have a root element.");
        root.Add(new XElement("PropertyGroup", new XAttribute("Condition", "Exists('$(MarkerPath)" + (suffix ? "/" : "") + "')"),
            new XElement("DefineConstants", "$(DefineConstants);OBSERVED_MARKER")));
        await File.WriteAllTextAsync(project, xml.ToString());
    }
    private static bool HasMarker(Microsoft.CodeAnalysis.Project project) =>
        project.ParseOptions is CSharpParseOptions options && options.PreprocessorSymbolNames.Contains("OBSERVED_MARKER");
    private sealed class PresenceFlipPolicy(string marker, bool initiallyPresent) : IWorkspacePathPolicy
    {
        private readonly OwnedTestWorkspacePathPolicy _inner = new();
        internal bool Armed { get; set; }
        internal bool Triggered { get; private set; }
        public async Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        {
            await _inner.ValidateAsync(workspaceId, paths, ct);
            if (!Armed || Triggered || !paths.Contains(marker, RoslynMcp.Roslyn.Helpers.FileSystemPath.Comparer)) return;
            Triggered = true;
            if (initiallyPresent) File.Delete(marker);
            else await File.WriteAllTextAsync(marker, "external marker", ct);
        }
    }
}
