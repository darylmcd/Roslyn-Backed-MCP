using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>An independently approved installation, never authority inferred from assets paths alone.</summary>
internal sealed class InstalledPackageAuthority
{
    internal const string DependencyGrantRequiredKey = "PackageDependencyGrantRequired";
    private readonly ImmutableDictionary<string, ImmutableArray<string>> _installations;
    private readonly ImmutableArray<PhysicalInputIdentity> _packageInputs;
    private readonly ImmutableHashSet<string> _independentlyAuthorizedFiles;
    internal ImmutableArray<PhysicalInputIdentity> AssetInputs { get; }
    internal ImmutableArray<PhysicalInputIdentity> Inputs { get; }
    internal ImmutableArray<string> Files { get; }
    private readonly ImmutableHashSet<string> _signedArchives;
    internal int SignedPackageCount => _signedArchives.Count;
    internal ImmutableArray<string> RequiresCurrentGrant { get; }
    private InstalledPackageAuthority(ImmutableDictionary<string, ImmutableArray<string>> installations, ImmutableArray<PhysicalInputIdentity> packageInputs,
        ImmutableArray<PhysicalInputIdentity> assetInputs, ImmutableHashSet<string> signedArchives)
    {
        _installations = installations;
        _packageInputs = packageInputs;
        AssetInputs = assetInputs;
        Inputs = PhysicalInputIdentity.MergeConsistent(packageInputs.Concat(assetInputs));
        _signedArchives = signedArchives;
        Files = packageInputs.Select(input => input.FilePath).ToImmutableArray();
        RequiresCurrentGrant = Files.Where(path => !BuildInputProvenance.IsBootstrapPackagePath(path))
            .Concat(AssetInputs.Select(input => input.FilePath)).Distinct(FileSystemPath.Comparer).ToImmutableArray();
        _independentlyAuthorizedFiles = Files.Except(RequiresCurrentGrant, FileSystemPath.Comparer).ToImmutableHashSet(FileSystemPath.Comparer);
    }

    public override string ToString() => $"Installed packages: {_installations.Count}; inputs: {Inputs.Length}";

    internal string[] PathsRequiringCurrentGrant(IEnumerable<string> paths)
    {
        return paths.Where(path => !_independentlyAuthorizedFiles.Contains(path)).Distinct(FileSystemPath.Comparer).ToArray();
    }

    internal static InstalledPackageAuthority Merge(IEnumerable<InstalledPackageAuthority> proofs)
    {
        var sources = proofs.ToArray();
        return MergeCore(sources, PhysicalInputIdentity.MergeConsistent(sources.SelectMany(proof => proof.AssetInputs)));
    }

    internal InstalledPackageAuthority RetainPackageFilesFrom(InstalledPackageAuthority previous)
        => MergeCore([this, previous], AssetInputs);

    private static InstalledPackageAuthority MergeCore(IEnumerable<InstalledPackageAuthority> proofs,
        ImmutableArray<PhysicalInputIdentity> selectingAssets)
    {
        var installations = ImmutableDictionary.CreateBuilder<string, ImmutableArray<string>>(StringComparer.OrdinalIgnoreCase);
        var inputs = ImmutableArray.CreateBuilder<PhysicalInputIdentity>();
        var signed = ImmutableHashSet.CreateBuilder<string>(FileSystemPath.Comparer);
        foreach (var proof in proofs)
        {
            foreach (var pair in proof._installations)
                installations[pair.Key] = installations.TryGetValue(pair.Key, out var prior)
                    ? prior.Concat(pair.Value).Distinct(FileSystemPath.Comparer).ToImmutableArray() : pair.Value;
            inputs.AddRange(proof._packageInputs);
            signed.UnionWith(proof._signedArchives);
        }
        return new(installations.ToImmutable(), PhysicalInputIdentity.MergeConsistent(inputs), selectingAssets, signed.ToImmutable());
    }

    internal ImmutableArray<string> Installations(string identity) => _installations.TryGetValue(identity, out var paths) ? paths
        : throw Refusal();

