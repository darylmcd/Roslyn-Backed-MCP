using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class FileSystemPathTests
{
    [TestMethod]
    public void Comparer_MatchesCurrentPlatformPathSemantics()
    {
        var upperPath = Path.Combine("root", "Feature.cs");
        var lowerPath = Path.Combine("root", "feature.cs");
        var paths = new Dictionary<string, int>(FileSystemPath.Comparer)
        {
            [upperPath] = 1,
            [lowerPath] = 2
        };

        Assert.AreEqual(OperatingSystem.IsWindows(), FileSystemPath.Comparer.Equals(upperPath, lowerPath));
        Assert.AreEqual(OperatingSystem.IsWindows() ? 1 : 2, paths.Count);
    }
}
