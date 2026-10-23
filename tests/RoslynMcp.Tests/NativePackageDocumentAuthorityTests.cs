using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativePackageDocumentAuthorityTests
{
    [TestMethod]
    [DataRow("exact", true)]
    [DataRow("descendant", false)]
    [DataRow("authored-overlay", false)]
    [DataRow("unverified", false)]
    public void PackageDocumentReadAuthority_RequiresExactFileAndPreservesAuthoredPrecedence(string shape, bool allowed)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(NativePackageDocumentAuthorityTests));
        var file = Path.Combine(root, "cache", "Package.cs");
        var path = shape == "descendant" ? Path.Combine(file, "Child.cs")
            : shape == "unverified" ? Path.Combine(root, "cache", "Other.cs") : file;
        var authored = shape == "authored-overlay" ? new[] { root } : new[] { Path.Combine(root, "project") };
        Assert.AreEqual(allowed, NativeCompilerContextCertificate.IsVerifiedPackageDocument(path,
            new HashSet<string>(FileSystemPath.Comparer) { file }, authored));
    }
}