    internal InstalledPackageAuthority MapSelectingAssets(Func<string, string> mapPath) =>
        new(_installations, _packageInputs, AssetInputs.Select(input => input with { FilePath = mapPath(input.FilePath) }).ToImmutableArray(), _signedArchives);

    internal static async Task<InstalledPackageAuthority> CaptureMissingAssetsAsync(string path,
        Func<IReadOnlyList<string>, CancellationToken, Task> authorizePaths, CancellationToken ct)
    {
        await authorizePaths([path], ct).ConfigureAwait(false);
        var missing = new PhysicalInputIdentity(path, false, []);
        await PhysicalInputIdentity.AssertCurrentAsync([missing], ct).ConfigureAwait(false);
        return new(ImmutableDictionary.Create<string, ImmutableArray<string>>(StringComparer.OrdinalIgnoreCase), [], [missing], ImmutableHashSet.Create<string>(FileSystemPath.Comparer));
    }

    internal Task AssertPackageFilesCurrentAsync(Func<IReadOnlyList<string>, CancellationToken, Task> authorizePaths, CancellationToken ct)
        => AssertPackageInputsCurrentAsync(_packageInputs, authorizePaths, ct);

    private static async Task AssertPackageInputsCurrentAsync(ImmutableArray<PhysicalInputIdentity> inputs,
        Func<IReadOnlyList<string>, CancellationToken, Task> authorizePaths, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        inputs = PhysicalInputIdentity.MergeConsistent(inputs);
        await AuthorizeCurrentGrantAsync(inputs.Select(input => input.FilePath).ToArray(), authorizePaths, ct).ConfigureAwait(false);
        await PhysicalInputCaptureBatch.AssertCurrentAsync(inputs, ct).ConfigureAwait(false);
    }

    internal async Task AssertCurrentAsync(Func<IReadOnlyList<string>, CancellationToken, Task> authorizePaths, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Preserve dependency-versus-mandatory refusal provenance and precedence.
        // Both complete grant phases finish before any retained input is read.
        await AuthorizeCurrentGrantAsync(_packageInputs.Select(input => input.FilePath).ToArray(), authorizePaths, ct).ConfigureAwait(false);
        await authorizePaths(AssetInputs.Select(input => input.FilePath).ToArray(), ct).ConfigureAwait(false);
        await PhysicalInputCaptureBatch.AssertCurrentAsync(Inputs, ct).ConfigureAwait(false);
    }

    internal static async Task<InstalledPackageAuthority> CreateAsync(string assetsPath, byte[] approvedAssets,
        IReadOnlyList<string> authoredProjectDirectories, Func<IReadOnlyList<string>, CancellationToken, Task> authorizePaths, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await authorizePaths([assetsPath], ct).ConfigureAwait(false);
        var identity = Identity(assetsPath, approvedAssets);
        await PhysicalInputIdentity.AssertCurrentAsync([identity], ct).ConfigureAwait(false);
        using var document = JsonDocument.Parse(approvedAssets);
        var proof = await CreateCoreAsync(document.RootElement, authoredProjectDirectories, authorizePaths, ct).ConfigureAwait(false);
        var issued = new InstalledPackageAuthority(proof._installations, proof._packageInputs, [identity], proof._signedArchives);
        await issued.AssertCurrentAsync(authorizePaths, ct).ConfigureAwait(false);
        return issued;
    }

