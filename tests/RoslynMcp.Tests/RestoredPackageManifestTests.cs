using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class RestoredPackageManifestTests
{
    [TestInitialize]
    public void Initialize() => RoslynMcp.Roslyn.MsBuildInitializer.EnsureInitialized();

    [TestMethod]
    public void ProjectContextIdentity_RetainsTargetPlatformWhileSdkDependencyGroupsUseBaseFramework()
    {
        Assert.IsTrue(RestoredPackageManifest.SameFramework("net10.0-windows7.0", "net10.0"));
        Assert.IsFalse(RestoredPackageManifest.SameTargetFramework("net10.0-windows7.0", "net10.0"));
        Assert.IsTrue(RestoredPackageManifest.SameTargetFramework("net10.0", ".NETCoreApp,Version=v10.0"));
        Assert.IsFalse(RestoredPackageManifest.SameTargetFramework("net9.0", "net10.0"));
    }
    [TestMethod]
    [DataRow("project", "", "", true)]
    [DataRow("project", "ref/_._", "", false)]
    [DataRow("project", "ref/Contract.dll", "", true)]
    [DataRow("project", "", "net10.0", false)]
    [DataRow("project", "", ".NETCoreApp,Version=v10.0", false)]
    [DataRow("project", "", "net9.0", true)]
    [DataRow("reference", "", "", false)]
    public async Task TransitiveProjects_UseRuntimeTargetAndApplicableDirectDependencyGroups(string type,
        string compileAsset, string directGroup, bool expected)
    {
        var runtime = new Dictionary<string, object>
        {
            ["Contract/1.0.0"] = new
            {
                type,
                framework = "net10.0",
                compile = compileAsset.Length == 0
                ? new Dictionary<string, object>() : new Dictionary<string, object> { [compileAsset] = new { } }
            }
        };
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            project = new { frameworks = new Dictionary<string, object> { ["net10.0"] = new { targetAlias = "net10.0" } } },
            projectFileDependencyGroups = directGroup.Length == 0 ? new Dictionary<string, string[]>()
                : new Dictionary<string, string[]> { [directGroup] = ["Contract >= 1.0.0"] },
            packageFolders = new Dictionary<string, object>(),
            libraries = new Dictionary<string, object> { ["Contract/1.0.0"] = new { type, msbuildProject = "../Contract/Contract.csproj" } },
            targets = new Dictionary<string, object>
            {
                ["net10.0"] = new Dictionary<string, object>(),
                ["net10.0/win-x64"] = runtime,
                ["net9.0"] = new Dictionary<string, object> { ["Unreviewed/1.0.0"] = new { type = "project" } }
            }
        }));
        var manifest = await ReadManifestAsync(json.RootElement, Inputs("net10.0", "win-x64"), null,
            LanguageNames.CSharp, CancellationToken.None);
        Assert.AreEqual(expected ? 1 : 0, manifest.TransitiveProjects.Length);
        if (expected)
        {
            Assert.AreEqual(Path.Combine("..", "Contract", "Contract.csproj"), manifest.TransitiveProjects.Single().ProjectPath);
            Assert.AreEqual("net10.0", manifest.TransitiveProjects.Single().TargetFramework);
        }
    }

    [TestMethod]
    public async Task TransitiveProject_MissingLibraryIdentityIsRefusedInsteadOfInferringFromDependencyName()
    {
        using var json = JsonDocument.Parse("""{"project":{"frameworks":{"net10.0":{}}},"targets":{"net10.0":{"Forged/1.0.0":{"type":"project"}}},"libraries":{},"packageFolders":{}}""");
        await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => ReadManifestAsync(json.RootElement,
            Inputs("net10.0", ""), null, LanguageNames.CSharp, CancellationToken.None));
    }

    [TestMethod]
    public async Task ActiveFrameworkAndRid_SelectCompileAndContentIndependentlyWithoutTrustingOtherPackageFiles()
    {
        var installation = await NuGetInstallationFixture.CreateAsync("Example", "1.0.0", new Dictionary<string, byte[]> { ["ref/net10.0/Active.dll"] = await File.ReadAllBytesAsync(typeof(object).Assembly.Location), ["ref/net9.0/Wrong.dll"] = await File.ReadAllBytesAsync(typeof(object).Assembly.Location), ["analyzers/dotnet/cs/Active.dll"] = await File.ReadAllBytesAsync(typeof(object).Assembly.Location), ["analyzers/dotnet/vb/Wrong.dll"] = await File.ReadAllBytesAsync(typeof(object).Assembly.Location), ["buildTransitive/Example.props"] = System.Text.Encoding.UTF8.GetBytes("<Project />"), ["contentFiles/cs/net10.0/Input.cs.pp"] = System.Text.Encoding.UTF8.GetBytes("class Input { }"), ["contentFiles/vb/net10.0/Input.vb"] = System.Text.Encoding.UTF8.GetBytes("Class Input\nEnd Class") });
        Exception? resourceFailure8 = null;
        try
        {
            var cache = installation.Cache;
            var preprocess = Path.Combine(TestTempRoot.Current, "manifest-output");
            var package = new
            {
                type = "package",
                compile = new Dictionary<string, object>
                {
                    ["ref/net10.0/Active.dll"] = new
                    {
                    }
                }
            };
            var runtimePackage = new
            {
                type = "package",
                contentFiles = new Dictionary<string, object>
                {
                    ["contentFiles/cs/net10.0/Input.cs.pp"] = new
                    {
                        buildAction = "Compile",
                        codeLanguage = "cs",
                        ppOutputPath = "Input.cs"
                    },
                    ["contentFiles/vb/net10.0/Input.vb"] = new
                    {
                        buildAction = "Compile",
                        codeLanguage = "vb"
                    }
                }
            };
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(new { project = new { frameworks = new Dictionary<string, object> { ["net10.0"] = new { targetAlias = "net10.0" } } }, packageFolders = new Dictionary<string, object> { [cache] = new { } }, targets = new Dictionary<string, object> { ["net10.0"] = new Dictionary<string, object> { ["Example/1.0.0"] = package }, ["net10.0/win-x64"] = new Dictionary<string, object> { ["Example/1.0.0"] = runtimePackage }, ["net9.0"] = new Dictionary<string, object> { ["Other/1.0.0"] = new { type = "package", compile = new Dictionary<string, object> { ["ref/Other.dll"] = new { } } } } }, libraries = new Dictionary<string, object> { ["Example/1.0.0"] = new { type = "package", path = "example/1.0.0", files = new[] { "ref/net10.0/Active.dll", "ref/net9.0/Wrong.dll", "analyzers/dotnet/cs/Active.dll", "analyzers/dotnet/vb/Wrong.dll", "buildTransitive/Example.props" } }, ["Other/1.0.0"] = new { type = "package", path = "other/1.0.0", files = new[] { "ref/Other.dll" } } } }));
            var inputs = Inputs("net10.0", "win-x64");
            using var installedAssets = JsonDocument.Parse(await File.ReadAllBytesAsync(installation.Assets));
            var certified = JsonNode.Parse(json.RootElement.GetRawText()) ?? throw new AssertFailedException("Fixture JSON has no root.");
            certified["libraries"] = JsonNode.Parse(installedAssets.RootElement.GetProperty("libraries").GetRawText());
            using var certifiedJson = JsonDocument.Parse(certified.ToJsonString());
            var manifest = await ReadManifestAsync(certifiedJson.RootElement, inputs, preprocess, LanguageNames.CSharp, CancellationToken.None);
            CollectionAssert.AreEquivalent(new[] { Canonical(cache, "example/1.0.0/ref/net10.0/Active.dll") }, manifest.Compile.ToArray());
            CollectionAssert.AreEquivalent(new[] { Canonical(cache, "example/1.0.0/analyzers/dotnet/cs/Active.dll") }, manifest.AnalyzerCandidates.ToArray());
            Assert.IsTrue(manifest.PackageFiles.Contains(Canonical(cache, "example/1.0.0/ref/net9.0/Wrong.dll")));
            Assert.IsFalse(manifest.Compile.Contains(Canonical(cache, "example/1.0.0/ref/net9.0/Wrong.dll")));
            CollectionAssert.AreEquivalent(new[] { Canonical(preprocess, "Example/1.0.0/Input.cs") }, manifest.ContentActions.Keys.ToArray());
            Assert.IsTrue(manifest.PackageFiles.Contains(Canonical(cache, "example/1.0.0/buildTransitive/Example.props")));
        }
        catch (Exception cleanupFailure)
        {
            resourceFailure8 = cleanupFailure;
            throw;
        }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Restore authority resource cleanup failed.", resourceFailure8, installation);
        }
    }

    [TestMethod]
    public async Task MissingSelectedTarget_DoesNotFallBackToAvailableFramework()
    {
        using var json = JsonDocument.Parse("""{"project":{"frameworks":{"net9.0":{}}},"targets":{"net9.0":{}},"libraries":{},"packageFolders":{}}""");
        await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => ReadManifestAsync(json.RootElement,
            Inputs("net10.0", ""), null, LanguageNames.CSharp, CancellationToken.None));
    }

    private static string Canonical(string root, string relative) => Path.GetFullPath(relative, root);

    [TestMethod]
    public async Task MalformedNestedAssetShape_IsTypedAndDoesNotEchoValues()
    {
        using var json = JsonDocument.Parse("""{"project":{"frameworks":{"net10.0":{}}},"targets":{"net10.0":{"Example/1.0.0":{"type":"package","compile":[]}}},"libraries":{"Example/1.0.0":{"type":"package","path":"synthetic-private-package-path"}},"packageFolders":{"invalid-relative-cache":{}}}""".Replace("\"invalid-relative-cache\"", JsonSerializer.Serialize(TestTempRoot.Current), StringComparison.Ordinal));
        var failure = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => ReadManifestAsync(json.RootElement,
            Inputs("net10.0", ""), null, LanguageNames.CSharp, CancellationToken.None));
        Assert.IsFalse(failure.ToString().Contains("synthetic-private-package-path", StringComparison.Ordinal));
        Assert.IsFalse(failure.ToString().Contains("invalid-relative-cache", StringComparison.Ordinal));
        Assert.IsNull(failure.InnerException);
    }

    [TestMethod]
    public async Task RequestedCancellation_RetainsExactTokenBeforeParsing()
    {
        using var json = JsonDocument.Parse("{}");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var failure = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => ReadManifestAsync(json.RootElement,
            Inputs("net10.0", ""), null, LanguageNames.CSharp, canceled.Token));
        Assert.AreEqual(canceled.Token, failure.CancellationToken);
    }

    private static async Task<RestoredPackageManifest> ReadManifestAsync(JsonElement root, MsBuildMetadataHelper.RestoreInputs inputs, string? preprocess, string language, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var path = Path.Combine(TestTempRoot.Current, "manifest-assets-" + Guid.NewGuid().ToString("N") + ".json");
        var bytes = System.Text.Encoding.UTF8.GetBytes(root.GetRawText());
        Exception? primary = null;
        try
        {
            await File.WriteAllBytesAsync(path, bytes, ct);
            var installed = await BuildInputProvenance.ReadPackagesAsync(path, bytes, [], (paths, token) => new OwnedTestWorkspacePathPolicy().ValidateAsync("manifest", paths, token), ct);
            return RestoredPackageManifest.Read(root, inputs, preprocess, language, installed, ct);
        }
        catch (Exception failure)
        {
            primary = failure;
            throw;
        }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Manifest input fixture cleanup failed.", primary, CleanupFailureCollector.FromAction(() => File.Delete(path)));
        }
    }
    private static MsBuildMetadataHelper.RestoreInputs Inputs(string framework, string rid) => new(null, null, [], framework,
        "", rid, false, "", false, false, false, false, false, "Library");
}
