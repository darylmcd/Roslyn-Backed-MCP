using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class BuildInputProvenanceTests
{
    [TestMethod]
    public void IndependentRegisteredSdkCatalog_AdmitsWorkloadManifestsWithoutGrantingInstallationAncestor()
    {
        RoslynMcp.Roslyn.MsBuildInitializer.EnsureInitialized();
        var registered = RegisteredSdkDirectory();
        var sdkDirectory = Path.GetDirectoryName(registered);
        Assert.IsNotNull(sdkDirectory);
        Assert.AreEqual("sdk", Path.GetFileName(sdkDirectory), ignoreCase: true);
        var installation = Path.GetDirectoryName(sdkDirectory);
        Assert.IsNotNull(installation);
        var manifests = Path.Combine(installation, "sdk-manifests");
        Assert.IsTrue(Directory.Exists(manifests), "The registered workload SDK fixture must include its native manifest directory.");
        var roots = BuildInputProvenance.ToolRoots();
        Assert.IsTrue(roots.Contains(manifests, FileSystemPath.Comparer));
        Assert.IsFalse(roots.Contains(installation, FileSystemPath.Comparer));
        var provenance = new ImmutablePathProvenance(roots, [], []);
        Assert.IsTrue(provenance.IsImmutable(Path.Combine(manifests, "owned-manifest", "WorkloadManifest.targets")));
        Assert.IsFalse(provenance.IsImmutable(Path.Combine(installation, "unrelated-authored-input")));
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static string RegisteredSdkDirectory() => Path.GetDirectoryName(typeof(Microsoft.Build.Evaluation.Project).Assembly.Location)
        ?? throw new AssertFailedException("The initialized build runtime must have an installation directory.");

    [TestMethod]
    public async Task RecordedPackageLibraries_PreserveOldCacheWithoutTrustingCacheAncestorOrProjectLibraries()
    {
        using var fixture = await NuGetInstallationFixture.CreateAsync("Example.Package", "1.2.3",
            new Dictionary<string, byte[]> { ["lib/net10.0/Reference.dll"] = await File.ReadAllBytesAsync(typeof(object).Assembly.Location) });
        var recorded = fixture.Cache;
        var reference = Path.Combine(recorded, "example.package", "1.2.3", "lib", "net10.0", "Reference.dll");
        var policy = new OwnedTestWorkspacePathPolicy();
        await policy.ValidateAsync("provenance", [fixture.Assets], CancellationToken.None);
        var proof = await BuildInputProvenance.ReadPackagesAsync([fixture.Assets], [],
            (paths, token) => policy.ValidateAsync("provenance", paths, token), CancellationToken.None);
        Assert.IsTrue(proof.Files.Contains(reference, FileSystemPath.Comparer));
        Assert.IsFalse(proof.Files.Contains(recorded, FileSystemPath.Comparer));
        Assert.IsFalse(proof.Files.Contains(Path.GetDirectoryName(reference)!, FileSystemPath.Comparer));
        var toolRoots = BuildInputProvenance.ToolRoots();
        Assert.IsFalse(toolRoots.Any(root => FileSystemPath.Comparer.Equals(root, fixture.Root)
            || FileSystemPath.IsStrictDescendant(root, fixture.Root)));
    }
    [TestMethod]
    public async Task Layout_UsesApprovedRecordedPackageLibraryAndKeepsAuthoredSiblingMutable()
    {
        var installation = await NuGetInstallationFixture.CreateAsync("Example.Package", "1.2.3",
            new Dictionary<string, byte[]> { ["lib/Reference.dll"] = await File.ReadAllBytesAsync(typeof(object).Assembly.Location) });
        Exception? primaryFailure = null;
        try
        {
            var root = installation.Root;
            var cache = installation.Cache;
            var projectDirectory = Path.Combine(cache, "Authored");
            var packageDirectory = Path.Combine(cache, "example.package", "1.2.3");
            var projectPath = Path.Combine(projectDirectory, "A.csproj");
            var codePath = Path.Combine(projectDirectory, "A.cs");
            var assets = Path.Combine(projectDirectory, "obj", "project.assets.json");
            var referencePath = Path.Combine(packageDirectory, "lib", "Reference.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(assets)!);
            Directory.CreateDirectory(Path.GetDirectoryName(referencePath)!);
            await File.WriteAllTextAsync(projectPath, $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit><RestorePackagesPath>{System.Security.SecurityElement.Escape(root)}</RestorePackagesPath></PropertyGroup></Project>");
            await File.WriteAllTextAsync(codePath, "class A { }");
            File.Copy(installation.Assets, assets);
            using var workspace = Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace.Create(OwnedNativeLoadContext.Properties(projectPath, new Dictionary<string, string>()));
            var id = ProjectId.CreateNewId();
            var solution = workspace.CurrentSolution.AddProject(ProjectInfo.Create(id, VersionStamp.Create(), "A", "A", LanguageNames.CSharp,
                filePath: projectPath, metadataReferences: [MetadataReference.CreateFromFile(referencePath)]))
                .AddDocument(DocumentId.CreateNewId(id), "A.cs", SourceText.From("class A { }"), filePath: codePath);

            var policy = new OwnedTestWorkspacePathPolicy();
            var layout = await PhysicalGraphLayout.CreateAsync("package-layout", projectPath, solution, policy, CancellationToken.None);
            Assert.IsTrue(layout.IsImmutablePath(referencePath));
            Assert.IsFalse(layout.IsImmutablePath(projectPath));
            Assert.IsFalse(layout.IsImmutablePath(codePath));
            Assert.IsFalse(layout.ImmutableRoots.Contains(cache, FileSystemPath.Comparer));
            Assert.IsFalse(layout.ImmutableRoots.Contains(root, FileSystemPath.Comparer));
            var destination = Path.Combine(projectDirectory, ".roslynmcp", "probe");
            Assert.AreEqual(referencePath, layout.MapInputPath(destination, referencePath));
            Assert.AreNotEqual(codePath, layout.MapInputPath(destination, codePath));
            Assert.IsFalse(layout.InputFiles.Contains(referencePath, FileSystemPath.Comparer));
            Assert.IsTrue(layout.InputFiles.Contains(codePath, FileSystemPath.Comparer));
        }
        catch (Exception failure)
        {
            primaryFailure = failure;
            throw;
        }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Native fixture cleanup failed.", primaryFailure,
                installation);
        }
    }

    [TestMethod]
    [DataRow("[]")]
    [DataRow("null")]
    [DataRow("17")]
    [DataRow("\"synthetic-private-assets-root\"")]
    public async Task NonObjectAssetsRoot_RefusesWithAuthoredSafeType(string json)
    {
        var directory = Path.Combine(TestTempRoot.Current, nameof(BuildInputProvenanceTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Exception? primaryFailure = null;
        try
        {
            var assets = Path.Combine(directory, "project.assets.json");
            await File.WriteAllTextAsync(assets, json);
            await new OwnedTestWorkspacePathPolicy().ValidateAsync("provenance", [assets], CancellationToken.None);
            var failure = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => BuildInputProvenance.ReadPackagesAsync([assets], [], (paths, token) => new OwnedTestWorkspacePathPolicy().ValidateAsync("package-proof", paths, token), CancellationToken.None));
            Assert.IsFalse(failure.ToString().Contains("synthetic-private-assets-root", StringComparison.Ordinal));
            Assert.IsNull(failure.InnerException);
        }
        catch (Exception failure)
        {
            primaryFailure = failure;
            throw;
        }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Native fixture cleanup failed.", primaryFailure,
                OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(directory)));
        }
    }

    [TestMethod]
    [DataRow("../outside")]
    [DataRow(".")]
    public async Task EscapingOrWholeFolderPackagePaths_RefuseWithoutBroadeningOwnership(string packagePath)
    {
        var directory = Path.Combine(TestTempRoot.Current, nameof(BuildInputProvenanceTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Exception? primaryFailure = null;
        try
        {
            var assets = Path.Combine(directory, "project.assets.json");
            await File.WriteAllTextAsync(assets, JsonSerializer.Serialize(new
            {
                packageFolders = new Dictionary<string, object> { [directory] = new { } },
                libraries = new Dictionary<string, object> { ["Package/1"] = new { type = "package", path = packagePath } }
            }));
            await new OwnedTestWorkspacePathPolicy().ValidateAsync("provenance", [assets], CancellationToken.None);
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => BuildInputProvenance.ReadPackagesAsync([assets], [], (paths, token) => new OwnedTestWorkspacePathPolicy().ValidateAsync("package-proof", paths, token), CancellationToken.None));
        }
        catch (Exception failure)
        {
            primaryFailure = failure;
            throw;
        }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Native fixture cleanup failed.", primaryFailure,
                OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(directory)));
        }
    }
}
