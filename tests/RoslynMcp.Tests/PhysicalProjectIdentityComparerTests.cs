using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class PhysicalProjectIdentityComparerTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PhysicalContextKey_RemainsDistinctUnderItsActualHostComparer(bool output)
    {
        var first = output ? "D:/CONTROL/@/OWNER.DLL" : "D:/CONTROL/@/OWNER.CSPROJ";
        var second = output ? "D:/CONTROL/Z/OWNER.DLL" : "D:/CONTROL/Z/OWNER.CSPROJ";
        const string fixedPath = "D:/CONTROL/FIXED.CSPROJ";
        Assert.IsFalse(FileSystemPath.Comparer.Equals(Path.GetFullPath(first), Path.GetFullPath(second)));
        var before = PhysicalProjectIdentity.Key(output ? fixedPath : first, output ? first : null, static path => path);
        var after = PhysicalProjectIdentity.Key(output ? fixedPath : second, output ? second : null, static path => path);
        Assert.IsFalse(FileSystemPath.Comparer.Equals(before, after),
            "Distinct normalized native paths must stay distinct under the comparer used by their consumer maps.");
    }
}
