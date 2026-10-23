using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Authorized analyzer bytes and directory membership; synchronous loaders never read the source.</summary>
internal sealed class AnalyzerInputCatalog
{
    internal sealed record Entry(string Path, string? AssemblyIdentity, ImmutableArray<byte> Bytes, string ContentHash)
    {
        public override string ToString() => "AdmittedAnalyzerInput";
    }

    internal ImmutableArray<Entry> Entries { get; }
    private readonly ImmutableArray<PhysicalInputIdentity> _inputs;
    private readonly ImmutableArray<CompilerArgumentDirectory> _directories;
    private AnalyzerInputCatalog(ImmutableArray<Entry> entries, ImmutableArray<PhysicalInputIdentity> inputs,
        ImmutableArray<CompilerArgumentDirectory> directories)
        => (Entries, _inputs, _directories) = (entries, inputs, directories);
    public override string ToString() => $"AnalyzerInputCatalog {{ FileCount = {Entries.Length}, DirectoryCount = {_directories.Length} }}";

    internal ImmutableArray<Entry> DirectoryEntries(string analyzerPath) => Entries.Where(entry =>
        FileSystemPath.Comparer.Equals(System.IO.Path.GetDirectoryName(entry.Path), System.IO.Path.GetDirectoryName(analyzerPath)))
        .OrderBy(entry => entry.Path, FileSystemPath.Comparer).ToImmutableArray();

    internal static string ContentIdentity(IEnumerable<Entry> entries, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            foreach (var entry in entries.OrderBy(entry => entry.Path, FileSystemPath.Comparer))
            {
                NativeIdentityEncoding.WriteString(writer, entry.Path, ct);
                NativeIdentityEncoding.WriteString(writer, entry.ContentHash, ct);
            }
        ct.ThrowIfCancellationRequested();
        var result = Convert.ToHexString(SHA256.HashData(stream.ToArray()));
        ct.ThrowIfCancellationRequested();
        return result;
    }

    internal static async Task<AnalyzerInputCatalog> CaptureAsync(Solution solution, IReadOnlyList<string> verifiedPackageFiles,
        Func<IReadOnlyList<string>, CancellationToken, Task> authorize,
        Func<string, CancellationToken, Task<byte[]?>> read,
        Func<string, CancellationToken, Task<FileContentDigest?>> readDigest, CancellationToken ct)
    {
        var mains = solution.Projects.SelectMany(project => project.AnalyzerReferences)
            .Where(reference => AnalyzerReferenceIsolation.GetFileReference(reference) is not null)
            .Select(reference => System.IO.Path.GetFullPath(reference.FullPath
                ?? throw new PublicInvalidOperationException("An admitted analyzer reference has no physical input identity."))).Distinct(FileSystemPath.Comparer).ToArray();
        var packages = verifiedPackageFiles.ToHashSet(FileSystemPath.Comparer);
        var paths = mains.ToHashSet(FileSystemPath.Comparer);
        var directories = ImmutableArray.CreateBuilder<CompilerArgumentDirectory>();
        foreach (var group in mains.GroupBy(path => System.IO.Path.GetDirectoryName(path)
                     ?? throw new PublicInvalidOperationException("An analyzer input has no physical directory."), FileSystemPath.Comparer))
        {
            ct.ThrowIfCancellationRequested();
            if (group.All(packages.Contains))
            {
                // Installed-package proof grants exact files, never enumeration of its parent.
                paths.UnionWith(verifiedPackageFiles.Where(path => FileSystemPath.Comparer.Equals(System.IO.Path.GetDirectoryName(path), group.Key)
                    && System.IO.Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase)));
            }
            else
            {
                var directory = await CompilerArgumentDirectory.CaptureAsync(group.Key, "*.dll", false, authorize, ct).ConfigureAwait(false);
                directories.Add(directory);
                paths.UnionWith(directory.Matches);
            }
        }
        await authorize(paths.ToArray(), ct).ConfigureAwait(false);
        var entries = ImmutableArray.CreateBuilder<Entry>();
        var identities = ImmutableArray.CreateBuilder<PhysicalInputIdentity>();
        foreach (var path in paths.OrderBy(path => path, FileSystemPath.Comparer))
        {
            ct.ThrowIfCancellationRequested();
            await authorize([path], ct).ConfigureAwait(false);
            WorkspaceManager.EnsurePhysicalPath(path);
            var bytes = await read(path, ct).ConfigureAwait(false);
            if (bytes is null) { identities.Add(new(path, false, [])); continue; }
            var hash = SHA256.HashData(bytes);
            identities.Add(new(path, true, ImmutableArray.CreateRange(hash)));
            entries.Add(new(path, AssemblyIdentity(bytes), ImmutableArray.CreateRange(bytes), Convert.ToHexString(hash)));
        }
        var catalog = new AnalyzerInputCatalog(entries.ToImmutable(), PhysicalInputIdentity.MergeConsistent(identities), directories.ToImmutable());
        await catalog.AssertCurrentAsync(authorize, readDigest, ct).ConfigureAwait(false);
        return catalog;
    }

    internal Task AssertCurrentAsync(Func<IReadOnlyList<string>, CancellationToken, Task> authorize,
        Func<string, CancellationToken, Task<FileContentDigest?>> readDigest, CancellationToken ct)
        => CompilerArgumentParseReceipt.AssertInputsAsync(_inputs, _directories, [], authorize, readDigest, ct);

    internal IReadOnlyList<string> CurrentGrantPaths => _inputs.Select(input => input.FilePath)
        .Concat(_directories.SelectMany(directory => directory.Traversed.Concat(directory.Matches).Prepend(directory.Path)))
        .Distinct(FileSystemPath.Comparer).ToArray();

    internal Task AssertPhysicalCurrentAsync(CancellationToken ct)
        => CompilerArgumentParseReceipt.AssertPhysicalInputsAsync(_inputs, _directories, [],
            ImmutableHashSet<string>.Empty, ct);

    private static string? AssemblyIdentity(byte[] bytes)
    {
        try
        {
            using var pe = new PEReader(new MemoryStream(bytes, writable: false));
            if (pe.HasMetadata)
            {
                var reader = pe.GetMetadataReader();
                if (reader.IsAssembly) return AnalyzerAssemblyBinding.DefinitionIdentity(reader);
            }
        }
        catch (BadImageFormatException) { /* Native DLLs are materialized but cannot satisfy managed assembly requests. */ }
        return null;
    }
}
