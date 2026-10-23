using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ImmutablePathProvenanceTests
{
    [TestMethod]
    public void ExactPackageFile_DoesNotAuthorizeDescendant()
    {
        var file = Path.Combine(TestTempRoot.Current, "package-membership", "approved.dll");
        var provenance = new ImmutablePathProvenance([], [file], []);
        Assert.IsTrue(provenance.IsImmutable(file));
        Assert.IsFalse(provenance.IsImmutable(Path.Combine(file, "unapproved.dll")),
            "An admitted package file is exact membership, never an immutable directory root.");
    }

    [TestMethod]
    public void SdkDirectory_ExcludesAuthoredDescendantsAndPrefixSiblings()
    {
        var sdk = Path.Combine(TestTempRoot.Current, "sdk-provenance");
        var authored = Path.Combine(sdk, "authored");
        var package = Path.Combine(authored, "packages", "approved.dll");
        var provenance = new ImmutablePathProvenance([sdk], [package], [authored]);

        Assert.IsTrue(provenance.IsImmutable(Path.Combine(sdk, "tools", "sdk.dll")));
        Assert.IsFalse(provenance.IsImmutable(authored));
        Assert.IsFalse(provenance.IsImmutable(Path.Combine(authored, "Source.cs")));
        Assert.IsFalse(provenance.IsImmutable(Path.Combine(sdk + "-sibling", "tool.dll")));
        Assert.IsTrue(provenance.IsImmutable(package), "Exact verified package membership remains independent of SDK exclusions.");
        Assert.IsFalse(provenance.IsImmutable(Path.Combine(package, "unapproved.dll")));
    }

    [TestMethod]
    public void Provenance_SnapshotsCallerCollections()
    {
        var root = Path.Combine(TestTempRoot.Current, "provenance-snapshot");
        var first = Path.Combine(root, "first.dll");
        var second = Path.Combine(root, "second.dll");
        var files = new List<string> { first };
        var provenance = new ImmutablePathProvenance([], files, []);
        files.Clear();
        files.Add(second);

        Assert.IsTrue(provenance.IsImmutable(first));
        Assert.IsFalse(provenance.IsImmutable(second));
    }

    [TestMethod]
    public void LayoutWithReplacementInputs_RebuildsMembershipAndParents()
    {
        var root = Path.Combine(TestTempRoot.Current, "layout-membership");
        var destination = Path.Combine(TestTempRoot.Current, "layout-membership-private");
        var first = Path.Combine(root, "first", "Input.cs");
        var second = Path.Combine(root, "second", "Input.cs");
        var inputs = new List<string> { first };
        var original = new PhysicalGraphLayout(root, root, [], [], [], new([], [], []),
            [], [], inputs, [], [], [], [], InstalledPackageAuthority.Merge([]), [], [], null);
        var replacement = original with { InputFiles = new[] { second } };
        inputs.Clear();

        Assert.AreEqual(original.MapTo(destination, first), original.MapInputPath(destination, first));
        Assert.AreEqual(first, replacement.MapInputPath(destination, first));
        Assert.AreEqual(Path.GetDirectoryName(first), replacement.MapInputPath(destination, Path.GetDirectoryName(first)!));
        Assert.AreEqual(replacement.MapTo(destination, second), replacement.MapInputPath(destination, second));
        Assert.AreEqual(replacement.MapTo(destination, Path.GetDirectoryName(second)!),
            replacement.MapInputPath(destination, Path.GetDirectoryName(second)!));
    }

    [TestMethod]
    public void LayoutWithReplacementReplay_RebuildsImmutableOverrides()
    {
        var root = Path.Combine(TestTempRoot.Current, "layout-replay-membership");
        var first = Path.Combine(root, "first.cs");
        var second = Path.Combine(root, "second.cs");
        var replay = new List<string> { first };
        var original = new PhysicalGraphLayout(root, root, [], [], [], new([], [first, second], []),
            [], [], [first, second], [], [], [], [], InstalledPackageAuthority.Merge([]), [], replay, null);
        var replacement = original with { ContentReplayFiles = new[] { second } };
        replay.Clear();

        Assert.IsTrue(original.IsContentReplayFile(first));
        Assert.IsTrue(original.IsImmutablePath(first));
        Assert.IsFalse(original.IsImmutablePath(second));
        Assert.IsFalse(replacement.IsContentReplayFile(first));
        Assert.IsFalse(replacement.IsImmutablePath(first));
        Assert.IsTrue(replacement.IsContentReplayFile(second));
        Assert.IsTrue(replacement.IsImmutablePath(second));
    }
}
