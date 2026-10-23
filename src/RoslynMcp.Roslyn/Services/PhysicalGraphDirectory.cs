using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Acquires shared service parents through their captured pinned anchor.</summary>
internal static class PhysicalGraphDirectory
{
    internal static async Task<PhysicalDirectoryScope> OpenParentAsync(string anchorPath,
        IReadOnlyList<string> sharedNames, string leaf, Func<IReadOnlyList<string>, CancellationToken, Task> assertAdmission, CancellationToken ct)
    {
        foreach (var name in sharedNames) PhysicalDirectoryScope.ValidateName(name);
        PhysicalDirectoryScope.ValidateName(leaf);
        var inventory = new List<string> { anchorPath };
        var path = anchorPath;
        foreach (var name in sharedNames) { path = Path.Combine(path, name); inventory.Add(path); }
        inventory.Add(Path.Combine(path, leaf));
        await assertAdmission(inventory, ct).ConfigureAwait(false);
        PhysicalDirectoryScope? anchor = PhysicalDirectoryScope.OpenRoute(anchorPath);
        PhysicalDirectoryScope? issued = null;
        Exception? primary = null;
        try
        {
            await assertAdmission(inventory, ct).ConfigureAwait(false);
            anchor.AssertCurrentPath();
            issued = PhysicalDirectoryScope.OpenOrCreateDescendant(anchor, sharedNames, ct);
            var releasing = anchor;
            anchor = null;
            releasing.Dispose();
            issued.AssertCurrentPath();
            if (!FileSystemPath.Comparer.Equals(issued.Path, path))
                throw InvalidOperationErrors.Internal("A service graph parent differs from its reviewed coordinate.");
            var result = issued;
            issued = null;
            return result;
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Shared graph parent release failed.", primary, issued, anchor);
        }
    }
}
