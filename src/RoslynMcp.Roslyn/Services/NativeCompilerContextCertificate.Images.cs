using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

internal sealed partial class NativeCompilerContextCertificate
{
    private sealed record ModuleAdmission(string Path, string Name, Guid Mvid, ImmutableArray<byte> Hash)
    {
        public override string ToString() => "AdmittedModuleIdentity";
    }
    private abstract record ImageAdmission;
    private sealed record AvailableImages(ImmutableArray<ModuleAdmission> Modules) : ImageAdmission;
    private sealed record UnavailableImages : ImageAdmission;
    private ImmutableDictionary<ProjectId, ImmutableDictionary<PortableExecutableReference, ImageAdmission>>? _admittedImages;

    // Invoked only by the owned native load completion, before the certificate escapes.
    private async Task CaptureAdmittedImagesAsync(string workspaceId, IWorkspacePathPolicy policy, CancellationToken ct)
    {
        if (_admittedImages is not null) throw InvalidOperationErrors.Internal("Image authority was already issued.");
        var authority = RequireCertified();
        var reader = BindInputReader((paths, token) => policy.ValidateAsync(workspaceId, paths, token), ReadPhysicalAsync, ReadPhysicalAsync);
        var projects = ImmutableDictionary.CreateBuilder<ProjectId, ImmutableDictionary<PortableExecutableReference, ImageAdmission>>();
        foreach (var project in NativeSolution.Projects)
        {
            var references = ImmutableDictionary.CreateBuilder<PortableExecutableReference, ImageAdmission>(ReferenceEqualityComparer.Instance);
            foreach (var reference in project.MetadataReferences.OfType<PortableExecutableReference>())
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (reference.FilePath is not { } path) throw ImageRefusal();
                    var bytes = await reader.Read(path, ct).ConfigureAwait(false) ?? throw ImageRefusal();
                    var main = Inspect(bytes);
                    using var manifestPe = new PEReader(new MemoryStream(bytes, writable: false));
                    var manifestReader = manifestPe.GetMetadataReader();
                    var paths = main.LinkedModules.Select(name => LinkedPath(path, name)).Prepend(path).ToArray();
                    await reader.Authorize(paths, ct).ConfigureAwait(false);
                    // Borrow existing native metadata without expanding its lazy factory.
                    // Linked buffers are independently bound by the admitted manifest hash;
                    // already materialized native modules must also match their full metadata.
                    var metadata = BorrowAdmittedMetadata(reference);
                    var deferred = false;
                    var admitted = metadata switch
                    {
                        AssemblyMetadata assembly => ReadAdmittedAssemblyModules(assembly, bytes, out deferred),
                        ModuleMetadata module => ImmutableArray.Create(module),
                        _ => throw ImageRefusal()
                    };
                    if (paths.Distinct(FileSystemPath.Comparer).Count() != paths.Length
                        || (deferred ? admitted.Length != 1 : paths.Length != admitted.Length))
                        throw ImageRefusal();
                    var modules = ImmutableArray.CreateBuilder<ModuleAdmission>();
                    var remaining = admitted.ToList();
                    for (var index = 0; index < paths.Length; index++)
                    {
                        var image = index == 0 ? bytes : await reader.Read(paths[index], ct).ConfigureAwait(false) ?? throw ImageRefusal();
                        if (index != 0 && !AssemblyManifestHashVerifier.Matches(manifestReader, Path.GetFileName(paths[index]), image, ct))
                            throw ImageRefusal();
                        var identity = index == 0 ? main : Inspect(image);
                        var matches = remaining.Where(item => ModuleIdentity(item) == (identity.Name, identity.Mvid)).ToArray();
                        if (matches.Length > 1 || (matches.Length == 0 && (index == 0 || !deferred))) throw ImageRefusal();
                        if (matches.Length == 1)
                        {
                            AssertAdmittedModuleIdentity(matches[0], image);
                            remaining.Remove(matches[0]);
                        }
                        var hash = ImmutableArray.CreateRange(SHA256.HashData(image));
                        var prior = authority.Inputs.Where(input => FileSystemPath.Comparer.Equals(input.FilePath, paths[index])).ToArray();
                        if (prior.Any(input => !input.Exists || !input.ContentHash.AsSpan().SequenceEqual(hash.AsSpan())))
                            throw new PublicInvalidOperationException("An admitted image changed during native loading; reload the workspace.");
                        modules.Add(new(paths[index], identity.Name, identity.Mvid, hash));
                    }
                    if (remaining.Count != 0) throw ImageRefusal();
                    // Re-read under the same current authority: issuance cannot span an image rewrite.
                    var finalBuffers = new List<(ModuleAdmission Module, byte[] Image)>();
                    foreach (var module in modules)
                    {
                        var image = await ReadImageAsync(module, reader.Read, ct).ConfigureAwait(false);
                        finalBuffers.Add((module, image));
                    }
                    // No awaited policy or byte callback may intervene between this native
                    // cache observation and publication of the exact image admission.
                    AssertNativeImageBuffers(reference, finalBuffers);
                    GC.KeepAlive(metadata);
                    GC.KeepAlive(reference);
                    references.Add(reference, new AvailableImages(modules.ToImmutable()));
                }
                catch (Exception failure) when (failure is IOException or BadImageFormatException or PublicInvalidOperationException
                    || WorkspacePathPolicyRefusal.IsDenied(failure))
                {
                    // The semantic native snapshot remains useful; later grants require a fresh load,
                    // never a new hash captured from an already-published reference.
                    references.Add(reference, new UnavailableImages());
                }
            }
            projects.Add(project.Id, references.ToImmutable());
        }
        _admittedImages = projects.ToImmutable();
    }

    internal async Task<ImmutableArray<AdmittedModuleImage>> ReadAdmittedImagesAsync(Solution solution, ProjectId projectId,
        PortableExecutableReference reference, string workspaceId, IWorkspacePathPolicy policy, CancellationToken ct)
    {
        var authority = RequireCertified();
        try
        {
            if (!ReferenceEquals(Require(solution), this) || solution.GetProject(projectId) is not { } project
                || !project.MetadataReferences.Any(item => ReferenceEquals(item, reference))
                || _admittedImages is null || !_admittedImages.TryGetValue(projectId, out var references)
                || !references.TryGetValue(reference, out var admission) || admission is not AvailableImages available)
                throw ImageRefusal();
            await AssertCurrentAsync(solution, workspaceId, policy, ct).ConfigureAwait(false);
            var reader = BindInputReader((paths, token) => policy.ValidateAsync(workspaceId, paths, token), ReadPhysicalAsync, ReadPhysicalAsync);
            foreach (var module in available.Modules)
                _ = await ReadImageAsync(module, reader.Read, ct).ConfigureAwait(false);
            await AssertCurrentAsync(solution, workspaceId, policy, ct).ConfigureAwait(false);
            var images = ImmutableArray.CreateBuilder<AdmittedModuleImage>();
            var finalBuffers = new List<(ModuleAdmission Module, byte[] Image)>();
            foreach (var module in available.Modules)
            {
                var bytes = await ReadImageAsync(module, reader.Read, ct).ConfigureAwait(false);
                finalBuffers.Add((module, bytes));
                images.Add(new(Path.GetFileName(module.Path), module.Name, module.Mvid, ImmutableArray.CreateRange(bytes)));
            }
            // Returned-buffer reads await current policy callbacks. They must not
            // invalidate the source, selecting assets or grants checked above.
            await AssertCurrentAsync(solution, workspaceId, policy, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            // A deferred native factory may have materialized metadata after issuance or
            // during any awaited read above. Compare against these exact returned buffers.
            AssertNativeImageBuffers(reference, finalBuffers);
            ct.ThrowIfCancellationRequested();
            return images.ToImmutable();
        }
        catch (Exception failure) { throw authority.Redactor.Failure(failure); }
    }

    private static async Task<byte[]> ReadImageAsync(ModuleAdmission module,
        Func<string, CancellationToken, Task<byte[]?>> read, CancellationToken ct)
    {
        var bytes = await read(module.Path, ct).ConfigureAwait(false) ?? throw ImageRefusal();
        if (!SHA256.HashData(bytes).AsSpan().SequenceEqual(module.Hash.AsSpan())) throw ImageRefusal();
        var actual = Inspect(bytes);
        if (actual.Name != module.Name || actual.Mvid != module.Mvid) throw ImageRefusal();
        return bytes;
    }

    private static (string Name, Guid Mvid) ModuleIdentity(ModuleMetadata module)
    {
        var reader = module.GetMetadataReader();
        var definition = reader.GetModuleDefinition();
        return (reader.GetString(definition.Name), reader.GetGuid(definition.Mvid));
    }

    private static void AssertNativeImageBuffers(PortableExecutableReference reference,
        IReadOnlyList<(ModuleAdmission Module, byte[] Image)> buffers)
    {
        if (buffers.Count == 0) throw ImageRefusal();
        var metadata = BorrowAdmittedMetadata(reference);
        var deferred = false;
        var cached = metadata switch
        {
            AssemblyMetadata assembly => ReadAdmittedAssemblyModules(assembly, buffers[0].Image, out deferred),
            ModuleMetadata module => ImmutableArray.Create(module),
            _ => throw ImageRefusal()
        };
        if ((deferred ? cached.Length != 1 : cached.Length != buffers.Count)
            || cached.Select(ModuleIdentity).Distinct().Count() != cached.Length) throw ImageRefusal();
        foreach (var module in cached)
        {
            var identity = ModuleIdentity(module);
            var matching = buffers.Where(buffer => (buffer.Module.Name, buffer.Module.Mvid) == identity).ToArray();
            if (matching.Length != 1) throw ImageRefusal();
            AssertAdmittedModuleIdentity(module, matching[0].Image);
        }
        GC.KeepAlive(metadata);
        GC.KeepAlive(reference);
    }

    private static ImmutableArray<ModuleMetadata> ReadAdmittedAssemblyModules(AssemblyMetadata assembly, byte[] manifest, out bool deferred)
    {
        // Public GetModules invokes a cached factory before returning the manifest.
        // Pin the private readonly field contract to our compiled Roslyn assembly identity,
        // read its value, and validate the full manifest before observing cached modules.
        var type = typeof(AssemblyMetadata);
        RequireRoslynMetadataContract();
        var field = type.GetField("_initialModules", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (field is null || !field.IsInitOnly
            || field.DeclaringType != type || field.FieldType != typeof(ImmutableArray<ModuleMetadata>)) throw ImageRefusal();
        ImmutableArray<ModuleMetadata> initial;
        try { initial = InitialAssemblyModules(assembly); }
        catch (Exception failure) when (failure is MissingFieldException or MissingMethodException or TypeLoadException)
        { throw ImageRefusal(); }
        if (initial.IsDefaultOrEmpty) throw ImageRefusal();
        AssertAdmittedModuleIdentity(initial[0], manifest);
        var factoryField = type.GetField("_moduleFactoryOpt", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var dataField = type.GetField("_lazyData", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var dataType = type.GetNestedType("Data", System.Reflection.BindingFlags.NonPublic);
        if (factoryField is null || !factoryField.IsInitOnly || factoryField.IsStatic || factoryField.DeclaringType != type
            || factoryField.FieldType != typeof(Func<string, ModuleMetadata>)
            || dataType is null || !dataType.IsSealed || !dataType.IsNestedPrivate || dataType.DeclaringType != type
            || dataField is null || dataField.IsStatic || dataField.IsInitOnly || dataField.DeclaringType != type
            || dataField.FieldType != dataType) throw ImageRefusal();
        var modulesField = dataType.GetField("Modules", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        if (modulesField is null || !modulesField.IsInitOnly || modulesField.IsStatic || modulesField.DeclaringType != dataType
            || modulesField.FieldType != typeof(ImmutableArray<ModuleMetadata>)) throw ImageRefusal();
        var data = dataField.GetValue(assembly);
        deferred = data is null && factoryField.GetValue(assembly) is not null;
        if (deferred && initial.Length != 1) throw ImageRefusal();
        if (data is null) return initial;
        if (data.GetType() != dataType || modulesField.GetValue(data) is not ImmutableArray<ModuleMetadata> cached
            || cached.IsDefaultOrEmpty || !ReferenceEquals(cached[0], initial[0])) throw ImageRefusal();
        return cached;
    }

    // No field mutation and no ref escapes this class. Roslyn upgrades must retain this
    // exact contract or image authority fails closed while semantic loading remains usable.
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_initialModules")]
    private static extern ref ImmutableArray<ModuleMetadata> InitialAssemblyModules(AssemblyMetadata assembly);

    private static Metadata BorrowAdmittedMetadata(PortableExecutableReference reference)
    {
        RequireRoslynMetadataContract();
        var type = typeof(PortableExecutableReference);
        var method = type.GetMethod("GetMetadataNoCopy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            binder: null, types: Type.EmptyTypes, modifiers: null);
        if (method is null || method.DeclaringType != type || method.ReturnType != typeof(Metadata)
            || method.IsStatic || method.IsGenericMethod || method.IsVirtual || !method.IsAssembly) throw ImageRefusal();
        try { return ReferenceMetadataNoCopy(reference); }
        catch (Exception failure) when (failure is MissingMethodException or TypeLoadException)
        { throw ImageRefusal(); }
    }

    private static void RequireRoslynMetadataContract()
    {
        var expected = typeof(NativeCompilerContextCertificate).Assembly.GetReferencedAssemblies()
            .SingleOrDefault(identity => identity.Name == "Microsoft.CodeAnalysis");
        var assembly = typeof(AssemblyMetadata).Assembly;
        if (expected is null || expected.FullName != assembly.GetName().FullName
            || typeof(ModuleMetadata).Assembly != assembly || typeof(PortableExecutableReference).Assembly != assembly
            || typeof(Metadata).Assembly != assembly) throw ImageRefusal();
    }

    // This accessor returns the native-owned instance; it creates no copy and transfers no
    // ownership. The workspace/reference remains alive until image capture has completed.
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "GetMetadataNoCopy")]
    private static extern Metadata ReferenceMetadataNoCopy(PortableExecutableReference reference);

    // The identity comparison uses metadata already admitted by Roslyn, never the new
    // file as its own proof. MVID alone does not bind the assembly definition.
    internal static unsafe void AssertAdmittedModuleIdentity(ModuleMetadata admitted, byte[] image)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var pe = new PEReader(stream);
        var actual = pe.GetMetadataReader();
        var expected = admitted.GetMetadataReader();
        var module = actual.GetModuleDefinition();
        if (ModuleIdentity(admitted) != (actual.GetString(module.Name), actual.GetGuid(module.Mvid))
            || AssemblyIdentity(expected) != AssemblyIdentity(actual)) throw ImageRefusal();
        // The admitted metadata instance stays alive in the caller for this entire scope.
        // Its pointer is read only here, hashed immediately, and never retained.
        var admittedHash = SHA256.HashData(new ReadOnlySpan<byte>(expected.MetadataPointer, expected.MetadataLength));
        GC.KeepAlive(admitted);
        if (!admittedHash.AsSpan().SequenceEqual(SHA256.HashData(pe.GetMetadata().GetContent().AsSpan())))
            throw ImageRefusal();
    }

    private static string AssemblyIdentity(MetadataReader reader)
    {
        if (!reader.IsAssembly) return "Module";
        var assembly = reader.GetAssemblyDefinition();
        return string.Join("|", reader.GetString(assembly.Name), assembly.Version.ToString(),
            reader.GetString(assembly.Culture), ((int)assembly.Flags).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ((int)assembly.HashAlgorithm).ToString(System.Globalization.CultureInfo.InvariantCulture),
            Convert.ToHexString(reader.GetBlobBytes(assembly.PublicKey)));
    }

    private static (string Name, Guid Mvid, ImmutableArray<string> LinkedModules) Inspect(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var module = reader.GetModuleDefinition();
        var linked = reader.AssemblyFiles.Select(handle => reader.GetAssemblyFile(handle))
            .Where(file => file.ContainsMetadata).Select(file => reader.GetString(file.Name)).ToImmutableArray();
        return (reader.GetString(module.Name), reader.GetGuid(module.Mvid), linked);
    }

    private static string LinkedPath(string manifest, string name)
    {
        if (string.IsNullOrEmpty(name) || name is "." or ".." || name.IndexOfAny(['/', '\\', ':']) >= 0)
            throw ImageRefusal();
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifest) ?? throw ImageRefusal(), name));
    }

    private static PublicInvalidOperationException ImageRefusal() => new(
        "The exact admitted module image is unavailable or changed. Authorize its physical input and reload the workspace before inspecting compiled constructor bodies.");
}
