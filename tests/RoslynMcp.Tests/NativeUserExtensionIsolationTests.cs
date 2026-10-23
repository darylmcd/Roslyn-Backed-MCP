using System.Collections.Immutable;
using System.Xml.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeUserExtensionIsolationTests
{
    [TestMethod]
    [DataRow("MSBuildExtensionsPath", false)]
    [DataRow("MSBuildExtensionsPath", true)]
    [DataRow("MSBuildExtensionsPath32", false)]
    [DataRow("MSBuildExtensionsPath32", true)]
    [DataRow("MSBuildExtensionsPath64", false)]
    [DataRow("MSBuildExtensionsPath64", true)]
    [DataRow("MSBuildUserExtensionsPath", false)]
    [DataRow("MSBuildUserExtensionsPath", true)]
    public async Task NativeExtensionInputRole_RequiresAnActuallyConsumedPath(string property, bool consumed)
    {
        var root = Path.Combine(TestTempRoot.Current, "native-extension-role-" + Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            var projectDirectory = Path.Combine(root, "project");
            var extensions = Path.Combine(root, "extensions");
            Directory.CreateDirectory(projectDirectory);
            Directory.CreateDirectory(extensions);
            await File.WriteAllTextAsync(Path.Combine(extensions, "Owned.props"), "<Project><PropertyGroup><Observed>true</Observed></PropertyGroup></Project>");
            var definition = new XElement("Project");
            if (consumed) definition.Add(new XElement("Import", new XAttribute("Project", "$(" + property + ")/Owned.props"),
                new XAttribute("Condition", "Exists('$(" + property + ")/Owned.props')")));
            var path = Path.Combine(projectDirectory, "Owner.proj");
            await File.WriteAllTextAsync(path, definition.ToString());
            var globals = new Dictionary<string, string> { [property] = extensions };
            var node = EvaluatedBuildGraph.FromRoots([path], globals, ImmutableDictionary<string, byte[]>.Empty,
                CancellationToken.None).Nodes.Single();
            Assert.AreEqual(consumed, node.PathEvidence.NativeInputDirectories.Contains(extensions,
                RoslynMcp.Roslyn.Helpers.FileSystemPath.Comparer));
            Assert.IsFalse(node.PathEvidence.Directories.Contains(extensions, RoslynMcp.Roslyn.Helpers.FileSystemPath.Comparer),
                "A consumed native input context is not a writable output-directory grant.");
            Assert.AreEqual(extensions, node.Globals[property]);
            Assert.AreEqual(extensions, globals[property]);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            OwnedResourceCleanup.Dispose("Failed to release extension role fixture.", primary,
                OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OwnedUserExtension_PrivateReloadTracksPrivateImportPresence(bool privateImportPresent)
    {
        NuGetInstallationFixture? fixture = null;
        PhysicalGraphCandidate? source = null;
        PhysicalGraphStage? stage = null;
        PhysicalGraphCandidate? reloaded = null;
        Exception? primary = null;
        try
        {
            fixture = await NuGetInstallationFixture.CreateAsync("Owned.ExtensionIsolation", "1.0.0",
                new Dictionary<string, byte[]> { ["lib/net10.0/_._"] = [] });
            var projectPath = Path.Combine(fixture.Root, "Owner.csproj");
            var extensions = Path.Combine(fixture.Root, "owned-user-extensions");
            var extensionPath = Path.Combine(extensions, "Current", "Imports", "Microsoft.Common.props", "ImportBefore", "Owned.props");
            Directory.CreateDirectory(Path.GetDirectoryName(extensionPath)!);
            const string extensionText = "<Project><PropertyGroup><DefineConstants>$(DefineConstants);OWNED_PRIVATE_EXTENSION</DefineConstants></PropertyGroup></Project>";
            await File.WriteAllTextAsync(extensionPath, extensionText);
            var originalProject = await File.ReadAllBytesAsync(projectPath);
            var originalExtension = await File.ReadAllBytesAsync(extensionPath);
            var globals = OwnedNativeLoadContext.Properties(projectPath,
                new Dictionary<string, string> { ["MSBuildUserExtensionsPath"] = extensions });
            var policy = new OwnedTestWorkspacePathPolicy();
            var loader = new WorkspaceSessionLoader();
            source = await PhysicalGraphCandidate.LoadAsync("owned-extension-source", projectPath, globals,
                policy, loader, NullLogger.Instance, null, CancellationToken.None);
            Assert.IsTrue(HasExtension(source.Solution.Projects.Single()),
                "The actual native SDK source must import the owned user extension before isolation.");
            var layout = await PhysicalGraphLayout.CreateAsync("owned-extension-source", projectPath, source.Solution,
                policy, CancellationToken.None);
            var operation = OwnedGraphStageAuthority.Create("owned-extension-private", source.Solution, policy, PhysicalGraphNamespace.StagingParent);
            stage = await PhysicalGraphStage.CreateAsync("owned-extension-private", projectPath, source.Solution,
                layout, [], globals, loader, operation.Policy, operation, NullLogger.Instance, CancellationToken.None);
            stage.EnterReadAuthority();
            var privateExtension = stage.MapFromSource(extensionPath);
            Assert.AreNotEqual(extensionPath, privateExtension, "The mutable extension must have an owned private copy.");
            Assert.IsTrue(File.Exists(privateExtension), "Native isolation must actually copy the selected extension input.");
            if (!privateImportPresent) File.Delete(privateExtension);
            var privateProject = stage.MapFromSource(projectPath);
            var privateGlobals = ((MSBuildWorkspace)stage.Expected.Solution.Workspace).Properties
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            reloaded = await PhysicalGraphCandidate.LoadAsync("owned-extension-private-reload", privateProject,
                privateGlobals, policy, loader, NullLogger.Instance, null, CancellationToken.None);
            Assert.AreEqual(privateImportPresent, HasExtension(reloaded.Solution.Projects.Single()),
                "The private native SDK must consult its own extension tree rather than the unchanged source tree.");
            Assert.IsTrue(HasExtension(source.Solution.Projects.Single()));
            Assert.AreEqual(extensions, ((MSBuildWorkspace)source.Solution.Workspace).Properties["MSBuildUserExtensionsPath"]);
            Assert.AreEqual(extensions, globals["MSBuildUserExtensionsPath"]);
            CollectionAssert.AreEqual(originalProject, await File.ReadAllBytesAsync(projectPath));
            CollectionAssert.AreEqual(originalExtension, await File.ReadAllBytesAsync(extensionPath));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            OwnedResourceCleanup.Dispose("Failed to release private extension fixture.", primary,
                reloaded, stage, source, fixture);
        }
    }

    private static bool HasExtension(Microsoft.CodeAnalysis.Project project) =>
        project.ParseOptions is CSharpParseOptions options && options.PreprocessorSymbolNames.Contains("OWNED_PRIVATE_EXTENSION");
}
