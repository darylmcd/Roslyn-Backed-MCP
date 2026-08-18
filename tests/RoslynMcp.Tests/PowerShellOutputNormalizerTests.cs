namespace RoslynMcp.Tests;

[TestClass]
public sealed class PowerShellOutputNormalizerTests
{
    private const char Esc = (char)0x1B;

    [TestMethod]
    public void Normalize_RejoinsGutterWrappedErrorText()
    {
        // Shape captured verbatim from the Linux publish runner, where the console width
        // split "require a major bump" across the formatter's '|' continuation gutter and
        // broke a substring assertion against output that was in fact correct.
        var wrapped =
            $"{Esc}[31;1mWrite-Error: {Esc}[0m/repo/eng/verify-breaking-version-bump.ps1:90{Esc}[0m\n" +
            $"{Esc}[36;1mLine |{Esc}[0m\n" +
            $"     | {Esc}[31;1mRefusing patch bump: pending breaking fragment(s) require a{Esc}[0m\n" +
            $"     | {Esc}[31;1mmajor bump: pending.md{Esc}[0m\n";

        var normalized = PowerShellOutputNormalizer.Normalize(wrapped);

        StringAssert.Contains(normalized, "require a major bump: pending.md");
        Assert.IsFalse(normalized.Contains(Esc), "ANSI styling should be stripped.");
    }

    [TestMethod]
    public void Normalize_PreservesMidLinePipes()
    {
        // Only a newline-anchored gutter is a wrap artifact; a mid-line pipe is content.
        Assert.AreEqual(
            "dotnet test | grep fail",
            PowerShellOutputNormalizer.Normalize("dotnet test | grep fail"));
    }

    [TestMethod]
    public void Normalize_CollapsesWhitespaceRuns()
    {
        Assert.AreEqual("a b c", PowerShellOutputNormalizer.Normalize("  a \t\t b \n\n c  "));
    }
}
