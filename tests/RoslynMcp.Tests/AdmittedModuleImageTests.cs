using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class AdmittedModuleImageTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Admission_LazyCaptureDoesNotExpandFactory_ExistingModulesRemainOwned(bool materializeBeforeCapture)
    {
        var root = Path.Combine(TestTempRoot.Current, "owned-lazy-image-" + Guid.NewGuid().ToString("N"));
        var createdModules = new List<ModuleMetadata>();
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var module = Emit("OwnedLinked", "public class Linked { }", OutputKind.NetModule, []);
            var manifest = Emit("OwnedManifest", "public class Manifest { public Linked Value = new Linked(); }",
                OutputKind.DynamicallyLinkedLibrary,
                [MetadataReference.CreateFromImage(module, new MetadataReferenceProperties(MetadataImageKind.Module))]);
            var modulePath = Path.Combine(root, "OwnedLinked.netmodule");
            var manifestPath = Path.Combine(root, "OwnedManifest.dll");
            await File.WriteAllBytesAsync(modulePath, module);
            await File.WriteAllBytesAsync(manifestPath, manifest);
            Func<string, ModuleMetadata> factory = name =>
            {
                Assert.AreEqual("OwnedLinked.netmodule", name);
                var image = ModuleMetadata.CreateFromFile(Path.Combine(root, name));
                createdModules.Add(image);
                return image;
            };
            var constructor = typeof(AssemblyMetadata).GetConstructor(
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null,
                [typeof(ModuleMetadata), typeof(Func<string, ModuleMetadata>)], null);
            Assert.IsNotNull(constructor);
            using var metadata = (AssemblyMetadata)constructor.Invoke([ModuleMetadata.CreateFromImage(manifest), factory]);
            if (materializeBeforeCapture) Assert.HasCount(2, metadata.GetModules());
            using var workspace = new AdhocWorkspace();
            var project = workspace.AddProject("OwnedLazyImage", LanguageNames.CSharp)
                .WithMetadataReferences([metadata.GetReference(filePath: manifestPath)]);
            var certificate = CreatePrivateImageCaptureFixture(project.Solution);
            var capture = typeof(NativeCompilerContextCertificate).GetMethod("CaptureAdmittedImagesAsync",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(capture);
            var pending = capture.Invoke(certificate, ["owned-lazy-image", new OwnedTestWorkspacePathPolicy(), CancellationToken.None]);
            Assert.IsNotNull(pending);
            await Assert.IsInstanceOfType<Task>(pending);
            var reference = project.MetadataReferences.OfType<PortableExecutableReference>().Single();
            Assert.AreEqual("AvailableImages", CapturedImageAdmission(certificate, project.Id, reference));
            Assert.HasCount(materializeBeforeCapture ? 1 : 0, createdModules,
                "Image capture must not invoke the factory or create native-owned file mappings.");
            if (materializeBeforeCapture) _ = createdModules.Single().GetMetadataReader();
            metadata.Dispose();
            if (materializeBeforeCapture)
                Assert.ThrowsExactly<ObjectDisposedException>(() => createdModules.Single().GetMetadataReader(),
                    "Already materialized modules remain owned and released by the original metadata owner.");
            File.Delete(modulePath);
            Assert.IsFalse(File.Exists(modulePath));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Owned lazy image probe cleanup failed.", primary,
            createdModules.Select(module => CleanupFailureCollector.FromAction(module.Dispose))
                .Append(CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root))).ToArray());
        }
    }

    [TestMethod]
    public async Task Admission_AlreadyMaterializedLinkedMetadataMustMatchManifestBoundBytes()
    {
        var root = Path.Combine(TestTempRoot.Current, "materialized-image-" + Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var module = Emit("MaterializedLinked", "public class Linked { }", OutputKind.NetModule, []);
            var manifest = Emit("MaterializedOwner", "public class Manifest { public Linked Value = new Linked(); }",
                OutputKind.DynamicallyLinkedLibrary,
                [MetadataReference.CreateFromImage(module, new MetadataReferenceProperties(MetadataImageKind.Module))]);
            var differentMetadata = (byte[])module.Clone();
            var oldName = System.Text.Encoding.UTF8.GetBytes("Linked\0");
            var newName = System.Text.Encoding.UTF8.GetBytes("Mutant\0");
            var offset = differentMetadata.AsSpan().IndexOf(oldName);
            Assert.IsTrue(offset >= 0);
            newName.CopyTo(differentMetadata, offset);
            Assert.AreEqual(Mvid(module), Mvid(differentMetadata));
            var manifestPath = Path.Combine(root, "MaterializedOwner.dll");
            await File.WriteAllBytesAsync(manifestPath, manifest);
            await File.WriteAllBytesAsync(Path.Combine(root, "MaterializedLinked.netmodule"), module);
            using var metadata = AssemblyMetadata.Create(ModuleMetadata.CreateFromImage(manifest), ModuleMetadata.CreateFromImage(differentMetadata));
            Assert.HasCount(2, metadata.GetModules(), "The fixture must populate the actual existing native cache.");
            using var workspace = new AdhocWorkspace();
            var reference = metadata.GetReference(filePath: manifestPath);
            var project = workspace.AddProject("MaterializedImage", LanguageNames.CSharp).WithMetadataReferences([reference]);
            var certificate = CreatePrivateImageCaptureFixture(project.Solution);
            var capture = typeof(NativeCompilerContextCertificate).GetMethod("CaptureAdmittedImagesAsync",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(capture);
            var pending = capture.Invoke(certificate, ["materialized-image", new OwnedTestWorkspacePathPolicy(), CancellationToken.None]);
            await Assert.IsInstanceOfType<Task>(pending);
            Assert.AreEqual("UnavailableImages", CapturedImageAdmission(certificate, project.Id, reference),
                "A valid manifest hash cannot excuse different already materialized native metadata.");
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Materialized image probe cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Admission_LateCachedModuleMustMatchFinalImageBuffers(bool afterPublication)
    {
        var root = Path.Combine(TestTempRoot.Current, "late-materialized-image-" + Guid.NewGuid().ToString("N"));
        Solution? registered = null;
        Exception? primaryFailure = null;
        try
        {
            Directory.CreateDirectory(root);
            var module = Emit("LateLinked", "public class Linked { }", OutputKind.NetModule, []);
            var manifest = Emit("LateOwner", "public class Manifest { public Linked Value = new Linked(); }",
                OutputKind.DynamicallyLinkedLibrary,
                [MetadataReference.CreateFromImage(module, new MetadataReferenceProperties(MetadataImageKind.Module))]);
            var differentMetadata = (byte[])module.Clone();
            var offset = differentMetadata.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes("Linked\0"));
            Assert.IsTrue(offset >= 0);
            System.Text.Encoding.UTF8.GetBytes("Mutant\0").CopyTo(differentMetadata, offset);
            Assert.AreEqual(Mvid(module), Mvid(differentMetadata));
            var manifestPath = Path.Combine(root, "LateOwner.dll");
            var linkedPath = Path.Combine(root, "LateLinked.netmodule");
            await File.WriteAllBytesAsync(manifestPath, manifest);
            await File.WriteAllBytesAsync(linkedPath, module);
            var factoryCalls = 0;
            Func<string, ModuleMetadata> factory = name =>
            {
                Assert.AreEqual("LateLinked.netmodule", name);
                factoryCalls++;
                return ModuleMetadata.CreateFromImage(differentMetadata);
            };
            var constructor = typeof(AssemblyMetadata).GetConstructor(
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null,
                [typeof(ModuleMetadata), typeof(Func<string, ModuleMetadata>)], null);
            Assert.IsNotNull(constructor);
            using var metadata = (AssemblyMetadata)constructor.Invoke([ModuleMetadata.CreateFromImage(manifest), factory]);
            using var workspace = new AdhocWorkspace();
            var reference = metadata.GetReference(filePath: manifestPath);
            var project = workspace.AddProject("LateImage", LanguageNames.CSharp).WithMetadataReferences([reference]);
            var certificate = CreatePrivateImageCaptureFixture(project.Solution);
            var policy = new LateImagePolicy(linkedPath, () => Assert.HasCount(2, metadata.GetModules()));
            if (!afterPublication) policy.Arm();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var capture = typeof(NativeCompilerContextCertificate).GetMethod("CaptureAdmittedImagesAsync", flags);
            Assert.IsNotNull(capture);
            await Assert.IsInstanceOfType<Task>(capture.Invoke(certificate, ["late-image", policy, CancellationToken.None]));
            Exception? readerFailure = null;
            if (afterPublication)
            {
                Assert.AreEqual("AvailableImages", CapturedImageAdmission(certificate, project.Id, reference));
                Assert.AreEqual(0, factoryCalls, "Initial issuance must leave the deferred native factory untouched.");
                // Isolate the private reader mechanism; this test does not represent public
                // issuance from a caller-created solution. Remove both registrations below.
                var bind = typeof(NativeCompilerContextCertificate).GetMethod("BindNormalized", flags);
                Assert.IsNotNull(bind);
                registered = project.Solution;
                bind.Invoke(certificate, [registered,
                    ImmutableDictionary<ProjectId, ImmutableArray<NativeCompilerContextCertificate.ProducedReference>>.Empty,
                    ImmutableArray<CompilerArgumentParseReceipt>.Empty]);
                policy.Arm();
                try
                {
                    _ = await certificate.ReadAdmittedImagesAsync(
                        project.Solution, project.Id, reference, "late-image", policy, CancellationToken.None);
                }
                catch (Exception failure) { readerFailure = failure; }
            }
            Assert.AreEqual(1, factoryCalls, "The final linked-read callback must actually materialize native metadata.");
            Assert.AreEqual(2, policy.LinkedReads, "The fixture must reach the last authorized linked byte read.");
            CollectionAssert.AreEqual(module, await File.ReadAllBytesAsync(linkedPath), "On-disk bytes remain valid for the fixed manifest.");
            if (afterPublication)
            {
                Assert.IsNotNull(readerFailure, "The reader must reject the native metadata materialized during its final byte read.");
                Assert.AreEqual(typeof(PublicInvalidOperationException), readerFailure.GetType());
            }
            else
                Assert.AreEqual("UnavailableImages", CapturedImageAdmission(certificate, project.Id, reference));
        }
        catch (Exception failure) { primaryFailure = failure; throw; }
        finally
        {
            var cleanup = new List<Func<ValueTask>>();
            if (registered is not null)
            {
                foreach (var fieldName in new[] { "Certificates", "CompilerInputs" })
                {
                    cleanup.Add(CleanupFailureCollector.FromAction(() =>
                    {
                        var field = typeof(NativeCompilerContextCertificate).GetField(fieldName,
                            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                        Assert.IsNotNull(field);
                        var table = field.GetValue(null);
                        Assert.IsNotNull(table);
                        var remove = table.GetType().GetMethod("Remove", [typeof(Solution)]);
                        Assert.IsNotNull(remove);
                        _ = remove.Invoke(table, [registered]);
                    }));
                }
            }
            cleanup.Add(CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
            await CleanupFailureCollector.RunAfterFailureAsync("Late cached image probe cleanup failed.", primaryFailure, cleanup.ToArray());
        }
    }

    private sealed class LateImagePolicy(string linkedPath, Action materialize) : IWorkspacePathPolicy
    {
        private readonly OwnedTestWorkspacePathPolicy _inner = new();
        private bool _armed;
        internal int LinkedReads { get; private set; }
        internal void Arm() { LinkedReads = 0; _armed = true; }
        public async Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        {
            await _inner.ValidateAsync(workspaceId, paths, ct);
            if (_armed && paths.Count == 1 && paths[0] == linkedPath && ++LinkedReads == 2) materialize();
        }
    }

    private static string CapturedImageAdmission(NativeCompilerContextCertificate certificate, ProjectId projectId,
        PortableExecutableReference reference)
    {
        var field = typeof(NativeCompilerContextCertificate).GetField("_admittedImages",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(field);
        var projects = Assert.IsInstanceOfType<System.Collections.IDictionary>(field.GetValue(certificate));
        var references = Assert.IsInstanceOfType<System.Collections.IDictionary>(projects[projectId]);
        var admission = references[reference];
        Assert.IsNotNull(admission);
        return admission.GetType().Name;
    }

    [TestMethod]
    public async Task Admission_CachedManifestCannotOpenDeniedOldModuleBeforeManifestComparison()
    {
        var root = Path.Combine(TestTempRoot.Current, "cached-image-" + Guid.NewGuid().ToString("N"));
        var createdModules = new List<ModuleMetadata>();
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var oldModule = Emit("DeniedOld", "public class Linked { }", OutputKind.NetModule, []);
            var newModule = Emit("AllowedNew", "public class Linked { }", OutputKind.NetModule, []);
            var oldManifest = Emit("CachedManifest", "public class Manifest { public Linked Value = new Linked(); }",
                OutputKind.DynamicallyLinkedLibrary,
                [MetadataReference.CreateFromImage(oldModule, new MetadataReferenceProperties(MetadataImageKind.Module))]);
            var newManifest = Emit("CachedManifest", "public class Manifest { public Linked Value = new Linked(); }",
                OutputKind.DynamicallyLinkedLibrary,
                [MetadataReference.CreateFromImage(newModule, new MetadataReferenceProperties(MetadataImageKind.Module))]);
            var manifestPath = Path.Combine(root, "CachedManifest.dll");
            var deniedPath = Path.Combine(root, "DeniedOld.netmodule");
            var allowedPath = Path.Combine(root, "AllowedNew.netmodule");
            await File.WriteAllBytesAsync(manifestPath, newManifest);
            await File.WriteAllBytesAsync(deniedPath, oldModule);
            await File.WriteAllBytesAsync(allowedPath, newModule);
            var policy = new RevocablePolicy { Denied = deniedPath };
            await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => policy.ValidateAsync("cached-image", [deniedPath], CancellationToken.None));
            var factoryCalls = 0;
            Func<string, ModuleMetadata> factory = name =>
            {
                Interlocked.Increment(ref factoryCalls);
                Assert.AreEqual("DeniedOld.netmodule", name, "Roslyn must select its old cached manifest's module name.");
                var module = ModuleMetadata.CreateFromFile(Path.Combine(root, name));
                createdModules.Add(module);
                return module;
            };
            // Pin the actual Roslyn lazy-factory protocol. Its public eager Create overload
            // would not reproduce the explicit GetModules read under review.
            var constructor = typeof(AssemblyMetadata).GetConstructor(
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null,
                [typeof(ModuleMetadata), typeof(Func<string, ModuleMetadata>)], null);
            Assert.IsNotNull(constructor);
            using var metadata = (AssemblyMetadata)constructor.Invoke([ModuleMetadata.CreateFromImage(oldManifest), factory]);
            using var workspace = new AdhocWorkspace();
            var project = workspace.AddProject("CachedImage", LanguageNames.CSharp)
                .WithMetadataReferences([metadata.GetReference(filePath: manifestPath)]);
            var certificate = CreatePrivateImageCaptureFixture(project.Solution);
            var capture = typeof(NativeCompilerContextCertificate).GetMethod("CaptureAdmittedImagesAsync",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(capture);
            var pending = capture.Invoke(certificate, ["cached-image", policy, CancellationToken.None]);
            Assert.IsNotNull(pending);
            await Assert.IsInstanceOfType<Task>(pending);
            Assert.AreEqual(0, factoryCalls, "Current-manifest authorization must not permit an old cached-manifest module factory to run.");
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Cached image probe cleanup failed.", primary,
            createdModules.Select(module => CleanupFailureCollector.FromAction(module.Dispose))
                .Append(CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root))).ToArray());
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Admission_LinkedImageMustMatchFixedManifestContentHash(bool mutateIl)
    {
        var root = Path.Combine(TestTempRoot.Current, "linked-content-" + Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var module = Emit("LinkedContent", "public class Linked { public int Value() => 1; }", OutputKind.NetModule, []);
            var manifest = Emit("LinkedOwner", "public class Manifest { public Linked Value = new Linked(); }",
                OutputKind.DynamicallyLinkedLibrary,
                [MetadataReference.CreateFromImage(module, new MetadataReferenceProperties(MetadataImageKind.Module))]);
            using var manifestPe = new PEReader(new MemoryStream(manifest, writable: false));
            var manifestReader = manifestPe.GetMetadataReader();
            var linkedFile = manifestReader.GetAssemblyFile(manifestReader.AssemblyFiles.Single());
            Assert.AreEqual("LinkedContent.netmodule", manifestReader.GetString(linkedFile.Name));
            Assert.IsTrue(linkedFile.ContainsMetadata);
            Assert.IsTrue(manifestReader.GetBlobBytes(linkedFile.HashValue).Length > 0);
            var currentModule = (byte[])module.Clone();
            if (mutateIl)
            {
                using var originalPe = new PEReader(new MemoryStream(module, writable: false));
                var reader = originalPe.GetMetadataReader();
                var method = reader.MethodDefinitions.Select(reader.GetMethodDefinition)
                    .Single(item => reader.GetString(item.Name) == "Value");
                var body = originalPe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes();
                Assert.IsNotNull(body);
                var instruction = Array.IndexOf(body, (byte)0x17); // ldc.i4.1
                Assert.IsTrue(instruction >= 0);
                var section = originalPe.PEHeaders.SectionHeaders.Single(item => method.RelativeVirtualAddress >= item.VirtualAddress
                    && method.RelativeVirtualAddress < item.VirtualAddress + item.VirtualSize);
                var header = section.PointerToRawData + method.RelativeVirtualAddress - section.VirtualAddress;
                var headerSize = (currentModule[header] & 3) == 2 ? 1
                    : ((System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(currentModule.AsSpan(header, 2)) >> 12) * 4);
                Assert.AreEqual((byte)0x17, currentModule[header + headerSize + instruction]);
                currentModule[header + headerSize + instruction] = 0x18; // ldc.i4.2
                using var currentPe = new PEReader(new MemoryStream(currentModule, writable: false));
                CollectionAssert.AreEqual(originalPe.GetMetadata().GetContent().ToArray(), currentPe.GetMetadata().GetContent().ToArray());
                Assert.AreEqual(Mvid(module), Mvid(currentModule));
                CollectionAssert.AreNotEqual(module, currentModule);
            }
            var manifestPath = Path.Combine(root, "LinkedOwner.dll");
            await File.WriteAllBytesAsync(manifestPath, manifest);
            await File.WriteAllBytesAsync(Path.Combine(root, "LinkedContent.netmodule"), currentModule);
            using var metadata = AssemblyMetadata.Create(ModuleMetadata.CreateFromImage(manifest), ModuleMetadata.CreateFromImage(module));
            using var workspace = new AdhocWorkspace();
            var reference = metadata.GetReference(filePath: manifestPath);
            var project = workspace.AddProject("LinkedContent", LanguageNames.CSharp).WithMetadataReferences([reference]);
            var certificate = CreatePrivateImageCaptureFixture(project.Solution);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var capture = typeof(NativeCompilerContextCertificate).GetMethod("CaptureAdmittedImagesAsync", flags);
            Assert.IsNotNull(capture);
            var pending = capture.Invoke(certificate, ["linked-content", new OwnedTestWorkspacePathPolicy(), CancellationToken.None]);
            await Assert.IsInstanceOfType<Task>(pending);
            var field = typeof(NativeCompilerContextCertificate).GetField("_admittedImages", flags);
            Assert.IsNotNull(field);
            var projects = Assert.IsInstanceOfType<System.Collections.IDictionary>(field.GetValue(certificate));
            var references = Assert.IsInstanceOfType<System.Collections.IDictionary>(projects[project.Id]);
            var admission = references[reference];
            Assert.IsNotNull(admission);
            Assert.AreEqual(mutateIl ? "UnavailableImages" : "AvailableImages", admission.GetType().Name,
                "A fixed owning manifest must bind linked raw bytes, including method bodies outside metadata.");
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Linked content probe cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }


    // This fixture invokes only the private image-capture mechanism; it never registers
    // a certificate or claims a caller-created solution is an owned native load.
    private static NativeCompilerContextCertificate CreatePrivateImageCaptureFixture(Solution solution)
    {
        var type = typeof(NativeCompilerContextCertificate);
        var authorityType = type.GetNestedType("CertifiedAuthority", System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(authorityType);
        var constructor = authorityType.GetConstructor(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
            null, [typeof(ImmutableArray<PhysicalInputIdentity>), typeof(ImmutableHashSet<string>),
                typeof(ImmutableDictionary<string, ImmutableHashSet<string>>), typeof(MsBuildCommandProperties.Redactor),
                typeof(ImmutablePathProvenance), typeof(InstalledPackageAuthority), typeof(ImmutableArray<SdkExistenceInput>),
                typeof(NativeBuildMembership)], null);
        Assert.IsNotNull(constructor);
        // No fixture path is immutable: every manifest and linked module still requires
        // current policy authorization. Empty membership is unused by private image capture.
        var authority = constructor.Invoke([ImmutableArray<PhysicalInputIdentity>.Empty,
            ImmutableHashSet<string>.Empty, ImmutableDictionary<string, ImmutableHashSet<string>>.Empty,
            new MsBuildCommandProperties.Redactor([]), new ImmutablePathProvenance([], [], []),
            InstalledPackageAuthority.Merge([]), ImmutableArray<SdkExistenceInput>.Empty,
            NativeBuildMembership.Capture(new EvaluatedBuildGraph.Graph([], []), CancellationToken.None)]);
        var certificate = (NativeCompilerContextCertificate)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var authorityField = type.GetField("_authority", flags);
        var nativeField = type.GetField("<NativeSolution>k__BackingField", flags);
        Assert.IsNotNull(authorityField);
        Assert.IsNotNull(nativeField);
        authorityField.SetValue(certificate, authority);
        nativeField.SetValue(certificate, solution);
        return certificate;
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Admission_RejectsChangedMetadataEvenWhenModuleNameAndMvidAreUnchanged(bool changeAssemblyIdentity)
    {
        var original = Emit("IdentityBefore", "public class Marker { }", OutputKind.DynamicallyLinkedLibrary, []);
        using var admitted = ModuleMetadata.CreateFromImage(original);
        NativeCompilerContextCertificate.AssertAdmittedModuleIdentity(admitted, original);
        var changed = (byte[])original.Clone();
        var before = System.Text.Encoding.UTF8.GetBytes(changeAssemblyIdentity ? "IdentityBefore\0" : "Marker\0");
        var after = System.Text.Encoding.UTF8.GetBytes(changeAssemblyIdentity ? "IdentityAfterX\0" : "Mutant\0");
        Assert.AreEqual(before.Length, after.Length);
        var offset = changed.AsSpan().IndexOf(before);
        Assert.IsTrue(offset >= 0);
        Assert.IsTrue(changed.AsSpan(offset + before.Length).IndexOf(before) < 0,
            "The fixture must mutate exactly one metadata string heap entry.");
        after.CopyTo(changed, offset);
        Assert.AreEqual(Mvid(original), Mvid(changed));
        using var originalPe = new PEReader(new MemoryStream(original, writable: false));
        using var changedPe = new PEReader(new MemoryStream(changed, writable: false));
        var originalReader = originalPe.GetMetadataReader();
        var changedReader = changedPe.GetMetadataReader();
        Assert.AreEqual(originalReader.GetString(originalReader.GetModuleDefinition().Name),
            changedReader.GetString(changedReader.GetModuleDefinition().Name));
        var sameAssembly = originalReader.GetString(originalReader.GetAssemblyDefinition().Name)
            == changedReader.GetString(changedReader.GetAssemblyDefinition().Name);
        Assert.AreEqual(!changeAssemblyIdentity, sameAssembly);
        Assert.ThrowsExactly<PublicInvalidOperationException>(() => NativeCompilerContextCertificate.AssertAdmittedModuleIdentity(admitted, changed));
    }

    [TestMethod]
    public async Task OwnedManager_ReturnsExactFrameworkAndInstalledPackageImages_RejectsEquivalentUnadmittedReference()
    {
        var library = Emit("OwnedImage", "public class OwnedImage { }", OutputKind.DynamicallyLinkedLibrary, []);
        using var fixture = await NuGetInstallationFixture.CreateAsync("OwnedImage", "1.0.0",
            new Dictionary<string, byte[]> { ["lib/net10.0/OwnedImage.dll"] = library });
        using var manager = Manager();
        var status = await manager.LoadAsync(Path.Combine(fixture.Root, "Owner.csproj"), fixture.LoadProperties, CancellationToken.None);
        var project = manager.GetCurrentSolution(status.WorkspaceId).Projects.Single();
        foreach (var name in new[] { "OwnedImage.dll", "System.Runtime.dll" })
        {
            var reference = project.MetadataReferences.OfType<PortableExecutableReference>()
                .Single(item => Path.GetFileName(item.FilePath) == name);
            Assert.IsNotNull(reference.FilePath);
            var images = await manager.ReadAdmittedImagesAsync(status.WorkspaceId, project.Id, reference, CancellationToken.None);
            Assert.HasCount(1, images);
            Assert.AreEqual(Path.GetFileName(reference.FilePath), images[0].FileName);
            CollectionAssert.AreEqual(await File.ReadAllBytesAsync(reference.FilePath), images[0].Image.ToArray());
            Assert.AreEqual(Mvid(images[0].Image.ToArray()), images[0].ModuleVersionId);
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => manager.ReadAdmittedImagesAsync(
                status.WorkspaceId, project.Id, MetadataReference.CreateFromFile(reference.FilePath), CancellationToken.None));
        }
    }

    [TestMethod]
    [DataRow("Source")]
    [DataRow("Assets")]
    [DataRow("Grant")]
    public async Task AdmittedImage_FinalBufferReadRevalidatesCurrentEpoch(string change)
    {
        var library = Emit("FinalReadImage", "public class FinalReadImage { }", OutputKind.DynamicallyLinkedLibrary, []);
        var fixture = await NuGetInstallationFixture.CreateAsync("FinalReadPackage", "1.0.0",
            new Dictionary<string, byte[]> { ["lib/net10.0/_._"] = [] });
        WorkspaceManager? manager = null;
        Exception? primaryFailure = null;
        try
        {
            var imagePath = Path.Combine(fixture.Root, "FinalReadImage.dll");
            var sourcePath = Path.Combine(fixture.Root, "Owner.cs");
            var projectPath = Path.Combine(fixture.Root, "Owner.csproj");
            await File.WriteAllBytesAsync(imagePath, library);
            await File.WriteAllTextAsync(sourcePath, "public class Owner { }");
            var xml = System.Xml.Linq.XDocument.Load(projectPath);
            Assert.IsNotNull(xml.Root);
            xml.Root.Add(new System.Xml.Linq.XElement("ItemGroup", new System.Xml.Linq.XElement("Reference",
                new System.Xml.Linq.XAttribute("Include", "FinalReadImage"), new System.Xml.Linq.XElement("HintPath", imagePath))));
            await File.WriteAllTextAsync(projectPath, xml.ToString());
            var policy = new FinalImageEpochPolicy(imagePath, sourcePath);
            manager = new WorkspaceManager(new ForkInvocationPathPolicy(policy), NullLogger<WorkspaceManager>.Instance,
                new PreviewStore(), new FileWatcherService(NullLogger<FileWatcherService>.Instance));
            var loaded = await manager.LoadAsync(projectPath, fixture.LoadProperties, CancellationToken.None);
            var solution = manager.GetCurrentSolution(loaded.WorkspaceId);
            var project = solution.Projects.Single();
            Assert.IsTrue(project.Documents.Any(document => document.FilePath == sourcePath));
            var reference = project.MetadataReferences.OfType<PortableExecutableReference>().Single(item => item.FilePath == imagePath);
            var initial = await manager.ReadAdmittedImagesAsync(loaded.WorkspaceId, project.Id, reference, CancellationToken.None);
            CollectionAssert.AreEqual(library, initial.Single().Image.ToArray(), "The original native image must be admitted before the final-read hook is armed.");
            var assetsBefore = await File.ReadAllBytesAsync(fixture.Assets);
            policy.Arm(async () =>
            {
                if (change == "Source") await File.AppendAllTextAsync(sourcePath, "\n// changed during the final image read\n");
                else if (change == "Assets") await File.AppendAllTextAsync(fixture.Assets, "\n ");
                else policy.RevokeSourceGrant();
            });
            Exception? observed = null;
            try { _ = await manager.ReadAdmittedImagesAsync(loaded.WorkspaceId, project.Id, reference, CancellationToken.None); }
            catch (Exception failure) { observed = failure; }
            Assert.AreEqual(2, policy.BufferReads, "The real reader must reach its second and final explicit image-buffer read.");
            Assert.IsTrue(policy.HookReached, "The mutation must occur inside that final read's awaited authorization callback.");
            CollectionAssert.AreEqual(library, await File.ReadAllBytesAsync(imagePath), "The PE bytes remain exactly admitted; the changed epoch must cause refusal.");
            Assert.AreSame(solution, manager.GetCurrentSolution(loaded.WorkspaceId));
            if (change == "Source") StringAssert.Contains(await File.ReadAllTextAsync(sourcePath), "changed during the final image read");
            if (change == "Assets")
            {
                var currentAssets = await File.ReadAllBytesAsync(fixture.Assets);
                Assert.IsFalse(assetsBefore.SequenceEqual(currentAssets));
            }
            Assert.IsNotNull(observed, "Image publication must revalidate current source, selecting assets and grants after the final awaited image read.");
            Assert.AreEqual(change == "Grant" ? typeof(UnauthorizedAccessException) : typeof(PublicInvalidOperationException), observed.GetType());
        }
        catch (Exception failure) { primaryFailure = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Final image read regression and fixture cleanup failed.", primaryFailure,
                CleanupFailureCollector.FromAction(() => manager?.Dispose()),
                CleanupFailureCollector.FromAction(fixture.Dispose));
        }
    }

    private sealed class FinalImageEpochPolicy(string imagePath, string sourcePath) : IWorkspacePathPolicy
    {
        private readonly OwnedTestWorkspacePathPolicy _inner = new();
        private Func<Task>? _hook;
        private bool _denySource;
        internal int BufferReads { get; private set; }
        internal bool HookReached { get; private set; }
        internal void Arm(Func<Task> hook) { _hook = hook; BufferReads = 0; }
        internal void RevokeSourceGrant() => _denySource = true;
        public async Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        {
            if (_denySource && paths.Contains(sourcePath, FileSystemPath.Comparer))
                throw new UnauthorizedAccessException("The current source input grant was revoked.");
            await _inner.ValidateAsync(workspaceId, paths, ct);
            if (_hook is not { } hook || paths.Count != 1 || !FileSystemPath.Comparer.Equals(paths[0], imagePath)) return;
            var insideBufferRead = new System.Diagnostics.StackTrace(false).GetFrames().Any(frame =>
                frame.GetMethod() is { } method && (method.Name == "ReadImageAsync"
                    || method.DeclaringType?.Name.StartsWith("<ReadImageAsync>", StringComparison.Ordinal) is true));
            if (!insideBufferRead || ++BufferReads != 2) return;
            _hook = null;
            HookReached = true;
            await hook();
        }
    }

    [TestMethod]
    public async Task AdmittedImage_SameMvidByteMutationRefuses_IndependentReloadAdmitsNewEpoch()
    {
        var library = Emit("Mutable", "public class Mutable { }", OutputKind.DynamicallyLinkedLibrary, []);
        using var fixture = await NuGetInstallationFixture.CreateAsync("OwnedImage", "1.0.0",
            new Dictionary<string, byte[]> { ["lib/net10.0/_._"] = [] });
        // A separately authored HintPath is mutable authority, not a modification of the package cache.
        var path = Path.Combine(fixture.Root, "Mutable.dll");
        await File.WriteAllBytesAsync(path, library);
        var projectPath = Path.Combine(fixture.Root, "Owner.csproj");
        var xml = System.Xml.Linq.XDocument.Load(projectPath);
        Assert.IsNotNull(xml.Root);
        xml.Root.Add(new System.Xml.Linq.XElement("ItemGroup", new System.Xml.Linq.XElement("Reference",
            new System.Xml.Linq.XAttribute("Include", "Mutable"), new System.Xml.Linq.XElement("HintPath", path))));
        await File.WriteAllTextAsync(projectPath, xml.ToString());
        using var manager = Manager();
        var status = await manager.LoadAsync(projectPath, fixture.LoadProperties, CancellationToken.None);
        var project = manager.GetCurrentSolution(status.WorkspaceId).Projects.Single();
        var reference = project.MetadataReferences.OfType<PortableExecutableReference>().Single(item => item.FilePath == path);
        _ = await manager.ReadAdmittedImagesAsync(status.WorkspaceId, project.Id, reference, CancellationToken.None);
        var changed = library.Concat(new byte[] { 42 }).ToArray();
        Assert.AreEqual(Mvid(library), Mvid(changed), "The negative must retain module identity while changing image bytes.");
        await File.WriteAllBytesAsync(path, changed);
        await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => manager.ReadAdmittedImagesAsync(
            status.WorkspaceId, project.Id, reference, CancellationToken.None));
        var reloaded = await manager.ReloadAsync(status.WorkspaceId, CancellationToken.None);
        var fresh = manager.GetCurrentSolution(reloaded.WorkspaceId).Projects.Single();
        var freshReference = fresh.MetadataReferences.OfType<PortableExecutableReference>().Single(item => item.FilePath == path);
        var images = await manager.ReadAdmittedImagesAsync(reloaded.WorkspaceId, fresh.Id, freshReference, CancellationToken.None);
        CollectionAssert.AreEqual(changed, images.Single().Image.ToArray());
    }

    [TestMethod]
    public async Task AdmittedImage_CurrentPolicyDenialPrecedesLockedImageRead()
    {
        var library = Emit("OwnedImage", "public class OwnedImage { }", OutputKind.DynamicallyLinkedLibrary, []);
        using var fixture = await NuGetInstallationFixture.CreateAsync("OwnedImage", "1.0.0",
            new Dictionary<string, byte[]> { ["lib/net10.0/OwnedImage.dll"] = library });
        var policy = new RevocablePolicy();
        using var manager = new WorkspaceManager(new RoslynMcp.Roslyn.Services.ForkInvocationPathPolicy(policy), NullLogger<WorkspaceManager>.Instance,
            new PreviewStore(), new FileWatcherService(NullLogger<FileWatcherService>.Instance),
            new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 4 });
        var status = await manager.LoadAsync(Path.Combine(fixture.Root, "Owner.csproj"), fixture.LoadProperties, CancellationToken.None);
        var project = manager.GetCurrentSolution(status.WorkspaceId).Projects.Single();
        var reference = project.MetadataReferences.OfType<PortableExecutableReference>()
            .Single(item => Path.GetFileName(item.FilePath) == "OwnedImage.dll");
        Assert.IsNotNull(reference.FilePath);
        _ = await manager.ReadAdmittedImagesAsync(status.WorkspaceId, project.Id, reference, CancellationToken.None);
        policy.Denied = reference.FilePath;
        using var locked = new FileStream(reference.FilePath, FileMode.Open, FileAccess.Read, FileShare.None);
        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => manager.ReadAdmittedImagesAsync(
            status.WorkspaceId, project.Id, reference, CancellationToken.None));
        Assert.IsTrue(policy.Denials > 0);
    }

    [TestMethod]
    public async Task OwnedManager_ReadsEveryAdmittedModule_RefusesChangedLinkedModule()
    {
        var module = Emit("Linked", "public class Linked { }", OutputKind.NetModule, []);
        var moduleReference = MetadataReference.CreateFromImage(module, new MetadataReferenceProperties(MetadataImageKind.Module));
        var manifest = Emit("InternalManifest", "public class Manifest { public Linked Value = new Linked(); }",
            OutputKind.DynamicallyLinkedLibrary, [moduleReference]);
        var fixture = await NuGetInstallationFixture.CreateAsync("OwnedImage", "1.0.0",
            new Dictionary<string, byte[]>
            {
                ["lib/net10.0/OwnedImage.dll"] = manifest,
                ["lib/net10.0/Linked.netmodule"] = module
            });
        Exception? primary = null;
        try
        {
            await ExerciseOwnedNativeModulesAsync(fixture, manifest, module);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Native linked image fixture cleanup failed.", primary,
                () => WaitForNativeImageReleaseAsync(fixture.Root), CleanupFailureCollector.FromAction(fixture.Dispose));
        }
    }

    // Keep native workspace/reference owners inside this scope. Their metadata caches may
    // be reclaimed after workspace disposal; borrowed metadata must never be disposed here.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static async Task ExerciseOwnedNativeModulesAsync(NuGetInstallationFixture fixture, byte[] manifest, byte[] module)
    {
        using var manager = Manager();
        var status = await manager.LoadAsync(Path.Combine(fixture.Root, "Owner.csproj"), fixture.LoadProperties, CancellationToken.None);
        var project = manager.GetCurrentSolution(status.WorkspaceId).Projects.Single();
        var reference = project.MetadataReferences.OfType<PortableExecutableReference>()
            .Single(item => Path.GetFileName(item.FilePath) == "OwnedImage.dll");
        var images = await manager.ReadAdmittedImagesAsync(status.WorkspaceId, project.Id, reference, CancellationToken.None);
        Assert.HasCount(2, images);
        CollectionAssert.AreEquivalent(new[] { "OwnedImage.dll", "Linked.netmodule" }, images.Select(image => image.FileName).ToArray());
        var manifestImage = images.Single(image => image.FileName == "OwnedImage.dll");
        Assert.AreEqual("InternalManifest.dll", manifestImage.ModuleName,
            "Physical manifest alias and ModuleDefinition.Name must remain distinct identities.");
        CollectionAssert.AreEquivalent(new[] { Mvid(manifest), Mvid(module) }, images.Select(image => image.ModuleVersionId).ToArray());
        var directory = Path.GetDirectoryName(reference.FilePath);
        Assert.IsNotNull(directory);
        var linked = Path.Combine(directory, "Linked.netmodule");
        var original = await File.ReadAllBytesAsync(linked);
        var mutated = false;
        Exception? mutationFailure = null;
        try
        {
            try
            {
                // Open before truncation: native metadata may legitimately retain a
                // Windows share lock for the lifetime of this admitted reference.
                await using var writer = new FileStream(linked, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                mutated = true;
                await writer.WriteAsync(original.Concat(new byte[] { 42 }).ToArray());
                writer.SetLength(original.Length + 1);
            }
            catch (IOException failure) when (!mutated && OperatingSystem.IsWindows() &&
                (failure.HResult & 0xffff) is 32 or 33)
            {
                CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(linked));
                var repeated = await manager.ReadAdmittedImagesAsync(status.WorkspaceId, project.Id, reference, CancellationToken.None);
                CollectionAssert.AreEqual(module, repeated.Single(image => image.FileName == "Linked.netmodule").Image.ToArray());
                return;
            }
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => manager.ReadAdmittedImagesAsync(
                status.WorkspaceId, project.Id, reference, CancellationToken.None));
        }
        catch (Exception failure) { mutationFailure = failure; throw; }
        finally
        {
            if (mutated)
                await CleanupFailureCollector.RunAfterFailureAsync("Linked image mutation restoration failed.", mutationFailure,
                    () => new ValueTask(File.WriteAllBytesAsync(linked, original)));
        }
    }

    private static ValueTask WaitForNativeImageReleaseAsync(string root)
    {
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path) is ".dll" or ".netmodule").ToArray();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        foreach (var file in files)
        {
            using var released = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        return ValueTask.CompletedTask;
    }

    private static WorkspaceManager Manager() => new(new RoslynMcp.Roslyn.Services.ForkInvocationPathPolicy(new OwnedTestWorkspacePathPolicy()), NullLogger<WorkspaceManager>.Instance,
        new PreviewStore(), new FileWatcherService(NullLogger<FileWatcherService>.Instance), new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 4 });

    private sealed class RevocablePolicy : IWorkspacePathPolicy
    {
        private readonly OwnedTestWorkspacePathPolicy _inner = new();
        internal string? Denied { get; set; }
        internal int Denials { get; private set; }
        public Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (paths.Any(path => string.Equals(path, Denied, StringComparison.OrdinalIgnoreCase)))
            {
                Denials++;
                throw new UnauthorizedAccessException("Owned image access was revoked.");
            }
            return _inner.ValidateAsync(workspaceId, paths, ct);
        }
    }

    [TestMethod]
    [DataRow("stable")]
    [DataRow("new-compile")]
    [DataRow("known-source")]
    [DataRow("revoked-grant")]
    [DataRow("image-source-drift")]
    public async Task Completion_RevalidatesMembershipInputsAndGrantsBeforePublishingImages(string scenario)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(AdmittedModuleImageTests), Guid.NewGuid().ToString("N"));
        var projectPath = Path.Combine(root, "Completion.csproj");
        var sourcePath = Path.Combine(root, "Owned.cs");
        var addedPath = Path.Combine(root, "Added.cs");
        var imagePath = Path.Combine(root, "CompletionImage.dll");
        var policy = new CompletionBoundaryPolicy(sourcePath, imagePath, addedPath, scenario);
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(Path.Combine(root, "global.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(root, "Directory.Build.props"), "<Project />");
            await File.WriteAllTextAsync(sourcePath, "public sealed class Owned { }");
            await File.WriteAllBytesAsync(imagePath, Emit("CompletionImage", "public sealed class CompletionImage { }", OutputKind.DynamicallyLinkedLibrary, []));
            await File.WriteAllTextAsync(projectPath, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit></PropertyGroup>
                  <ItemGroup><Reference Include="CompletionImage"><HintPath>CompletionImage.dll</HintPath></Reference></ItemGroup>
                </Project>
                """);
            var properties = OwnedNativeLoadContext.Properties(projectPath, new Dictionary<string, string>());
            var restore = await new DotnetCommandRunner().RunAsync(root, projectPath,
                MsBuildCommandProperties.Append(["restore", projectPath, "--nologo"], properties), CancellationToken.None);
            Assert.IsTrue(restore.Succeeded, restore.StdErr + restore.StdOut);
            var loaded = await new WorkspaceSessionLoader().CreateAndOpenAsync("completion-boundary", projectPath, properties,
                policy, new WorkspaceDiagnosticsSink(64), NullLogger.Instance, SupplementalPackageReadAuthority.None, null, CancellationToken.None);
            MSBuildWorkspace? completionWorkspace = null;
            AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease? completionLease = null;
            Exception? loadFailure = null;
            try
            {
                Assert.AreEqual(NativeCompilerContextAvailability.Certified, loaded.Context.Availability);
                var native = loaded.Context.NativeSolution;
                var project = native.Projects.Single();
                Assert.IsTrue(project.Documents.Any(document => FileSystemPath.Comparer.Equals(document.FilePath, sourcePath)));
                Assert.IsTrue(project.MetadataReferences.OfType<PortableExecutableReference>().Any(reference => FileSystemPath.Comparer.Equals(reference.FilePath, imagePath)));
                var epoch = await NativeCompilerContextCertificate.Epoch.CaptureAsync("completion-boundary", projectPath,
                    EvaluatedProjectGraph.Properties(native), policy, SupplementalPackageReadAuthority.None, CancellationToken.None);
                // A new owned completion must publish a genuinely fresh native owner;
                // the earlier loader's solution already belongs to its certificate.
                completionWorkspace = MSBuildWorkspace.Create(properties);
                await completionWorkspace.OpenProjectAsync(projectPath, cancellationToken: CancellationToken.None);
                native = completionWorkspace.CurrentSolution;
                policy.Arm();
                if (scenario == "stable")
                {
                    var completed = await epoch.CompleteAsync("completion-boundary", native, policy, CancellationToken.None);
                    Assert.AreEqual(NativeCompilerContextAvailability.Certified, completed.Availability);
                    Assert.IsTrue(policy.ImageAuthorizationObserved, "Stable completion must reach explicit admission of the owned custom image.");
                    completed = await completed.CaptureAnalyzerInputsAsync("completion-boundary", policy, CancellationToken.None);
                    await completed.PrepareProjectionAsync("completion-boundary", policy, CancellationToken.None);
                    completionLease = await completed.InstallAnalyzerIsolationAsync("completion-boundary", policy, NullLogger.Instance, CancellationToken.None);
                    var projection = await EvaluatedProjectGraph.WithPersistedReferenceMetadataAsync(
                        "completion-boundary", completed, policy, CancellationToken.None);
                    Assert.AreSame(completed, NativeCompilerContextCertificate.Require(projection.Solution),
                        "Image reads must use the newly completed certificate, not the earlier loader certificate.");
                    var admittedProject = projection.Solution.Projects.Single();
                    var admittedReference = admittedProject.MetadataReferences.OfType<PortableExecutableReference>()
                        .Single(reference => FileSystemPath.Comparer.Equals(reference.FilePath, imagePath));
                    var images = await completed.ReadAdmittedImagesAsync(projection.Solution, admittedProject.Id,
                        admittedReference, "completion-boundary", policy, CancellationToken.None);
                    Assert.HasCount(1, images, "Certified completion must issue usable custom-image admission.");
                    Assert.AreEqual("CompletionImage.dll", images[0].FileName);
                    CollectionAssert.AreEqual(await File.ReadAllBytesAsync(imagePath), images[0].Image.ToArray());
                }
                else if (scenario == "revoked-grant")
                    await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => epoch.CompleteAsync("completion-boundary", native, policy, CancellationToken.None));
                else
                    await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => epoch.CompleteAsync("completion-boundary", native, policy, CancellationToken.None));
                if (scenario != "stable") Assert.IsTrue(policy.MutationReached, "The controlled authority boundary must be reached before refusal.");
                if (scenario is "new-compile" or "known-source" or "revoked-grant")
                    Assert.IsFalse(policy.ImageAuthorizationObserved, "Pre-image input/membership refusal must precede explicit custom-image admission.");
                if (scenario == "image-source-drift") Assert.IsTrue(policy.ImageAuthorizationObserved);
                if (scenario == "new-compile")
                {
                    Assert.IsTrue(File.Exists(addedPath));
                    Assert.AreEqual("public sealed class Owned { }", await File.ReadAllTextAsync(sourcePath), "New membership must refuse without changing any original source byte.");
                }
                if (scenario is "known-source" or "image-source-drift")
                    StringAssert.Contains(await File.ReadAllTextAsync(sourcePath), "completion-boundary-change");
            }
            catch (Exception failure) { loadFailure = failure; throw; }
            finally
            {
                OwnedResourceCleanup.Dispose("Completion boundary native cleanup failed.", loadFailure,
                    completionWorkspace, completionLease, loaded.Workspace, loaded.Lease);
            }
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Completion boundary fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    private sealed class CompletionBoundaryPolicy(string sourcePath, string imagePath, string addedPath, string scenario) : IWorkspacePathPolicy
    {
        private readonly OwnedTestWorkspacePathPolicy _owned = new();
        private bool _armed;
        private bool _denySource;
        internal bool MutationReached { get; private set; }
        internal bool ImageAuthorizationObserved { get; private set; }
        internal void Arm() => _armed = true;

        public async Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        {
            if (_denySource && paths.Contains(sourcePath, FileSystemPath.Comparer))
                throw new UnauthorizedAccessException("The completion source grant was revoked.");
            await _owned.ValidateAsync(workspaceId, paths, ct);
            if (!_armed) return;
            var imageOnly = paths.Count == 1 && FileSystemPath.Comparer.Equals(paths[0], imagePath);
            if (imageOnly) ImageAuthorizationObserved = true;
            if (MutationReached || scenario == "stable") return;
            if (scenario == "image-source-drift" ? !imageOnly : !paths.Contains(sourcePath, FileSystemPath.Comparer)) return;
            MutationReached = true;
            if (scenario == "revoked-grant")
            {
                _denySource = true;
                throw new UnauthorizedAccessException("The completion source grant was revoked.");
            }
            if (scenario == "new-compile")
                await File.WriteAllTextAsync(addedPath, "public sealed class Added { }", ct);
            else
                await File.AppendAllTextAsync(sourcePath, "\n// completion-boundary-change\n", ct);
        }
    }

    private static byte[] Emit(string name, string source, OutputKind kind, ImmutableArray<MetadataReference> additional)
    {
        var compilation = CSharpCompilation.Create(name, [CSharpSyntaxTree.ParseText(source)],
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) }.Concat(additional), new CSharpCompilationOptions(kind));
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.IsTrue(emitted.Success, string.Join("\n", emitted.Diagnostics));
        return stream.ToArray();
    }

    private static Guid Mvid(byte[] image)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        return metadata.GetGuid(metadata.GetModuleDefinition().Mvid);
    }
}
