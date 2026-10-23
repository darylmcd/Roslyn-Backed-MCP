using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class LocalPackageForkTests : IsolatedWorkspaceTestBase
{
    [TestMethod]
    public void PhysicalLayout_RootDirectoryTransportRoundTripsWithoutDoubleRelocation()
    {
        var root = Path.Combine(TestTempRoot.Current, "root-transport");
        var fork = Path.Combine(root, ".roslynmcp", "forks", "transport");
        var layout = new PhysicalGraphLayout(root, root, [root], [], [], new([], [], [root]), [root], [], [], [], [], [], [], InstalledPackageAuthority.Merge([]), [], [], null);
        var suffix = Path.DirectorySeparatorChar.ToString();
        Assert.AreEqual(root, layout.MapFrom(fork, fork + suffix));
        Assert.AreEqual(fork + suffix, layout.MapInputPath(fork, fork + suffix));
        Assert.AreEqual(fork + suffix, BuildPathRelocation.RelocateGlobal("ProjectDir", root + suffix,
            path => layout.MapInputPath(fork, path), BuildPathRelocation.KnownPathProperties));
    }

    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();
    [TestMethod]
    [DataRow("RestoreSources")]
    [DataRow("RestoreAdditionalProjectSources")]
    [DataRow("RestoreAdditionalProjectFallbackFolders")]
    public void NuGetSourceProperties_PreserveExactSharedInputLocations(string name)
    {
        var value = Path.Combine(TestTempRoot.Current, "configured-feed") + ";https://fixture.invalid/v3/index.json";
        var properties = BuildPathRelocation.KnownPathProperties.Append(name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var calls = 0;
        var actual = BuildPathRelocation.RelocateGlobal(name, value, _ => { calls++; return "unexpected-mapping"; }, properties);
        Assert.AreEqual(value, actual);
        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    public async Task ReviewedPackageVersion_RestoresCertifiedAssemblyContentAndCompilerInputs()
    {
        var fixture = await CreateFixtureAsync();
        WorkspaceForkApplyResultDto? fork = null;
        Exception? primary = null;
        try
        {
            var source = fixture.Source;
            var original = WorkspaceManager.GetCurrentSolution(source.WorkspaceId);
            var project = original.Projects.Single();
            var sourceOptions = project.ParseOptions as CSharpParseOptions;
            Assert.IsNotNull(sourceOptions);
            Assert.IsTrue(sourceOptions.PreprocessorSymbolNames.Contains("PACKAGE_V1"));
            var authored = project.AdditionalDocuments.Single(document => document.FilePath == fixture.ProjectPath);
            var xml = XDocument.Parse((await authored.GetTextAsync()).ToString(), LoadOptions.PreserveWhitespace);
            xml.Descendants("PackageReference").Single().SetAttributeValue("Version", "2.0.0");
            var modified = original.WithAdditionalDocumentText(authored.Id, SourceText.From(xml.ToString(SaveOptions.DisableFormatting)));
            var changes = await SolutionDiffHelper.ComputeChangesAsync(original, modified,
                SolutionDiffReadContext.ForWorkspace(WorkspaceManager, source.WorkspaceId), CancellationToken.None);
            var token = PreviewStore.Store(source.WorkspaceId, original, modified,
                WorkspaceManager.GetCurrentVersion(source.WorkspaceId), "upgrade local package", changes, PreviewKind.Unspecified);
            var sourceFiles = new[] { fixture.ProjectPath, Path.Combine(source.RootPath, "Entry.cs"),
                Path.Combine(source.RootPath, "obj", "project.assets.json"),
                Path.Combine(source.RootPath, "obj", "Derived.csproj.nuget.g.props") };
            var before = new Dictionary<string, byte[]>();
            foreach (var file in sourceFiles) before[file] = await File.ReadAllBytesAsync(file);
            var version = WorkspaceManager.GetCurrentVersion(source.WorkspaceId);
            fork = await Service(new DotnetCommandRunner(), PhysicalGraphNamespace.ForkParent).ApplyAsync(source.WorkspaceId, token, "keep", false, null,
                "package-v2", CancellationToken.None);
            Assert.IsTrue(fork.Success);
            var loaded = WorkspaceManager.GetCurrentSolution(fork.ForkWorkspaceId!).Projects.Single();
            var options = loaded.ParseOptions as CSharpParseOptions;
            Assert.IsNotNull(options);
            CollectionAssert.Contains(options.PreprocessorSymbolNames.ToArray(), "PACKAGE_V2");
            CollectionAssert.DoesNotContain(options.PreprocessorSymbolNames.ToArray(), "PACKAGE_V1");
            var oldPackageRoot = Path.Combine(fixture.CachePath, "fixture.derived.inputs", "1.0.0");
            Assert.IsFalse(loaded.Documents.Any(document => document.FilePath is { } path
                && FileSystemPath.IsStrictDescendant(oldPackageRoot, path)));
            var compilation = await loaded.GetCompilationAsync();
            Assert.IsNotNull(compilation);
            foreach (var name in new[] { "CertifiedPackageLibrary.Marker", "CertifiedPackageContent.Marker" })
            {
                var type = compilation.GetTypeByMetadataName(name);
                Assert.IsNotNull(type);
                Assert.AreEqual("2.0.0", ((IFieldSymbol)type.GetMembers("Version").Single()).ConstantValue);
            }
            foreach (var (path, bytes) in before) CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(path));
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(source.WorkspaceId));
            Assert.IsNotNull(PreviewStore.Retrieve(token));
        }
        catch (Exception failure)
        {
            primary = failure;
            foreach (var key in new[] { "LayoutMapFailureKind", "LayoutMapDirectorySuffix", "SdkGeneratedConfig", "DifferentLineIndices", "GeneratedConfigDifferenceKinds" })
                if (failure.Data.Contains(key)) Console.WriteLine(key + ": " + failure.Data[key]);
            throw;
        }
        finally { await CleanupAsync(fixture, fork, primary); }
    }

    [TestMethod]
    public async Task DirectPackageMetadataSnapshotTampering_IsRefusedBeforeForkEffects()
    {
        var fixture = await CreateFixtureAsync();
        var namespaceRoot = Path.Combine(TestTempRoot.Current, nameof(LocalPackageForkTests) + "-" + Guid.NewGuid().ToString("N"));
        PhysicalDirectoryScope.OwnedRoot? rootOwner = null;
        PhysicalDirectoryScope.OwnedRoot? forkParent = null;
        PhysicalDirectoryScope.OwnedRoot? probe = null;
        Exception? primary = null;
        try
        {
            rootOwner = PhysicalDirectoryScope.CreateOwnedRoot(namespaceRoot);
            forkParent = PhysicalDirectoryScope.CreateOwnedRoot(Path.Combine(rootOwner.Path, "roslynmcp-forks"));
            probe = PhysicalDirectoryScope.CreateOwnedRoot(Path.Combine(forkParent.Path, "oracle-child"));
            CollectionAssert.AreEqual(new[] { probe.Path }, Directory.GetFileSystemEntries(forkParent.Path),
                "The no-effects oracle must observe an actual owned child in the exact service parent.");
            var probePath = probe.Path;
            var retiredProbe = probe;
            probe = null;
            retiredProbe.Dispose();
            TestFixtureFileSystem.DeleteDirectoryIfExists(probePath);
            Assert.IsEmpty(Directory.GetFileSystemEntries(forkParent.Path),
                "The actual service parent must start empty after the oracle control is reclaimed.");
            var source = fixture.Source;
            var original = WorkspaceManager.GetCurrentSolution(source.WorkspaceId);
            var project = original.Projects.Single();
            var reference = project.MetadataReferences.OfType<PortableExecutableReference>()
                .Single(reference => Path.GetFileName(reference.FilePath) == "Fixture.Derived.Inputs.dll");
            var modified = original.RemoveMetadataReference(project.Id, reference);
            var token = PreviewStore.Store(source.WorkspaceId, original, modified,
                WorkspaceManager.GetCurrentVersion(source.WorkspaceId), "invalid direct package metadata", [], PreviewKind.Unspecified);
            var bytes = await File.ReadAllBytesAsync(fixture.ProjectPath);
            var runner = new RejectingRunner();
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => Service(runner, forkParent.Path).ApplyAsync(source.WorkspaceId,
                token, "keep", false, null, "invalid-metadata", CancellationToken.None));
            Assert.AreEqual(0, runner.Calls);
            Assert.IsEmpty(Directory.GetFileSystemEntries(forkParent.Path), "Invalid metadata must not create any child in the actual issued fork parent.");
            CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(fixture.ProjectPath));
            Assert.IsNotNull(PreviewStore.Retrieve(token));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Metadata refusal namespace cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => probe?.Dispose()),
                CleanupFailureCollector.FromAction(() => forkParent?.Dispose()),
                CleanupFailureCollector.FromAction(() => rootOwner?.Dispose()),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(namespaceRoot)),
                () => CleanupAsync(fixture, null, null));
        }
    }

    [TestMethod]
    public async Task ActivePackageXmlImport_RemainsHashBoundAfterCompilerAuthorityCapture()
    {
        var fixture = await CreateFixtureAsync();
        Exception? primary = null;
        var import = Path.Combine(fixture.CachePath, "fixture.derived.inputs", "1.0.0", "tools", "CompilerIntent.xml");
        byte[]? before = null;
        try
        {
            before = await File.ReadAllBytesAsync(import);
            var receipt = await RestoredCompilerInputAuthority.CaptureAsync(fixture.Source.WorkspaceId,
                WorkspaceManager.GetCurrentSolution(fixture.Source.WorkspaceId), static path => path,
                new OwnedTestWorkspacePathPolicy(), RestoreArtifactPhase.BeforeRestore, CancellationToken.None);
            await File.WriteAllTextAsync(import, "<Project><PropertyGroup><DefineConstants>UNREVIEWED_PACKAGE_INPUT</DefineConstants></PropertyGroup></Project>");
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => receipt.AssertCurrentAsync(
                fixture.Source.WorkspaceId, new OwnedTestWorkspacePathPolicy(), CancellationToken.None));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Package import fixture cleanup failed.", primary,
                () => before is null ? ValueTask.CompletedTask : new ValueTask(File.WriteAllBytesAsync(import, before)),
                () => CleanupAsync(fixture, null, null));
        }
    }

    private static WorkspaceForkApplyService Service(IDotnetCommandRunner runner, string forkParent) => new(PathPolicy,
        WorkspaceManager, WorkspaceExecutionGate, PreviewStore,
        new WorkspaceValidationService(CompileCheckService, DiagnosticService, TestDiscoveryService, TestRunnerService, WorkspaceManager, ChangeTracker),
        TestRunnerService, runner, NullLogger<WorkspaceForkApplyService>.Instance, forkParent, TimeProvider.System);

    private sealed class RejectingRunner : IDotnetCommandRunner
    {
        internal int Calls { get; private set; }
        public Task<CommandExecutionDto> RunAsync(string workingDirectory, string targetPath, IReadOnlyList<string> arguments, CancellationToken ct) =>
            RunAsync(workingDirectory, targetPath, arguments, null, ct);
        public Task<CommandExecutionDto> RunAsync(string workingDirectory, string targetPath, IReadOnlyList<string> arguments,
            IReadOnlyList<EarlyKillPattern>? earlyKillPatterns, CancellationToken ct)
        {
            Calls++;
            throw new AssertFailedException("An invalid snapshot must not execute a command.");
        }
    }

    private sealed record PackageFixture(IsolatedWorkspaceScope Source, string ProjectPath, string CachePath);

    private static async Task<PackageFixture> CreateFixtureAsync()
    {
        var id = Guid.NewGuid().ToString("N");
        var root = Path.Combine(TestTempRoot.Current, "derived-package-" + id);
        var cache = Path.Combine(TestTempRoot.Current, "derived-cache-" + id);
        var projectPath = Path.Combine(root, "Derived.csproj");
        var source = CreateWorkspaceScope(root, projectPath, WorkspaceManager);
        try
        {
            Directory.CreateDirectory(root);
            var feed = Path.Combine(root, "feed");
            Directory.CreateDirectory(feed);
            await File.WriteAllTextAsync(Path.Combine(root, "global.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(root, "Directory.Build.props"), "<Project />");
            foreach (var version in new[] { "1.0.0", "2.0.0" }) await CreatePackageAsync(feed, version);
            var xml = new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0"), new XElement("RestoreSources", feed),
                    new XElement("RestorePackagesPath", cache)),
                new XElement("ItemGroup", new XElement("PackageReference", new XAttribute("Include", "Fixture.Derived.Inputs"), new XAttribute("Version", "1.0.0")),
                    new XElement("AdditionalFiles", new XAttribute("Include", "Derived.csproj"))));
            await File.WriteAllTextAsync(projectPath, xml.ToString(SaveOptions.DisableFormatting));
            await File.WriteAllTextAsync(Path.Combine(root, "Entry.cs"), "namespace Authored; public class Entry { }\n");
            await RestoreWorkspaceAsync(source);
            await source.LoadAsync();
            return new(source, projectPath, cache);
        }
        catch (Exception failure)
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Local package fixture initialization failed.", failure,
                () => source.DisposeAsync(), CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(cache)));
            throw;
        }
    }

    private static async Task CreatePackageAsync(string feed, string version)
    {
        var syntax = CSharpSyntaxTree.ParseText("namespace CertifiedPackageLibrary { public class Marker { public const string Version = \"" + version + "\"; } }");
        var compilation = CSharpCompilation.Create("Fixture.Derived.Inputs", [syntax],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)], new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var assembly = new MemoryStream();
        var emitted = compilation.Emit(assembly);
        Assert.IsTrue(emitted.Success, string.Join("\n", emitted.Diagnostics));
        await NuGetInstallationFixture.WriteArchiveAsync(Path.Combine(feed, "Fixture.Derived.Inputs." + version + ".nupkg"),
            new Dictionary<string, byte[]>
            {
                ["Fixture.Derived.Inputs.nuspec"] = Encoding.UTF8.GetBytes(
                    "<package><metadata><id>Fixture.Derived.Inputs</id><version>" + version + "</version><authors>Fixture</authors><description>Local synthetic restore input fixture</description>"
                    + "<contentFiles><files include=\"cs/net10.0/PackageContent.cs\" buildAction=\"Compile\" /></contentFiles></metadata></package>"),
                ["lib/net10.0/Fixture.Derived.Inputs.dll"] = assembly.ToArray(),
                ["build/Fixture.Derived.Inputs.props"] = Encoding.UTF8.GetBytes("<Project><Import Project=\"../tools/CompilerIntent.xml\" /></Project>"),
                ["tools/CompilerIntent.xml"] = Encoding.UTF8.GetBytes("<Project><PropertyGroup><DefineConstants>$(DefineConstants);PACKAGE_V" + version[0] + "</DefineConstants></PropertyGroup></Project>"),
                ["contentFiles/cs/net10.0/PackageContent.cs"] = Encoding.UTF8.GetBytes("namespace CertifiedPackageContent { public class Marker { public const string Version = \"" + version + "\"; } }")
            });
    }
    private static ValueTask CleanupAsync(PackageFixture fixture, WorkspaceForkApplyResultDto? fork, Exception? primary)
    {
        var steps = new List<Func<ValueTask>>();
        if (fork is not null)
        {
            steps.Add(CleanupFailureCollector.FromAction(() => WorkspaceManager.Close(fork.ForkWorkspaceId!)));
            steps.Add(CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(fork.ForkPath)));
        }
        steps.Add(() => fixture.Source.DisposeAsync());
        steps.Add(CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(fixture.CachePath)));
        return CleanupFailureCollector.RunAfterFailureAsync("Local package fixture cleanup failed.", primary, [.. steps]);
    }
}
