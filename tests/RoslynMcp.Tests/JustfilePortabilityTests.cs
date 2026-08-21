namespace RoslynMcp.Tests;

[TestClass]
public sealed class JustfilePortabilityTests
{
    [TestMethod]
    public void PowerShellScriptRecipes_UseExplicitPortableInvocation()
    {
        var justfile = LoadJustfile();
        var scriptCommands = justfile
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line =>
                !line.StartsWith('#') &&
                line.Contains("./eng/", StringComparison.Ordinal) &&
                line.Contains(".ps1", StringComparison.Ordinal))
            .ToList();

        Assert.IsNotEmpty(scriptCommands, "Expected the Justfile to contain PowerShell-backed recipes.");
        foreach (var command in scriptCommands)
        {
            StringAssert.StartsWith(command, "pwsh ", $"Recipe must invoke pwsh explicitly: {command}");
            StringAssert.Contains(command, " -NoProfile ", $"Recipe must disable profile loading: {command}");
            StringAssert.Contains(command, " -File ./eng/", $"Recipe must use PowerShell's -File boundary: {command}");
        }
    }

    [TestMethod]
    public void CleanAll_IsPlatformSpecificAndIdempotentWhenArtifactsAreAbsent()
    {
        var justfile = LoadJustfile();

        StringAssert.Contains(
            justfile,
            "[unix]\nclean-all: clean\n    rm -rf artifacts",
            "The Unix recipe should retain rm -rf idempotency.");
        StringAssert.Contains(
            justfile,
            "[windows]\nclean-all: clean\n" +
            "    if (Test-Path -LiteralPath artifacts) { " +
            "Remove-Item -LiteralPath artifacts -Recurse -Force }",
            "The Windows recipe must test existence because Remove-Item exits nonzero for an absent literal path.");
    }

    private static string LoadJustfile()
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(repositoryRoot, "justfile"))
            .ReplaceLineEndings("\n");
    }
}
