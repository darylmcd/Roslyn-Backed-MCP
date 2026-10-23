using System.Reflection;
using System.Reflection.Metadata;
using System.Security.Cryptography;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>Proves the CLI manifest's declared linked-file content, independently of image admission.</summary>
internal static class AssemblyManifestHashVerifier
{
    internal static bool Matches(MetadataReader manifest, string fileName, ReadOnlySpan<byte> image, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!manifest.IsAssembly) return false;
        var files = manifest.AssemblyFiles.Select(manifest.GetAssemblyFile)
            .Where(file => file.ContainsMetadata && manifest.GetString(file.Name) == fileName).ToArray();
        if (files.Length != 1 || files[0].HashValue.IsNil) return false;
        var algorithm = manifest.GetAssemblyDefinition().HashAlgorithm switch
        {
            AssemblyHashAlgorithm.MD5 => HashAlgorithmName.MD5,
            // The CLI default for multi-module assembly file hashes is SHA1.
            AssemblyHashAlgorithm.None or AssemblyHashAlgorithm.Sha1 => HashAlgorithmName.SHA1,
            AssemblyHashAlgorithm.Sha256 => HashAlgorithmName.SHA256,
            AssemblyHashAlgorithm.Sha384 => HashAlgorithmName.SHA384,
            AssemblyHashAlgorithm.Sha512 => HashAlgorithmName.SHA512,
            _ => default
        };
        if (algorithm.Name is null) return false;
        using var hash = IncrementalHash.CreateHash(algorithm);
        hash.AppendData(image);
        var actual = hash.GetHashAndReset();
        ct.ThrowIfCancellationRequested();
        return actual.AsSpan().SequenceEqual(manifest.GetBlobContent(files[0].HashValue).AsSpan());
    }
}
