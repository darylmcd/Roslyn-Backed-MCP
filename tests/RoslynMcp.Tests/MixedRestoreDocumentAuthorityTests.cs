using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class MixedRestoreDocumentAuthorityTests
{
    [TestMethod]
    public async Task MixedNuGetAndAuthoredOccurrences_RequireMatchingManifestContentAction()
    {
        NuGetInstallationFixture? fixture = null;
        PhysicalGraphCandidate? beforeCandidate = null;
        PhysicalGraphCandidate? afterCandidate = null;
        Exception? primary = null;
        try
        {
            fixture = await NuGetInstallationFixture.CreateAsync("Owned.MixedOrigin", "1.0.0",
                new Dictionary<string, byte[]>
                {
                    ["sources/Input.cs"] = Encoding.UTF8.GetBytes("// A package member without a contentFiles build action.")
                });
            var project = Path.Combine(fixture.Root, "Owner.csproj");
            var input = Path.Combine(fixture.Cache, "owned.mixedorigin", "1.0.0", "sources", "Input.cs");
            var props = Path.Combine(fixture.Root, "obj", "Owner.csproj.nuget.g.props");
            using (var assets = JsonDocument.Parse(await File.ReadAllTextAsync(fixture.Assets)))
            {
                var package = assets.RootElement.GetProperty("targets").EnumerateObject().Single().Value
                    .GetProperty("Owned.MixedOrigin/1.0.0");
                Assert.IsFalse(package.TryGetProperty("contentFiles", out _),
                    "Exact package membership alone must not certify a NuGet Compile action.");
            }
            var generated = XDocument.Load(props);
            var ns = generated.Root!.Name.Namespace;
            generated.Root.Add(new XElement(ns + "ItemGroup", new XElement(ns + "Compile", new XAttribute("Include", input))));
            await File.WriteAllTextAsync(props, generated.ToString());
            var policy = new ForkInvocationPathPolicy(new OwnedTestWorkspacePathPolicy());
            beforeCandidate = await PhysicalGraphCandidate.LoadAsync("mixed-document-before", project,
                fixture.LoadProperties, policy, new WorkspaceSessionLoader(), NullLogger.Instance, null, CancellationToken.None);
            var source = beforeCandidate.Solution;
            Assert.AreEqual(2, source.Projects.Single().Documents.Count(document => FileSystemPath.Comparer.Equals(document.FilePath, input)),
                "The native premise requires separate generated-props and SDK-glob occurrences.");
            var before = await RestoredCompilerInputAuthority.CaptureAsync("mixed-document-before", source,
                static path => path, policy, RestoreArtifactPhase.BeforeRestore, CancellationToken.None);

            var restored = await new DotnetCommandRunner().RunAsync(fixture.Root, project,
                MsBuildCommandProperties.Append(["restore", project, "--force", "--nologo", "--source", Path.Combine(fixture.Root, "feed"),
                    "--packages", fixture.Cache], fixture.LoadProperties), CancellationToken.None);
            Assert.IsTrue(restored.Succeeded, "Real restore must replace the synthetic generated props item.");
            afterCandidate = await PhysicalGraphCandidate.LoadAsync("mixed-document-after", project,
                fixture.LoadProperties, policy, new WorkspaceSessionLoader(), NullLogger.Instance, null, CancellationToken.None);
            Assert.AreEqual(1, afterCandidate.Solution.Projects.Single().Documents.Count(document => FileSystemPath.Comparer.Equals(document.FilePath, input)),
                "Restore must remove only the unsupported generated item, leaving the authored SDK glob.");
            var after = await RestoredCompilerInputAuthority.RefreshAfterSuccessfulRestoreAsync("mixed-document-after", afterCandidate,
                static path => path, policy, finalInvocation: true, CancellationToken.None);
            var predicted = await before.WithRefreshedInputsAsync("mixed-document-after", policy, source, afterCandidate, after,
                static path => path, static path => path, new HashSet<string>(), new HashSet<string>(), CancellationToken.None);
            Assert.AreEqual(2, predicted.Projects.Single().Documents.Count(document => FileSystemPath.Comparer.Equals(document.FilePath, input)),
                "An unsupported NuGet action cannot authorize removing a reviewed occurrence.");
            await Assert.ThrowsExactlyAsync<PhysicalGraphMismatchException>(() => SolutionFileMutationPlanner.AssertEquivalentAsync(
                predicted, afterCandidate.Solution, static path => path, static path => path, true, new HashSet<string>(), CancellationToken.None));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Mixed restore document fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => afterCandidate?.Dispose()),
                CleanupFailureCollector.FromAction(() => beforeCandidate?.Dispose()),
                CleanupFailureCollector.FromAction(() => fixture?.Dispose()));
        }
    }
}
