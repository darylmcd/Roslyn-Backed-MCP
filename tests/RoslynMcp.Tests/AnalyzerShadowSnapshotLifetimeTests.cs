using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class AnalyzerShadowSnapshotLifetimeTests
{
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task PreparedShadow_MainAndDependencyCannotChangeBeforeLazyLoad(bool dependency, bool replaceDirectory)
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("This contract exercises Windows deny-write/delete ownership."); return; }
        var root = Path.Combine(TestTempRoot.Current, "shadow-snapshot-" + Guid.NewGuid().ToString("N"));
        AdhocWorkspace? workspace = null;
        PhysicalDirectoryScope.OwnedRoot? shadowOwner = null;
        AnalyzerReferenceIsolation.ShadowCopyAnalyzerAssemblyLoader? loader = null;
        AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease? lease = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var source = Path.Combine(root, "source");
            var shadow = Path.Combine(root, "shadow");
            Directory.CreateDirectory(source);
            var suffix = Guid.NewGuid().ToString("N");
            var dependencyName = "OwnedDependency" + suffix;
            var mainName = "OwnedMain" + suffix;
            var originalDependency = Emit(dependencyName, "public static class Dependency {public static int Value()=>7;}");
            var replacementDependency = Emit(dependencyName, "public static class Dependency {public static int Value()=>99;}");
            var originalMain = Emit(mainName, "public static class Entry {public static int Value()=>Dependency.Value();}", originalDependency);
            var replacementMain = Emit(mainName, "public static class Entry {public static int Value()=>99;}");
            var mainPath = Path.Combine(source, mainName + ".dll");
            var dependencyPath = Path.Combine(source, dependencyName + ".dll");
            await File.WriteAllBytesAsync(mainPath, originalMain);
            await File.WriteAllBytesAsync(dependencyPath, originalDependency);
            workspace = new AdhocWorkspace();
            var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "Owner", "Owner",
                LanguageNames.CSharp, filePath: Path.Combine(source, "Owner.csproj")));
            var solution = project.Solution.AddAnalyzerReference(project.Id, new AnalyzerFileReference(mainPath, UnusedLoader.Instance));
            var policy = new OwnedTestWorkspacePathPolicy();
            Task Authorize(IReadOnlyList<string> paths, CancellationToken token) => policy.ValidateAsync("shadow-snapshot", paths, token);
            var catalog = await AnalyzerInputCatalog.CaptureAsync(solution, [], Authorize,
                (path, token) => FileSnapshotCapture.ReadIfPresentAsync(path, File.ReadAllBytesAsync, token),
                (path, token) => FileSnapshotCapture.ReadIfPresentAsync(path, PhysicalInputReader.ReadDigestAsync, token), CancellationToken.None);
            shadowOwner = PhysicalDirectoryScope.CreateOwnedRoot(shadow);
            loader = await AnalyzerReferenceIsolation.ShadowCopyAnalyzerAssemblyLoader.CreateAsync(catalog.DirectoryEntries(mainPath),
                shadowOwner, string.Empty, NullLogger.Instance, isCollectible: true, CancellationToken.None);
            lease = new AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease(shadowOwner, [loader], 1, NullLogger.Instance);
            shadowOwner = null;
            {
                var target = Path.Combine(shadow, Path.GetFileName(dependency ? dependencyPath : mainPath));
                try
                {
                    if (replaceDirectory)
                    {
                        Directory.Move(shadow, Path.Combine(root, "retained-original-shadow"));
                        Directory.CreateDirectory(shadow);
                        await File.WriteAllBytesAsync(Path.Combine(shadow, Path.GetFileName(mainPath)), originalMain);
                        await File.WriteAllBytesAsync(Path.Combine(shadow, Path.GetFileName(dependencyPath)), originalDependency);
                    }
                    await File.WriteAllBytesAsync(target, dependency ? replacementDependency : replacementMain);
                }
                catch (IOException) { /* A lifetime-bound native file handle may reject the attempted change. */ }
                Assert.AreEqual(7, ReadValue(loader, mainPath, shadow), "Lazy execution must consume the admitted main and dependency bytes.");
                CollectionAssert.AreEqual(originalMain, await File.ReadAllBytesAsync(mainPath));
                CollectionAssert.AreEqual(originalDependency, await File.ReadAllBytesAsync(dependencyPath));
            }
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Analyzer shadow snapshot fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => workspace?.Dispose()),
                CleanupFailureCollector.FromAction(() => { if (lease is not null) lease.Dispose(); else loader?.UnloadAndRelease(); }),
                CleanupFailureCollector.FromAction(() => shadowOwner?.Dispose()),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int ReadValue(AnalyzerReferenceIsolation.ShadowCopyAnalyzerAssemblyLoader loader, string source, string shadow)
    {
        var assembly = loader.LoadFromPath(source);
        Assert.AreEqual(Path.Combine(shadow, Path.GetFileName(source)), assembly.Location, "Retain the native assembly-location contract.");
        return (int)assembly.GetType("Entry", throwOnError: true)!.GetMethod("Value")!.Invoke(null, null)!;
    }

    private static byte[] Emit(string name, string source, byte[]? dependency = null)
    {
        var references = new List<MetadataReference> { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) };
        if (dependency is not null) references.Add(MetadataReference.CreateFromImage(ImmutableArray.CreateRange(dependency)));
        var compilation = CSharpCompilation.Create(name, [CSharpSyntaxTree.ParseText(source)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output = new MemoryStream();
        var result = compilation.Emit(output);
        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return output.ToArray();
    }

    private sealed class UnusedLoader : IAnalyzerAssemblyLoader
    {
        internal static readonly UnusedLoader Instance = new();
        public void AddDependencyLocation(string fullPath) { }
        public System.Reflection.Assembly LoadFromPath(string fullPath) => throw new AssertFailedException("Catalog capture cannot load analyzer code.");
    }
}