    private static async Task<InstalledPackageAuthority> CreateCoreAsync(JsonElement root, IReadOnlyList<string> authoredProjectDirectories,
        Func<IReadOnlyList<string>, CancellationToken, Task> authorizePaths, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        MsBuildInitializer.EnsureInitialized();
        if (root.ValueKind != JsonValueKind.Object) throw Refusal();
        var installations = ImmutableDictionary.CreateBuilder<string, ImmutableArray<string>>(StringComparer.OrdinalIgnoreCase);
        var inputs = ImmutableArray.CreateBuilder<PhysicalInputIdentity>();
        var signed = ImmutableHashSet.CreateBuilder<string>(FileSystemPath.Comparer);
        foreach (var request in ReadRequests(root))
        {
            ct.ThrowIfCancellationRequested();
            var id = request.Id;
            var version = request.Version;
            if (!NuGet.Packaging.PackageIdValidator.IsValidPackageId(id)) throw Refusal();
            var canonical = id.ToLowerInvariant() + "/" + version.ToNormalizedString().ToLowerInvariant();
            if (request.Library is { } library && (!library.TryGetProperty("path", out var relative)
                || relative.ValueKind != JsonValueKind.String || relative.GetString() != canonical)) throw Refusal();
            if (!root.TryGetProperty("packageFolders", out var folders) || folders.ValueKind != JsonValueKind.Object) throw Refusal();
            var found = false;
            foreach (var folder in folders.EnumerateObject())
            {
                if (!Path.IsPathFullyQualified(folder.Name)) throw Refusal();
                var directory = Path.GetFullPath(canonical, folder.Name);
                if (!FileSystemPath.IsStrictDescendant(Path.GetFullPath(folder.Name), directory)) throw Refusal();
                if (authoredProjectDirectories.Any(authored => FileSystemPath.Comparer.Equals(directory, authored)
                    || FileSystemPath.IsStrictDescendant(directory, authored))) throw Refusal();
                var prefix = id.ToLowerInvariant() + "." + version.ToNormalizedString().ToLowerInvariant();
                var archivePath = Path.Combine(directory, prefix + ".nupkg");
                var hashPath = archivePath + ".sha512";
                var metadataPath = Path.Combine(directory, ".nupkg.metadata");
                // Independent process cache provenance or the CURRENT exact grant precedes every explicit read.
                await AuthorizeAsync([archivePath, hashPath, metadataPath], authorizePaths, ct).ConfigureAwait(false);
                var archiveBytes = await ReadAsync(archivePath, PhysicalInputReader.ReadAllBytesAsync, authorizePaths, ct).ConfigureAwait(false);
                if (archiveBytes is null) continue;
                using var stream = new MemoryStream(archiveBytes, writable: false);
                using var archive = new NuGet.Packaging.PackageArchiveReader(stream);
                var hash = archive.GetContentHash(ct);
                var archiveHash = Convert.ToBase64String(await archive.GetArchiveHashAsync(NuGet.Common.HashAlgorithmName.SHA512, ct).ConfigureAwait(false));
                if (await archive.IsSignedAsync(ct).ConfigureAwait(false))
                {
                    var signature = await archive.GetPrimarySignatureAsync(ct).ConfigureAwait(false) ?? throw Refusal();
                    await archive.ValidateIntegrityAsync(signature.SignatureContent, ct).ConfigureAwait(false);
                    signed.Add(archivePath);
                }
                var hashBytes = await RequiredBytesAsync(hashPath, authorizePaths, ct).ConfigureAwait(false);
                var metadataBytes = await RequiredBytesAsync(metadataPath, authorizePaths, ct).ConfigureAwait(false);
                if (System.Text.Encoding.UTF8.GetString(hashBytes).Trim() != archiveHash) throw Refusal();
                using (var metadata = JsonDocument.Parse(metadataBytes))
                    if (metadata.RootElement.ValueKind != JsonValueKind.Object
                        || !metadata.RootElement.TryGetProperty("contentHash", out var recordedHash)
                        || recordedHash.ValueKind != JsonValueKind.String || recordedHash.GetString() != hash) throw Refusal();
                if (request.Library is { } selectedLibrary && (!selectedLibrary.TryGetProperty("sha512", out var assetsHash)
                    || assetsHash.ValueKind != JsonValueKind.String
                    || assetsHash.GetString()?.Replace("sha512-", "", StringComparison.Ordinal) != hash)) throw Refusal();
                var identity = archive.GetIdentity();
                if (!identity.Id.Equals(id, StringComparison.OrdinalIgnoreCase) || !identity.Version.Equals(version)) throw Refusal();
                var entries = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var entry in archive.GetFiles())
                {
                    var name = Uri.UnescapeDataString(entry.TrimStart('/')).Replace('\\', '/');
                    if (!entries.TryAdd(name, entry)) throw Refusal();
                }
                var nuspec = archive.GetNuspecFile();
                var files = InstallationFiles(request, archive, nuspec, id);
                var current = new List<PhysicalInputIdentity>
                {
                    Identity(archivePath, archiveBytes), Identity(hashPath, hashBytes), Identity(metadataPath, metadataBytes)
                };
                foreach (var name in files)
                {
                    ct.ThrowIfCancellationRequested();
                    if (string.IsNullOrEmpty(name) || Path.IsPathRooted(name)) throw Refusal();
                    var path = Path.GetFullPath(name, directory);
                    if (!FileSystemPath.IsStrictDescendant(directory, path)) throw Refusal();
                    var digest = await ReadAsync(path, PhysicalInputReader.ReadDigestAsync, authorizePaths, ct).ConfigureAwait(false) ?? throw Refusal();
                    var captured = new PhysicalInputIdentity(path, true, digest.Hash);
                    if (name != prefix + ".nupkg.sha512" && name != ".nupkg.metadata")
                    {
                        var key = name.Replace('\\', '/');
                        var entry = key == id.ToLowerInvariant() + ".nuspec" ? nuspec
                            : entries.TryGetValue(key, out var candidate) ? candidate : throw Refusal();
                        using var contents = archive.GetStream(entry);
                        var expected = await SHA256.HashDataAsync(contents, ct).ConfigureAwait(false);
                        if (!expected.AsSpan().SequenceEqual(captured.ContentHash.AsSpan())) throw Refusal();
                    }
                    current.Add(captured);
                }
                // Bind the source manifest/installation throughout issuance, including concurrent extraction changes.
                await AssertPackageInputsCurrentAsync([.. current], authorizePaths, ct).ConfigureAwait(false);
                inputs.AddRange(current);
                installations[request.Identity] = [directory];
                found = true;
                break;
            }
            if (!found) throw Refusal();
        }
        return new(installations.ToImmutable(), PhysicalInputIdentity.MergeConsistent(inputs), [], signed.ToImmutable());
    }

    private sealed record InstallationRequest(string Id, NuGet.Versioning.NuGetVersion Version, JsonElement? Library)
    {
        internal string Identity => Id + "/" + Version.ToNormalizedString();
    }

    private static IEnumerable<InstallationRequest> ReadRequests(JsonElement root)
    {
        var requests = new Dictionary<string, InstallationRequest>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("libraries", out var libraries))
        {
            if (libraries.ValueKind != JsonValueKind.Object) throw Refusal();
            foreach (var library in libraries.EnumerateObject())
            {
                if (library.Value.ValueKind != JsonValueKind.Object || !library.Value.TryGetProperty("type", out var type)
                    || type.ValueKind != JsonValueKind.String || type.GetString() != "package") continue;
                var separator = library.Name.LastIndexOf('/');
                if (separator < 1 || !NuGet.Versioning.NuGetVersion.TryParse(library.Name[(separator + 1)..], out var version)) throw Refusal();
                var request = new InstallationRequest(library.Name[..separator], version, library.Value);
                if (!requests.TryAdd(request.Identity, request)) throw Refusal();
            }
        }
        if (root.TryGetProperty("project", out var project))
        {
            if (project.ValueKind != JsonValueKind.Object) throw Refusal();
            if (!project.TryGetProperty("frameworks", out var frameworks)) return requests.Values;
            if (frameworks.ValueKind != JsonValueKind.Object) throw Refusal();
            foreach (var framework in frameworks.EnumerateObject())
            {
                if (framework.Value.ValueKind != JsonValueKind.Object) throw Refusal();
                if (!framework.Value.TryGetProperty("downloadDependencies", out var downloads)) continue;
                if (downloads.ValueKind != JsonValueKind.Array) throw Refusal();
                foreach (var download in downloads.EnumerateArray())
                {
                    if (download.ValueKind != JsonValueKind.Object || !download.TryGetProperty("name", out var name)
                        || name.ValueKind != JsonValueKind.String || name.GetString() is not { Length: > 0 } id
                        || !download.TryGetProperty("version", out var selected) || selected.ValueKind != JsonValueKind.String
                        || !NuGet.Versioning.VersionRange.TryParse(selected.GetString(), out var range)
                        || !range.IsMinInclusive || !range.IsMaxInclusive || range.MinVersion is not { } version
                        || range.MaxVersion is null || !version.Equals(range.MaxVersion) || range.IsFloating) throw Refusal();
                    var request = new InstallationRequest(id, version, null);
                    // A library entry supplies stronger assets hash/inventory evidence for the same installation.
                    requests.TryAdd(request.Identity, request);
                }
            }
        }
        return requests.Values;
    }

    private static IEnumerable<string> InstallationFiles(InstallationRequest request,
        NuGet.Packaging.PackageArchiveReader archive, string nuspec, string id)
    {
        if (request.Library is { } library)
        {
            if (!library.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array) throw Refusal();
            return files.EnumerateArray().Select(file => file.ValueKind == JsonValueKind.String && file.GetString() is { Length: > 0 } name
                ? name : throw Refusal()).ToArray();
        }
        // PackageDownload records exact identity, not a libraries hash/inventory. Use NuGet's actual
        // extraction rules, then prove every extracted payload against the independently approved archive.
        return archive.GetFiles().Where(file => NuGet.Packaging.PackageHelper.IsPackageFile(file, NuGet.Packaging.PackageSaveMode.Defaultv3))
            .Select(file => file == nuspec ? id.ToLowerInvariant() + ".nuspec" : Uri.UnescapeDataString(file.TrimStart('/')).Replace('\\', '/')).ToArray();
    }

    private static async Task AuthorizeAsync(IReadOnlyList<string> paths,
        Func<IReadOnlyList<string>, CancellationToken, Task> authorizePaths, CancellationToken ct)
    {
        await AuthorizeCurrentGrantAsync(paths, authorizePaths, ct).ConfigureAwait(false);
        foreach (var path in paths) WorkspaceManager.EnsurePhysicalPath(path);
    }

    private static async Task AuthorizeCurrentGrantAsync(IReadOnlyList<string> paths,
        Func<IReadOnlyList<string>, CancellationToken, Task> authorizePaths, CancellationToken ct)
    {
        try { await authorizePaths(paths.Where(path => !BuildInputProvenance.IsBootstrapPackagePath(path)).ToArray(), ct).ConfigureAwait(false); }
        catch (UnauthorizedAccessException original)
        {
            var failure = new UnauthorizedAccessException("The recorded package installation requires a current path grant. Authorize the exact recorded cache and reload.");
            WorkspacePathPolicyRefusal.Transfer(original, failure);
            failure.Data[DependencyGrantRequiredKey] = true;
            throw failure;
        }
        catch (Exception failure) when (WorkspacePathPolicyRefusal.IsDenied(failure))
        {
            failure.Data[DependencyGrantRequiredKey] = true;
            throw;
        }
    }
    private static async Task<byte[]> RequiredBytesAsync(string path,
        Func<IReadOnlyList<string>, CancellationToken, Task> authorizePaths, CancellationToken ct)
        => await ReadAsync(path, PhysicalInputReader.ReadAllBytesAsync, authorizePaths, ct).ConfigureAwait(false) ?? throw Refusal();
    private static async Task<T?> ReadAsync<T>(string path, Func<string, CancellationToken, Task<T>> read,
        Func<IReadOnlyList<string>, CancellationToken, Task> authorizePaths, CancellationToken ct) where T : class
    {
        // Batch preflight does not carry grants across archive parsing or earlier reads.
        // Keep policy refusals outside IO redaction so their typed provenance survives.
        await AuthorizeAsync([path], authorizePaths, ct).ConfigureAwait(false);
        try { return await FileSnapshotCapture.ReadIfPresentAsync(path, read, ct).ConfigureAwait(false); }
        catch (UnauthorizedAccessException) { throw new UnauthorizedAccessException("Installed package input access was refused."); }
        catch (IOException) { throw new IOException("Installed package inputs could not be read."); }
    }
    private static PhysicalInputIdentity Identity(string path, byte[] bytes) => new(path, true, [.. SHA256.HashData(bytes)]);
    private static PublicInvalidOperationException Refusal() => new("Installed package provenance is unavailable. Restore into an approved package cache or authorize the exact recorded cache and reload.");
}
