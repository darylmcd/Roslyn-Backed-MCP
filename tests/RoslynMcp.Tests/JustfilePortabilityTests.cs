namespace RoslynMcp.Tests;

[TestClass]
public sealed class JustfilePortabilityTests
{
    [TestMethod]
    public void CleanAll_IsPlatformSpecificAndIdempotentWhenArtifactsAreAbsent()
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var justfile = File.ReadAllText(Path.Combine(repositoryRoot, "justfile"))
            .ReplaceLineEndings("\n");

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
}
