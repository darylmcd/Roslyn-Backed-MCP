using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Security.Cryptography;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class AnalyzerAssemblyBindingTests
{
    [TestMethod]
    [DataRow("1.0.0.0", "neutral", "null", false)]
    [DataRow("3.0.0.0", "neutral", "null", true)]
    [DataRow("2.0.0.0", "fr", "null", false)]
    [DataRow("2.0.0.0", "neutral", "0123456789abcdef", false)]
    [DataRow("2.0.0.0", "neutral", "null", true)]
    public void Compatible_PreservesAlcVersionRuleAndRequestedCultureAndToken(string version, string culture, string token, bool compatible)
    {
        var requested = new AssemblyName("OwnedDependency, Version=2.0.0.0, Culture=neutral, PublicKeyToken=null");
        var candidate = new AssemblyName($"OwnedDependency, Version={version}, Culture={culture}, PublicKeyToken={token}");
        Assert.AreEqual(compatible, AnalyzerAssemblyBinding.Compatible(requested, candidate));
        var strong = new AssemblyName("OwnedDependency, Version=2.0.0.0, Culture=neutral, PublicKeyToken=0123456789abcdef");
        Assert.IsFalse(AnalyzerAssemblyBinding.Compatible(strong, requested));
        Assert.IsTrue(AnalyzerAssemblyBinding.Compatible(new AssemblyName("OwnedDependency"), candidate));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DuplicateSimpleNames_PreserveExactRequestsOrRefuseAmbiguousIdentity(bool sameIdentity)
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("The production shadow-loader contract is Windows-specific.");
        var root = Path.Combine(TestTempRoot.Current, "analyzer-binding-" + Guid.NewGuid().ToString("N"));
        var shadow = Path.Combine(root, "shadow");
        PhysicalDirectoryScope.OwnedRoot? shadowOwner = null;
        AnalyzerReferenceIsolation.ShadowCopyAnalyzerAssemblyLoader? loader = null;
        AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease? lease = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            byte[] Emit(string version, int marker)
            {
                var compilation = CSharpCompilation.Create("OwnedDependency", [CSharpSyntaxTree.ParseText(
                    $"[assembly: System.Reflection.AssemblyVersion(\"{version}\")] public class Marker {{ public int Value() => {marker}; }}")],
                    [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)], new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                using var stream = new MemoryStream();
                var emitted = compilation.Emit(stream);
                Assert.IsTrue(emitted.Success, string.Join("\n", emitted.Diagnostics));
                return stream.ToArray();
            }
            var first = Emit("1.0.0.0", 1);
            var second = Emit(sameIdentity ? "1.0.0.0" : "2.0.0.0", 2);
            var firstPath = Path.Combine(root, "First.dll");
            var secondPath = Path.Combine(root, "Second.dll");
            await File.WriteAllBytesAsync(firstPath, first);
            await File.WriteAllBytesAsync(secondPath, second);
            string Identity(byte[] bytes)
            {
                using var pe = new System.Reflection.PortableExecutable.PEReader(new MemoryStream(bytes, writable: false));
                return AnalyzerAssemblyBinding.DefinitionIdentity(pe.GetMetadataReader());
            }
            var firstIdentity = Identity(first);
            var secondIdentity = Identity(second);
            var entries = new[]
            {
                new AnalyzerInputCatalog.Entry(firstPath, firstIdentity, ImmutableArray.CreateRange(first), Convert.ToHexString(SHA256.HashData(first))),
                new AnalyzerInputCatalog.Entry(secondPath, secondIdentity, ImmutableArray.CreateRange(second), Convert.ToHexString(SHA256.HashData(second)))
            };
            shadowOwner = PhysicalDirectoryScope.CreateOwnedRoot(shadow);
            loader = await AnalyzerReferenceIsolation.ShadowCopyAnalyzerAssemblyLoader.CreateAsync(entries, shadowOwner, string.Empty, NullLogger.Instance, true, CancellationToken.None);
            lease = new AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease(shadowOwner, [loader], 1, NullLogger.Instance);
            shadowOwner = null; // The lease now owns the admitted namespace and loader.
            {
                using var lockFirst = new FileStream(firstPath, FileMode.Open, FileAccess.Read, FileShare.None);
                using var lockSecond = new FileStream(secondPath, FileMode.Open, FileAccess.Read, FileShare.None);
                var request = new AssemblyName(firstIdentity);
                if (sameIdentity)
                {
                    Assert.ThrowsExactly<FileLoadException>(() => loader.TryResolveDependency(request, out _));
                }
                else
                {
                    Assert.IsTrue(loader.TryResolveDependency(request, out var chosenFirst));
                    CollectionAssert.AreEqual(first, await File.ReadAllBytesAsync(chosenFirst));
                    Assert.IsTrue(loader.TryResolveDependency(new AssemblyName(secondIdentity), out var chosenSecond));
                    CollectionAssert.AreEqual(second, await File.ReadAllBytesAsync(chosenSecond));
                    Assert.AreNotEqual(chosenFirst, chosenSecond);
                }
            }
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Analyzer binding fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => { if (lease is not null) lease.Dispose(); else loader?.UnloadAndRelease(); }),
                CleanupFailureCollector.FromAction(() => shadowOwner?.Dispose()),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }
}
