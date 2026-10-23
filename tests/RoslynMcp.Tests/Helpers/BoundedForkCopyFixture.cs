using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests.Helpers;

/// <summary>Declares one explicit owned input tree for low-level production copy-policy controls.</summary>
internal static class BoundedForkCopyFixture
{
    internal static async Task CopyAsync(string source, string destination, CancellationToken ct)
    {
        var layout = new PhysicalGraphLayout(source, source, [source], [], [], new([], [], [source]), [], [], [], [], [], [], [], InstalledPackageAuthority.Merge([]), [], [], null);
        var plan = await layout.PlanCopyAsync("bounded-copy", destination, new OwnedTestWorkspacePathPolicy([source, Path.GetDirectoryName(destination)!]), ct);
        Assert.IsFalse(Directory.Exists(destination), "Planning must not create the destination.");
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var owner = PhysicalDirectoryScope.CreateOwnedRoot(destination);
        await plan.CopyAsync(owner, ct);
    }
}
