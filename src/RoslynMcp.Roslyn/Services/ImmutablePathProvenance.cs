using System.Collections.Immutable;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Indexes immutable provenance membership, never permissions or current file bytes.</summary>
internal sealed class ImmutablePathProvenance
{
    private sealed record DirectoryRule(string Root, string Prefix, ImmutableArray<string> Exclusions);
    private readonly ImmutableArray<DirectoryRule> _directories;
    internal ImmutableArray<string> DirectoryRoots { get; }
    internal ImmutableHashSet<string> ExactFiles { get; }
    internal ImmutableArray<string> AuthoredRoots { get; }

    internal ImmutablePathProvenance(IEnumerable<string> directoryRoots, IEnumerable<string> exactFiles,
        IEnumerable<string> authoredRoots)
    {
        DirectoryRoots = directoryRoots.Select(Canonical).Distinct(FileSystemPath.Comparer).ToImmutableArray();
        ExactFiles = exactFiles.Select(Canonical).ToImmutableHashSet(FileSystemPath.Comparer);
        AuthoredRoots = authoredRoots.Select(Canonical).Distinct(FileSystemPath.Comparer).ToImmutableArray();
        _directories = DirectoryRoots.Select(root => new DirectoryRule(root, Prefix(root),
            AuthoredRoots.Where(authored => Contains(root, Prefix(root), authored)).ToImmutableArray())).ToImmutableArray();
    }

    internal bool IsImmutable(string path)
    {
        var canonical = Canonical(path);
        if (ExactFiles.Contains(canonical)) return true;
        return _directories.Any(directory => Contains(directory.Root, directory.Prefix, canonical)
            && !directory.Exclusions.Any(authored => Contains(authored, Prefix(authored), canonical)));
    }

    internal static string Canonical(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    private static string Prefix(string root) => Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
    private static bool Contains(string root, string prefix, string path) => FileSystemPath.Comparer.Equals(root, path)
        || path.StartsWith(prefix, FileSystemPath.Comparison);
}

/// <summary>Snapshots exact layout membership and parent coordinates together for record updates.</summary>
internal sealed class PhysicalPathMembership
{
    internal ImmutableArray<string> Files { get; }
    internal ImmutableHashSet<string> Paths { get; }
    internal ImmutableHashSet<string> Parents { get; }

    internal PhysicalPathMembership(IEnumerable<string> paths)
    {
        Files = paths.ToImmutableArray();
        Paths = Files.ToImmutableHashSet(FileSystemPath.Comparer);
        Parents = Files.Select(Path.GetDirectoryName).OfType<string>().ToImmutableHashSet(FileSystemPath.Comparer);
    }
}
