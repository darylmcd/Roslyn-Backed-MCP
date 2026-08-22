using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
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

    [TestMethod]
    public void FindDocument_UsesCurrentPlatformPathIdentity()
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var upperId = DocumentId.CreateNewId(projectId);
        var lowerId = DocumentId.CreateNewId(projectId);
        var upperPath = Path.GetFullPath(Path.Combine("root", "Feature.cs"));
        var lowerPath = Path.GetFullPath(Path.Combine("root", "feature.cs"));
        var solution = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(projectId, VersionStamp.Create(), "Paths", "Paths", LanguageNames.CSharp))
            .AddDocument(upperId, "Feature.cs", SourceText.From("class Upper { }"), filePath: upperPath)
            .AddDocument(lowerId, "feature.cs", SourceText.From("class Lower { }"), filePath: lowerPath);

        var resolvedUpper = SymbolResolver.FindDocument(solution, upperPath);
        var resolvedLower = SymbolResolver.FindDocument(solution, lowerPath);

        Assert.IsNotNull(resolvedUpper);
        Assert.IsNotNull(resolvedLower);
        Assert.AreEqual(
            OperatingSystem.IsWindows(),
            resolvedUpper.Id == resolvedLower.Id,
            "Windows document lookup is case-insensitive; Unix lookup must preserve case-distinct identities.");
    }
}
