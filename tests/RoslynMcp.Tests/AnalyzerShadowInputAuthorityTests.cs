using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class AnalyzerShadowInputAuthorityTests
{
    [TestMethod]
    public async Task AnalyzerIsolation_DeniedSiblingRetainsNativeReferencesWithoutPartialCatalogPublication()
    {
        var fixture = await NuGetInstallationFixture.CreateAsync("Owned.ShadowSibling", "1.0.0",
            new Dictionary<string, byte[]> { ["lib/net10.0/_._"] = [] });
        Exception? fixtureFailure = null;
        try
        {
            var main = Path.Combine(fixture.Root, "AdmittedAnalyzer.dll");
            var sibling = Path.Combine(fixture.Root, "DeniedDependency.dll");
            await File.WriteAllBytesAsync(main, EmitAssembly("AdmittedAnalyzer"));
            await File.WriteAllBytesAsync(sibling, EmitAssembly("DeniedDependency"));
            var project = Path.Combine(fixture.Root, "Owner.csproj");
            var xml = System.Xml.Linq.XDocument.Load(project);
            Assert.IsNotNull(xml.Root);
            xml.Root.Add(new System.Xml.Linq.XElement("ItemGroup", new System.Xml.Linq.XElement("Analyzer",
                new System.Xml.Linq.XAttribute("Include", main))));
            await File.WriteAllTextAsync(project, xml.ToString());
            var policy = new ShadowPolicy(sibling) { Deny = true };
            var loaded = await new WorkspaceSessionLoader().CreateAndOpenAsync("shadow-sibling", project,
                fixture.LoadProperties, policy, new WorkspaceDiagnosticsSink(64), NullLogger.Instance, SupplementalPackageReadAuthority.None, null, CancellationToken.None);
            Exception? primary = null;
            try
            {
                if (!OperatingSystem.IsWindows())
                {
                    Assert.AreEqual(0, loaded.Lease.RetargetedReferenceCount);
                    return;
                }
                Assert.IsTrue(policy.Denials > 0, "The discovered sibling must be presented to current authority before any owned read.");
                Assert.AreEqual(NativeCompilerContextAvailability.BuildDependencyGrantRequired, loaded.Context.Availability);
                Assert.AreEqual(0, loaded.Lease.RetargetedReferenceCount);
                var reference = loaded.Workspace.CurrentSolution.Projects.Single().AnalyzerReferences.OfType<AnalyzerFileReference>()
                    .Single(item => item.FullPath == main);
                var loader = typeof(AnalyzerFileReference).GetField("_assemblyLoader", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(loader);
                Assert.IsFalse(loader.GetValue(reference) is AnalyzerReferenceIsolation.ShadowCopyAnalyzerAssemblyLoader,
                    "A refused sibling catalog must leave the owned native reference unchanged.");
                StringAssert.Contains(loaded.Context.RecoveryGuidance, "reload");
            }
            catch (Exception failure) { primary = failure; throw; }
            finally { OwnedResourceCleanup.Dispose("Analyzer sibling authority cleanup failed.", primary, loaded.Workspace, loaded.Lease); }
        }
        catch (Exception failure) { fixtureFailure = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Analyzer shadow input fixture cleanup failed.", fixtureFailure,
                CleanupFailureCollector.FromAction(fixture.Dispose));
        }
    }

    private static byte[] EmitAssembly(string name)
    {
        var compilation = CSharpCompilation.Create(name, [CSharpSyntaxTree.ParseText("public class OwnedMarker { }")],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)], new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output = new MemoryStream();
        var result = compilation.Emit(output);
        Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
        return output.ToArray();
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task AnalyzerIsolation_UnavailableDoesNotCopyDeniedInput_AdmittedLazyLoadUsesFrozenBytes(bool deniedAtLoad)
    {
        var fixture = await NuGetInstallationFixture.CreateAsync("Owned.Shadow", "1.0.0",
            new Dictionary<string, byte[]> { ["lib/net10.0/_._"] = [] });
        Exception? fixtureFailure = null;
        try
        {
            var name = "OwnedShadow" + Guid.NewGuid().ToString("N");
            var analyzer = Path.Combine(fixture.Root, name + ".dll");
            var original = EmitAssembly(name);
            await File.WriteAllBytesAsync(analyzer, original);
            var project = Path.Combine(fixture.Root, "Owner.csproj");
            var xml = System.Xml.Linq.XDocument.Load(project);
            Assert.IsNotNull(xml.Root);
            xml.Root.Add(new System.Xml.Linq.XElement("ItemGroup", new System.Xml.Linq.XElement("Analyzer",
                new System.Xml.Linq.XAttribute("Include", analyzer))));
            await File.WriteAllTextAsync(project, xml.ToString());
            var policy = new ShadowPolicy(analyzer) { Deny = deniedAtLoad };
            var loaded = await new WorkspaceSessionLoader().CreateAndOpenAsync("shadow-authority", project,
                fixture.LoadProperties, policy, new WorkspaceDiagnosticsSink(64), NullLogger.Instance, SupplementalPackageReadAuthority.None, null, CancellationToken.None);
            Exception? primary = null;
            try
            {
                var published = (await loaded.Context.NormalizeAsync("shadow-authority", policy, CancellationToken.None)).Solution;
                var reference = published.Projects.Single().AnalyzerReferences
                    .Single(item => item.FullPath == analyzer);
                var loaderField = typeof(AnalyzerFileReference).GetField("_assemblyLoader", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(loaderField);
                var shadow = loaderField.GetValue(AnalyzerReferenceIsolation.GetFileReference(reference)) as AnalyzerReferenceIsolation.ShadowCopyAnalyzerAssemblyLoader;
                if (deniedAtLoad)
                {
                    Assert.IsTrue(policy.Denials > 0);
                    Assert.AreEqual(NativeCompilerContextAvailability.BuildDependencyGrantRequired, loaded.Context.Availability);
                    var copyReached = false;
                    using (var locked = new FileStream(analyzer, FileMode.Open, FileAccess.Read, FileShare.None))
                    {
                        if (shadow is not null)
                        {
                            try { _ = shadow.LoadFromPath(analyzer); }
                            catch (IOException failure)
                            {
                                copyReached = failure.StackTrace?.Contains("ShadowCopy", StringComparison.Ordinal) == true;
                                Assert.IsTrue(copyReached, "The controlled locked-input failure must reach our actual shadow-copy boundary.");
                            }
                        }
                        Assert.IsFalse(copyReached, "Declined compiler authority must not authorize our shadow copy of a denied input.");
                        Assert.IsNull(shadow, "An unavailable load must preserve native references without installing our unapproved reader.");
                    }
                }
                else
                {
                    Assert.AreEqual(NativeCompilerContextAvailability.Certified, loaded.Context.Availability);
                    if (!OperatingSystem.IsWindows())
                    {
                        Assert.IsNull(shadow, "The existing non-Windows contract retains the native loader.");
                        Assert.AreEqual(0, loaded.Lease.RetargetedReferenceCount);
                        return;
                    }
                    Assert.IsNotNull(shadow);
                    policy.Deny = true;
                    var denials = policy.Denials;
                    using var locked = new FileStream(analyzer, FileMode.Open, FileAccess.Read, FileShare.None);
                    var assembly = shadow.LoadFromPath(analyzer);
                    Assert.AreNotEqual(analyzer, assembly.Location);
                    CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(assembly.Location));
                    Assert.AreEqual(denials, policy.Denials, "Lazy callbacks must consume the owned admitted snapshot without new source IO or stale authority callbacks.");
                }
            }
            catch (Exception failure) { primary = failure; throw; }
            finally { OwnedResourceCleanup.Dispose("Analyzer input authority load cleanup failed.", primary, loaded.Workspace, loaded.Lease); }
        }
        catch (Exception failure) { fixtureFailure = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Analyzer shadow input fixture cleanup failed.", fixtureFailure,
                CleanupFailureCollector.FromAction(fixture.Dispose));
        }
    }

    private sealed class ShadowPolicy(string analyzer) : IWorkspacePathPolicy
    {
        private readonly OwnedTestWorkspacePathPolicy _inner = new();
        internal bool Deny { get; set; }
        internal int Denials { get; private set; }
        public Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (Deny && paths.Contains(analyzer, FileSystemPath.Comparer))
            {
                Denials++;
                throw new UnauthorizedAccessException("The owned analyzer input is outside this grant.");
            }
            return _inner.ValidateAsync(workspaceId, paths, ct);
        }
    }
}
