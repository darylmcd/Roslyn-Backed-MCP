using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>One operation's held administrative parent and exact transition coordinates.</summary>
internal sealed class PhysicalSourceCustody(
    PhysicalDirectoryScope sourceParent,
    PhysicalDirectoryScope parent,
    string leafName,
    Action assertOperationOwned,
    Action assertAdmissionOwned,
    Func<IReadOnlyList<string>, CancellationToken, Task> authorizeSource) : IDisposable
{
    private bool _disposed;
    private bool _leafAdmitted;
    private PhysicalDirectoryScope? _leaf;
    internal PhysicalDirectoryScope SourceParent { get; } = sourceParent;
    internal PhysicalDirectoryScope Parent { get; } = parent;
    internal string LeafName { get; } = ValidateLeaf(leafName);
    internal string Path => System.IO.Path.Combine(Parent.Path, LeafName);

    private static string ValidateLeaf(string value)
    {
        PhysicalDirectoryScope.ValidateName(value);
        return value;
    }

    internal void AssertCurrent()
    {
        if (_disposed) throw InvalidOperationErrors.Internal("The physical source custody authority was released.");
        assertOperationOwned();
        Parent.AssertCurrentPath();
        SourceParent.AssertCurrentPath();
        _leaf?.AssertCurrentPath();
    }

    internal void BindLeaf(PhysicalDirectoryScope source, PhysicalDirectoryScope leaf)
    {
        AssertCurrent();
        if (_leafAdmitted || !ReferenceEquals(source, SourceParent)
            || !FileSystemPath.Comparer.Equals(leaf.Path, Path))
            throw new IOException("Source custody requires its exact initial source parent and issued leaf.");
        using var current = Parent.OpenChild(LeafName);
        if (current is null || !leaf.SameIdentity(current) || leaf.Entries().Count != 0)
            throw new IOException("The issued source custody leaf changed before empty-directory admission.");
        leaf.AssertCurrentPath();
        AssertCurrent();
        _leaf = leaf;
        _leafAdmitted = true;
    }

    internal void MarkLeafRemoved(PhysicalDirectoryScope leaf)
    {
        if (_disposed || !_leafAdmitted || !ReferenceEquals(_leaf, leaf))
            throw InvalidOperationErrors.Internal("A different custody leaf cannot complete this receipt.");
        // Called only after object-bound removal succeeds. The removed leaf no longer
        // has a published route; retained/failed removals keep their identity bound.
        _leaf = null;
    }

    internal async Task AuthorizeAsync(IReadOnlyList<string> paths, CancellationToken ct)
    {
        AssertCurrent();
        var sourcePaths = paths.Where(path => !IsAdministrativeCoordinate(path)).Distinct(FileSystemPath.Comparer).ToArray();
        await authorizeSource(sourcePaths, ct).ConfigureAwait(false);
        AssertCurrent();
    }

    internal void AssertAdmissionCurrent()
    {
        AssertCurrent();
        assertAdmissionOwned();
    }

    private bool IsAdministrativeCoordinate(string path) => FileSystemPath.Comparer.Equals(path, Parent.Path)
        || FileSystemPath.Comparer.Equals(path, Path)
        || FileSystemPath.Comparer.Equals(path, System.IO.Path.Combine(Path, "captured"))
        || FileSystemPath.Comparer.Equals(path, System.IO.Path.Combine(Path, "prepared"));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Parent.Dispose();
    }
}
