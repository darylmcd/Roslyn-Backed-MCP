using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Issues independent tool roots and exact verified installed-file proofs, never project-configured cache ancestors.</summary>
internal static class BuildInputProvenance
{
    internal static IReadOnlyList<string> ToolRoots()
    {
        MsBuildInitializer.EnsureInitialized();
        return RegisteredToolRoots();
    }

    // Keep SDK types behind the sanctioned Locator bootstrap and out of discovery signatures.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ImmutableArray<string> RegisteredToolRoots()
    {
        var tools = RegisteredSdkDirectory();
        var roots = new HashSet<string>(FileSystemPath.Comparer) { tools, Path.Combine(tools, "Sdks") };
        var sdkParent = Path.GetDirectoryName(tools);
        if (sdkParent is not null && Path.GetFileName(sdkParent).Equals("sdk", StringComparison.OrdinalIgnoreCase)
            && Path.GetDirectoryName(sdkParent) is { } sdkInstallation)
        {
            roots.Add(Path.Combine(sdkInstallation, "packs"));
            roots.Add(Path.Combine(sdkInstallation, "sdk-manifests"));
        }
        var runtime = Path.GetDirectoryName(typeof(object).Assembly.Location)
            ?? throw new PublicInvalidOperationException("The host runtime has no physical installation identity.");
        roots.Add(runtime);
        var runtimeFamily = Path.GetDirectoryName(runtime);
        var shared = runtimeFamily is null ? null : Path.GetDirectoryName(runtimeFamily);
        if (shared is not null && Path.GetFileName(shared).Equals("shared", StringComparison.OrdinalIgnoreCase)
            && Path.GetDirectoryName(shared) is { } runtimeInstallation)
            roots.Add(Path.Combine(runtimeInstallation, "packs"));
        return roots.Select(path => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))).ToImmutableArray();
    }

    internal static string RegisteredSdkDirectory()
    {
        MsBuildInitializer.EnsureInitialized();
        return RegisteredBuildDirectory();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string RegisteredBuildDirectory() => Path.GetDirectoryName(typeof(Microsoft.Build.Evaluation.Project).Assembly.Location)
        ?? throw new PublicInvalidOperationException("The registered build runtime has no physical installation identity.");

    private static ImmutableArray<string> _bootstrapPackageCaches;
    private static readonly object BootstrapGate = new();
    internal static void CaptureBootstrapPackageCaches()
    {
        lock (BootstrapGate)
        {
            if (!_bootstrapPackageCaches.IsDefault) return;
            var configured = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
            var root = string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages") : configured;
            _bootstrapPackageCaches = Path.IsPathFullyQualified(root)
                ? [Path.TrimEndingDirectorySeparator(Path.GetFullPath(root))] : [];
        }
    }
    internal static bool IsBootstrapPackagePath(string path)
    {
        lock (BootstrapGate) return !_bootstrapPackageCaches.IsDefault
            && _bootstrapPackageCaches.Any(root => FileSystemPath.IsStrictDescendant(root, path));
    }

    // A restore destination may be the configured cache itself. This does not
    // issue immutable input membership: installed files still require proof.
    internal static bool IsBootstrapRestoreDestination(string path)
    {
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        lock (BootstrapGate) return !_bootstrapPackageCaches.IsDefault
            && _bootstrapPackageCaches.Any(root => FileSystemPath.Comparer.Equals(root, normalized));
    }

    internal static async Task AuthorizeRestoreDestinationsAsync(IEnumerable<string> destinations,
        Func<IReadOnlyList<string>, CancellationToken, Task> authorizePaths, CancellationToken ct)
    {
        var paths = destinations.Distinct(FileSystemPath.Comparer).ToArray();
        await authorizePaths(paths.Where(path => !IsBootstrapRestoreDestination(path)).ToArray(), ct).ConfigureAwait(false);
        foreach (var path in paths)
        {
            ct.ThrowIfCancellationRequested();
            WorkspaceManager.EnsurePhysicalPath(path);
        }
    }

    /// <summary>Assets are already approved; installation manifests require independent cache or CURRENT grants.</summary>
    internal static async Task<InstalledPackageAuthority> ReadPackagesAsync(IEnumerable<string> approvedAssetsPaths, IReadOnlyList<string> authoredProjectDirectories,
        Func<IReadOnlyList<string>, CancellationToken, Task> authorizePaths, CancellationToken ct)
    {
        var proofs = new List<InstalledPackageAuthority>();
        foreach (var assets in approvedAssetsPaths.Distinct(FileSystemPath.Comparer))
        {
            ct.ThrowIfCancellationRequested();
            await authorizePaths([assets], ct).ConfigureAwait(false);
            WorkspaceManager.EnsurePhysicalPath(assets);
            byte[]? bytes;
            try { bytes = await FileSnapshotCapture.ReadIfPresentAsync(assets, PhysicalInputReader.ReadAllBytesAsync, ct).ConfigureAwait(false); }
            catch (UnauthorizedAccessException) { throw new UnauthorizedAccessException("Recorded restore assets could not be read for package provenance."); }
            catch (IOException) { throw new IOException("Recorded restore assets could not be read for package provenance."); }
            if (bytes is null)
            {
                proofs.Add(await InstalledPackageAuthority.CaptureMissingAssetsAsync(assets, authorizePaths, ct).ConfigureAwait(false));
                continue;
            }
            proofs.Add(await ReadPackagesAsync(assets, bytes, authoredProjectDirectories, authorizePaths, ct).ConfigureAwait(false));
        }
        var merged = InstalledPackageAuthority.Merge(proofs);
        await merged.AssertCurrentAsync(authorizePaths, ct).ConfigureAwait(false);
        return merged;
    }
    internal static async Task<InstalledPackageAuthority> ReadPackagesAsync(string assetsPath, byte[] approvedAssets, IReadOnlyList<string> authoredProjectDirectories,
        Func<IReadOnlyList<string>, CancellationToken, Task> authorizePaths, CancellationToken ct)
    {
        try
        {
            return await InstalledPackageAuthority.CreateAsync(assetsPath, approvedAssets, authoredProjectDirectories, authorizePaths, ct).ConfigureAwait(false);
        }
        catch (Exception failure) when (!WorkspacePathPolicyRefusal.IsDenied(failure)
            && (failure is JsonException or XmlException or InvalidDataException or ArgumentException or NuGet.Packaging.Core.PackagingException))
        { throw new PublicInvalidOperationException("Recorded restore assets do not establish verified installed package provenance. Restore into an approved cache and reload."); }
    }
}
